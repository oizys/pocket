# Pockets — build / test / parity entry points.
#
# SHELL-AGNOSTIC BY DESIGN: every recipe is a plain sequence of `dotnet run` calls — no sh-only
# `mkdir -p`/`diff`/`if…fi`/`rm -rf`. Those break under cmd-spawned make on Windows (the "syntax of the
# command is incorrect" failure). Directory creation, EOL-normalized comparison, and cleanup are all
# folded into the journey-runner (`--out` self-creates its dir; `--compare`/`--compare-dirs`/`--clean`),
# so these targets run identically under cmd-make, Git Bash, and Linux sh.
#
# The parity gate (target-demo-build-plan.md, principle 2): one journey script drives BOTH
# frontends and the checkpoint streams must diff clean, with the invariant pack green after
# every step. Two standing gates (build plan's sync policy — both are pre-push):
#
#   make parity        FAST gate — the smoke journey (first ~10 min, core mechanics) on tui +
#                      mock-godot, streams diffed. Run this constantly while developing.
#   make parity-full   FULL gate — runs BOTH journeys on both drivers, then regression-checks the
#                      target demo's committed goldens (VM checkpoint stream + TUI ledger-row
#                      buffers). The pre-push demo gate. (Depends on `parity`, so it covers smoke too.)
#
# Event stream (two-layer Phase 1, Slice 1.7): every run also writes its SimEvent stream (`--events-out`,
# one `<journeyStep>:<k> <step> <seq> <Kind> field=value…` line per event, see tools/journey-runner/README.md)
# and runs the replay gate (`--replay-check`: replay the session's intent log from the initial profile and
# assert the identical VM and event stream at every checkpoint). `parity` diffs the smoke event streams
# cross-driver; `parity-full` does the same for the target demo and regression-checks
# journeys/goldens/target-demo.events.
#
# Pose stream (two-layer Phase 2, Slice 2.4): every run also writes its pose stream (`--poses-out`, one
# `<journeyStep>:<k> u=<q>/4 t=<ms> entity=… bag=… col=… row=… subX=… subY=… height=… facing=…` line per moving
# entity per quarter sample of each commit), and `--replay-check` also compares the replayed pose lines. `parity` diffs
# the smoke pose streams cross-driver; `parity-full` does the same for the target demo and regression-checks
# journeys/goldens/target-demo.poses.
#
# Supporting:
#   make record-goldens  regenerate the committed goldens after an INTENTIONAL journey/state change.
#   make parity-godot    the full journey against a live Godot build (needs a Godot runtime — Aaron's
#                        Windows box; not this WSL env) + the golden screenshot set into artifacts/.

RUNNER  := tools/journey-runner/JourneyRunner.csproj
SMOKE   := journeys/smoke.journey.json
FULL    := journeys/target-demo.journey.json
GOLDENS := journeys/goldens
OUT     := artifacts/parity
SHOTS   := artifacts/godot-screenshots

.PHONY: build test parity parity-full record-goldens parity-godot clean-parity spikes spikes-bench

## Build the whole solution.
build:
	dotnet build Pockets.sln

## Run the shipping test suites (Core + App + Realization).
test:
	dotnet test Pockets.sln

## FAST parity gate: run the smoke journey on the TUI driver (real headless GameView) and the
## mock-transport Godot driver (shared debug-command handler), then diff the checkpoint and event
## streams. Both runs also assert the invariant pack after every step and the replay gate at the end.
## Fails if either run fails or the streams diverge.
parity:
	dotnet run --project $(RUNNER) -- --driver tui        --script $(SMOKE) --out $(OUT)/smoke.tui.checkpoints        --events-out $(OUT)/smoke.tui.events        --poses-out $(OUT)/smoke.tui.poses        --replay-check
	dotnet run --project $(RUNNER) -- --driver mock-godot --script $(SMOKE) --out $(OUT)/smoke.mock-godot.checkpoints --events-out $(OUT)/smoke.mock-godot.events --poses-out $(OUT)/smoke.mock-godot.poses --replay-check
	dotnet run --project $(RUNNER) -- --compare $(OUT)/smoke.tui.checkpoints $(OUT)/smoke.mock-godot.checkpoints --label "smoke cross-driver"
	dotnet run --project $(RUNNER) -- --compare $(OUT)/smoke.tui.events      $(OUT)/smoke.mock-godot.events      --label "smoke cross-driver events"
	dotnet run --project $(RUNNER) -- --compare $(OUT)/smoke.tui.poses       $(OUT)/smoke.mock-godot.poses       --label "smoke cross-driver poses"

## FULL demo gate: the complete 30-minute target-demo journey on tui + mock-godot, cross-driver
## diffed (checkpoints and events), then regression-checked against the committed goldens — the VM
## checkpoint stream (journeys/goldens/target-demo.checkpoints), the event stream
## (journeys/goldens/target-demo.events), the pose stream (journeys/goldens/target-demo.poses) and the TUI ledger-row buffers
## (journeys/goldens/buffers/*.txt, captured at each progressive-UI materialization). Depends on
## `parity`, so a single `make parity-full` runs BOTH journeys on both drivers. A golden mismatch
## means either a regression or an intentional change — in the latter case run `make record-goldens`.
parity-full: parity
	dotnet run --project $(RUNNER) -- --driver tui        --script $(FULL) --out $(OUT)/target-demo.tui.checkpoints        --events-out $(OUT)/target-demo.tui.events        --poses-out $(OUT)/target-demo.tui.poses        --replay-check --goldens $(OUT)/buffers
	dotnet run --project $(RUNNER) -- --driver mock-godot --script $(FULL) --out $(OUT)/target-demo.mock-godot.checkpoints --events-out $(OUT)/target-demo.mock-godot.events --poses-out $(OUT)/target-demo.mock-godot.poses --replay-check
	dotnet run --project $(RUNNER) -- --compare      $(OUT)/target-demo.tui.checkpoints $(OUT)/target-demo.mock-godot.checkpoints --label "target-demo cross-driver"
	dotnet run --project $(RUNNER) -- --compare      $(OUT)/target-demo.tui.events      $(OUT)/target-demo.mock-godot.events      --label "target-demo cross-driver events"
	dotnet run --project $(RUNNER) -- --compare      $(OUT)/target-demo.tui.poses       $(OUT)/target-demo.mock-godot.poses       --label "target-demo cross-driver poses"
	dotnet run --project $(RUNNER) -- --compare      $(GOLDENS)/target-demo.checkpoints $(OUT)/target-demo.tui.checkpoints          --label "golden: VM checkpoint stream"
	dotnet run --project $(RUNNER) -- --compare      $(GOLDENS)/target-demo.events      $(OUT)/target-demo.tui.events               --label "golden: event stream"
	dotnet run --project $(RUNNER) -- --compare      $(GOLDENS)/target-demo.poses       $(OUT)/target-demo.tui.poses                --label "golden: pose stream"
	dotnet run --project $(RUNNER) -- --compare-dirs $(GOLDENS)/buffers                 $(OUT)/buffers                                --label "golden: TUI ledger-row buffers"
	@echo "PARITY-FULL OK — target demo green on both drivers, goldens clean"

## Regenerate the committed goldens from the target-demo journey (TUI driver). The VM checkpoint
## stream is driver-independent (byte-identical across drivers by construction), so recording it
## from tui is canonical (so are the event and pose streams, diffed cross-driver by `parity-full`); the buffer
## goldens are TUI-only render surfaces. Run this ONLY after an
## intentional journey/state change, then commit journeys/goldens/.
record-goldens:
	dotnet run --project $(RUNNER) -- --driver tui --script $(FULL) --out $(GOLDENS)/target-demo.checkpoints --events-out $(GOLDENS)/target-demo.events --poses-out $(GOLDENS)/target-demo.poses --goldens $(GOLDENS)/buffers
	@echo "Recorded goldens → $(GOLDENS)/ (VM stream + event stream + pose stream + TUI ledger-row buffers). Review the diff, then commit."

## Real-Godot pass, EXTENDED TO THE FULL JOURNEY (Slice 8). Requires a running Godot build exposing
## the debug WebSocket server on port 9080 (a machine with a Godot .NET runtime — NOT this WSL env;
## see design/parity-drift-report.md). Start Godot FRESH (no input before the run): bag names in the event
## stream (`bag#N`) count from the controller's first event. Diffs the live Godot checkpoint, event and pose
## streams against the TUI baseline and the event and pose streams against the goldens (the replay gate needs an
## in-process session, so it runs on the tui leg only) and, at
## each golden-flagged ledger row, saves a viewport screenshot into $(SHOTS)/ — the Godot render
## golden set for a vision-model pass. The screenshot capture is scripted here but only runs where a
## Godot viewport exists; it never blocks the in-process gates above.
parity-godot:
	dotnet run --project $(RUNNER) -- --driver tui   --script $(FULL) --out $(OUT)/target-demo.tui.checkpoints   --events-out $(OUT)/target-demo.tui.events   --poses-out $(OUT)/target-demo.tui.poses   --replay-check
	dotnet run --project $(RUNNER) -- --driver godot --script $(FULL) --out $(OUT)/target-demo.godot.checkpoints --events-out $(OUT)/target-demo.godot.events --poses-out $(OUT)/target-demo.godot.poses --url ws://localhost:9080 --screenshots $(SHOTS)
	dotnet run --project $(RUNNER) -- --compare $(OUT)/target-demo.tui.checkpoints $(OUT)/target-demo.godot.checkpoints --label "tui vs live-godot"
	dotnet run --project $(RUNNER) -- --compare $(OUT)/target-demo.tui.events      $(OUT)/target-demo.godot.events      --label "tui vs live-godot events"
	dotnet run --project $(RUNNER) -- --compare $(OUT)/target-demo.tui.poses       $(OUT)/target-demo.godot.poses       --label "tui vs live-godot poses"
	dotnet run --project $(RUNNER) -- --compare $(GOLDENS)/target-demo.events      $(OUT)/target-demo.godot.events      --label "golden: event stream (live-godot)"
	dotnet run --project $(RUNNER) -- --compare $(GOLDENS)/target-demo.poses       $(OUT)/target-demo.godot.poses       --label "golden: pose stream (live-godot)"
	@echo "Godot ledger-row screenshots → $(SHOTS)/"

clean-parity:
	dotnet run --project $(RUNNER) -- --clean $(OUT) $(SHOTS)

## Phase 0 architecture spikes (design/two-layer.md #23) — a separate solution, never part of the game build.
spikes:
	dotnet test spikes/Spikes.sln

## Spike B benchmark (Release, Stopwatch harness; ~5 min full, add ARGS=--quick for a fast pass).
spikes-bench:
	dotnet run -c Release --project spikes/Pockets.Spikes.Bench -- $(ARGS)
