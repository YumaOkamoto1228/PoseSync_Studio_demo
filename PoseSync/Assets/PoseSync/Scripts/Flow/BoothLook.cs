using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>
    /// 見た目の調整値(明るさ・タレントの配置と大きさ・立ち位置の枠)をまとめたもの。
    /// 毎フレーム反映するので、Play 中に Inspector で数値を動かして確認できる。
    /// ※ Play 中の変更は停止すると元に戻る。決まった値は停止後に入れ直すか、Play 中に右クリック → Copy Component で控える。
    /// 舞台は 1080×1080 の座標系(Stage)。
    /// </summary>
    public class BoothLook : MonoBehaviour
    {
        const float StageSize = 1080f;

        [Header("明るさ")]
        [Tooltip("カメラ映像と撮影写真の明るさ倍率")]
        [Range(0.5f, 2.5f)] public float cameraGain = 1.2f;
        [Tooltip("タレントを照らすライトの強さ")]
        [Range(0f, 3f)] public float talentLightIntensity = 1.08f;
        [Tooltip("タレントの環境光(影の部分の明るさ)")]
        public Color talentAmbient = new Color(0.48f, 0.48f, 0.54f);

        [Header("タレントの配置")]
        // 既定値: 頭の中心が画面の横74%・上から約35%。大きさは撮影者の約1.2倍(足は画面下で見切れてよい)。
        // 大きさを変える時は、頭の位置を保つよう offsetY = 701 - 0.865 × talentViewSize を目安に合わせる
        [Tooltip("タレント表示領域(正方形)の一辺(px)。大きいほどタレントが大きく映る")]
        [Range(500f, 2400f)] public float talentViewSize = 1273f;
        [Tooltip("タレントの体の中心の横位置(0=左端, 1=右端)")]
        [Range(0.5f, 1f)] public float talentCenterX = 0.74f;
        [Tooltip("タレントの縦位置のずらし(px, +で上へ)")]
        public float talentOffsetY = -400f;

        [Header("立ち位置の枠")]
        [Tooltip("枠の左右(0〜1)")]
        public Vector2 guideFrameX = new Vector2(0.05f, 0.55f);
        [Tooltip("枠の上端(0〜1, 下端は画面下)")]
        [Range(0.5f, 1f)] public float guideFrameTop = 0.86f;
        public Color guideColor = new Color(1f, 0.62f, 0.78f, 0.85f);

        [Header("References")]
        public Shader gainShader;
        public RawImage[] cameraImages;
        public Light keyLight;
        public RectTransform talentView;
        public RectTransform guideFrame;
        public Graphic[] guideGraphics;

        Material gainMaterial;

        /// <summary>タレントが表示されている時の anchoredPosition(BoothFlow がスライド演出で使う)。</summary>
        public Vector2 TalentShownPosition => new Vector2(talentCenterX * StageSize, talentOffsetY);

        void Awake()
        {
            if (gainShader != null)
            {
                gainMaterial = new Material(gainShader) { name = "CameraGain (runtime)" };
                foreach (var img in cameraImages) if (img != null) img.material = gainMaterial;
            }
            Apply();
        }

        void Update() => Apply();

        public void Apply()
        {
            if (gainMaterial != null) gainMaterial.SetFloat("_Gain", cameraGain);
            if (keyLight != null) keyLight.intensity = talentLightIntensity;
            RenderSettings.ambientLight = talentAmbient;

            if (talentView != null) talentView.sizeDelta = new Vector2(talentViewSize, talentViewSize);

            if (guideFrame != null)
            {
                guideFrame.anchorMin = new Vector2(guideFrameX.x, 0f);
                guideFrame.anchorMax = new Vector2(guideFrameX.y, guideFrameTop);
            }
            foreach (var g in guideGraphics) if (g != null) g.color = guideColor;
        }

        void OnDestroy()
        {
            if (gainMaterial != null) Destroy(gainMaterial);
        }
    }
}
