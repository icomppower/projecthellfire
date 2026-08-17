using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// The determinism spine: fixed-timestep, seeded, single-threaded, pure
    /// transition tick(state, doctrine, seed) -> state. Zero UnityEngine types.
    /// Behaviour is the minimum needed to exercise every determinism-relevant
    /// path (hashed RNG, float accumulation, spatial-hash queries) — no boids,
    /// no doctrine semantics, no renderer.
    /// </summary>
    public sealed class Simulation
    {
        public const float WorldWidth = 512f;
        public const float WorldHeight = 512f;
        public const float FixedDt = 1f / 60f;
        private const float CellSize = 12f;

        private readonly SpatialHash _hash;
        private readonly int[] _queryScratch;

        public Simulation(int maxAgents)
        {
            _hash = new SpatialHash(WorldWidth, WorldHeight, CellSize, maxAgents);
            _queryScratch = new int[maxAgents];
        }

        public static SimState CreateInitialState(int agentCount, ulong seed)
        {
            var s = new SimState(agentCount);
            for (int i = 0; i < agentCount; i++)
            {
                ulong id = (ulong)i;
                s.PosX[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosX) * WorldWidth;
                s.PosY[i] = DetHash.Float01(seed, 0, id, (ulong)Tag.InitPosY) * WorldHeight;
                s.VelX[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelX) * 10f;
                s.VelY[i] = DetHash.FloatSigned(seed, 0, id, (ulong)Tag.InitVelY) * 10f;
            }
            return s;
        }

        private enum Tag : ulong
        {
            InitPosX = 1, InitPosY = 2, InitVelX = 3, InitVelY = 4,
            JitterX = 5, JitterY = 6,
        }

        /// <summary>
        /// Advances <paramref name="state"/> by one fixed step, in place.
        /// Same (state, doctrine, seed) always produces the same next state.
        /// </summary>
        public void Tick(SimState state, in Doctrine doctrine, ulong seed)
        {
            int n = state.AgentCount;
            ulong tick = (ulong)state.Tick;
            _hash.Build(state.PosX, state.PosY, n);

            for (int i = 0; i < n; i++)
            {
                ulong id = (ulong)i;

                // Hashed-RNG wander: independent of every other draw this tick.
                float ax = DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterX) * doctrine.JitterAccel;
                float ay = DetHash.FloatSigned(seed, tick, id, (ulong)Tag.JitterY) * doctrine.JitterAccel;

                float vx = state.VelX[i] + ax;
                float vy = state.VelY[i] + ay;

                // Neighbor count (integer — order-independent) drives crowd damping,
                // putting the spatial hash inside the determinism-critical path.
                int neighbors = _hash.QueryRadius(
                    state.PosX[i], state.PosY[i], doctrine.NeighborRadius, i,
                    state.PosX, state.PosY, _queryScratch);
                float damp = 1f - Math.Min(0.9f, doctrine.CrowdDampPerNeighbor * neighbors);
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

                float px = state.PosX[i] + vx * FixedDt;
                float py = state.PosY[i] + vy * FixedDt;

                // Bounce off world edges (no wrap — keeps neighbor metric plain Euclidean).
                if (px < 0f) { px = -px; vx = -vx; }
                else if (px > WorldWidth) { px = 2f * WorldWidth - px; vx = -vx; }
                if (py < 0f) { py = -py; vy = -vy; }
                else if (py > WorldHeight) { py = 2f * WorldHeight - py; vy = -vy; }

                state.PosX[i] = px;
                state.PosY[i] = py;
                state.VelX[i] = vx;
                state.VelY[i] = vy;
            }
            state.Tick++;
        }

        /// <summary>Runs a full seeded episode and returns the final state.</summary>
        public static SimState Run(ulong seed, int agentCount, int ticks, in Doctrine doctrine)
        {
            var sim = new Simulation(agentCount);
            var state = CreateInitialState(agentCount, seed);
            for (int t = 0; t < ticks; t++) sim.Tick(state, in doctrine, seed);
            return state;
        }
    }
}
