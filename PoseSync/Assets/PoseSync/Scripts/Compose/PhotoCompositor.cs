using System;
using System.IO;
using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// 最終写真(1:1)を合成する(§6)。背景 → 撮影者の影 → 撮影者(切り抜き) → タレント の順に重ねる。
    /// 撮影者の位置は撮影したままの位置、タレントは画面に表示していた位置と大きさで重ねるので、
    /// 撮影中に画面で見ていた構図がそのまま写真になる。
    /// </summary>
    public class PhotoCompositor : MonoBehaviour
    {
        const float StageSize = 1080f;

        public int size = 1080;
        public Shader gainShader;
        public BoothLook look;
        public TalentFraming talentFraming;
        public RectTransform talentView;

        [Header("背景")]
        [Tooltip("StreamingAssets 内の背景画像。無ければグラデーション+水玉を生成する")]
        public string backgroundFile = "backgrounds/background.png";
        public Color backgroundTop = new Color(1f, 0.86f, 0.92f);
        public Color backgroundBottom = new Color(0.87f, 0.85f, 1f);

        [Header("撮影者の影")]
        [Range(0f, 1f)] public float shadowOpacity = 0.25f;
        public Vector2 shadowOffset = new Vector2(14f, 10f);

        [Header("保存")]
        public bool saveToDisk = true;

        Material material;
        Texture2D background;

        public string LastSavedPath { get; private set; }

        void Awake()
        {
            material = new Material(gainShader) { name = "Compositor (runtime)" };
            background = LoadBackground();
        }

        /// <summary>person が null の時は合成しない。cutout が null の時は元の写真をそのまま使う(背景差し替えなし)。</summary>
        public Texture2D Compose(Texture2D photo, Texture2D cutout)
        {
            float k = size / StageSize;
            var rt = RenderTexture.GetTemporary(size, size, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            GL.Clear(true, true, Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, size, size, 0); // 左上原点

            var full = new Rect(0, 0, size, size);
            float gain = look != null ? look.cameraGain : 1f;
            if (cutout != null)
            {
                Draw(full, background, Color.white, 1f);
                if (shadowOpacity > 0f)
                    Draw(new Rect(shadowOffset.x * k, shadowOffset.y * k, size, size), cutout, new Color(0f, 0f, 0f, shadowOpacity), 1f);
                Draw(full, cutout, Color.white, gain);
            }
            else if (photo != null)
            {
                Draw(full, photo, Color.white, gain);
            }

            // タレントは画面表示と同じ位置・大きさ(pivot は下端中央、アンカーは左下)
            if (talentFraming != null && talentFraming.Texture != null && talentView != null)
            {
                Vector2 pos = look != null ? look.TalentShownPosition : talentView.anchoredPosition;
                float s = talentView.sizeDelta.x;
                var r = new Rect((pos.x - s / 2f) * k, (StageSize - pos.y - s) * k, s * k, s * k);
                Draw(r, talentFraming.Texture, Color.white, 1f);
            }

            GL.PopMatrix();
            var result = new Texture2D(size, size, TextureFormat.RGB24, false) { name = "Composite" };
            result.ReadPixels(full, 0, 0);
            result.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);

            if (saveToDisk) Save(result);
            return result;
        }

        void Draw(Rect rect, Texture tex, Color color, float gain)
        {
            material.SetFloat("_Gain", gain);
            // 既定色 (0.5,0.5,0.5,0.5) は GUI 用シェーダー向けなので、色は明示する
            Graphics.DrawTexture(rect, tex, new Rect(0, 0, 1, 1), 0, 0, 0, 0, color, material);
        }

        void Save(Texture2D tex)
        {
            try
            {
                string dir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../data/photos"));
                Directory.CreateDirectory(dir);
                LastSavedPath = Path.Combine(dir, $"PoseSync_{DateTime.Now:yyyyMMdd_HHmmss}.png");
                File.WriteAllBytes(LastSavedPath, tex.EncodeToPNG());
                Debug.Log($"[PhotoCompositor] 保存: {LastSavedPath}");
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[PhotoCompositor] 保存に失敗: {e.Message}");
            }
        }

        Texture2D LoadBackground()
        {
            string path = Path.Combine(Application.streamingAssetsPath, backgroundFile);
            if (File.Exists(path))
            {
                var tex = new Texture2D(2, 2) { name = "Background" };
                if (tex.LoadImage(File.ReadAllBytes(path))) return tex;
            }
            return GenerateBackground(512);
        }

        /// <summary>淡いピンク→ラベンダーのグラデーションに白い水玉。</summary>
        Texture2D GenerateBackground(int n)
        {
            var tex = new Texture2D(n, n, TextureFormat.RGB24, false) { name = "GeneratedBackground", wrapMode = TextureWrapMode.Clamp };
            var px = new Color[n * n];
            float cell = n / 8f, radius = cell * 0.18f;
            for (int y = 0; y < n; y++)
            {
                // Texture2D は下が y=0
                var baseColor = Color.Lerp(backgroundBottom, backgroundTop, y / (float)(n - 1));
                for (int x = 0; x < n; x++)
                {
                    // 段ごとに半マスずらした水玉
                    float row = Mathf.Floor(y / cell);
                    float ox = (row % 2 == 0) ? 0f : cell * 0.5f;
                    float cx = Mathf.Repeat(x + ox, cell) - cell * 0.5f;
                    float cy = Mathf.Repeat(y, cell) - cell * 0.5f;
                    float d = Mathf.Sqrt(cx * cx + cy * cy);
                    float dot = Mathf.Clamp01(radius + 1f - d) * 0.55f;
                    px[y * n + x] = Color.Lerp(baseColor, Color.white, dot);
                }
            }
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
        }
    }
}
