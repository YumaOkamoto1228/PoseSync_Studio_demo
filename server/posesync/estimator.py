"""MediaPipe Pose Landmarker のラッパー(§4-4 の IPoseEstimator の Python 実装側)。

入力は Unity から届く「鏡像・1:1 クロップ済み」の RGB 画像。
人物が複数写った場合は、肩・肘・手首の visibility 平均が最も高い1人を撮影者とする(§5-1)。
"""
from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

from .features import REQUIRED, Skeleton2D, skeleton_from_landmarks

DEFAULT_MODEL = Path(__file__).resolve().parents[1] / "models" / "pose_landmarker_heavy.task"


@dataclass
class Detection:
    skeleton: Skeleton2D
    landmarks: list          # 選ばれた1人の NormalizedLandmark 列
    mask: np.ndarray | None  # (H, W) float32 0..1、人物セグメンテーション
    num_persons: int


class PoseEstimator:
    def __init__(self, model_path: Path = DEFAULT_MODEL, num_poses: int = 2, video: bool = False):
        options = vision.PoseLandmarkerOptions(
            base_options=BaseOptions(model_asset_path=str(model_path)),
            running_mode=vision.RunningMode.VIDEO if video else vision.RunningMode.IMAGE,
            num_poses=num_poses,
            output_segmentation_masks=True,
        )
        self._landmarker = vision.PoseLandmarker.create_from_options(options)
        self._video = video

    def estimate(self, rgb: np.ndarray, timestamp_ms: int = 0) -> Detection | None:
        image = mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb))
        result = (self._landmarker.detect_for_video(image, timestamp_ms) if self._video
                  else self._landmarker.detect(image))
        if not result.pose_landmarks:
            return None

        scores = [np.mean([lm[i].visibility for i in REQUIRED]) for lm in result.pose_landmarks]
        idx = int(np.argmax(scores))
        landmarks = result.pose_landmarks[idx]
        h, w = rgb.shape[:2]
        mask = result.segmentation_masks[idx].numpy_view().copy() if result.segmentation_masks else None
        return Detection(skeleton_from_landmarks(landmarks, w, h), landmarks, mask, len(result.pose_landmarks))

    def close(self) -> None:
        self._landmarker.close()

    def __enter__(self):
        return self

    def __exit__(self, *exc):
        self.close()
