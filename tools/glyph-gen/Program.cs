using Pockets.Core.Cosmology.Glyphs;
using Pockets.GlyphGen;

// Entry point for the glyph asset generator.
//
// Usage:
//   glyph-gen [--svg-out <dir>] [--sheet <png-path>] [--no-sheet]
//
// Defaults: SVGs to <repo>/assets/glyphs, contact sheet to the Obsidian vault
// path used for Aaron's phone review. Every ambiguous shape is a knob on
// GlyphParams; pass a different knob set by editing the Params below.

string repoRoot = RepoLocator.FindRepoRoot();
string svgOut = GetArg("--svg-out") ?? Path.Combine(repoRoot, "assets", "glyphs");
string sheetOut = GetArg("--sheet")
    ?? "/home/oizys/obsid/paths/projects/pockets/assets/glyphs-contact-sheet-v2.png";
bool emitSheet = !args.Contains("--no-sheet");

var glyphParams = GlyphParams.Default;
var glyphs = GlyphCatalog.All(glyphParams);

// 1. Emit the 12 Godot-ready SVG files (single canonical black stroke).
Directory.CreateDirectory(svgOut);
foreach (var g in glyphs)
{
    string path = Path.Combine(svgOut, $"{g.Name}.svg");
    File.WriteAllText(path, g.ToSvg(glyphParams));
    Console.WriteLine($"svg   {g.Name}.svg");
}

// 2. Render the labeled light+dark contact sheet PNG for phone review.
if (emitSheet)
{
    Directory.CreateDirectory(Path.GetDirectoryName(sheetOut)!);
    ContactSheet.Render(glyphs, glyphParams, sheetOut);
    Console.WriteLine($"sheet {sheetOut}");
}

Console.WriteLine($"Done: {glyphs.Length} glyphs -> {svgOut}");

string? GetArg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
