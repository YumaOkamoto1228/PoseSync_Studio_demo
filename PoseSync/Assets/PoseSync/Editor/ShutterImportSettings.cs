using UnityEditor;

namespace PoseSync.EditorTools
{
    /// <summary>シャッターの連番 PNG を UI 用の設定(透明あり・ミップマップなし・高品質圧縮)で取り込む。</summary>
    public class ShutterImportSettings : AssetPostprocessor
    {
        public const string Folder = "Assets/ThirdParty/Shutter";

        void OnPreprocessTexture()
        {
            if (!assetPath.StartsWith(Folder)) return;
            Apply((TextureImporter)assetImporter);
        }

        /// <summary>設定が違っていれば直して再インポートする(ポストプロセッサ追加前に取り込まれた分の救済)。</summary>
        public static void EnsureImported()
        {
            foreach (var guid in AssetDatabase.FindAssets("t:Texture2D", new[] { Folder }))
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(AssetDatabase.GUIDToAssetPath(guid));
                if (importer == null || IsApplied(importer)) continue;
                Apply(importer);
                importer.SaveAndReimport();
            }
        }

        static bool IsApplied(TextureImporter t) =>
            t.textureType == TextureImporterType.Default && t.alphaIsTransparency && !t.mipmapEnabled
            && t.textureCompression == TextureImporterCompression.CompressedHQ && t.wrapMode == UnityEngine.TextureWrapMode.Clamp;

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
