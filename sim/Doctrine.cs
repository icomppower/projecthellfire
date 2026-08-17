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
                default: throw new System.ArgumentException($"unknown preset '{name}'", nameof(name));
            }
        }

        public static readonly string[] PresetNames =
            { "default", "aggressive", "cautious", "chatty", "silent", "centralized", "decentralized" };
    }
}
