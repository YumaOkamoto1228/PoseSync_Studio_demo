using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// 「立ち位置ガイド」画面の見た目(枠とタレントの位置関係・大きさ)を Play せずに PNG で確認する。
    /// カメラ映像の代わりに灰色の板を置く。出力: (プロジェクトの1つ上)/docs/layout_preview.png
    /// </summary>
    public static class LayoutPreviewRenderer
    {
        /// <summary>プレビュー用に、実行時の TalentSwitcher と同じ見た目の名前ボタンを作る(シーンは保存しない)。</summary>
        static (GameObject, UnityEngine.UI.Text) PreviewItem(TalentSwitcher s, string name)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.LayoutElement));
            go.transform.SetParent(s.listPanel, false);
            go.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = s.itemSize.y;
            go.GetComponent<UnityEngine.UI.Image>().color = s.listPanel.childCount == 1 ? s.selectedColor : s.itemColor;
            var label = new GameObject("Label", typeof(RectTransform), typeof(UnityEngine.UI.Text));
            label.transform.SetParent(go.transform, false);
            var rt = (RectTransform)label.transform;
            rt.anchorMin = Vector2.zero; rt.anchorMax = Vector2.one; rt.offsetMin = rt.offsetMax = Vector2.zero;
            var text = label.GetComponent<UnityEngine.UI.Text>();
            text.text = name; text.fontSize = 44; text.alignment = TextAnchor.MiddleCenter; text.color = s.textColor;
            return (go, text);
        }

        const int Size = 1080;
        const int UiLayer = 5;

        [MenuItem("PoseSync/Render Layout Preview")]
        public static void Render()
        {
            var scene = EditorSceneManager.OpenScene("Assets/PoseSync/Scenes/Booth.unity");
            var flow = Object.FindFirstObjectByType<BoothFlow>();
            var look = flow.look;
            var rig = flow.talent;
            var framing = Object.FindFirstObjectByType<TalentFraming>();
            var canvas = Object.FindFirstObjectByType<Canvas>();
            var mainCam = Camera.main;

            // タレント(デフォルトポーズ)を RenderTexture に描く
            PoseLibrary.Reload();
            rig.Initialize();
            framing.Setup();
            rig.SetImmediate(PoseLibrary.Rest.ToTalentPose());
            rig.ApplyBones();
            look.Apply();
            PosePreviewRenderer.RenderWithBakedSkin(rig, framing.GetComponent<Camera>());

            // 立ち位置ガイドの画面を再現
            var placeholder = new Texture2D(2, 2);
            placeholder.SetPixels(new[] { new Color(0.35f, 0.33f, 0.3f), new Color(0.35f, 0.33f, 0.3f), new Color(0.6f, 0.58f, 0.55f), new Color(0.6f, 0.58f, 0.55f) });
            placeholder.Apply();
            flow.webcam.view.texture = placeholder;
            flow.startPanel.SetActive(false);
            flow.standingGuide.SetActive(true);
            // 実際の立ち位置ガイドと同じ見た目(目立たせる案内文、ボタンなし = 枠に入ると自動で開始)
            var style = flow.highlightMessageStyle;
            flow.messageText.text = "枠の中に、頭から腰までが入るように立ってね";
            flow.messageText.fontSize = style.fontSize;
            flow.messageText.resizeTextForBestFit = true;
            flow.messageText.resizeTextMinSize = 28;
            flow.messageText.resizeTextMaxSize = style.fontSize;
            flow.messageText.verticalOverflow = VerticalWrapMode.Truncate;
            flow.messageText.color = style.color;
            if (flow.messageText.TryGetComponent(out UnityEngine.UI.Outline outline))
            {
                outline.effectColor = style.outlineColor;
                outline.effectDistance = style.outlineDistance;
            }
            flow.primaryButton.gameObject.SetActive(false);
            // タレント切り替えの一覧を開いた状態も確認できるよう、アイコンの上に名前を並べる
            var switcher = Object.FindFirstObjectByType<TalentSwitcher>();
            if (switcher != null && switcher.listPanel != null)
            {
                switcher.listPanel.gameObject.SetActive(true);
                foreach (var e in switcher.entries)
                {
                    var (_, label) = PreviewItem(switcher, e.displayName);
                    label.font = flow.messageText.font;
                }
            }
            flow.talentView.anchoredPosition = look.TalentShownPosition;

            // Overlay の Canvas はカメラで撮れないので、一時的に Camera モードにして描く
            foreach (var t in canvas.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = UiLayer;
            var rt = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            mainCam.targetTexture = rt;
            mainCam.cullingMask = 1 << UiLayer;
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = mainCam;
            canvas.planeDistance = 1f;
            Canvas.ForceUpdateCanvases();
            mainCam.Render();

            var prev = RenderTexture.active;
            RenderTexture.active = rt;
            var tex = new Texture2D(Size, Size, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
            tex.Apply();
            RenderTexture.active = prev;

            string path = Path.GetFullPath(Path.Combine(Application.dataPath, "../../docs/layout_preview.png"));
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Debug.Log($"[PoseSync] layout preview: {path} (talentCenterX={look.talentCenterX}, size={look.talentViewSize}, frameX={look.guideFrameX})");

            mainCam.targetTexture = null;
            rt.Release();
            EditorSceneManager.OpenScene(scene.path); // 変更を破棄
        }
    }
}
