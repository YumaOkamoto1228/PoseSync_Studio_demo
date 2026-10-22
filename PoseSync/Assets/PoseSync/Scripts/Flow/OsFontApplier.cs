using UnityEngine;
using UnityEngine.UI;

namespace PoseSync
{
    /// <summary>子孫の Text に Windows の日本語フォントを割り当てる(フォントアセットを同梱しないため)。</summary>
    [DefaultExecutionOrder(-1000)]
    public class OsFontApplier : MonoBehaviour
    {
        public string[] fontNames = { "BIZ UDPGothic", "Yu Gothic UI", "Meiryo UI", "MS Gothic" };
        public int size = 64;

        void Awake()
        {
            var font = Font.CreateDynamicFontFromOSFont(fontNames, size);
            foreach (var text in GetComponentsInChildren<Text>(true)) text.font = font;
        }
    }
}
