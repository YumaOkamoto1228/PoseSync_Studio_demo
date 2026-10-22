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
        public PhotoCompositor compositor;
        [Tooltip("判定に使うフレーム数(シャッター直前〜シャッター)")]
        public int judgeFrameCount = 5;

        [Header("Timing")]
        public float talentSlideSeconds = 0.5f;
        public float guidePoseInterval = 2f;
        public float countdownSeconds = 3f;
        public float poseTransitionSeconds = 0.4f;
        public int captureSize = 1080;

        [Header("UI")]
        public GameObject startPanel;
        public Button startButton;
        public Button primaryButton;
        public Text primaryButtonLabel;
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

        bool advanceRequested;
        CancellationTokenSource sessionCts;
        Vector2 talentShownPos;

        void Awake()
        {
            if (startButton != null) startButton.onClick.AddListener(RequestAdvance);
            if (primaryButton != null) primaryButton.onClick.AddListener(RequestAdvance);
            if (talentView != null) talentShownPos = talentView.anchoredPosition;
            if (Estimator == null) Debug.LogError("[BoothFlow] poseEstimator に IPoseEstimator がありません", this);
        }

        void Start() => Restart();

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (kb.spaceKey.wasPressedThisFrame) RequestAdvance();
                if (kb.escapeKey.wasPressedThisFrame)
                {
                    if (State != BoothState.Idle && State != BoothState.Result) SessionAborted?.Invoke(State);
                    Restart();
                }
            }
            if (statusText != null)
                statusText.text = $"{State}  cam:{(webcam != null && webcam.IsReady ? webcam.DeviceName : "not ready")}  judge:{Estimator?.DisplayName}{StatusSuffix}";
        }

        public void RequestAdvance() => advanceRequested = true;

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
                await StandingStep(ct);
                await PoseGuideStep(ct);
                await CountdownStep(ct);
                await JudgeStep(ct);
                await ResultStep(ct);
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
            await SlideTalent(true, ct);
            standingGuide.SetActive(true);
            SetMessage("枠の中に、頭から腰までが入るように立ってね");
            SetPrimary("撮影開始！");
            await WaitForAdvance(ct);
            standingGuide.SetActive(false);
        }

        // ---- 4. ガイド②ポーズの指示(タレントが見本を順に見せる) ------------
        async Awaitable PoseGuideStep(CancellationToken ct)
        {
            State = BoothState.PoseGuide;
            SetMessage("好きなポーズをとろう!");
            SetPrimary(null); // 見本を見せている間はボタンを出さない(Space キーでは先に進める)
            advanceRequested = false;

            int i = 0;
            float next = 0f;
            float end = Time.time + guidePoseInterval * PoseLibrary.Count; // 一巡したら自動で撮影へ
            while (!advanceRequested && Time.time < end)
            {
                if (Time.time >= next)
                {
                    var pose = PoseLibrary.Data.poses[i % PoseLibrary.Count];
                    talent.TransitionTo(pose.ToTalentPose(), 0.35f);
                    SetPoseLabel($"見本: {pose.label}");
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
            idleMotion.Play(countdownSeconds);
            if (frameRecorder != null) frameRecorder.StartRecording(); // 直近 capacity 枚だけ残る

            float end = Time.time + countdownSeconds;
            while (Time.time < end)
            {
                countdownText.text = Mathf.CeilToInt(end - Time.time).ToString();
                await Awaitable.NextFrameAsync(ct);
            }
            countdownText.text = "";
        }

        // ---- 6-7. シャッター → 判定 → ポーズ確定 ---------------------------
        async Awaitable JudgeStep(CancellationToken ct)
        {
            State = BoothState.Judging;
            SetMessage("");

            if (LastPhoto != null) Destroy(LastPhoto);
            LastPhoto = webcam.CaptureSquare(captureSize);
            Flash(ct);
            if (LastPhoto != null)
            {
                resultPhoto.texture = LastPhoto;
                resultPhoto.gameObject.SetActive(true); // 判定中はライブ映像ではなく撮影した1枚を見せる
            }

            // 切り抜きは判定と並行して進める(撮影した1枚だけが対象、§6)
            Awaitable<Texture2D> cutoutTask = Segmenter != null && LastPhoto != null ? Segmenter.CutoutAsync(LastPhoto) : null;

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

            float t0 = Time.realtimeSinceStartup;
            LastResult = await Estimator.JudgeAsync(frames);
            ct.ThrowIfCancellationRequested();
            Debug.Log($"[BoothFlow] judge id={LastResult.poseId} dist={LastResult.distance:F3} fallback={LastResult.fallback} " +
                      $"latency={(Time.realtimeSinceStartup - t0) * 1000f:F0}ms");

            var pose = LastResult.fallback ? PoseLibrary.Fallback : PoseLibrary.Get(LastResult.poseId);
            if (idleMotion.enabled) idleMotion.enabled = false;
            talent.hipsOffset = Vector3.zero;
            talent.hipsRoll = 0f;
            talent.TransitionTo(pose.ToTalentPose(), poseTransitionSeconds);
            await Awaitable.WaitForSecondsAsync(poseTransitionSeconds, ct);

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
            ShotCompleted?.Invoke(lastCutout != null);
        }

        // ---- 8. 結果表示 --------------------------------------------------
        async Awaitable ResultStep(CancellationToken ct)
        {
            State = BoothState.Result;
            // 判定したポーズ名は出さない(写真を見てもらう)
            SetMessage("撮影完了!", resultMessageStyle);
            SetPrimary("もう一度");
            await WaitForAdvance(ct);
            SetMessage("");
            if (compositeView != null) compositeView.gameObject.SetActive(false);
            talentView.gameObject.SetActive(true);
            await SlideTalent(false, ct);
        }

        // ---- helpers ------------------------------------------------------
        void ResetView()
        {
            advanceRequested = false;
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
        [Tooltip("結果画面の「撮影完了!」。明るい背景の上でも読めるよう濃いピンク + 白縁")]
        public MessageStyle resultMessageStyle = new MessageStyle
        {
            fontSize = 84,
            color = new Color(0.86f, 0.18f, 0.45f),
            outlineColor = Color.white,
            outlineDistance = new Vector2(4f, -4f),
        };

        MessageStyle? defaultMessageStyle;

        /// <summary>style 省略時は通常の見た目(シーンで設定した白文字+黒縁)に戻す。</summary>
        void SetMessage(string text, MessageStyle? style = null)
        {
            var outline = messageText.GetComponent<Outline>();
            defaultMessageStyle ??= new MessageStyle
            {
                fontSize = messageText.fontSize,
                color = messageText.color,
                outlineColor = outline != null ? outline.effectColor : Color.black,
                outlineDistance = outline != null ? outline.effectDistance : Vector2.zero,
            };
            var s = style ?? defaultMessageStyle.Value;
            messageText.text = text;
            messageText.fontSize = s.fontSize;
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

        void SetPrimary(string label)
        {
            primaryButton.gameObject.SetActive(label != null);
            if (label != null) primaryButtonLabel.text = label;
        }
    }
}
