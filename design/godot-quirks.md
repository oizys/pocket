# Godot Quirks (WSL2 / Development)

Known issues and workarounds encountered while running the Godot app target.

---

## C# script class not found on launch

**Error:** `Cannot instantiate C# script because the associated class could not be found. Script: 'res://Scripts/GameSceneController.cs'`

**Cause:** The .NET assembly (`Pockets.Godot.dll`) hasn't been built. Godot can't resolve C# classes without a compiled DLL. Build artifacts live at `.godot/mono/temp/bin/Debug/` and are gitignored.

**Fix:** Run `dotnet build src/Pockets.Godot/Pockets.Godot.csproj` before launching Godot. Consider updating `run-godot.sh` to build automatically.

---

## PulseAudio server info is null

**Error:** `PulseAudio server info is null.`

**Cause:** WSL2 has no PulseAudio server running — no sound hardware is exposed. Godot tries to initialize audio and fails.

**Impact:** None. Godot falls back gracefully and runs without audio. Purely cosmetic log noise.

**Suppress (optional):** Launch with `--audio-driver Dummy` or set `audio/driver/driver = "Dummy"` in Godot project settings.
