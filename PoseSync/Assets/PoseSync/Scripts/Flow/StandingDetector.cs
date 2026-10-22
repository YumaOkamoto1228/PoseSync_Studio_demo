using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PoseSync
{
    /// <summary>
    /// 立ち位置ガイドの自動判定。撮影者の両肩・両肘が「枠の中」かつ「枠の高さの 40〜90%」に
    /// 一定時間とどまったら Ready になる(頭は髪型やフードで検出しにくいので腕で判定する)。
    /// 判定は Python サーバーの /detect に小さいフレームを送って行う。サーバーに繋がらない時は Available=false。
    /// </summary>
    public class StandingDetector : MonoBehaviour
    {
        public string baseUrl = "http://127.0.0.1:8765";
        public WebcamSource webcam;
        public BoothLook look;
        [Tooltip("判定に送る画像の大きさ(px)")]
        public int size = 384;
        [Tooltip("判定の間隔(秒)")]
        public float interval = 0.15f;
        [Tooltip("この秒数、立ち位置 OK が続いたら撮影を始める")]
        public float holdSeconds = 2.5f; // 「その場でキープ！」を見せる時間
        [Tooltip("両肩・両肘がこの高さの範囲(枠の高さに対する割合)にあれば立ち位置 OK")]
        public Vector2 armBand = new Vector2(0.4f, 0.9f);

        public bool Available { get; private set; } = true;
        public bool InPosition { get; private set; }
        public bool Ready => heldSeconds >= holdSeconds;
        public float Progress => Mathf.Clamp01(heldSeconds / holdSeconds);

        [Serializable]
        class Points { public float[] sl, el, wl, sr, er, wr; }

        [Serializable]
        class DetectResponse
        {
            public bool found;
            public Points points;
        }

        bool running;
        float heldSeconds;
        Texture2D frame;

        public void Begin()
        {
            heldSeconds = 0f;
            InPosition = false;
            Available = true;
            if (running) return;
            running = true;
            Loop();
        }

        public void End() => running = false;

        async void Loop()
        {
            frame ??= new Texture2D(size, size, TextureFormat.RGB24, false) { name = "StandingDetectFrame" };
            float last = Time.time;
            try
            {
                while (running && this != null)
                {
                    bool ok = await Check();
                    if (!running) break;
                    float now = Time.time;
                    InPosition = ok;
                    heldSeconds = ok ? heldSeconds + (now - last) : 0f;
                    last = now;
                    await Awaitable.WaitForSecondsAsync(interval, destroyCancellationToken);
                }
            }
            catch (OperationCanceledException) { }
            running = false;
        }

        async Awaitable<bool> Check()
        {
            if (webcam == null || !webcam.CaptureSquareInto(frame)) return false;
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("frame", frame.EncodeToJPG(80), "frame.jpg", "image/jpeg"),
            };
            using var req = UnityWebRequest.Post(baseUrl + "/detect", form);
            req.timeout = 2;
            var op = req.SendWebRequest();
            while (!op.isDone) await Awaitable.NextFrameAsync();
            if (req.result != UnityWebRequest.Result.Success)
            {
                if (Available) Debug.LogWarning($"[StandingDetector] 立ち位置の自動判定ができません: {req.responseCode} {req.error}(ボタン操作に切り替えます)");
                Available = false;
                running = false;
                return false;
            }
            var res = JsonUtility.FromJson<DetectResponse>(req.downloadHandler.text);
            return res.found && res.points != null && IsInFrame(res.points);
        }

        bool IsInFrame(Points p)
        {
            // 枠(画面座標 0..1): 左右 = guideFrameX、下端 = 0、上端 = guideFrameTop
            Vector2 fx = look != null ? look.guideFrameX : new Vector2(0.05f, 0.55f);
            float top = look != null ? look.guideFrameTop : 0.86f;
            float yMin = top * armBand.x, yMax = top * armBand.y;
            foreach (var q in new[] { p.sl, p.sr, p.el, p.er })
            {
                if (q == null || q.Length < 2) return false;
                if (q[0] < fx.x || q[0] > fx.y || q[1] < yMin || q[1] > yMax) return false;
            }
            return true;
        }

        void OnDestroy()
        {
            running = false;
            if (frame != null) Destroy(frame);
        }
    }
}
