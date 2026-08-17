namespace Hellfire.Sim
{
    /// <summary>
    /// The doctrine schema (GDD §1) — the complete set of pre-run bets the player
    /// authors. Plain C# + JSON-serializable properties; the ScriptableObject
    /// wrapper arrives with Unity at step 4. Treated as immutable during a run.
    ///
    /// Step-2 wiring note: every axis is plumbed into the sim, but the autonomy
    /// dial's *counterweight* (jamming that punishes centralization) is step-3
    /// emergence work. Until then centralized is strictly safer — known, accepted.
    /// </summary>
    public sealed class Doctrine
    {
        // --- The autonomy dial (§1): 0 = centralized, 1 = decentralized. ---
        // Centralized: agents share all threat knowledge (avoid preemptively).
        // Decentralized: agents only react to threats inside their own SensorRange.
        public float Autonomy { get; set; } = 0.5f;

        // --- Engagement rules ---
        // 0 = evade hard (strong avoidance steering, wide berth), 1 = press on.
        public float RiskTolerance { get; set; } = 0.5f;
        // Own-sensor threat detection radius (world units).
        public float SensorRange { get; set; } = 60f;

        // --- Comms posture ---
        // 0 = chatty: coordinated (less wander → faster transit) but detectable
        //     (threats engage at extended radius).
        // 1 = silent: full wander scatter, minimal detectability.
        public float CommsDiscipline { get; set; } = 0.5f;

        // --- Loss threshold ---
        // Swarm-level latch: when the loss fraction exceeds this, the mission
        // aborts and every non-completed agent turns for home.
        public float AbortLossFraction { get; set; } = 0.5f;

        // --- Force structure ---
        // Fraction held back at spawn, launched only by the CommitReserve
        // interrupt. Insurance priced in completion ceiling: an uncommitted
        // reserve survives trivially and delivers nothing. Default 0 keeps the
        // pre-step-6 scored baseline intact.
        public float ReserveFraction { get; set; } = 0f;

        // --- Formation ---
        // 0 = loose swarm (agents transit independently), 1 = tight flock
        // (cohesion pulls the group together — including toward danger the
        // group is already in: herd risk is the intended emergent cost).
        // MEASURED INERT at the 500-seed gate (2026-08-16): the scenario has no
        // mutual-support benefit to weigh against herd risk, so this axis does
        // not move outcomes above seed noise. Wired but not player-offered
        // until a supporting mechanism exists.
        public float Cohesion { get; set; } = 0.5f;

        // --- Movement envelope (carried over from step 1) ---
        public float MaxSpeed { get; set; } = 30f;
        public float NeighborRadius { get; set; } = 12f;
        public float CrowdDampPerNeighbor { get; set; } = 0.02f;
        public float JitterAccel { get; set; } = 2.0f;

        public static Doctrine Default => new Doctrine();

        /// <summary>
        /// Named presets spanning the doctrine space — the discrimination grid the
        /// step-2 gate is measured against.
        /// </summary>
        public static Doctrine Preset(string name)
        {
            switch (name)
            {
                case "default": return new Doctrine();
                case "aggressive": return new Doctrine { RiskTolerance = 0.95f, AbortLossFraction = 0.95f };
                case "cautious": return new Doctrine { RiskTolerance = 0.05f, AbortLossFraction = 0.15f };
                case "chatty": return new Doctrine { CommsDiscipline = 0.0f };
                case "silent": return new Doctrine { CommsDiscipline = 1.0f };
                case "centralized": return new Doctrine { Autonomy = 0.0f };
                case "decentralized": return new Doctrine { Autonomy = 1.0f };
                case "tight-flock": return new Doctrine { Cohesion = 1.0f };
                case "loose-swarm": return new Doctrine { Cohesion = 0.0f };
                // The predicted self-defeating combination: full network reliance
                // with the comms posture that starves the network.
                case "silent-centralized": return new Doctrine { Autonomy = 0.0f, CommsDiscipline = 1.0f };
                default: throw new System.ArgumentException($"unknown preset '{name}'", nameof(name));
            }
        }

        public static readonly string[] PresetNames =
            { "default", "aggressive", "cautious", "chatty", "silent", "centralized", "decentralized",
              "tight-flock", "loose-swarm", "silent-centralized" };
    }
}
