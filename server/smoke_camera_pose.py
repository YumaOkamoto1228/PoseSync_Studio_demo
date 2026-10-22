"""Day0 動作確認: Webカメラ → 鏡像・1:1クロップ → MediaPipe Pose で12関節を描画する。

使い方:
    .venv\\Scripts\\python smoke_camera_pose.py            # ウィンドウ表示 (q で終了)
    .venv\\Scripts\\python smoke_camera_pose.py --headless --frames 30   # 速度だけ確認

※ Unity がカメラを掴んでいる間は開けないので、Unity の Play を止めてから実行する。
"""
import argparse
import time
from pathlib import Path

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

MODEL = Path(__file__).parent / "models" / "pose_landmarker_heavy.task"

# 判定に使う関節 (MediaPipe の番号)。顔と指は使わない。
JOINTS = {
    "shoulder_l": 11, "shoulder_r": 12, "elbow_l": 13, "elbow_r": 14,
    "wrist_l": 15, "wrist_r": 16, "hip_l": 23, "hip_r": 24,
    "knee_l": 25, "knee_r": 26, "ankle_l": 27, "ankle_r": 28,
}
BONES = [(11, 13), (13, 15), (12, 14), (14, 16), (23, 25), (25, 27), (24, 26), (26, 28),
         (11, 12), (23, 24), (11, 23), (12, 24)]


def open_camera(index: int, width: int, height: int) -> cv2.VideoCapture:
    for backend in (cv2.CAP_DSHOW, cv2.CAP_MSMF):
        cap = cv2.VideoCapture(index, backend)
        if not cap.isOpened():
            continue
        cap.set(cv2.CAP_PROP_FOURCC, cv2.VideoWriter_fourcc(*"MJPG"))
        cap.set(cv2.CAP_PROP_FRAME_WIDTH, width)
        cap.set(cv2.CAP_PROP_FRAME_HEIGHT, height)
        cap.set(cv2.CAP_PROP_FPS, 30)
        if cap.read()[0]:
            print("backend:", cap.getBackendName())
            return cap
        cap.release()
    raise RuntimeError(f"camera {index} から映像を取得できません (他アプリ/Unity が使用中か、"
                       "設定 > プライバシー > カメラ でデスクトップアプリのアクセスがオフ)")


def to_square_mirror(frame: np.ndarray) -> np.ndarray:
    h, w = frame.shape[:2]
    s = min(h, w)
    x0, y0 = (w - s) // 2, (h - s) // 2
    return cv2.flip(frame[y0:y0 + s, x0:x0 + s], 1)


def pick_person(result) -> int | None:
    """§5-1: 使用関節の visibility 平均が最も高い1人を選ぶ。"""
    if not result.pose_landmarks:
        return None
    scores = [np.mean([lm[i].visibility for i in JOINTS.values()]) for lm in result.pose_landmarks]
    return int(np.argmax(scores))


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("--camera", type=int, default=0)
    ap.add_argument("--width", type=int, default=1920)
    ap.add_argument("--height", type=int, default=1080)
    ap.add_argument("--headless", action="store_true")
    ap.add_argument("--frames", type=int, default=0, help="0 = 無制限")
    args = ap.parse_args()

    options = vision.PoseLandmarkerOptions(
        base_options=BaseOptions(model_asset_path=str(MODEL)),
        running_mode=vision.RunningMode.VIDEO,
        num_poses=2,
        output_segmentation_masks=True,
    )
    cap = open_camera(args.camera, args.width, args.height)
    print("camera:", int(cap.get(cv2.CAP_PROP_FRAME_WIDTH)), "x", int(cap.get(cv2.CAP_PROP_FRAME_HEIGHT)))

    times = []
    with vision.PoseLandmarker.create_from_options(options) as landmarker:
        n = 0
        t0 = time.perf_counter()
        while args.frames == 0 or n < args.frames:
            ok, frame = cap.read()
            if not ok:
                continue
            img = to_square_mirror(frame)
            rgb = cv2.cvtColor(img, cv2.COLOR_BGR2RGB)
            ts = int((time.perf_counter() - t0) * 1000)

            t = time.perf_counter()
            result = landmarker.detect_for_video(mp.Image(image_format=mp.ImageFormat.SRGB, data=rgb), ts)
            times.append((time.perf_counter() - t) * 1000)
            n += 1

            idx = pick_person(result)
            if args.headless:
                continue

            s = img.shape[0]
            if idx is not None:
                mask = result.segmentation_masks[idx].numpy_view()
                img = (img * (0.35 + 0.65 * mask[..., None])).astype(np.uint8)
                lm = result.pose_landmarks[idx]
                for a, b in BONES:
                    cv2.line(img, (int(lm[a].x * s), int(lm[a].y * s)), (int(lm[b].x * s), int(lm[b].y * s)), (0, 255, 0), 3)
                for i in JOINTS.values():
                    color = (0, 255, 255) if lm[i].visibility > 0.5 else (0, 0, 255)
                    cv2.circle(img, (int(lm[i].x * s), int(lm[i].y * s)), 7, color, -1)
            cv2.putText(img, f"infer {times[-1]:.0f} ms  persons {len(result.pose_landmarks)}",
                        (20, 50), cv2.FONT_HERSHEY_SIMPLEX, 1.2, (255, 255, 255), 3)
            cv2.imshow("smoke", cv2.resize(img, (800, 800)))
            if cv2.waitKey(1) & 0xFF == ord("q"):
                break

    cap.release()
    cv2.destroyAllWindows()
    if times:
        warm = times[5:] or times
        print(f"frames={len(times)}  infer mean={np.mean(warm):.1f} ms  p95={np.percentile(warm, 95):.1f} ms")


if __name__ == "__main__":
    main()
