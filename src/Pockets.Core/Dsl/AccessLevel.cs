namespace Pockets.Core.Dsl;

/// <summary>
/// The level of indirection a DSL parameter expects. The coercion resolver walks
/// down this chain to resolve arguments: Location → Bag → Cell → Stack.
/// Index resolves to a cursor Position within a location.
/// </summary>
public enum AccessLevel
{
    Location,
    Bag,
    Cell,
    Stack,
    Index
}
