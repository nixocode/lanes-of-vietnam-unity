using System.Collections.Generic;
using LanesOfVietnam.Sim;
using UnityEngine;
using UnityEngine.Rendering;

namespace LanesOfVietnam.View
{
    /// <summary>
    /// The men, drawn where the simulation says they are, between its ticks.
    ///
    /// Grey-box: a capsule per man, coloured by side, shaped by posture — up,
    /// kneeling, flat — and fallen when dead. The soldiers replace the capsule;
    /// everything else here (pooling, interpolation, standing on the ground)
    /// stays.
    /// </summary>
    public sealed class ArmyView : MonoBehaviour
    {
        public Material UsMaterial;
        public Material VcMaterial;
        public Material DeadMaterial;

        private readonly List<Transform> _men = new List<Transform>();
        private readonly List<MeshRenderer> _renderers = new List<MeshRenderer>();
        private Mesh _capsule;

        public int Drawn { get; private set; }

        private void Awake()
        {
            var probe = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            _capsule = probe.GetComponent<MeshFilter>().sharedMesh;
            Destroy(probe);
        }

        public void Draw(MatchDriver d, Ground g)
        {
            var st = d.State;
            while (_men.Count < st.Men.Count)
            {
                var go = new GameObject($"man {_men.Count}");
                go.transform.SetParent(transform, false);
                go.AddComponent<MeshFilter>().sharedMesh = _capsule;
                var mr = go.AddComponent<MeshRenderer>();
                mr.shadowCastingMode = ShadowCastingMode.On;
                _men.Add(go.transform);
                _renderers.Add(mr);
            }

            Drawn = 0;
            for (int i = 0; i < st.Men.Count; i++)
            {
                var m = st.Men[i];
                var t = _men[i];
                var (x, z) = d.Position(i);
                float y = (float)g.HeightAt(x, z);
                var mr = _renderers[i];
                mr.sharedMaterial = !m.Alive ? DeadMaterial : m.Side == Side.Us ? UsMaterial : VcMaterial;

                // Unity's capsule is 2 m tall, 1 m across, centred on its pivot.
                float face = m.Side == Side.Us ? 90f : -90f;
                if (!m.Alive)
                {
                    t.SetPositionAndRotation(Coords.World(x, z, y + 0.18f), Quaternion.Euler(0, face, 90));
                    t.localScale = new Vector3(0.36f, 0.85f, 0.36f);
                }
                else if (m.Posture == Posture.Prone)
                {
                    t.SetPositionAndRotation(Coords.World(x, z, y + 0.2f), Quaternion.Euler(0, face, 90));
                    t.localScale = new Vector3(0.4f, 0.85f, 0.4f);
                }
                else
                {
                    float hgt = m.Posture == Posture.Crouched ? 1.15f : 1.77f;
                    t.SetPositionAndRotation(Coords.World(x, z, y + hgt * 0.5f), Quaternion.Euler(0, face, 0));
                    t.localScale = new Vector3(0.45f, hgt * 0.5f, 0.45f);
                }
                Drawn++;
            }
        }
    }
}
