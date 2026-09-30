using UnityEditor;
using UnityEngine;

namespace LanesOfVietnam.Tools
{
    /// <summary>
    /// Import settings for the project's art, enforced rather than clicked.
    ///
    /// Unity's defaults would quietly spoil the sky: the importer caps a
    /// texture at 2048 (the window is 2560 wide, cut to keep 40 px per degree
    /// at a 19 degree lens) and rescales a non-power-of-two image to the
    /// nearest power of two. Rules by folder, so every file that lands there
    /// is treated the same and the settings live in a diff.
    /// </summary>
    public sealed class ArtImport : AssetPostprocessor
    {
        private void OnPreprocessTexture()
        {
            var ti = (TextureImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');

            if (p.StartsWith("Assets/_Project/Art/Sky/"))
            {
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = true;
                ti.wrapModeU = p.EndsWith("sky_full.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 1;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.textureCompression = TextureImporterCompression.CompressedHQ;
            }
        }
    }
}
