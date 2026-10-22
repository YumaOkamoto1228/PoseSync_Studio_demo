using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// 撮影者の切り抜きの抽象(§4-4, §6)。今は Python サーバー実装。
    /// 戻り値はアルファ付きの切り抜き画像。人物がいない・失敗した時は null(呼び出し側は元の写真で合成する)。
    /// </summary>
    public interface ISegmenter
    {
        string DisplayName { get; }
        Awaitable<Texture2D> CutoutAsync(Texture2D photo);
    }
}
