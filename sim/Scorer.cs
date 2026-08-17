using System;

namespace Hellfire.Sim
{
    public struct SeedOutcome
    {
        public ulong Seed;
        public float Survival;    // fraction not Dead at episode end
        public float Completion;  // fraction Completed (latched)
        public float Composite;
        public int Ticks;         // episode length (early exit possible)
    }

    public sealed class FitnessReport
    {
        public string Name = "";
        public int Agents;
        public int SeedCount;
        public float MeanSurvival;
        public float MeanCompletion;
        public float MeanComposite;
        /// <summary>Std-dev of composite across seeds — the "doctrine variance"
        /// output: how consistent this doctrine's outcomes are.</summary>
        public float StdComposite;
        /// <summary>Std-error of the mean composite — the seed-noise floor the
        /// agent loop must beat before a delta counts as real.</summary>
        public float StdErrComposite;
        public SeedOutcome[] PerSeed = Array.Empty<SeedOutcome>();
    }

    /// <summary>
    /// Headless scorer — the solve.mjs equivalent: doctrine in, numeric fitness
    /// out, no vision critic, no pixel diff. Deterministic: same doctrine + seed
    /// range always yields the identical report.
    /// </summary>
    public static class Scorer
    {
        // Mission-first weighting: the objective is the point; survival is the cost term.
        public const float WeightCompletion = 0.6f;
        public const float WeightSurvival = 0.4f;
        public const int DefaultAgents = 96;
        public const int DefaultMaxTicks = 1800;

        public static FitnessReport Score(Doctrine doctrine, string name,
                                          ulong firstSeed, int seedCount,
                                          int agents = DefaultAgents, int maxTicks = DefaultMaxTicks)
        {
            if (seedCount <= 1) throw new ArgumentOutOfRangeException(nameof(seedCount));
            var perSeed = new SeedOutcome[seedCount];
            double sumS = 0, sumC = 0, sumF = 0, sumF2 = 0;

            for (int k = 0; k < seedCount; k++)
            {
                ulong seed = firstSeed + (ulong)k;
                var end = Simulation.Run(seed, agents, maxTicks, doctrine);
                float survival = 1f - end.CountStatus(AgentStatus.Dead) / (float)agents;
                float completion = end.CountStatus(AgentStatus.Completed) / (float)agents;
                float composite = WeightCompletion * completion + WeightSurvival * survival;
                perSeed[k] = new SeedOutcome
                {
                    Seed = seed, Survival = survival, Completion = completion,
                    Composite = composite, Ticks = end.Tick,
                };
                sumS += survival; sumC += completion; sumF += composite; sumF2 += (double)composite * composite;
            }

            double mean = sumF / seedCount;
            double variance = Math.Max(0.0, sumF2 / seedCount - mean * mean) * seedCount / (seedCount - 1);
            return new FitnessReport
            {
                Name = name,
                Agents = agents,
                SeedCount = seedCount,
                MeanSurvival = (float)(sumS / seedCount),
                MeanCompletion = (float)(sumC / seedCount),
                MeanComposite = (float)mean,
                StdComposite = (float)Math.Sqrt(variance),
                StdErrComposite = (float)Math.Sqrt(variance / seedCount),
                PerSeed = perSeed,
            };
        }

        /// <summary>
        /// Cohen's d between two doctrines' composite fitness — the discrimination
        /// measure the step-2 gate is defined over.
        /// </summary>
        public static float EffectSize(FitnessReport a, FitnessReport b)
        {
            double va = (double)a.StdComposite * a.StdComposite;
            double vb = (double)b.StdComposite * b.StdComposite;
            double pooled = Math.Sqrt(((a.SeedCount - 1) * va + (b.SeedCount - 1) * vb)
                                      / (a.SeedCount + b.SeedCount - 2));
            if (pooled < 1e-9) return Math.Abs(a.MeanComposite - b.MeanComposite) < 1e-9 ? 0f : float.PositiveInfinity;
            return (float)(Math.Abs(a.MeanComposite - b.MeanComposite) / pooled);
        }
    }
}
