using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;

namespace PoseSync
{
    /// <summary>ローカルの Python サーバー(server/app.py の /cutout)で撮影者を切り抜く。</summary>
    public class HttpSegmenter : MonoBehaviour, ISegmenter
    {
        public string baseUrl = "http://127.0.0.1:8765";
        public float timeoutSeconds = 5f;
        [Range(10, 100)] public int jpegQuality = 92;

        public string DisplayName => $"HTTP {baseUrl}/cutout";

        public async Awaitable<Texture2D> CutoutAsync(Texture2D photo)
        {
            if (photo == null) return null;
            var form = new List<IMultipartFormSection>
            {
                new MultipartFormFileSection("frame", photo.EncodeToJPG(jpegQuality), "photo.jpg", "image/jpeg"),
            };
            float t0 = Time.realtimeSinceStartup;
            using var req = UnityWebRequest.Post(baseUrl + "/cutout", form);
            req.timeout = Mathf.CeilToInt(timeoutSeconds);
            var op = req.SendWebRequest();
            while (!op.isDone) await Awaitable.NextFrameAsync();

            if (req.result != UnityWebRequest.Result.Success)
            {
                string hint = req.responseCode switch
                {
                    404 => " → 判定サーバーが切り抜き機能を追加する前の版です。app.py を再起動してください",
                    422 => " → 写真の中に人物が見つかりませんでした",
                    _ => "",
                };
                Debug.LogWarning($"[HttpSegmenter] 切り抜きに失敗: {req.responseCode} {req.error}{hint}(元の写真で合成します)");
                return null;
            }
            var tex = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "Cutout", wrapMode = TextureWrapMode.Clamp };
            tex.LoadImage(req.downloadHandler.data);
            Debug.Log($"[HttpSegmenter] {tex.width}x{tex.height} {(Time.realtimeSinceStartup - t0) * 1000f:F0}ms");
            return tex;
        }
    }
}
