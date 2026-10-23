using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace PoseSync
{
    /// <summary>ガイドのボイス一式(素材は再配布禁止のため Assets/ThirdParty/Voice。無い物は鳴らさない)。</summary>
    [Serializable]
    public class GuideVoices
    {
        public AudioClip start;        // 撮影するよ
        public AudioClip standInFrame; // 枠に収まるように立ってね
        public AudioClip keep;         // その場でキープ
        public AudioClip thinkPose;    // ポーズを考えてね
        public AudioClip decided;      // 決まったかな?
        public AudioClip count3, count2, count1;
        public AudioClip result;       // 上手く撮れたかな?
        public AudioClip bye;          // また撮ろうね
    }

    /// <summary>
    /// ボイスを順番に1つずつ流す。前のボイスが終わるまで次は流さず、ボイスとボイスの間は必ず minGap 秒空ける。
    /// 再生中かどうかは AudioSource ではなくクリップの長さで管理する(バッチモードなど音が出ない環境でも同じ時間で進む)。
    /// </summary>
    public class VoicePlayer : MonoBehaviour
    {
        public AudioSource source;
        public GuideVoices voices = new GuideVoices();
        [Tooltip("ボイスとボイスの間に必ず空ける時間(秒)")]
        public float minGap = 0.5f;
        [Tooltip("ボイスを流すたびに時刻と前のボイスとの間隔をログに出す(確認用)")]
        public bool logTimings = true;

        class Item
        {
            public AudioClip clip;
            public bool started, finished;
        }

        readonly Queue<Item> queue = new Queue<Item>();
        Item playing;
        float playingEnd = float.NegativeInfinity;

        /// <summary>今流れているか、待っているボイスがあるか。</summary>
        public bool IsBusy => queue.Count > 0 || Time.time < playingEnd;

        /// <summary>流す順番に加える(すぐには鳴らないことがある)。</summary>
        public void Play(AudioClip clip) => Enqueue(clip);

        /// <summary>順番に加え、鳴り始めるまで待つ。戻った時がボイスの鳴り始め(画面の表示を合わせる用)。</summary>
        public async Awaitable PlayAndWaitStart(AudioClip clip, CancellationToken ct)
        {
            var item = Enqueue(clip);
            while (item != null && !item.started) await Awaitable.NextFrameAsync(ct);
        }

        /// <summary>順番に加え、流し終わるまで待つ。</summary>
        public async Awaitable PlayAndWaitEnd(AudioClip clip, CancellationToken ct)
        {
            var item = Enqueue(clip);
            while (item != null && !item.finished) await Awaitable.NextFrameAsync(ct);
        }

        /// <summary>
        /// 順番待ちと 0.5 秒の間隔を無視して、今すぐ流す(カウントダウン用)。
        /// 流れている・待っているボイスは止めて捨てる。この後に加えたボイスは、このボイスの後に間隔を空けて流れる。
        /// </summary>
        public void PlayImmediate(AudioClip clip)
        {
            if (clip == null) return;
            StopAll();
            queue.Enqueue(new Item { clip = clip });
            playingEnd = float.NegativeInfinity; // 間隔の待ちをなくす
            Pump();
        }

        /// <summary>今のボイスを止め、待っているボイスも捨てる(最初に戻る時)。</summary>
        public void StopAll()
        {
            queue.Clear();
            if (playing != null) playing.finished = true;
            playing = null;
            playingEnd = float.NegativeInfinity;
            if (source != null) source.Stop();
        }

        Item Enqueue(AudioClip clip)
        {
            if (clip == null) return null;
            var item = new Item { clip = clip };
            queue.Enqueue(item);
            Pump();
            return item;
        }

        void Update() => Pump();

        void Pump()
        {
            if (playing != null && Time.time >= playingEnd)
            {
                playing.finished = true;
                playing = null;
            }
            if (playing != null || queue.Count == 0 || Time.time < playingEnd + minGap) return;

            playing = queue.Dequeue();
            playing.started = true;
            if (logTimings)
                Debug.Log($"[VoicePlayer] t={Time.time:F2} {playing.clip.name} ({playing.clip.length:F2}s) 前のボイスとの間隔={(float.IsNegativeInfinity(playingEnd) ? "-" : (Time.time - playingEnd).ToString("F2"))}s");
            playingEnd = Time.time + playing.clip.length;
            if (source != null)
            {
                source.Stop();
                source.clip = playing.clip;
                source.Play();
            }
        }
    }
}
