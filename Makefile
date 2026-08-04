# Pockets — build / test / parity entry points.
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

.PHONY: build test parity parity-full record-goldens parity-godot clean-parity

## Build the whole solution.
build:
	dotnet build Pockets.sln

## Run the shipping test suites (Core + App).
test:
	dotnet test Pockets.sln

## FAST parity gate: run the smoke journey on the TUI driver (real headless GameView) and the
## mock-transport Godot driver (shared debug-command handler), then diff the checkpoint streams.
## Both runs also assert the invariant pack after every step. Fails if either run fails or the
## streams diverge.
parity:
	@mkdir -p $(OUT)
	dotnet run --project $(RUNNER) -- --driver tui        --script $(SMOKE) --out $(OUT)/smoke.tui.checkpoints
	dotnet run --project $(RUNNER) -- --driver mock-godot --script $(SMOKE) --out $(OUT)/smoke.mock-godot.checkpoints
	@if diff -u $(OUT)/smoke.tui.checkpoints $(OUT)/smoke.mock-godot.checkpoints; then \
		echo "PARITY OK — smoke tui/mock-godot checkpoint streams diff-clean"; \
	else \
		echo "PARITY FAIL — smoke checkpoint streams diverge"; exit 1; \
	fi

## FULL demo gate: the complete 30-minute target-demo journey on tui + mock-godot, cross-driver
## diffed, then regression-checked against the committed goldens — the VM checkpoint stream
## (journeys/goldens/target-demo.checkpoints) and the TUI ledger-row buffers
## (journeys/goldens/buffers/*.txt, captured at each progressive-UI materialization). Depends on
## `parity`, so a single `make parity-full` runs BOTH journeys on both drivers. A golden mismatch
## means either a regression or an intentional change — in the latter case run `make record-goldens`.
parity-full: parity
	@mkdir -p $(OUT)/buffers
	dotnet run --project $(RUNNER) -- --driver tui        --script $(FULL) --out $(OUT)/target-demo.tui.checkpoints --goldens $(OUT)/buffers
	dotnet run --project $(RUNNER) -- --driver mock-godot --script $(FULL) --out $(OUT)/target-demo.mock-godot.checkpoints
	@echo "--- cross-driver parity ---"
	@if diff -u $(OUT)/target-demo.tui.checkpoints $(OUT)/target-demo.mock-godot.checkpoints; then \
		echo "PARITY OK — target-demo tui/mock-godot checkpoint streams diff-clean"; \
	else \
		echo "PARITY FAIL — target-demo checkpoint streams diverge"; exit 1; \
	fi
	@echo "--- golden regression: VM checkpoint stream ---"
	@if diff -u $(GOLDENS)/target-demo.checkpoints $(OUT)/target-demo.tui.checkpoints; then \
		echo "GOLDEN OK — VM checkpoint stream matches the committed golden"; \
	else \
		echo "GOLDEN FAIL — VM stream drifted from golden (run 'make record-goldens' if intended)"; exit 1; \
	fi
	@echo "--- golden regression: TUI ledger-row buffers ---"
	@if diff -ru $(GOLDENS)/buffers $(OUT)/buffers; then \
		echo "GOLDEN OK — TUI ledger-row buffers match the committed goldens"; \
	else \
		echo "GOLDEN FAIL — TUI buffers drifted from goldens (run 'make record-goldens' if intended)"; exit 1; \
	fi
	@echo "PARITY-FULL OK — target demo green on both drivers, goldens clean"

## Regenerate the committed goldens from the target-demo journey (TUI driver). The VM checkpoint
## stream is driver-independent (byte-identical across drivers by construction), so recording it
## from tui is canonical; the buffer goldens are TUI-only render surfaces. Run this ONLY after an
## intentional journey/state change, then commit journeys/goldens/.
record-goldens:
	@mkdir -p $(GOLDENS)/buffers
	dotnet run --project $(RUNNER) -- --driver tui --script $(FULL) --out $(GOLDENS)/target-demo.checkpoints --goldens $(GOLDENS)/buffers
	@echo "Recorded goldens → $(GOLDENS)/ (VM stream + TUI ledger-row buffers). Review the diff, then commit."

## Real-Godot pass, EXTENDED TO THE FULL JOURNEY (Slice 8). Requires a running Godot build exposing
## the debug WebSocket server on port 9080 (a machine with a Godot .NET runtime — NOT this WSL env;
## see design/parity-drift-report.md). Diffs the live Godot stream against the TUI baseline and, at
## each golden-flagged ledger row, saves a viewport screenshot into $(SHOTS)/ — the Godot render
## golden set for a vision-model pass. The screenshot capture is scripted here but only runs where a
## Godot viewport exists; it never blocks the in-process gates above.
parity-godot:
	@mkdir -p $(OUT) $(SHOTS)
	dotnet run --project $(RUNNER) -- --driver tui   --script $(FULL) --out $(OUT)/target-demo.tui.checkpoints
	dotnet run --project $(RUNNER) -- --driver godot --script $(FULL) --out $(OUT)/target-demo.godot.checkpoints --url ws://localhost:9080 --screenshots $(SHOTS)
	@if diff -u $(OUT)/target-demo.tui.checkpoints $(OUT)/target-demo.godot.checkpoints; then \
		echo "PARITY OK — tui and godot checkpoint streams diff-clean"; \
	else \
		echo "PARITY FAIL — checkpoint streams diverge"; exit 1; \
	fi
	@echo "Godot ledger-row screenshots → $(SHOTS)/"

clean-parity:
	rm -rf $(OUT) $(SHOTS)
