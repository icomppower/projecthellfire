using Hellfire.Presentation;
using Hellfire.Sim;
using NUnit.Framework;
using UnityEngine;

namespace Hellfire.Tests
{
    /// <summary>
    /// The GDD §3 load-bearing invariant, tested from inside Unity: the sim
    /// core embedded in a MonoBehaviour driver produces byte-identical state to
    /// the headless .NET path. If Unity's runtime (domain, IL2CPP-adjacent
    /// settings, float env) perturbed the sim, or the driver leaked frame state
    /// into ticks, this hash comparison is where it surfaces.
    /// </summary>
    public class RenderNeverWritesTests
    {
        [Test]
        public void DriverPath_MatchesHeadlessPath_ByteIdentical()
        {
            const ulong seed = 42UL;
            const int agents = 96;
            const int ticks = 600;

            var headless = Simulation.Run(seed, agents, ticks, Doctrine.Default);

            var go = new GameObject("driver-under-test");
            try
            {
                var driver = go.AddComponent<SimDriver>();
                driver.agentCount = agents;
                driver.seed = seed;
                driver.maxTicks = ticks;
                // Awake does not run for AddComponent in EditMode tests reliably;
                // initialize explicitly through the public reset path.
                driver.ResetSim();
                driver.StepTicks(ticks);

                Assert.That(driver.State.Tick, Is.EqualTo(headless.Tick));
                Assert.That(driver.State.StateHash(), Is.EqualTo(headless.StateHash()),
                    "sim state diverged between Unity driver and headless .NET — the §3 invariant is broken");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void DoctrineAsset_RoundTrips_ToSimDoctrine()
        {
            var asset = ScriptableObject.CreateInstance<DoctrineAsset>();
            try
            {
                asset.autonomy = 0.9f;
                asset.commsDiscipline = 0.1f;
                asset.abortLossFraction = 0.35f;
                var d = asset.ToDoctrine();
                Assert.That(d.Autonomy, Is.EqualTo(0.9f));
                Assert.That(d.CommsDiscipline, Is.EqualTo(0.1f));
                Assert.That(d.AbortLossFraction, Is.EqualTo(0.35f));
            }
            finally
            {
                Object.DestroyImmediate(asset);
            }
        }
    }
}
