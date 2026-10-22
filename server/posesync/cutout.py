"""撮影した1枚から撮影者を切り抜く(設計書 §6)。

マスクは姿勢推定と同じ MediaPipe Pose Landmarker の人物セグメンテーション(選ばれた1人分)を使う。
そのままだと境界がぼやけるので、元画像をガイドにしたガイデッドフィルタで輪郭に沿わせ、
少し締めてから(smoothstep)アルファにする。

差し替えやすいよう、切り抜きは `Segmenter` を満たすものなら何でもよい(§4-4)。
"""
from __future__ import annotations

from dataclasses import dataclass
from typing import Protocol

import cv2
import numpy as np

from .estimator import PoseEstimator


class Segmenter(Protocol):
    def cutout(self, bgr: np.ndarray) -> np.ndarray | None:
        """BGR 画像 → BGRA(切り抜き済み)。人物がいなければ None。"""


@dataclass
class CutoutConfig:
    guided_radius: int = 8        # ガイデッドフィルタの半径(px, 1080px 画像基準)
    guided_eps: float = 1e-3
    edge_low: float = 0.35        # この値以下は完全に透明
    edge_high: float = 0.65       # この値以上は完全に不透明


class PoseMaskSegmenter:
    """MediaPipe Pose のセグメンテーションマスクで切り抜く。"""

    def __init__(self, estimator: PoseEstimator, config: CutoutConfig | None = None):
        self.estimator = estimator
        self.config = config or CutoutConfig()

    def mask(self, bgr: np.ndarray) -> np.ndarray | None:
        det = self.estimator.estimate(cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB))
        if det is None or det.mask is None:
            return None
        return refine_mask(bgr, det.mask, self.config)

    def cutout(self, bgr: np.ndarray) -> np.ndarray | None:
        alpha = self.mask(bgr)
        if alpha is None:
            return None
        bgra = cv2.cvtColor(bgr, cv2.COLOR_BGR2BGRA)
        bgra[..., 3] = (alpha * 255).astype(np.uint8)
        return bgra


def refine_mask(bgr: np.ndarray, mask: np.ndarray, c: CutoutConfig) -> np.ndarray:
    """ぼやけたマスクを画像の輪郭に沿わせ、境界を締める。0..1 の float32 を返す。"""
    h, w = bgr.shape[:2]
    m = mask.astype(np.float32)
    if m.shape != (h, w):
        m = cv2.resize(m, (w, h), interpolation=cv2.INTER_LINEAR)
    scale = max(h, w) / 1080
    if hasattr(cv2, "ximgproc"):
        m = cv2.ximgproc.guidedFilter(bgr, m, max(1, int(c.guided_radius * scale)), c.guided_eps)
    t = np.clip((m - c.edge_low) / (c.edge_high - c.edge_low), 0.0, 1.0)
    return (t * t * (3 - 2 * t)).astype(np.float32)  # smoothstep
