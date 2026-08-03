# Pockets — build / test / parity entry points.
#
# The parity gate (target-demo-build-plan.md, principle 2): one journey script drives BOTH
# frontends and the checkpoint streams must diff clean, with the invariant pack green after
# every step. `make parity` is the standing, CI/worker-runnable demo gate.

RUNNER := tools/journey-runner/JourneyRunner.csproj
SMOKE  := journeys/smoke.journey.json
OUT    := artifacts/parity

.PHONY: build test parity parity-godot clean-parity

## Build the whole solution.
build:
	dotnet build Pockets.sln

## Run the shipping test suites (Core + App).
test:
	dotnet test Pockets.sln

## Parity gate: run the smoke journey on the TUI driver (real headless GameView) and the
## mock-transport Godot driver (shared debug-command handler), then diff the checkpoint
## streams. Both runs also assert the invariant pack after every step. Fails if either run
## fails or the streams diverge.
parity:
	@mkdir -p $(OUT)
	dotnet run --project $(RUNNER) -- --driver tui        --script $(SMOKE) --out $(OUT)/tui.checkpoints
	dotnet run --project $(RUNNER) -- --driver mock-godot --script $(SMOKE) --out $(OUT)/mock-godot.checkpoints
	@if diff -u $(OUT)/tui.checkpoints $(OUT)/mock-godot.checkpoints; then \
		echo "PARITY OK — tui and mock-godot checkpoint streams diff-clean"; \
	else \
		echo "PARITY FAIL — checkpoint streams diverge"; exit 1; \
	fi

## Real-Godot parity pass. Requires a running Godot build exposing the debug WebSocket
## server on port 9080 (a machine with a Godot .NET runtime — NOT available in this WSL env;
## see design/parity-drift-report.md). Diffs the live Godot stream against the TUI baseline.
parity-godot:
	@mkdir -p $(OUT)
	dotnet run --project $(RUNNER) -- --driver tui   --script $(SMOKE) --out $(OUT)/tui.checkpoints
	dotnet run --project $(RUNNER) -- --driver godot --script $(SMOKE) --out $(OUT)/godot.checkpoints --url ws://localhost:9080
	@if diff -u $(OUT)/tui.checkpoints $(OUT)/godot.checkpoints; then \
		echo "PARITY OK — tui and godot checkpoint streams diff-clean"; \
	else \
		echo "PARITY FAIL — checkpoint streams diverge"; exit 1; \
	fi

clean-parity:
	rm -rf $(OUT)
