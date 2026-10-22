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


def test_judge_without_person_falls_back():
    blank = np.full((512, 512, 3), 128, np.uint8)
    files = [("frames", (f"f{i}.jpg", jpeg(blank), "image/jpeg")) for i in range(3)]
    with TestClient(server.app) as c:
        body = c.post("/judge", files=files).json()
    assert body["pose_id"] == -1 and body["fallback"] and body["reason"] == "no_person"
    assert body["distance"] == 999.0  # Infinity は返さない(Unity の JsonUtility が読めない)
    assert len(body["votes"]) == 3
