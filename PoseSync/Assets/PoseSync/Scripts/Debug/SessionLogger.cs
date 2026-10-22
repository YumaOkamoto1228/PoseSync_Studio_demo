using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using UnityEngine;
using UnityEngine.InputSystem;

namespace PoseSync
{
    /// <summary>
    /// 簡易ユーザーテスト用の体験ログ(§10)。1回の体験ごとに data/sessions/日付.jsonl へ1行追記する。
    /// - 各ステップの所要時間(ガイドで迷った箇所の目安)、途中でやめた場所(離脱点)
    /// - 判定結果(ポーズ・距離・フレームごとの判定・遅延)、切り抜きの成否、保存した写真
    /// - 記録係が結果画面で押した「参加者がとったつもりのポーズ」(数字キー 1〜4 / 0 = どれでもない)
    /// 待機画面で N キーを押すと次の参加者(P01 → P02 …)に切り替える。
    /// </summary>
    public class SessionLogger : MonoBehaviour
    {
        public BoothFlow flow;
        public PhotoCompositor compositor;
        public bool logging = true;
        [Tooltip("参加者 ID。待機画面で N キーを押すと末尾の番号が1つ進む")]
        public string participant = "P01";
        [Tooltip("記録に残すメモ(グッズの有無など)。Play 中に書き換えてよい")]
        public string note = "";

        const int Unset = -99;

        [Serializable]
        class StepTime
        {
            public string step;
            public float seconds;
        }

        [Serializable]
        class Record
        {
            public string session_id;
            public string participant;
            public string note;
            public string started_at;
            public List<StepTime> steps = new();
            public bool completed;
            public bool aborted;
            public string aborted_at;
            public int result_id = Unset;
            public string result_key;
            public float distance;
            public bool fallback;
            public string reason;
            public string[] votes;
            public float latency_ms;
            public string judge_source;
            public string log_id;
            public bool cutout_ok;
            public string photo;
            public int intended = Unset;  // Unset = 記録係が入力しなかった
            public string intended_key;
            public bool hit;              // intended と result が一致したか(intended がある時だけ意味を持つ)
        }

        Record current;
        BoothState lastState;
        float stateEnteredAt;

        [Tooltip("ログファイル名の末尾(自動テストでは _probe にして本番の記録と分ける)")]
        public string fileSuffix = "";

        public string LogPath => Path.GetFullPath(Path.Combine(Application.dataPath, $"../../data/sessions/{DateTime.Now:yyyyMMdd}{fileSuffix}.jsonl"));

        /// <summary>参加者がとったつもりのポーズを記録する(-1 = どれでもない)。結果画面の数字キーと同じ。</summary>
        public void SetIntended(int poseId)
        {
            if (current == null) return;
            current.intended = poseId;
            current.intended_key = poseId < 0 ? PoseLibrary.Fallback.key : PoseLibrary.Get(poseId).key;
            current.hit = current.intended == current.result_id;
        }

        void OnEnable()
        {
            flow.StateChanged += OnStateChanged;
            flow.SessionAborted += OnAborted;
            flow.ShotCompleted += OnShotCompleted;
        }

        void OnDisable()
        {
            flow.StateChanged -= OnStateChanged;
            flow.SessionAborted -= OnAborted;
            flow.ShotCompleted -= OnShotCompleted;
            Finish();
        }

        void OnStateChanged(BoothState s)
        {
            if (current != null)
                current.steps.Add(new StepTime { step = lastState.ToString(), seconds = Round(Time.time - stateEnteredAt) });

            if (s == BoothState.Idle) Finish();
            else if (current == null && s == BoothState.Standing) Begin();

            lastState = s;
            stateEnteredAt = Time.time;
        }

        void Begin()
        {
            current = new Record
            {
                session_id = DateTime.Now.ToString("yyyyMMdd_HHmmss"),
                participant = participant,
                note = note,
                started_at = DateTime.Now.ToString("s"),
            };
        }

        void OnAborted(BoothState at)
        {
            if (current == null) return;
            current.aborted = true;
            current.aborted_at = at.ToString();
        }

        void OnShotCompleted(bool cutoutOk)
        {
            if (current == null) return;
            var r = flow.LastResult;
            current.completed = true;
            current.result_id = r.fallback ? -1 : r.poseId;
            current.result_key = r.fallback ? PoseLibrary.Fallback.key : PoseLibrary.Get(r.poseId).key;
            current.distance = Round(r.distance);
            current.fallback = r.fallback;
            current.reason = r.reason;
            current.votes = r.votes;
            current.latency_ms = Round(r.latencyMs);
            current.judge_source = r.source;
            current.log_id = r.logId;
            current.cutout_ok = cutoutOk;
            current.photo = compositor != null ? compositor.LastSavedPath : "";
        }

        void Finish()
        {
            if (current == null) return;
            if (logging)
            {
                try
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                    File.AppendAllText(LogPath, JsonUtility.ToJson(current) + "\n");
                    Debug.Log($"[SessionLogger] {current.participant} {current.result_key} 意図={current.intended_key ?? "-"} 中断={current.aborted_at ?? "-"} → {LogPath}");
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[SessionLogger] 記録に失敗: {e.Message}");
                }
            }
            current = null;
        }

        void Update()
        {
            var kb = Keyboard.current;
            if (kb != null)
            {
                if (flow.State == BoothState.Result && current != null)
                {
                    int? id = kb.digit1Key.wasPressedThisFrame ? 0 : kb.digit2Key.wasPressedThisFrame ? 1
                        : kb.digit3Key.wasPressedThisFrame ? 2 : kb.digit4Key.wasPressedThisFrame ? 3
                        : kb.digit0Key.wasPressedThisFrame ? -1 : (int?)null;
                    if (id.HasValue && id.Value < PoseLibrary.Count) SetIntended(id.Value);
                }
                if (flow.State == BoothState.Idle && kb.nKey.wasPressedThisFrame) participant = NextParticipant(participant);
            }

            string intended = current == null ? "" : current.intended == Unset ? "  意図:未入力(1-4/0)" : $"  意図:{current.intended_key}";
            flow.StatusSuffix = logging ? $"  |  記録 {participant}{intended}" : "";
        }

        static string NextParticipant(string id)
        {
            var m = Regex.Match(id, @"^(.*?)(\d+)$");
            if (!m.Success) return id + "2";
            int n = int.Parse(m.Groups[2].Value) + 1;
            return m.Groups[1].Value + n.ToString().PadLeft(m.Groups[2].Value.Length, '0');
        }

        static float Round(float v) => Mathf.Round(v * 1000f) / 1000f;
    }
}
