"""上半身の骨格から、ポーズ判定用の特徴量を作る(設計書 §5-3 + design_changes.md)。

座標系はすべて「画面座標」: x=画面右, y=上。Unity から届く画像は鏡像・1:1 クロップ済みなので、
MediaPipe の解剖学的な左右ラベルは使わず、肩の x 座標で画面左/右を決める。

特徴量
- 上腕・前腕(画面左右 計4本)の 2D 単位方向ベクトル
- 肩幅で正規化した「両肩の中点 → 手首」の相対位置(左右)
- 肩ラインの傾き(度, +で頭が画面右へ傾く)

テンプレート(poses.json の user_templates)も同じ Skeleton2D を経由して特徴量にするので、
撮影者とテンプレートで計算方法がずれない。
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

# MediaPipe Pose の関節番号(画面左右は肩の位置で決め直す)
SHOULDERS = (11, 12)
ARM_CHAINS = {11: (11, 13, 15), 12: (12, 14, 16)}  # shoulder -> (shoulder, elbow, wrist)
REQUIRED = (11, 12, 13, 14, 15, 16)

# 肩幅を 1 とした時の腕の長さ(テンプレート → 骨格への変換用。成人のおおよその比率)
UPPER_ARM = 0.85
FOREARM = 0.75

BONE_NAMES = ("left_upper", "left_lower", "right_upper", "right_lower")


@dataclass
class Skeleton2D:
    """画面座標(y 上向き)の上半身。単位は任意(特徴量で肩幅正規化する)。"""
    sl: np.ndarray
    el: np.ndarray
    wl: np.ndarray
    sr: np.ndarray
    er: np.ndarray
    wr: np.ndarray
    min_visibility: float = 1.0
    visibility: dict[str, float] | None = None  # 関節ごと(sl, el, wl, sr, er, wr)

    def points(self) -> dict[str, np.ndarray]:
        return {"sl": self.sl, "el": self.el, "wl": self.wl, "sr": self.sr, "er": self.er, "wr": self.wr}

    @property
    def shoulder_visibility(self) -> float:
        """撮影者が枠内にいるかの目安。腕は体の前を横切ると visibility が下がるので判定に使わない。"""
        if self.visibility is None:
            return 1.0
        return min(self.visibility["sl"], self.visibility["sr"])


@dataclass
class Features:
    dirs: np.ndarray         # (4, 2) 単位ベクトル、BONE_NAMES 順
    reliability: np.ndarray  # (4,) 0..1、画面に対して奥行き方向を向いた骨ほど小さい
    wrists: np.ndarray       # (2, 2) 肩中点→手首 / 肩幅、[左, 右]
    roll: float              # 度, +で頭が画面右へ

    def to_list(self) -> list[float]:
        return [*self.dirs.ravel(), *self.wrists.ravel(), self.roll]


def skeleton_from_landmarks(landmarks, width: int, height: int) -> Skeleton2D:
    """MediaPipe の landmark 列(正規化座標, y 下向き)から画面座標の骨格を作る。"""
    def p(i: int) -> np.ndarray:
        return np.array([landmarks[i].x * width, -landmarks[i].y * height], dtype=np.float64)

    a, b = SHOULDERS
    left_key, right_key = (a, b) if p(a)[0] <= p(b)[0] else (b, a)
    ls, le, lw = (p(i) for i in ARM_CHAINS[left_key])
    rs, re, rw = (p(i) for i in ARM_CHAINS[right_key])
    names = ("sl", "el", "wl", "sr", "er", "wr")
    indices = (*ARM_CHAINS[left_key], *ARM_CHAINS[right_key])
    per_joint = {n: float(landmarks[i].visibility) for n, i in zip(names, indices)}
    return Skeleton2D(ls, le, lw, rs, re, rw, min_visibility=min(per_joint.values()), visibility=per_joint)


def skeleton_from_template(template: dict) -> Skeleton2D:
    """poses.json の user_template(3D 方向ベクトル)を正面に投影した骨格にする。"""
    roll = np.radians(float(template.get("shoulder_roll", 0.0)))
    # 頭が画面右へ傾く = 上半身が時計回り → 肩ラインは -roll 回転
    c, s = np.cos(-roll), np.sin(-roll)
    half = np.array([0.5 * c, 0.5 * s])
    sl, sr = -half, half

    def arm(shoulder: np.ndarray, data: dict) -> tuple[np.ndarray, np.ndarray]:
        upper = _unit3(data["upper"])
        lower = _unit3(data["lower"])
        # 投影で短くなる分(奥行き成分)はそのまま残す = 正面カメラから見た長さ
        elbow = shoulder + UPPER_ARM * upper[:2]
        wrist = elbow + FOREARM * lower[:2]
        return elbow, wrist

    el, wl = arm(sl, template["screen_left"])
    er, wr = arm(sr, template["screen_right"])
    return Skeleton2D(sl, el, wl, sr, er, wr)


def mirror_template(template: dict) -> dict:
    """左右反転したテンプレート(mirror=true 用)。"""
    def flip(data: dict) -> dict:
        return {k: ([-v[0], v[1], v[2]] if k in ("upper", "lower") else v) for k, v in data.items()}

    return {
        "screen_left": flip(template["screen_right"]),
        "screen_right": flip(template["screen_left"]),
        "shoulder_roll": -float(template.get("shoulder_roll", 0.0)),
        "mirror": False,
    }


def compute_features(sk: Skeleton2D) -> Features:
    shoulder_vec = sk.sr - sk.sl
    width = float(np.linalg.norm(shoulder_vec))
    if width < 1e-6:
        raise ValueError("肩幅がゼロです")
    center = (sk.sl + sk.sr) * 0.5

    bones = [(sk.sl, sk.el, UPPER_ARM), (sk.el, sk.wl, FOREARM), (sk.sr, sk.er, UPPER_ARM), (sk.er, sk.wr, FOREARM)]
    dirs = np.zeros((4, 2))
    reliability = np.zeros(4)
    for i, (a, b, expected) in enumerate(bones):
        v = b - a
        n = float(np.linalg.norm(v))
        dirs[i] = v / n if n > 1e-9 else (0.0, -1.0)
        # 投影長が期待長の半分以上なら方向は信頼できる
        reliability[i] = float(np.clip((n / width) / (expected * 0.5), 0.0, 1.0))

    wrists = np.stack([(sk.wl - center) / width, (sk.wr - center) / width])
    roll = -float(np.degrees(np.arctan2(shoulder_vec[1], shoulder_vec[0])))
    return Features(dirs, reliability, wrists, roll)


def _unit3(v) -> np.ndarray:
    a = np.asarray(v, dtype=np.float64)
    return a / np.linalg.norm(a)
