using Pockets.Core.Models;

namespace Pockets.Core.Dsl;

/// <summary>
/// Marks a static method as a DSL opcode. The interpreter reflects over these
/// at startup to build the dispatch table. The Name is the DSL token that invokes this opcode.
/// DefaultLocation is the location used when the tacit (zero-arg) form is called.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public class OpcodeAttribute : Attribute
{
    public string Name { get; }
    public LocationId DefaultLocation { get; init; }

    public OpcodeAttribute(string name)
    {
        Name = name;
    }
}

/// <summary>
/// Marks a method parameter with its expected access level and role.
/// The interpreter uses this to drive automatic coercion from the data stack.
/// Source parameters are cleared/decremented after the op.
/// Target parameters are written to after the op.
/// DefaultLocation provides a per-param default when not explicitly supplied.
/// </summary>
[AttributeUsage(AttributeTargets.Parameter)]
public class ParamAttribute : Attribute
{
    public AccessLevel Level { get; }
    public bool Source { get; init; }
    public bool Target { get; init; }
    public LocationId DefaultLocation { get; init; } = (LocationId)(-1); // sentinel for "not set"

    public bool HasDefaultLocation => (int)DefaultLocation >= 0;

    public ParamAttribute(AccessLevel level)
    {
        Level = level;
    }
}
