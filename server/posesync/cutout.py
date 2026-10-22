"""撮影した1枚から撮影者を切り抜く(設計書 §6)。

基本のマスクは姿勢推定と同じ MediaPipe Pose Landmarker の人物セグメンテーション(選ばれた1人分)。
ユーザーテストで抜けやすかった部分を、関節位置を手がかりに補う:
- 脚: 暗いズボン × 暗い床・椅子で膝から下が抜ける → 腰→膝→足首に沿った帯を足す
- 手: ハートの親指など細い部分が抜ける → 手の周りだけマスクを採用する基準をゆるめる
必要なら別の人物セグメンテーション(DeepLab v3 の person / selfie multiclass)のマスクと合わせる(和集合)。
最後に元画像をガイドにしたガイデッドフィルタで輪郭に沿わせ、smoothstep で境界を締める。

差し替えやすいよう、切り抜きは `Segmenter` を満たすものなら何でもよい(§4-4)。
"""
from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Protocol

import cv2
import mediapipe as mp
import numpy as np
from mediapipe.tasks.python import vision
from mediapipe.tasks.python.core.base_options import BaseOptions

from .estimator import PoseEstimator

MODELS = Path(__file__).resolve().parents[1] / "models"
# 追加の人物セグメンテーション: モデルファイル名と「人物らしさ」の取り出し方
EXTRA_MODELS = {
    "deeplab": ("deeplab_v3.tflite", "person"),                    # Pascal VOC。人物と椅子・ソファを別クラスで分ける
    "multiclass": ("selfie_multiclass_256x256.tflite", "not_background"),  # 髪・肌・服・アクセサリ
}


class Segmenter(Protocol):
    def cutout(self, bgr: np.ndarray) -> np.ndarray | None:
        """BGR 画像 → BGRA(切り抜き済み)。人物がいなければ None。"""


@dataclass
class CutoutConfig:
    guided_radius: int = 8        # ガイデッドフィルタの半径(px, 1080px 画像基準)
    guided_eps: float = 1e-3
    edge_low: float = 0.35        # この値以下は完全に透明
    edge_high: float = 0.65       # この値以上は完全に不透明
    # 脚の補強: 腰→膝→足首の関節位置に沿って「脚がありそうな帯」を足す
    leg_prior: bool = True
    leg_width_ratio: float = 0.42  # 帯の太さ(左右の腰の距離に対する比)
    leg_strength: float = 0.75     # 帯の中のマスク値の下限(edge_high より上なら不透明になる)
    # 手の補強: 手の周りだけ、採用する基準(edge_low/high)をゆるめる
    hand_prior: bool = True
    hand_radius_ratio: float = 2.2  # 手首→指先の距離に対する、ゆるめる範囲の半径
    hand_edge_low: float = 0.08
    hand_edge_high: float = 0.35
    # 追加の人物セグメンテーション(None / "deeplab" / "multiclass")
    extra_model: str | None = None


# MediaPipe Pose の腰・膝・足首(左右)と、手首・小指・人差し指・親指(左右)
LEG_CHAINS = ((23, 25, 27), (24, 26, 28))
HANDS = ((15, 17, 19, 21), (16, 18, 20, 22))


@dataclass
class ExtraSegmenter:
    """MediaPipe ImageSegmenter で「人物らしさ」(0..1) を返す。"""
    name: str
    _segmenter: object = field(init=False, repr=False)
    _index: int = field(init=False, repr=False)
    _mode: str = field(init=False, repr=False)

    def __post_init__(self):
        file, self._mode = EXTRA_MODELS[self.name]
        self._segmenter = vision.ImageSegmenter.create_from_options(vision.ImageSegmenterOptions(
            base_options=BaseOptions(model_asset_path=str(MODELS / file)), output_confidence_masks=True))
        labels = list(self._segmenter.labels)
        self._index = labels.index("person") if self._mode == "person" else 0

    def person(self, rgb: np.ndarray) -> np.ndarray:
        res = self._segmenter.segment(mp.Image(image_format=mp.ImageFormat.SRGB, data=np.ascontiguousarray(rgb)))
        conf = _as_2d(res.confidence_masks[self._index].numpy_view())
        return conf if self._mode == "person" else 1.0 - conf


class PoseMaskSegmenter:
    """MediaPipe Pose のセグメンテーションマスク(+関節位置による補強、+追加モデル)で切り抜く。"""

    def __init__(self, estimator: PoseEstimator, config: CutoutConfig | None = None, extra: ExtraSegmenter | None = None):
        self.estimator = estimator
        self.config = config or CutoutConfig()
        if extra is None and self.config.extra_model:
            extra = ExtraSegmenter(self.config.extra_model)
        self.extra = extra

    def mask(self, bgr: np.ndarray) -> np.ndarray | None:
        rgb = cv2.cvtColor(bgr, cv2.COLOR_BGR2RGB)
        det = self.estimator.estimate(rgb)
        if det is None or det.mask is None:
            return None
        h, w = bgr.shape[:2]
        m = _resize(_as_2d(det.mask), w, h)
        if self.extra is not None:
            # 追加モデルは写っている全員を拾うので、撮影者(選ばれた1人)の関節の周りだけ使う
            extra = _resize(self.extra.person(rgb), w, h) * person_region(det.landmarks, w, h)
            m = np.maximum(m, extra)
        if self.config.leg_prior:
            m = add_leg_prior(m, det.landmarks, self.config)
        relax = hand_region(det.landmarks, w, h, self.config) if self.config.hand_prior else None
        return refine_mask(bgr, m, self.config, relax)

    def cutout(self, bgr: np.ndarray) -> np.ndarray | None:
        alpha = self.mask(bgr)
        if alpha is None:
            return None
        bgra = cv2.cvtColor(bgr, cv2.COLOR_BGR2BGRA)
        bgra[..., 3] = (alpha * 255).astype(np.uint8)
        return bgra


def _as_2d(mask: np.ndarray) -> np.ndarray:
    """MediaPipe のマスクは (H, W, 1) で返る。2D 配列と演算すると (H, W, W) に拡張されてしまうので必ず 2D にそろえる。"""
    m = np.asarray(mask, dtype=np.float32)
    return m[..., 0] if m.ndim == 3 else m


def _resize(m: np.ndarray, w: int, h: int) -> np.ndarray:
    return m if m.shape == (h, w) else cv2.resize(m, (w, h), interpolation=cv2.INTER_LINEAR)


def add_leg_prior(mask: np.ndarray, landmarks, c: CutoutConfig) -> np.ndarray:
    """腰→膝→足首に沿った太い帯をマスクに足す。関節が画面外でも(MediaPipe は推定位置を返す)帯は画面内だけ描かれる。"""
    m = _as_2d(mask).copy()
    h, w = m.shape

    def p(i: int) -> tuple[int, int]:
        return int(round(landmarks[i].x * w)), int(round(landmarks[i].y * h))

    hip_l, hip_r = p(23), p(24)
    hip_width = float(np.hypot(hip_l[0] - hip_r[0], hip_l[1] - hip_r[1]))
    if hip_width < 2:
        return m
    thickness = max(2, int(hip_width * c.leg_width_ratio))
    band = np.zeros((h, w), np.uint8)
    for chain in LEG_CHAINS:
        pts = [p(i) for i in chain]
        for a, b in zip(pts, pts[1:]):
            cv2.line(band, a, b, 255, thickness, lineType=cv2.LINE_AA)
    band = cv2.GaussianBlur(band, (0, 0), thickness * 0.25).astype(np.float32) / 255.0
    return np.maximum(m, band * c.leg_strength)


def person_region(landmarks, w: int, h: int, margin: float = 0.25) -> np.ndarray:
    """撮影者の全関節を囲む矩形を、肩幅の margin 倍広げた範囲を 1 とした重み。"""
    pts = np.array([[lm.x * w, lm.y * h] for lm in landmarks], np.float32)
    pad = margin * float(np.linalg.norm(pts[11] - pts[12])) + 0.05 * w
    x0, y0 = (pts.min(axis=0) - pad).astype(int)
    x1, y1 = (pts.max(axis=0) + pad).astype(int)
    region = np.zeros((h, w), np.float32)
    region[max(0, y0):min(h, y1), max(0, x0):min(w, x1)] = 1.0
    return cv2.GaussianBlur(region, (0, 0), 8)


def hand_region(landmarks, w: int, h: int, c: CutoutConfig) -> np.ndarray:
    """手の周り(手首と指の関節を囲む円)を 1、それ以外を 0 とした重み(境界はぼかす)。"""
    region = np.zeros((h, w), np.uint8)
    for hand in HANDS:
        pts = np.array([[landmarks[i].x * w, landmarks[i].y * h] for i in hand], np.float32)
        center = pts.mean(axis=0)
        reach = float(np.max(np.linalg.norm(pts[1:] - pts[0], axis=1)))  # 手首→指先
        radius = int(max(12.0, reach * c.hand_radius_ratio))
        cv2.circle(region, (int(center[0]), int(center[1])), radius, 255, -1, lineType=cv2.LINE_AA)
    return cv2.GaussianBlur(region, (0, 0), 6).astype(np.float32) / 255.0


def refine_mask(bgr: np.ndarray, mask: np.ndarray, c: CutoutConfig, relax: np.ndarray | None = None) -> np.ndarray:
    """ぼやけたマスクを画像の輪郭に沿わせ、境界を締める。relax が 1 の所は基準をゆるめる。0..1 の float32 を返す。"""
    h, w = bgr.shape[:2]
    m = _resize(_as_2d(mask), w, h)
    scale = max(h, w) / 1080
    if hasattr(cv2, "ximgproc"):
        m = cv2.ximgproc.guidedFilter(bgr, m, max(1, int(c.guided_radius * scale)), c.guided_eps)
    low, high = c.edge_low, c.edge_high
    if relax is not None:
        relax = _resize(_as_2d(relax), w, h)
        low = low + (c.hand_edge_low - low) * relax
        high = high + (c.hand_edge_high - high) * relax
    t = np.clip((m - low) / (high - low), 0.0, 1.0)
    return (t * t * (3 - 2 * t)).astype(np.float32)  # smoothstep
