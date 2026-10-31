using System.Text.Json.Nodes;
using Pockets.Core.Models;
using Pockets.JourneyRunner;

namespace Pockets.JourneyRunner.Tests;

/// <summary>
/// The screenshot path the runner hands a screenshot-capable driver. Godot resolves a relative path against
/// its project dir (res:// = src/Pockets.Godot), not the runner's cwd, so the runner must send a rooted path
/// inside the dir it created (the Windows `make parity-godot` FileNotFound failure).
/// </summary>
public class ScreenshotPathTests
{
    /// <summary>A minimal screenshot-capable driver: no game, an empty view-model, and a log of requested paths.</summary>
    private sealed class RecordingScreenshotDriver : IJourneyDriver, IScreenshotDriver
    {
        public List<string> Paths { get; } = new();
        public string Name => "recording";
        public DriverObservation Observe() => new(new JsonObject(), FullState: null, RenderProbe: null);
        public void Key(GameKey key) { }
        public void Tick() { }
        public void AdvanceTime(TimeSpan delta) { }
        public void Back() { }
        public void Click(Position pos, ClickType type) { }
        public void Steer(LocationId at, Direction8? dir) { }
        public void SetMovement(LocationId at, MovementModel model) { }
        public void Screenshot(string path) => Paths.Add(path);
        public void Dispose() { }
    }

    /// <summary>Runs a one-step golden journey with a relative screenshot dir from the given cwd.</summary>
    private static (RecordingScreenshotDriver Driver, string ExpectedDir) RunGolden(string relativeDir)
    {
        var driver = new RecordingScreenshotDriver();
        var script = new JourneyScript("shot", null, new[] { new JourneyStep { Checkpoint = "frame-0", Golden = true } });
        JourneyRunnerCore.Run(driver, script, relativeDir);
        return (driver, Path.GetFullPath(relativeDir));
    }

    [Fact]
    public void RelativeScreenshotDir_DriverReceivesRootedPath()
    {
        var rel = Path.Combine("artifacts", "test-screenshots", Guid.NewGuid().ToString("N"));
        var (driver, _) = RunGolden(rel);

        var path = Assert.Single(driver.Paths);
        Assert.True(Path.IsPathRooted(path), $"expected a rooted path, got '{path}'");
    }

    [Fact]
    public void RelativeScreenshotDir_PathIsInsideTheCreatedDir()
    {
        var rel = Path.Combine("artifacts", "test-screenshots", Guid.NewGuid().ToString("N"));
        var (driver, expectedDir) = RunGolden(rel);

        var path = Assert.Single(driver.Paths);
        Assert.Equal(Path.Combine(expectedDir, "frame-0.png"), path);
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)), "the runner creates the screenshot dir it sends");
    }
}
