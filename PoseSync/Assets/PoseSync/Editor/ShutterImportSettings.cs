using UnityEditor;

namespace PoseSync.EditorTools
{
    /// <summary>
    /// UI に使う画像(シャッターの連番・タレント切り替えアイコン・立ち位置の人型)を UI 用の設定で取り込む。
    /// 透明あり・ミップマップなし・高品質圧縮・サイズを 2 の累乗に変えない(変えると縦横比が崩れる)。
    /// </summary>
    public class ShutterImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/Shutter";
        static readonly string[] Folders = { Folder, "Assets/ThirdParty/Ui", "Assets/ThirdParty/Guide" };

        void OnPreprocessTexture()
        {
            if (!System.Array.Exists(Folders, f => assetPath.StartsWith(f))) return;
            Apply((TextureImporter)assetImporter);
        }

        /// <summary>設定が違っていれば直して再インポートする(ポストプロセッサ追加前に取り込まれた分の救済)。</summary>
        public static void EnsureImported()
        {
            foreach (var folder in Folders)
            {
                if (!AssetDatabase.IsValidFolder(folder)) continue;
                foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { folder }))
                {
                    var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                    if (importer == null || IsApplied(importer)) continue;
                    Apply(importer);
                    importer.SaveAndReimport();
                }
            }
        }

        static bool IsApplied(TextureImporter t) =>
            t.textureType == TextureImporterType.Default && t.alphaIsTransparency && !t.mipmapEnabled
            && t.textureCompression == TextureImporterCompression.CompressedHQ && t.wrapMode == UnityEngine.TextureWrapMode.Clamp
            && t.npotScale == TextureImporterNPOTScale.None;

        static void Apply(TextureImporter t)
        {
            t.textureType = TextureImporterType.Default;
            t.alphaSource = TextureImporterAlphaSource.FromInput;
            t.alphaIsTransparency = true;
            t.mipmapEnabled = false;
            t.wrapMode = UnityEngine.TextureWrapMode.Clamp;
            t.npotScale = TextureImporterNPOTScale.None;
            t.maxTextureSize = 2048;
            t.textureCompression = TextureImporterCompression.CompressedHQ; // PC では BC7
        }
    }
}
