"""テンプレート間の距離表を作る(§5-6: 近すぎる組を見つける / 閾値の目安を決める)。

    .venv\\Scripts\\python tools\\distance_table.py [--out ..\\docs\\distance_table.md]

行・列はテンプレート(mirror 版を含む)。対角は 0。
閾値は「異なるポーズ間の最小距離」の半分程度が目安(それ以上だと隣のポーズを誤って受け入れやすい)。
"""
import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from posesync.features import compute_features, skeleton_from_template  # noqa: E402
from posesync.matcher import PoseMatcher  # noqa: E402

REST = {"screen_left": {"upper": [-0.2, -0.98, 0], "lower": [-0.1, -0.98, 0.15]},
        "screen_right": {"upper": [0.2, -0.98, 0], "lower": [0.1, -0.98, 0.15]}}


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[2] / "docs" / "distance_table.md")
    args = ap.parse_args()

    m = PoseMatcher()
    names = [f"{t.key}{' (mirror)' if t.mirrored else ''}" for t in m.templates]
    rest = compute_features(skeleton_from_template(REST))
    names_all = names + ["腕を下ろす(参考)"]
    feats = [t.features for t in m.templates] + [rest]

    lines = ["# テンプレート間距離表", "",
             f"単位: 度相当(matcher.py の重み付き距離)。現在の閾値: {m.config.threshold_deg}", "",
             "| | " + " | ".join(names_all) + " |", "|---" * (len(names_all) + 1) + "|"]
    min_between = float("inf")
    pair = ("", "")
    for i, a in enumerate(feats):
        row = []
        for j, b in enumerate(feats):
            d = m.distance(a, b)
            row.append(f"{d:.1f}")
            same = i < len(m.templates) and j < len(m.templates) and m.templates[i].pose_id == m.templates[j].pose_id
            if i != j and not same and d < min_between:
                min_between, pair = d, (names_all[i], names_all[j])
        lines.append(f"| {names_all[i]} | " + " | ".join(row) + " |")
    lines += ["", f"異なるポーズ間の最小距離: **{min_between:.1f}** ({pair[0]} ↔ {pair[1]})",
              f"閾値の目安(最小距離の半分): {min_between / 2:.1f}"]

    text = "\n".join(lines) + "\n"
    args.out.write_text(text, encoding="utf-8")
    print(text)


if __name__ == "__main__":
    main()
