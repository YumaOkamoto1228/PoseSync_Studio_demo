using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>
    /// タレント専用カメラ。背景透過の RenderTexture に描画し、UI の RawImage に表示する。
    /// 構図は腰から上(撮影者と同じ)+ 手を上げた時/広げた時の余白。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class TalentFraming : MonoBehaviour
    {
        public TalentRig rig;
        public RawImage output;
        public int textureSize = 1024;
        public float fieldOfView = 20f;
        [Tooltip("腰からどれだけ下まで映すか(m)")]
        public float belowHips = 1.0f; // 足先まで描く(奥に立っているように小さく置いても体が途切れない)
        public float margin = 0.06f;

        public RenderTexture Texture { get; private set; }

        void Start() => Setup();

        public void Setup()
        {
            if (Texture != null) return;
            var cam = GetComponent<Camera>();
            Texture = new RenderTexture(textureSize, textureSize, 24, RenderTextureFormat.ARGB32) { name = "TalentRT", antiAliasing = 4 };
            cam.targetTexture = Texture;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            cam.fieldOfView = fieldOfView;
            if (output != null) output.texture = Texture;
            Frame(cam);
        }

        void Frame(Camera cam)
        {
            if (rig == null || rig.hips == null || rig.leftUpper == null) return;

            Vector3 shoulderL = rig.leftUpper.position, shoulderR = rig.rightUpper.position;
            float armLength = Vector3.Distance(rig.leftUpper.position, rig.leftLower.position)
                            + Vector3.Distance(rig.leftLower.position, rig.leftHand.position);
            float shoulderY = (shoulderL.y + shoulderR.y) * 0.5f;
            float bottom = rig.hips.position.y - belowHips;
            float top = shoulderY + armLength + margin;               // 手を上げた時の指先
            float span = Vector3.Distance(shoulderL, shoulderR) + armLength * 2f + margin * 2f; // 両手を広げた時

            float size = Mathf.Max(top - bottom, span);              // 正方形なので縦横の大きい方
            float centerY = bottom + size * 0.5f;
            float distance = size * 0.5f / Mathf.Tan(fieldOfView * 0.5f * Mathf.Deg2Rad);

            Vector3 center = new Vector3((shoulderL.x + shoulderR.x) * 0.5f, centerY, rig.hips.position.z);
            Vector3 front = rig.transform.forward; // キャラの正面方向から撮る
            transform.position = center + front * distance;
            transform.rotation = Quaternion.LookRotation(-front, Vector3.up);
        }

        void OnDestroy()
        {
            if (Texture != null) Texture.Release();
        }
    }
}
