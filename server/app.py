"""PoseSync 判定サーバー(Unity → Python、ローカル HTTP)。

    .venv\\Scripts\\python app.py            # http://127.0.0.1:8765

エンドポイント
    GET  /health  : 起動確認
    POST /judge   : multipart の frames(JPEG 複数、鏡像・1:1 済み)→ ポーズ判定結果(JSON)
    POST /cutout  : multipart の frame(JPEG 1枚)→ 撮影者を切り抜いた PNG(アルファ付き)。人物なしは 422

Unity 側は撮影直前〜シャッターの数フレームを送り、フレームごとの判定を多数決で1つにまとめて返す(§5-5)。
"""
from __future__ import annotations

import argparse
import datetime as dt
import json
import threading
import time
from contextlib import asynccontextmanager
from pathlib import Path

import cv2
import numpy as np
import uvicorn
from fastapi import FastAPI, File, HTTPException, Response, UploadFile

from posesync.cutout import PoseMaskSegmenter
from posesync.estimator import PoseEstimator
from posesync.features import compute_features
from posesync.matcher import PoseMatcher

ROOT = Path(__file__).resolve().parent.parent
LOG_DIR = ROOT / "data" / "judge_log"

_estimator: PoseEstimator | None = None
_matcher: PoseMatcher | None = None
_lock = threading.Lock()  # MediaPipe の推論器はスレッドセーフではない
SAVE_FRAMES = False  # 判定に使ったフレーム画像を保存するか(--save-frames)
SAVE_LOG = False     # 判定結果と骨格を data/judge_log に記録するか(起動時は既定で記録、テストでは記録しない)


def _models() -> tuple[PoseEstimator, PoseMatcher]:
    global _estimator, _matcher
    if _estimator is None:
        _estimator = PoseEstimator()
        _matcher = PoseMatcher()
        _estimator.estimate(np.zeros((256, 256, 3), np.uint8))  # 初回推論が遅いので起動時に温めておく
    return _estimator, _matcher


def _finite(x: float, cap: float = 999.0) -> float:
    # Unity の JsonUtility は Infinity を読めない
    return float(min(x, cap)) if np.isfinite(x) else cap


@asynccontextmanager
async def _lifespan(_app: FastAPI):
    _models()
    yield


app = FastAPI(title="PoseSync", lifespan=_lifespan)


@app.get("/health")
def health() -> dict:
    est, matcher = _models()
    return {"ok": True, "poses": list(matcher.labels.values()), "threshold": matcher.config.threshold_deg}


@app.post("/judge")
def judge(frames: list[UploadFile] = File(...)) -> dict:
    t0 = time.perf_counter()
    est, matcher = _models()
    images = [cv2.imdecode(np.frombuffer(f.file.read(), np.uint8), cv2.IMREAD_COLOR) for f in frames]

    per_frame, skeletons = [], []
    with _lock:
        for img in images:
            if img is None:
                continue
            det = est.estimate(cv2.cvtColor(img, cv2.COLOR_BGR2RGB))
            feats = compute_features(det.skeleton) if det else None
            per_frame.append(matcher.classify(feats, det.skeleton.shoulder_visibility if det else 0.0))
            skeletons.append(_skeleton_json(det))
    result = matcher.combine(per_frame)
    latency = (time.perf_counter() - t0) * 1000

    log_id = dt.datetime.now().strftime("%Y%m%d_%H%M%S_%f") if SAVE_LOG else ""
    response = {
        "pose_id": result.pose_id,
        "key": result.key,
        "distance": _finite(result.distance),
        "fallback": result.fallback,
        "reason": result.reason,
        "votes": [r.key for r in per_frame],
        "latency_ms": round(latency, 1),
        "log_id": log_id,
    }
    print(f"[judge] {result.key:13s} d={response['distance']:6.1f} votes={response['votes']} {latency:.0f}ms")
    if SAVE_LOG:
        _save(log_id, images if SAVE_FRAMES else [], response, per_frame, skeletons)
    return response


def _skeleton_json(det) -> dict | None:
    """閾値やテンプレートを変えた後に再計算できるよう、判定に使った骨格を残す(画像は含まない)。"""
    if det is None:
        return None
    sk = det.skeleton
    return {"points": {k: v.tolist() for k, v in sk.points().items()},
            "visibility": sk.visibility, "min_visibility": sk.min_visibility, "num_persons": det.num_persons}


@app.post("/cutout")
def cutout(frame: UploadFile = File(...)) -> Response:
    t0 = time.perf_counter()
    est, _ = _models()
    img = cv2.imdecode(np.frombuffer(frame.file.read(), np.uint8), cv2.IMREAD_COLOR)
    if img is None:
        raise HTTPException(400, "画像を読めません")
    with _lock:
        bgra = PoseMaskSegmenter(est).cutout(img)
    if bgra is None:
        raise HTTPException(422, "人物が見つかりません")
    png = cv2.imencode(".png", bgra, [cv2.IMWRITE_PNG_COMPRESSION, 1])[1].tobytes()
    print(f"[cutout] {img.shape[1]}x{img.shape[0]} {len(png) // 1024}KB {(time.perf_counter() - t0) * 1000:.0f}ms")
    return Response(content=png, media_type="image/png")


def _save(log_id: str, images, response: dict, per_frame, skeletons) -> None:
    """判定結果と骨格を残す(検証データ・誤判定の原因調査用)。画像は --save-frames の時だけ。"""
    d = LOG_DIR / log_id
    d.mkdir(parents=True, exist_ok=True)
    for i, img in enumerate(images):
        if img is not None:
            cv2.imwrite(str(d / f"frame{i}.jpg"), img)
    response = {**response, "frames": [r.to_json() for r in per_frame], "skeletons": skeletons}
    for fr in response["frames"]:
        fr["distance"] = _finite(fr["distance"])
    (d / "result.json").write_text(json.dumps(response, ensure_ascii=False, indent=1), encoding="utf-8")


def main() -> None:
    global SAVE_FRAMES, SAVE_LOG
    ap = argparse.ArgumentParser()
    ap.add_argument("--host", default="127.0.0.1")
    ap.add_argument("--port", type=int, default=8765)
    ap.add_argument("--save-frames", action="store_true", help="判定に使ったフレーム画像も data/judge_log に保存する(個人データ)")
    ap.add_argument("--no-log", action="store_true", help="判定結果と骨格の記録もしない")
    args = ap.parse_args()
    SAVE_FRAMES = args.save_frames
    SAVE_LOG = not args.no_log
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")


if __name__ == "__main__":
    main()
