using System;
using Hellfire.Sim;
using Unity.Collections;
using Unity.Jobs;

namespace Hellfire.Dots
{
    /// <summary>
    /// Owner of the Burst tick's buffers and per-tick orchestration: builds the
    /// spatial hash on the main thread (deterministic counting sort, identical
    /// algorithm to sim/SpatialHash.cs), precomputes the same per-tick scalars
    /// Sim.Tick derives, runs TickJob sequentially (Run) or parallel
    /// (ScheduleParallel), then applies the abort latch from an integer count —
    /// the only cross-agent reduction, and order-independent by construction.
    /// </summary>
    public sealed class DotsSim : IDisposable
    {
        // Mirrors Simulation's private constants (inaccessible across assemblies).
        private const float CellSize = 12f;
        private const float SteerRate = 4.0f;
        private const float AvoidDeltaMax = 3.0f;
        private const float RouteShapeRange = 2.5f;
        private const float NetworkFloor = 0.05f;

        public int Tick { get; private set; }
        public bool Aborted { get; private set; }
        public readonly int AgentCount;

        private readonly ulong _seed;
        private readonly Doctrine _doctrine;
        private readonly int _cols;
        private readonly int _rows;

        private NativeArray<float> _threatX, _threatY, _jammerX, _jammerY;
        private NativeArray<int> _cellStart, _cellCounts, _entries;
        // Double buffer: index 0/1 flip each tick; _cur is the READ side.
        private readonly NativeArray<float>[] _posX = new NativeArray<float>[2];
        private readonly NativeArray<float>[] _posY = new NativeArray<float>[2];
        private readonly NativeArray<float>[] _velX = new NativeArray<float>[2];
        private readonly NativeArray<float>[] _velY = new NativeArray<float>[2];
        private readonly NativeArray<byte>[] _status = new NativeArray<byte>[2];
        private readonly NativeArray<byte>[] _deathFlags = new NativeArray<byte>[2];
        private readonly NativeArray<int>[] _deathTick = new NativeArray<int>[2];
        private int _cur;

        public DotsSim(int agentCount, ulong seed, Doctrine doctrine)
        {
            AgentCount = agentCount;
            _seed = seed;
            _doctrine = doctrine;
            _cols = Math.Max(1, (int)(Simulation.WorldWidth / CellSize));
            _rows = Math.Max(1, (int)(Simulation.WorldHeight / CellSize));

            var scenario = new Scenario(seed);
            _threatX = new NativeArray<float>(scenario.ThreatX, Allocator.Persistent);
            _threatY = new NativeArray<float>(scenario.ThreatY, Allocator.Persistent);
            _jammerX = new NativeArray<float>(scenario.JammerX, Allocator.Persistent);
            _jammerY = new NativeArray<float>(scenario.JammerY, Allocator.Persistent);
            _cellStart = new NativeArray<int>(_cols * _rows + 1, Allocator.Persistent);
            _cellCounts = new NativeArray<int>(_cols * _rows, Allocator.Persistent);
            _entries = new NativeArray<int>(agentCount, Allocator.Persistent);

            var init = Simulation.CreateInitialState(agentCount, seed);
            for (int b = 0; b < 2; b++)
            {
                _posX[b] = new NativeArray<float>(agentCount, Allocator.Persistent);
                _posY[b] = new NativeArray<float>(agentCount, Allocator.Persistent);
                _velX[b] = new NativeArray<float>(agentCount, Allocator.Persistent);
                _velY[b] = new NativeArray<float>(agentCount, Allocator.Persistent);
                _status[b] = new NativeArray<byte>(agentCount, Allocator.Persistent);
                _deathFlags[b] = new NativeArray<byte>(agentCount, Allocator.Persistent);
                _deathTick[b] = new NativeArray<int>(agentCount, Allocator.Persistent);
            }
            _posX[0].CopyFrom(init.PosX);
            _posY[0].CopyFrom(init.PosY);
            _velX[0].CopyFrom(init.VelX);
            _velY[0].CopyFrom(init.VelY);
            _status[0].CopyFrom(init.Status);
            _cur = 0;
        }

        public void Step(bool parallel)
        {
            BuildSpatialHash();
            int next = 1 - _cur;

            // Per-tick scalars — the same derivation block as Sim.Tick, verbatim.
            float engageRadius = Scenario.EngageRadius(_doctrine.CommsDiscipline);
            float avoidRadius = engageRadius * (1f + 1.5f * (1f - _doctrine.RiskTolerance));
            float routeRadius = avoidRadius * RouteShapeRange;
            float networkShare = (1f - _doctrine.Autonomy)
                * (NetworkFloor + (1f - NetworkFloor) * (1f - _doctrine.CommsDiscipline));

            var job = new TickJob
            {
                Doctrine = DoctrineData.From(_doctrine),
                EngageR2 = engageRadius * engageRadius,
                BaseKillR2 = Scenario.ThreatKillRadius * Scenario.ThreatKillRadius,
                AvoidRadius = avoidRadius,
                AvoidStrength = AvoidDeltaMax * (1f - 0.7f * _doctrine.RiskTolerance),
                RouteRadius = routeRadius,
                RouteR2 = routeRadius * routeRadius,
                NetworkShare = networkShare,
                BaseJitter = _doctrine.JitterAccel * (0.4f + 0.6f * _doctrine.CommsDiscipline),
                SepRadius = _doctrine.NeighborRadius * 0.5f,
                SteerBlend = Math.Min(1f, SteerRate * Simulation.FixedDt),
                Seed = _seed,
                Tick = (ulong)Tick,
                AbortedPrev = Aborted,
                RecalledPrev = false, // DotsSim issues no interrupts; RecallUntilTick stays 0

                ThreatX = _threatX,
                ThreatY = _threatY,
                JammerX = _jammerX,
                JammerY = _jammerY,
                CellStart = _cellStart,
                Entries = _entries,
                GridCols = _cols,
                GridRows = _rows,
                CellSize = CellSize,
                PrevPosX = _posX[_cur], PrevPosY = _posY[_cur],
                PrevVelX = _velX[_cur], PrevVelY = _velY[_cur],
                PrevStatus = _status[_cur],
                PrevDeathFlags = _deathFlags[_cur],
                PrevDeathTick = _deathTick[_cur],
                NextPosX = _posX[next], NextPosY = _posY[next],
                NextVelX = _velX[next], NextVelY = _velY[next],
                NextStatus = _status[next],
                NextDeathFlags = _deathFlags[next],
                NextDeathTick = _deathTick[next],
            };

            if (parallel) job.ScheduleParallel(AgentCount, 64, default).Complete();
            else job.Run(AgentCount);

            _cur = next;
            // Abort latch — integer reduction on the main thread, like Sim.Tick's
            // post-loop check.
            if (!Aborted)
            {
                int dead = CountStatus(AgentStatus.Dead);
                if (dead > _doctrine.AbortLossFraction * AgentCount) Aborted = true;
            }
            Tick++;
        }

        /// <summary>Counting-sort grid over the READ-side positions — the same
        /// clamp/count/prefix/stable-place sequence as sim/SpatialHash.Build.</summary>
        private void BuildSpatialHash()
        {
            var posX = _posX[_cur];
            var posY = _posY[_cur];
            for (int c = 0; c < _cellCounts.Length; c++) _cellCounts[c] = 0;
            for (int i = 0; i < AgentCount; i++) _cellCounts[CellIndex(posX[i], posY[i])]++;
            int running = 0;
            for (int c = 0; c < _cellCounts.Length; c++)
            {
                _cellStart[c] = running;
                running += _cellCounts[c];
            }
            _cellStart[_cellCounts.Length] = running;
            for (int c = 0; c < _cellCounts.Length; c++) _cellCounts[c] = 0;
            for (int i = 0; i < AgentCount; i++)
            {
                int c = CellIndex(posX[i], posY[i]);
                _entries[_cellStart[c] + _cellCounts[c]] = i;
                _cellCounts[c] = _cellCounts[c] + 1;
            }
        }

        private int CellIndex(float x, float y)
        {
            int cx = (int)(x / CellSize);
            int cy = (int)(y / CellSize);
            if (cx < 0) cx = 0; else if (cx >= _cols) cx = _cols - 1;
            if (cy < 0) cy = 0; else if (cy >= _rows) cy = _rows - 1;
            return cy * _cols + cx;
        }

        public int CountStatus(AgentStatus s)
        {
            var status = _status[_cur];
            byte b = (byte)s;
            int n = 0;
            for (int i = 0; i < AgentCount; i++) { if (status[i] == b) n++; }
            return n;
        }

        /// <summary>FNV-1a 64 in the exact field order of SimState.StateHash.</summary>
        public ulong StateHash()
        {
            const ulong prime = 1099511628211UL;
            ulong h = 14695981039346656037UL;
            h = FnvInt(h, Tick, prime);
            h = FnvInt(h, AgentCount, prime);
            h = FnvInt(h, Aborted ? 1 : 0, prime);
            h = FnvInt(h, 0, prime); // RecallUntilTick — always 0 here (no interrupts)
            h = FnvFloats(h, _posX[_cur], prime);
            h = FnvFloats(h, _posY[_cur], prime);
            h = FnvFloats(h, _velX[_cur], prime);
            h = FnvFloats(h, _velY[_cur], prime);
            var status = _status[_cur];
            var flags = _deathFlags[_cur];
            unchecked
            {
                for (int i = 0; i < AgentCount; i++) { h = (h ^ status[i]) * prime; }
                for (int i = 0; i < AgentCount; i++) { h = (h ^ flags[i]) * prime; }
            }
            var deathTick = _deathTick[_cur];
            for (int i = 0; i < AgentCount; i++) { h = FnvInt(h, deathTick[i], prime); }
            return h;
        }

        private static ulong FnvInt(ulong h, int value, ulong prime)
        {
            unchecked
            {
                uint v = (uint)value;
                for (int b = 0; b < 4; b++) { h = (h ^ ((v >> (b * 8)) & 0xFF)) * prime; }
                return h;
            }
        }

        private static ulong FnvFloats(ulong h, NativeArray<float> arr, ulong prime)
        {
            unchecked
            {
                for (int i = 0; i < arr.Length; i++)
                {
                    uint v = (uint)BitConverter.SingleToInt32Bits(arr[i]);
                    for (int b = 0; b < 4; b++) { h = (h ^ ((v >> (b * 8)) & 0xFF)) * prime; }
                }
                return h;
            }
        }

        /// <summary>Full episode with Simulation.Run's early-exit rule.</summary>
        public static DotsSim RunEpisode(ulong seed, int agentCount, int maxTicks, Doctrine doctrine, bool parallel)
        {
            var sim = new DotsSim(agentCount, seed, doctrine);
            for (int t = 0; t < maxTicks; t++)
            {
                sim.Step(parallel);
                if (sim.CountStatus(AgentStatus.Active) == 0) break;
            }
            return sim;
        }

        public void Dispose()
        {
            _threatX.Dispose(); _threatY.Dispose(); _jammerX.Dispose(); _jammerY.Dispose();
            _cellStart.Dispose(); _cellCounts.Dispose(); _entries.Dispose();
            for (int b = 0; b < 2; b++)
            {
                _posX[b].Dispose(); _posY[b].Dispose();
                _velX[b].Dispose(); _velY[b].Dispose();
                _status[b].Dispose(); _deathFlags[b].Dispose(); _deathTick[b].Dispose();
            }
        }
    }
}
