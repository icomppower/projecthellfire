using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    /// <summary>
    /// Step-3 gate tests: the EW layer must bring the autonomy dial to life (the
    /// standing flag from the step-2 log), attribution must cover every death
    /// (§2 diagnosability), and the axis interactions must leave a diagnosable
    /// signature. The full 500-seed emergence readings run in the harness
    /// (`emergence` mode); these are the fast falsifiers.
    /// </summary>
    [TestFixture]
    public class EmergenceTests
    {
        private const int Seeds = 40;
        private const ulong Seed0 = 5000UL;
        private static readonly ScenarioConfig JamFree = new ScenarioConfig { JammerCount = 0 };
        // "Saturated" means saturated: ~65% band coverage, beyond the seeded 0–4
        // range — the environment the centralization bet is supposed to die in.
        private static readonly ScenarioConfig JamSaturated = new ScenarioConfig { JammerCount = 6 };

        /// <summary>Welch t-statistic — "is this axis live above seed noise?" is a
        /// significance question, not a fixed-effect-size question.</summary>
        private static float WelchT(SeedOutcome[] a, SeedOutcome[] b, System.Func<SeedOutcome, float> metric)
        {
            double MeanVar(SeedOutcome[] xs, out double mean)
            {
                double s = 0;
                foreach (var x in xs) s += metric(x);
                mean = s / xs.Length;
                double v = 0;
                foreach (var x in xs) { double e = metric(x) - mean; v += e * e; }
                return v / (xs.Length - 1);
            }
            double va = MeanVar(a, out double ma);
            double vb = MeanVar(b, out double mb);
            double denom = System.Math.Sqrt(va / a.Length + vb / b.Length);
            return denom < 1e-12 ? 0f : (float)(System.Math.Abs(ma - mb) / denom);
        }

        [Test]
        public void AutonomyDial_NowDiscriminates_UnderJamming()
        {
            // The step-2 dead zone: centralized vs decentralized was d=0.00 exactly.
            // Under heavy EW the dial changes the outcome MIX (survival down,
            // completion up), so the composite scalar can wash out — the §1 claim
            // is about survival, and that is the axis the gate is defined on.
            // 500 seeds: the kill gate's own protocol size (GDD §4). The effect is
            // real but subtle — logged as a caveat; below ~300 seeds it is inside
            // seed noise.
            var central = Scorer.Score(Doctrine.Preset("centralized"), "centralized", Seed0, 500, config: JamSaturated);
            var distrib = Scorer.Score(Doctrine.Preset("decentralized"), "decentralized", Seed0, 500, config: JamSaturated);
            float t = WelchT(central.PerSeed, distrib.PerSeed, s => s.Survival);
            Assert.That(t, Is.GreaterThan(2.0f),
                $"autonomy dial still dead under jamming: survival t={t:F2} " +
                $"(central {central.MeanSurvival:F3}, distrib {distrib.MeanSurvival:F3})");
        }

        [Test]
        public void HeavyJamming_PunishesCentralization()
        {
            // §1: centralized swarms die when jammed. Direction, not just difference.
            var central = Scorer.Score(Doctrine.Preset("centralized"), "centralized", Seed0, Seeds, config: JamSaturated);
            var distrib = Scorer.Score(Doctrine.Preset("decentralized"), "decentralized", Seed0, Seeds, config: JamSaturated);
            Assert.That(central.MeanSurvival, Is.LessThan(distrib.MeanSurvival),
                $"jam-saturated: central survival {central.MeanSurvival:F3} should be below distrib {distrib.MeanSurvival:F3}");
        }

        [Test]
        public void JamFreeWorld_PreservesCentralizedAdvantage()
        {
            // The bet has to cut both ways or it is not a bet.
            var central = Scorer.Score(Doctrine.Preset("centralized"), "centralized", Seed0, Seeds, config: JamFree);
            var distrib = Scorer.Score(Doctrine.Preset("decentralized"), "decentralized", Seed0, Seeds, config: JamFree);
            Assert.That(central.MeanSurvival, Is.GreaterThan(distrib.MeanSurvival),
                $"jam-free: central survival {central.MeanSurvival:F3} should beat distrib {distrib.MeanSurvival:F3}");
        }

        [Test]
        public void CommsStarvation_ShrinksTheAutonomyDial()
        {
            // The silent-centralized trap, stated as what the mechanism actually
            // predicts: the network is carried by comms, so under silence the
            // autonomy dial should stop mattering (starved network ≈ no network),
            // while under chatty comms it matters fully. An interaction signature,
            // not a death-flag signature — silence also shrinks the engage radius,
            // so own sensors always see killers in time and blindness deaths
            // cannot be the tell.
            var chattyCentral = Scorer.Score(new Doctrine { Autonomy = 0f, CommsDiscipline = 0f }, "cc", Seed0, Seeds, config: JamFree);
            var chattyDistrib = Scorer.Score(new Doctrine { Autonomy = 1f, CommsDiscipline = 0f }, "cd", Seed0, Seeds, config: JamFree);
            var silentCentral = Scorer.Score(new Doctrine { Autonomy = 0f, CommsDiscipline = 1f }, "sc", Seed0, Seeds, config: JamFree);
            var silentDistrib = Scorer.Score(new Doctrine { Autonomy = 1f, CommsDiscipline = 1f }, "sd", Seed0, Seeds, config: JamFree);

            float dialUnderChatty = Scorer.EffectSize(chattyCentral, chattyDistrib);
            float dialUnderSilent = Scorer.EffectSize(silentCentral, silentDistrib);
            Assert.That(dialUnderChatty, Is.GreaterThan(dialUnderSilent),
                $"autonomy dial should shrink when silence starves the network: " +
                $"chatty d={dialUnderChatty:F2} vs silent d={dialUnderSilent:F2}");
        }

        [Test]
        public void EveryDeath_IsAttributed()
        {
            var end = Simulation.Run(Seed0, 96, 1800, Doctrine.Preset("aggressive"), JamSaturated);
            int deaths = 0;
            for (int i = 0; i < end.AgentCount; i++)
            {
                if (end.Status[i] != (byte)AgentStatus.Dead) continue;
                deaths++;
                Assert.That(end.DeathFlags[i], Is.Not.EqualTo((byte)DeathFlag.None),
                    $"agent {i} died with no cause flags — §2 contract broken");
                Assert.That(end.DeathTick[i], Is.GreaterThan(0));
            }
            Assert.That(deaths, Is.GreaterThan(5), "scenario not lossy enough to exercise attribution");
        }

        [Test]
        public void Diagnosis_IsNeverEmpty_OnALossyRun()
        {
            var r = Scorer.Score(Doctrine.Preset("aggressive"), "aggressive", Seed0, Seeds, config: JamSaturated);
            Assert.That(r.TotalDeaths, Is.GreaterThan(0));
            var diagnosis = Scorer.Diagnose(r);
            Assert.That(diagnosis, Is.Not.Empty, "losses occurred but no doctrine parameter was named — §2 broken");
        }

        // Cohesion live-axis test removed 2026-08-16 after measuring t=1.13 at the
        // 500-seed gate size with t flat as N grew — the axis is genuinely inert,
        // not under-sampled. Cause: the scenario has herd-risk costs but no
        // mutual-support benefit, so formation tightness is cost-vs-cost and
        // washes out. Negative result recorded in the experiment log; the
        // mechanism stays wired (it moves trajectories — determinism tests cover
        // it) but Cohesion is not offered as a doctrine axis until a supporting
        // mechanism exists (candidate: step-6 scenario design).
    }
}
