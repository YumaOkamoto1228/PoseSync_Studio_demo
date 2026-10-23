using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace PoseSync
{
    /// <summary>
    /// Play 中に体験フローを自動で進め、「見本で見せた時」と「撮影結果で同じポーズになった時」の
    /// タレントの手・頭・腰の画面位置を記録して比べる(見本と撮影時で位置がずれる問題の調査用)。
    /// 判定は DebugPoseEstimator で各ポーズを順に強制する。エディタの PoseSync > Run Flow Probe から使う。
    /// </summary>
    public class FlowProbe : MonoBehaviour
    {
        public static bool Done { get; private set; }
        [Tooltip("カメラの前に人がいなくても合成を確認できるよう、楕円形に切り抜いたことにする")]
        public static bool useFakeCutout = true;
        public static string Report { get; private set; } = "";

        BoothFlow flow;
        Camera cam;
        readonly Dictionary<string, Vector3[]> guide = new();
        readonly StringBuilder log = new();

        IEnumerator Start()
        {
            flow = FindFirstObjectByType<BoothFlow>();
            cam = FindFirstObjectByType<TalentFraming>().GetComponent<Camera>();
            var debug = flow.GetComponent<DebugPoseEstimator>();
            flow.poseEstimator = debug; // 判定サーバーの有無に関係なく結果を固定する
            if (useFakeCutout) flow.segmenter = gameObject.AddComponent<FakeSegmenter>();
            var logger = flow.GetComponent<SessionLogger>();
            if (logger != null)
            {
                logger.fileSuffix = "_probe"; // 本番の体験ログと混ぜない
                logger.participant = "PROBE";
            }

            var targets = new List<PoseDefinition>(PoseLibrary.Data.poses) { PoseLibrary.Fallback };
            for (int run = 0; run < targets.Count; run++)
            {
                var target = targets[run];
                yield return WaitState(BoothState.Idle);
                flow.RequestAdvance();                       // スタート
                yield return WaitState(BoothState.Standing);
                yield return new WaitForSeconds(flow.talentSlideSeconds + 0.3f);
                flow.RequestAdvance();                       // 撮影開始
                yield return WaitState(BoothState.PoseGuide);

                // 見本: 各ポーズを表示し終わる直前の位置を記録(1周目だけ)
                for (int i = 0; i < PoseLibrary.Count; i++)
                {
                    yield return new WaitForSeconds(flow.guidePoseInterval * (i == 0 ? 0.8f : 1f));
                    if (run == 0) guide[PoseLibrary.Data.poses[i].key] = Sample();
                }

                yield return WaitState(BoothState.Countdown);
                debug.Force(target.id);
                yield return WaitState(BoothState.Result);
                yield return new WaitForSeconds(1.0f);
                var result = Sample();
                Compare(target.key, result);
                if (logger != null) logger.SetIntended(target.id); // 記録係の数字キー入力の代わり
                var comp = flow.compositor;
                log.AppendLine($"  合成: {(flow.LastComposite != null ? $"{flow.LastComposite.width}x{flow.LastComposite.height} → {comp.LastSavedPath}" : "なし(カメラ映像なし?)")}");
                if (run == 0)
                {
                    flow.RequestAdvance();                   // もう一度 → 立ち位置ガイドに戻ること
                    yield return WaitState(BoothState.Standing);
                    log.AppendLine($"  もう一度 → {flow.State}");
                    flow.RequestAdvance();                   // ボタン操作で撮影へ(自動判定はサーバー無しでは使えない)
                    yield return WaitState(BoothState.PoseGuide);
                    yield return WaitState(BoothState.Countdown);
                    debug.Force(target.id);
                    yield return WaitState(BoothState.Result);
                    yield return new WaitForSeconds(0.5f);
                }
                flow.RequestExit();                          // 終了する → あいさつ → 待機画面
                yield return WaitState(BoothState.Idle);
                log.AppendLine($"  終了する → {flow.State}");
                yield return new WaitForSeconds(flow.talentSlideSeconds + 0.2f);
            }
            Report = log.ToString();
            Debug.Log("[FlowProbe]\n" + Report);
            Done = true;
        }

        IEnumerator WaitState(BoothState s)
        {
            float limit = Time.time + 30f;
            while (flow.State != s && Time.time < limit) yield return null;
        }

        Vector3[] Sample()
        {
            var rig = flow.talent;
            var view = flow.talentView;
            return new[]
            {
                cam.WorldToViewportPoint(rig.leftHand.position), cam.WorldToViewportPoint(rig.rightHand.position),
                cam.WorldToViewportPoint(rig.head.position), cam.WorldToViewportPoint(rig.hips.position),
                new Vector3(view.anchoredPosition.x, view.anchoredPosition.y, view.sizeDelta.x),
                new Vector3(rig.hipsOffset.y, rig.hipsRoll, rig.IsTransitioning ? 1 : 0),
            };
        }

        void Compare(string key, Vector3[] result)
        {
            if (!guide.TryGetValue(key, out var g))
            {
                log.AppendLine($"{key,-13} (見本なし) 左手={(Vector2)result[0]} 右手={(Vector2)result[1]} 頭={(Vector2)result[2]} 腰={(Vector2)result[3]} view={result[4]} hipsOffY/roll/trans={result[5]}");
                return;
            }
            string[] names = { "左手", "右手", "頭", "腰" };
            var sb = new StringBuilder($"{key,-13}");
            for (int k = 0; k < 4; k++) sb.Append($" {names[k]}Δ={Vector2.Distance(g[k], result[k]):F4}");
            sb.Append($"  view 見本={g[4]} 撮影={result[4]}  hipsOffY/roll/trans 見本={g[5]} 撮影={result[5]}");
            log.AppendLine(sb.ToString());
        }
    }
}
