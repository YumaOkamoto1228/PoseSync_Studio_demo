using System.Collections.Generic;
using UnityEngine;

namespace PoseSync
{
    public struct PoseJudgeResult
    {
        /// <summary>0..N-1、デフォルトポーズは -1。</summary>
        public int poseId;
        public float distance;
        public bool fallback;
    }

    /// <summary>
    /// 姿勢推定+ポーズ判定の抽象(§4-4)。
    /// 今は Python サーバー実装を想定。将来 Unity 内推論に差し替える時もこの形を保つ。
    /// </summary>
    public interface IPoseEstimator
    {
        string DisplayName { get; }
        Awaitable<PoseJudgeResult> JudgeAsync(IReadOnlyList<Texture2D> frames);
    }
}
