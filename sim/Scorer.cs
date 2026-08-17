using System;
using System.Collections.Generic;

namespace Hellfire.Sim
{
    public struct SeedOutcome
    {
        public ulong Seed;
        public float Survival;    // fraction not Dead at episode end
        public float Completion;  // fraction Completed (latched)
        public float Composite;
        public int Ticks;         // episode length (early exit possible)
        public bool Aborted;
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
        public float AbortRate;
        /// <summary>Fraction of agents still Active at episode end (stragglers).</summary>
        public float MeanStragglers;
        // §2 diagnosability: death-cause shares, as fractions of all deaths
        // across all seeds (flags are non-exclusive; shares can sum past 1).
        public int TotalDeaths;
        public float DeathShareUnknown;
        public float DeathShareJammed;
        public float DeathShareDetected;
        public float DeathSharePressed;
        public SeedOutcome[] PerSeed = Array.Empty<SeedOutcome>();
    }

    /// <summary>
    /// Headless scorer — doctrine in, numeric fitness out, no vision critic, no
    /// pixel diff. Deterministic: same doctrine + seed range yields the identical
    /// report. Also hosts the §2 post-run diagnosis: every loss maps to the
    /// doctrine parameter responsible.
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
                                          int agents = DefaultAgents, int maxTicks = DefaultMaxTicks,
                                          ScenarioConfig? config = null)
        {
            if (seedCount <= 1) throw new ArgumentOutOfRangeException(nameof(seedCount));
            config ??= ScenarioConfig.Default;
            var perSeed = new SeedOutcome[seedCount];
            double sumS = 0, sumC = 0, sumF = 0, sumF2 = 0, sumStrag = 0;
            int aborts = 0, deaths = 0, fUnknown = 0, fJammed = 0, fDetected = 0, fPressed = 0;

            for (int k = 0; k < seedCount; k++)
            {
                ulong seed = firstSeed + (ulong)k;
                var end = Simulation.Run(seed, agents, maxTicks, doctrine, config);
                int dead = end.CountStatus(AgentStatus.Dead);
                float survival = 1f - dead / (float)agents;
                float completion = end.CountStatus(AgentStatus.Completed) / (float)agents;
                float composite = WeightCompletion * completion + WeightSurvival * survival;
                perSeed[k] = new SeedOutcome
                {
                    Seed = seed, Survival = survival, Completion = completion,
                    Composite = composite, Ticks = end.Tick, Aborted = end.Aborted,
                };
                if (end.Aborted) aborts++;
                deaths += dead;
                fUnknown += end.CountDeathFlag(DeathFlag.UnknownThreat);
                fJammed += end.CountDeathFlag(DeathFlag.Jammed);
                fDetected += end.CountDeathFlag(DeathFlag.Detected);
                fPressed += end.CountDeathFlag(DeathFlag.PressedKnown);
                sumStrag += end.CountStatus(AgentStatus.Active) / (double)agents;
                sumS += survival; sumC += completion; sumF += composite; sumF2 += (double)composite * composite;
            }

            double mean = sumF / seedCount;
            double variance = Math.Max(0.0, sumF2 / seedCount - mean * mean) * seedCount / (seedCount - 1);
            float inv = deaths > 0 ? 1f / deaths : 0f;
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
                AbortRate = aborts / (float)seedCount,
                MeanStragglers = (float)(sumStrag / seedCount),
                TotalDeaths = deaths,
                DeathShareUnknown = fUnknown * inv,
                DeathShareJammed = fJammed * inv,
                DeathShareDetected = fDetected * inv,
                DeathSharePressed = fPressed * inv,
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

        /// <summary>
        /// The §2 contract, executable: ranked (doctrine parameter, evidence)
        /// attribution for a report's losses. "Punishment lands on doctrine you
        /// never re-examined" only works if this list is never empty on a loss.
        /// </summary>
        public static List<(string Parameter, float Share, string Evidence)> Diagnose(FitnessReport r)
        {
            var items = new List<(string, float, string)>();
            if (r.TotalDeaths > 0)
            {
                // Unknown-threat deaths split by jamming: same symptom, opposite fixes.
                float unknownJammed = Math.Min(r.DeathShareUnknown, r.DeathShareJammed);
                float unknownBlind = Math.Max(0f, r.DeathShareUnknown - unknownJammed);
                if (unknownJammed > 0f)
                    items.Add(("Autonomy (too low for the EW environment)", unknownJammed,
                               "killed by threats the jammed network could no longer show them"));
                if (unknownBlind > 0f)
                    items.Add(("Autonomy/SensorRange (flying blind)", unknownBlind,
                               "killed by threats outside sensor reach with no network picture"));
                if (r.DeathShareDetected > 0f)
                    items.Add(("CommsDiscipline (too chatty)", r.DeathShareDetected,
                               "killed inside the detectability-extended engagement band"));
                if (r.DeathSharePressed > 0f)
                    items.Add(("RiskTolerance (pressed known kill zones)", r.DeathSharePressed,
                               "killed by threats they knew about and approached anyway"));
            }
            if (r.AbortRate > 0.05f)
                items.Add(("AbortLossFraction", r.AbortRate,
                           $"mission aborted in {r.AbortRate:P0} of runs at the loss threshold"));
            if (r.MeanStragglers > 0.05f)
                items.Add(("Cohesion/MaxSpeed (stragglers)", r.MeanStragglers,
                           "agents still in transit when time expired"));
            items.Sort((a, b) => b.Item2.CompareTo(a.Item2));
            return items;
        }
    }
}
