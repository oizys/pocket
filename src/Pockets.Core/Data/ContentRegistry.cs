using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Data;

/// <summary>
/// Holds all resolved game content definitions loaded from data files.
/// Built by ContentLoader in two phases: parse (raw blocks) then resolve (cross-reference).
/// </summary>
public record ContentRegistry(
    ImmutableDictionary<string, ItemType> Items,
    ImmutableDictionary<string, Recipe> Recipes,
    ImmutableDictionary<string, FacilityDefinition> Facilities,
    ImmutableDictionary<string, GridTemplate> GridTemplates,
    ImmutableDictionary<string, LootTableTemplate> LootTableTemplates)
{
    public static ContentRegistry Empty { get; } = new(
        ImmutableDictionary<string, ItemType>.Empty,
        ImmutableDictionary<string, Recipe>.Empty,
        ImmutableDictionary<string, FacilityDefinition>.Empty,
        ImmutableDictionary<string, GridTemplate>.Empty,
        ImmutableDictionary<string, LootTableTemplate>.Empty);

    /// <summary>
    /// Combines this registry with another, returning a new registry containing all entries.
    /// On key conflict, b's entries override a's entries.
    /// </summary>
    public ContentRegistry Merge(ContentRegistry b) => new(
        Items.SetItems(b.Items),
        Recipes.SetItems(b.Recipes),
        Facilities.SetItems(b.Facilities),
        GridTemplates.SetItems(b.GridTemplates),
        LootTableTemplates.SetItems(b.LootTableTemplates));

    /// <summary>
    /// Builds a mapping from facility EnvironmentType to recipe IDs.
    /// Used by GameSession to look up which recipes belong to which facility.
    /// </summary>
    public ImmutableDictionary<string, ImmutableArray<string>> BuildFacilityRecipeMap() =>
        Facilities.Values.ToImmutableDictionary(
            f => f.EnvironmentType,
            f => f.RecipeIds);
}
