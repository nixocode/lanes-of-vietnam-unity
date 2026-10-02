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
    /// nearest power of two. The ground's layers must be crunched,
    /// with anisotropic filtering. Rules by folder, so every file that lands there
    /// is treated the same and the settings live in a diff.
    /// </summary>
    public sealed class ArtImport : AssetPostprocessor
    {
        /// <summary>Crunch quality for the sky and the ground's layers (0-100).</summary>
        public const int CrunchQuality = 75;

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
                // The window is only ever magnified (40 texels a degree against
                // 57 screen pixels at 1080p through a 19 degree lens), so it
                // needs no mips; and with mips Unity will not compress it,
                // because 1040 stops dividing by four three levels down.
                ti.mipmapEnabled = p.EndsWith("sky_full.png");
                ti.wrapModeU = p.EndsWith("sky_full.png") ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.wrapModeV = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 1;
                ti.alphaSource = TextureImporterAlphaSource.None;
                // Crunched DXT. "CompressedHQ" has no format the WebGL build
                // accepts at this size, and Unity quietly shipped the window
                // as raw RGBA: 10.2 MB, 63% of the whole build.
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = CrunchQuality;
            }
            else if ((p.StartsWith("Assets/_Project/Art/Plants/") || p.StartsWith("Assets/_Project/Art/Props/")) && p.EndsWith(".png"))
            {
                // Baked plant atlases (tools/blender/plant_bake.py). The normal
                // atlas is not a Unity normal map: it holds the bake frame's x
                // and y, the occlusion and the sunlit term, read as plain linear data.
                bool albedo = p.EndsWith("_albedo.png");
                ti.textureType = TextureImporterType.Default;
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.sRGBTexture = albedo;
                ti.alphaSource = TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;          // the bake already dilated the colour under the cut-outs
                ti.maxTextureSize = 4096;
                ti.mipmapEnabled = true;
                // Alpha-tested leaves thin out with distance as mips average
                // their coverage down; this keeps each mip's coverage at the cutoff.
                ti.mipMapsPreserveCoverage = albedo;
                ti.alphaTestReferenceValue = 0.5f;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 2;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = CrunchQuality;
            }
            else if (p.StartsWith("Assets/_Project/Art/Soldiers3D/") && p.EndsWith(".png"))
            {
                // The 3D soldiers' atlas (tools/blender/soldier_rig.py): albedo,
                // a tangent-space normal map, and a linear mask (metallic,
                // occlusion, -, smoothness). Baked at 2048, shipped at 1024: a
                // man is at most ~330 px tall at full zoom, and 1024 over his
                // atlas is ~540 texels a metre.
                bool normal = p.EndsWith("_normal.png");
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = p.EndsWith("_albedo.png");
                ti.alphaSource = p.EndsWith("_mask.png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = false;
                // Six men now: the colour at 1024, the normal and the mask at 512
                // (they carry cloth grain and soft occlusion, not edges).
                ti.maxTextureSize = p.EndsWith("_albedo.png") ? 1024 : 512;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 2;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = CrunchQuality;
            }
            else if (p.StartsWith("Assets/_Project/Art/Structures/") && p.EndsWith(".png"))
            {
                // The sandbag walls (tools/blender/sandbag_mesh.py): an atlas of
                // albedo, tangent-space normal and a linear mask (-, occlusion,
                // -, smoothness), a cell a bag; and the cloth's unevenness,
                // tiled over each bag as a detail map: its light and dark about
                // 0.5, read linear, because URP doubles what it reads.
                bool normal = p.EndsWith("_normal.png");
                bool weave = p.Contains("_weave");
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = p.EndsWith("_albedo.png");
                ti.alphaSource = p.EndsWith("_mask.png") ? TextureImporterAlphaSource.FromInput : TextureImporterAlphaSource.None;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = p.EndsWith("_albedo.png") ? 2048 : 1024;
                ti.mipmapEnabled = true;
                ti.wrapMode = weave ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Trilinear;
                ti.anisoLevel = 4;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = CrunchQuality;
            }
            else if (p.StartsWith("Assets/_Project/Art/Weapons/") && p.EndsWith(".png"))
            {
                // The weapons' palette (tools/blender/weapons.py): a texel a material, read exactly.
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.textureType = TextureImporterType.Default;
                ti.sRGBTexture = true;
                ti.alphaSource = TextureImporterAlphaSource.None;
                ti.npotScale = TextureImporterNPOTScale.None;
                ti.mipmapEnabled = false;
                ti.wrapMode = TextureWrapMode.Clamp;
                ti.filterMode = FilterMode.Point;
                ti.textureCompression = TextureImporterCompression.Uncompressed;
            }
            else if (p.StartsWith("Assets/_Project/Art/Terrain/ground_") && p.EndsWith(".png"))
            {
                // One albedo (+ height in alpha) and one normal per layer
                // (tools/art/terrain_pack.py), crunched: DXT alone gave the
                // WebGL build 11 MB for four layers, and Brotli barely touches
                // DXT data. Crunch does not take texture arrays, hence the pairs.
                bool normal = p.EndsWith("_normal.png");
                ti.textureShape = TextureImporterShape.Texture2D;
                ti.textureType = normal ? TextureImporterType.NormalMap : TextureImporterType.Default;
                ti.sRGBTexture = !normal;
                ti.alphaSource = normal ? TextureImporterAlphaSource.None : TextureImporterAlphaSource.FromInput;
                ti.alphaIsTransparency = false;
                ti.maxTextureSize = 2048;
                ti.mipmapEnabled = true;
                ti.wrapMode = TextureWrapMode.Repeat;
                ti.filterMode = FilterMode.Trilinear;
                // The ground is seen at a few degrees: without anisotropy the
                // mip chain blurs it to mush within 30 m.
                ti.anisoLevel = 8;
                ti.textureCompression = TextureImporterCompression.Compressed;
                ti.crunchedCompression = true;
                ti.compressionQuality = CrunchQuality;
            }
        }

        private void OnPreprocessModel()
        {
            var mi = (ModelImporter)assetImporter;
            string p = assetPath.Replace('\\', '/');
            if (p.StartsWith("Assets/_Project/Art/Soldiers3D/soldier_") && !p.EndsWith("_poses.fbx"))
            {
                // The soldiers' bodies. SoldierBuilder makes their avatar (from a
                // T-pose), their material and their Animator; the importer brings
                // only the mesh and the bones: Generic without an avatar ("None"
                // imports the body unskinned, as a plain mesh). Readable, so a
                // fallen man's pose can be baked into a plain mesh.
                mi.animationType = ModelImporterAnimationType.Generic;
                mi.avatarSetup = ModelImporterAvatarSetup.NoAvatar;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.importVisibility = false;
                mi.optimizeGameObjects = false;
                mi.isReadable = true;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
                mi.skinWeights = ModelImporterSkinWeights.Standard;
            }
            else if (p.StartsWith("Assets/_Project/Art/Weapons/"))
            {
                // The weapons: plain meshes with their marker empties; SoldierBuilder makes the material.
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importCameras = false;
                mi.importLights = false;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.importNormals = ModelImporterNormals.Import;
            }
            else if (p.StartsWith("Assets/_Project/Art/Structures/"))
            {
                // Built structures: plain meshes, their material made by
                // SceneBuilder. Readable, so the dressing can combine them.
                mi.animationType = ModelImporterAnimationType.None;
                mi.importAnimation = false;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importBlendShapes = false;
                mi.importCameras = false;
                mi.importLights = false;
                mi.isReadable = true;
                mi.meshCompression = ModelImporterMeshCompression.Off;
                mi.importNormals = ModelImporterNormals.Import;
                mi.importTangents = ModelImporterTangents.CalculateMikk;
            }
            else if (p.StartsWith("Assets/_Licensed/Mixamo/"))
            {
                // Mixamo's clips (PLAN §12.3): Humanoid, each on the avatar of
                // its own skeleton (Mixamo's, T-posed), retargeted at run time.
                // Root motion is left in the clip for its measured speed and
                // not applied: the simulation owns where a man is.
                mi.animationType = ModelImporterAnimationType.Human;
                mi.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
                mi.importAnimation = true;
                mi.materialImportMode = ModelImporterMaterialImportMode.None;
                mi.importCameras = false;
                mi.importLights = false;
            }
        }
    }
}
