using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// Overrides for scenario generation. Defaults reproduce the standard scored
    /// scenario; tests and emergence experiments override to isolate mechanisms
    /// (e.g. jam-free vs jam-saturated worlds).
    /// </summary>
    public sealed class ScenarioConfig
    {
        /// <summary>Negative = seeded draw in [JammerCountMin, JammerCountMax] —
        /// EW weight is a per-run unknown, which is what makes the autonomy dial
        /// a genuine pre-run bet (GDD §1). Non-negative = fixed count.</summary>
        public int JammerCount { get; set; } = -1;
        public int ThreatCount { get; set; } = Scenario.DefaultThreatCount;

        public static readonly ScenarioConfig Default = new ScenarioConfig();
    }

    /// <summary>
    /// The step-2/3 scoring scenario: transit a defended band. Agents spawn along
    /// the south edge, the objective circle sits at the north edge; seeded threat
    /// emplacements and seeded jammer zones occupy the band between. Everything
    /// derives from the seed via DetHash. Premise flavor stays TBD per GDD §5.
    /// </summary>
    public sealed class Scenario
    {
        public const float SpawnBandHeight = 40f;
        public const float ObjectiveX = 256f;
        public const float ObjectiveY = 480f;
        public const float ObjectiveRadius = 40f;
        public const int DefaultThreatCount = 14;
        public const float ThreatBandY0 = 120f;
        public const float ThreatBandY1 = 400f;
        public const float ThreatKillRadius = 26f;
        // Per-tick kill probability at exposure 1.0 inside the engage radius.
        public const float ThreatBaseKillProb = 0.006f;
        // Chatty comms extend the effective engage radius by up to this factor.
        public const float DetectabilityBonus = 0.5f;
        // EW: jammers strip network share inside their radius (binary, for
        // diagnosability — "you were inside jammer 2" beats a falloff curve).
        public const int JammerCountMin = 0;
        // Widened 4 → 6 at step 6 (round-3 flag): with a 0–4 draw the autonomy
        // dial was ~inert in the scored environment (d = 0.09) because heavy-EW
        // worlds were too rare to price in. 0–6 spans "no EW" to "saturated",
        // so how much thinking you delegate is a bet that can actually lose.
        public const int JammerCountMax = 6;
        public const float JammerRadius = 85f;

        public readonly int ThreatCount;
        public readonly float[] ThreatX;
        public readonly float[] ThreatY;
        public readonly int JammerCount;
        public readonly float[] JammerX;
        public readonly float[] JammerY;

        private enum Tag : ulong
        {
            ThreatX = 20, ThreatY = 21,
            JammerCount = 22, JammerX = 23, JammerY = 24,
        }

        public Scenario(ulong seed) : this(seed, ScenarioConfig.Default) { }

        public Scenario(ulong seed, ScenarioConfig config)
        {
            ThreatCount = config.ThreatCount;
            ThreatX = new float[ThreatCount];
            ThreatY = new float[ThreatCount];
            for (int t = 0; t < ThreatCount; t++)
            {
                ulong id = (ulong)t;
                ThreatX[t] = DetHash.Float01(seed, 0, id, (ulong)Tag.ThreatX) * Simulation.WorldWidth;
                ThreatY[t] = ThreatBandY0 + DetHash.Float01(seed, 0, id, (ulong)Tag.ThreatY) * (ThreatBandY1 - ThreatBandY0);
            }

            JammerCount = config.JammerCount >= 0
                ? config.JammerCount
                : JammerCountMin + (int)(DetHash.Hash(seed, 0, 0, (ulong)Tag.JammerCount)
                                         % (ulong)(JammerCountMax - JammerCountMin + 1));
            JammerX = new float[JammerCount];
            JammerY = new float[JammerCount];
            for (int j = 0; j < JammerCount; j++)
            {
                ulong id = (ulong)j;
                JammerX[j] = DetHash.Float01(seed, 0, id, (ulong)Tag.JammerX) * Simulation.WorldWidth;
                JammerY[j] = ThreatBandY0 + DetHash.Float01(seed, 0, id, (ulong)Tag.JammerY) * (ThreatBandY1 - ThreatBandY0);
            }
        }

        public static float EngageRadius(in float commsDiscipline)
        {
            return ThreatKillRadius * (1f + DetectabilityBonus * (1f - commsDiscipline));
        }

        public bool IsJammed(float x, float y)
        {
            const float r2 = JammerRadius * JammerRadius;
            for (int j = 0; j < JammerCount; j++)
            {
                float dx = x - JammerX[j];
                float dy = y - JammerY[j];
                if (dx * dx + dy * dy <= r2) return true;
            }
            return false;
        }
    }
}
