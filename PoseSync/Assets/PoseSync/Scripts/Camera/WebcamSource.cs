using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>
    /// Webカメラ映像を中央1:1クロップ+鏡像で表示し、同じ見た目のフレームを切り出す。
    /// 表示しているものと判定・合成に使うものが同じになるよう、切り出しも同じ uvRect を使う。
    /// </summary>
    public class WebcamSource : MonoBehaviour
    {
        [Tooltip("デバイス名にこの文字列を含むカメラを優先(空なら先頭)")]
        public string preferredDeviceKeyword = "C200";
        public int requestWidth = 1920;
        public int requestHeight = 1080;
        public int requestFps = 30;
        public bool mirror = true;
        public RawImage view;

        public WebCamTexture Texture { get; private set; }
        public string DeviceName { get; private set; } = "(none)";
        public bool IsReady => Texture != null && Texture.isPlaying && Texture.width > 16;

        void Start()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Debug.LogWarning("[WebcamSource] カメラが見つかりません");
                return;
            }
            DeviceName = devices[0].name;
            foreach (var d in devices)
            {
                if (!string.IsNullOrEmpty(preferredDeviceKeyword) && d.name.Contains(preferredDeviceKeyword))
                {
                    DeviceName = d.name;
                    break;
                }
            }
            Texture = new WebCamTexture(DeviceName, requestWidth, requestHeight, requestFps);
            Texture.Play();
            if (view != null) view.texture = Texture;
        }

        void Update()
        {
            if (view != null && IsReady) view.uvRect = CropRect();
        }

        /// <summary>中央正方形+鏡像の uvRect。</summary>
        public Rect CropRect()
        {
            float w = Mathf.Min(1f, (float)Texture.height / Texture.width);
            float h = Mathf.Min(1f, (float)Texture.width / Texture.height);
            float x0 = (1f - w) * 0.5f, y0 = (1f - h) * 0.5f;
            var r = mirror ? new Rect(x0 + w, y0, -w, h) : new Rect(x0, y0, w, h);
            if (Texture.videoVerticallyMirrored) r = new Rect(r.x, r.y + r.height, r.width, -r.height);
            return r;
        }

        /// <summary>画面に表示中の見た目そのまま(1:1・鏡像)の静止画を返す。</summary>
        public Texture2D CaptureSquare(int size)
        {
            if (!IsReady) return null;
            var tex = new Texture2D(size, size, TextureFormat.RGB24, false);
            CaptureSquareInto(tex);
            return tex;
        }

        /// <summary>既存の正方形テクスチャに書き込む(判定用フレームのリングバッファで再利用する)。</summary>
        public bool CaptureSquareInto(Texture2D dst)
        {
            if (!IsReady) return false;
            int size = dst.width;
            Rect r = CropRect();
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(Texture, rt, new Vector2(r.width, r.height), new Vector2(r.x, r.y));

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            dst.ReadPixels(new Rect(0, 0, size, size), 0, 0);
            dst.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            return true;
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Stop();
        }
    }
}
