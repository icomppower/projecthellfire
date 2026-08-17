using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// Deterministic core: fixed-timestep, seeded, single-threaded, pure transition
    /// tick(state, doctrine, seed) -> state. Zero UnityEngine types.
    ///
    /// Step 3 adds the interacting rules the emergence gate judges:
    /// - Boids (separation / alignment / cohesion) over canonical-order
    ///   spatial-hash neighbour queries.
    /// - The EW layer: network share = f(autonomy, comms discipline); jammer
    ///   zones strip it. Centralized swarms are smart until jammed (§1).
    /// - Death-cause attribution flags (§2 diagnosability contract).
    ///
    /// Determinism invariants unchanged: order-independent hashed RNG, canonical
    /// neighbour order, fixed ascending agent iteration (in-place update — agent i
    /// reads j&lt;i post-update; sequential by design, deterministic always).
    /// </summary>
    public sealed class Simulation
    {
        public const float WorldWidth = 512f;
        public const float WorldHeight = 512f;
        public const float FixedDt = 1f / 60f;
        private const float CellSize = 12f;

        // Steering constants (velocity-delta units per tick unless noted).
        private const float SteerRate = 4.0f;        // 1/s blend toward desired velocity
        private const float AvoidDeltaMax = 3.0f;    // max repulsion delta per threat per tick
        private const float CompletedDamp = 0.85f;   // completed agents brake and hold
        private const float RouteShapeRange = 2.5f;  // route-shaping reach, in avoid radii
        private const float RouteShapeFactor = 0.25f; // route-shaping strength vs local dodge
        // Boids.
        private const int MaxBoidNeighbors = 32;     // deterministic cap (canonical ascending order)
        private const float SeparationDelta = 1.2f;  // per-tick push inside the separation bubble
        private const float AlignRate = 2.4f;        // 1/s blend toward flock heading, at full network
        private const float CohesionDelta = 1.4f;    // max per-tick pull toward flock center
        // EW.
        private const float NetworkFloor = 0.05f;    // network share retained under full comms silence
        private const float AlignFloor = 0.3f;       // alignment retained with zero network
        private const float JamPanicJitter = 3.5f;   // absolute extra wander delta when a networked swarm is jammed
        // Outsourced attention: how much of its OWN sensor a network-reliant swarm
        // loses when the network drops. This is what makes jamming an inversion —
        // worse than never having the network — rather than a mere equalizer.
        private const float JamDependencyPenalty = 0.8f;

        private readonly SpatialHash _hash;
        private readonly int[] _queryScratch;
        private readonly Scenario _scenario;

        public Scenario Scenario => _scenario;

        public Simulation(int maxAgents, ulong seed) : this(maxAgents, seed, ScenarioConfig.Default) { }

        public Simulation(int maxAgents, ulong seed, ScenarioConfig config)
        {
            _hash = new SpatialHash(WorldWidth, WorldHeight, CellSize, maxAgents);
            _queryScratch = new int[maxAgents];
            _scenario = new Scenario(seed, config);
        }

        public static SimState CreateInitialState(int agentCount, ulong seed, float reserveFraction = 0f)
        {
            var s = new SimState(agentCount);
            // Highest indices are the reserve: held at spawn, zero velocity,
            // launched only by the CommitReserve interrupt.
            int reserveStart = agentCount - (int)(agentCount * reserveFraction);
            for (int i = 0; i < agentCount; i++)
            {
                ulong id = (ulong)i;
                s.PosX[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosX) * WorldWidth;
                s.PosY[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosY) * Scenario.SpawnBandHeight;
                if (i >= reserveStart)
                {
                    s.Status[i] = (byte)AgentStatus.Reserve;
                }
                else
                {
                    s.VelX[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelX) * 5f;
                    s.VelY[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelY) * 5f;
                }
            }
            return s;
        }

        /// <summary>Number of ticks a FallBack interrupt keeps the swarm recalled.</summary>
        public const int RecallDurationTicks = 600;

        /// <summary>Doctrine-level commander interrupts (GDD §1) — swarm-wide
        /// state changes only, never a per-unit order. Deterministic: replay is
        /// (seed, doctrine, tick-stamped plan).</summary>
        public static void ApplyInterrupt(SimState state, InterruptType type)
        {
            switch (type)
            {
                case InterruptType.Abort:
                    state.Aborted = true;
                    break;
                case InterruptType.FallBack:
                    state.RecallUntilTick = state.Tick + RecallDurationTicks;
                    break;
                case InterruptType.CommitReserve:
                    for (int i = 0; i < state.AgentCount; i++)
                    {
                        if (state.Status[i] == (byte)AgentStatus.Reserve)
                        {
                            state.Status[i] = (byte)AgentStatus.Active;
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(type));
            }
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
            float baseKillR2 = Scenario.ThreatKillRadius * Scenario.ThreatKillRadius;
            // Give a wide berth when risk-averse; hug the kill zone when aggressive.
            float avoidRadius = engageRadius * (1f + 1.5f * (1f - doctrine.RiskTolerance));
            float avoidStrength = AvoidDeltaMax * (1f - 0.7f * doctrine.RiskTolerance);
            float routeRadius = avoidRadius * RouteShapeRange;
            float routeR2 = routeRadius * routeRadius;
            // Network share: what centralization buys — but only what comms carry
            // and only where no jammer reaches (§1: smart until jammed).
            float networkShare = (1f - doctrine.Autonomy)
                * (NetworkFloor + (1f - NetworkFloor) * (1f - doctrine.CommsDiscipline));
            float baseJitter = doctrine.JitterAccel * (0.4f + 0.6f * doctrine.CommsDiscipline);
            float sepRadius = doctrine.NeighborRadius * 0.5f;
            float steerBlend = Math.Min(1f, SteerRate * FixedDt);
            var sc = _scenario;

            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                if (status == AgentStatus.Dead || status == AgentStatus.Safe
                    || status == AgentStatus.Reserve) continue;

                float px = state.PosX[i];
                float py = state.PosY[i];
                float vx = state.VelX[i];
                float vy = state.VelY[i];

                if (status == AgentStatus.Completed)
                {
                    vx *= CompletedDamp;
                    vy *= CompletedDamp;
                    state.PosX[i] = px + vx * FixedDt;
                    state.PosY[i] = py + vy * FixedDt;
                    state.VelX[i] = vx;
                    state.VelY[i] = vy;
                    continue;
                }

                ulong id = (ulong)i;

                // --- EW state for this agent, this tick. ---
                bool jammed = sc.IsJammed(px, py);
                float effNet = jammed ? 0f : networkShare;
                // Jammed + network-reliant = blinder than a swarm that never had the
                // network: outsourced attention degrades the agent's own sensor.
                float effSensor = jammed
                    ? doctrine.SensorRange * (1f - JamDependencyPenalty * networkShare)
                    : doctrine.SensorRange;
                float knowledgeRadius = effSensor + effNet * (720f - effSensor);
                float knowledgeR2 = knowledgeRadius * knowledgeRadius;

                // --- Target: objective, or home while aborted / recalled. ---
                float tx, ty;
                if (state.Aborted || state.Tick < state.RecallUntilTick) { tx = px; ty = 0f; }
                else { tx = Scenario.ObjectiveX; ty = Scenario.ObjectiveY; }

                float dxT = tx - px;
                float dyT = ty - py;
                float distT = (float)Math.Sqrt(dxT * dxT + dyT * dyT);
                if (distT > 1e-3f)
                {
                    vx += (dxT / distT * doctrine.MaxSpeed - vx) * steerBlend;
                    vy += (dyT / distT * doctrine.MaxSpeed - vy) * steerBlend;
                }

                // --- Threats: avoidance of *known* ones; exposure census of all. ---
                int threatsInKillRange = 0;
                float nearestEngageD2 = float.MaxValue;
                bool nearestKnown = false;
                for (int t = 0; t < sc.ThreatCount; t++)
                {
                    float dx = px - sc.ThreatX[t];
                    float dy = py - sc.ThreatY[t];
                    float d2 = dx * dx + dy * dy;
                    bool known = d2 <= knowledgeR2;

                    // World truth: you die from threats you never knew about.
                    if (d2 <= engageR2)
                    {
                        threatsInKillRange++;
                        if (d2 < nearestEngageD2) { nearestEngageD2 = d2; nearestKnown = known; }
                    }

                    if (known && d2 <= routeR2 && d2 > 1e-6f)
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
                            // Route shaping — gentle early curve around known threats.
                            float w = RouteShapeFactor * avoidStrength * (1f - d / routeRadius);
                            vx += dx / d * w;
                            vy += dy / d * w;
                        }
                    }
                }

                // --- Boids over canonical-order neighbours (Active peers only). ---
                int nearby = _hash.QueryRadius(px, py, doctrine.NeighborRadius, i,
                                               state.PosX, state.PosY, _queryScratch);
                int flock = 0;
                float cx = 0f, cy = 0f, avx = 0f, avy = 0f;
                int liveNeighbors = 0;
                for (int k = 0; k < nearby; k++)
                {
                    int j = _queryScratch[k];
                    if (state.Status[j] == (byte)AgentStatus.Dead) continue;
                    liveNeighbors++;
                    if (state.Status[j] != (byte)AgentStatus.Active) continue;
                    if (flock >= MaxBoidNeighbors) continue;
                    flock++;
                    cx += state.PosX[j];
                    cy += state.PosY[j];
                    avx += state.VelX[j];
                    avy += state.VelY[j];

                    // Separation: hard bubble, always on (collision physics, not doctrine).
                    float sdx = px - state.PosX[j];
                    float sdy = py - state.PosY[j];
                    float sd2 = sdx * sdx + sdy * sdy;
                    if (sd2 < sepRadius * sepRadius && sd2 > 1e-6f)
                    {
                        float sd = (float)Math.Sqrt(sd2);
                        float w = SeparationDelta * (1f - sd / sepRadius);
                        vx += sdx / sd * w;
                        vy += sdy / sd * w;
                    }
                }
                if (flock > 0)
                {
                    float inv = 1f / flock;
                    // Alignment: coordination is a network product — silence and
                    // jamming both starve it.
                    float alignBlend = AlignRate * FixedDt * (AlignFloor + (1f - AlignFloor) * effNet);
                    vx += (avx * inv - vx) * alignBlend;
                    vy += (avy * inv - vy) * alignBlend;

                    // Cohesion: doctrine's formation bet — including the herd-risk cost.
                    float gx = cx * inv - px;
                    float gy = cy * inv - py;
                    float gd = (float)Math.Sqrt(gx * gx + gy * gy);
                    if (gd > 1e-3f)
                    {
                        float w = CohesionDelta * doctrine.Cohesion * Math.Min(1f, gd / doctrine.NeighborRadius);
                        vx += gx / gd * w;
                        vy += gy / gd * w;
                    }
                }

                // --- Hashed-RNG wander; jam panic scatters networked swarms. ---
                float jitterScale = jammed ? baseJitter + JamPanicJitter * networkShare : baseJitter;
                vx += DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterX) * jitterScale;
                vy += DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterY) * jitterScale;

                // --- Crowd damping (step-1 path, kept). ---
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

                // --- Attrition roll + §2 attribution. ---
                if (threatsInKillRange > 0)
                {
                    float killProb = Scenario.ThreatBaseKillProb * threatsInKillRange;
                    if (DetHash.Float01(seed, tick, id, (ulong)Tag.Kill) < killProb)
                    {
                        var flags = DeathFlag.None;
                        if (!nearestKnown) flags |= DeathFlag.UnknownThreat;
                        else flags |= DeathFlag.PressedKnown;
                        if (jammed) flags |= DeathFlag.Jammed;
                        if (nearestEngageD2 > baseKillR2) flags |= DeathFlag.Detected;
                        state.Status[i] = (byte)AgentStatus.Dead;
                        state.DeathFlags[i] = (byte)flags;
                        state.DeathTick[i] = state.Tick + 1;
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

            // --- Jam-exposure census: recomputed from scratch every tick. ---
            int jammedNow = 0;
            for (int i = 0; i < n; i++)
            {
                var status = (AgentStatus)state.Status[i];
                if (status == AgentStatus.Dead || status == AgentStatus.Safe
                    || status == AgentStatus.Reserve) continue;
                if (sc.IsJammed(state.PosX[i], state.PosY[i])) jammedNow++;
            }
            state.JammedNowCount = jammedNow;

            state.Tick++;
        }

        /// <summary>
        /// Runs a seeded episode, ending early once no agent is still Active.
        /// </summary>
        public static SimState Run(ulong seed, int agentCount, int maxTicks, Doctrine doctrine)
            => Run(seed, agentCount, maxTicks, doctrine, ScenarioConfig.Default, InterruptPlan.None);

        public static SimState Run(ulong seed, int agentCount, int maxTicks, Doctrine doctrine, ScenarioConfig config)
            => Run(seed, agentCount, maxTicks, doctrine, config, InterruptPlan.None);

        public static SimState Run(ulong seed, int agentCount, int maxTicks, Doctrine doctrine,
                                   ScenarioConfig config, InterruptPlan plan)
        {
            var sim = new Simulation(agentCount, seed, config);
            var state = CreateInitialState(agentCount, seed, doctrine.ReserveFraction);
            for (int t = 0; t < maxTicks; t++)
            {
                plan.ApplyDue(state);
                sim.Tick(state, doctrine, seed);
                // A held reserve keeps the run alive: all actives resolved with
                // an uncommitted reserve is still a decision state, not an end.
                if (state.CountStatus(AgentStatus.Active) == 0
                    && state.CountStatus(AgentStatus.Reserve) == 0) break;
            }
            return state;
        }
    }
}
