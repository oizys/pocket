using System.Collections.Immutable;
using Terminal.Gui;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.App.Views;

// Resolve data path by walking up to the directory containing Pockets.sln
var dir = new DirectoryInfo(AppContext.BaseDirectory);
while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Pockets.sln")))
    dir = dir.Parent;

if (dir is null)
{
    Console.Error.WriteLine("Could not find Pockets.sln. Run from the project directory.");
    return;
}

var dataPath = Path.Combine(dir.FullName, "data");
var registry = ContentLoader.LoadFromDirectory(dataPath);
var (gameState, recipes) = GameInitializer.CreateFromRegistry(registry);
var facilityRecipeMap = registry.BuildFacilityRecipeMap();

Application.Init();
var top = Application.Top;
top.Add(new GameView(gameState, recipes, facilityRecipeMap));
Application.Run();
Application.Shutdown();
