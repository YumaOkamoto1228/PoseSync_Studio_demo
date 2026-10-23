using System.Threading;
using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>
    /// カメラのシャッター演出(連番 PNG)。撮影の瞬間に閉じ、判定・合成が終わるまで閉じたまま待ち、終わったら開く。
    /// 閉じている時間は処理時間で変わるので、「閉じる」「開く」を別の連番にして、閉じきったコマで止めておく。
    /// 素材(再配布禁止)は Assets/ThirdParty/Shutter に置く。無い時は HasFrames=false になり、呼び出し側は白フラッシュにする。
    /// </summary>
    public class ShutterPlayer : MonoBehaviour
    {
        public RawImage view;
        public Texture2D[] closeFrames;
        public Texture2D[] openFrames;
        public float fps = 30f;
        [Tooltip("閉じる時に鳴らす音(任意)")]
        public AudioClip closeSound;
        [Tooltip("開く時に鳴らす音(任意)")]
        public AudioClip openSound;
        public AudioSource audioSource;

        // シーンには素材への参照だけが残るので、素材の無い環境(clone 直後など)では要素が null になる
        public bool HasFrames => Valid(closeFrames) && Valid(openFrames);

        static bool Valid(Texture2D[] frames) => frames != null && frames.Length > 0 && System.Array.TrueForAll(frames, f => f != null);
        public bool IsClosed { get; private set; }

        void Awake() => Hide();

        public void Hide()
        {
            IsClosed = false;
            if (view != null) view.gameObject.SetActive(false);
        }

        /// <summary>閉じる連番を再生し、最後のコマ(閉じた状態)を表示したまま戻る。</summary>
        public async Awaitable CloseAsync(CancellationToken ct)
        {
            Play(closeSound);
            await PlayFrames(closeFrames, ct);
            IsClosed = true;
        }

        /// <summary>開く連番を再生し、終わったら非表示にする。</summary>
        public async Awaitable OpenAsync(CancellationToken ct)
        {
            Play(openSound);
            await PlayFrames(openFrames, ct);
            Hide();
        }

        async Awaitable PlayFrames(Texture2D[] frames, CancellationToken ct)
        {
            view.gameObject.SetActive(true);
            float start = Time.time;
            int last = -1;
            while (true)
            {
                int i = Mathf.Min(frames.Length - 1, Mathf.FloorToInt((Time.time - start) * fps));
                if (i != last)
                {
                    view.texture = frames[i];
                    last = i;
                }
                if (i >= frames.Length - 1) break;
                await Awaitable.NextFrameAsync(ct);
            }
        }

        void Play(AudioClip clip)
        {
            if (clip != null && audioSource != null) audioSource.PlayOneShot(clip);
        }
    }
}
