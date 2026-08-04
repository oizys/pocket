using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.JourneyRunner;

// journey-runner — drives one journey script against a pluggable frontend driver and emits a
// diffable view-model checkpoint stream + invariant results. See design/parity-drift-report.md
// and the repo Makefile's `parity` target.
//
// Usage:
//   journey-runner --driver tui|godot|mock-godot --script <file> [--out <file>]
//                  [--data <dir>] [--seed <int>] [--url ws://host:port] [--dump]

var opts = ParseArgs(args);
if (opts is null) { PrintUsage(); return 2; }

var (driverName, scriptPath, outPath, dataDir, seed, url, dump) = opts.Value;

var script = JourneyScript.Load(scriptPath);

// The in-process drivers (tui, mock-godot) load the shared demo profile so they begin in the
// exact state the real frontends do. The godot driver observes a running Godot build that loads
// the same profile itself.
DemoProfile? profile = null;
if (driverName is "tui" or "mock-godot")
{
    var registry = ContentLoader.LoadFromDirectory(dataDir);
    var dialogue = DialogueLoader.LoadFromDirectory(dataDir);
    profile = GameInitializer.CreateDemoProfile(registry, seed, dialogue);
}

using IJourneyDriver driver = driverName switch
{
    "tui" => new TuiDriver(profile!),
    "mock-godot" => new MockGodotDriver(new GameController(profile!.NewSession())),
    "godot" => new GodotWebSocketDriver(url),
    _ => throw new ArgumentException($"Unknown driver '{driverName}'")
};

Console.Error.WriteLine($"[journey-runner] driver={driver.Name} script={script.Name} steps={script.Steps.Count}");

var result = JourneyRunnerCore.Run(driver, script);

if (outPath is not null)
{
    File.WriteAllLines(outPath, result.Checkpoints);
    Console.Error.WriteLine($"[journey-runner] wrote {result.Checkpoints.Count} checkpoints → {outPath}");
}

if (dump)
    foreach (var line in result.Checkpoints)
        Console.WriteLine(line);

foreach (var note in result.Notes)
    Console.Error.WriteLine($"  note: {note}");

if (result.Passed)
{
    Console.Error.WriteLine($"[journey-runner] PASS ({result.Checkpoints.Count} checkpoints, invariants green)");
    return 0;
}

Console.Error.WriteLine($"[journey-runner] FAIL ({result.Failures.Count} failure(s)):");
foreach (var f in result.Failures)
    Console.Error.WriteLine($"  ✗ {f}");
return 1;

// --- arg parsing ---

static (string driver, string script, string? outPath, string dataDir, int? seed, string url, bool dump)?
    ParseArgs(string[] args)
{
    string? driver = null, script = null, outPath = null, dataDir = null, url = "ws://localhost:9080";
    int? seed = null;
    bool dump = false;

    for (int i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--driver": driver = Next(args, ref i); break;
            case "--script": script = Next(args, ref i); break;
            case "--out": outPath = Next(args, ref i); break;
            case "--data": dataDir = Next(args, ref i); break;
            case "--seed": seed = int.Parse(Next(args, ref i)); break;
            case "--url": url = Next(args, ref i); break;
            case "--dump": dump = true; break;
            default:
                Console.Error.WriteLine($"Unknown argument: {args[i]}");
                return null;
        }
    }

    if (driver is null || script is null) return null;
    dataDir ??= DefaultDataDir();
    return (driver, script, outPath, dataDir, seed, url, dump);
}

static string Next(string[] args, ref int i)
{
    if (i + 1 >= args.Length) throw new ArgumentException($"Missing value after {args[i]}");
    return args[++i];
}

static string DefaultDataDir()
{
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pockets.sln")))
        dir = dir.Parent;
    if (dir is null) throw new DirectoryNotFoundException("Could not locate Pockets.sln to find data/. Pass --data.");
    return Path.Combine(dir.FullName, "data");
}

static void PrintUsage() => Console.Error.WriteLine(
    "Usage: journey-runner --driver tui|godot|mock-godot --script <file> [--out <file>]\n" +
    "                      [--data <dir>] [--seed <int>] [--url ws://host:port] [--dump]");
