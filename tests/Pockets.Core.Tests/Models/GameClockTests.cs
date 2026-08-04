using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests.Models;

/// <summary>
/// Slice-5 injectable clock: the virtual clock is fully scripted (parity determinism), the system
/// clock is wall-driven, and the controller syncs the injected reading onto the session.
/// </summary>
public class GameClockTests
{
    [Fact]
    public void VirtualClock_StartsAtZero_AdvancesByDelta()
    {
        var clock = new VirtualGameClock();
        Assert.Equal(TimeSpan.Zero, clock.Elapsed);
        clock.Advance(TimeSpan.FromSeconds(5));
        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(15), clock.Elapsed);
    }

    [Fact]
    public void VirtualClock_IgnoresNonPositiveDelta_TimeIsMonotonic()
    {
        var clock = new VirtualGameClock(TimeSpan.FromSeconds(3));
        clock.Advance(TimeSpan.FromSeconds(-2));
        clock.Advance(TimeSpan.Zero);
        Assert.Equal(TimeSpan.FromSeconds(3), clock.Elapsed);
    }

    [Fact]
    public void SystemClock_AdvanceIsNoOp_WallDriven()
    {
        var clock = new SystemGameClock();
        var before = clock.Elapsed;
        clock.Advance(TimeSpan.FromHours(1)); // scripted advance does not apply to a wall clock
        Assert.True(clock.Elapsed < TimeSpan.FromMinutes(1));
        Assert.True(clock.Elapsed >= before);
    }

    // ==================== Controller integration ====================

    private static GameController FreshController()
    {
        var types = ImmutableArray.Create(new ItemType("Rock", Category.Material, IsStackable: true));
        var state = GameState.CreateStage1(types, new[] { new ItemStack(types[0], 5) });
        return new GameController(GameSession.New(state));
    }

    [Fact]
    public void Controller_DefaultsToVirtualClock_AdvanceSyncsOntoSession()
    {
        var controller = FreshController();
        Assert.IsType<VirtualGameClock>(controller.Clock);
        Assert.Equal(TimeSpan.Zero, controller.Session.Elapsed);

        controller.AdvanceClock(TimeSpan.FromSeconds(15));
        Assert.Equal(TimeSpan.FromSeconds(15), controller.Session.Elapsed);
    }

    [Fact]
    public void Controller_AdvanceClock_IsCumulative()
    {
        var controller = FreshController();
        controller.AdvanceClock(TimeSpan.FromSeconds(5));
        controller.AdvanceClock(TimeSpan.FromSeconds(10));
        Assert.Equal(TimeSpan.FromSeconds(15), controller.Session.Elapsed);
    }
}
