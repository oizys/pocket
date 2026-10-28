using Pockets.Core.Sim;

namespace Pockets.Core.Tests.Sim;

/// <summary>
/// Slice 1.1: <see cref="SimRng"/>, the counter-based RNG held in <see cref="SimState"/> (G2). Every draw is
/// a pure integer hash of (WorldSeed, Step, Stream, Draw), so it can be pinned to literal vectors that a
/// future runtime cannot silently change.
/// </summary>
public class SimRngTests
{
    [Fact]
    public void Mix_MatchesSplitMix64ReferenceFinalizer()
    {
        // SplitMix64 seeded with 0: the first output is Mix(0 + golden gamma) = 0xE220A8397B1DCDAF.
        Assert.Equal(0xE220A8397B1DCDAFUL, SimRng.Mix(0x9E3779B97F4A7C15UL));
    }

    [Fact]
    public void Hash_IsPure_AndSeparatesEveryCoordinate()
    {
        var h = SimRng.Hash(7, 3, SimStream.Acquire, 0);
        Assert.Equal(h, SimRng.Hash(7, 3, SimStream.Acquire, 0));
        Assert.NotEqual(h, SimRng.Hash(8, 3, SimStream.Acquire, 0));
        Assert.NotEqual(h, SimRng.Hash(7, 4, SimStream.Acquire, 0));
        Assert.NotEqual(h, SimRng.Hash(7, 3, SimStream.Craft, 0));
        Assert.NotEqual(h, SimRng.Hash(7, 3, SimStream.Acquire, 1));
    }

    /// <summary>
    /// Cross-runtime golden vectors: the first 64 draws of the Acquire stream at step 1 for the demo seed.
    /// If this fails, the hash changed. That is a save/replay break, never a re-pin.
    /// </summary>
    [Fact]
    public void GoldenVectors_First64Draws_ArePinned()
    {
        var rng = SimRng.Create(20260803).AtStep(1);
        var draws = new ulong[64];
        for (var i = 0; i < draws.Length; i++)
            (draws[i], rng) = rng.Next(SimStream.Acquire);

        Assert.Equal(GoldenVectors, draws);
    }

    [Fact]
    public void Next_AdvancesOnlyItsOwnStreamCounter()
    {
        var rng = SimRng.Create(1).AtStep(5);
        var (_, after) = rng.Next(SimStream.Craft);
        Assert.Equal(1, after.DrawCount(SimStream.Craft));
        Assert.Equal(0, after.DrawCount(SimStream.Acquire));
        Assert.Equal(0, rng.DrawCount(SimStream.Craft));
    }

    [Fact]
    public void AtStep_ResetsCounters_OnlyWhenTheStepChanges()
    {
        var (_, drawn) = SimRng.Create(1).AtStep(5).Next(SimStream.Acquire);
        Assert.Same(drawn, drawn.AtStep(5));
        Assert.Equal(0, drawn.AtStep(6).DrawCount(SimStream.Acquire));
        Assert.Equal(6, drawn.AtStep(6).Step);
    }

    [Fact]
    public void Equality_IsByValue()
    {
        var a = SimRng.Create(9).AtStep(2).Next(SimStream.Dsl).Next;
        var b = SimRng.Create(9).AtStep(2).Next(SimStream.Dsl).Next;
        Assert.Equal(a, b);
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
        Assert.NotEqual(a, SimRng.Create(9).AtStep(2));
    }

    [Fact]
    public void AsRandom_DrawsTheSameSequenceAsNext_AndCommitsTheCounter()
    {
        var rng = SimRng.Create(3).AtStep(1);
        var random = rng.AsRandom(SimStream.Acquire);
        var a = random.Next(1000);
        var b = random.Next(1000);

        var (r0, n1) = rng.Next(SimStream.Acquire);
        var (r1, n2) = n1.Next(SimStream.Acquire);
        Assert.Equal((int)(r0 % 1000), a);
        Assert.Equal((int)(r1 % 1000), b);
        Assert.Equal(n2, random.Rng);
    }

    [Fact]
    public void AsRandom_HonoursTheRandomRangeContracts()
    {
        var random = SimRng.Create(11).AtStep(1).AsRandom(SimStream.Craft);
        for (var i = 0; i < 2000; i++)
        {
            Assert.InRange(random.Next(), 0, int.MaxValue - 1);
            Assert.InRange(random.Next(7), 0, 6);
            Assert.InRange(random.Next(-3, 4), -3, 3);
            Assert.InRange(random.NextDouble(), 0.0, 0.9999999999999999);
            Assert.InRange(random.NextInt64(5, 9), 5L, 8L);
        }
        Assert.Equal(0, random.Next(0));
        Assert.Equal(4, random.Next(4, 4));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => random.Next(5, 4));
    }

    private static readonly ulong[] GoldenVectors =
    {
        // Computed by an independent reference implementation of the same hash (Python), then frozen.
        0x2ACC794533E8DA6FUL, 0xD0A244DA974CC1A0UL, 0xE57EB23F666463FFUL, 0xB5CF40B6D39C28D6UL,
        0x604369636513D054UL, 0x78661A3880681E2CUL, 0x47D63A9D15CA4007UL, 0xA9C23857085328E6UL,
        0x48EBCA84F912CCB0UL, 0x8DC0015766F44DE3UL, 0xA46D605F5C840E8DUL, 0x87F98C12D95DBFE5UL,
        0x28AC6A8096C71057UL, 0x69DCE1A971590CBBUL, 0x4B4ED6F722EFC8CCUL, 0x1320288BFDBF8D70UL,
        0x26B57CB7A0443C78UL, 0x90EFD428EC38512BUL, 0xB67364A5B8E85E61UL, 0x23F17C4D025E5A85UL,
        0x871725E0C0787BBEUL, 0xD0644B44F0336E8BUL, 0xE25E2BE2EDDDA81CUL, 0x60D64A11EA45B25AUL,
        0xF9CDDACC4F7E2151UL, 0x118CF8A1B7D15BC2UL, 0x02B23463D6E34350UL, 0x5B07341A1C136366UL,
        0xB062F896B84CD797UL, 0xB56B0C82E52899D5UL, 0x045144F14CC53EC4UL, 0x9709F6B5C26868ECUL,
        0x042D050BBE59797EUL, 0x89B5BECB9A7FAF1FUL, 0x3CB499D5EDFAE3CDUL, 0xA59A0A8CC61D6077UL,
        0x3B3E1FB4FA69E006UL, 0xF00EE40E7447F0FCUL, 0x05BCB29D913C7376UL, 0x635313B4FF91050EUL,
        0x4FB25E6AA996E2F8UL, 0x8471493508735ED6UL, 0xB2CAF7C5B2D6002CUL, 0xCA2F055F41EB2B2CUL,
        0x2B7780F38987D25CUL, 0x972150005C669A46UL, 0xB879172EF31F864BUL, 0xEF17D1E7C2AA7B3FUL,
        0x6AAF1D685050B72AUL, 0x16F5A78E4C87D3CBUL, 0x83FBF15F920C3511UL, 0x0E92BD9279E101DEUL,
        0x03B5CD15741116B4UL, 0xB9741694A74C5C56UL, 0x179AA39AF7E63C6EUL, 0xC316DC3646921E3BUL,
        0xFBAA8B124DCB6DA9UL, 0x7CF4B0108E247470UL, 0x8FDFEA503D2528F2UL, 0x62D9367580CD9BBDUL,
        0x0C4FA1CBF9D3DCC2UL, 0xC3FEFB49447BFACFUL, 0x9243181A2E646394UL, 0x2F86422D4C1DA5ABUL,
    };
}
