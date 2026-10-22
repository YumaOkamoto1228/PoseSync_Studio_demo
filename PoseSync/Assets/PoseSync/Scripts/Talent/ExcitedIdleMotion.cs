using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// カウントダウン中の「わくわく」待機モーション(§2-5, §8)。
    /// 撮影者には追従しない。最後はデフォルト姿勢に戻るので、選ばれたポーズへ自然につながる。
    /// Mixamo 等のクリップに差し替える場合は、このコンポーネントを無効にして Animator 側で再生する。
    /// </summary>
    public class ExcitedIdleMotion : MonoBehaviour
    {
        public TalentRig rig;
        public float bounceHeight = 0.03f;
        public float bouncePerSecond = 2f;
        public float pumpPerSecond = 2f;
        public float swayDegrees = 4f;
        [Tooltip("最後のこの秒数でデフォルト姿勢へ戻す")]
        public float settleSeconds = 0.4f;

        float elapsed, duration;

        public bool IsPlaying => enabled && elapsed < duration;

        public void Play(float seconds)
        {
            elapsed = 0f;
            duration = seconds;
            enabled = true;
        }

        // 胸の前で両手をグーにして弾むポーズ(画面座標)
        static TalentPose Fists(float pumpL, float pumpR)
        {
            var fist = HandShape.FromName("fist");
            var leftLower = (new Vector3(0.35f, 0.75f, 0.55f) + Vector3.up * pumpL).normalized;
            var rightLower = (new Vector3(-0.35f, 0.75f, 0.55f) + Vector3.up * pumpR).normalized;
            return new TalentPose
            {
                left = new ArmPose { upper = new Vector3(-0.30f, -0.85f, 0.43f).normalized, lower = leftLower, fingers = leftLower, palm = Vector3.right, shape = fist },
                right = new ArmPose { upper = new Vector3(0.30f, -0.85f, 0.43f).normalized, lower = rightLower, fingers = rightLower, palm = Vector3.left, shape = fist },
            };
        }

        void Update()
        {
            if (rig == null) return;
            var rest = PoseLibrary.Rest.ToTalentPose();
            elapsed += Time.deltaTime;
            if (elapsed >= duration)
            {
                rig.hipsOffset = Vector3.zero;
                rig.hipsRoll = 0f;
                rig.SetImmediate(rest);
                enabled = false;
                return;
            }

            float remain = duration - elapsed;
            float settle = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(remain / settleSeconds)); // 1 → 0
            float intro = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / 0.25f));
            float w = Mathf.Min(settle, intro);

            float t = elapsed;
            float bounce = Mathf.Abs(Mathf.Sin(t * Mathf.PI * bouncePerSecond)) * bounceHeight;
            rig.hipsOffset = new Vector3(0f, bounce * w, 0f);
            rig.hipsRoll = Mathf.Sin(t * Mathf.PI * bouncePerSecond * 0.5f) * swayDegrees * w;

            // 左右交互に拳を上下させる
            float pumpL = Mathf.Sin(t * Mathf.PI * 2f * pumpPerSecond) * 0.25f;
            float pumpR = Mathf.Sin(t * Mathf.PI * 2f * pumpPerSecond + Mathf.PI) * 0.25f;
            rig.SetImmediate(TalentPose.Lerp(rest, Fists(pumpL, pumpR), w));
        }
    }
}
