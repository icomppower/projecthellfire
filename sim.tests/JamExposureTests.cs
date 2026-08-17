using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    [TestFixture]
    public class JamExposureTests
    {
        [Test]
        public void SameSeed_TwoRuns_ByteIdenticalFinalHash_WithJammers()
        {
            var config = new ScenarioConfig { JammerCount = 6 };
            var a = Simulation.Run(seed: 42UL, agentCount: 96, maxTicks: 500, Doctrine.Default, config);
            var b = Simulation.Run(seed: 42UL, agentCount: 96, maxTicks: 500, Doctrine.Default, config);
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
        }

        [Test]
        public void NoJammers_JammedNowCountAlwaysZero()
        {
            var doctrine = Doctrine.Default;
            var config = new ScenarioConfig { JammerCount = 0 };
            var sim = new Simulation(96, seed: 5UL, config);
            var state = Simulation.CreateInitialState(96, seed: 5UL, doctrine.ReserveFraction);

            for (int t = 0; t < 500; t++)
            {
                sim.Tick(state, doctrine, 5UL);
                Assert.That(state.JammedNowCount, Is.EqualTo(0),
                    $"expected JammedNowCount == 0 at tick {state.Tick} with JammerCount = 0");
            }
        }

        [Test]
        public void SixJammers_SomeTickHasPositiveJammedNowCount()
        {
            var doctrine = Doctrine.Default;
            var config = new ScenarioConfig { JammerCount = 6 };
            var sim = new Simulation(96, seed: 11UL, config);
            var state = Simulation.CreateInitialState(96, seed: 11UL, doctrine.ReserveFraction);

            bool sawJammed = false;
            for (int t = 0; t < 500; t++)
            {
                sim.Tick(state, doctrine, 11UL);
                if (state.JammedNowCount > 0)
                {
                    sawJammed = true;
                    break;
                }
            }
            Assert.That(sawJammed, Is.True, "expected at least one tick with JammedNowCount > 0 for JammerCount = 6");
        }
    }
}
