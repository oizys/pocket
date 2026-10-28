using System;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using Pockets.Core.Models;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Two-layer Phase 1, Slice 1.0: the Step contract types. Covers SimEvent value equality, the
/// canonical (Step, Seq) ordering, the sealed hierarchies, and the enum shapes the rule registry
/// (Slice 1.4) relies on (no zero member, so an undeclared value can never pass as a real one).
/// </summary>
public class SimContractTests
{
    private static readonly Guid Facility = Guid.Parse("11111111-1111-1111-1111-111111111111");

    // ── Equality ──────────────────────────────────────────────────────────────

    [Fact]
    public void SimEvent_SamePayload_AreEqual()
    {
        var a = new SimEvent.BeltAdvanced(3, 0, ChainId: 7, Dir: Direction.Right, Count: 4, HeadCell: 12);
        var b = new SimEvent.BeltAdvanced(3, 0, ChainId: 7, Dir: Direction.Right, Count: 4, HeadCell: 12);

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void SimEvent_DifferentStepOrSeq_AreNotEqual()
    {
        var a = new SimEvent.TimeAdvanced(3, 0);

        Assert.NotEqual(a, a with { Step = 4 });
        Assert.NotEqual(a, a with { Seq = 1 });
    }

    [Fact]
    public void SimEvent_DifferentPayload_AreNotEqual()
    {
        var a = new SimEvent.BeltAdvanced(3, 0, 7, Direction.Right, 4, 12);

        Assert.NotEqual(a, a with { Dir = Direction.Left });
        Assert.NotEqual(a, a with { HeadCell = 13 });
    }

    [Fact]
    public void SimEvent_DifferentKinds_SameStepSeq_AreNotEqual()
    {
        SimEvent applied = new SimEvent.IntentApplied(1, 0, new Intent.Tick());
        SimEvent rejected = new SimEvent.IntentRejected(1, 0, new Intent.Tick(), "x");

        Assert.NotEqual(applied, rejected);
    }

    [Fact]
    public void SimEvent_IntentPayload_ComparesIntentsByValue()
    {
        var a = new SimEvent.IntentRejected(2, 1, new Intent.Tick(), "bag full");
        var b = new SimEvent.IntentRejected(2, 1, new Intent.Tick(), "bag full");

        Assert.Equal(a, b);
        Assert.NotEqual(a, b with { Reason = "type mismatch" });
    }

    // ── Ordering ──────────────────────────────────────────────────────────────

    [Fact]
    public void Order_SortsByStepThenSeq()
    {
        SimEvent[] shuffled =
        {
            new SimEvent.TimeAdvanced(2, 1),
            new SimEvent.CraftCompleted(1, 2, Facility, "compass"),
            new SimEvent.UiTriggerFired(2, 0, UiTrigger.FirstTimedAction),
            new SimEvent.IntentApplied(1, 0, new Intent.Tick()),
            new SimEvent.TimeAdvanced(1, 1),
        };

        var sorted = shuffled.OrderBy(e => e, SimEvent.Order).Select(e => (e.Step, e.Seq)).ToArray();

        Assert.Equal(new[] { (1, 0), (1, 1), (1, 2), (2, 0), (2, 1) }, sorted);
    }

    [Fact]
    public void Order_IgnoresKind_OnlyStepAndSeqDecide()
    {
        SimEvent belt = new SimEvent.BeltAdvanced(5, 3, 0, Direction.Up, 1, 0);
        SimEvent tick = new SimEvent.TimeAdvanced(5, 3);

        Assert.Equal(0, SimEvent.Order.Compare(belt, tick));
        Assert.True(SimEvent.Order.Compare(tick, belt with { Seq = 4 }) < 0);
        Assert.True(SimEvent.Order.Compare(tick with { Step = 6, Seq = 0 }, belt) > 0);
    }

    [Fact]
    public void Order_IsStable_ForEqualKeys()
    {
        // OrderBy is stable, so the declared (Step, Seq) order never reshuffles a valid stream.
        SimEvent[] stream =
        {
            new SimEvent.IntentApplied(1, 0, new Intent.Tick()),
            new SimEvent.TimeAdvanced(1, 1),
            new SimEvent.CraftCompleted(1, 2, Facility, "compass"),
        };

        Assert.Equal(stream, stream.OrderBy(e => e, SimEvent.Order).ToArray());
    }

    // ── StepResult ────────────────────────────────────────────────────────────

    [Fact]
    public void StepResult_EqualStateAndEvents_AreEqual()
    {
        var a = new StepResult<int>(42, ImmutableArray.Create<SimEvent>(new SimEvent.TimeAdvanced(1, 0)));
        var b = new StepResult<int>(42, ImmutableArray.Create<SimEvent>(new SimEvent.TimeAdvanced(1, 0)));

        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, b with { Events = ImmutableArray<SimEvent>.Empty });
        Assert.NotEqual(a, b with { State = 43 });
    }

    [Fact]
    public void StepResult_Unchanged_HasNoEvents()
    {
        var r = StepResult<int>.Unchanged(7);

        Assert.Equal(7, r.State);
        Assert.Empty(r.Events);
    }

    // ── Sealed hierarchies ────────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(Intent))]
    [InlineData(typeof(SimEvent))]
    public void Hierarchy_IsClosed_AllCasesNestedAndSealed(Type root)
    {
        Assert.True(root.IsAbstract);
        // The compiler-synthesized record copy constructor must stay protected (CS8878); it can only
        // clone an existing case, so it does not open the hierarchy. Every other constructor is private.
        var ctors = root.GetConstructors(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            .Where(c => !c.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { root }))
            .ToArray();
        Assert.NotEmpty(ctors);
        Assert.All(ctors, c => Assert.True(c.IsPrivate, $"{root.Name} constructor must be private"));

        var cases = root.Assembly.GetTypes().Where(t => t != root && root.IsAssignableFrom(t)).ToArray();
        Assert.NotEmpty(cases);
        Assert.All(cases, t =>
        {
            Assert.True(t.IsSealed, $"{t.Name} must be sealed");
            Assert.Equal(root, t.DeclaringType);
        });
    }

    [Fact]
    public void Intent_Phase1Slice3_DeclaresOneDataCasePerControllerPath()
    {
        var cases = typeof(Intent).GetNestedTypes().Where(t => typeof(Intent).IsAssignableFrom(t));

        // Slice 1.3 deletes the transitional closure-carrying Tool case (1.2) and declares one data-only
        // case per controller path, each naming its target location where it acts on a panel (P1-D5).
        Assert.Equal(
            new[]
            {
                "AcquireRandom", "AdjustSplit", "AdvanceDialogue", "BeginSplit", "CancelSplit", "Click",
                "CloseRecipeMenu", "CommitSplit", "ConfirmRecipeMenu", "CycleRecipe", "Drop", "Dsl", "EnterBag",
                "Grab", "Harvest", "Interact", "Leave", "LeaveBag", "ModalSplit", "Move", "MoveRecipeMenu",
                "OpenRecipeMenu", "Peek", "Primary", "QuickSplit", "Secondary", "SetCursor", "SetElapsed",
                "Sort", "Tick", "ToolPrimary", "Undo"
            },
            cases.Select(t => t.Name).OrderBy(n => n, StringComparer.Ordinal).ToArray());
        Assert.Equal(new Intent.Tick(), new Intent.Tick());
        Assert.Equal(new Intent.AcquireRandom(LocationId.B), new Intent.AcquireRandom(LocationId.B));
        Assert.Equal(new Intent.Click(LocationId.C, new Position(1, 2), ClickType.Primary),
            new Intent.Click(LocationId.C, new Position(1, 2), ClickType.Primary));
    }

    /// <summary>
    /// Every intent is plain data: no delegate-typed and no Guid-typed property on any case, so an intent
    /// log replays in a fresh session (whose bag ids differ) and compares by value.
    /// </summary>
    [Fact]
    public void Intent_Cases_CarryNoClosuresOrBagIds()
    {
        var props = typeof(Intent).GetNestedTypes()
            .Where(t => typeof(Intent).IsAssignableFrom(t))
            .SelectMany(t => t.GetProperties().Where(p => p.DeclaringType == t).Select(p => (t.Name, p)))
            .ToList();
        Assert.All(props, x =>
        {
            Assert.False(typeof(Delegate).IsAssignableFrom(x.p.PropertyType), $"{x.Name}.{x.p.Name} is a delegate");
            Assert.False(x.p.PropertyType == typeof(Guid) || x.p.PropertyType == typeof(Guid?), $"{x.Name}.{x.p.Name} is a Guid");
        });
    }

    // ── Rule declaration enums ────────────────────────────────────────────────

    [Theory]
    [InlineData(typeof(TimeClass))]
    [InlineData(typeof(RuleKind))]
    public void RuleEnums_HaveNoZeroMember(Type enumType)
    {
        Assert.DoesNotContain(0, Enum.GetValues(enumType).Cast<int>());
    }

    [Fact]
    public void TimeClass_DeclaresTheFourClasses()
    {
        Assert.Equal(
            new[] { "ClosedForm", "EventSkippable", "BoundedCatchUp", "MustSimulate" },
            Enum.GetNames<TimeClass>());
        Assert.Equal(new[] { "Field", "Transfer" }, Enum.GetNames<RuleKind>());
    }

    /// <summary>A test rule declaring the bounded class with an explicit catch-up cap.</summary>
    private sealed class BoundedRule : IRule
    {
        public RuleKind Kind => RuleKind.Transfer;
        public TimeClass Time => TimeClass.BoundedCatchUp;
        public int MaxDelta => 16;
        public ImmutableArray<Type> EventKinds => ImmutableArray.Create(typeof(SimEvent.BeltAdvanced));
    }

    /// <summary>A test rule that does not override MaxDelta.</summary>
    private sealed class ClosedRule : IRule
    {
        public RuleKind Kind => RuleKind.Field;
        public TimeClass Time => TimeClass.ClosedForm;
        public ImmutableArray<Type> EventKinds => ImmutableArray<Type>.Empty;
    }

    [Fact]
    public void IRule_MaxDelta_DefaultsToZero_BoundedRulesOverride()
    {
        IRule closed = new ClosedRule();
        IRule bounded = new BoundedRule();

        Assert.Equal(0, closed.MaxDelta);
        Assert.Equal(16, bounded.MaxDelta);
        Assert.All(bounded.EventKinds, t => Assert.True(typeof(SimEvent).IsAssignableFrom(t)));
    }
}
