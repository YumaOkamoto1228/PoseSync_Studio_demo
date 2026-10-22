"""撮影時に保存した元の写真(data/photos/raw)で、切り抜き方式を並べて比べる。

    .venv\\Scripts\\python tools\\cutout_compare.py [--last 6] [--out ..\\docs\\cutout_compare.png]

列: 元の写真 / A 姿勢推定のマスクのみ(補強なし)/ B A+脚・手の補強(現在の既定)/
    C B+DeepLab の人物マスク / D B+selfie multiclass のマスク
切り抜き結果は市松模様の上に重ねる(抜けた所が市松模様になる)。1枚あたりの処理時間も出す。
選んだ方式は `app.py --extra-seg deeplab` のように起動時に指定する。
"""
import argparse
import sys
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from posesync.cutout import CutoutConfig, ExtraSegmenter, PoseMaskSegmenter  # noqa: E402
from posesync.estimator import PoseEstimator  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]


def checker(h: int, w: int, s: int = 24) -> np.ndarray:
    yy, xx = np.mgrid[0:h, 0:w]
    c = (((yy // s) + (xx // s)) % 2) * 70 + 140
    return np.repeat(c[..., None], 3, axis=2).astype(np.uint8)


def label(img: np.ndarray, text: str) -> np.ndarray:
    out = img.copy()
    cv2.rectangle(out, (0, 0), (out.shape[1], 34), (0, 0, 0), -1)
    cv2.putText(out, text, (8, 25), cv2.FONT_HERSHEY_SIMPLEX, 0.7, (255, 255, 255), 2)
    return out


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--last", type=int, default=6, help="新しい順に何枚比べるか")
    ap.add_argument("--size", type=int, default=360, help="一覧での1枚の大きさ(px)")
    ap.add_argument("--out", type=Path, default=ROOT / "docs" / "cutout_compare.png")
    args = ap.parse_args()

    photos = sorted((ROOT / "data" / "photos" / "raw").glob("*.jpg"))[-args.last:]
    if not photos:
        print("元の写真がありません。Unity で撮影すると data/photos/raw に保存されます")
        return

    est = PoseEstimator()
    variants = [
        ("A pose only", PoseMaskSegmenter(est, CutoutConfig(leg_prior=False, hand_prior=False))),
        ("B +legs+hands", PoseMaskSegmenter(est, CutoutConfig())),
        ("C +deeplab", PoseMaskSegmenter(est, CutoutConfig(), ExtraSegmenter("deeplab"))),
        ("D +multiclass", PoseMaskSegmenter(est, CutoutConfig(), ExtraSegmenter("multiclass"))),
    ]
    times = {name: [] for name, _ in variants}
    rows = []
    for path in photos:
        img = cv2.imread(str(path))
        bg = checker(*img.shape[:2])
        tiles = [label(cv2.resize(img, (args.size, args.size)), path.stem[-6:])]
        for name, seg in variants:
            t = time.perf_counter()
            alpha = seg.mask(img)
            times[name].append((time.perf_counter() - t) * 1000)
            if alpha is None:
                tile = np.zeros_like(img)
            else:
                a = alpha[..., None]
                tile = (img * a + bg * (1 - a)).astype(np.uint8)
            tiles.append(label(cv2.resize(tile, (args.size, args.size)), name))
        rows.append(np.hstack(tiles))
    cv2.imwrite(str(args.out), np.vstack(rows))
    for name, ts in times.items():
        print(f"{name:15s} {np.median(ts):6.0f} ms/枚")
    print(f"-> {args.out}")


if __name__ == "__main__":
    main()
