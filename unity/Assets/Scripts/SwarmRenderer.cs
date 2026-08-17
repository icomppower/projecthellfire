using System.Collections.Generic;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// GPU-instanced swarm view. Reads SimDriver.State each frame; never writes.
    /// Sim (x, y) maps to world (x, 0, z) under a top-down camera. Status drives
    /// per-instance color; Active→Dead transitions fire the explosion pool —
    /// detected by diffing the presentation's own copy of last-frame statuses,
    /// so the sim needs no event plumbing.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class SwarmRenderer : MonoBehaviour
    {
        public float agentSize = 3f;
        public Color activeColor = new Color(0.35f, 0.95f, 1f);
        public Color completedColor = new Color(0.4f, 1f, 0.45f);
        public Color deadColor = new Color(1f, 0.25f, 0.15f);
        public Color safeColor = new Color(0.45f, 0.6f, 1f);
        public ExplosionPool explosions;

        private SimDriver _driver;
        private Mesh _mesh;
        private Material _material;
        private Matrix4x4[] _matrices;
        private Vector4[] _colors;
        private MaterialPropertyBlock _props;
        private byte[] _prevStatus;
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _mesh = BuildQuad();
            var shader = Shader.Find("Universal Render Pipeline/Unlit");
            _material = new Material(shader) { enableInstancing = true };
            _material.SetFloat("_Surface", 1f); // transparent
            _material.renderQueue = 3000;
            _props = new MaterialPropertyBlock();
        }

        private void LateUpdate()
        {
            var state = _driver.State;
            if (state == null) return;
            int n = state.AgentCount;
            if (_matrices == null || _matrices.Length != n)
            {
                _matrices = new Matrix4x4[n];
                _colors = new Vector4[n];
                _prevStatus = new byte[n];
                System.Array.Copy(state.Status, _prevStatus, n);
            }

            var rot = Quaternion.Euler(90f, 0f, 0f);
            var scale = new Vector3(agentSize, agentSize, agentSize);
            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                var pos = new Vector3(state.PosX[i], 0.5f, state.PosY[i]);
                _matrices[i] = Matrix4x4.TRS(pos, rot, scale);
                _colors[i] = ColorFor(status);

                if (explosions != null
                    && status == AgentStatus.Dead
                    && _prevStatus[i] != (byte)AgentStatus.Dead)
                {
                    explosions.Spawn(pos);
                }
                _prevStatus[i] = state.Status[i];
            }

            _props.SetVectorArray(ColorProp, _colors);
            var rp = new RenderParams(_material) { matProps = _props };
            Graphics.RenderMeshInstanced(rp, _mesh, 0, _matrices, n);
        }

        private Vector4 ColorFor(AgentStatus s)
        {
            switch (s)
            {
                case AgentStatus.Completed: return completedColor;
                case AgentStatus.Dead: return deadColor * 0.6f;
                case AgentStatus.Safe: return safeColor;
                default: return activeColor;
            }
        }

        private static Mesh BuildQuad()
        {
            var m = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0f), new Vector3(0.5f, -0.5f, 0f),
                    new Vector3(-0.5f, 0.5f, 0f), new Vector3(0.5f, 0.5f, 0f),
                },
                uv = new[] { Vector2.zero, Vector2.right, Vector2.up, Vector2.one },
                triangles = new[] { 0, 2, 1, 2, 3, 1 },
            };
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }
    }
}
