"""簡易ユーザーテストの体験ログ(data/sessions/*.jsonl)を集計する(§10)。

    .venv\\Scripts\\python tools\\analyze_sessions.py [--date 20261023] [--participant P] [--since 2026-10-23T00:00] [--until 2026-10-23T02:00] [--out ..\\docs\\user_test.md]

集計する内容
- 意図したポーズが選ばれた割合(記録係が結果画面で 1〜4/0 を押した回のみ)と混同行列
- 各ステップの所要時間(立ち位置・見本で長いほど迷っている目安)と、途中でやめた場所(離脱点)
- おまかせになった割合と理由、判定の遅延、切り抜きの成功率
- 判定ログ(data/judge_log)の骨格を使い、閾値を変えた場合の正解率を再計算
"""
import argparse
import json
import sys
from collections import Counter, defaultdict
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from posesync.features import Skeleton2D, compute_features  # noqa: E402
from posesync.matcher import MatchConfig, PoseMatcher  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
UNSET = -99
STEP_LABELS = {"Standing": "①立ち位置", "PoseGuide": "②見本", "Countdown": "③カウントダウン",
               "Judging": "判定・合成", "Result": "結果画面"}


def load_sessions(date: str, participant: str, since: str = "", until: str = "") -> list[dict]:
    rows = []
    for f in sorted((ROOT / "data" / "sessions").glob("*.jsonl")):
        if date and date not in f.name:
            continue
        if "_probe" in f.name and "_probe" not in date:
            continue  # Unity の自動テスト(Run Flow Probe)の記録は、明示しない限り集計しない
        for line in f.read_text(encoding="utf-8").splitlines():
            if line.strip():
                r = json.loads(line)
                started = r.get("started_at", "")
                if since and started < since:
                    continue
                if until and started >= until:
                    continue
                if not participant or r.get("participant", "").startswith(participant):
                    rows.append(r)
    return rows


def rejudge(matcher: PoseMatcher, log_id: str):
    """判定ログの骨格から、現在のロジック・設定で判定し直す。ログが無ければ None。"""
    path = ROOT / "data" / "judge_log" / log_id / "result.json"
    if not log_id or not path.exists():
        return None
    data = json.loads(path.read_text(encoding="utf-8"))
    results = []
    for sk in data.get("skeletons", []):
        if sk is None:
            results.append(matcher.classify(None))
            continue
        s = Skeleton2D(**{k: np.array(v) for k, v in sk["points"].items()},
                       min_visibility=sk.get("min_visibility", 1.0), visibility=sk.get("visibility"))
        results.append(matcher.classify(compute_features(s), s.shoulder_visibility))
    return matcher.combine(results)


def pct(n: int, d: int) -> str:
    return f"{n / d * 100:.0f}% ({n}/{d})" if d else "-"


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--date", default="", help="例: 20261023(ファイル名に含まれる日付で絞る)")
    ap.add_argument("--participant", default="", help="参加者 ID の先頭で絞る")
    ap.add_argument("--since", default="", help="この時刻以降に始めた体験だけ(例: 2026-10-23T00:00)")
    ap.add_argument("--until", default="", help="この時刻より前に始めた体験だけ(例: 2026-10-23T02:00)")
    ap.add_argument("--out", type=Path, default=ROOT / "docs" / "user_test.md")
    args = ap.parse_args()

    rows = load_sessions(args.date, args.participant, args.since, args.until)
    if not rows:
        print("体験ログがありません(data/sessions/*.jsonl)")
        return
    matcher = PoseMatcher()
    name = matcher.labels

    completed = [r for r in rows if r.get("completed")]
    aborted = [r for r in rows if r.get("aborted") and not r.get("completed")]
    labeled = [r for r in completed if r.get("intended", UNSET) != UNSET]
    participants = sorted({r.get("participant", "") for r in rows})
    hits = sum(r["intended"] == r["result_id"] for r in labeled)

    lines = ["# 簡易ユーザーテストの結果", "",
             f"参加者 {len(participants)} 名({', '.join(participants)})/ 体験 {len(rows)} 回(撮影まで完了 {len(completed)} 回)", "",
             "## まとめ(ES・企画書用)", "",
             f"- **意図したポーズが選ばれた割合: {pct(hits, len(labeled))}**(記録係が意図を記録した回のみ)",
             f"- 撮影まで完了した割合: {pct(len(completed), len(rows))}",
             f"- おまかせポーズになった割合: {pct(sum(r.get('fallback', False) for r in completed), len(completed))}",
             f"- 切り抜きの成功率: {pct(sum(r.get('cutout_ok', False) for r in completed), len(completed))}"]
    lat = [r["latency_ms"] for r in completed if r.get("latency_ms")]
    if lat:
        lines.append(f"- 判定の待ち時間(往復): 中央値 {np.median(lat):.0f}ms / 最大 {max(lat):.0f}ms")

    # 混同行列
    if labeled:
        ids = sorted(name)
        conf = Counter((r["intended"], r["result_id"]) for r in labeled)
        lines += ["", "## 意図 × 判定(行=とったつもりのポーズ、列=判定結果)", "",
                  "| | " + " | ".join(name[i] for i in ids) + " | 正解率 |", "|---" * (len(ids) + 2) + "|"]
        for e in ids:
            total = sum(conf[(e, p)] for p in ids)
            if total:
                lines.append(f"| {name[e]} | " + " | ".join(str(conf[(e, p)]) for p in ids) + f" | {pct(conf[(e, e)], total)} |")

    # ステップの所要時間と離脱点
    durations = defaultdict(list)
    for r in rows:
        for s in r.get("steps", []):
            durations[s["step"]].append(s["seconds"])
    lines += ["", "## ステップごとの所要時間(秒)", "",
              "立ち位置・見本の時間が長い/ばらつく箇所は、ガイドで迷っている可能性がある。", "",
              "| ステップ | 回数 | 中央値 | 最大 |", "|---|---|---|---|"]
    for step, label in STEP_LABELS.items():
        ds = durations.get(step)
        if ds:
            lines.append(f"| {label} | {len(ds)} | {np.median(ds):.1f} | {max(ds):.1f} |")
    lines += ["", "## 途中でやめた場所(離脱点)", ""]
    if aborted:
        for step, n in Counter(r.get("aborted_at", "?") for r in aborted).most_common():
            lines.append(f"- {STEP_LABELS.get(step, step)}: {n} 回")
    else:
        lines.append("- なし")

    reasons = Counter(r.get("reason") or "-" for r in completed if r.get("fallback"))
    if reasons:
        lines += ["", "おまかせになった理由: " + ", ".join(f"{k} ×{v}" for k, v in reasons.items())]

    # 閾値を変えた場合(判定ログの骨格から再計算)
    rejudgeable = [r for r in labeled if r.get("log_id")]
    if rejudgeable:
        lines += ["", "## 閾値を変えた場合の正解率(判定ログの骨格から再計算)", "",
                  f"対象 {len(rejudgeable)} 回。現在の閾値は {matcher.config.threshold_deg}。", "",
                  "| 閾値 | 正解率 |", "|---|---|"]
        for th in [25, 30, 35, 40, 45, 50]:
            m = PoseMatcher(config=MatchConfig(**{**matcher.config.__dict__, "threshold_deg": th}))
            ok = n = 0
            for r in rejudgeable:
                res = rejudge(m, r["log_id"])
                if res is not None:
                    n += 1
                    ok += res.pose_id == r["intended"]
            lines.append(f"| {th} | {pct(ok, n)} |")

    # 外れた回の一覧
    misses = [r for r in labeled if r["intended"] != r["result_id"]]
    if misses:
        lines += ["", "## 意図と違った回", "", "| 参加者 | 時刻 | 意図 | 判定 | 距離 | 理由 | フレームごと | メモ |", "|---|---|---|---|---|---|---|---|"]
        for r in misses:
            lines.append(f"| {r.get('participant')} | {r.get('started_at')} | {r.get('intended_key')} | {r.get('result_key')} | "
                         f"{r.get('distance', 0):.1f} | {r.get('reason') or ''} | {','.join(r.get('votes') or [])} | {r.get('note', '')} |")

    text = "\n".join(lines) + "\n"
    args.out.write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
