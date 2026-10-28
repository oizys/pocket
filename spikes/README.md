# spikes/ — Phase 0 architecture spikes (design #23)

Throwaway-but-rigorous experiments backing [`design/two-layer.md`](../design/two-layer.md).
**Not part of the game:** this is its own solution (`spikes/Spikes.sln`). It is not referenced by
`Pockets.sln`, `Pockets.Core`, the frontends, the journeys or the parity goldens. Nothing
here may be imported by game code. Promote ideas by re-implementing them under TDD in `Pockets.Core`.

| Project | What | Spike |
|---|---|---|
| `Pockets.Spikes/Belts` | belt grid, two-layer ASCII parse/render, `NaiveStep` (Rule 184) vs `ChainStep` (head→tail resolve, fixed side-priority merges (stateless; full loops stall)) | A |
| `Pockets.Spikes/Belts/MergeCatchUp.cs` | merge networks N1–N3 × feed configs: sub-step, big step, **cycle skip**, main-only, fluid; seeded sweep | C |
| `Pockets.Spikes/Plaque` | plaque P1 accumulator / P2 spread, big-step vs sub-step vs catch-up, linear-belt catch-up recurrence, lint RNG | C |
| `Pockets.Spikes.Tests` | xunit: ASCII traces (A), catch-up ≡ stepped property tests (C), report table | A, C |
| `Pockets.Spikes.Bench` | Stopwatch harness: immutable-builder vs `SetItem` vs SoA hot set + event log + snapshots | B |
| `results/` | captured outputs quoted in the design doc | B, C |

```bash
dotnet test spikes/Spikes.sln                                                          # A + C (make spikes)
dotnet test spikes/Spikes.sln --filter ReportTests --logger "console;verbosity=detailed" # C table
dotnet run -c Release --project spikes/Pockets.Spikes.Bench [-- --quick|--no-paced]     # B (make spikes-bench)
```
