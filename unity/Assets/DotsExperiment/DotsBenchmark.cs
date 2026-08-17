using System;
using System.Diagnostics;
using Hellfire.Sim;
using UnityEditor;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Hellfire.Dots
{
    /// <summary>
    /// H1 (agent ceiling) and H2 (replay divergence) measurement entry points,
    /// run via -executeMethod. Numeric outcomes are recorded as measured — no
    /// tuning lives here. See the pre-registration amendment (2026-08-17) for
    /// how the three H2 comparisons are to be read.
    /// </summary>
    public static class DotsBenchmark
    {
        private static readonly int[] H1AgentCounts = { 512, 2048, 8192, 32768 };
        private const int H1Warmup = 30;
        private const float FrameBudgetMs = 1000f / 60f;

        public static void RunH1()
        {
            try
            {
                var doctrine = Doctrine.Default;
                Debug.Log("[H1] agents | managed ms/tick | burstSeq ms/tick | burstPar ms/tick");
                var managedMs = new float[H1AgentCounts.Length];
                var seqMs = new float[H1AgentCounts.Length];
                var parMs = new float[H1AgentCounts.Length];

                for (int a = 0; a < H1AgentCounts.Length; a++)
                {
                    int agents = H1AgentCounts[a];
                    // Same measured-tick count per path; fewer ticks at high agent
                    // counts to keep the managed path's wall-clock sane.
                    int measured = Math.Max(30, 240 * 512 / agents);

                    managedMs[a] = MeasureManaged(agents, measured, doctrine);
                    seqMs[a] = MeasureDots(agents, measured, doctrine, parallel: false);
                    parMs[a] = MeasureDots(agents, measured, doctrine, parallel: true);
                    Debug.Log($"[H1] {agents,6} | {managedMs[a],10:F3} | {seqMs[a],10:F3} | {parMs[a],10:F3}" +
                              $"  (x{managedMs[a] / seqMs[a]:F1} seq, x{managedMs[a] / parMs[a]:F1} par, {measured} ticks)");
                }

                Debug.Log($"[H1] 60fps ceiling (agents at {FrameBudgetMs:F2} ms/tick): " +
                          $"managed ~{Ceiling(H1AgentCounts, managedMs):F0}, " +
                          $"burstSeq ~{Ceiling(H1AgentCounts, seqMs):F0}, " +
                          $"burstPar ~{Ceiling(H1AgentCounts, parMs):F0}");
                Debug.Log("[H1] done");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[H1] FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static float MeasureManaged(int agents, int measured, Doctrine doctrine)
        {
            var sim = new Simulation(agents, 42UL);
            var state = Simulation.CreateInitialState(agents, 42UL);
            for (int t = 0; t < H1Warmup; t++) sim.Tick(state, doctrine, 42UL);
            var sw = Stopwatch.StartNew();
            for (int t = 0; t < measured; t++) sim.Tick(state, doctrine, 42UL);
            sw.Stop();
            return (float)(sw.Elapsed.TotalMilliseconds / measured);
        }

        private static float MeasureDots(int agents, int measured, Doctrine doctrine, bool parallel)
        {
            using (var sim = new DotsSim(agents, 42UL, doctrine))
            {
                for (int t = 0; t < H1Warmup; t++) sim.Step(parallel);
                var sw = Stopwatch.StartNew();
                for (int t = 0; t < measured; t++) sim.Step(parallel);
                sw.Stop();
                return (float)(sw.Elapsed.TotalMilliseconds / measured);
            }
        }

        /// <summary>Piecewise-linear interpolation of agent count at the frame
        /// budget; extrapolates from the last segment when the budget was never hit.</summary>
        private static float Ceiling(int[] counts, float[] msPerTick)
        {
            for (int i = 0; i < counts.Length; i++)
            {
                if (msPerTick[i] < FrameBudgetMs) continue;
                if (i == 0) return counts[0] * FrameBudgetMs / msPerTick[0];
                float f = (FrameBudgetMs - msPerTick[i - 1]) / (msPerTick[i] - msPerTick[i - 1]);
                return counts[i - 1] + f * (counts[i] - counts[i - 1]);
            }
            int last = counts.Length - 1;
            return counts[last] * FrameBudgetMs / msPerTick[last];
        }

        private const int H2Agents = 512;
        private const int H2Ticks = 2000;
        private const int H2Seeds = 500;
        private const int H2ManagedSeeds = 100;
        private const ulong H2Seed0 = 5000UL;

        public static void RunH2()
        {
            try
            {
                var doctrine = Doctrine.Default;

                int seqParMatch = 0;
                ulong firstSeqParMismatch = 0;
                for (int k = 0; k < H2Seeds; k++)
                {
                    ulong seed = H2Seed0 + (ulong)k;
                    ulong hSeq, hPar;
                    using (var s = DotsSim.RunEpisode(seed, H2Agents, H2Ticks, doctrine, parallel: false)) hSeq = s.StateHash();
                    using (var p = DotsSim.RunEpisode(seed, H2Agents, H2Ticks, doctrine, parallel: true)) hPar = p.StateHash();
                    if (hSeq == hPar) seqParMatch++;
                    else if (firstSeqParMismatch == 0) firstSeqParMismatch = seed;
                }
                Debug.Log($"[H2] burstSeq vs burstPar: {seqParMatch}/{H2Seeds} identical, " +
                          $"{H2Seeds - seqParMatch} divergent ({H2Agents} agents x {H2Ticks} ticks)");
                if (firstSeqParMismatch != 0)
                {
                    Debug.Log($"[H2] first seq/par divergence: seed {firstSeqParMismatch}, " +
                              $"tick {FirstDivergentTick(firstSeqParMismatch, doctrine)}");
                }

                int managedMatch = 0;
                ulong firstManagedMismatch = 0;
                for (int k = 0; k < H2ManagedSeeds; k++)
                {
                    ulong seed = H2Seed0 + (ulong)k;
                    ulong hManaged = Simulation.Run(seed, H2Agents, H2Ticks, doctrine).StateHash();
                    ulong hSeq;
                    using (var s = DotsSim.RunEpisode(seed, H2Agents, H2Ticks, doctrine, parallel: false)) hSeq = s.StateHash();
                    if (hManaged == hSeq) managedMatch++;
                    else if (firstManagedMismatch == 0) firstManagedMismatch = seed;
                }
                Debug.Log($"[H2] managed vs burstSeq: {managedMatch}/{H2ManagedSeeds} identical, " +
                          $"{H2ManagedSeeds - managedMatch} divergent (structural Jacobi-vs-GaussSeidel " +
                          $"difference expected — see pre-reg amendment)");
                if (firstManagedMismatch != 0)
                {
                    Debug.Log($"[H2] first managed/burstSeq divergence: seed {firstManagedMismatch}, " +
                              $"tick {FirstManagedDivergentTick(firstManagedMismatch, doctrine)}");
                }

                Debug.Log("[H2] done");
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[H2] FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        /// <summary>Anomaly probe: repeated fresh-instance episode hashes for one
        /// seed. Distinguishes a real scheduling race (parallel varies against
        /// itself) from a harness bug (stable but unequal).</summary>
        public static void RunProbe()
        {
            try
            {
                var doctrine = Doctrine.Default;
                for (int r = 0; r < 3; r++)
                {
                    ulong hSeq, hPar;
                    int tSeq, tPar;
                    using (var s = DotsSim.RunEpisode(H2Seed0, H2Agents, H2Ticks, doctrine, parallel: false)) { hSeq = s.StateHash(); tSeq = s.Tick; }
                    using (var p = DotsSim.RunEpisode(H2Seed0, H2Agents, H2Ticks, doctrine, parallel: true)) { hPar = p.StateHash(); tPar = p.Tick; }
                    Debug.Log($"[PROBE] rep {r}: seq 0x{hSeq:X16} (end tick {tSeq})  par 0x{hPar:X16} (end tick {tPar})  equal: {hSeq == hPar}");
                }
                if (Application.isBatchMode) EditorApplication.Exit(0);
            }
            catch (Exception e)
            {
                Debug.LogError($"[PROBE] FAILED: {e}");
                if (Application.isBatchMode) EditorApplication.Exit(1);
                throw;
            }
        }

        private static int FirstDivergentTick(ulong seed, Doctrine doctrine)
        {
            using (var a = new DotsSim(H2Agents, seed, doctrine))
            using (var b = new DotsSim(H2Agents, seed, doctrine))
            {
                for (int t = 0; t < H2Ticks; t++)
                {
                    a.Step(parallel: false);
                    b.Step(parallel: true);
                    if (a.StateHash() != b.StateHash()) return t;
                }
            }
            return -1;
        }

        private static int FirstManagedDivergentTick(ulong seed, Doctrine doctrine)
        {
            var sim = new Simulation(H2Agents, seed);
            var state = Simulation.CreateInitialState(H2Agents, seed);
            using (var b = new DotsSim(H2Agents, seed, doctrine))
            {
                for (int t = 0; t < H2Ticks; t++)
                {
                    sim.Tick(state, doctrine, seed);
                    b.Step(parallel: false);
                    if (state.StateHash() != b.StateHash()) return t;
                }
            }
            return -1;
        }
    }
}
