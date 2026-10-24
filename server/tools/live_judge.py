"""Webカメラでリアルタイムにポーズ判定を表示し、検証用サンプルを記録する(§10)。

    .venv\\Scripts\\python tools\\live_judge.py

画面は Unity と同じ「中央 1:1 クロップ + 鏡像」。
キー操作:
    1〜4 : 「このポーズをとっている」として現在のフレームを記録(1=ハート 2=大きなハート 3=両手ピース 4=ピース)
    0    : 「どのポーズでもない」として記録
    q    : 終了
記録先: data/samples/<日付>.jsonl(閾値調整・正解率の集計に使う)

※ Unity がカメラを使っている間は開けない。
"""
import argparse
import datetime as dt
import json
import sys
import time
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

from posesync.estimator import PoseEstimator  # noqa: E402
from posesync.features import compute_features  # noqa: E402
from posesync.matcher import FALLBACK_ID, PoseMatcher  # noqa: E402

SAMPLES = Path(__file__).resolve().parents[2] / "data" / "samples"


def open_camera(index: int) -> cv2.VideoCapture:
    for backend in (cv2.CAP_DSHOW, cv2.CAP_MSMF):
        cap = cv2.VideoCapture(index, backend)
        if not cap.isOpened():
            continue
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, 1920)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, 1080)
        if cap.read()[0]:
            return cap
        cap.release()
    raise RuntimeError("カメラを開けません(Unity など他のアプリが使用中でないか確認)")


def square_mirror(frame: np.ndarray) -> np.ndarray:
    h, w = frame.shape[:2]
    s = min(h, w)
    x0, y0 = (w - s) // 2, (h - s) // 2
    return cv2.flip(frame[y0:y0 + s, x0:x0 + s], 1)


def draw(img: np.ndarray, det, result, matcher: PoseMatcher, threshold: float,
         footer: str = "1-4: record pose  0: record none  q: quit") -> None:
    s = img.shape[0]
    if det is not None:
        pts = {k: (int(v[0]), int(-v[1])) for k, v in det.skeleton.points().items()}
        for a, b in [("sl", "el"), ("el", "wl"), ("sr", "er"), ("er", "wr"), ("sl", "sr")]:
            cv2.line(img, pts[a], pts[b], (0, 255, 0), 4)
        for k in ("wl", "wr"):
            cv2.circle(img, pts[k], 10, (0, 220, 255), -1)

    label = result.key  # cv2.putText は日本語を描けないので key を表示
    color = (80, 80, 255) if result.fallback else (80, 255, 80)
    cv2.putText(img, f"{label}  d={result.distance:.1f} {result.reason}", (20, 60),
                cv2.FONT_HERSHEY_SIMPLEX, 1.6, color, 4)
    # 各ポーズまでの距離バー(短いほど近い、赤線=閾値)
    y = 110
    scale = 4.0
    for pid, d in sorted(result.distances.items()):
        w = int(min(d, 120) * scale)
        cv2.rectangle(img, (20, y), (20 + w, y + 28), (200, 200, 200), -1)
        cv2.putText(img, f"{matcher.labels[pid]} {d:.1f}", (30 + w, y + 24), cv2.FONT_HERSHEY_SIMPLEX, 0.8, (255, 255, 255), 2)
        y += 40
    tx = 20 + int(threshold * scale)
    cv2.line(img, (tx, 105), (tx, y), (0, 0, 255), 2)
    if footer:
        cv2.putText(img, footer, (20, s - 20), cv2.FONT_HERSHEY_SIMPLEX, 0.9, (255, 255, 255), 2)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--note", default="", help="記録に残すメモ(被験者名やグッズの有無など)")
    args = ap.parse_args()

    matcher = PoseMatcher()
    SAMPLES.mkdir(parents=True, exist_ok=True)
    out_path = SAMPLES / f"{dt.date.today():%Y%m%d}.jsonl"
    cap = open_camera(args.camera)
    t0 = time.perf_counter()

    with PoseEstimator(video=True) as est, out_path.open("a", encoding="utf-8") as out:
        while True:
            ok, frame = cap.read()
            if not ok:
                continue
            img = square_mirror(frame)
            det = est.estimate(cv2.cvtColor(img, cv2.COLOR_BGR2RGB), int((time.perf_counter() - t0) * 1000))
            feats = compute_features(det.skeleton) if det else None
            result = matcher.classify(feats, det.skeleton.shoulder_visibility if det else 0.0)

            view = img.copy()
            draw(view, det, result, matcher, matcher.config.threshold_deg)
            cv2.imshow("PoseSync live judge", cv2.resize(view, (900, 900)))

            key = cv2.waitKey(1) & 0xFF
            if key == ord("q"):
                break
            if ord("0") <= key <= ord("4"):
                expected = FALLBACK_ID if key == ord("0") else key - ord("1")
                record = {
                    "time": dt.datetime.now().isoformat(timespec="seconds"),
                    "note": args.note,
                    "expected": expected,
                    "result": result.to_json(),
                    "min_visibility": det.skeleton.min_visibility if det else 0.0,
                    "visibility": det.skeleton.visibility if det else None,
                    "num_persons": det.num_persons if det else 0,
                    "skeleton": {k: v.tolist() for k, v in det.skeleton.points().items()} if det else None,
                }
                out.write(json.dumps(record, ensure_ascii=False) + "\n")
                out.flush()
                hit = "OK" if result.pose_id == expected else "NG"
                print(f"recorded expected={matcher.labels[expected]} got={result.key} d={result.distance:.1f} {hit}")

    cap.release()
    cv2.destroyAllWindows()
    print(f"saved: {out_path}")


if __name__ == "__main__":
    main()
