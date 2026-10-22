"""テンプレートとの距離でポーズを判定する(設計書 §5-5)。

距離は「度」に揃えた重み付き二乗平均平方根(RMS):
- 骨4本:方向ベクトルの角度差(度)。奥行き方向を向いた骨は信頼度で重みを下げる
- 手首2点:肩幅単位のずれ × WRIST_DEG_PER_WIDTH
- 肩ラインの傾き差(度)
1ポーズに複数テンプレートがある場合は最小距離を採用する。
"""
from __future__ import annotations

import json
from collections import Counter
from dataclasses import dataclass, field
from pathlib import Path

import numpy as np

from .features import Features, compute_features, mirror_template, skeleton_from_template

DEFAULT_POSES_JSON = Path(__file__).resolve().parents[2] / "PoseSync" / "Assets" / "StreamingAssets" / "poses.json"

FALLBACK_ID = -1


@dataclass
class MatchConfig:
    # 2026-10-21 自分のサンプル46件で調整(docs/validation.md)。友人データで再調整する
    threshold_deg: float = 40.0       # これを超えたらどのポーズにも似ていない → デフォルト
    min_visibility: float = 0.5       # 両肩の visibility がこれ未満(枠から外れている) → デフォルト
    bone_weight: float = 1.0
    wrist_weight: float = 1.0
    wrist_deg_per_width: float = 40.0  # 手首が肩幅1つ分ずれたら 40° 相当
    roll_weight: float = 0.5


@dataclass
class MatchResult:
    pose_id: int
    key: str
    distance: float
    fallback: bool
    reason: str = ""                                      # "", "low_visibility", "too_far", "no_person"
    distances: dict[int, float] = field(default_factory=dict)  # pose_id -> 最小距離

    def to_json(self) -> dict:
        return {"pose_id": self.pose_id, "key": self.key, "distance": round(self.distance, 3),
                "fallback": self.fallback, "reason": self.reason,
                "distances": {str(k): round(v, 3) for k, v in self.distances.items()}}


@dataclass
class Template:
    pose_id: int
    key: str
    features: Features
    mirrored: bool


class PoseMatcher:
    def __init__(self, poses_json: Path = DEFAULT_POSES_JSON, config: MatchConfig | None = None):
        self.config = config or MatchConfig()
        data = json.loads(Path(poses_json).read_text(encoding="utf-8"))
        self.labels: dict[int, str] = {p["id"]: p["key"] for p in data["poses"]}
        self.labels[FALLBACK_ID] = data["fallback_pose"]["key"]
        self.templates: list[Template] = []
        for pose in data["poses"]:
            for t in pose.get("user_templates", []):
                self.templates.append(Template(pose["id"], pose["key"], compute_features(skeleton_from_template(t)), False))
                if t.get("mirror"):
                    m = mirror_template(t)
                    self.templates.append(Template(pose["id"], pose["key"], compute_features(skeleton_from_template(m)), True))

    # ---- 距離 ------------------------------------------------------------
    def distance(self, a: Features, b: Features) -> float:
        c = self.config
        cos = np.clip(np.sum(a.dirs * b.dirs, axis=1), -1.0, 1.0)
        bone_deg = np.degrees(np.arccos(cos))
        bone_w = c.bone_weight * np.minimum(a.reliability, b.reliability)
        wrist_deg = np.linalg.norm(a.wrists - b.wrists, axis=1) * c.wrist_deg_per_width
        roll_deg = abs(a.roll - b.roll)

        # 二乗平均の平方根: 一部の腕だけ大きく違う(片手ポーズ同士)を平均で薄めない
        num = float(np.sum(bone_w * bone_deg ** 2) + c.wrist_weight * np.sum(wrist_deg ** 2) + c.roll_weight * roll_deg ** 2)
        den = float(np.sum(bone_w) + c.wrist_weight * 2 + c.roll_weight)
        return float(np.sqrt(num / den))

    def distances(self, f: Features) -> dict[int, float]:
        out: dict[int, float] = {}
        for t in self.templates:
            d = self.distance(f, t.features)
            if t.pose_id not in out or d < out[t.pose_id]:
                out[t.pose_id] = d
        return out

    # ---- 1フレーム判定 ---------------------------------------------------
    def classify(self, f: Features | None, min_visibility: float = 1.0) -> MatchResult:
        if f is None:
            return self._fallback("no_person", float("inf"), {})
        dists = self.distances(f)
        best = min(dists, key=dists.get)
        if min_visibility < self.config.min_visibility:
            return self._fallback("low_visibility", dists[best], dists)
        if dists[best] > self.config.threshold_deg:
            return self._fallback("too_far", dists[best], dists)
        return MatchResult(best, self.labels[best], dists[best], False, "", dists)

    def _fallback(self, reason: str, distance: float, dists: dict[int, float]) -> MatchResult:
        return MatchResult(FALLBACK_ID, self.labels[FALLBACK_ID], distance, True, reason, dists)

    # ---- 複数フレーム統合(§5-5) -----------------------------------------
    def combine(self, results: list[MatchResult]) -> MatchResult:
        """多数決。同数なら距離の中央値が小さい方。フォールバックも1票として数える。"""
        if not results:
            return self._fallback("no_person", float("inf"), {})
        votes = Counter(r.pose_id for r in results)
        top = max(votes.values())
        candidates = [pid for pid, n in votes.items() if n == top]

        def median_distance(pid: int) -> float:
            ds = [r.distance for r in results if r.pose_id == pid and np.isfinite(r.distance)]
            return float(np.median(ds)) if ds else float("inf")

        winner = min(candidates, key=median_distance)
        sample = next(r for r in results if r.pose_id == winner)
        return MatchResult(winner, self.labels[winner], median_distance(winner), winner == FALLBACK_ID,
                           sample.reason, sample.distances)
