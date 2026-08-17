# Gate Criteria (local mirror — Notion is source of truth)

GDD: Notion `3bf1f269-eaea-81f0-980f-ce53df155597` · Pre-registration: `3bf1f269-eaea-81c8-844c-da945db9453a` · Experiment log: `3bf1f269-eaea-816b-b6e1-e736ebc9e6f4`

## Step 1 — determinism spine (this repo state)

1. Two runs, same seed → byte-identical final state hash.
2. 500 seeded runs complete in under 60 s.
3. Spatial hash: 0 dropped occupants vs brute force over 300 dense queries.
4. `dotnet test` green, 0 warnings.

Run locally: `dotnet test -c Release` then `dotnet run -c Release --project tools`.

## Step 3 — emergence kill gate

Change one doctrine value, run 500 seeds. The outcome delta must surprise *and* explain itself. Unreadable emergence kills the project.

## Convergence exits (never improvise new ones)

Target met · plateau <0.15 ×2 · regression ×2 · oscillation ×3 · hard budget cap.
