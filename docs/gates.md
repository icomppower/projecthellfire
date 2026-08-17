# Gate Criteria (local mirror — Notion is source of truth)

GDD: Notion `3bf1f269-eaea-81f0-980f-ce53df155597` · Pre-registration: `3bf1f269-eaea-81c8-844c-da945db9453a` · Experiment log: `3bf1f269-eaea-816b-b6e1-e736ebc9e6f4`

## Step 1 — determinism spine (this repo state)

1. Two runs, same seed → byte-identical final state hash.
2. 500 seeded runs complete in under 60 s.
3. Spatial hash: 0 dropped occupants vs brute force over 300 dense queries.
4. `dotnet test` green, 0 warnings.

Run locally: `dotnet test -c Release` then `dotnet run -c Release --project tools`.

## Step 2 — doctrine schema + headless scorer (gate self-defined; GDD names none)

1. Fitness determinism: same doctrine + seed range → identical report, per-seed.
2. Discrimination: contrasting doctrine presets separate above seed noise (Cohen's d > 1.0 for at least two pairs of the preset grid; measured d up to 2.47 at 100 seeds).
3. Directional probes: risk↑ → survival↓; early abort → survival↑ & completion↓; silent comms → survival↑; centralized → survival↑ (pre-jamming).
4. Non-degenerate outcomes: default doctrine lands strictly inside (0.05, 0.98) on both metrics.
5. Batch perf: 7 doctrines × 100 seeds under 60 s (measured 6.9 s).
6. `dotnet test` green, 0 warnings.

Scorer: `dotnet run -c Release --project tools -- grid --seeds 100` or `score --preset <name>` / `score --doctrine file.json`.

## Step 3 — emergence kill gate — PASSED 2026-08-16 (with logged caveats)

Protocol: change one doctrine value, run 500 seeds (`tools -- emergence --seeds 500`). The outcome delta must surprise *and* explain itself. Unreadable emergence kills the project.

Result: gate does not fire — every single-axis delta is diagnosed by the death-cause
attribution layer (§2 contract: `Scorer.Diagnose`, never empty on a lossy run).
Highlights: risk 0.5→0.65 triggers an abort cascade (survival −6.4 pts, aborts
44%→67%); early abort is composite-neutral (d=0.04) while swinging survival +7.4
vs completion −4.4; comms is the strongest axis (d=0.91 silent, d=0.73 chatty).

Caveats (logged, not tuned — fix rounds were spent):
- Autonomy dial: live under saturated EW (survival t>2 at 6 jammers; §1 inversion
  holds — jamming makes centralization *worse* than never networking, via the
  dependency penalty), but ~inert (d=0.09) in the default seeded 0–4 jammer
  environment. Environment-distribution decision deferred to step 6.
- Cohesion axis: measured inert (t=1.13 at 500 seeds, flat in N) — negative
  result; no mutual-support mechanism exists to weigh against herd risk. Wired
  but not player-offered.

## Step 4 — presentation layer — PASSED 2026-08-17, zero fix rounds

Runner: `tools/run-unity-gates.sh`. All gates green on the first licensed run:
compile exit 0 / 0 errors; §3 parity byte-identical; bootstrap generates the
scene headlessly; dotnet 24/24. H3 first data: warm Unity round ~16 s vs ~11 s
.NET baseline (~1.5–2×; pre-registered prediction was 15–60×, falsifier <5×) —
preliminary, real loop rounds at steps 5–6 decide.

1. Headless compile: batchmode exit 0, zero `error CS`.
2. §3 invariant from inside Unity: EditMode test proves SimDriver's 600-tick state hash is byte-identical to the headless .NET path.
3. Procedural bootstrap generates `Assets/Scenes/Main.unity` + URP assets headlessly (scenes are built, never hand-edited).
4. `dotnet test` still green (sim untouched by Unity packaging).
5. Record per-stage wall-clock — the first H3 Unity-round numbers, against the ~11 s .NET baseline.

Scope deviation, logged: VFX Graph explosions are editor-authored graphs (hand-writing .vfx YAML sits in the same corruption category as .unity edits) — code-configured ParticleSystem pool ships instead; VFX Graph reserved for an editor-in-the-loop session (natural H5 test case).

## Step 5 — DOTS experiment — DONE 2026-08-17, kept contained (Editor-only)

Runner: `tools/run-dots-experiment.sh` (compile → dots tests → H1 → H2).

H1: per-tick speedup managed→Burst-parallel 7.3×–26.1× (512→32k agents);
60 fps ceiling 3,332 → 17,245 agents (5.2× — compressed by the scenario's
superlinear density). H2: Burst seq vs parallel **500/500 byte-identical**
(Jacobi per-agent structure preserves replay — see pre-reg amendment
2026-08-17); managed vs Burst 0/100, divergent from tick 0 (compiler
substrate changes floats immediately → the scored artifact stays on the
managed sim; GDD §3 split vindicated). Burst trap on record: async
compilation runs first episodes on the managed fallback with different
floats — `CompileSynchronously = true` is a determinism requirement.
Decision: presentation keeps the managed sim; DOTS stays a measured
capability, not a foundation.

## Convergence exits (never improvise new ones)

Target met · plateau <0.15 ×2 · regression ×2 · oscillation ×3 · hard budget cap.
