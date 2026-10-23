"""シャッター動画(透明なし・穴の部分が黒)を、黒を透明にした 1:1 の連番 PNG に変換する。

    .venv\\Scripts\\python tools\\convert_shutter.py <閉じる動画> <開く動画> [--out ..\\PoseSync\\Assets\\ThirdParty\\Shutter]

- 中央を正方形に切り出して size×size に縮小する(画面が 1:1 のため。絞りは中央にあるので閉じた時は全面を覆う)。
- 明るさが key_low 以下を透明、key_high 以上を不透明にし、その間はなめらかにつなぐ(H.264 の黒のノイズ対策)。
  素材の羽根は最も暗い所でも明るさ 28 程度なので、既定の 8〜24 で羽根に穴は空かない。
- 出力: <out>/Close/close_000.png …、<out>/Open/open_000.png …
素材は再配布禁止のため、出力先は GitHub に上げない ThirdParty 以下にする。
"""
import argparse
from pathlib import Path

import cv2
import numpy as np

ROOT = Path(__file__).resolve().parents[2]


def convert(src: Path, out_dir: Path, prefix: str, size: int, key_low: float, key_high: float) -> int:
    cap = cv2.VideoCapture(str(src))
    if not cap.isOpened():
        raise SystemExit(f"動画を開けません: {src}")
    out_dir.mkdir(parents=True, exist_ok=True)
    for old in out_dir.glob(f"{prefix}_*.png"):
        old.unlink()
    n = 0
    while True:
        ok, frame = cap.read()
        if not ok:
            break
        h, w = frame.shape[:2]
        s = min(h, w)
        square = frame[(h - s) // 2:(h + s) // 2, (w - s) // 2:(w + s) // 2]
        square = cv2.resize(square, (size, size), interpolation=cv2.INTER_AREA)
        luma = cv2.cvtColor(square, cv2.COLOR_BGR2GRAY).astype(np.float32)
        t = np.clip((luma - key_low) / (key_high - key_low), 0.0, 1.0)
        alpha = t * t * (3 - 2 * t)
        bgra = cv2.cvtColor(square, cv2.COLOR_BGR2BGRA)
        bgra[..., 3] = (alpha * 255).astype(np.uint8)
        cv2.imwrite(str(out_dir / f"{prefix}_{n:03d}.png"), bgra)
        n += 1
    return n


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("close", type=Path)
    ap.add_argument("open", type=Path)
    ap.add_argument("--out", type=Path, default=ROOT / "PoseSync" / "Assets" / "ThirdParty" / "Shutter")
    ap.add_argument("--size", type=int, default=1080)
    ap.add_argument("--key-low", type=float, default=8)
    ap.add_argument("--key-high", type=float, default=24)
    args = ap.parse_args()
    for src, sub, prefix in ((args.close, "Close", "close"), (args.open, "Open", "open")):
        n = convert(src, args.out / sub, prefix, args.size, args.key_low, args.key_high)
        print(f"{src.name} -> {args.out / sub} ({n} 枚)")


if __name__ == "__main__":
    main()
