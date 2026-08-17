using System.Collections.Generic;
using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Live combat feedback (step 7 workstream 1): the sim knows which threat
    /// kills what — the nearest engaging threat drives the kill roll — but until
    /// now the renderer never showed it. This layer infers engagements purely
    /// renderer-side by mirroring Sim.Tick's logic (deaths via status diff,
    /// attacker = nearest threat within the doctrine's engage radius), so the
    /// sim core and its hash stay untouched.
    ///
    /// Draws: kill tracers (bright HDR beam from attacker muzzle to victim),
    /// suppressive tracer bursts while agents sit inside a threat's engage
    /// radius, and a pulsing engagement ring on actively-firing threats.
    /// Exposes: per-agent under-fire flags (SwarmRenderer flashes them) and the
    /// kill feed (TacticalLabels prints it). Presentation is free to be
    /// nondeterministic (§3), but everything here is frame-driven anyway.
    /// </summary>
    [RequireComponent(typeof(SimDriver))]
    public sealed class EngagementRenderer : MonoBehaviour
    {
        public struct FeedEntry
        {
            public float SimSeconds;
            public int ThreatIndex;
            public int AgentIndex;
            public DeathFlag Flags;
        }

        private struct Tracer
        {
            public Vector3 From;
            public Vector3 To;
            public float Born;
            public float Ttl;
            public Color Color;
            public float Width;
        }

        /// <summary>Assigned by SceneBootstrap — additive unlit; HDR colors here
        /// are what feeds bloom.</summary>
        public Material material;
        public float muzzleHeight = 6f;
        public float suppressiveInterval = 0.55f;
        public Color killBeam = new Color(6f, 1.6f, 0.5f, 1f);
        public Color suppressiveBeam = new Color(2.2f, 1.5f, 0.7f, 0.55f);
        public Color engagedRing = new Color(3f, 0.7f, 0.25f, 0.8f);

        /// <summary>Per-agent: inside some threat's engage radius this frame.</summary>
        public bool[] UnderFire { get; private set; }
        public IReadOnlyList<FeedEntry> Feed => _feed;

        private SimDriver _driver;
        private Mesh _beam;
        private Mesh _ring;
        private byte[] _prevStatus;
        private bool[] _threatEngaged;
        private int[] _threatTarget;
        private float[] _threatNextBurst;
        private readonly List<Tracer> _tracers = new List<Tracer>();
        private readonly List<FeedEntry> _feed = new List<FeedEntry>();
        private readonly List<Matrix4x4> _m = new List<Matrix4x4>();
        private readonly List<Vector4> _c = new List<Vector4>();
        private MaterialPropertyBlock _props;
        private static readonly int ColorProp = Shader.PropertyToID("_BaseColor");

        private void Awake()
        {
            _driver = GetComponent<SimDriver>();
            _beam = BuildBeam();
            _ring = BuildRing(48, 0.9f);
            _props = new MaterialPropertyBlock();
        }

        /// <summary>New run: forget the previous run's engagements.</summary>
        private void Reset(SimState state)
        {
            _prevStatus = new byte[state.AgentCount];
            System.Array.Copy(state.Status, _prevStatus, state.AgentCount);
            UnderFire = new bool[state.AgentCount];
            _tracers.Clear();
            _feed.Clear();
            int threats = _driver.Sim.Scenario.ThreatCount;
            _threatEngaged = new bool[threats];
            _threatTarget = new int[threats];
            _threatNextBurst = new float[threats];
            for (int t = 0; t < threats; t++)
            {
                // Stagger bursts so 14 threats don't strobe in sync.
                _threatNextBurst[t] = Time.time + t * 0.09f;
            }
        }

        private Simulation _builtFor;

        private void LateUpdate()
        {
            var state = _driver.State;
            var sim = _driver.Sim;
            if (state == null || sim == null || material == null) return;
            if (!ReferenceEquals(sim, _builtFor) || _prevStatus == null
                || _prevStatus.Length != state.AgentCount)
            {
                _builtFor = sim;
                Reset(state);
            }

            var sc = sim.Scenario;
            var doctrine = _driver.ActiveDoctrine;
            float engageRadius = Scenario.EngageRadius(doctrine.CommsDiscipline);
            float engageR2 = engageRadius * engageRadius;
            float flightY = SwarmRenderer.FlightHeight;

            // --- Under-fire census + per-threat nearest engaged target. ---
            int n = state.AgentCount;
            for (int t = 0; t < _threatEngaged.Length; t++)
            {
                _threatEngaged[t] = false;
                _threatTarget[t] = -1;
            }
            var nearestD2 = new float[_threatEngaged.Length];
            for (int i = 0; i < n; i++)
            {
                bool exposed = false;
                var status = (AgentStatus)state.Status[i];
                if (status == AgentStatus.Active)
                {
                    float px = state.PosX[i];
                    float py = state.PosY[i];
                    for (int t = 0; t < sc.ThreatCount; t++)
                    {
                        float dx = px - sc.ThreatX[t];
                        float dy = py - sc.ThreatY[t];
                        float d2 = dx * dx + dy * dy;
                        if (d2 <= engageR2)
                        {
                            exposed = true;
                            if (!_threatEngaged[t] || d2 < nearestD2[t])
                            {
                                _threatEngaged[t] = true;
                                nearestD2[t] = d2;
                                _threatTarget[t] = i;
                            }
                        }
                    }
                }
                UnderFire[i] = exposed;
            }

            // --- Death diff → kill tracer + explosion-adjacent feed entry. ---
            for (int i = 0; i < n; i++)
            {
                if (state.Status[i] == (byte)AgentStatus.Dead
                    && _prevStatus[i] != (byte)AgentStatus.Dead)
                {
                    var deathPos = new Vector3(state.PosX[i], flightY, state.PosY[i]);
                    int attacker = NearestThreat(sc, state.PosX[i], state.PosY[i]);
                    if (attacker >= 0)
                    {
                        var muzzle = new Vector3(sc.ThreatX[attacker], muzzleHeight, sc.ThreatY[attacker]);
                        _tracers.Add(new Tracer
                        {
                            From = muzzle,
                            To = deathPos,
                            Born = Time.time,
                            Ttl = 0.5f,
                            Color = killBeam,
                            Width = 1.1f,
                        });
                    }
                    _feed.Add(new FeedEntry
                    {
                        SimSeconds = state.DeathTick[i] * Simulation.FixedDt,
                        ThreatIndex = attacker,
                        AgentIndex = i,
                        Flags = (DeathFlag)state.DeathFlags[i],
                    });
                }
                _prevStatus[i] = state.Status[i];
            }

            // --- Suppressive bursts: fire is continuous while engaged, not only
            // at the kill — this is what makes "which threat attacks what"
            // readable before anyone dies. ---
            if (_driver.Running && !_driver.Finished && _driver.timeScale > 0f)
            {
                for (int t = 0; t < _threatEngaged.Length; t++)
                {
                    if (!_threatEngaged[t] || Time.time < _threatNextBurst[t]) continue;
                    _threatNextBurst[t] = Time.time + suppressiveInterval
                        * (0.7f + 0.6f * Mathf.PerlinNoise(t * 3.7f, Time.time));
                    int target = _threatTarget[t];
                    var muzzle = new Vector3(sc.ThreatX[t], muzzleHeight, sc.ThreatY[t]);
                    var aim = new Vector3(
                        state.PosX[target] + state.VelX[target] * 0.12f,
                        flightY,
                        state.PosY[target] + state.VelY[target] * 0.12f);
                    _tracers.Add(new Tracer
                    {
                        From = muzzle,
                        To = aim,
                        Born = Time.time,
                        Ttl = 0.28f,
                        Color = suppressiveBeam,
                        Width = 0.45f,
                    });
                }
            }

            DrawTracers();
            DrawEngagedRings(sc);
        }

        private int NearestThreat(Scenario sc, float px, float py)
        {
            int best = -1;
            float bestD2 = float.MaxValue;
            for (int t = 0; t < sc.ThreatCount; t++)
            {
                float dx = px - sc.ThreatX[t];
                float dy = py - sc.ThreatY[t];
                float d2 = dx * dx + dy * dy;
                if (d2 < bestD2) { bestD2 = d2; best = t; }
            }
            return best;
        }

        private void DrawTracers()
        {
            _m.Clear(); _c.Clear();
            float now = Time.time;
            for (int i = _tracers.Count - 1; i >= 0; i--)
            {
                var tr = _tracers[i];
                float age = (now - tr.Born) / tr.Ttl;
                if (age >= 1f) { _tracers.RemoveAt(i); continue; }
                var dir = tr.To - tr.From;
                float len = dir.magnitude;
                if (len < 0.01f) continue;
                float fade = 1f - age;
                var mid = (tr.From + tr.To) * 0.5f;
                _m.Add(Matrix4x4.TRS(mid, Quaternion.LookRotation(dir),
                    new Vector3(tr.Width * fade, tr.Width * fade, len)));
                var col = tr.Color;
                col.a *= fade;
                _c.Add(col);
            }
            Draw(_beam);
        }

        private void DrawEngagedRings(Scenario sc)
        {
            _m.Clear(); _c.Clear();
            float pulse = 0.6f + 0.4f * Mathf.Sin(Time.time * 9f);
            for (int t = 0; t < _threatEngaged.Length; t++)
            {
                if (!_threatEngaged[t]) continue;
                float r = Scenario.ThreatKillRadius * 2f;
                _m.Add(Matrix4x4.TRS(
                    new Vector3(sc.ThreatX[t], 0.4f, sc.ThreatY[t]),
                    Quaternion.identity, new Vector3(r, 1f, r)));
                var col = engagedRing;
                col.a *= pulse;
                _c.Add(col);
            }
            Draw(_ring);
        }

        private void Draw(Mesh mesh)
        {
            if (_m.Count == 0) return;
            _props.Clear();
            _props.SetVectorArray(ColorProp, _c);
            var rp = new RenderParams(material) { matProps = _props };
            Graphics.RenderMeshInstanced(rp, mesh, 0, _m, _m.Count);
        }

        /// <summary>Unit box centered on origin — scaled to (width, width, length)
        /// and LookRotated along the shot line.</summary>
        private static Mesh BuildBeam()
        {
            var v = new Vector3[8];
            for (int i = 0; i < 8; i++)
            {
                v[i] = new Vector3(
                    (i & 1) == 0 ? -0.5f : 0.5f,
                    (i & 2) == 0 ? -0.5f : 0.5f,
                    (i & 4) == 0 ? -0.5f : 0.5f);
            }
            var m = new Mesh
            {
                vertices = v,
                triangles = new[]
                {
                    0,2,1, 1,2,3,  4,5,6, 5,7,6,
                    0,1,4, 1,5,4,  2,6,3, 3,6,7,
                    0,4,2, 2,4,6,  1,3,5, 3,7,5,
                },
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
