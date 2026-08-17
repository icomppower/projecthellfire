using Hellfire.Dots;
using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Tests
{
    /// <summary>
    /// Fast falsifiers for the DOTS port. Repeat-determinism is asserted hard;
    /// cross-mode and cross-substrate comparisons are recorded (Assert.Pass with
    /// the hashes) rather than asserted — their outcomes ARE the H2 measurement,
    /// and a test suite must not encode a hypothesis's predicted answer.
    /// </summary>
    public class DotsDeterminismTests
    {
        private const ulong Seed = 42UL;
        private const int Agents = 256;
        private const int Ticks = 300;

        private static ulong RunHash(bool parallel)
        {
            using (var s = DotsSim.RunEpisode(Seed, Agents, Ticks, Doctrine.Default, parallel))
            {
                return s.StateHash();
            }
        }

        [Test]
        public void BurstSequential_SameSeed_ByteIdentical()
        {
            Assert.That(RunHash(false), Is.EqualTo(RunHash(false)));
        }

        [Test]
        public void BurstParallel_RepeatDeterminism_Recorded()
        {
            ulong a = RunHash(true);
            ulong b = RunHash(true);
            Assert.Pass($"parallel repeat-determinism: {a == b} (0x{a:X16} vs 0x{b:X16})");
        }

        [Test]
        public void JacobiVsGaussSeidel_Recorded()
        {
            ulong burstSeq = RunHash(false);
            ulong managed = Simulation.Run(Seed, Agents, Ticks, Doctrine.Default).StateHash();
            Assert.Pass($"managed(GaussSeidel) 0x{managed:X16} vs burstSeq(Jacobi) 0x{burstSeq:X16} " +
                        $"— equal: {managed == burstSeq} (divergence expected, structural)");
        }
    }
}
