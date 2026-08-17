# Project Hellfire — Unity Drone Swarm (Experiment Vehicle)

Priority order: **A. Test Unity · B. Learn agentic workflow · C. Game result.**

Design docs, pre-registration (H1–H6), and the experiment log live in Notion (source of truth). Local mirror of gate criteria: `docs/gates.md`.

- `sim/` — plain C# sim core, netstandard2.1, **zero UnityEngine references**. Deterministic: fixed timestep, seeded, single-threaded, order-independent hashed RNG, SoA storage.
- `sim.tests/` — NUnit.
- `tools/` — headless gate harness (`dotnet run -c Release --project tools`).
- `docs/` — gate criteria mirror.

No Unity project yet by design — Unity enters at step 4.
