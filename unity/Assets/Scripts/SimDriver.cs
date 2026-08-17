using Hellfire.Sim;
using UnityEngine;

namespace Hellfire.Presentation
{
    /// <summary>
    /// Owns the deterministic sim and advances it on a fixed-step accumulator.
    /// The presentation boundary (GDD §3): everything below this component READS
    /// sim state; nothing ever writes it. The sim tick consumes no Unity state —
    /// Time.deltaTime only decides HOW MANY ticks run, never what they compute,
    /// so the same (seed, doctrine) always replays byte-identically regardless
    /// of frame rate.
    /// </summary>
    public sealed class SimDriver : MonoBehaviour
    {
        public DoctrineAsset doctrine;
        public int agentCount = 96;
        public ulong seed = 42;
        [Tooltip("Sim ticks to run before halting (0 = unbounded).")]
        public int maxTicks = 1800;
        [Range(0f, 8f)] public float timeScale = 1f;

        public Simulation Sim { get; private set; }
        public SimState State { get; private set; }
        public bool Finished { get; private set; }

        private Doctrine _doctrine;
        private float _accumulator;

        private void Awake()
        {
            ResetSim();
        }

        public void ResetSim()
        {
            _doctrine = doctrine != null ? doctrine.ToDoctrine() : Doctrine.Default;
            Sim = new Simulation(agentCount, seed);
            State = Simulation.CreateInitialState(agentCount, seed);
            Finished = false;
            _accumulator = 0f;
        }

        private void Update()
        {
            if (Finished) return;
            _accumulator += Time.deltaTime * timeScale;
            // Cap catch-up work per frame so a hitch cannot spiral.
            int steps = Mathf.Min((int)(_accumulator / Simulation.FixedDt), 8);
            _accumulator -= steps * Simulation.FixedDt;
            StepTicks(steps);
        }

        /// <summary>Advance exactly <paramref name="ticks"/> fixed steps. Public so
        /// the render-never-writes parity test can drive the sim without play mode.</summary>
        public void StepTicks(int ticks)
        {
            if (State == null) return;
            for (int t = 0; t < ticks; t++)
            {
                if (maxTicks > 0 && State.Tick >= maxTicks) { Finished = true; return; }
                Sim.Tick(State, _doctrine, seed);
                if (State.CountStatus(AgentStatus.Active) == 0) { Finished = true; return; }
            }
        }
    }
}
