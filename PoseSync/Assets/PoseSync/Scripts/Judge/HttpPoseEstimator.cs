using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PoseSync
{
    /// <summary>
    /// ローカルの Python 判定サーバー(server/app.py)に HTTP で判定を依頼する IPoseEstimator 実装。
    /// サーバーに繋がらない時は fallbackEstimator(DebugPoseEstimator)に任せるので、サーバー無しでも体験は止まらない。
    /// </summary>
    public class HttpPoseEstimator : MonoBehaviour, IPoseEstimator
    {
        public string baseUrl = "http://127.0.0.1:8765";
        public float timeoutSeconds = 5f;
        [Range(10, 100)] public int jpegQuality = 85;
        [Tooltip("サーバーに繋がらない時に使う判定")]
        public MonoBehaviour fallbackEstimator;

        public bool ServerAvailable { get; private set; }
        public string LastSummary { get; private set; } = "";

        public string DisplayName => ServerAvailable ? $"HTTP {baseUrl} {LastSummary}" : $"HTTP 未接続 → {(fallbackEstimator as IPoseEstimator)?.DisplayName}";

        [Serializable]
        class JudgeResponse
        {
            public int pose_id;
            public string key;
            public float distance;
            public bool fallback;
            public string reason;
            public string[] votes;
            public float latency_ms;
        }

        async void Start()
        {
            ServerAvailable = await CheckHealth();
            Debug.Log(ServerAvailable ? $"[HttpPoseEstimator] 判定サーバーに接続: {baseUrl}" : $"[HttpPoseEstimator] 判定サーバーに繋がりません: {baseUrl}(デバッグ判定を使います)");
        }

        public async Awaitable<bool> CheckHealth()
        {
            using var req = UnityWebRequest.Get(baseUrl + "/health");
            req.timeout = 2;
            await Send(req);
            return req.result == UnityWebRequest.Result.Success;
        }

        public async Awaitable<PoseJudgeResult> JudgeAsync(IReadOnlyList<Texture2D> frames)
        {
            if (!ServerAvailable) ServerAvailable = await CheckHealth(); // 後からサーバーを起動した場合
            if (!ServerAvailable) return await Fallback(frames);

            var form = new List<IMultipartFormSection>();
            for (int i = 0; i < frames.Count; i++)
                form.Add(new MultipartFormFileSection("frames", frames[i].EncodeToJPG(jpegQuality), $"frame{i}.jpg", "image/jpeg"));

            float t0 = Time.realtimeSinceStartup;
            using var req = UnityWebRequest.Post(baseUrl + "/judge", form);
            req.timeout = Mathf.CeilToInt(timeoutSeconds);
            await Send(req);

            if (req.result != UnityWebRequest.Result.Success)
            {
                Debug.LogWarning($"[HttpPoseEstimator] 判定に失敗: {req.error}");
                ServerAvailable = false;
                return await Fallback(frames);
            }

            var res = JsonUtility.FromJson<JudgeResponse>(req.downloadHandler.text);
            float roundTrip = (Time.realtimeSinceStartup - t0) * 1000f;
            LastSummary = $"{res.key} d={res.distance:F1} votes=[{string.Join(",", res.votes ?? Array.Empty<string>())}] server={res.latency_ms:F0}ms total={roundTrip:F0}ms";
            Debug.Log($"[HttpPoseEstimator] {LastSummary} reason={res.reason}");
            return new PoseJudgeResult { poseId = res.pose_id, distance = res.distance, fallback = res.fallback };
        }

        async Awaitable<PoseJudgeResult> Fallback(IReadOnlyList<Texture2D> frames)
        {
            if (fallbackEstimator is IPoseEstimator f) return await f.JudgeAsync(frames);
            return new PoseJudgeResult { poseId = -1, distance = 0f, fallback = true };
        }

        static async Awaitable Send(UnityWebRequest req)
        {
            var op = req.SendWebRequest();
            while (!op.isDone) await Awaitable.NextFrameAsync();
        }
    }
}
