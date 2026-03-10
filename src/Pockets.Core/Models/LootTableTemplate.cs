using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Weighted item list for random generation. Each entry has an item name
/// (resolved to ItemType later) and a relative weight.
/// </summary>
public record LootTableTemplate(
    string Id,
    ImmutableArray<LootTableEntry> Entries,
    double FillRatio = 0.5);

/// <summary>
/// One entry in a loot table: an item name and its relative weight.
/// ItemName is resolved to an ItemType during the resolve phase.
/// </summary>
public record LootTableEntry(string ItemName, double Weight);
