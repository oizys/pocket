namespace Pockets.Core.Sim;

/// <summary>
/// The purpose a <see cref="SimRng"/> draw serves. Each stream has its own draw counter, so adding draws to
/// one mechanic never shifts the values another sees. Values are part of the hash input: never renumber.
/// </summary>
public enum SimStream
{
    /// <summary>The debug AcquireRandom tool (key 5).</summary>
    Acquire = 1,

    /// <summary>The DSL <c>acquire-random</c> opcode.</summary>
    Dsl = 2,

    /// <summary>Recipe output factories run by a facility tick (e.g. the seedling's wilderness bag).</summary>
    Craft = 3,
}

/// <summary>
/// The counter-based RNG held in <see cref="SimState"/> (two-layer #23, G2; plan Slice 1.1). A draw is the
/// pure integer hash <see cref="Hash"/>(<see cref="WorldSeed"/>, <see cref="Step"/>, stream, draw index), so
/// a draw depends only on where it happens and never on how many draws came before it in other streams or
/// steps. The only state is the per-stream draw counter for the current step. It resets when the step
/// changes, so two runs that reach the same step through the same intents see the same values.
/// <para>
/// Immutable: <see cref="Next"/> returns the advanced value. <see cref="AsRandom"/> wraps it as a
/// <see cref="System.Random"/> for the existing Random-typed generator code.
/// </para>
/// </summary>
public sealed record SimRng
{
    private readonly ImmutableDictionary<SimStream, int> _draws;

    private SimRng(ulong worldSeed, int step, ImmutableDictionary<SimStream, int> draws)
    {
        WorldSeed = worldSeed;
        Step = step;
        _draws = draws;
    }

    /// <summary>The world seed every draw is keyed on (the demo profile uses its init seed).</summary>
    public ulong WorldSeed { get; }

    /// <summary>The step the draw counters belong to.</summary>
    public int Step { get; }

    /// <summary>A fresh RNG for <paramref name="worldSeed"/> at step 0 with no draws taken.</summary>
    public static SimRng Create(ulong worldSeed) =>
        new(worldSeed, 0, ImmutableDictionary<SimStream, int>.Empty);

    /// <summary>How many draws <paramref name="stream"/> has taken in <see cref="Step"/>.</summary>
    public int DrawCount(SimStream stream) => _draws.GetValueOrDefault(stream);

    /// <summary>
    /// This RNG positioned at <paramref name="step"/>: unchanged (same instance) when already there,
    /// otherwise with every stream's counter reset to zero.
    /// </summary>
    public SimRng AtStep(int step) =>
        step == Step ? this : new SimRng(WorldSeed, step, ImmutableDictionary<SimStream, int>.Empty);

    /// <summary>Draws the next 64-bit value from <paramref name="stream"/> and returns the advanced RNG.</summary>
    public (ulong Value, SimRng Next) Next(SimStream stream)
    {
        var draw = DrawCount(stream);
        return (Hash(WorldSeed, Step, stream, draw), new SimRng(WorldSeed, Step, _draws.SetItem(stream, draw + 1)));
    }

    /// <summary>
    /// A <see cref="System.Random"/> view drawing from <paramref name="stream"/>, starting at this RNG's
    /// counter. Read <see cref="SimRandom.Rng"/> afterwards to get the advanced RNG back into state.
    /// </summary>
    public SimRandom AsRandom(SimStream stream) => new(this, stream);

    /// <summary>
    /// The draw function: integer-only and runtime-independent. SplitMix64's finalizer folds in the seed,
    /// then the step, then (stream, draw).
    /// </summary>
    public static ulong Hash(ulong worldSeed, int step, SimStream stream, int draw)
    {
        var h = Mix(worldSeed + Gamma);
        h = Mix(h ^ (uint)step);
        return Mix(h ^ (((ulong)(uint)stream << 32) | (uint)draw));
    }

    /// <summary>The SplitMix64 output finalizer (Steele, Lea, Flood 2014). A bijection on 64-bit integers.</summary>
    public static ulong Mix(ulong z)
    {
        unchecked
        {
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }

    /// <summary>SplitMix64's golden-ratio increment.</summary>
    private const ulong Gamma = 0x9E3779B97F4A7C15UL;

    /// <summary>Value equality: seed, step and every stream's counter (zero counters count as absent).</summary>
    public bool Equals(SimRng? other) =>
        other is not null
        && WorldSeed == other.WorldSeed
        && Step == other.Step
        && _draws.Count == other._draws.Count
        && _draws.All(kv => other.DrawCount(kv.Key) == kv.Value);

    /// <summary>Hash consistent with <see cref="Equals(SimRng?)"/> (order-independent over the counters).</summary>
    public override int GetHashCode() =>
        _draws.Aggregate(HashCode.Combine(WorldSeed, Step), (acc, kv) => acc ^ HashCode.Combine(kv.Key, kv.Value));
}

/// <summary>
/// The <see cref="System.Random"/> bridge over a <see cref="SimRng"/> stream (plan Slice 1.1): it lets the
/// existing Random-typed code (wilderness generation, AcquireRandom) draw from the in-state RNG without a
/// rewrite. Each call consumes one or more <see cref="SimRng"/> draws, and the advanced RNG is
/// <see cref="Rng"/>. Every override derives its result from the 64-bit draw with integer arithmetic,
/// except <see cref="NextDouble"/>, which is an exact power-of-two scaling. No call touches the base
/// class's own (runtime-specific) generator.
/// </summary>
public sealed class SimRandom : Random
{
    private readonly SimStream _stream;

    /// <summary>Wraps <paramref name="rng"/>, drawing from <paramref name="stream"/>.</summary>
    internal SimRandom(SimRng rng, SimStream stream) : base(0)
    {
        Rng = rng;
        _stream = stream;
    }

    /// <summary>The RNG advanced past every draw taken so far; write it back into state after use.</summary>
    public SimRng Rng { get; private set; }

    /// <summary>One raw 64-bit draw.</summary>
    private ulong NextRaw()
    {
        var (value, next) = Rng.Next(_stream);
        Rng = next;
        return value;
    }

    /// <summary>A draw in [0, bound) by modulo (bias &lt; bound/2^64, negligible for game-sized bounds).</summary>
    private ulong NextBelow(ulong bound) => bound == 0 ? 0 : NextRaw() % bound;

    /// <inheritdoc/>
    public override int Next() => (int)NextBelow(int.MaxValue);

    /// <inheritdoc/>
    public override int Next(int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (int)NextBelow((ulong)maxValue);
    }

    /// <inheritdoc/>
    public override int Next(int minValue, int maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return (int)(minValue + (long)NextBelow((ulong)((long)maxValue - minValue)));
    }

    /// <inheritdoc/>
    public override long NextInt64() => (long)NextBelow(long.MaxValue);

    /// <inheritdoc/>
    public override long NextInt64(long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxValue);
        return (long)NextBelow((ulong)maxValue);
    }

    /// <inheritdoc/>
    public override long NextInt64(long minValue, long maxValue)
    {
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minValue, maxValue);
        return unchecked(minValue + (long)NextBelow((ulong)(maxValue - minValue)));
    }

    /// <inheritdoc/>
    public override double NextDouble() => (NextRaw() >> 11) * (1.0 / (1UL << 53));

    /// <inheritdoc/>
    public override float NextSingle() => (NextRaw() >> 40) * (1.0f / (1 << 24));

    /// <inheritdoc/>
    protected override double Sample() => NextDouble();

    /// <inheritdoc/>
    public override void NextBytes(byte[] buffer) => NextBytes(buffer.AsSpan());

    /// <inheritdoc/>
    public override void NextBytes(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
            buffer[i] = (byte)NextRaw();
    }
}
