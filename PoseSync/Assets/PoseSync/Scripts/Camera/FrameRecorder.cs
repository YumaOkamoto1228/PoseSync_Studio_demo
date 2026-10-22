using System.Collections.Generic;
using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// カウントダウン中のフレームを一定間隔でリングバッファに貯める(§5-5 シャッター付近の複数フレーム判定)。
    /// テクスチャは使い回すので、録画中も GC が発生しない。
    /// </summary>
    public class FrameRecorder : MonoBehaviour
    {
        public WebcamSource webcam;
        [Tooltip("判定用フレームの解像度(MediaPipe は内部で縮小するので大きくしなくてよい)")]
        public int size = 720;
        [Tooltip("保持するフレーム数(= 判定に使う最大枚数)")]
        public int capacity = 5;
        [Tooltip("取り込み間隔(秒)。capacity × interval が判定に使う時間範囲")]
        public float interval = 0.1f;

        Texture2D[] ring;
        int next, count;
        float nextTime;
        bool recording;

        public bool IsRecording => recording;
        public int Count => count;

        void Awake()
        {
            ring = new Texture2D[capacity];
            for (int i = 0; i < capacity; i++)
                ring[i] = new Texture2D(size, size, TextureFormat.RGB24, false) { name = $"JudgeFrame{i}" };
        }

        public void StartRecording()
        {
            next = count = 0;
            nextTime = 0f;
            recording = true;
        }

        public void StopRecording() => recording = false;

        /// <summary>シャッターの瞬間のフレームを間隔に関係なく取り込む。</summary>
        public void CaptureNow()
        {
            if (!webcam.CaptureSquareInto(ring[next])) return;
            next = (next + 1) % capacity;
            count = Mathf.Min(count + 1, capacity);
        }

        void Update()
        {
            if (!recording || Time.time < nextTime) return;
            CaptureNow();
            nextTime = Time.time + interval;
        }

        /// <summary>古い順に、直近 n 枚(録画が短ければそれ以下)。</summary>
        public List<Texture2D> Recent(int n)
        {
            n = Mathf.Min(n, count);
            var list = new List<Texture2D>(n);
            for (int i = n; i >= 1; i--) list.Add(ring[(next - i + capacity) % capacity]);
            return list;
        }

        void OnDestroy()
        {
            if (ring == null) return;
            foreach (var t in ring) Destroy(t);
        }
    }
}
