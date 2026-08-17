using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// Deterministic core: fixed-timestep, seeded, single-threaded, pure transition
    /// tick(state, doctrine, seed) -> state. Zero UnityEngine types.
    ///
    /// Step 2 adds the minimum scenario semantics the scorer needs (transit a
    /// defended band; see Scenario). The doctrine→outcome mapping here is simple
    /// and direct on purpose — step 3 replaces it with interacting rules where the
    /// emergence lives. Determinism invariants are unchanged from step 1:
    /// order-independent hashed RNG, canonical-order neighbour queries, fixed
    /// iteration order.
    /// </summary>
    public sealed class Simulation
    {
        public const float WorldWidth = 512f;
        public const float WorldHeight = 512f;
        public const float FixedDt = 1f / 60f;
        private const float CellSize = 12f;

        // Steering constants (velocity-delta units per tick unless noted).
        private const float SteerRate = 4.0f;      // 1/s blend toward desired velocity
        private const float AvoidDeltaMax = 3.0f;  // max repulsion delta per threat per tick
        private const float CompletedDamp = 0.85f; // completed agents brake and hold
        private const float RouteShapeRange = 2.5f;  // route-shaping reach, in avoid radii
        private const float RouteShapeFactor = 0.25f; // route-shaping strength vs local dodge

        private readonly SpatialHash _hash;
        private readonly int[] _queryScratch;
        private readonly Scenario _scenario;
        private readonly ulong _seed;

        public Scenario Scenario => _scenario;

        public Simulation(int maxAgents, ulong seed)
        {
            _hash = new SpatialHash(WorldWidth, WorldHeight, CellSize, maxAgents);
            _queryScratch = new int[maxAgents];
            _scenario = new Scenario(seed);
            _seed = seed;
        }

        public static SimState CreateInitialState(int agentCount, ulong seed)
        {
            var s = new SimState(agentCount);
            for (int i = 0; i < agentCount; i++)
            {
                ulong id = (ulong)i;
                s.PosX[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosX) * WorldWidth;
                s.PosY[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosY) * Scenario.SpawnBandHeight;
                s.VelX[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelX) * 5f;
                s.VelY[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelY) * 5f;
            }
            return s;
        }

        private enum Tag : ulong
        {
            InitPosX = 1, InitPosY = 2, InitVelX = 3, InitVelY = 4,
            JitterX = 5, JitterY = 6,
            Kill = 7,
        }

        /// <summary>
        /// Advances <paramref name="state"/> by one fixed step, in place.
        /// Same (state, doctrine, seed) always produces the same next state.
        /// </summary>
        public void Tick(SimState state, Doctrine doctrine, ulong seed)
        {
            int n = state.AgentCount;
            ulong tick = (ulong)state.Tick;
            _hash.Build(state.PosX, state.PosY, n);

            float engageRadius = Scenario.EngageRadius(doctrine.CommsDiscipline);
            float engageR2 = engageRadius * engageRadius;
            // Give a wide berth when risk-averse; hug the kill zone when aggressive.
            float avoidRadius = engageRadius * (1f + 1.5f * (1f - doctrine.RiskTolerance));
            float avoidStrength = AvoidDeltaMax * (1f - 0.7f * doctrine.RiskTolerance);
            // Autonomy dial: centralized shares all threat positions; decentralized
            // knows only what its own sensor sees. Continuous blend between the two.
            float knowledgeRadius = doctrine.SensorRange
                + (1f - doctrine.Autonomy) * (720f - doctrine.SensorRange);
            float knowledgeR2 = knowledgeRadius * knowledgeRadius;
            float routeRadius = avoidRadius * RouteShapeRange;
            float routeR2 = routeRadius * routeRadius;
            // Chatty comms coordinate the swarm: less wander scatter, faster transit.
            float jitterScale = doctrine.JitterAccel * (0.4f + 0.6f * doctrine.CommsDiscipline);
            float steerBlend = Math.Min(1f, SteerRate * FixedDt);
            var threats = _scenario;

            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                if (status == AgentStatus.Dead || status == AgentStatus.Safe) continue;

                float px = state.PosX[i];
                float py = state.PosY[i];
                float vx = state.VelX[i];
                float vy = state.VelY[i];

                if (status == AgentStatus.Completed)
                {
                    // Payload delivered: brake and hold inside the objective.
                    vx *= CompletedDamp;
                    vy *= CompletedDamp;
                    state.PosX[i] = px + vx * FixedDt;
                    state.PosY[i] = py + vy * FixedDt;
                    state.VelX[i] = vx;
                    state.VelY[i] = vy;
                    continue;
                }

                ulong id = (ulong)i;

                // --- Target: objective, or home once the swarm has aborted. ---
                float tx, ty;
                if (state.Aborted) { tx = px; ty = 0f; }
                else { tx = Scenario.ObjectiveX; ty = Scenario.ObjectiveY; }

                float dxT = tx - px;
                float dyT = ty - py;
                float distT = (float)Math.Sqrt(dxT * dxT + dyT * dyT);
                float desiredX = 0f, desiredY = 0f;
                if (distT > 1e-3f)
                {
                    desiredX = dxT / distT * doctrine.MaxSpeed;
                    desiredY = dyT / distT * doctrine.MaxSpeed;
                }
                vx += (desiredX - vx) * steerBlend;
                vy += (desiredY - vy) * steerBlend;

                // --- Threat avoidance (known threats only) + exposure census (all). ---
                int threatsInKillRange = 0;
                for (int t = 0; t < Scenario.ThreatCount; t++)
                {
                    float dx = px - threats.ThreatX[t];
                    float dy = py - threats.ThreatY[t];
                    float d2 = dx * dx + dy * dy;

                    // World truth: you die from threats you never knew about.
                    if (d2 <= engageR2) threatsInKillRange++;

                    if (d2 <= knowledgeR2 && d2 <= routeR2 && d2 > 1e-6f)
                    {
                        float d = (float)Math.Sqrt(d2);
                        if (d < avoidRadius)
                        {
                            // Local dodge — hard repulsion inside the danger bubble.
                            float w = avoidStrength * (1f - d / avoidRadius);
                            vx += dx / d * w;
                            vy += dy / d * w;
                        }
                        else
                        {
                            // Route shaping — gentle early curve around *known* threats.
                            // This is where centralized intel pays: decentralized agents
                            // have knowledgeRadius clipped to SensorRange and never see
                            // threats early enough to reroute.
                            float w = RouteShapeFactor * avoidStrength * (1f - d / routeRadius);
                            vx += dx / d * w;
                            vy += dy / d * w;
                        }
                    }
                }

                // --- Hashed-RNG wander (independent of every other draw). ---
                vx += DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterX) * jitterScale;
                vy += DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterY) * jitterScale;

                // --- Crowd damping via canonical-order neighbour query (step-1 path). ---
                int nearby = _hash.QueryRadius(px, py, doctrine.NeighborRadius, i,
                                               state.PosX, state.PosY, _queryScratch);
                int liveNeighbors = 0;
                for (int k = 0; k < nearby; k++)
                {
                    if (state.Status[_queryScratch[k]] != (byte)AgentStatus.Dead) liveNeighbors++;
                }
                float damp = 1f - Math.Min(0.9f, doctrine.CrowdDampPerNeighbor * liveNeighbors);
                vx *= damp;
                vy *= damp;

                float speed2 = vx * vx + vy * vy;
                float max2 = doctrine.MaxSpeed * doctrine.MaxSpeed;
                if (speed2 > max2)
                {
                    float scale = doctrine.MaxSpeed / (float)Math.Sqrt(speed2);
                    vx *= scale;
                    vy *= scale;
                }

                px += vx * FixedDt;
                py += vy * FixedDt;

                if (px < 0f) { px = -px; vx = -vx; }
                else if (px > WorldWidth) { px = 2f * WorldWidth - px; vx = -vx; }
                if (py < 0f) { py = -py; vy = -vy; }
                else if (py > WorldHeight) { py = 2f * WorldHeight - py; vy = -vy; }

                state.PosX[i] = px;
                state.PosY[i] = py;
                state.VelX[i] = vx;
                state.VelY[i] = vy;

                // --- Attrition roll: one draw regardless of how many threats engage. ---
                if (threatsInKillRange > 0)
                {
                    float killProb = Scenario.ThreatBaseKillProb * threatsInKillRange;
                    if (DetHash.Float01(seed, tick, id, (ulong)Tag.Kill) < killProb)
                    {
                        state.Status[i] = (byte)AgentStatus.Dead;
                        continue;
                    }
                }

                // --- Latched outcomes. ---
                if (!state.Aborted)
                {
                    float dxO = px - Scenario.ObjectiveX;
                    float dyO = py - Scenario.ObjectiveY;
                    if (dxO * dxO + dyO * dyO <= Scenario.ObjectiveRadius * Scenario.ObjectiveRadius)
                    {
                        state.Status[i] = (byte)AgentStatus.Completed;
                    }
                }
                else if (py <= Scenario.SpawnBandHeight)
                {
                    state.Status[i] = (byte)AgentStatus.Safe;
                }
            }

            // --- Doctrine loss threshold: swarm-level abort latch. ---
            if (!state.Aborted)
            {
                int dead = state.CountStatus(AgentStatus.Dead);
                if (dead > doctrine.AbortLossFraction * n) state.Aborted = true;
            }

            state.Tick++;
        }

        /// <summary>
        /// Runs a seeded episode, ending early once no agent is still Active.
        /// </summary>
        public static SimState Run(ulong seed, int agentCount, int maxTicks, Doctrine doctrine)
        {
            var sim = new Simulation(agentCount, seed);
            var state = CreateInitialState(agentCount, seed);
            for (int t = 0; t < maxTicks; t++)
            {
                sim.Tick(state, doctrine, seed);
                if (state.CountStatus(AgentStatus.Active) == 0) break;
            }
            return state;
        }
    }
}
