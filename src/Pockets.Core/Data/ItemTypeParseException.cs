namespace Pockets.Core.Data;

/// <summary>
/// Thrown when an item definition markdown file cannot be parsed.
/// </summary>
public class ItemTypeParseException : Exception
{
    public string Filename { get; }

    public ItemTypeParseException(string filename, string message)
        : base($"{filename}: {message}")
    {
        Filename = filename;
    }
}
