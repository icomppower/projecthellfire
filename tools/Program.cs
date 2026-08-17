using System;
using System.Diagnostics;
using Hellfire.Sim;

namespace Hellfire.Tools
{
    /// <summary>
    /// Headless gate harness (step-1 gates 1 and 2). Exit code 0 = gates pass.
    ///   dotnet run -c Release --project tools -- [--runs 500] [--agents 512] [--ticks 1000] [--budget-s 60]
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            int runs = 500, agents = 512, ticks = 1000;
            double budgetSeconds = 60.0;
            for (int i = 0; i + 1 < args.Length; i += 2)
            {
                switch (args[i])
                {
                    case "--runs": runs = int.Parse(args[i + 1]); break;
                    case "--agents": agents = int.Parse(args[i + 1]); break;
                    case "--ticks": ticks = int.Parse(args[i + 1]); break;
                    case "--budget-s": budgetSeconds = double.Parse(args[i + 1]); break;
                    default:
                        Console.Error.WriteLine($"unknown arg {args[i]}");
                        return 2;
                }
            }

            var doctrine = Doctrine.Default;

            // GATE 1 — two runs, same seed, byte-identical final state hash.
            ulong hashA = Simulation.Run(42UL, agents, ticks, in doctrine).StateHash();
            ulong hashB = Simulation.Run(42UL, agents, ticks, in doctrine).StateHash();
            bool gate1 = hashA == hashB;
            Console.WriteLine($"gate1 determinism: run A 0x{hashA:X16}  run B 0x{hashB:X16}  -> {(gate1 ? "PASS" : "FAIL")}");

            // GATE 2 — 500 seeded runs under the wall-clock budget.
            var sw = Stopwatch.StartNew();
            ulong xor = 0;
            for (int r = 0; r < runs; r++)
            {
                xor ^= Simulation.Run((ulong)(1000 + r), agents, ticks, in doctrine).StateHash();
            }
            sw.Stop();
            double elapsed = sw.Elapsed.TotalSeconds;
            bool gate2 = elapsed < budgetSeconds;
            Console.WriteLine($"gate2 perf: {runs} runs x {agents} agents x {ticks} ticks = " +
                              $"{elapsed:F2}s (budget {budgetSeconds:F0}s, {elapsed * 1000.0 / runs:F1} ms/run, " +
                              $"hash-xor 0x{xor:X16}) -> {(gate2 ? "PASS" : "FAIL")}");

            return gate1 && gate2 ? 0 : 1;
        }
    }
}
