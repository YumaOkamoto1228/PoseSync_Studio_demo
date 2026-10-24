"""判定の様子を見せるモニターウィンドウ(デモ動画用)。

Unity から少しずつ届くカメラ映像(鏡像・1:1)に、腕の骨格と各ポーズまでの距離を重ねて別ウィンドウに表示する。
見た目は tools/live_judge.py と同じ(緑の線 = 肩・腕、黄色の丸 = 手首、灰色のバー = 各ポーズまでの距離、赤線 = 閾値)。
撮影した時の判定結果(多数決・フレームごとの判定)も数秒間、下に重ねる。

OpenCV のウィンドウ操作はすべて専用スレッドで行う(HTTP の処理スレッドは最新の結果を置いていくだけ)。
"""
from __future__ import annotations

import sys
import threading
import time
from dataclasses import dataclass
from pathlib import Path

import cv2
import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parents[1] / "tools"))
from live_judge import draw  # noqa: E402

WINDOW = "PoseSync judge monitor"


@dataclass
class _Frame:
    image: np.ndarray
    det: object
    result: object


class JudgeMonitor:
    def __init__(self, matcher, size: int = 720, shot_seconds: float = 4.0):
        self.matcher = matcher
        self.size = size
        self.shot_seconds = shot_seconds
        self._latest: _Frame | None = None
        self._shot: tuple[float, str] | None = None
        self._lock = threading.Lock()
        self._thread = threading.Thread(target=self._loop, name="judge-monitor", daemon=True)

    def start(self) -> None:
        self._thread.start()

    def update(self, image: np.ndarray, det, result) -> None:
        with self._lock:
            self._latest = _Frame(image, det, result)

    def shot(self, response: dict) -> None:
        """撮影時の判定結果(/judge の応答)を、しばらく画面に出す。"""
        text = f"SHOT: {response['key']}  d={response['distance']:.1f}  votes={','.join(response['votes'])}"
        with self._lock:
            self._shot = (time.time(), text)

    def _loop(self) -> None:
        cv2.namedWindow(WINDOW, cv2.WINDOW_NORMAL)
        cv2.resizeWindow(WINDOW, self.size, self.size)
        blank = np.zeros((self.size, self.size, 3), np.uint8)
        cv2.putText(blank, "waiting for Unity...", (30, self.size // 2), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (200, 200, 200), 2)
        while True:
            with self._lock:
                frame, shot = self._latest, self._shot
            if frame is None:
                view = blank
            else:
                view = frame.image.copy()
                draw(view, frame.det, frame.result, self.matcher, self.matcher.config.threshold_deg, footer="")
                if shot is not None and time.time() - shot[0] < self.shot_seconds:
                    h = view.shape[0]
                    cv2.rectangle(view, (0, h - 70), (view.shape[1], h), (0, 0, 0), -1)
                    cv2.putText(view, shot[1], (20, h - 25), cv2.FONT_HERSHEY_SIMPLEX, 1.0, (80, 220, 255), 2)
                view = cv2.resize(view, (self.size, self.size))
            cv2.imshow(WINDOW, view)
            cv2.waitKey(30)
