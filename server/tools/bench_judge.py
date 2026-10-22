"""起動中の判定サーバーに、Webカメラの数フレームを Unity と同じ形式で送って遅延を測る。

    .venv\\Scripts\\python app.py                       # 別ターミナルで起動しておく
    .venv\\Scripts\\python tools\\bench_judge.py --runs 5

Unity と同じく「中央 1:1 クロップ + 鏡像」した JPEG を frames として送る。
"""
import argparse
import sys
import time
import urllib.request
import uuid
from pathlib import Path

import cv2

sys.path.insert(0, str(Path(__file__).resolve().parent))

from live_judge import open_camera, square_mirror  # noqa: E402


def post_frames(url: str, jpegs: list[bytes]) -> bytes:
    boundary = uuid.uuid4().hex
    body = b""
    for i, data in enumerate(jpegs):
        body += (f"--{boundary}\r\nContent-Disposition: form-data; name=\"frames\"; filename=\"f{i}.jpg\"\r\n"
                 f"Content-Type: image/jpeg\r\n\r\n").encode() + data + b"\r\n"
    body += f"--{boundary}--\r\n".encode()
    req = urllib.request.Request(url, data=body, headers={"Content-Type": f"multipart/form-data; boundary={boundary}"})
    with urllib.request.urlopen(req, timeout=10) as res:
        return res.read()


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--url", default="http://127.0.0.1:8765/judge")
    ap.add_argument("--frames", type=int, default=5)
    ap.add_argument("--size", type=int, default=720)
    ap.add_argument("--runs", type=int, default=3)
    args = ap.parse_args()

    cap = open_camera(0)
    for run in range(args.runs):
        jpegs = []
        for _ in range(args.frames):
            ok, frame = cap.read()
            img = cv2.resize(square_mirror(frame), (args.size, args.size))
            jpegs.append(cv2.imencode(".jpg", img, [cv2.IMWRITE_JPEG_QUALITY, 85])[1].tobytes())
            time.sleep(0.1)
        t = time.perf_counter()
        body = post_frames(args.url, jpegs)
        print(f"run{run}: {(time.perf_counter() - t) * 1000:.0f}ms (送信 {sum(map(len, jpegs)) // 1024}KB) {body.decode()}")
    cap.release()


if __name__ == "__main__":
    main()
