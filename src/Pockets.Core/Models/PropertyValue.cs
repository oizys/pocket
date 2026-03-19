namespace Pockets.Core.Models;

/// <summary>
/// Variant value type for per-instance item properties. Name lives in the dictionary key on ItemStack.
/// </summary>
public abstract record PropertyValue;

/// <summary>
/// Integer property value (durability, progress, stack counts, etc.)
/// </summary>
public record IntValue(int Value) : PropertyValue;

/// <summary>
/// String property value (custom names, enchantment types, etc.)
/// </summary>
public record StringValue(string Value) : PropertyValue;
