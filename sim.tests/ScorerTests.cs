using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    /// <summary>
    /// Step-2 gate: the scorer must be deterministic, must discriminate between
    /// doctrines above seed noise, and must move in the predicted direction on
    /// single-axis probes. A scorer that fails any of these wastes the loop.
    /// </summary>
    [TestFixture]
    public class ScorerTests
    {
        private const int Seeds = 30;
        private const ulong Seed0 = 5000UL;

        [Test]
        public void Fitness_IsDeterministic()
        {
            var a = Scorer.Score(Doctrine.Preset("default"), "a", Seed0, Seeds);
            var b = Scorer.Score(Doctrine.Preset("default"), "b", Seed0, Seeds);
            Assert.That(b.MeanComposite, Is.EqualTo(a.MeanComposite));
            for (int i = 0; i < Seeds; i++)
            {
                Assert.That(b.PerSeed[i].Composite, Is.EqualTo(a.PerSeed[i].Composite));
                Assert.That(b.PerSeed[i].Ticks, Is.EqualTo(a.PerSeed[i].Ticks));
            }
        }

        [Test]
        public void Discriminates_AggressiveVsCautious_AboveSeedNoise()
        {
            var agg = Scorer.Score(Doctrine.Preset("aggressive"), "aggressive", Seed0, Seeds);
            var cau = Scorer.Score(Doctrine.Preset("cautious"), "cautious", Seed0, Seeds);
            float d = Scorer.EffectSize(agg, cau);
            Assert.That(d, Is.GreaterThan(1.0f),
                $"effect size {d:F2} — scorer cannot separate opposite doctrines from seed wobble " +
                $"(agg {agg.MeanComposite:F3}±{agg.StdComposite:F3}, cau {cau.MeanComposite:F3}±{cau.StdComposite:F3})");
        }

        [Test]
        public void Probe_RiskTolerance_HighRiskLowersSurvival()
        {
            var risky = Scorer.Score(new Doctrine { RiskTolerance = 0.95f }, "risky", Seed0, Seeds);
            var timid = Scorer.Score(new Doctrine { RiskTolerance = 0.05f }, "timid", Seed0, Seeds);
            Assert.That(risky.MeanSurvival, Is.LessThan(timid.MeanSurvival),
                $"risky {risky.MeanSurvival:F3} vs timid {timid.MeanSurvival:F3}");
        }

        [Test]
        public void Probe_AbortThreshold_TradesObjectiveForSurvival()
        {
            var early = Scorer.Score(new Doctrine { AbortLossFraction = 0.05f, RiskTolerance = 0.7f },
                                     "early-abort", Seed0, Seeds);
            var never = Scorer.Score(new Doctrine { AbortLossFraction = 0.95f, RiskTolerance = 0.7f },
                                     "never-abort", Seed0, Seeds);
            Assert.That(early.MeanSurvival, Is.GreaterThanOrEqualTo(never.MeanSurvival),
                $"early-abort survival {early.MeanSurvival:F3} vs never-abort {never.MeanSurvival:F3}");
            Assert.That(early.MeanCompletion, Is.LessThan(never.MeanCompletion),
                $"early-abort completion {early.MeanCompletion:F3} vs never-abort {never.MeanCompletion:F3}");
        }

        [Test]
        public void Probe_CommsDiscipline_SilenceImprovesSurvival_WhenNetworkUnused()
        {
            // Pinned to autonomy=1 so comms has no knowledge cost (network share is
            // zero either way) — isolating the detectability mechanism. At lower
            // autonomy the axis is deliberately a tradeoff, probed in EmergenceTests.
            var chatty = Scorer.Score(new Doctrine { Autonomy = 1f, CommsDiscipline = 0f }, "chatty", Seed0, Seeds);
            var silent = Scorer.Score(new Doctrine { Autonomy = 1f, CommsDiscipline = 1f }, "silent", Seed0, Seeds);
            Assert.That(silent.MeanSurvival, Is.GreaterThan(chatty.MeanSurvival),
                $"silent {silent.MeanSurvival:F3} vs chatty {chatty.MeanSurvival:F3}");
        }

        [Test]
        public void Probe_Autonomy_CentralizedKnowledgeHelps_JamFree()
        {
            // In a jam-free world the network is pure upside; the counterweight
            // (jamming) is probed in EmergenceTests.
            var jamFree = new ScenarioConfig { JammerCount = 0 };
            var central = Scorer.Score(Doctrine.Preset("centralized"), "centralized", Seed0, Seeds, config: jamFree);
            var distrib = Scorer.Score(Doctrine.Preset("decentralized"), "decentralized", Seed0, Seeds, config: jamFree);
            Assert.That(central.MeanSurvival, Is.GreaterThan(distrib.MeanSurvival),
                $"centralized {central.MeanSurvival:F3} vs decentralized {distrib.MeanSurvival:F3}");
        }

        [Test]
        public void Outcomes_AreNotDegenerate()
        {
            // Guard against a scenario where everyone always dies or always wins —
            // both make every doctrine look identical and void the gate.
            var r = Scorer.Score(Doctrine.Preset("default"), "default", Seed0, Seeds);
            Assert.That(r.MeanSurvival, Is.InRange(0.05f, 0.98f));
            Assert.That(r.MeanCompletion, Is.InRange(0.05f, 0.98f));
        }
    }
}
