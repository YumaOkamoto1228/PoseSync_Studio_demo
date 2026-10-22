"""テンプレートから作った合成骨格で、判定ロジックを確認する(カメラ不要)。"""
import json

import numpy as np
import pytest

from posesync.features import compute_features, mirror_template, skeleton_from_template
from posesync.matcher import DEFAULT_POSES_JSON, FALLBACK_ID, PoseMatcher

POSES = json.loads(DEFAULT_POSES_JSON.read_text(encoding="utf-8"))
CASES = [(p["id"], t) for p in POSES["poses"] for t in p["user_templates"]]


@pytest.fixture(scope="module")
def matcher():
    return PoseMatcher()


def jitter(sk, rng, sigma):
    for name, p in sk.points().items():
        setattr(sk, name, p + rng.normal(0, sigma, 2))
    return sk


@pytest.mark.parametrize("pose_id,template", CASES)
def test_template_matches_itself(matcher, pose_id, template):
    r = matcher.classify(compute_features(skeleton_from_template(template)))
    assert r.pose_id == pose_id
    assert r.distance < 1e-6


@pytest.mark.parametrize("pose_id,template", CASES)
def test_robust_to_small_noise(matcher, pose_id, template):
    # 肩幅の 5% 程度の関節位置ノイズでは結果が変わらない
    rng = np.random.default_rng(pose_id)
    for _ in range(20):
        sk = jitter(skeleton_from_template(template), rng, 0.05)
        assert matcher.classify(compute_features(sk)).pose_id == pose_id


def test_peace_with_other_hand(matcher):
    peace = next(p for p in POSES["poses"] if p["key"] == "peace")
    other_hand = mirror_template(peace["user_templates"][0])
    assert matcher.classify(compute_features(skeleton_from_template(other_hand))).key == "peace"


@pytest.mark.parametrize("key", ["heart_half", "big_heart"])
def test_heart_toward_wrong_side_is_not_heart(matcher, key):
    # タレントと反対側(画面左)に作ったハートはハート扱いしない
    pose = next(p for p in POSES["poses"] if p["key"] == key)
    wrong = mirror_template(pose["user_templates"][0])
    assert matcher.classify(compute_features(skeleton_from_template(wrong))).key != key


def test_arms_down_falls_back(matcher):
    rest = {"screen_left": {"upper": [-0.2, -0.98, 0], "lower": [-0.1, -0.98, 0.15]},
            "screen_right": {"upper": [0.2, -0.98, 0], "lower": [0.1, -0.98, 0.15]}}
    r = matcher.classify(compute_features(skeleton_from_template(rest)))
    assert r.pose_id == FALLBACK_ID and r.reason == "too_far"


def test_low_visibility_falls_back(matcher):
    f = compute_features(skeleton_from_template(CASES[0][1]))
    r = matcher.classify(f, min_visibility=0.2)
    assert r.fallback and r.reason == "low_visibility"


def test_combine_majority(matcher):
    f0 = compute_features(skeleton_from_template(CASES[0][1]))
    a = matcher.classify(f0)
    b = matcher.classify(None)
    assert matcher.combine([a, a, b]).pose_id == a.pose_id
    assert matcher.combine([a, b, b]).fallback
