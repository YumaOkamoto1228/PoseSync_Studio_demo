"""data/samples/*.jsonl を集計する(§10 の検証)。

記録済みの骨格から「現在の」判定ロジックで再計算するので、閾値やテンプレートを変えた効果をすぐ比べられる。

    .venv\\Scripts\\python tools\\analyze_samples.py [--threshold 30] [--note 自分] [--out ..\\docs\\validation.md]
"""
import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from posesync.features import Skeleton2D, compute_features  # noqa: E402
from posesync.matcher import FALLBACK_ID, MatchConfig, PoseMatcher  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


def load(note_filter: str):
    rows = []
    for f in sorted((ROOT / "data" / "samples").glob("*.jsonl")):
        for line in f.read_text(encoding="utf-8").splitlines():
            if not line.strip():
                continue
            r = json.loads(line)
            if note_filter and note_filter not in r.get("note", ""):
                continue
            rows.append(r)
    return rows


def evaluate(matcher: PoseMatcher, rows):
    out = []
    for r in rows:
        sk = r.get("skeleton")
        if sk is None:
            res = matcher.classify(None)
        else:
            # 古い記録は関節ごとの visibility が無いので肩は見えていた扱い(visibility=None → 1.0)
            s = Skeleton2D(**{k: np.array(v) for k, v in sk.items()},
                           min_visibility=r.get("min_visibility", 1.0), visibility=r.get("visibility"))
            res = matcher.classify(compute_features(s), s.shoulder_visibility)
        out.append((r, res))
    return out


def accuracy(results):
    return sum(r["expected"] == res.pose_id for r, res in results) / max(len(results), 1)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--threshold", type=float, default=None)
    ap.add_argument("--min-vis", type=float, default=None, help="visibility によるフォールバックの基準")
    ap.add_argument("--note", default="")
    ap.add_argument("--out", type=Path, default=ROOT / "docs" / "validation.md")
    args = ap.parse_args()

    cfg = MatchConfig()
    if args.threshold is not None:
        cfg.threshold_deg = args.threshold
    if args.min_vis is not None:
        cfg.min_visibility = args.min_vis
    matcher = PoseMatcher(config=cfg)
    rows = load(args.note)
    results = evaluate(matcher, rows)
    ids = sorted(matcher.labels)  # -1 を含む
    name = matcher.labels

    lines = [f"# 判定の検証結果", "", f"サンプル数: {len(rows)} / 閾値: {cfg.threshold_deg} / visibility 基準: {cfg.min_visibility} / フィルタ: '{args.note}'", "",
             f"**全体の正解率: {accuracy(results) * 100:.1f}%**", ""]

    # 混同行列
    conf = Counter((r["expected"], res.pose_id) for r, res in results)
    lines += ["## 混同行列(行=とったポーズ、列=判定)", "",
              "| | " + " | ".join(name[i] for i in ids) + " | 正解率 |", "|---" * (len(ids) + 2) + "|"]
    for e in ids:
        total = sum(conf[(e, p)] for p in ids)
        if total == 0:
            continue
        lines.append(f"| {name[e]} | " + " | ".join(str(conf[(e, p)]) for p in ids) + f" | {conf[(e, e)] / total * 100:.0f}% ({conf[(e, e)]}/{total}) |")

    # 距離の分布: 正解ポーズまでの距離 / どれでもない時の最小距離
    own = defaultdict(list)
    none_min = []
    reasons = Counter()
    for r, res in results:
        if res.reason:
            reasons[(name[r["expected"]], res.reason)] += 1
        if not res.distances:
            continue
        if r["expected"] == FALLBACK_ID:
            none_min.append(min(res.distances.values()))
        else:
            own[r["expected"]].append(res.distances[r["expected"]])
    lines += ["", "## 距離の分布", "", "| | n | 最小 | 中央値 | 最大 |", "|---|---|---|---|---|"]
    for pid, ds in sorted(own.items()):
        lines.append(f"| {name[pid]}(正解テンプレまで) | {len(ds)} | {min(ds):.1f} | {np.median(ds):.1f} | {max(ds):.1f} |")
    if none_min:
        lines.append(f"| どれでもない(最も近いテンプレまで) | {len(none_min)} | {min(none_min):.1f} | {np.median(none_min):.1f} | {max(none_min):.1f} |")
    if reasons:
        lines += ["", "フォールバック理由: " + ", ".join(f"{k[0]}→{k[1]}×{v}" for k, v in sorted(reasons.items()))]

    # 閾値スイープ
    lines += ["", "## 閾値と正解率", "", "| 閾値 | 正解率 | ポーズの正解率 | どれでもないの正解率 |", "|---|---|---|---|"]
    for th in [15, 20, 25, 30, 35, 40, 45, 50, 60]:
        c = MatchConfig(**{**cfg.__dict__, "threshold_deg": th})
        res_th = evaluate(PoseMatcher(config=c), rows)
        pose = [x for x in res_th if x[0]["expected"] != FALLBACK_ID]
        none = [x for x in res_th if x[0]["expected"] == FALLBACK_ID]
        lines.append(f"| {th} | {accuracy(res_th) * 100:.1f}% | {accuracy(pose) * 100:.1f}% | {accuracy(none) * 100:.1f}% |")

    # 誤判定の一覧
    wrong = [(r, res) for r, res in results if r["expected"] != res.pose_id]
    if wrong:
        lines += ["", "## 誤判定", "", "| 時刻 | とったポーズ | 判定 | 距離 | 理由 | 各ポーズまでの距離 |", "|---|---|---|---|---|---|"]
        for r, res in wrong:
            ds = ", ".join(f"{name[k]}={v:.0f}" for k, v in sorted(res.distances.items()))
            lines.append(f"| {r['time']} | {name[r['expected']]} | {name[res.pose_id]} | {res.distance:.1f} | {res.reason} | {ds} |")

    text = "\n".join(lines) + "\n"
    args.out.write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
