import cv2
import numpy as np
from fastapi.testclient import TestClient

import app as server


def jpeg(img: np.ndarray) -> bytes:
    return cv2.imencode(".jpg", img)[1].tobytes()


def test_health():
    with TestClient(server.app) as c:
        r = c.get("/health")
        assert r.status_code == 200 and r.json()["ok"]


def test_cutout_without_person_is_422():
    blank = np.full((512, 512, 3), 128, np.uint8)
    with TestClient(server.app) as c:
        r = c.post("/cutout", files={"frame": ("f.jpg", jpeg(blank), "image/jpeg")})
    assert r.status_code == 422


def test_refine_mask_keeps_shape_and_range():
    from posesync.cutout import CutoutConfig, refine_mask
    img = np.zeros((200, 200, 3), np.uint8)
    img[50:150, 60:140] = 255
    rough = np.zeros((100, 100), np.float32)
    rough[25:75, 30:70] = 0.9  # 半分の解像度のぼやけたマスク
    alpha = refine_mask(img, rough, CutoutConfig())
    assert alpha.shape == (200, 200) and alpha.min() >= 0 and alpha.max() <= 1
    assert alpha[100, 100] > 0.95 and alpha[5, 5] < 0.05


def test_leg_prior_fills_weak_legs():
    from types import SimpleNamespace as P
    from posesync.cutout import CutoutConfig, add_leg_prior
    lm = [P(x=0.5, y=0.5) for _ in range(33)]
    lm[23], lm[24] = P(x=0.45, y=0.5), P(x=0.55, y=0.5)   # 腰
    lm[25], lm[26] = P(x=0.45, y=0.75), P(x=0.55, y=0.75)  # 膝
    lm[27], lm[28] = P(x=0.45, y=1.05), P(x=0.55, y=1.05)  # 足首(画面外)
    weak = np.zeros((200, 200, 1), np.float32)             # 脚がマスクから抜けている(MediaPipe と同じ (H, W, 1))
    m = add_leg_prior(weak, lm, CutoutConfig())
    assert m.shape == (200, 200)                           # (H, W, W) に拡張されない
    assert m[180, 90] >= 0.7 and m[180, 110] >= 0.7       # 膝から下も脚として残る
    assert m[180, 100] < 0.3 and m[100, 20] == 0           # 脚の間・脚の外は足さない


def test_hand_region_relaxes_only_around_hands():
    from types import SimpleNamespace as P
    from posesync.cutout import CutoutConfig, hand_region, refine_mask
    lm = [P(x=0.5, y=0.5) for _ in range(33)]
    for i in (15, 17, 19, 21):  # 左手: 手首と指が (0.2, 0.3) 付近
        lm[i] = P(x=0.2 + 0.01 * (i - 15), y=0.3)
    for i in (16, 18, 20, 22):  # 右手: (0.8, 0.3) 付近
        lm[i] = P(x=0.8 + 0.01 * (i - 16), y=0.3)
    region = hand_region(lm, 200, 200, CutoutConfig())
    assert region[60, 42] > 0.9 and region[180, 100] == 0
    weak = np.full((200, 200), 0.25, np.float32)   # 指など細い所の弱いマスク
    img = np.full((200, 200, 3), 128, np.uint8)
    alpha = refine_mask(img, weak, CutoutConfig(), region)
    assert alpha[60, 42] > 0.5     # 手の周りでは残る
    assert alpha[180, 100] == 0    # それ以外では従来どおり消える


def test_detect_without_person():
    blank = np.full((256, 256, 3), 128, np.uint8)
    with TestClient(server.app) as c:
        body = c.post("/detect", files={"frame": ("f.jpg", jpeg(blank), "image/jpeg")}).json()
    assert body["found"] is False


def test_judge_log_saves_result_without_images(tmp_path, monkeypatch):
    import json
    monkeypatch.setattr(server, "LOG_DIR", tmp_path)
    monkeypatch.setattr(server, "SAVE_LOG", True)
    monkeypatch.setattr(server, "SAVE_FRAMES", False)
    blank = np.full((256, 256, 3), 128, np.uint8)
    with TestClient(server.app) as c:
        body = c.post("/judge", files=[("frames", ("f.jpg", jpeg(blank), "image/jpeg"))]).json()
    assert body["log_id"]
    saved = tmp_path / body["log_id"]
    assert not list(saved.glob("*.jpg"))  # 画像は --save-frames の時だけ
    data = json.loads((saved / "result.json").read_text(encoding="utf-8"))
    assert data["skeletons"] == [None] and data["frames"][0]["reason"] == "no_person"


def test_judge_without_person_falls_back():
    blank = np.full((512, 512, 3), 128, np.uint8)
    files = [("frames", (f"f{i}.jpg", jpeg(blank), "image/jpeg")) for i in range(3)]
    with TestClient(server.app) as c:
        body = c.post("/judge", files=files).json()
    assert body["pose_id"] == -1 and body["fallback"] and body["reason"] == "no_person"
    assert body["distance"] == 999.0  # Infinity は返さない(Unity の JsonUtility が読めない)
    assert len(body["votes"]) == 3
