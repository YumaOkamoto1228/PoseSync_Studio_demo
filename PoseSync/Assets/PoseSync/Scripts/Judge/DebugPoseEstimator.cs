using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseSync
{
    /// <summary>
    /// 判定サーバーが無くても流れを確認するための仮実装。
    /// カウントダウン中に数字キー 1〜(ポーズ数) を押すとそのポーズ、0 でフォールバック。何も押さなければランダム。
    /// </summary>
    public class DebugPoseEstimator : MonoBehaviour, IPoseEstimator
    {
        public float fakeLatencySeconds = 0.2f;

        int? forced;

        /// <summary>次の判定結果を指定する(-1 = おまかせ)。自動テスト用。</summary>
        public void Force(int poseId) => forced = poseId;

        public string DisplayName => "Debug (keys 0-9)";

        void Update()
        {
            var kb = Keyboard.current;
            if (kb == null) return;
            if (kb.digit0Key.wasPressedThisFrame) forced = -1;
            if (kb.digit1Key.wasPressedThisFrame) forced = 0;
            if (kb.digit2Key.wasPressedThisFrame) forced = 1;
            if (kb.digit3Key.wasPressedThisFrame) forced = 2;
            if (kb.digit4Key.wasPressedThisFrame) forced = 3;
            if (kb.digit5Key.wasPressedThisFrame) forced = 4;
            if (kb.digit6Key.wasPressedThisFrame) forced = 5;
        }

        public async Awaitable<PoseJudgeResult> JudgeAsync(IReadOnlyList<Texture2D> frames)
        {
            await Awaitable.WaitForSecondsAsync(fakeLatencySeconds);
            int id = forced ?? Random.Range(0, PoseLibrary.Count);
            forced = null;
            return new PoseJudgeResult { poseId = id, distance = 0f, fallback = id < 0 };
        }
    }
}
