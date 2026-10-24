using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Networking;

namespace PoseSync
{
    /// <summary>
    /// デモ動画用:カメラ映像を少しずつ判定サーバーの /monitor に送り、サーバー側のモニターウィンドウに
    /// 腕の骨格と各ポーズまでの距離を出してもらう(サーバーを --monitor 付きで起動した時だけ表示される)。
    /// 前の送信が終わるまで次は送らないので、撮影時の判定を邪魔しない。F2 キーで送信の ON/OFF。
    /// </summary>
    public class JudgeMonitorStreamer : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8765";
        public WebcamSource webcam;
        public bool streaming = true;
        [Tooltip("1秒あたりに送る枚数")]
        public float framesPerSecond = 10f;
        public int size = 512;

        Texture2D frame;
        bool sending, unavailable;
        float nextTime;

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null && kb.f2Key.wasPressedThisFrame)
            {
                streaming = !streaming;
                unavailable = false;
                Debug.Log($"[JudgeMonitorStreamer] モニターへの送信: {(streaming ? "ON" : "OFF")}");
            }
            if (!streaming || unavailable || sending || Time.time < nextTime || webcam == null) return;
            nextTime = Time.time + 1f / Mathf.Max(1f, framesPerSecond);
            frame ??= new Texture2D(size, size, TextureFormat.RGB24, false) { name = "MonitorFrame" };
            if (!webcam.CaptureSquareInto(frame)) return;
            Send(frame.EncodeToJPG(80));
        }

        async void Send(byte[] jpeg)
        {
            sending = true;
            try
            {
                var form = new List<IMultipartFormSection> { new MultipartFormFileSection("frame", jpeg, "frame.jpg", "image/jpeg") };
                using var req = UnityWebRequest.Post(baseUrl + "/monitor", form);
                req.timeout = 2;
                var op = req.SendWebRequest();
                while (!op.isDone) await Awaitable.NextFrameAsync();
                // 古いサーバー(/monitor が無い)なら送るのをやめる。繋がらない時は少し待って再試行
                if (req.responseCode == 404) unavailable = true;
                else if (req.result != UnityWebRequest.Result.Success) nextTime = Time.time + 2f;
            }
            finally
            {
                sending = false;
            }
        }

        void OnDestroy()
        {
            if (frame != null) Destroy(frame);
        }
    }
}
