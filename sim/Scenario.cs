using System;

namespace Hellfire.Sim
{
    /// <summary>
    /// The step-2 scoring scenario: transit a defended band. Agents spawn along the
    /// south edge, the objective circle sits at the north edge, and seeded threat
    /// emplacements occupy the band between. Everything is derived from the seed
    /// via DetHash — no scenario state survives outside SimState + this readback.
    /// Premise flavor stays TBD per GDD §5; this is deliberately abstract geometry.
    /// </summary>
    public sealed class Scenario
    {
        public const float SpawnBandHeight = 40f;
        public const float ObjectiveX = 256f;
        public const float ObjectiveY = 480f;
        public const float ObjectiveRadius = 40f;
        public const int ThreatCount = 14;
        public const float ThreatBandY0 = 120f;
        public const float ThreatBandY1 = 400f;
        public const float ThreatKillRadius = 26f;
        // Per-tick kill probability at exposure 1.0 inside the engage radius.
        public const float ThreatBaseKillProb = 0.006f;
        // Chatty comms extend the effective engage radius by up to this factor.
        public const float DetectabilityBonus = 0.5f;

        public readonly float[] ThreatX = new float[ThreatCount];
        public readonly float[] ThreatY = new float[ThreatCount];

        private enum Tag : ulong { ThreatX = 20, ThreatY = 21 }

        public Scenario(ulong seed)
        {
            for (int t = 0; t < ThreatCount; t++)
            {
                ulong id = (ulong)t;
                ThreatX[t] = DetHash.Float01(seed, 0, id, (ulong)Tag.ThreatX) * Simulation.WorldWidth;
                ThreatY[t] = ThreatBandY0 + DetHash.Float01(seed, 0, id, (ulong)Tag.ThreatY) * (ThreatBandY1 - ThreatBandY0);
            }
        }

        public static float EngageRadius(in float commsDiscipline)
        {
            return ThreatKillRadius * (1f + DetectabilityBonus * (1f - commsDiscipline));
        }
    }
}
