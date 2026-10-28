namespace Pockets.Core.Sim;

/// <summary>
/// The event log's retention policy (Slice 1.8, P1-D9). Events are kept back to the oldest retained
/// snapshot; older ones are compacted away (Phase 7 flushes them to the save instead). Independently,
/// the log never holds more than <see cref="MaxBytes"/> (estimated): past it the oldest chunks are dropped
/// and a <see cref="LogTruncated"/> marker is recorded.
/// </summary>
/// <param name="MaxBytes">The hard memory cap (estimated bytes, see <see cref="EventLog.EstimateBytes"/>).</param>
/// <param name="ChunkSize">Events per chunk; compaction and the cap drop whole chunks.</param>
/// <param name="CheckpointEvery">Realtime: a checkpoint snapshot every this many steps.</param>
/// <param name="CheckpointRing">Realtime: how many checkpoints are kept (the oldest is evicted).</param>
public sealed record EventLogPolicy(
    long MaxBytes = EventLogPolicy.DefaultMaxBytes,
    int ChunkSize = 1024,
    int CheckpointEvery = 8,
    int CheckpointRing = 1000)
{
    /// <summary>8 MB (P1-D9).</summary>
    public const long DefaultMaxBytes = 8L * 1024 * 1024;

    /// <summary>The ratified policy: 8 MB, 1024-event chunks, a checkpoint every 8 steps, 1000 kept.</summary>
    public static EventLogPolicy Default { get; } = new();
}

/// <summary>
/// Where the timeline was when a snapshot was taken: the step it reflects (the session's
/// <c>TickCount</c>) and the event log's end position. Undo truncates the log back to it.
/// </summary>
public readonly record struct LogMark(int Step, long EventPos);

/// <summary>A realtime checkpoint (Slice 1.8): the state after <see cref="Mark"/>.Step, and where the log stood.</summary>
public sealed record Checkpoint(LogMark Mark, Models.GameState State);

/// <summary>
/// The marker the hard cap leaves (Slice 1.8): every event before position <paramref name="Before"/> was
/// dropped for memory, the last of them from step <paramref name="LastStep"/>. <paramref name="Count"/> is how
/// many cap drops this log has had in all. Compaction to the retention horizon is policy, not truncation,
/// and leaves no marker.
/// </summary>
public sealed record LogTruncated(long Before, int LastStep, int Count);

/// <summary>
/// The shell's event log (Slice 1.8; two-layer #23, A3): every <see cref="SimEvent"/> of the current
/// timeline, in order, as a chunked append-only log in the spike B (b) style. Positions are absolute along
/// the timeline: <see cref="Start"/> is the first retained event, <see cref="End"/> one past the last.
/// <para>
/// It is a persistent value, so the immutable <see cref="Models.GameSession"/> can hold it: an instance never
/// changes. Appends write into the shared tail chunk past every existing view's end (claimed atomically) and
/// never copy; only appending to a view that is no longer the tail's furthest (after an undo truncation)
/// copies that one chunk. Older sessions keep seeing exactly their own events.
/// </para>
/// </summary>
public sealed class EventLog
{
    /// <summary>
    /// One fixed-size chunk: the events and, per slot, the estimated bytes of the chunk up to and including it.
    /// <see cref="Filled"/> is the furthest any view has claimed.
    /// </summary>
    private sealed class Chunk
    {
        public readonly SimEvent[] Items;
        public readonly int[] CumBytes;
        public int Filled;

        public Chunk(int size)
        {
            Items = new SimEvent[size];
            CumBytes = new int[size];
        }
    }

    private readonly ImmutableArray<Chunk> _chunks;
    private readonly int _tail;

    /// <summary>An empty log at position 0.</summary>
    public static EventLog Empty { get; } = new(ImmutableArray<Chunk>.Empty, 0, 0, 0, null);

    private EventLog(ImmutableArray<Chunk> chunks, long start, int tail, long bytes, LogTruncated? truncated)
    {
        _chunks = chunks;
        Start = start;
        _tail = tail;
        Bytes = bytes;
        Truncated = truncated;
    }

    /// <summary>The position of the first retained event.</summary>
    public long Start { get; }

    /// <summary>One past the position of the last event (the next append's position).</summary>
    public long End => _chunks.IsEmpty ? Start : Start + (long)(_chunks.Length - 1) * _chunks[0].Items.Length + _tail;

    /// <summary>How many events are retained.</summary>
    public long Count => End - Start;

    /// <summary>The estimated bytes the retained events hold (see <see cref="EstimateBytes"/>).</summary>
    public long Bytes { get; }

    /// <summary>The latest hard-cap marker, or null when the cap never dropped anything.</summary>
    public LogTruncated? Truncated { get; }

    /// <summary>The retained events, oldest first.</summary>
    public IEnumerable<SimEvent> Events =>
        _chunks.SelectMany((c, i) => c.Items.Take(i == _chunks.Length - 1 ? _tail : c.Items.Length));

    /// <summary>The retained events at or after <paramref name="position"/>, oldest first.</summary>
    public IEnumerable<SimEvent> Since(long position) => Events.Skip((int)Math.Clamp(position - Start, 0, Count));

    /// <summary>
    /// Appends <paramref name="events"/> in order, then enforces <paramref name="policy"/>'s hard cap. Returns
    /// this same log when there is nothing to append.
    /// </summary>
    public EventLog Append(ImmutableArray<SimEvent> events, EventLogPolicy policy)
    {
        if (events.IsDefaultOrEmpty)
            return this;

        var chunks = _chunks;
        var tail = _tail;
        var bytes = Bytes;
        var builder = (ImmutableArray<Chunk>.Builder?)null;
        foreach (var e in events)
        {
            var size = EstimateBytes(e);
            var last = builder is not null ? builder[^1] : chunks.IsEmpty ? null : chunks[^1];
            if (last is null || tail == last.Items.Length)
            {
                last = new Chunk(policy.ChunkSize) { Filled = 1 };
                builder ??= chunks.ToBuilder();
                builder.Add(last);
                tail = 0;
            }
            else if (Interlocked.CompareExchange(ref last.Filled, tail + 1, tail) != tail)
            {
                // Another view already wrote past our end in this chunk: fork it (copy what we see).
                var fork = new Chunk(last.Items.Length) { Filled = tail + 1 };
                Array.Copy(last.Items, fork.Items, tail);
                Array.Copy(last.CumBytes, fork.CumBytes, tail);
                builder ??= chunks.ToBuilder();
                builder[^1] = fork;
                last = fork;
            }

            // Slot `tail` of `last` is now ours alone.
            last.Items[tail] = e;
            last.CumBytes[tail] = (tail > 0 ? last.CumBytes[tail - 1] : 0) + size;
            tail++;
            bytes += size;
        }

        var log = new EventLog(builder?.ToImmutable() ?? chunks, Start, tail, bytes, Truncated);
        return log.Bytes > policy.MaxBytes ? log.Cap(policy.MaxBytes) : log;
    }

    /// <summary>
    /// Drops the oldest whole chunks while the log is over <paramref name="maxBytes"/> (the newest chunk is
    /// always kept), recording a <see cref="LogTruncated"/> marker.
    /// </summary>
    private EventLog Cap(long maxBytes)
    {
        var drop = 0;
        var bytes = Bytes;
        while (bytes > maxBytes && drop < _chunks.Length - 1)
            bytes -= _chunks[drop++].CumBytes[^1];
        if (drop == 0)
            return this;
        var size = _chunks[0].Items.Length;
        var before = Start + (long)drop * size;
        var lastStep = _chunks[drop - 1].Items[^1].Step;
        return new EventLog(_chunks.RemoveRange(0, drop), before, _tail, bytes,
            new LogTruncated(before, lastStep, (Truncated?.Count ?? 0) + 1));
    }

    /// <summary>
    /// Compaction (P1-D9): drops every whole chunk that lies entirely before <paramref name="horizon"/>, the
    /// position of the oldest retained snapshot. Events at or after the horizon are always kept. No marker.
    /// </summary>
    public EventLog CompactTo(long horizon)
    {
        if (_chunks.IsEmpty)
            return this;
        var size = _chunks[0].Items.Length;
        var drop = (int)Math.Clamp((horizon - Start) / size, 0, _chunks.Length - 1);
        if (drop == 0)
            return this;
        var dropped = _chunks.Take(drop).Sum(c => (long)c.CumBytes[^1]);
        return new EventLog(_chunks.RemoveRange(0, drop), Start + (long)drop * size, _tail, Bytes - dropped, Truncated);
    }

    /// <summary>
    /// Undo truncation: the log as it stood at <paramref name="position"/> (events at or after it are
    /// dropped). A position before <see cref="Start"/> — its events were already compacted or capped away —
    /// empties the log and restarts it there.
    /// </summary>
    public EventLog TruncateTo(long position)
    {
        if (position >= End)
            return this;
        if (position <= Start)
            return new EventLog(ImmutableArray<Chunk>.Empty, position, 0, 0, Truncated);

        var size = _chunks[0].Items.Length;
        var keep = position - Start;
        var chunkCount = (int)((keep + size - 1) / size);
        var tail = (int)(keep - (long)(chunkCount - 1) * size);
        var chunks = _chunks.RemoveRange(chunkCount, _chunks.Length - chunkCount);
        var bytes = chunks.Take(chunkCount - 1).Sum(c => (long)c.CumBytes[^1]) + chunks[^1].CumBytes[tail - 1];
        return new EventLog(chunks, Start, tail, bytes, Truncated);
    }

    /// <summary>
    /// The estimated retained size of one event: its log slot (reference plus cumulative-size entry) and the
    /// record itself (header, step/seq, a word per field), plus its array payloads. Items and bags are shared
    /// with the game state, so they are not counted. An estimate, documented as such: the cap is on this.
    /// </summary>
    public static int EstimateBytes(SimEvent e) => 16 + 48 + e switch
    {
        SimEvent.IntentApplied => 32,
        SimEvent.IntentRejected r => 32 + 2 * r.Reason.Length,
        SimEvent.BagRearranged r => 24 + 4 * (r.Permutation.IsDefault ? 0 : r.Permutation.Length),
        SimEvent.CraftCompleted c => 48 + 48 * (c.Outputs.IsDefault ? 0 : c.Outputs.Length)
                                         + 24 * (c.Consumed.IsDefault ? 0 : c.Consumed.Length),
        SimEvent.Harvested h => 24 + 48 * (h.Yields.IsDefault ? 0 : h.Yields.Length),
        _ => 32
    };
}
