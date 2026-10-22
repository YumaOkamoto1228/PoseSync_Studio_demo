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

        // ---- 検証ログ用の詳細(実装によっては空) ----
        public string reason;     // フォールバックの理由(low_visibility / too_far / no_person)
        public string[] votes;    // フレームごとの判定
        public float latencyMs;   // 判定にかかった時間(往復)
        public string logId;      // サーバー側に保存したフレームの ID(--save-frames 時)
        public string source;     // どの実装で判定したか(http / debug)
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
