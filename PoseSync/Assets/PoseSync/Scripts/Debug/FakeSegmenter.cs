using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// テスト用の切り抜き。写真の左側(撮影者の立ち位置)を楕円形に切り抜いたことにする。
    /// カメラの前に人がいなくても、合成の重なり順や位置を確認できる。
    /// </summary>
    public class FakeSegmenter : MonoBehaviour, ISegmenter
    {
        public string DisplayName => "Fake (ellipse)";

        public async Awaitable<Texture2D> CutoutAsync(Texture2D photo)
        {
            await Awaitable.NextFrameAsync();
            if (photo == null) return null;
            int w = photo.width, h = photo.height;
            var src = photo.GetPixels32();
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                {
                    // 中心 (0.3, 0.45)、半径 (0.2, 0.42) の楕円(Texture2D は下が y=0)
                    float dx = (x / (float)w - 0.3f) / 0.2f, dy = (y / (float)h - 0.45f) / 0.42f;
                    var c = src[y * w + x];
                    c.a = (byte)(Mathf.Clamp01((1f - (dx * dx + dy * dy)) * 8f) * 255);
                    dst[y * w + x] = c;
                }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false) { name = "FakeCutout" };
            tex.SetPixels32(dst);
            tex.Apply();
            return tex;
        }
    }
}
