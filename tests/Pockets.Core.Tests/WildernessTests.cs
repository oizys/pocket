using System.Collections.Immutable;
using Pockets.Core;
using Pockets.Core.Data;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Xunit;

namespace Pockets.Core.Tests;

/// <summary>
/// Slice-6 integration: the Quiet 1 wilderness (enter-only, Dust palette, Quiet+ glyph header),
/// unnavigable tree cells + the bump feedback/beat, the seeded harvestables + ruin bag, and the
/// recipe-as-item → KnownRecipes registry.
/// </summary>
public class WildernessTests
{
    private static readonly ItemType TreeType = new("Standing Tree", Category.Structure, IsStackable: false);
    private static readonly ItemType GrassType = new("Dry Grass", Category.Material, IsStackable: true);
    private static readonly ItemType RecipeType = new("Quiet Recipe", Category.Misc, IsStackable: false);

    private static DialogueBook Book() => DialogueLoader.LoadFromDirectory(TestPaths.DataDir);

    /// <summary>A single root bag (1 row) with the given cells, cursor at (0,0), demo dialogue book.</summary>
    private static GameController World(params Cell[] cells)
    {
        var grid = Grid.Create(cells.Length, 1);
        for (int i = 0; i < cells.Length; i++)
            grid = grid.SetCell(i, cells[i]);
        var root = new Bag(grid);
        var hand = GameState.CreateHandBag(1);
        var store = BagStore.Empty.Add(root).Add(hand);
        var state = new GameState(store, LocationMap.Create(hand.Id, root.Id),
            ImmutableArray.Create(TreeType, GrassType, RecipeType)) { Ui = UiLedger.DemoInitial };
        return new GameController(GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue) with { Book = Book() });
    }

    private static Cell Tree() => new(Stack: new ItemStack(TreeType, 1), Frame: new TreeFrame());
    private static Cell Grass(int n = 1) => new(Stack: new ItemStack(GrassType, n));

    // ==================== Unnavigable tree cells ====================

    [Fact]
    public void MovingIntoTree_LeavesCursorUnchanged_AndQueuesBumpPulse()
    {
        var c = World(Grass(), Tree(), Grass()); // cursor at (0,0)
        c.HandleKey(GameKey.Right);              // → would enter the tree at (0,1)
        Assert.Equal(new Position(0, 0), c.Session.Current.Cursor.Position); // refused: unchanged
        Assert.Equal(FeedbackPulse.Bump, c.ConsumeFeedbackPulse());          // bump cue queued
    }

    [Fact]
    public void MovingIntoOpenCell_MovesNormally_NoBump()
    {
        var c = World(Grass(), Grass(), Grass());
        c.HandleKey(GameKey.Right);
        Assert.Equal(new Position(0, 1), c.Session.Current.Cursor.Position);
        Assert.Equal(FeedbackPulse.None, c.ConsumeFeedbackPulse());
    }

    [Fact]
    public void ThirdTreeBump_FiresAxeAbsenceBeat_ExactlyOnce()
    {
        var c = World(Grass(), Tree());
        c.HandleKey(GameKey.Right); Assert.Null(c.Session.Current.Dialogue.ActiveBeatId);   // bump 1
        c.HandleKey(GameKey.Right); Assert.Null(c.Session.Current.Dialogue.ActiveBeatId);   // bump 2
        c.HandleKey(GameKey.Right);                                                          // bump 3
        Assert.Equal("axe-absence", c.Session.Current.Dialogue.ActiveBeatId);

        c.HandleKey(GameKey.Primary);                                                        // dismiss
        Assert.Null(c.Session.Current.Dialogue.ActiveBeatId);

        c.HandleKey(GameKey.Right);                                                          // bump 4 — no refire
        Assert.Null(c.Session.Current.Dialogue.ActiveBeatId);
        Assert.Equal(4, c.Session.Current.Dialogue.TreeBumpCount);
    }

    // ==================== Recipe-as-item → KnownRecipes ====================

    [Fact]
    public void GrabbingRecipeItem_AddsRecipeToKnownRegistry()
    {
        var recipe = new ItemStack(RecipeType, 1)
            .WithProperty(GameState.RecipeItemProperty, new StringValue("another-quiet-1"));
        var c = World(new Cell(Stack: recipe));
        Assert.Empty(c.Session.Current.KnownRecipes);

        c.HandleKey(GameKey.Primary); // grab the recipe into the hand
        Assert.Contains("another-quiet-1", c.Session.Current.KnownRecipes);
    }

    [Fact]
    public void GrabbingPlainItem_DoesNotRegisterAnyRecipe()
    {
        var c = World(Grass());
        c.HandleKey(GameKey.Primary);
        Assert.Empty(c.Session.Current.KnownRecipes);
    }

    // ==================== Demo profile content ====================

    private static GameState DemoState() =>
        GameInitializer.CreateDemoProfile(ContentLoader.LoadFromDirectory(TestPaths.DataDir)).State;

    private static Bag Wilderness(GameState s) =>
        s.Store.GetById(s.RootBag.Grid.GetCell(10).Stack!.ContainedBagId!.Value)!;

    [Fact]
    public void DemoProfile_SeedsQuiet1Wilderness_EnterOnlyDustQuietGlyph()
    {
        var wild = Wilderness(DemoState());
        Assert.Equal(GameInitializer.WildernessEnvironment, wild.EnvironmentType); // "Quiet 1"
        Assert.Equal(GameInitializer.WildernessPalette, wild.ColorScheme);         // "Dust"
        Assert.Equal(GameInitializer.WildernessGlyph, wild.Glyph);                 // "quiet-positive"
        Assert.True(wild.EnterOnly);
        Assert.Contains(wild.Grid.Cells, c => c.Frame is TreeFrame);              // unnavigable trees present
    }

    [Fact]
    public void DemoProfile_Ruin_HoldsNoteRecipeAndEyeCore()
    {
        var state = DemoState();
        var wild = Wilderness(state);
        var ruin = wild.Grid.Cells
            .Select(c => c.Stack?.ContainedBagId is { } id ? state.Store.GetById(id) : null)
            .FirstOrDefault(b => b?.EnvironmentType == "Ruin");
        Assert.NotNull(ruin);
        var names = ruin!.Grid.Cells.Where(c => !c.IsEmpty).Select(c => c.Stack!.ItemType.Name).ToList();
        Assert.Contains("Scrawled Note", names);
        Assert.Contains("Quiet Recipe", names);
        Assert.Contains("Eye Core", names);

        var recipe = ruin.Grid.Cells.Select(c => c.Stack).First(s => s?.ItemType.Name == "Quiet Recipe");
        Assert.Equal(GameInitializer.AnotherQuiet1RecipeId, recipe!.GetString(GameState.RecipeItemProperty));
        var core = ruin.Grid.Cells.Select(c => c.Stack).First(s => s?.ItemType.Name == "Eye Core");
        Assert.Equal("eye", core!.GetString(FeatureSlotFrame.GlyphProperty));
    }

    // ==================== View-model projection ====================

    [Fact]
    public void ViewModel_EnvFields_ProjectPaletteAndGlyph()
    {
        var c = World(Grass());
        // Ordinary bag: default palette, empty glyph.
        var vm0 = ViewModelSerializer.Serialize(c.Session);
        Assert.Equal("Default", (string)vm0["env"]!["palette"]!);
        Assert.Equal("", (string)vm0["env"]!["glyph"]!);

        // A wilderness bag projects Dust + the Quiet+ glyph slug.
        var state = DemoState();
        var session = GameSession.New(state, ImmutableArray<Recipe>.Empty, TickMode.Rogue);
        // Enter the wilderness at cell 10 so it becomes the active bag.
        session = session.MoveCursor(state, Position.FromIndex(10, state.RootBag.Grid.Columns)).ExecuteEnterBag();
        var vm = ViewModelSerializer.Serialize(session);
        Assert.Equal("Quiet 1", (string)vm["activeBag"]!);
        Assert.Equal("Dust", (string)vm["env"]!["palette"]!);
        Assert.Equal("quiet-positive", (string)vm["env"]!["glyph"]!);
    }

    [Fact]
    public void ViewModel_KnownRecipes_EmptyThenListsLearnedRecipe()
    {
        var recipe = new ItemStack(RecipeType, 1)
            .WithProperty(GameState.RecipeItemProperty, new StringValue("another-quiet-1"));
        var c = World(new Cell(Stack: recipe));
        Assert.Empty((System.Text.Json.Nodes.JsonArray)ViewModelSerializer.Serialize(c.Session)["knownRecipes"]!);

        c.HandleKey(GameKey.Primary); // learn it
        var arr = (System.Text.Json.Nodes.JsonArray)ViewModelSerializer.Serialize(c.Session)["knownRecipes"]!;
        Assert.Single(arr);
        Assert.Equal("another-quiet-1", (string)arr[0]!);
    }
}
