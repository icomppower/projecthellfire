using System.Collections.Generic;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// GPU-instanced swarm view in aircraft vocabulary — playtest finding: dots
    /// read as cells, so live agents are velocity-oriented chevrons with motion
    /// trails, and the dead are small debris diamonds. Reads SimDriver.State
    /// each frame; never writes. Sim (x, y) maps to world (x, 0, z).
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class SwarmRenderer : MonoBehaviour
    {
        public float agentSize = 4.2f;
        public Color activeColor = new Color(0.35f, 0.95f, 1f);
        public Color completedColor = new Color(0.4f, 1f, 0.45f);
        public Color deadColor = new Color(0.85f, 0.2f, 0.12f, 0.8f);
        public Color safeColor = new Color(0.45f, 0.6f, 1f);
        public Color reserveColor = new Color(0.55f, 0.65f, 0.75f, 0.9f);
        public Color jammedTint = new Color(0.7f, 0.4f, 1f);
        public ExplosionPool explosions;
        /// <summary>Assigned by SceneBootstrap from a saved material asset — a
        /// runtime Shader.Find would return null in a player build (the shader
        /// never gets included), which is exactly how v1.1's first build shipped
        /// a black screen. The asset reference is what pulls the shader in.</summary>
        public Material material;

        private SimDriver _driver;
        private Mesh _chevron;
        private Mesh _diamond;
        private Mesh _quad;
        private Material _material;
        private MaterialPropertyBlock _props;
        private byte[] _prevStatus;
        private readonly List<Matrix4x4> _chevM = new List<Matrix4x4>();
        private readonly List<Vector4> _chevC = new List<Vector4>();
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

            _chevM.Clear(); _chevC.Clear();
            _diaM.Clear(); _diaC.Clear();
            _trailM.Clear(); _trailC.Clear();
            var scenario = _driver.Sim.Scenario;

            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                var pos = new Vector3(state.PosX[i], 0.5f, state.PosY[i]);
                Vector4 color = ColorFor(status);
                if (status != AgentStatus.Dead
                    && scenario.IsJammed(state.PosX[i], state.PosY[i]))
                {
                    color = Vector4.Lerp(color, (Vector4)jammedTint, 0.5f);
                }

                if (status == AgentStatus.Dead || status == AgentStatus.Reserve)
                {
                    float s = status == AgentStatus.Dead ? agentSize * 0.55f : agentSize * 0.7f;
                    _diaM.Add(Matrix4x4.TRS(pos, Quaternion.identity, new Vector3(s, s, s)));
                    _diaC.Add(color);
                }
                else
                {
                    var vel = new Vector3(state.VelX[i], 0f, state.VelY[i]);
                    float speed = vel.magnitude;
                    var rot = speed > 0.05f
                        ? Quaternion.LookRotation(vel)
                        : Quaternion.identity;
                    _chevM.Add(Matrix4x4.TRS(pos, rot, new Vector3(agentSize, agentSize, agentSize)));
                    _chevC.Add(color);

                    if (status == AgentStatus.Active && speed > 2f)
                    {
                        float len = Mathf.Clamp(speed * 0.28f, 2f, 9f);
                        var dir = vel / speed;
                        _trailM.Add(Matrix4x4.TRS(
                            pos - dir * (len * 0.5f + agentSize * 0.45f) + Vector3.down * 0.1f,
                            rot, new Vector3(0.8f, 1f, len)));
                        var faded = color; faded.w *= 0.3f;
                        _trailC.Add(faded);
                    }
                }

                if (explosions != null
                    && status == AgentStatus.Dead
                    && _prevStatus[i] != (byte)AgentStatus.Dead)
                {
                    explosions.Spawn(pos);
                }
                _prevStatus[i] = state.Status[i];
            }

            Draw(_trailM, _trailC, _quad);
            Draw(_chevM, _chevC, _chevron);
            Draw(_diaM, _diaC, _diamond);
        }

        private void Draw(List<Matrix4x4> m, List<Vector4> c, Mesh mesh)
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
    }
}
