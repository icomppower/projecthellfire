using System.Collections.Generic;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// GPU-instanced swarm view. Step 7: live agents are 3D low-poly craft
    /// (Kenney CC0, baked by SceneBootstrap into an instancing-ready mesh whose
    /// accent submesh carries the per-instance status tint), flying at altitude
    /// over the terrain, with a status ring underneath (flashes while under
    /// fire — data from EngagementRenderer), motion trails, and debris diamonds
    /// where the dead fell. Reads SimDriver.State each frame; never writes.
    /// Sim (x, y) maps to world (x, altitude, z).
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class SwarmRenderer : MonoBehaviour
    {
        /// <summary>Cruise altitude of the swarm view — shared with the tracer
        /// layer so beams terminate on the craft, not the ground.</summary>
        public const float FlightHeight = 8f;

        public float craftSize = 7f;
        public Color activeColor = new Color(0.35f, 0.95f, 1f);
        public Color completedColor = new Color(0.4f, 1f, 0.45f);
        public Color deadColor = new Color(0.85f, 0.2f, 0.12f, 0.8f);
        public Color safeColor = new Color(0.45f, 0.6f, 1f);
        public Color reserveColor = new Color(0.55f, 0.65f, 0.75f, 0.9f);
        public Color jammedTint = new Color(0.7f, 0.4f, 1f);
        public Color underFireFlash = new Color(4f, 0.9f, 0.5f);
        public ExplosionPool explosions;
        public EngagementRenderer engagement;
        /// <summary>Assigned by SceneBootstrap from a saved material asset — a
        /// runtime Shader.Find would return null in a player build (the shader
        /// never gets included), which is exactly how v1.1's first build shipped
        /// a black screen. The asset reference is what pulls the shader in.
        /// This one draws the unlit overlay marks (rings, trails, debris).</summary>
        public Material material;
        /// <summary>Baked craft mesh + per-submesh lit materials (SceneBootstrap).
        /// accentSubmesh is drawn with the per-instance status color instead of
        /// its baked color — the whole craft stays a lit 3D model, the accent
        /// carries the tactical tint.</summary>
        public Mesh craftMesh;
        public Material[] craftMaterials;
        public int accentSubmesh = -1;
        /// <summary>Model-forward correction if the FBX nose does not face +Z —
        /// the one render truth headless gates cannot check; adjustable without
        /// a code round.</summary>
        public float modelYawOffset = 0f;

        private SimDriver _driver;
        private Mesh _chevron;
        private Mesh _diamond;
        private Mesh _quad;
        private Mesh _ring;
        private Material _material;
        private MaterialPropertyBlock _props;
        private byte[] _prevStatus;
        private readonly List<Matrix4x4> _craftM = new List<Matrix4x4>();
        private readonly List<Vector4> _craftC = new List<Vector4>();
        private readonly List<Matrix4x4> _ringM = new List<Matrix4x4>();
        private readonly List<Vector4> _ringC = new List<Vector4>();
        private readonly List<Matrix4x4> _diaM = new List<Matrix4x4>();
        private readonly List<Vector4> _diaC = new List<Vector4>();
        private readonly List<Matrix4x4> _trailM = new List<Matrix4x4>();
        private readonly List<Vector4> _trailC = new List<Vector4>();
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _chevron = BuildChevron();
            _diamond = BuildDiamond();
            _quad = BuildQuad();
            _ring = BuildRing(40, 0.82f);
            _material = material;
            if (_material == null)
            {
                // Editor-only fallback; never reachable in a correct build.
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader != null) _material = new Material(shader) { enableInstancing = true };
            }
            _props = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            var state = _driver.State;
            if (state == null || _material == null) return;
            int n = state.AgentCount;
            if (_prevStatus == null || _prevStatus.Length != n)
            {
                _prevStatus = new byte[n];
                System.Array.Copy(state.Status, _prevStatus, n);
            }

            _craftM.Clear(); _craftC.Clear();
            _ringM.Clear(); _ringC.Clear();
            _diaM.Clear(); _diaC.Clear();
            _trailM.Clear(); _trailC.Clear();
            var scenario = _driver.Sim.Scenario;
            var yawFix = Quaternion.Euler(0f, modelYawOffset, 0f);
            bool flashOn = Mathf.Repeat(Time.time, 0.24f) < 0.13f;
            var underFire = engagement != null ? engagement.UnderFire : null;

            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                Vector4 color = ColorFor(status);
                if (status != AgentStatus.Dead
                    && scenario.IsJammed(state.PosX[i], state.PosY[i]))
                {
                    color = Vector4.Lerp(color, (Vector4)jammedTint, 0.55f);
                }

                if (status == AgentStatus.Dead)
                {
                    var ground = new Vector3(state.PosX[i], 0.6f, state.PosY[i]);
                    float s = craftSize * 0.5f;
                    _diaM.Add(Matrix4x4.TRS(ground, Quaternion.identity, new Vector3(s, s, s)));
                    _diaC.Add(color);
                }
                else
                {
                    bool parked = status == AgentStatus.Reserve;
                    float alt = parked ? 1.2f : FlightHeight;
                    var pos = new Vector3(state.PosX[i], alt, state.PosY[i]);
                    var vel = new Vector3(state.VelX[i], 0f, state.VelY[i]);
                    float speed = vel.magnitude;
                    var rot = speed > 0.05f
                        ? Quaternion.LookRotation(vel) * yawFix
                        : yawFix;
                    float scale = parked ? craftSize * 0.8f : craftSize;
                    _craftM.Add(Matrix4x4.TRS(pos, rot, new Vector3(scale, scale, scale)));

                    bool flashing = underFire != null && i < underFire.Length
                        && underFire[i] && flashOn;
                    Vector4 markColor = flashing ? (Vector4)underFireFlash : color;
                    _craftC.Add(markColor);
                    _ringM.Add(Matrix4x4.TRS(
                        new Vector3(pos.x, 0.5f, pos.z), Quaternion.identity,
                        new Vector3(craftSize * 1.5f, 1f, craftSize * 1.5f)));
                    var ringCol = markColor;
                    ringCol.w = flashing ? 0.95f : 0.5f;
                    _ringC.Add(ringCol);

                    if (status == AgentStatus.Active && speed > 2f)
                    {
                        float len = Mathf.Clamp(speed * 0.35f, 2.5f, 12f);
                        var dir = vel / speed;
                        _trailM.Add(Matrix4x4.TRS(
                            pos - dir * (len * 0.5f + craftSize * 0.5f),
                            Quaternion.LookRotation(dir), new Vector3(1f, 1f, len)));
                        var faded = color; faded.w *= 0.3f;
                        _trailC.Add(faded);
                    }
                }

                if (explosions != null
                    && status == AgentStatus.Dead
                    && _prevStatus[i] != (byte)AgentStatus.Dead)
                {
                    explosions.Spawn(new Vector3(state.PosX[i], FlightHeight, state.PosY[i]));
                }
                _prevStatus[i] = state.Status[i];
            }

            DrawOverlay(_trailM, _trailC, _quad);
            DrawOverlay(_ringM, _ringC, _ring);
            DrawOverlay(_diaM, _diaC, _diamond);
            DrawCraft();
        }

        /// <summary>Craft body: one instanced draw per submesh, lit, shadowed;
        /// the accent submesh gets the per-instance status colors.</summary>
        private void DrawCraft()
        {
            if (_craftM.Count == 0) return;
            if (craftMesh == null || craftMaterials == null || craftMaterials.Length == 0)
            {
                // Old-scene fallback: flat chevrons (pre-step-7 look).
                DrawOverlay(_craftM, _craftC, _chevron);
                return;
            }
            int subs = Mathf.Min(craftMesh.subMeshCount, craftMaterials.Length);
            for (int s = 0; s < subs; s++)
            {
                var rp = new RenderParams(craftMaterials[s])
                {
                    shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On,
                };
                if (s == accentSubmesh)
                {
                    _props.Clear();
                    _props.SetVectorArray(ColorProp, _craftC);
                    rp.matProps = _props;
                }
                Graphics.RenderMeshInstanced(rp, craftMesh, s, _craftM, _craftM.Count);
            }
        }

        private void DrawOverlay(List<Matrix4x4> m, List<Vector4> c, Mesh mesh)
        {
            if (m.Count == 0) return;
            _props.Clear();
            _props.SetVectorArray(ColorProp, c);
            var rp = new RenderParams(_material) { matProps = _props };
            Graphics.RenderMeshInstanced(rp, mesh, 0, m, m.Count);
        }

        private Vector4 ColorFor(AgentStatus s)
        {
            switch (s)
            {
                case AgentStatus.Completed: return completedColor;
                case AgentStatus.Dead: return deadColor;
                case AgentStatus.Safe: return safeColor;
                case AgentStatus.Reserve: return reserveColor;
                default: return activeColor;
            }
        }

        /// <summary>Flat dart in the XZ plane, nose toward +Z (LookRotation forward).</summary>
        private static Mesh BuildChevron()
        {
            var m = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0.55f),    // nose
                    new Vector3(-0.38f, 0f, -0.45f), // left wingtip
                    new Vector3(0f, 0f, -0.18f),   // tail notch
                    new Vector3(0.38f, 0f, -0.45f),  // right wingtip
                },
                triangles = new[] { 0, 2, 1, 0, 3, 2 },
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        private static Mesh BuildDiamond()
        {
            var m = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(0f, 0f, 0.5f), new Vector3(0.5f, 0f, 0f),
                    new Vector3(0f, 0f, -0.5f), new Vector3(-0.5f, 0f, 0f),
                },
                triangles = new[] { 0, 1, 3, 1, 2, 3 },
            };
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
    }
}
