using System.Collections.Generic;
using UnityEngine;
using VRM;

namespace PoseSync
{
    /// <summary>
    /// TalentRig の現在のポーズに含まれる表情(ウインク・笑顔)をモデルに反映する。
    /// ポーズの補間と一緒に表情も補間されるので、ポーズ遷移中に自然に切り替わる。
    ///
    /// ウインクは VRM の Blink_L / Blink_R プリセットを使わず、顔メッシュの片目モーフを直接動かす。
    /// (配布モデルでは Blink_R が空のことがある。あやめモデルも Blink_R に割り当てが無かった)
    /// </summary>
    [RequireComponent(typeof(TalentRig))]
    public class TalentExpression : MonoBehaviour
    {
        [Tooltip("片目を閉じるモーフ名の候補(先に見つかったものを使う)。{0} に R / L が入る。R/L はキャラ本人から見た左右")]
        public string[] winkMorphPatterns = { "Fcl_EYE_Joy_{0}", "Fcl_EYE_Close_{0}", "ウィンク{1}", "まばたき{1}" };

        TalentRig rig;
        VRMBlendShapeProxy proxy;
        readonly List<(SkinnedMeshRenderer smr, int index)> anatomicalRight = new(), anatomicalLeft = new();
        bool initialized;

        void Awake() => Initialize();

        public void Initialize()
        {
            if (initialized) return;
            initialized = true;
            rig = GetComponent<TalentRig>();
            proxy = GetComponentInChildren<VRMBlendShapeProxy>();
            FindWinkMorph("R", "右", anatomicalRight);
            FindWinkMorph("L", "左", anatomicalLeft);
            if (anatomicalRight.Count == 0 || anatomicalLeft.Count == 0)
                Debug.LogWarning($"[TalentExpression] 片目のモーフが見つかりません (R={anatomicalRight.Count}, L={anatomicalLeft.Count})", this);
        }

        void FindWinkMorph(string side, string sideJa, List<(SkinnedMeshRenderer, int)> into)
        {
            foreach (var pattern in winkMorphPatterns)
            {
                string name = string.Format(pattern, side, sideJa);
                foreach (var smr in GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    if (smr.sharedMesh == null) continue;
                    int index = smr.sharedMesh.GetBlendShapeIndex(name);
                    if (index >= 0) into.Add((smr, index));
                }
                if (into.Count > 0) return;
            }
        }

        void LateUpdate() => Apply();

        public void Apply()
        {
            if (rig == null) return;
            var e = rig.Current.expression;
            // 正面を向いたタレントは、画面左の目 = 本人の右目
            bool leftIsAnatomicalLeft = rig.ScreenLeftIsAnatomicalLeft;
            float right = leftIsAnatomicalLeft ? e.blink_screen_right : e.blink_screen_left;
            float left = leftIsAnatomicalLeft ? e.blink_screen_left : e.blink_screen_right;
            foreach (var (smr, i) in anatomicalRight) smr.SetBlendShapeWeight(i, right * 100f);
            foreach (var (smr, i) in anatomicalLeft) smr.SetBlendShapeWeight(i, left * 100f);
            if (proxy != null && Application.isPlaying) proxy.ImmediatelySetValue(BlendShapeKey.CreateFromPreset(BlendShapePreset.Joy), e.joy);
        }
    }
}
