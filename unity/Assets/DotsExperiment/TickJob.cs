using System;
using Hellfire.Sim;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;

namespace Hellfire.Dots
{
    /// <summary>
    /// Blittable copy of <see cref="Doctrine"/>'s float axes — Doctrine itself is a
    /// managed class and cannot live inside a Burst job.
    /// </summary>
    public struct DoctrineData
    {
        public float Autonomy;
        public float RiskTolerance;
        public float SensorRange;
        public float CommsDiscipline;
        public float AbortLossFraction;
        public float Cohesion;
        public float MaxSpeed;
        public float NeighborRadius;
        public float CrowdDampPerNeighbor;
        public float JitterAccel;

        public static DoctrineData From(Doctrine d) => new DoctrineData
        {
            Autonomy = d.Autonomy,
            RiskTolerance = d.RiskTolerance,
            SensorRange = d.SensorRange,
            CommsDiscipline = d.CommsDiscipline,
            AbortLossFraction = d.AbortLossFraction,
            Cohesion = d.Cohesion,
            MaxSpeed = d.MaxSpeed,
            NeighborRadius = d.NeighborRadius,
            CrowdDampPerNeighbor = d.CrowdDampPerNeighbor,
            JitterAccel = d.JitterAccel,
        };
    }

    /// <summary>
    /// Burst port of <see cref="Simulation.Tick"/>'s per-agent body.
    ///
    /// JACOBI vs GAUSS-SEIDEL (deliberate divergence from sim/Sim.cs):
    /// Sim.Tick updates <c>SimState</c> in place — agent i's neighbour queries at
    /// index j &lt; i see j's value ALREADY updated this tick (sequential /
    /// Gauss-Seidel). This job reads ONLY the previous-tick snapshot (Prev*
    /// arrays, [ReadOnly]) and writes ONLY next-tick values at its own index i
    /// (Next*, effectively write-only) — every agent's neighbour queries this
    /// tick see the same frozen prior-tick world (Jacobi). This is what makes
    /// the parallel schedule race-free (no agent ever reads another agent's
    /// in-flight write) but it is NOT the same numerical recurrence as the
    /// sequential sim — see H2 (DotsBenchmark.RunH2 / JacobiDiffersFromGaussSeidel
    /// test): divergence from the managed path is expected, not a bug.
    ///
    /// Spatial hash is READ-ONLY here — built once per tick on the main thread
    /// (DotsSim.BuildSpatialHash), identical counting-sort algorithm to
    /// sim/SpatialHash.cs, over the same previous-tick positions this job reads.
    /// </summary>
    // CompileSynchronously: without it Burst compiles in the background and the
    // first executions run on the managed fallback with DIFFERENT float results
    // — measured as three distinct state hashes across the process's first two
    // episodes, indistinguishable from a replay bug after the fact. For a
    // determinism-measured job, synchronous compilation is a correctness
    // requirement, not a performance preference.
    [BurstCompile(CompileSynchronously = true)]
    public unsafe struct TickJob : IJobFor
    {
        // --- World / rule constants, copied from sim/Sim.cs and sim/Scenario.cs. ---
        private const float WorldWidth = 512f;
        private const float WorldHeight = 512f;
        private const float FixedDt = 1f / 60f;
        private const float CompletedDamp = 0.85f;
        private const float RouteShapeFactor = 0.25f;
        private const int MaxBoidNeighbors = 32;
        private const float SeparationDelta = 1.2f;
        private const float AlignRate = 2.4f;
        private const float CohesionDelta = 1.4f;
        private const float AlignFloor = 0.3f;
        private const float JamPanicJitter = 3.5f;
        private const float JamDependencyPenalty = 0.8f;
        private const float ObjectiveX = 256f;
        private const float ObjectiveY = 480f;
        private const float ObjectiveRadius = 40f;
        private const float SpawnBandHeight = 40f;
        private const float ThreatKillRadius = 26f;
        private const float ThreatBaseKillProb = 0.006f;
        private const float JammerRadius = 85f;

        // DetHash tag values — mirror sim/Sim.cs's private `Tag` enum (inaccessible
        // across assemblies, so the integer values are copied here verbatim).
        private const ulong TagJitterX = 5;
        private const ulong TagJitterY = 6;
        private const ulong TagKill = 7;

        // --- Doctrine + per-tick derived scalars (computed once outside the
        // per-agent loop in Sim.Tick; precomputed the same way in DotsSim.Tick
        // before scheduling, so they are copied here as job fields, not
        // recomputed per agent). ---
        public DoctrineData Doctrine;
        public float EngageR2;
        public float BaseKillR2;
        public float AvoidRadius;
        public float AvoidStrength;
        public float RouteRadius;
        public float RouteR2;
        public float NetworkShare;
        public float BaseJitter;
        public float SepRadius;
        public float SteerBlend;
        public ulong Seed;
        public ulong Tick;
        public bool AbortedPrev;
        // Step-6 recall semantics (FallBack interrupt). DotsSim never issues
        // interrupts, so this stays false there — mirrored for parity with
        // sim/Sim.cs's target-selection condition.
        public bool RecalledPrev;

        // --- Scenario (copied once from a Hellfire.Sim.Scenario built from the
        // same seed / ScenarioConfig.Default equivalent). ---
        [ReadOnly] public NativeArray<float> ThreatX;
        [ReadOnly] public NativeArray<float> ThreatY;
        [ReadOnly] public NativeArray<float> JammerX;
        [ReadOnly] public NativeArray<float> JammerY;

        // --- Spatial hash, built main-thread each tick over Prev positions. ---
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> Entries;
        public int GridCols;
        public int GridRows;
        public float CellSize;

        // --- Jacobi double buffer. ---
        [ReadOnly] public NativeArray<float> PrevPosX;
        [ReadOnly] public NativeArray<float> PrevPosY;
        [ReadOnly] public NativeArray<float> PrevVelX;
        [ReadOnly] public NativeArray<float> PrevVelY;
        [ReadOnly] public NativeArray<byte> PrevStatus;
        [ReadOnly] public NativeArray<byte> PrevDeathFlags;
        [ReadOnly] public NativeArray<int> PrevDeathTick;

        [WriteOnly] public NativeArray<float> NextPosX;
        [WriteOnly] public NativeArray<float> NextPosY;
        [WriteOnly] public NativeArray<float> NextVelX;
        [WriteOnly] public NativeArray<float> NextVelY;
        [WriteOnly] public NativeArray<byte> NextStatus;
        [WriteOnly] public NativeArray<byte> NextDeathFlags;
        [WriteOnly] public NativeArray<int> NextDeathTick;

        public void Execute(int i)
        {
            byte status = PrevStatus[i];

            // --- Dead / Safe: original `continue`s and leaves state untouched;
            // Jacobi must carry the value forward explicitly since Next* starts
            // uninitialized each tick. ---
            if (status == (byte)AgentStatus.Dead || status == (byte)AgentStatus.Safe
                || status == (byte)AgentStatus.Reserve)
            {
                NextPosX[i] = PrevPosX[i];
                NextPosY[i] = PrevPosY[i];
                NextVelX[i] = PrevVelX[i];
                NextVelY[i] = PrevVelY[i];
                NextStatus[i] = status;
                NextDeathFlags[i] = PrevDeathFlags[i];
                NextDeathTick[i] = PrevDeathTick[i];
                return;
            }

            float px = PrevPosX[i];
            float py = PrevPosY[i];
            float vx = PrevVelX[i];
            float vy = PrevVelY[i];

            if (status == (byte)AgentStatus.Completed)
            {
                vx *= CompletedDamp;
                vy *= CompletedDamp;
                px += vx * FixedDt;
                py += vy * FixedDt;
                NextPosX[i] = px;
                NextPosY[i] = py;
                NextVelX[i] = vx;
                NextVelY[i] = vy;
                NextStatus[i] = status;
                NextDeathFlags[i] = PrevDeathFlags[i];
                NextDeathTick[i] = PrevDeathTick[i];
                return;
            }

            // status == Active from here on.
            ulong id = (ulong)i;

            // --- EW state for this agent, this tick. ---
            bool jammed = IsJammed(px, py);
            float effNet = jammed ? 0f : NetworkShare;
            float effSensor = jammed
                ? Doctrine.SensorRange * (1f - JamDependencyPenalty * NetworkShare)
                : Doctrine.SensorRange;
            float knowledgeRadius = effSensor + effNet * (720f - effSensor);
            float knowledgeR2 = knowledgeRadius * knowledgeRadius;

            // --- Target: objective, or home once the swarm has aborted. ---
            float tx, ty;
            if (AbortedPrev || RecalledPrev) { tx = px; ty = 0f; }
            else { tx = ObjectiveX; ty = ObjectiveY; }

            float dxT = tx - px;
            float dyT = ty - py;
            float distT = (float)Math.Sqrt(dxT * dxT + dyT * dyT);
            if (distT > 1e-3f)
            {
                vx += (dxT / distT * Doctrine.MaxSpeed - vx) * SteerBlend;
                vy += (dyT / distT * Doctrine.MaxSpeed - vy) * SteerBlend;
            }

            // --- Threats: avoidance of *known* ones; exposure census of all. ---
            int threatsInKillRange = 0;
            float nearestEngageD2 = float.MaxValue;
            bool nearestKnown = false;
            for (int t = 0; t < ThreatX.Length; t++)
            {
                float dx = px - ThreatX[t];
                float dy = py - ThreatY[t];
                float d2 = dx * dx + dy * dy;
                bool known = d2 <= knowledgeR2;

                if (d2 <= EngageR2)
                {
                    threatsInKillRange++;
                    if (d2 < nearestEngageD2) { nearestEngageD2 = d2; nearestKnown = known; }
                }

                if (known && d2 <= RouteR2 && d2 > 1e-6f)
                {
                    float d = (float)Math.Sqrt(d2);
                    if (d < AvoidRadius)
                    {
                        float w = AvoidStrength * (1f - d / AvoidRadius);
                        vx += dx / d * w;
                        vy += dy / d * w;
                    }
                    else
                    {
                        float w = RouteShapeFactor * AvoidStrength * (1f - d / RouteRadius);
                        vx += dx / d * w;
                        vy += dy / d * w;
                    }
                }
            }

            // --- Boids over canonical-order neighbours (Active peers only). ---
            // Two passes over the query, since the Jacobi/parallel job cannot use
            // sim/SpatialHash.cs's "collect into scratch array, then Array.Sort"
            // approach without an unbounded per-agent buffer:
            //   Pass A (order-independent): scan candidate cells once, counting
            //     liveNeighbors (all non-Dead hits — a plain sum, order never
            //     matters) and selecting the 32 SMALLEST-INDEX Active hits via
            //     insertion into a small sorted buffer. "First 32 in ascending
            //     index order" and "32 smallest indices" are the same set, so this
            //     reproduces sim/SpatialHash.cs's sorted-query + 32-cap exactly
            //     without ever materializing the full (potentially huge) hit list.
            //   Pass B (order-dependent): walk the already-ascending winners and
            //     accumulate flock sums / apply separation in that order — bit-
            //     identical accumulation order to Sim.Tick's ascending scan, since
            //     entries it skips (Dead, non-Active, over-cap) never touch the
            //     running sums there either.
            int liveNeighbors = 0;
            int flockCount = 0;
            int* flockIdx = stackalloc int[MaxBoidNeighbors];

            float qRadius = Doctrine.NeighborRadius;
            float qR2 = qRadius * qRadius;
            int cx0 = (int)((px - qRadius) / CellSize);
            int cy0 = (int)((py - qRadius) / CellSize);
            int cx1 = (int)((px + qRadius) / CellSize);
            int cy1 = (int)((py + qRadius) / CellSize);
            if (cx0 < 0) cx0 = 0;
            if (cy0 < 0) cy0 = 0;
            if (cx1 >= GridCols) cx1 = GridCols - 1;
            if (cy1 >= GridRows) cy1 = GridRows - 1;

            for (int cy = cy0; cy <= cy1; cy++)
            {
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int c = cy * GridCols + cx;
                    int end = CellStart[c + 1];
                    for (int e = CellStart[c]; e < end; e++)
                    {
                        int j = Entries[e];
                        if (j == i) continue;
                        float dx = PrevPosX[j] - px;
                        float dy = PrevPosY[j] - py;
                        if (dx * dx + dy * dy > qR2) continue;

                        byte jStatus = PrevStatus[j];
                        if (jStatus != (byte)AgentStatus.Dead) liveNeighbors++;
                        if (jStatus != (byte)AgentStatus.Active) continue;

                        if (flockCount < MaxBoidNeighbors)
                        {
                            int pos = flockCount;
                            while (pos > 0 && flockIdx[pos - 1] > j) { flockIdx[pos] = flockIdx[pos - 1]; pos--; }
                            flockIdx[pos] = j;
                            flockCount++;
                        }
                        else if (j < flockIdx[MaxBoidNeighbors - 1])
                        {
                            int pos = MaxBoidNeighbors - 1;
                            while (pos > 0 && flockIdx[pos - 1] > j) { flockIdx[pos] = flockIdx[pos - 1]; pos--; }
                            flockIdx[pos] = j;
                        }
                    }
                }
            }

            int flock = 0;
            float cx_ = 0f, cy_ = 0f, avx = 0f, avy = 0f;
            for (int k = 0; k < flockCount; k++)
            {
                int j = flockIdx[k];
                flock++;
                cx_ += PrevPosX[j];
                cy_ += PrevPosY[j];
                avx += PrevVelX[j];
                avy += PrevVelY[j];

                float sdx = px - PrevPosX[j];
                float sdy = py - PrevPosY[j];
                float sd2 = sdx * sdx + sdy * sdy;
                if (sd2 < SepRadius * SepRadius && sd2 > 1e-6f)
                {
                    float sd = (float)Math.Sqrt(sd2);
                    float w = SeparationDelta * (1f - sd / SepRadius);
                    vx += sdx / sd * w;
                    vy += sdy / sd * w;
                }
            }
            if (flock > 0)
            {
                float inv = 1f / flock;
                float alignBlend = AlignRate * FixedDt * (AlignFloor + (1f - AlignFloor) * effNet);
                vx += (avx * inv - vx) * alignBlend;
                vy += (avy * inv - vy) * alignBlend;

                float gx = cx_ * inv - px;
                float gy = cy_ * inv - py;
                float gd = (float)Math.Sqrt(gx * gx + gy * gy);
                if (gd > 1e-3f)
                {
                    float w = CohesionDelta * Doctrine.Cohesion * Math.Min(1f, gd / Doctrine.NeighborRadius);
                    vx += gx / gd * w;
                    vy += gy / gd * w;
                }
            }

            // --- Hashed-RNG wander; jam panic scatters networked swarms. ---
            float jitterScale = jammed ? BaseJitter + JamPanicJitter * NetworkShare : BaseJitter;
            vx += DetHash.FloatSigned(Seed, Tick, id, TagJitterX) * jitterScale;
            vy += DetHash.FloatSigned(Seed, Tick, id, TagJitterY) * jitterScale;

            // --- Crowd damping (step-1 path, kept). ---
            float damp = 1f - Math.Min(0.9f, Doctrine.CrowdDampPerNeighbor * liveNeighbors);
            vx *= damp;
            vy *= damp;

            float speed2 = vx * vx + vy * vy;
            float max2 = Doctrine.MaxSpeed * Doctrine.MaxSpeed;
            if (speed2 > max2)
            {
                float scale = Doctrine.MaxSpeed / (float)Math.Sqrt(speed2);
                vx *= scale;
                vy *= scale;
            }

            px += vx * FixedDt;
            py += vy * FixedDt;

            if (px < 0f) { px = -px; vx = -vx; }
            else if (px > WorldWidth) { px = 2f * WorldWidth - px; vx = -vx; }
            if (py < 0f) { py = -py; vy = -vy; }
            else if (py > WorldHeight) { py = 2f * WorldHeight - py; vy = -vy; }

            NextPosX[i] = px;
            NextPosY[i] = py;
            NextVelX[i] = vx;
            NextVelY[i] = vy;

            // --- Attrition roll + §2 attribution. ---
            if (threatsInKillRange > 0)
            {
                float killProb = ThreatBaseKillProb * threatsInKillRange;
                if (DetHash.Float01(Seed, Tick, id, TagKill) < killProb)
                {
                    var flags = DeathFlag.None;
                    if (!nearestKnown) flags |= DeathFlag.UnknownThreat;
                    else flags |= DeathFlag.PressedKnown;
                    if (jammed) flags |= DeathFlag.Jammed;
                    if (nearestEngageD2 > BaseKillR2) flags |= DeathFlag.Detected;
                    NextStatus[i] = (byte)AgentStatus.Dead;
                    NextDeathFlags[i] = (byte)flags;
                    NextDeathTick[i] = (int)Tick + 1;
                    return;
                }
            }

            // --- Latched outcomes. ---
            byte nextStatus = (byte)AgentStatus.Active;
            if (!AbortedPrev)
            {
                float dxO = px - ObjectiveX;
                float dyO = py - ObjectiveY;
                if (dxO * dxO + dyO * dyO <= ObjectiveRadius * ObjectiveRadius)
                {
                    nextStatus = (byte)AgentStatus.Completed;
                }
            }
            else if (py <= SpawnBandHeight)
            {
                nextStatus = (byte)AgentStatus.Safe;
            }

            NextStatus[i] = nextStatus;
            NextDeathFlags[i] = PrevDeathFlags[i];
            NextDeathTick[i] = PrevDeathTick[i];
        }

        private bool IsJammed(float x, float y)
        {
            const float r2 = JammerRadius * JammerRadius;
            for (int j = 0; j < JammerX.Length; j++)
            {
                float dx = x - JammerX[j];
                float dy = y - JammerY[j];
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }
    }
}
