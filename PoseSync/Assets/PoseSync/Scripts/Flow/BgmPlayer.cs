using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// 背景の BGM をループで流す。ボイスが流れている間は音量を下げて、ボイスを聞き取りやすくする。
    /// 素材(再配布)は Assets/ThirdParty/Bgm。無ければ何もしない。
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public class BgmPlayer : MonoBehaviour
    {
        public AudioClip clip;
        [Range(0f, 1f)] public float volume = 0.35f;
        [Tooltip("ボイスが流れている間の音量(volume に対する割合)")]
        [Range(0f, 1f)] public float duckRatio = 0.5f;
        [Tooltip("音量を切り替える速さ(秒)")]
        public float fadeSeconds = 0.25f;
        public VoicePlayer voice;

        AudioSource source;

        void Awake()
        {
            source = GetComponent<AudioSource>();
            source.playOnAwake = false;
            source.loop = true;
        }

        void Start()
        {
            if (clip == null) return;
            source.clip = clip;
            source.volume = volume;
            source.Play();
        }

        void Update()
        {
            if (source.clip == null) return;
            float target = voice != null && voice.IsBusy ? volume * duckRatio : volume;
            source.volume = Mathf.MoveTowards(source.volume, target, volume / Mathf.Max(fadeSeconds, 0.01f) * Time.deltaTime);
        }
    }
}
