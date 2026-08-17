using System.Collections.Generic;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Draws the scenario ground truth in command-display vocabulary — thin
    /// range rings and center markers rather than filled blobs (playtest
    /// finding: filled discs read as biology, not as a tactical picture), plus
    /// a faint map grid, a target reticle, and the launch line. Read-only over
    /// Simulation.Scenario — nothing here is hidden from the player (§2).
    /// Geometry rebuilds whenever the driver starts a new run (new seed = new
    /// emplacements).
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class FieldRenderer : MonoBehaviour
    {
        public Color threatRing = new Color(1f, 0.35f, 0.2f, 0.6f);
        public Color threatFill = new Color(1f, 0.3f, 0.15f, 0.035f);
        public Color jammerRing = new Color(0.72f, 0.42f, 1f, 0.7f);
        public Color jammerFill = new Color(0.7f, 0.4f, 1f, 0.05f);
        public Color objectiveColor = new Color(0.35f, 1f, 0.55f, 0.9f);
        public Color spawnLine = new Color(0.4f, 0.75f, 1f, 0.5f);
        public Color gridColor = new Color(1f, 1f, 1f, 0.045f);
        /// <summary>Assigned by SceneBootstrap — see SwarmRenderer.material.</summary>
        public Material material;
        /// <summary>Emplacement models (Kenney CC0, baked by SceneBootstrap):
        /// gun/SAM turrets on the threats, EW dishes on the jammers. World scale
        /// applied to the unit-normalized baked meshes.</summary>
        public const float TurretSize = 14f;
        public const float DishSize = 18f;
        public Mesh turretMesh;
        public Material[] turretMaterials;
        public Mesh dishMesh;
        public Material[] dishMaterials;

        private SimDriver _driver;
        private Simulation _builtFor;
        private Mesh _disc;
        private Mesh _ring;
        private Mesh _quad;
        private Material _material;
        private MaterialPropertyBlock _props;
        private readonly List<Matrix4x4> _discM = new List<Matrix4x4>();
        private readonly List<Vector4> _discC = new List<Vector4>();
        private readonly List<Matrix4x4> _ringM = new List<Matrix4x4>();
        private readonly List<Vector4> _ringC = new List<Vector4>();
        private readonly List<Matrix4x4> _quadM = new List<Matrix4x4>();
        private readonly List<Vector4> _quadC = new List<Vector4>();
        private readonly List<Matrix4x4> _turretM = new List<Matrix4x4>();
        private readonly List<Matrix4x4> _dishM = new List<Matrix4x4>();
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        /// <summary>Explicit culling bounds for every instanced draw: default
        /// RenderParams bounds are a zero-size box at the origin, which the old
        /// top-down camera happened to keep on screen — the perspective camera
        /// does not, so without this the whole instanced layer can vanish.</summary>
        private static readonly Bounds DrawBounds =
            new Bounds(new Vector3(256f, 60f, 256f), new Vector3(1800f, 500f, 1800f));

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _disc = BuildDisc(48);
            _ring = BuildRing(64, 0.965f);
            _quad = BuildQuad();
            _material = material;
            if (_material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) _material = new Material(shader) { enableInstancing = true };
            }
            _props = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            var sim = _driver.Sim;
            if (sim == null || _material == null) return;
            if (!ReferenceEquals(sim, _builtFor)) BuildLayout(sim.Scenario);

            Draw(_quad, _quadM, _quadC);
            Draw(_disc, _discM, _discC);
            Draw(_ring, _ringM, _ringC);
            DrawModel(turretMesh, turretMaterials, _turretM);
            DrawModel(dishMesh, dishMaterials, _dishM);
        }

        /// <summary>Lit, shadowed, instanced — one draw per submesh so the baked
        /// flat colors survive.</summary>
        private static void DrawModel(Mesh mesh, Material[] mats, List<Matrix4x4> m)
        {
            if (mesh == null || mats == null || mats.Length == 0 || m.Count == 0) return;
            int subs = Mathf.Min(mesh.subMeshCount, mats.Length);
            for (int s = 0; s < subs; s++)
            {
                var rp = new RenderParams(mats[s])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                    worldBounds = DrawBounds,
                };
                Graphics.RenderMeshInstanced(rp, mesh, s, m, m.Count);
            }
        }

        private void Draw(Mesh mesh, List<Matrix4x4> m, List<Vector4> c)
        {
            if (m.Count == 0) return;
            _props.Clear();
            _props.SetVectorArray(ColorProp, c);
            var rp = new RenderParams(_material) { matProps = _props, worldBounds = DrawBounds };
            Graphics.RenderMeshInstanced(rp, mesh, 0, m, m.Count);
        }

        private void BuildLayout(Scenario sc)
        {
            _builtFor = _driver.Sim;
            _discM.Clear(); _discC.Clear();
            _ringM.Clear(); _ringC.Clear();
            _quadM.Clear(); _quadC.Clear();
            _turretM.Clear(); _dishM.Clear();

            // Emplacement models: turrets face south toward the incoming swarm,
            // with a per-site deterministic yaw wobble so the belt doesn't look
            // stamped; dishes at the EW sites.
            for (int t = 0; t < sc.ThreatCount; t++)
            {
                float yaw = 180f + (((t * 73) % 21) - 10f);
                _turretM.Add(Matrix4x4.TRS(
                    new Vector3(sc.ThreatX[t], 0f, sc.ThreatY[t]),
                    Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(TurretSize, TurretSize, TurretSize)));
            }
            for (int j = 0; j < sc.JammerCount; j++)
            {
                float yaw = (j * 61) % 360;
                _dishM.Add(Matrix4x4.TRS(
                    new Vector3(sc.JammerX[j], 0f, sc.JammerY[j]),
                    Quaternion.Euler(0f, yaw, 0f),
                    new Vector3(DishSize, DishSize, DishSize)));
            }

            // Map grid, every 64 units — the command-display ground.
            for (int g = 0; g <= 8; g++)
            {
                float p = g * 64f;
                AddQuad(new Vector3(p, 0.12f, Simulation.WorldHeight * 0.5f),
                        new Vector3(0.9f, 1f, Simulation.WorldHeight), gridColor);
                AddQuad(new Vector3(Simulation.WorldWidth * 0.5f, 0.12f, p),
                        new Vector3(Simulation.WorldWidth, 1f, 0.9f), gridColor);
            }

            // Threat emplacements: kill-radius ring + faint danger fill + marker.
            for (int t = 0; t < sc.ThreatCount; t++)
            {
                AddRing(sc.ThreatX[t], sc.ThreatY[t], Scenario.ThreatKillRadius, threatRing);
                AddDisc(sc.ThreatX[t], sc.ThreatY[t], Scenario.ThreatKillRadius, threatFill);
                AddDisc(sc.ThreatX[t], sc.ThreatY[t], 3.5f, threatRing);
            }

            // EW sites: jam-radius ring + faint fill + marker.
            for (int j = 0; j < sc.JammerCount; j++)
            {
                AddRing(sc.JammerX[j], sc.JammerY[j], Scenario.JammerRadius, jammerRing);
                AddDisc(sc.JammerX[j], sc.JammerY[j], Scenario.JammerRadius, jammerFill);
                AddDisc(sc.JammerX[j], sc.JammerY[j], 3f, jammerRing);
            }

            // Objective: target reticle — ring + crosshair, no puddle.
            AddRing(Scenario.ObjectiveX, Scenario.ObjectiveY, Scenario.ObjectiveRadius, objectiveColor);
            AddRing(Scenario.ObjectiveX, Scenario.ObjectiveY, Scenario.ObjectiveRadius * 0.55f, objectiveColor);
            AddQuad(new Vector3(Scenario.ObjectiveX, 0.22f, Scenario.ObjectiveY),
                    new Vector3(Scenario.ObjectiveRadius * 2.4f, 1f, 1.1f), objectiveColor);
            AddQuad(new Vector3(Scenario.ObjectiveX, 0.22f, Scenario.ObjectiveY),
                    new Vector3(1.1f, 1f, Scenario.ObjectiveRadius * 2.4f), objectiveColor);

            // Launch line at the top of the spawn band.
            AddQuad(new Vector3(Simulation.WorldWidth * 0.5f, 0.18f, Scenario.SpawnBandHeight),
                    new Vector3(Simulation.WorldWidth, 1f, 1.4f), spawnLine);
        }

        private void AddDisc(float x, float y, float radius, Color c)
        {
            _discM.Add(Matrix4x4.TRS(new Vector3(x, 0.2f, y), Quaternion.identity,
                                     new Vector3(radius * 2f, 1f, radius * 2f)));
            _discC.Add(c);
        }

        private void AddRing(float x, float y, float radius, Color c)
        {
            _ringM.Add(Matrix4x4.TRS(new Vector3(x, 0.25f, y), Quaternion.identity,
                                     new Vector3(radius * 2f, 1f, radius * 2f)));
            _ringC.Add(c);
        }

        private void AddQuad(Vector3 center, Vector3 size, Color c)
        {
            _quadM.Add(Matrix4x4.TRS(center, Quaternion.identity, size));
            _quadC.Add(c);
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

        /// <summary>Flat annulus in the XZ plane; innerScale is the inner radius
        /// as a fraction of the outer — thin outline, the range-ring idiom.</summary>
        private static Mesh BuildRing(int segments, float innerScale)
        {
            var verts = new Vector3[segments * 2];
            var tris = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float cx = Mathf.Cos(a) * 0.5f;
                float cz = Mathf.Sin(a) * 0.5f;
                verts[i * 2] = new Vector3(cx, 0f, cz);
                verts[i * 2 + 1] = new Vector3(cx * innerScale, 0f, cz * innerScale);
                int ni = (i + 1) % segments;
                tris[i * 6] = i * 2;
                tris[i * 6 + 1] = i * 2 + 1;
                tris[i * 6 + 2] = ni * 2;
                tris[i * 6 + 3] = ni * 2;
                tris[i * 6 + 4] = i * 2 + 1;
                tris[i * 6 + 5] = ni * 2 + 1;
            }
            var m = new Mesh { vertices = verts, triangles = tris };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildQuad()
        {
            var m = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, 0f, -0.5f), new Vector3(0.5f, 0f, -0.5f),
                    new Vector3(-0.5f, 0f, 0.5f), new Vector3(0.5f, 0f, 0.5f),
                },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
