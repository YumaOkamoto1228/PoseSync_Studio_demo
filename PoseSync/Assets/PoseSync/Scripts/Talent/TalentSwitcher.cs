using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>
    /// タレントのモデルを切り替える。右下の人アイコンを押すと、名前の一覧が上方向に開き(ドロップアップ)、
    /// 選んだタレントのモデルに入れ替わる。スタート画面と立ち位置ガイドの間だけ操作できる(撮影中は隠す)。
    ///
    /// タレントを増やす時:シーンにモデル(TalentRig 付き)を置き、entries に「表示名・モデル」を1行足すだけでよい
    /// (一覧のボタンは entries から実行時に作る)。PoseSync > Build Booth Scene は Assets/ThirdParty/VRM の
    /// すべての VRM を自動で並べ、表示名には VRM に書かれたタイトルを使う。
    /// </summary>
    public class TalentSwitcher : MonoBehaviour
    {
        [Serializable]
        public class Entry
        {
            public string displayName;
            public GameObject model; // TalentRig を持つモデルの一番上
        }

        public List<Entry> entries = new List<Entry>();
        [Tooltip("起動時に使うタレント(entries の番号)")]
        public int defaultIndex;

        [Header("References")]
        public BoothFlow flow;
        public TalentFraming framing;
        public Button iconButton;
        [Tooltip("名前のボタンを縦に並べる入れ物(アイコンの上に開く)")]
        public RectTransform listPanel;
        [Tooltip("名前のボタンの文字に使うフォント(実行時に作るボタン用)")]
        public Text fontSource;

        [Header("Look")]
        public Vector2 itemSize = new Vector2(320f, 90f);
        public Color itemColor = new Color(1f, 1f, 1f, 0.92f);
        public Color selectedColor = new Color(1f, 0.62f, 0.78f, 0.95f);
        public Color textColor = new Color(0.25f, 0.2f, 0.25f);

        public int Current { get; private set; } = -1;
        public event Action<Entry> Changed;

        readonly List<Image> itemImages = new List<Image>();
        bool open;

        void Awake()
        {
            if (iconButton != null) iconButton.onClick.AddListener(Toggle);
            BuildList();
            SetOpen(false);
            Select(Mathf.Clamp(defaultIndex, 0, Mathf.Max(0, entries.Count - 1)));
        }

        void Update()
        {
            // 撮影中・結果画面では切り替えさせない(タレントがいきなり別人になるのを防ぐ)
            bool usable = flow == null || flow.State == BoothState.Idle || flow.State == BoothState.Standing;
            bool show = usable && entries.Count > 1;
            if (iconButton != null && iconButton.gameObject.activeSelf != show) iconButton.gameObject.SetActive(show);
            if (!show && open) SetOpen(false);
        }

        public void Toggle() => SetOpen(!open);

        void SetOpen(bool value)
        {
            open = value;
            if (listPanel != null) listPanel.gameObject.SetActive(value);
        }

        void BuildList()
        {
            if (listPanel == null) return;
            for (int i = listPanel.childCount - 1; i >= 0; i--) Destroy(listPanel.GetChild(i).gameObject);
            itemImages.Clear();
            for (int i = 0; i < entries.Count; i++)
            {
                int index = i;
                var go = new GameObject(entries[i].displayName, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
                go.transform.SetParent(listPanel, false);
                go.GetComponent<LayoutElement>().preferredHeight = itemSize.y;
                var image = go.GetComponent<Image>();
                image.color = itemColor;
                go.GetComponent<Button>().onClick.AddListener(() => { Select(index); SetOpen(false); });
                itemImages.Add(image);

                var label = new GameObject("Label", typeof(RectTransform), typeof(Text));
                label.transform.SetParent(go.transform, false);
                var rt = (RectTransform)label.transform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = rt.offsetMax = Vector2.zero;
                var text = label.GetComponent<Text>();
                text.text = entries[i].displayName;
                text.font = fontSource != null ? fontSource.font : Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 44;
                text.alignment = TextAnchor.MiddleCenter;
                text.color = textColor;
                text.raycastTarget = false;
            }
            listPanel.sizeDelta = new Vector2(itemSize.x, listPanel.sizeDelta.y);
        }

        /// <summary>entries[index] のタレントに切り替える。</summary>
        public void Select(int index)
        {
            if (index < 0 || index >= entries.Count || entries[index].model == null) return;
            for (int i = 0; i < entries.Count; i++)
                if (entries[i].model != null) entries[i].model.SetActive(i == index); // 初めて有効にした時に TalentRig が基準姿勢を記録する

            var model = entries[index].model;
            var rig = model.GetComponent<TalentRig>();
            var idle = model.GetComponent<ExcitedIdleMotion>();
            rig.Initialize();
            rig.hipsOffset = Vector3.zero;
            rig.hipsRoll = 0f;
            rig.SetImmediate(PoseLibrary.Rest.ToTalentPose());
            if (idle != null) idle.enabled = false;

            if (flow != null)
            {
                flow.talent = rig;
                if (idle != null) flow.idleMotion = idle;
            }
            if (framing != null) framing.Reframe(rig);

            Current = index;
            for (int i = 0; i < itemImages.Count; i++) itemImages[i].color = i == index ? selectedColor : itemColor;
            Debug.Log($"[TalentSwitcher] タレント: {entries[index].displayName}");
            Changed?.Invoke(entries[index]);
        }
    }
}
