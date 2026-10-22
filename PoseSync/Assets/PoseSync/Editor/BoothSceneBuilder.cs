using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// メニュー「PoseSync」からブースのシーンを一式生成する。
    /// シーンを手で組まずにコードで再生成できるようにしておく(UI 配置を変えたい時もここを直す)。
    /// </summary>
    public static class BoothSceneBuilder
    {
        const string Root = "Assets/PoseSync";
        const string ScenePath = Root + "/Scenes/Booth.unity";
        const string MaterialPath = Root + "/Materials/Mannequin.mat";
        const float StageSize = 1080f;

        [MenuItem("PoseSync/Build Booth Scene")]
        public static void BuildMenu()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            BuildWithTalent(EditorPrefs.GetString(TalentPrefKey, "ayame"));
        }

        const string TalentPrefKey = "PoseSync.TalentKeyword";

        /// <summary>
        /// バッチモード用: Unity.exe -batchmode -quit -executeMethod PoseSync.EditorTools.BoothSceneBuilder.BuildFromCommandLine [-talent Lamy]
        /// -talent には VRM のファイル名の一部を指定する(省略時は最初に見つかった VRM、無ければマネキン)。
        /// </summary>
        public static void BuildFromCommandLine()
        {
            var args = System.Environment.GetCommandLineArgs();
            int i = System.Array.IndexOf(args, "-talent");
            BuildWithTalent(i >= 0 && i + 1 < args.Length ? args[i + 1] : "");
        }

        static void BuildWithTalent(string keyword)
        {
            BuildScene();
            var prefab = FindVrmPrefab(keyword);
            if (prefab == null)
            {
                Debug.LogWarning($"[PoseSync] VRM が見つからないのでマネキンのままにします (keyword='{keyword}')");
                return;
            }
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            SetTalent(instance);
            EditorSceneManager.SaveScene(instance.scene);
        }

        static GameObject FindVrmPrefab(string keyword)
        {
            foreach (var guid in AssetDatabase.FindAssets("", new[] { "Assets/ThirdParty/VRM" }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.EndsWith(".vrm", System.StringComparison.OrdinalIgnoreCase)) continue;
                if (keyword.Length > 0 && !Path.GetFileName(path).Contains(keyword, System.StringComparison.OrdinalIgnoreCase)) continue;
                var go = AssetDatabase.LoadMainAssetAtPath(path) as GameObject;
                Debug.Log($"[PoseSync] VRM: {path} -> {(go != null ? go.name : "(GameObject として読めない)")}");
                if (go != null) return go;
            }
            return null;
        }

        [MenuItem("PoseSync/Use Selected As Talent")]
        public static void UseSelectedAsTalent()
        {
            var selected = Selection.activeGameObject;
            var animator = selected != null ? selected.GetComponentInChildren<Animator>() : null;
            if (animator == null || !animator.isHuman)
            {
                EditorUtility.DisplayDialog("PoseSync", "Humanoid の Animator を持つモデル(VRM など)をシーンに置いて選択してください。", "OK");
                return;
            }
            if (Object.FindFirstObjectByType<BoothFlow>() == null)
            {
                EditorUtility.DisplayDialog("PoseSync", "先に PoseSync > Build Booth Scene でシーンを作ってください。", "OK");
                return;
            }
            SetTalent(selected);
            // 次回 Build Booth Scene でも同じモデルを使う
            EditorPrefs.SetString(TalentPrefKey, selected.name.Split('_')[0]);
        }

        static void SetTalent(GameObject selected)
        {
            var oldRig = Object.FindFirstObjectByType<TalentRig>(FindObjectsInactive.Include);
            var flow = Object.FindFirstObjectByType<BoothFlow>();
            var framing = Object.FindFirstObjectByType<TalentFraming>();

            Undo.RegisterFullObjectHierarchyUndo(selected, "Use As Talent");
            selected.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity); // +Z を向かせる(VRM の正面)

            if (!selected.TryGetComponent(out TalentRig rig)) rig = Undo.AddComponent<TalentRig>(selected);
            rig.autoFromAnimator = true;
            rig.viewCamera = framing.GetComponent<Camera>();

            if (!selected.TryGetComponent(out TalentExpression _)) Undo.AddComponent<TalentExpression>(selected);
            if (!selected.TryGetComponent(out ExcitedIdleMotion idle)) idle = Undo.AddComponent<ExcitedIdleMotion>(selected);
            idle.rig = rig;
            idle.enabled = false;

            Undo.RecordObject(flow, "Use As Talent");
            flow.talent = rig;
            flow.idleMotion = idle;
            Undo.RecordObject(framing, "Use As Talent");
            framing.rig = rig;

            if (oldRig != null && oldRig != rig) oldRig.gameObject.SetActive(false);
            EditorSceneManager.MarkSceneDirty(selected.scene);
            Debug.Log($"[PoseSync] タレントを {selected.name} に差し替えました");
        }

        static void BuildScene()
        {
            EnsureFolder(Root + "/Scenes");
            EnsureFolder(Root + "/Materials");

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.4f, 0.4f, 0.45f);
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 0.9f;
            light.shadows = LightShadows.Soft;
            light.transform.rotation = Quaternion.Euler(30f, 160f, 0f); // 正面やや上から

            // UI だけを描くメインカメラ(3D は何も映さない)
            var mainCam = new GameObject("Main Camera").AddComponent<Camera>();
            mainCam.tag = "MainCamera";
            mainCam.clearFlags = CameraClearFlags.SolidColor;
            mainCam.backgroundColor = Color.black;
            mainCam.cullingMask = 0;
            mainCam.gameObject.AddComponent<UniversalAdditionalCameraData>();

            // 仮マネキン(VRM が入ったら PoseSync > Use Selected As Talent で差し替える)
            var rig = BuildMannequin();

            var talentCam = new GameObject("Talent Camera").AddComponent<Camera>();
            talentCam.transform.SetPositionAndRotation(new Vector3(0f, 1.3f, 5f), Quaternion.Euler(0f, 180f, 0f));
            talentCam.depth = -2;
            var talentCamData = talentCam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            talentCamData.renderPostProcessing = false; // 背景のアルファを残す
            rig.viewCamera = talentCam;

            var idle = rig.gameObject.AddComponent<ExcitedIdleMotion>();
            idle.rig = rig;
            idle.enabled = false;

            // ---- UI ----
            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(OsFontApplier));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(StageSize, StageSize);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.Expand;
            new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));

            // どの Game ビュー比率でも 1:1 の舞台を中央に置く
            var stage = NewRect("Stage", canvasGo.transform);
            stage.sizeDelta = new Vector2(StageSize, StageSize);
            var fitter = stage.gameObject.AddComponent<AspectRatioFitter>();
            fitter.aspectMode = AspectRatioFitter.AspectMode.FitInParent;
            fitter.aspectRatio = 1f;
            stage.gameObject.AddComponent<RectMask2D>();

            var webcamView = NewRawImage("WebcamView", stage);
            Stretch(webcamView.rectTransform);

            var resultPhoto = NewRawImage("ResultPhoto", stage);
            Stretch(resultPhoto.rectTransform);
            resultPhoto.gameObject.SetActive(false);

            var (standingGuide, guideFrame, guideGraphics) = BuildStandingGuide(stage);

            // 位置(体の中心の横位置)と大きさは BoothLook が毎フレーム決める
            var talentView = NewRawImage("TalentView", stage);
            var tv = talentView.rectTransform;
            tv.anchorMin = tv.anchorMax = Vector2.zero;
            tv.pivot = new Vector2(0.5f, 0f);
            tv.sizeDelta = new Vector2(1100f, 1100f);
            tv.anchoredPosition = new Vector2(0.78f * StageSize, 0f);

            // 合成した最終写真(背景・撮影者・タレント入り)。結果画面でだけ表示する
            var compositeView = NewRawImage("CompositeView", stage);
            Stretch(compositeView.rectTransform);
            compositeView.gameObject.SetActive(false);

            var poseLabel = NewText("PoseLabel", stage, "", 52, TextAnchor.MiddleCenter);
            SetAnchors(poseLabel.rectTransform, new Vector2(0.5f, 0.02f), new Vector2(1f, 0.11f));
            poseLabel.gameObject.SetActive(false);

            var message = NewText("Message", stage, "", 52, TextAnchor.MiddleCenter);
            SetAnchors(message.rectTransform, new Vector2(0.03f, 0.87f), new Vector2(0.97f, 0.99f));
            message.horizontalOverflow = HorizontalWrapMode.Wrap; // 長い案内文が画面の外にはみ出さないよう折り返す

            var countdown = NewText("Countdown", stage, "", 320, TextAnchor.MiddleCenter);
            Stretch(countdown.rectTransform);

            var flash = NewImage("Flash", stage, new Color(1f, 1f, 1f, 0f));
            Stretch(flash.rectTransform);
            flash.raycastTarget = false;

            var (primary, primaryLabel) = NewButton("PrimaryButton", stage, "撮影開始！", new Vector2(400f, 110f));
            var pr = (RectTransform)primary.transform;
            pr.anchorMin = pr.anchorMax = pr.pivot = new Vector2(0.5f, 0f); // 中央下
            pr.anchoredPosition = new Vector2(0f, 30f);

            var startPanel = NewImage("StartPanel", stage, new Color(0f, 0f, 0f, 0.65f));
            Stretch(startPanel.rectTransform);
            var title = NewText("Title", startPanel.transform, "PoseSync Studio", 96, TextAnchor.MiddleCenter);
            SetAnchors(title.rectTransform, new Vector2(0f, 0.62f), new Vector2(1f, 0.76f));
            var subtitle = NewText("Subtitle", startPanel.transform, "ポーズをとると、タレントが合わせてくれる!", 44, TextAnchor.MiddleCenter);
            SetAnchors(subtitle.rectTransform, new Vector2(0f, 0.52f), new Vector2(1f, 0.6f));
            var (startButton, _) = NewButton("StartButton", startPanel.transform, "スタート", new Vector2(440f, 140f));
            var sr = (RectTransform)startButton.transform;
            sr.anchorMin = sr.anchorMax = sr.pivot = new Vector2(0.5f, 0.32f);

            var status = NewText("Status", stage, "", 20, TextAnchor.LowerLeft);
            SetAnchors(status.rectTransform, new Vector2(0.01f, 0f), new Vector2(0.99f, 0.03f));
            status.raycastTarget = false;

            // ---- Systems ----
            var framing = talentCam.gameObject.AddComponent<TalentFraming>();
            framing.rig = rig;
            framing.output = talentView;

            var booth = new GameObject("Booth");
            var webcam = booth.AddComponent<WebcamSource>();
            webcam.view = webcamView;
            var recorder = booth.AddComponent<FrameRecorder>();
            recorder.webcam = webcam;
            // Python サーバーで判定し、繋がらない時はデバッグ判定(数字キー/ランダム)に切り替える
            var debugEstimator = booth.AddComponent<DebugPoseEstimator>();
            var estimator = booth.AddComponent<HttpPoseEstimator>();
            estimator.fallbackEstimator = debugEstimator;

            var flow = booth.AddComponent<BoothFlow>();
            flow.talent = rig;
            flow.idleMotion = idle;
            flow.webcam = webcam;
            flow.frameRecorder = recorder;
            flow.poseEstimator = estimator;
            flow.startPanel = startPanel.gameObject;
            flow.startButton = startButton;
            flow.primaryButton = primary;
            flow.primaryButtonLabel = primaryLabel;
            flow.standingGuide = standingGuide;

            var look = booth.AddComponent<BoothLook>();
            look.gainShader = AssetDatabase.LoadAssetAtPath<Shader>(Root + "/Shaders/UIGain.shader");
            look.cameraImages = new[] { webcamView, resultPhoto };
            look.keyLight = light;
            look.talentView = tv;
            look.guideFrame = guideFrame;
            look.guideGraphics = guideGraphics;
            flow.look = look;

            var segmenter = booth.AddComponent<HttpSegmenter>();
            var compositor = booth.AddComponent<PhotoCompositor>();
            compositor.gainShader = look.gainShader;
            compositor.look = look;
            compositor.talentFraming = framing;
            compositor.talentView = tv;
            flow.segmenter = segmenter;
            flow.compositor = compositor;
            flow.compositeView = compositeView;
            debugEstimator.flow = flow;

            // 簡易ユーザーテスト用の体験ログ(data/sessions/*.jsonl)
            var logger = booth.AddComponent<SessionLogger>();
            logger.flow = flow;
            logger.compositor = compositor;
            flow.messageText = message;
            flow.countdownText = countdown;
            flow.poseLabelText = poseLabel;
            flow.talentView = tv;
            flow.resultPhoto = resultPhoto;
            flow.flash = flash;
            flow.statusText = status;

            EditorSceneManager.SaveScene(scene, ScenePath);
            AddToBuildSettings(ScenePath);
            Debug.Log($"[PoseSync] シーンを生成しました: {ScenePath}");
        }

        // ---- 仮マネキン ------------------------------------------------------
        static TalentRig BuildMannequin()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                mat.SetColor("_BaseColor", new Color(0.95f, 0.75f, 0.8f));
                AssetDatabase.CreateAsset(mat, MaterialPath);
            }

            var root = new GameObject("Talent (Mannequin)").transform;
            var hips = Bone("Hips", root, new Vector3(0f, 0.95f, 0f));
            Part(PrimitiveType.Capsule, hips, new Vector3(0f, 0.22f, 0f), Vector3.zero, new Vector3(0.32f, 0.25f, 0.2f), mat);
            Part(PrimitiveType.Capsule, hips, new Vector3(0.09f, -0.45f, 0f), Vector3.zero, new Vector3(0.12f, 0.45f, 0.12f), mat);
            Part(PrimitiveType.Capsule, hips, new Vector3(-0.09f, -0.45f, 0f), Vector3.zero, new Vector3(0.12f, 0.45f, 0.12f), mat);
            var chest = Bone("Chest", hips, new Vector3(0f, 0.3f, 0f));
            var head = Bone("Head", chest, new Vector3(0f, 0.25f, 0f));
            Part(PrimitiveType.Sphere, head, Vector3.zero, Vector3.zero, Vector3.one * 0.24f, mat);

            var rig = root.gameObject.AddComponent<TalentRig>();
            rig.hips = hips;
            rig.head = head;
            rig.spine = chest;
            // カメラは +Z 側から -Z を見るので、画面左 = ワールド +X
            (rig.leftUpper, rig.leftLower, rig.leftHand) = Arm(chest, +1f, mat);
            (rig.rightUpper, rig.rightLower, rig.rightHand) = Arm(chest, -1f, mat);
            return rig;
        }

        static (Transform, Transform, Transform) Arm(Transform chest, float side, Material mat)
        {
            const float upperLen = 0.27f, lowerLen = 0.25f;
            string n = side > 0 ? "ScreenLeft" : "ScreenRight";
            var upper = Bone(n + "UpperArm", chest, new Vector3(side * 0.18f, 0.13f, 0f));
            var lower = Bone(n + "LowerArm", upper, new Vector3(side * upperLen, 0f, 0f));
            var hand = Bone(n + "Hand", lower, new Vector3(side * lowerLen, 0f, 0f));
            var limb = new Vector3(0f, 0f, 90f);
            Part(PrimitiveType.Capsule, upper, new Vector3(side * upperLen / 2f, 0f, 0f), limb, new Vector3(0.09f, upperLen / 2f, 0.09f), mat);
            Part(PrimitiveType.Capsule, lower, new Vector3(side * lowerLen / 2f, 0f, 0f), limb, new Vector3(0.08f, lowerLen / 2f, 0.08f), mat);
            Part(PrimitiveType.Sphere, hand, Vector3.zero, Vector3.zero, Vector3.one * 0.09f, mat);
            return (upper, lower, hand);
        }

        static Transform Bone(string name, Transform parent, Vector3 localPos)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            t.localPosition = localPos;
            return t;
        }

        static void Part(PrimitiveType type, Transform parent, Vector3 pos, Vector3 euler, Vector3 scale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localPosition = pos;
            go.transform.localEulerAngles = euler;
            go.transform.localScale = scale;
            go.GetComponent<Renderer>().sharedMaterial = mat;
        }

        // ---- UI helpers ------------------------------------------------------
        static (GameObject guide, RectTransform frame, Graphic[] colored) BuildStandingGuide(Transform stage)
        {
            var guide = NewRect("StandingGuide", stage);
            Stretch(guide);
            // 画面左側が撮影者の立ち位置(右はタレント)。位置と色は BoothLook で調整する
            var frame = NewRect("Frame", guide);
            SetAnchors(frame, new Vector2(0.05f, 0f), new Vector2(0.55f, 0.86f));
            var c = new Color(1f, 0.62f, 0.78f, 0.85f);
            const float w = 8f;
            var top = Border(frame, "Top", new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0f, w), c);
            var left = Border(frame, "Left", new Vector2(0f, 0f), new Vector2(0f, 1f), new Vector2(w, 0f), c);
            var right = Border(frame, "Right", new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(w, 0f), c);
            var hint = NewText("Hint", frame, "頭", 36, TextAnchor.UpperCenter);
            SetAnchors(hint.rectTransform, new Vector2(0f, 0.86f), new Vector2(1f, 0.98f));
            var waist = NewText("Waist", frame, "— 腰 —", 36, TextAnchor.MiddleCenter);
            SetAnchors(waist.rectTransform, new Vector2(0f, 0.02f), new Vector2(0.55f, 0.1f)); // 中央下のボタンと重ならないよう左寄せ
            return (guide.gameObject, frame, new Graphic[] { top, left, right, hint, waist });
        }

        static Image Border(RectTransform parent, string name, Vector2 min, Vector2 max, Vector2 size, Color color)
        {
            var img = NewImage(name, parent, color);
            var r = img.rectTransform;
            r.anchorMin = min;
            r.anchorMax = max;
            r.sizeDelta = size;
            r.anchoredPosition = Vector2.zero;
            img.raycastTarget = false;
            return img;
        }

        static RectTransform NewRect(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            return (RectTransform)go.transform;
        }

        static RawImage NewRawImage(string name, Transform parent)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<RawImage>();
            img.raycastTarget = false;
            return img;
        }

        static Image NewImage(string name, Transform parent, Color color)
        {
            var img = NewRect(name, parent).gameObject.AddComponent<Image>();
            img.color = color;
            return img;
        }

        static Text NewText(string name, Transform parent, string text, int size, TextAnchor anchor)
        {
            var t = NewRect(name, parent).gameObject.AddComponent<Text>();
            t.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            t.text = text;
            t.fontSize = size;
            t.alignment = anchor;
            t.color = Color.white;
            t.horizontalOverflow = HorizontalWrapMode.Overflow;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            var outline = t.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(0f, 0f, 0f, 0.8f);
            outline.effectDistance = new Vector2(3f, -3f);
            return t;
        }

        static (Button, Text) NewButton(string name, Transform parent, string label, Vector2 size)
        {
            var img = NewImage(name, parent, new Color(1f, 0.45f, 0.62f));
            img.rectTransform.sizeDelta = size;
            var button = img.gameObject.AddComponent<Button>();
            var text = NewText("Label", img.transform, label, 52, TextAnchor.MiddleCenter);
            Stretch(text.rectTransform);
            return (button, text);
        }

        static void Stretch(RectTransform r) => SetAnchors(r, Vector2.zero, Vector2.one);

        static void SetAnchors(RectTransform r, Vector2 min, Vector2 max)
        {
            r.anchorMin = min;
            r.anchorMax = max;
            r.offsetMin = r.offsetMax = Vector2.zero;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            Directory.CreateDirectory(path);
            AssetDatabase.Refresh();
        }

        static void AddToBuildSettings(string path)
        {
            var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            if (scenes.Exists(s => s.path == path)) return;
            scenes.Insert(0, new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
