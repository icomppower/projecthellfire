using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    [TestFixture]
    public class DeterminismTests
    {
        [Test]
        public void SameSeed_TwoRuns_ByteIdenticalFinalHash()
        {
            var a = Simulation.Run(seed: 42UL, agentCount: 96, maxTicks: 500, Doctrine.Default);
            var b = Simulation.Run(seed: 42UL, agentCount: 96, maxTicks: 500, Doctrine.Default);
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
        }

        [Test]
        public void DifferentSeeds_DifferentFinalHash()
        {
            var a = Simulation.Run(seed: 1UL, agentCount: 96, maxTicks: 200, Doctrine.Default);
            var b = Simulation.Run(seed: 2UL, agentCount: 96, maxTicks: 200, Doctrine.Default);
            Assert.That(b.StateHash(), Is.Not.EqualTo(a.StateHash()));
        }

        [Test]
        public void TickIsPure_SameInputsSameOutput_FromMidEpisodeState()
        {
            var doctrine = Doctrine.Default;
            var sim = new Simulation(96, seed: 7UL);
            var state = Simulation.CreateInitialState(96, seed: 7UL);
            for (int t = 0; t < 100; t++) sim.Tick(state, doctrine, 7UL);

            var copyA = state.Clone();
            var copyB = state.Clone();
            var simA = new Simulation(96, seed: 7UL);
            var simB = new Simulation(96, seed: 7UL);
            simA.Tick(copyA, doctrine, 7UL);
            simB.Tick(copyB, doctrine, 7UL);
            Assert.That(copyB.StateHash(), Is.EqualTo(copyA.StateHash()));
        }

        [Test]
        public void DoctrineChange_ChangesOutcome()
        {
            var baseline = Doctrine.Default;
            var altered = new Doctrine { RiskTolerance = baseline.RiskTolerance + 0.1f };
            var a = Simulation.Run(seed: 9UL, agentCount: 96, maxTicks: 300, baseline);
            var b = Simulation.Run(seed: 9UL, agentCount: 96, maxTicks: 300, altered);
            Assert.That(b.StateHash(), Is.Not.EqualTo(a.StateHash()));
        }
    }

    [TestFixture]
    public class DetHashTests
    {
        [Test]
        public void SameInputs_SameOutput()
        {
            Assert.That(DetHash.Hash(1, 2, 3, 4), Is.EqualTo(DetHash.Hash(1, 2, 3, 4)));
        }

        [Test]
        public void EachArgumentPerturbsResult()
        {
            ulong baseline = DetHash.Hash(1, 2, 3, 4);
            Assert.That(DetHash.Hash(9, 2, 3, 4), Is.Not.EqualTo(baseline));
            Assert.That(DetHash.Hash(1, 9, 3, 4), Is.Not.EqualTo(baseline));
            Assert.That(DetHash.Hash(1, 2, 9, 4), Is.Not.EqualTo(baseline));
            Assert.That(DetHash.Hash(1, 2, 3, 9), Is.Not.EqualTo(baseline));
        }

        [Test]
        public void Float01_StaysInRange()
        {
            for (ulong i = 0; i < 10000; i++)
            {
                float v = DetHash.Float01(123, i, i * 7);
                Assert.That(v, Is.GreaterThanOrEqualTo(0f).And.LessThan(1f));
            }
        }

        [Test]
        public void Float01_RoughlyUniform()
        {
            int low = 0;
            const int n = 100000;
            for (ulong i = 0; i < n; i++)
            {
                if (DetHash.Float01(55, i, 0) < 0.5f) low++;
            }
            Assert.That(low, Is.InRange(48500, 51500));
        }
    }
}
