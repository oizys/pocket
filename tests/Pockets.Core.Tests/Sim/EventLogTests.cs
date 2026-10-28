using System.Collections.Immutable;
using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.8 acceptance 1, log level: the <see cref="EventLog"/>'s append order and positions, its
/// persistence (old views never change, a truncated view forks on append), compaction to the retention
/// horizon, the hard cap with its <see cref="LogTruncated"/> marker, and undo truncation.
/// </summary>
public class EventLogTests
{
    /// <summary>A small-chunk policy so chunk boundaries are cheap to reach.</summary>
    private static readonly EventLogPolicy Small = new(ChunkSize: 4);

    /// <summary>A distinguishable event for step <paramref name="step"/>.</summary>
    private static SimEvent Ev(int step, int seq = 0) => new SimEvent.TimeAdvanced(step, seq);

    /// <summary><paramref name="n"/> events, steps <paramref name="from"/> onward.</summary>
    private static ImmutableArray<SimEvent> Evs(int from, int n) => Enumerable.Range(from, n).Select(i => Ev(i)).ToImmutableArray();

    /// <summary>The steps of a log's retained events, in order.</summary>
    private static int[] Steps(EventLog log) => log.Events.Select(e => e.Step).ToArray();

    /// <summary>The estimated bytes of a log's retained events, recomputed from scratch.</summary>
    private static long Recount(EventLog log) => log.Events.Sum(e => (long)EventLog.EstimateBytes(e));

    [Fact]
    public void Empty_HasNoEvents_AtPositionZero()
    {
        Assert.Equal(0, EventLog.Empty.Start);
        Assert.Equal(0, EventLog.Empty.End);
        Assert.Empty(EventLog.Empty.Events);
        Assert.Null(EventLog.Empty.Truncated);
    }

    [Fact]
    public void Append_Nothing_ReturnsSameLog()
    {
        var log = EventLog.Empty.Append(Evs(1, 3), Small);
        Assert.Same(log, log.Append(ImmutableArray<SimEvent>.Empty, Small));
    }

    [Fact]
    public void Append_KeepsOrder_AcrossChunkBoundaries()
    {
        var log = EventLog.Empty.Append(Evs(1, 3), Small).Append(Evs(4, 6), Small);

        Assert.Equal(Enumerable.Range(1, 9), Steps(log));
        Assert.Equal(0, log.Start);
        Assert.Equal(9, log.End);
        Assert.Equal(9, log.Count);
        Assert.Equal(Recount(log), log.Bytes);
    }

    [Fact]
    public void Append_LeavesEarlierViewsUnchanged()
    {
        var a = EventLog.Empty.Append(Evs(1, 2), Small);
        var b = a.Append(Evs(3, 5), Small);

        Assert.Equal(new[] { 1, 2 }, Steps(a));
        Assert.Equal(Enumerable.Range(1, 7), Steps(b));
    }

    [Fact]
    public void Append_OnTwoBranchesOfOneView_ForksTheTail_NeitherSeesTheOther()
    {
        var root = EventLog.Empty.Append(Evs(1, 2), Small);
        var left = root.Append(ImmutableArray.Create(Ev(100)), Small);
        var right = root.Append(ImmutableArray.Create(Ev(200), Ev(201)), Small);

        Assert.Equal(new[] { 1, 2, 100 }, Steps(left));
        Assert.Equal(new[] { 1, 2, 200, 201 }, Steps(right));
        Assert.Equal(new[] { 1, 2 }, Steps(root));
        Assert.Equal(Recount(right), right.Bytes);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(4)] // a chunk boundary
    [InlineData(6)]
    [InlineData(9)]
    public void TruncateTo_KeepsExactlyTheEventsBeforeThePosition(int position)
    {
        var log = EventLog.Empty.Append(Evs(1, 9), Small);
        var cut = log.TruncateTo(position);

        Assert.Equal(Enumerable.Range(1, position), Steps(cut));
        Assert.Equal(position, cut.End);
        Assert.Equal(Recount(cut), cut.Bytes);
        Assert.Equal(Enumerable.Range(1, 9), Steps(log)); // the original is untouched
    }

    [Fact]
    public void TruncateTo_ThenAppend_ContinuesAtThePosition_AndForksSharedChunk()
    {
        var log = EventLog.Empty.Append(Evs(1, 7), Small);
        var redone = log.TruncateTo(5).Append(ImmutableArray.Create(Ev(50), Ev(51)), Small);

        Assert.Equal(new[] { 1, 2, 3, 4, 5, 50, 51 }, Steps(redone));
        Assert.Equal(Enumerable.Range(1, 7), Steps(log));
        Assert.Equal(Recount(redone), redone.Bytes);
    }

    [Fact]
    public void TruncateTo_BeforeStart_EmptiesAndRestartsThere()
    {
        var log = EventLog.Empty.Append(Evs(1, 12), Small).CompactTo(8);
        Assert.Equal(8, log.Start);

        var cut = log.TruncateTo(5);
        Assert.Empty(cut.Events);
        Assert.Equal(5, cut.Start);
        Assert.Equal(5, cut.End);
        Assert.Equal(0, cut.Bytes);

        var next = cut.Append(ImmutableArray.Create(Ev(99)), Small);
        Assert.Equal(5, next.Start);
        Assert.Equal(6, next.End);
    }

    [Fact]
    public void CompactTo_DropsOnlyWholeChunksBeforeTheHorizon()
    {
        var log = EventLog.Empty.Append(Evs(1, 10), Small);

        var compacted = log.CompactTo(6); // chunk [0..3] is wholly before 6; [4..7] holds position 6
        Assert.Equal(4, compacted.Start);
        Assert.Equal(Enumerable.Range(5, 6), Steps(compacted));
        Assert.Equal(Recount(compacted), compacted.Bytes);
        Assert.Null(compacted.Truncated); // compaction is policy, not truncation
    }

    [Fact]
    public void CompactTo_NeverDropsAnEventAtOrAfterTheHorizon()
    {
        var log = EventLog.Empty.Append(Evs(1, 13), Small);
        foreach (var horizon in Enumerable.Range(0, 14))
        {
            var compacted = log.CompactTo(horizon);
            Assert.True(compacted.Start <= horizon);
            Assert.Equal(log.Since(horizon).ToArray(), compacted.Since(horizon).ToArray());
        }
    }

    [Fact]
    public void CompactTo_KeepsTheNewestChunk()
    {
        var log = EventLog.Empty.Append(Evs(1, 8), Small);
        var compacted = log.CompactTo(100);
        Assert.Equal(new[] { 5, 6, 7, 8 }, Steps(compacted));
    }

    [Fact]
    public void Cap_DropsOldestChunks_AndRecordsMarker()
    {
        var perEvent = EventLog.EstimateBytes(Ev(0));
        var policy = Small with { MaxBytes = perEvent * 6 };

        var log = EventLog.Empty.Append(Evs(1, 10), policy);

        Assert.True(log.Bytes <= policy.MaxBytes);
        Assert.Equal(new[] { 5, 6, 7, 8, 9, 10 }, Steps(log));
        Assert.Equal(new LogTruncated(Before: 4, LastStep: 4, Count: 1), log.Truncated);
    }

    [Fact]
    public void Cap_MarkerCountsEveryDrop_AndSurvivesTruncation()
    {
        var perEvent = EventLog.EstimateBytes(Ev(0));
        var policy = Small with { MaxBytes = perEvent * 6 };

        var log = EventLog.Empty.Append(Evs(1, 10), policy).Append(Evs(11, 4), policy);

        Assert.Equal(2, log.Truncated!.Count);
        Assert.Equal(8, log.Truncated.Before);
        Assert.Same(log.Truncated, log.TruncateTo(9).Truncated);
    }

    [Fact]
    public void Since_ReturnsTheSuffixFromAPosition()
    {
        var log = EventLog.Empty.Append(Evs(1, 6), Small);
        Assert.Equal(new[] { 4, 5, 6 }, log.Since(3).Select(e => e.Step));
        Assert.Empty(log.Since(6));
    }

    [Fact]
    public void EstimateBytes_GrowsWithArrayPayloads()
    {
        var small = new SimEvent.BagRearranged(1, 0, Guid.Empty, ImmutableArray.Create(0));
        var big = new SimEvent.BagRearranged(1, 0, Guid.Empty, Enumerable.Range(0, 32).ToImmutableArray());
        Assert.True(EventLog.EstimateBytes(big) > EventLog.EstimateBytes(small));
    }

    [Fact]
    public void DefaultPolicy_IsTheRatifiedOne()
    {
        Assert.Equal(8L * 1024 * 1024, EventLogPolicy.Default.MaxBytes);
        Assert.Equal(8, EventLogPolicy.Default.CheckpointEvery);
        Assert.Equal(1000, EventLogPolicy.Default.CheckpointRing);
    }
}
