using System.Collections.Generic;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// 同じポーズでも「直前の姿勢」によって手や頭の位置が変わらないかを調べる。
    /// 見本(直前=別の見本ポーズ)と撮影時(直前=待機モーションの拳)で位置がずれる問題の検出用。
    /// 直前の姿勢を数フレーム適用 → 対象ポーズを数フレーム適用 → 手首・頭の画面位置を比べる。
    /// </summary>
    public static class PoseConsistencyCheck
    {
        const int Frames = 10;

        [MenuItem("PoseSync/Check Pose Consistency")]
        public static void Run()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PoseSync/Scenes/Booth.unity");
            var flow = Object.FindFirstObjectByType<BoothFlow>();
            var rig = flow.talent;
            var cam = Object.FindFirstObjectByType<TalentFraming>().GetComponent<Camera>();
            PoseLibrary.Reload();
            rig.Initialize();

            var previous = new List<(string name, TalentPose pose)> { ("rest", PoseLibrary.Rest.ToTalentPose()), ("idle_fists", IdleFists()) };
            foreach (var p in PoseLibrary.Data.poses) previous.Add((p.key, p.ToTalentPose()));
            previous.Add((PoseLibrary.Fallback.key, PoseLibrary.Fallback.ToTalentPose()));

            var targets = new List<PoseDefinition>(PoseLibrary.Data.poses) { PoseLibrary.Fallback };
            var sb = new StringBuilder("[PoseSync] consistency (画面幅=1 とした時の最大ずれ)\n");
            float worst = 0f;
            foreach (var target in targets)
            {
                var samples = new List<Vector3[]>();
                foreach (var (_, prev) in previous)
                {
                    Apply(rig, prev, Frames);
                    Apply(rig, target.ToTalentPose(), Frames);
                    samples.Add(new[] { Screen(cam, rig.leftHand), Screen(cam, rig.rightHand), Screen(cam, rig.head), Screen(cam, rig.hips) });
                }
                float[] maxDev = new float[4];
                for (int k = 0; k < 4; k++)
                    foreach (var a in samples)
                        foreach (var b in samples)
                            maxDev[k] = Mathf.Max(maxDev[k], Vector2.Distance(a[k], b[k]));
                worst = Mathf.Max(worst, Mathf.Max(maxDev));
                sb.AppendLine($"  {target.key,-13} 左手={maxDev[0]:F4} 右手={maxDev[1]:F4} 頭={maxDev[2]:F4} 腰={maxDev[3]:F4}");
            }
            sb.AppendLine($"  worst={worst:F4}");
            Debug.Log(sb.ToString());
            EditorSceneManager.OpenScene(scene.path); // 変更を破棄
        }

        static void Apply(TalentRig rig, TalentPose pose, int frames)
        {
            rig.SetImmediate(pose);
            for (int i = 0; i < frames; i++) rig.ApplyBones();
        }

        static Vector3 Screen(Camera cam, Transform t) => cam.WorldToViewportPoint(t.position);

        static TalentPose IdleFists()
        {
            var fist = HandShape.FromName("fist");
            var l = new Vector3(0.35f, 0.75f, 0.55f).normalized;
            var r = new Vector3(-0.35f, 0.75f, 0.55f).normalized;
            var rest = PoseLibrary.Rest.ToTalentPose();
            rest.left = new ArmPose { upper = new Vector3(-0.30f, -0.85f, 0.43f).normalized, lower = l, fingers = l, palm = Vector3.right, shape = fist };
            rest.right = new ArmPose { upper = new Vector3(0.30f, -0.85f, 0.43f).normalized, lower = r, fingers = r, palm = Vector3.left, shape = fist };
            return rest;
        }
    }
}
