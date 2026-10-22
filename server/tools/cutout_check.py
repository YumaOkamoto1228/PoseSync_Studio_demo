"""Webカメラの1枚(Unity と同じ 1:1・鏡像)を切り抜いて、結果を確認用の画像に保存する。

    .venv\\Scripts\\python tools\\cutout_check.py [--out ..\\docs\\cutout_check.png]

左: 元画像 / 中: アルファ / 右: 市松模様の上に切り抜き結果
"""
import argparse
import sys
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
sys.path.insert(0, str(Path(__file__).resolve().parent))

from live_judge import open_camera, square_mirror  # noqa: E402
from posesync.cutout import PoseMaskSegmenter  # noqa: E402
from posesync.estimator import PoseEstimator  # noqa: E402


def checker(h: int, w: int, s: int = 32) -> np.ndarray:
    yy, xx = np.mgrid[0:h, 0:w]
    c = (((yy // s) + (xx // s)) % 2) * 60 + 150
    return np.repeat(c[..., None], 3, axis=2).astype(np.uint8)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", type=Path, default=Path(__file__).resolve().parents[2] / "docs" / "cutout_check.png")
    ap.add_argument("--warmup", type=int, default=15, help="カメラの露出が落ち着くまで捨てるフレーム数")
    args = ap.parse_args()

    cap = open_camera(0)
    for _ in range(args.warmup):
        cap.read()
    ok, frame = cap.read()
    cap.release()
    img = cv2.resize(square_mirror(frame), (1080, 1080))

    with PoseEstimator() as est:
        t = time.perf_counter()
        bgra = PoseMaskSegmenter(est).cutout(img)
        ms = (time.perf_counter() - t) * 1000
    if bgra is None:
        print("人物が見つかりませんでした(カメラの前に立って再実行)")
        return
    a = bgra[..., 3:4].astype(np.float32) / 255
    over = (img * a + checker(*img.shape[:2]) * (1 - a)).astype(np.uint8)
    alpha_vis = cv2.cvtColor(bgra[..., 3], cv2.COLOR_GRAY2BGR)
    sheet = np.hstack([img, alpha_vis, over])
    cv2.imwrite(str(args.out), cv2.resize(sheet, None, fx=0.5, fy=0.5))
    print(f"cutout {ms:.0f}ms -> {args.out}")


if __name__ == "__main__":
    main()
