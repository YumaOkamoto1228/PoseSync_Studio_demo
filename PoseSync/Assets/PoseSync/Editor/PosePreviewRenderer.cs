using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// Booth シーンのタレントに各ポーズを適用し、タレントカメラの画像を PNG で書き出す。
    /// ポーズ定義(poses.json)を直した時に、Play せず見た目を確認するためのもの。
    /// 出力先: (プロジェクトの1つ上)/docs/pose_previews/
    /// </summary>
    public static class PosePreviewRenderer
    {
        [MenuItem("PoseSync/Render Pose Previews")]
        public static void Render()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PoseSync/Scenes/Booth.unity");
            var rig = Object.FindFirstObjectByType<TalentRig>();
            var framing = Object.FindFirstObjectByType<TalentFraming>();
            var cam = framing.GetComponent<Camera>();

            PoseLibrary.Reload();
            rig.Initialize();
            framing.Setup();

            string outDir = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/pose_previews"));
            Directory.CreateDirectory(outDir);

            Save(rig, cam, PoseLibrary.Fallback, outDir);
            foreach (var pose in PoseLibrary.Data.poses) Save(rig, cam, pose, outDir);

            Debug.Log($"[PoseSync] previews: {outDir} (talent={rig.name}, L={rig.leftUpper.name}, R={rig.rightUpper.name})");
            EditorSceneManager.OpenScene(scene.path); // ボーンを書き換えたのでシーンを読み直して破棄
        }

        static void Save(TalentRig rig, Camera cam, PoseDefinition pose, string dir)
        {
            rig.SetImmediate(pose.ToTalentPose());
            rig.ApplyBones();
            if (rig.TryGetComponent(out TalentExpression expression))
            {
                expression.Initialize();
                expression.Apply();
            }
            Vector3 l = cam.WorldToViewportPoint(rig.leftHand.position), r = cam.WorldToViewportPoint(rig.rightHand.position);
            Debug.Log($"[PoseSync] {pose.key}: screenLeft hand=({l.x:F2},{l.y:F2}) screenRight hand=({r.x:F2},{r.y:F2})");
            RenderWithBakedSkin(rig, cam);
            ReadAndSave(cam, pose, dir);
        }

        /// <summary>エディタ(非 Play)ではスキニングが描画に反映されないので、変形済みメッシュを焼いて代わりに描く。</summary>
        public static void RenderWithBakedSkin(TalentRig rig, Camera cam)
        {
            var baked = new System.Collections.Generic.List<GameObject>();
            var smrs = rig.GetComponentsInChildren<SkinnedMeshRenderer>();
            foreach (var smr in smrs)
            {
                var mesh = new Mesh();
                smr.BakeMesh(mesh, true);
                var go = new GameObject("baked") { hideFlags = HideFlags.HideAndDontSave };
                go.transform.SetPositionAndRotation(smr.transform.position, smr.transform.rotation);
                go.AddComponent<MeshFilter>().sharedMesh = mesh;
                go.AddComponent<MeshRenderer>().sharedMaterials = smr.sharedMaterials;
                smr.enabled = false;
                baked.Add(go);
            }
            cam.Render();
            foreach (var go in baked)
            {
                Object.DestroyImmediate(go.GetComponent<MeshFilter>().sharedMesh);
                Object.DestroyImmediate(go);
            }
            foreach (var smr in smrs) smr.enabled = true;
        }

        static void ReadAndSave(Camera cam, PoseDefinition pose, string dir)
        {
            var rt = cam.targetTexture;
            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(rt.width, rt.height, TextureFormat.RGBA32, false);
            tex.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string path = Path.Combine(dir, $"{pose.id + 1:D2}_{pose.key}.png");
            try
            {
                File.WriteAllBytes(path, tex.EncodeToPNG());
            }
            catch (IOException)
            {
                // ビューアで開いているとロックされるので別名で残す
                path = Path.ChangeExtension(path, ".latest.png");
                File.WriteAllBytes(path, tex.EncodeToPNG());
                Debug.LogWarning($"[PoseSync] 元ファイルが使用中のため {path} に保存しました");
            }
            Object.DestroyImmediate(tex);
        }
    }
}
