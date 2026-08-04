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
var dialogue = DialogueLoader.LoadFromDirectory(dataPath);

// Both frontends load the same shared, seeded demo profile so the TUI and Godot
// builds start in identical game state with the same tick mode + dialogue beats (parity baseline).
var profile = GameInitializer.CreateDemoProfile(registry, dialogue: dialogue);

Application.Init();
var top = Application.Top;
top.Add(new GameView(profile.State, profile.Recipes, profile.FacilityRecipeMap,
    tickMode: profile.TickMode, dialogue: profile.Dialogue));
Application.Run();
Application.Shutdown();
