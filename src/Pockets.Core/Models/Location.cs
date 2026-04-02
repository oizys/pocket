using System.Collections.Immutable;

namespace Pockets.Core.Models;

/// <summary>
/// Identifies a named location (panel context) in the game.
/// H=Hand, T=Toolbar, B=Bag (inventory), W=World, C=Container.
/// </summary>
public enum LocationId { H, T, B, W, C }

/// <summary>
/// A cursor position within a bag context. Tracks which bag is the root of this
/// location, the current cursor position, and any breadcrumb navigation stack.
/// </summary>
public record Location(
    Guid BagId,
    Cursor Cursor,
    ImmutableStack<BreadcrumbEntry> Breadcrumbs)
{
    /// <summary>
    /// Creates a location pointing at cell (0,0) of the given bag with no breadcrumbs.
    /// </summary>
    public static Location AtOrigin(Guid bagId) =>
        new(bagId, new Cursor(new Position(0, 0)), ImmutableStack<BreadcrumbEntry>.Empty);

    /// <summary>
    /// True when inside a nested bag (breadcrumb stack is non-empty).
    /// </summary>
    public bool IsNested => !Breadcrumbs.IsEmpty;
}

/// <summary>
/// Variadic map of named locations. H and B are expected to always be present
/// (enforced at construction, not by the type system). Other locations (T, W, C)
/// are added/removed as panels open and close.
/// </summary>
public record LocationMap(ImmutableDictionary<LocationId, Location> Entries)
{
    /// <summary>
    /// Gets a location by id. Throws if not present.
    /// </summary>
    public Location Get(LocationId id) => Entries[id];

    /// <summary>
    /// Gets a location by id, or null if not present.
    /// </summary>
    public Location? TryGet(LocationId id) =>
        Entries.TryGetValue(id, out var loc) ? loc : null;

    /// <summary>
    /// Returns a new map with the location added or replaced.
    /// </summary>
    public LocationMap Set(LocationId id, Location entry) =>
        new(Entries.SetItem(id, entry));

    /// <summary>
    /// Returns a new map with the location removed.
    /// </summary>
    public LocationMap Remove(LocationId id) =>
        new(Entries.Remove(id));

    /// <summary>
    /// Returns true if the given location exists.
    /// </summary>
    public bool Has(LocationId id) => Entries.ContainsKey(id);

    /// <summary>
    /// Creates a LocationMap with the minimum required locations (Hand + Bag).
    /// </summary>
    public static LocationMap Create(Guid handBagId, Guid rootBagId) =>
        new(ImmutableDictionary<LocationId, Location>.Empty
            .Add(LocationId.H, Location.AtOrigin(handBagId))
            .Add(LocationId.B, Location.AtOrigin(rootBagId)));

    /// <summary>
    /// Creates a LocationMap with Hand + Bag, with a specific cursor position for B.
    /// </summary>
    public static LocationMap Create(Guid handBagId, Guid rootBagId, Cursor cursor) =>
        new(ImmutableDictionary<LocationId, Location>.Empty
            .Add(LocationId.H, Location.AtOrigin(handBagId))
            .Add(LocationId.B, new Location(rootBagId, cursor, ImmutableStack<BreadcrumbEntry>.Empty)));
}
