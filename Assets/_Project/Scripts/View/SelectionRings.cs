using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// Rings on the selected squad's men, at chest height and facing the lens.
    ///
    /// The colour carries PLAN §5.1's distinction between the squad's state
    /// and the click history: amber when the player has given the order it is
    /// following, pale when the plan decided it. A squad the plan sent to
    /// ground and one the player ordered there look different.
    /// </summary>
    public sealed class SelectionRings : MonoBehaviour
    {
        public Commander Commander;
        public Material RingMaterial;

        public static readonly Color PlayerOrdered = new Color(1.0f, 0.74f, 0.26f, 0.95f);
        public static readonly Color PlanDecided = new Color(0.92f, 0.94f, 0.86f, 0.75f);

        /// <summary>Ring diameter at the man's distance, in metres: readable, not a hula hoop.</summary>
        public float Diameter = 1.05f;

        private readonly List<Transform> _rings = new List<Transform>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private MaterialPropertyBlock _mpb;
        private Mesh _quad;
        private static readonly int ColorId = Shader.PropertyToID("_Color");

        /// <summary>How many rings were drawn last frame. UIAudit reads it.</summary>
        public int Shown { get; private set; }
        public Color ShownColor { get; private set; }

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            var probe = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad = probe.GetComponent<MeshFilter>().sharedMesh;
            Destroy(probe);
        }

        private void LateUpdate()
        {
            Shown = 0;
            var root = Commander.Root;
            int squad = Commander.Selected;
            if (squad >= 0 && root.Driver != null)
            {
                var st = root.Driver.State;
                var sq = st.Squads[squad];
                var color = sq.PlayerOrder.HasValue ? PlayerOrdered : PlanDecided;
                ShownColor = color;
                _mpb.SetColor(ColorId, color);
                var camRot = root.CameraRig.Camera.transform.rotation;
                // The squad's living men, in the simulation's own order (by id).
                for (int i = 0; i < st.Men.Count; i++)
                {
                    var m = st.Men[i];
                    if (m.Squad != squad || !m.Alive) continue;
                    int k = Shown++;
                    var t = Ring(k);
                    t.SetPositionAndRotation(Commander.ChestOf(m.Id), camRot);
                    t.localScale = Vector3.one * Diameter;
                    _renderers[k].SetPropertyBlock(_mpb);
                    if (!t.gameObject.activeSelf) t.gameObject.SetActive(true);
                }
            }
            for (int i = Shown; i < _rings.Count; i++) if (_rings[i].gameObject.activeSelf) _rings[i].gameObject.SetActive(false);
        }

        private Transform Ring(int i)
        {
            while (_rings.Count <= i)
            {
                var go = new GameObject("ring");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _quad;
                var mr = go.AddComponent<MeshRenderer>();
                mr.sharedMaterial = RingMaterial;
                mr.shadowCastingMode = ShadowCastingMode.Off;
                mr.receiveShadows = false;
                _rings.Add(go.transform);
                _renderers.Add(mr);
            }
            return _rings[i];
        }
    }
}
