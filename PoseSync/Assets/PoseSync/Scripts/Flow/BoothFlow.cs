using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace PoseSync
{
    public enum BoothState { Idle, Standing, PoseGuide, Countdown, Judging, Result }

    /// <summary>
    /// 体験フロー(§2)。待機 → 立ち位置 → ポーズ指示 → カウントダウン → 判定 → 結果。
    /// タレントが撮影者に追従する時間帯は作らない(§2 の要点)。
    /// Space キーでボタンと同じ操作、Esc で待機に戻る。
    /// </summary>
    public class BoothFlow : MonoBehaviour
    {
        [Header("Systems")]
        public TalentRig talent;
        public ExcitedIdleMotion idleMotion;
        public WebcamSource webcam;
        public FrameRecorder frameRecorder;
        [Tooltip("IPoseEstimator を実装したコンポーネント")]
        public MonoBehaviour poseEstimator;
        [Tooltip("ISegmenter を実装したコンポーネント(撮影者の切り抜き)")]
        public MonoBehaviour segmenter;
        [Tooltip("立ち位置の自動判定。無い・使えない時は「撮影開始！」ボタンで進む")]
        public StandingDetector standingDetector;
        [Tooltip("撮影時のシャッター演出。素材が無い時は白フラッシュ")]
        public ShutterPlayer shutter;
        public PhotoCompositor compositor;
        [Tooltip("判定に使うフレーム数(シャッター直前〜シャッター)")]
        public int judgeFrameCount = 5;

        [Header("Timing")]
        public float talentSlideSeconds = 0.5f;
        [Tooltip("見本ポーズを1つ見せる時間(秒)")]
        public float guidePoseInterval = 3f;
        public float countdownSeconds = 3f;
        public float poseTransitionSeconds = 0.4f;
        public int captureSize = 1080;

        [Header("UI")]
        public GameObject startPanel;
        public Button startButton;
        public Button primaryButton;
        public Text primaryButtonLabel;
        [Tooltip("結果画面で「もう一度」の隣に出す「終了する」ボタン")]
        public Button exitButton;
        [Tooltip("2つのボタンを並べる時の、中央からのずれ(px)")]
        public float buttonPairOffset = 230f;

        [Header("ボイス")]
        [Tooltip("ガイドのボイス(順番に1つずつ・間を 0.5 秒以上空けて流す)。無ければ文字だけ")]
        public VoicePlayer voice;
        [Tooltip("ポーズを考える画面に入ってから「決まったかな?」を流すまでの秒数")]
        public float decidedVoiceDelay = 9f;
        [Tooltip("ボイスが無い時に、終了のあいさつを表示する時間(秒)")]
        public float exitMessageSeconds = 2f;
        public GameObject standingGuide;
        public Text messageText;
        public Text countdownText;
        public Text poseLabelText;
        public RectTransform talentView;
        [Tooltip("見た目の調整値。タレントの表示位置もここから取る")]
        public BoothLook look;
        public RawImage resultPhoto;
        [Tooltip("合成した最終写真を表示する(明るさ補正済みなのでゲインはかけない)")]
        public RawImage compositeView;
        public Image flash;
        public Text statusText;
        [Tooltip("左下の開発用ステータス(状態・カメラ・判定・記録状況)を表示するか。Play 中は F1 キーで切り替え")]
        public bool showStatus = false;

        public BoothState State
        {
            get => state;
            private set
            {
                if (state == value) return;
                state = value;
                StateChanged?.Invoke(value);
            }
        }
        BoothState state;

        /// <summary>状態が変わった時(検証ログ用)。</summary>
        public event Action<BoothState> StateChanged;
        /// <summary>体験の途中で Esc により最初に戻された時。引数は中断した時の状態。</summary>
        public event Action<BoothState> SessionAborted;
        /// <summary>合成まで終わった時。引数は切り抜きに成功したか。</summary>
        public event Action<bool> ShotCompleted;
        /// <summary>状態表示の末尾に足す文字列(検証ログの記録状況など)。</summary>
        public string StatusSuffix { get; set; } = "";

        public PoseJudgeResult LastResult { get; private set; }
        public Texture2D LastPhoto { get; private set; }
        public Texture2D LastComposite { get; private set; }
        Texture2D lastCutout;

        IPoseEstimator Estimator => poseEstimator as IPoseEstimator;
        ISegmenter Segmenter => segmenter as ISegmenter;

        bool advanceRequested, exitRequested;
        CancellationTokenSource sessionCts;
        Vector2 talentShownPos;

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(RequestAdvance);
            if (primaryButton != null) primaryButton.onClick.AddListener(RequestAdvance);
            if (exitButton != null) exitButton.onClick.AddListener(RequestExit);
            if (talentView != null) talentShownPos = talentView.anchoredPosition;
            if (Estimator == null) Debug.LogError("[BoothFlow] poseEstimator に IPoseEstimator がありません", this);

            // AudioListener が無いとボイス・効果音が一切聞こえない(古いシーンにも対応するため実行時に補う)
            if (FindFirstObjectByType<AudioListener>() == null)
            {
                var cam = Camera.main != null ? Camera.main.gameObject : gameObject;
                cam.AddComponent<AudioListener>();
                Debug.Log($"[BoothFlow] AudioListener が無かったので {cam.name} に追加しました");
            }
        }

        void Start() => Restart();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) RequestAdvance();
                if (kb.escapeKey.wasPressedThisFrame) Quit();
            }
            if (kb != null && kb.f1Key.wasPressedThisFrame) showStatus = !showStatus;
            if (statusText != null && statusText.gameObject.activeSelf != showStatus) statusText.gameObject.SetActive(showStatus);
            if (statusText != null && showStatus)
                statusText.text =$"{State}  cam:{(webcam != null && webcam.IsReady ? webcam.DeviceName : "not ready")}  judge:{Estimator?.DisplayName}{StatusSuffix}";
        }

        public void RequestAdvance() => advanceRequested = true;

        /// <summary>Esc: アプリを終了する(エディタでは Play を止める)。体験の途中なら中断として記録する。</summary>
        public void Quit()
        {
            if (State != BoothState.Idle && State != BoothState.Result) SessionAborted?.Invoke(State);
            sessionCts?.Cancel();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>結果画面の「終了する」。</summary>
        public void RequestExit() => exitRequested = true;

        public void Restart()
        {
            sessionCts?.Cancel();
            sessionCts = CancellationTokenSource.CreateLinkedTokenSource(destroyCancellationToken);
            RunSession(sessionCts.Token);
        }

        async void RunSession(CancellationToken ct)
        {
            try
            {
                await IdleStep(ct);
                // 「もう一度」なら立ち位置ガイドから撮り直し、「終了する」ならあいさつして待機画面へ
                while (true)
                {
                    await StandingStep(ct);
                    await PoseGuideStep(ct);
                    await CountdownStep(ct);
                    await JudgeStep(ct);
                    if (!await ResultStep(ct)) break;
                    ClearShot();
                }
                await ExitStep(ct);
                Restart();
            }
            catch (OperationCanceledException) { }
        }

        // ---- 1. 待機 ------------------------------------------------------
        async Awaitable IdleStep(CancellationToken ct)
        {
            State = BoothState.Idle;
            ResetView();
            startPanel.SetActive(true);
            await WaitForAdvance(ct);
            startPanel.SetActive(false);
        }

        // ---- 2-3. タレント登場 + ガイド①立ち位置 ----------------------------
        async Awaitable StandingStep(CancellationToken ct)
        {
            State = BoothState.Standing;
            if (talentView.anchoredPosition != ShownTalentPos) await SlideTalent(true, ct); // 撮り直しの時はもう出ている
            standingGuide.SetActive(true);
            const string Guide = "枠の中に、頭から腰までが入るように立ってね";
            SetMessage(Guide);
            // 「撮影するよ」→(0.5 秒)→「枠に収まるように立ってね」。撮り直しの時も同じ
            Voice(v => v.start);
            Voice(v => v.standInFrame);
            bool keepPlayed = false;

            // 枠の中に立ったら自動で撮影へ進む。判定できない時(サーバー未起動など)だけボタンを出す
            bool auto = standingDetector != null && standingDetector.isActiveAndEnabled;
            if (auto) standingDetector.Begin();
            SetPrimary(auto ? null : "撮影開始！");
            advanceRequested = false;
            bool wasInPosition = false;
            while (!advanceRequested)
            {
                if (auto)
                {
                    if (!standingDetector.Available)
                    {
                        auto = false;
                        SetPrimary("撮影開始！");
                    }
                    else if (standingDetector.Ready)
                    {
                        break;
                    }
                    else if (standingDetector.InPosition != wasInPosition)
                    {
                        wasInPosition = standingDetector.InPosition;
                        SetMessage(wasInPosition ? "その場でキープ！" : Guide);
                        if (wasInPosition && !keepPlayed)
                        {
                            Voice(v => v.keep); // 前のボイスが流れていれば、終わって 0.5 秒後に流れる
                            keepPlayed = true;
                        }
                    }
                }
                await Awaitable.NextFrameAsync(ct);
            }
            advanceRequested = false;
            if (standingDetector != null) standingDetector.End();
            SetPrimary(null);
            standingGuide.SetActive(false);
        }

        // ---- 4. ガイド②ポーズの指示(タレントが見本を順に見せる) ------------
        async Awaitable PoseGuideStep(CancellationToken ct)
        {
            State = BoothState.PoseGuide;
            SetMessage("ポーズを考えて！");
            SetPrimary(null); // 見本を見せている間はボタンを出さない(Space キーでは先に進める)
            advanceRequested = false;
            Voice(v => v.thinkPose);

            int i = 0;
            float next = 0f;
            float stepStart = Time.time;
            bool decidedPlayed = false;
            float end = Time.time + guidePoseInterval * PoseLibrary.Count; // 一巡したら自動で撮影へ
            while (!advanceRequested && Time.time < end)
            {
                if (!decidedPlayed && Time.time - stepStart >= decidedVoiceDelay)
                {
                    Voice(v => v.decided); // 「決まったかな?」
                    decidedPlayed = true;
                }
                if (Time.time >= next)
                {
                    var pose = PoseLibrary.Data.poses[i % PoseLibrary.Count];
                    talent.TransitionTo(pose.ToTalentPose(), 0.35f); // ポーズ名は出さない(タレントの見本だけ見せる)
                    i++;
                    next = Time.time + guidePoseInterval;
                }
                await Awaitable.NextFrameAsync(ct);
            }
            SetPrimary(null);
            SetPoseLabel(null);
        }

        // ---- 5. ガイド③カウントダウン(タレントは専用待機モーション) -----------
        async Awaitable CountdownStep(CancellationToken ct)
        {
            State = BoothState.Countdown;
            SetMessage("ポーズをキープ!");
            if (frameRecorder != null) frameRecorder.StartRecording(); // 直近 capacity 枚だけ残る

            // 正確に 1 秒ごと。数字が変わる瞬間にそのボイスをすぐ流す(カウントダウンだけは順番待ち・0.5 秒の間隔を無視)
            var g = voice != null ? voice.voices : null;
            var counts = g != null ? new[] { g.count3, g.count2, g.count1 } : new AudioClip[0];
            idleMotion.Play(countdownSeconds);
            float end = Time.time + countdownSeconds;
            int shown = -1;
            while (Time.time < end)
            {
                int n = Mathf.CeilToInt(end - Time.time);
                if (n != shown)
                {
                    shown = n;
                    countdownText.text = n.ToString();
                    int index = counts.Length - n; // 3 → 0, 2 → 1, 1 → 2
                    if (index >= 0 && index < counts.Length) voice.PlayImmediate(counts[index]);
                }
                await Awaitable.NextFrameAsync(ct);
            }
            countdownText.text = "";
        }

        /// <summary>ガイドのボイスを順番待ちに加える(素材が無い時は何もしない)。</summary>
        void Voice(Func<GuideVoices, AudioClip> pick)
        {
            if (voice != null) voice.Play(pick(voice.voices));
        }

        // ---- 6-7. シャッター → 判定 → ポーズ確定 ---------------------------
        async Awaitable JudgeStep(CancellationToken ct)
        {
            State = BoothState.Judging;
            SetMessage("");

            // 写真と判定用フレームはカメラ映像から直接取るので、この後シャッターを画面に重ねても写り込まない
            if (LastPhoto != null) Destroy(LastPhoto);
            LastPhoto = webcam.CaptureSquare(captureSize);

            // 判定にはシャッター直前の数フレーム + シャッターの瞬間を使い、多数決でまとめる(§5-5)
            List<Texture2D> frames;
            if (frameRecorder != null)
            {
                frameRecorder.CaptureNow();
                frameRecorder.StopRecording();
                frames = frameRecorder.Recent(judgeFrameCount);
            }
            else
            {
                frames = new List<Texture2D>();
                if (LastPhoto != null) frames.Add(LastPhoto);
            }

            // 切り抜きと判定はサーバーで並行して進め、その間にシャッターを閉じる
            float t0 = Time.realtimeSinceStartup;
            Awaitable<Texture2D> cutoutTask = Segmenter != null && LastPhoto != null ? Segmenter.CutoutAsync(LastPhoto) : null;
            Awaitable<PoseJudgeResult> judgeTask = Estimator.JudgeAsync(frames);

            bool useShutter = shutter != null && shutter.HasFrames;
            if (useShutter)
            {
                await shutter.CloseAsync(ct); // 閉じきったコマで止まり、合成が終わるまで閉じたまま
            }
            else
            {
                Flash(ct);
                if (shutter != null && shutter.closeSound != null && shutter.audioSource != null)
                    shutter.audioSource.PlayOneShot(shutter.closeSound); // シャッター音だけは鳴らす
                if (LastPhoto != null)
                {
                    resultPhoto.texture = LastPhoto;
                    resultPhoto.gameObject.SetActive(true); // 判定中はライブ映像ではなく撮影した1枚を見せる
                }
            }

            LastResult = await judgeTask;
            ct.ThrowIfCancellationRequested();
            Debug.Log($"[BoothFlow] judge id={LastResult.poseId} dist={LastResult.distance:F3} fallback={LastResult.fallback} " +
                      $"latency={(Time.realtimeSinceStartup - t0) * 1000f:F0}ms");

            var pose = LastResult.fallback ? PoseLibrary.Fallback : PoseLibrary.Get(LastResult.poseId);
            if (idleMotion.enabled) idleMotion.enabled = false;
            talent.hipsOffset = Vector3.zero;
            talent.hipsRoll = 0f;
            if (useShutter)
            {
                // シャッターの裏で切り替えるので遷移アニメは不要(待ち時間を短くする)
                talent.SetImmediate(pose.ToTalentPose());
            }
            else
            {
                talent.TransitionTo(pose.ToTalentPose(), poseTransitionSeconds);
                await Awaitable.WaitForSecondsAsync(poseTransitionSeconds, ct);
            }

            // ---- 合成(§6): 背景 + 切り抜いた撮影者 + 選ばれたポーズのタレント ----
            if (lastCutout != null) Destroy(lastCutout);
            lastCutout = cutoutTask != null ? await cutoutTask : null;
            ct.ThrowIfCancellationRequested();
            await Awaitable.NextFrameAsync(ct); // タレントの RenderTexture が最終ポーズで描かれてから合成する
            if (compositor != null && LastPhoto != null)
            {
                if (LastComposite != null) Destroy(LastComposite);
                LastComposite = compositor.Compose(LastPhoto, lastCutout);
                compositeView.texture = LastComposite;
                compositeView.gameObject.SetActive(true);
                resultPhoto.gameObject.SetActive(false);
                talentView.gameObject.SetActive(false); // 写真に含まれている
            }
            if (useShutter)
            {
                Debug.Log($"[BoothFlow] シャッターを閉じていた時間 {(Time.realtimeSinceStartup - t0) * 1000f:F0}ms");
                await shutter.OpenAsync(ct); // 開くと合成した写真が見える
            }
            ShotCompleted?.Invoke(lastCutout != null);
        }

        // ---- 8. 結果表示 --------------------------------------------------
        /// <summary>結果を見せて「もう一度」(true)か「終了する」(false)を待つ。Space = もう一度。</summary>
        async Awaitable<bool> ResultStep(CancellationToken ct)
        {
            State = BoothState.Result;
            // 判定したポーズ名は出さない(写真を見てもらう)
            SetMessage("撮影完了!");
            Voice(v => v.result); // 「上手く撮れたかな?」(シャッターが開いて写真が見えてから)
            SetPrimary("もう一度", exitButton != null);
            if (exitButton != null) exitButton.gameObject.SetActive(true);

            advanceRequested = exitRequested = false;
            while (!advanceRequested && !exitRequested) await Awaitable.NextFrameAsync(ct);
            bool retry = advanceRequested;
            advanceRequested = exitRequested = false;

            SetPrimary(null);
            if (exitButton != null) exitButton.gameObject.SetActive(false);
            SetMessage("");
            return retry;
        }

        // ---- 9. 終了(あいさつのボイス → 待機画面へ) ---------------------------
        async Awaitable ExitStep(CancellationToken ct)
        {
            SetMessage("また撮ろうね！");
            if (voice != null && voice.voices.bye != null)
                await voice.PlayAndWaitEnd(voice.voices.bye, ct); // 「また撮ろうね」を流し終わってから戻る
            else
                await Awaitable.WaitForSecondsAsync(exitMessageSeconds, ct);
            SetMessage("");
            if (compositeView != null) compositeView.gameObject.SetActive(false);
            talentView.gameObject.SetActive(true);
            await SlideTalent(false, ct);
        }

        /// <summary>撮り直しの前に、結果画面の表示を消してタレントを待機の立ち姿に戻す。</summary>
        void ClearShot()
        {
            if (compositeView != null) compositeView.gameObject.SetActive(false);
            resultPhoto.gameObject.SetActive(false);
            talentView.gameObject.SetActive(true);
            talent.SetImmediate(PoseLibrary.Rest.ToTalentPose());
        }

        // ---- helpers ------------------------------------------------------
        void ResetView()
        {
            advanceRequested = exitRequested = false;
            if (exitButton != null) exitButton.gameObject.SetActive(false);
            startPanel.SetActive(false);
            standingGuide.SetActive(false);
            resultPhoto.gameObject.SetActive(false);
            if (compositeView != null) compositeView.gameObject.SetActive(false);
            talentView.gameObject.SetActive(true);
            countdownText.text = "";
            SetMessage("");
            SetPoseLabel(null);
            SetPrimary(null);
            idleMotion.enabled = false;
            talent.hipsOffset = Vector3.zero;
            talent.hipsRoll = 0f;
            talent.SetImmediate(PoseLibrary.Rest.ToTalentPose());
            talentView.anchoredPosition = HiddenTalentPos();
            flash.color = new Color(1f, 1f, 1f, 0f);
            if (shutter != null) shutter.Hide(); // 閉じている途中で Esc を押された時
            if (voice != null) voice.StopAll();
        }

        async Awaitable WaitForAdvance(CancellationToken ct)
        {
            advanceRequested = false;
            while (!advanceRequested) await Awaitable.NextFrameAsync(ct);
            advanceRequested = false;
        }

        Vector2 ShownTalentPos => look != null ? look.TalentShownPosition : talentShownPos;

        Vector2 HiddenTalentPos() => ShownTalentPos + new Vector2(talentView.rect.width + 200f, 0f);

        async Awaitable SlideTalent(bool show, CancellationToken ct)
        {
            Vector2 from = show ? HiddenTalentPos() : ShownTalentPos;
            Vector2 to = show ? ShownTalentPos : HiddenTalentPos();
            for (float t = 0f; t < talentSlideSeconds; t += Time.deltaTime)
            {
                talentView.anchoredPosition = Vector2.Lerp(from, to, Mathf.SmoothStep(0f, 1f, t / talentSlideSeconds));
                await Awaitable.NextFrameAsync(ct);
            }
            talentView.anchoredPosition = to;
        }

        async void Flash(CancellationToken ct)
        {
            const float duration = 0.3f;
            try
            {
                for (float t = 0f; t < duration; t += Time.deltaTime)
                {
                    flash.color = new Color(1f, 1f, 1f, 1f - t / duration);
                    await Awaitable.NextFrameAsync(ct);
                }
            }
            catch (OperationCanceledException) { }
            flash.color = new Color(1f, 1f, 1f, 0f);
        }

        [Serializable]
        public struct MessageStyle
        {
            public int fontSize;
            public Color color;
            public Color outlineColor;
            public Vector2 outlineDistance;
        }

        [Header("Message styles")]
        [Tooltip("画面上部の案内文すべて(立ち位置・見本・カウントダウン・撮影完了)。明るい背景やカメラ映像の上でも読めるよう濃いピンク + 白縁")]
        public MessageStyle highlightMessageStyle = new MessageStyle
        {
            fontSize = 84,
            color = new Color(0.86f, 0.18f, 0.45f),
            outlineColor = Color.white,
            outlineDistance = new Vector2(4f, -4f),
        };

        /// <summary>案内文を出す。style 省略時は highlightMessageStyle(すべての案内文で見た目を揃える)。</summary>
        void SetMessage(string text, MessageStyle? style = null)
        {
            var outline = messageText.GetComponent<Outline>();
            var s = style ?? highlightMessageStyle;
            messageText.text = text;
            messageText.fontSize = s.fontSize;
            // 長い案内文は表示枠に収まるまで自動で縮める(短い文は指定の大きさのまま)
            messageText.resizeTextForBestFit = true;
            messageText.resizeTextMinSize = 28;
            messageText.resizeTextMaxSize = s.fontSize;
            messageText.verticalOverflow = VerticalWrapMode.Truncate;
            messageText.color = s.color;
            if (outline != null)
            {
                outline.effectColor = s.outlineColor;
                outline.effectDistance = s.outlineDistance;
            }
        }

        void SetPoseLabel(string text)
        {
            poseLabelText.text = text ?? "";
            poseLabelText.gameObject.SetActive(!string.IsNullOrEmpty(text));
        }

        /// <summary>中央下のボタン。besideExit=true の時は「終了する」ボタンと横に並ぶよう左へずらす。</summary>
        void SetPrimary(string label, bool besideExit = false)
        {
            primaryButton.gameObject.SetActive(label != null);
            if (label != null) primaryButtonLabel.text = label;
            var r = (RectTransform)primaryButton.transform;
            r.anchoredPosition = new Vector2(besideExit ? -buttonPairOffset : 0f, r.anchoredPosition.y);
        }
    }
}
