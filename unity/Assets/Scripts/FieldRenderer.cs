using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Draws the scenario ground truth: threat kill zones, jammer bubbles,
    /// objective circle, spawn band. Read-only over Simulation.Scenario —
    /// nothing here is hidden from the player (§2: no hidden information;
    /// only the *interaction* is incomputable).
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class FieldRenderer : MonoBehaviour
    {
        public Color threatColor = new Color(1f, 0.35f, 0.2f, 0.22f);
        public Color jammerColor = new Color(0.7f, 0.4f, 1f, 0.16f);
        public Color objectiveColor = new Color(0.3f, 1f, 0.5f, 0.25f);
        public Color spawnColor = new Color(0.4f, 0.7f, 1f, 0.12f);

        private SimDriver _driver;
        private Mesh _disc;
        private Material _material;
        private Matrix4x4[] _matrices;
        private Vector4[] _colors;
        private MaterialPropertyBlock _props;
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _disc = BuildDisc(48);
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            _material = new Material(shader) { enableInstancing = true };
            _material.SetFloat("_Surface", 1f);
            _material.renderQueue = 2900;
            _props = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            if (sim == null) return;
            var sc = sim.Scenario;

            if (_matrices == null)
            {
                int count = sc.ThreatCount + sc.JammerCount + 2;
                _matrices = new Matrix4x4[count];
                _colors = new Vector4[count];
                int k = 0;
                for (int t = 0; t < sc.ThreatCount; t++, k++)
                {
                    Fill(k, sc.ThreatX[t], sc.ThreatY[t], Scenario.ThreatKillRadius, threatColor);
                }
                for (int j = 0; j < sc.JammerCount; j++, k++)
                {
                    Fill(k, sc.JammerX[j], sc.JammerY[j], Scenario.JammerRadius, jammerColor);
                }
                Fill(k++, Scenario.ObjectiveX, Scenario.ObjectiveY, Scenario.ObjectiveRadius, objectiveColor);
                // Spawn band as a stretched disc across the south edge.
                _matrices[k] = Matrix4x4.TRS(
                    new Vector3(Simulation.WorldWidth * 0.5f, 0.05f, Scenario.SpawnBandHeight * 0.5f),
                    Quaternion.identity,
                    new Vector3(Simulation.WorldWidth, 1f, Scenario.SpawnBandHeight));
                _colors[k] = spawnColor;
            }

            _props.SetVectorArray(ColorProp, _colors);
            var rp = new RenderParams(_material) { matProps = _props };
            Graphics.RenderMeshInstanced(rp, _disc, 0, _matrices, _matrices.Length);
        }

        private void Fill(int k, float x, float y, float radius, Color c)
        {
            _matrices[k] = Matrix4x4.TRS(
                new Vector3(x, 0.05f, y), Quaternion.identity,
                new Vector3(radius * 2f, 1f, radius * 2f));
            _colors[k] = c;
        }

        private static Mesh BuildDisc(int segments)
        {
            var verts = new Vector3[segments + 1];
            var tris = new int[segments * 3];
            verts[0] = Vector3.zero;
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                verts[i + 1] = new Vector3(Mathf.Cos(a) * 0.5f, 0f, Mathf.Sin(a) * 0.5f);
                tris[i * 3] = 0;
                tris[i * 3 + 1] = 1 + (i + 1) % segments;
                tris[i * 3 + 2] = 1 + i;
            }
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
