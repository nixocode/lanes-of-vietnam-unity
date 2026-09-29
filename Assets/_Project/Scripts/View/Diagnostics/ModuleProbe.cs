// Size probe for PLAN §12.9: what does *using* each engine module cost?
//
// Listing a module in the manifest costs almost nothing — engine stripping
// removes whatever no code or scene references — so a module's real price is
// only visible once something uses it. Build.ModuleDeltas compiles this file
// once per module with that module's LOV_PROBE_* define, builds the empty
// scene, and reports the size against the floor. With no define, this file
// compiles to nothing and costs nothing.
#if LOV_PROBE_PARTICLES || LOV_PROBE_TERRAIN || LOV_PROBE_ASSETBUNDLE || LOV_PROBE_ANIMATION || LOV_PROBE_ADDRESSABLES || LOV_PROBE_ALL
using UnityEngine;

namespace LanesOfVietnam.View.Diagnostics
{
    public sealed class ModuleProbe : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Use()
        {
            var go = new GameObject("module probe");
#if LOV_PROBE_PARTICLES || LOV_PROBE_ALL
            var ps = go.AddComponent<ParticleSystem>();
            ps.Emit(1);
#endif
#if LOV_PROBE_TERRAIN || LOV_PROBE_ALL
            var td = new TerrainData { heightmapResolution = 33, size = new Vector3(10, 1, 10) };
            Terrain.CreateTerrainGameObject(td);
#endif
#if LOV_PROBE_ASSETBUNDLE || LOV_PROBE_ALL
            var req = UnityEngine.Networking.UnityWebRequestAssetBundle.GetAssetBundle("probe.bundle");
            req.SendWebRequest();
            AssetBundle.UnloadAllAssetBundles(false);
#endif
#if LOV_PROBE_ANIMATION || LOV_PROBE_ALL
            var anim = go.AddComponent<Animator>();
            anim.SetFloat("speed", 1f);
#endif
#if LOV_PROBE_ADDRESSABLES
            UnityEngine.AddressableAssets.Addressables.InitializeAsync();
#endif
        }
    }
}
#endif
