using System.Text.RegularExpressions;
using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Recipes;
using Pockets.DepthRecipes;

// Design/analysis tool for the depth-recipe progression graph. Two subcommands:
//
//   depth-recipes frontier [--reached <nodes>]   print the progression frontier
//   depth-recipes diagram  [--out <png-path>]    render the labeled graph PNG
//
// <nodes> is a comma-separated list like "quiet+5,gloam+1" (quadrant, +/− aspect,
// depth) or the canonical key form "quiet-positive:5". With no --reached, the world
// start (Quiet 1) is used.

var book = DepthRecipeData.Book;
string command = args.FirstOrDefault() ?? "frontier";

switch (command)
{
    case "frontier":
        RunFrontier();
        break;
    case "diagram":
        RunDiagram();
        break;
    case "validate":
        RunValidate();
        break;
    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use: frontier | diagram | validate");
        Environment.Exit(2);
        break;
}

void RunFrontier()
{
    string? reached = GetArg("--reached");
    ProgressionState state = reached is null
        ? ProgressionState.Start(book)
        : ProgressionState.Of(ParseNodes(reached));
    FrontierPrinter.Print(book, state, Console.Out);
}

void RunDiagram()
{
    string outPath = GetArg("--out")
        ?? "/home/oizys/obsid/paths/projects/pockets/assets/depth-recipes-v1.png";
    Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
    ProgressionDiagram.Render(book, outPath);
    Console.WriteLine($"diagram → {outPath}  ({book.Nodes.Length} nodes, {book.Edges.Length} cross-edges, "
        + $"{book.Gates.Length} hero gates)");
}

void RunValidate()
{
    var issues = RecipeValidation.Validate(book);
    if (issues.IsEmpty)
    {
        Console.WriteLine($"OK — {book.Nodes.Length} nodes, {book.Recipes.Count()} recipes, "
            + $"{book.Edges.Length} edges, {book.Gates.Length} gates: no issues.");
    }
    else
    {
        Console.Error.WriteLine($"{issues.Length} issue(s):");
        foreach (var i in issues) Console.Error.WriteLine($"  - {i}");
        Environment.Exit(1);
    }
}

// Parse "quiet+5,gloam+1,quiet-negative:11" into nodes.
IEnumerable<ZoneDepth> ParseNodes(string spec) =>
    spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(ParseNode)
        .ToImmutableArray();

ZoneDepth ParseNode(string token)
{
    // Canonical key form: "<quadrant>-<positive|negative>:<depth>"
    var key = Regex.Match(token, @"^(quiet|gloam|flux|jitter)-(positive|negative):(\d+)$",
        RegexOptions.IgnoreCase);
    if (key.Success)
    {
        var quadrant = Enum.Parse<Quadrant>(Capitalize(key.Groups[1].Value));
        var aspect = key.Groups[2].Value.Equals("positive", StringComparison.OrdinalIgnoreCase)
            ? Aspect.Positive : Aspect.Negative;
        var zone = aspect == Aspect.Positive
            ? EntropyMatrix.Positive(quadrant).Zone
            : EntropyMatrix.Negative(quadrant).Zone;
        return new ZoneDepth(zone, int.Parse(key.Groups[3].Value));
    }

    // Shorthand: "<quadrant><+|->:<depth>" e.g. "quiet+5" / "gloam-7"
    var m = Regex.Match(token, @"^(quiet|gloam|flux|jitter)([+-])(\d+)$", RegexOptions.IgnoreCase);
    if (!m.Success)
        throw new FormatException($"Cannot parse node '{token}'. Use e.g. 'quiet+5' or 'quiet-positive:5'.");

    var q = Enum.Parse<Quadrant>(Capitalize(m.Groups[1].Value));
    var asp = m.Groups[2].Value == "+" ? Aspect.Positive : Aspect.Negative;
    var z = asp == Aspect.Positive ? EntropyMatrix.Positive(q).Zone : EntropyMatrix.Negative(q).Zone;
    return new ZoneDepth(z, int.Parse(m.Groups[3].Value));
}

static string Capitalize(string s) => char.ToUpperInvariant(s[0]) + s[1..].ToLowerInvariant();

string? GetArg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
