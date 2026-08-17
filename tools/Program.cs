using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using Hellfire.Sim;

namespace Hellfire.Tools
{
    /// <summary>
    /// Headless harness. Exit code 0 = pass.
    ///   gate  [--runs 500] [--agents 512] [--ticks 1000] [--budget-s 60]   (default mode)
    ///   score (--preset name | --doctrine file.json) [--seeds 100] [--agents 96] [--ticks 1800]
    ///   grid  [--seeds 100]      — all presets + pairwise effect sizes (step-2 gate evidence)
    /// </summary>
    public static class Program
    {
        public static int Main(string[] args)
        {
            string mode = "gate";
            int argStart = 0;
            if (args.Length > 0 && !args[0].StartsWith("--")) { mode = args[0]; argStart = 1; }
            var opts = ParseOpts(args, argStart);
            switch (mode)
            {
                case "gate": return Gate(opts);
                case "score": return ScoreOne(opts);
                case "grid": return Grid(opts);
                default:
                    Console.Error.WriteLine($"unknown mode '{mode}'");
                    return 2;
            }
        }

        private static System.Collections.Generic.Dictionary<string, string> ParseOpts(string[] args, int start)
        {
            var d = new System.Collections.Generic.Dictionary<string, string>();
            for (int i = start; i + 1 < args.Length; i += 2) d[args[i]] = args[i + 1];
            return d;
        }

        private static int GetInt(System.Collections.Generic.Dictionary<string, string> o, string k, int dflt)
            => o.TryGetValue(k, out var v) ? int.Parse(v) : dflt;

        private static int Gate(System.Collections.Generic.Dictionary<string, string> o)
        {
            int runs = GetInt(o, "--runs", 500);
            int agents = GetInt(o, "--agents", 512);
            int ticks = GetInt(o, "--ticks", 1000);
            double budget = o.TryGetValue("--budget-s", out var b) ? double.Parse(b) : 60.0;
            var doctrine = Doctrine.Default;

            ulong hashA = Simulation.Run(42UL, agents, ticks, doctrine).StateHash();
            ulong hashB = Simulation.Run(42UL, agents, ticks, doctrine).StateHash();
            bool gate1 = hashA == hashB;
            Console.WriteLine($"gate1 determinism: run A 0x{hashA:X16}  run B 0x{hashB:X16}  -> {(gate1 ? "PASS" : "FAIL")}");

            var sw = Stopwatch.StartNew();
            ulong xor = 0;
            for (int r = 0; r < runs; r++)
            {
                xor ^= Simulation.Run((ulong)(1000 + r), agents, ticks, doctrine).StateHash();
            }
            sw.Stop();
            double elapsed = sw.Elapsed.TotalSeconds;
            bool gate2 = elapsed < budget;
            Console.WriteLine($"gate2 perf: {runs} runs x {agents} agents x {ticks} ticks = " +
                              $"{elapsed:F2}s (budget {budget:F0}s, {elapsed * 1000.0 / runs:F1} ms/run, " +
                              $"hash-xor 0x{xor:X16}) -> {(gate2 ? "PASS" : "FAIL")}");
            return gate1 && gate2 ? 0 : 1;
        }

        private static readonly JsonSerializerOptions JsonOpts = new JsonSerializerOptions
        {
            WriteIndented = true,
        };

        private static int ScoreOne(System.Collections.Generic.Dictionary<string, string> o)
        {
            Doctrine doctrine;
            string name;
            if (o.TryGetValue("--doctrine", out var path))
            {
                doctrine = JsonSerializer.Deserialize<Doctrine>(File.ReadAllText(path))
                           ?? throw new InvalidDataException($"empty doctrine file {path}");
                name = Path.GetFileNameWithoutExtension(path);
            }
            else
            {
                name = o.TryGetValue("--preset", out var p) ? p : "default";
                doctrine = Doctrine.Preset(name);
            }
            int seeds = GetInt(o, "--seeds", 100);
            int agents = GetInt(o, "--agents", Scorer.DefaultAgents);
            int ticks = GetInt(o, "--ticks", Scorer.DefaultMaxTicks);

            var sw = Stopwatch.StartNew();
            var report = Scorer.Score(doctrine, name, 5000UL, seeds, agents, ticks);
            sw.Stop();
            // Per-seed detail stays out of stdout by default — means + noise floor only.
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                report.Name, report.Agents, report.SeedCount,
                report.MeanSurvival, report.MeanCompletion, report.MeanComposite,
                report.StdComposite, report.StdErrComposite,
                WallSeconds = Math.Round(sw.Elapsed.TotalSeconds, 2),
            }, JsonOpts));
            return 0;
        }

        private static int Grid(System.Collections.Generic.Dictionary<string, string> o)
        {
            int seeds = GetInt(o, "--seeds", 100);
            var sw = Stopwatch.StartNew();
            var reports = new FitnessReport[Doctrine.PresetNames.Length];
            for (int i = 0; i < Doctrine.PresetNames.Length; i++)
            {
                string name = Doctrine.PresetNames[i];
                reports[i] = Scorer.Score(Doctrine.Preset(name), name, 5000UL, seeds);
                var r = reports[i];
                Console.WriteLine($"{name,-14} composite {r.MeanComposite:F3} ± {r.StdComposite:F3} " +
                                  $"(sem {r.StdErrComposite:F4})  survival {r.MeanSurvival:F3}  " +
                                  $"completion {r.MeanCompletion:F3}");
            }
            sw.Stop();

            Console.WriteLine();
            float minInteresting = float.MaxValue;
            for (int i = 0; i < reports.Length; i++)
            {
                for (int j = i + 1; j < reports.Length; j++)
                {
                    float d = Scorer.EffectSize(reports[i], reports[j]);
                    Console.WriteLine($"effect {reports[i].Name} vs {reports[j].Name}: d = {d:F2}");
                    if (d < minInteresting) minInteresting = d;
                }
            }
            Console.WriteLine($"\ngrid wall-clock: {sw.Elapsed.TotalSeconds:F2}s for {reports.Length} doctrines x {seeds} seeds");
            return 0;
        }
    }
}
