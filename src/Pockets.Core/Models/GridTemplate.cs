namespace Pockets.Core.Models;

/// <summary>
/// Reusable bag layout template: dimensions, environment, color scheme.
/// Used by generators to create bags with consistent properties.
/// </summary>
public record GridTemplate(
    string Id,
    int Columns,
    int Rows,
    string EnvironmentType = "Default",
    string ColorScheme = "Default");
