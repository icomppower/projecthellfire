using Hellfire.Sim;
using NUnit.Framework;

namespace Hellfire.Sim.Tests
{
    /// <summary>
    /// Step-6 gate: interrupts are deterministic, doctrine-level, capped, and
    /// each one produces its intended swarm-level consequence.
    /// </summary>
    [TestFixture]
    public class InterruptTests
    {
        private const ulong Seed = 42UL;
        private const int Agents = 96;

        private static InterruptPlan Plan(params InterruptOrder[] orders) => new InterruptPlan(orders);

        [Test]
        public void SamePlan_ByteIdenticalReplay()
        {
            var plan = Plan(new InterruptOrder(200, InterruptType.FallBack),
                            new InterruptOrder(900, InterruptType.CommitReserve));
            var doctrine = new Doctrine { ReserveFraction = 0.25f };
            var a = Simulation.Run(Seed, Agents, 1500, doctrine, ScenarioConfig.Default, plan);
            var b = Simulation.Run(Seed, Agents, 1500, doctrine, ScenarioConfig.Default, plan);
            Assert.That(b.StateHash(), Is.EqualTo(a.StateHash()));
        }

        [Test]
        public void DifferentPlan_DifferentOutcome()
        {
            var doctrine = Doctrine.Default;
            var none = Simulation.Run(Seed, Agents, 1000, doctrine);
            var recalled = Simulation.Run(Seed, Agents, 1000, doctrine, ScenarioConfig.Default,
                                          Plan(new InterruptOrder(300, InterruptType.FallBack)));
            Assert.That(recalled.StateHash(), Is.Not.EqualTo(none.StateHash()));
        }

        [Test]
        public void Abort_SendsSwarmHome()
        {
            var end = Simulation.Run(Seed, Agents, 1800, Doctrine.Default, ScenarioConfig.Default,
                                     Plan(new InterruptOrder(120, InterruptType.Abort)));
            Assert.That(end.Aborted, Is.True);
            // Early abort, before the threat band: nearly everyone gets out.
            Assert.That(end.CountStatus(AgentStatus.Safe), Is.GreaterThan(Agents / 2));
            Assert.That(end.CountStatus(AgentStatus.Completed), Is.EqualTo(0).Within(2));
        }

        [Test]
        public void Reserve_HeldWithoutCommit_LaunchedByCommit()
        {
            var doctrine = new Doctrine { ReserveFraction = 0.25f };
            int reserveSize = (int)(Agents * 0.25f);

            var held = Simulation.Run(Seed, Agents, 600, doctrine);
            Assert.That(held.CountStatus(AgentStatus.Reserve), Is.EqualTo(reserveSize),
                "uncommitted reserve must stay held");

            var committed = Simulation.Run(Seed, Agents, 600, doctrine, ScenarioConfig.Default,
                                           Plan(new InterruptOrder(60, InterruptType.CommitReserve)));
            Assert.That(committed.CountStatus(AgentStatus.Reserve), Is.EqualTo(0),
                "committed reserve must launch");
        }

        [Test]
        public void HeldReserve_KeepsRunAlive_AndCannotComplete()
        {
            var doctrine = new Doctrine { ReserveFraction = 0.25f, RiskTolerance = 0.95f, AbortLossFraction = 0.95f };
            var end = Simulation.Run(Seed, Agents, 1800, doctrine);
            int reserveSize = (int)(Agents * 0.25f);
            Assert.That(end.CountStatus(AgentStatus.Reserve), Is.EqualTo(reserveSize));
            // Reserve survives by construction; completion ceiling shrinks to 75%.
            Assert.That(end.CountStatus(AgentStatus.Completed), Is.LessThanOrEqualTo(Agents - reserveSize));
        }

        [Test]
        public void FallBack_ExpiresAndMissionResumes()
        {
            var doctrine = Doctrine.Default;
            var plan = Plan(new InterruptOrder(150, InterruptType.FallBack));
            var mid = Simulation.Run(Seed, Agents, 150 + Simulation.RecallDurationTicks / 2, doctrine,
                                     ScenarioConfig.Default, plan);
            var end = Simulation.Run(Seed, Agents, 3000, doctrine, ScenarioConfig.Default, plan);
            // During recall nobody is marked Safe (mission not aborted)...
            Assert.That(mid.Aborted, Is.False);
            Assert.That(mid.CountStatus(AgentStatus.Safe), Is.EqualTo(0));
            // ...and after it expires the swarm goes on to complete at least as
            // often as not at all — the mission genuinely resumes.
            Assert.That(end.CountStatus(AgentStatus.Completed), Is.GreaterThan(0));
        }

        [Test]
        public void PlanCap_EnforcedAtThree()
        {
            Assert.Throws<System.ArgumentException>(() => new InterruptPlan(new[]
            {
                new InterruptOrder(1, InterruptType.FallBack),
                new InterruptOrder(2, InterruptType.FallBack),
                new InterruptOrder(3, InterruptType.FallBack),
                new InterruptOrder(4, InterruptType.Abort),
            }));
        }
    }
}
