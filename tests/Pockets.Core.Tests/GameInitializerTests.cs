using System.Collections.Immutable;
using Pockets.Core.Models;

namespace Pockets.Core.Tests;

public class GameInitializerTests
{
    [Fact]
    public void CreateRandomStage1Game_ReturnsValidGameState()
    {
        var itemTypes = CreateTestItemTypes();
        var random = new Random(123);

        var gameState = GameInitializer.CreateRandomStage1Game(itemTypes, random);

        Assert.Equal(8, gameState.RootBag.Grid.Columns);
        Assert.Equal(4, gameState.RootBag.Grid.Rows);
        Assert.Equal(new Position(0, 0), gameState.Cursor.Position);
        Assert.Equal(itemTypes, gameState.ItemTypes);
    }

    [Fact]
    public void CreateRandomStage1Game_PlacesItemsInGrid()
    {
        var itemTypes = CreateTestItemTypes();

        for (int seed = 0; seed < 20; seed++)
        {
            var gameState = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));
            var nonEmptyCells = gameState.RootBag.Grid.Cells.Count(c => !c.IsEmpty);

            Assert.True(nonEmptyCells >= 1, $"Seed {seed}: expected items in grid");
            Assert.True(nonEmptyCells <= 10, $"Seed {seed}: too many cells filled ({nonEmptyCells})");
        }
    }

    [Fact]
    public void CreateRandomStage1Game_StackableItemsHaveValidCounts()
    {
        var itemTypes = CreateTestItemTypes();

        for (int seed = 0; seed < 20; seed++)
        {
            var gameState = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));

            var stackableCells = gameState.RootBag.Grid.Cells
                .Where(c => !c.IsEmpty && c.Stack!.ItemType.IsStackable);

            foreach (var cell in stackableCells)
            {
                Assert.InRange(cell.Stack!.Count, 1, cell.Stack.ItemType.EffectiveMaxStackSize);
            }
        }
    }

    [Fact]
    public void CreateRandomStage1Game_UniqueItemsAlwaysHaveCount1()
    {
        var itemTypes = CreateTestItemTypes();

        for (int seed = 0; seed < 20; seed++)
        {
            var gameState = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));

            var uniqueCells = gameState.RootBag.Grid.Cells
                .Where(c => !c.IsEmpty && !c.Stack!.ItemType.IsStackable);

            Assert.All(uniqueCells, cell => Assert.Equal(1, cell.Stack!.Count));
        }
    }

    [Fact]
    public void CreateRandomStage1Game_DeterministicWithSameSeed()
    {
        var itemTypes = CreateTestItemTypes();
        const int seed = 42;

        var gameState1 = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));
        var gameState2 = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));

        for (int i = 0; i < gameState1.RootBag.Grid.Cells.Length; i++)
        {
            var cell1 = gameState1.RootBag.Grid.Cells[i];
            var cell2 = gameState2.RootBag.Grid.Cells[i];

            Assert.Equal(cell1.IsEmpty, cell2.IsEmpty);
            if (!cell1.IsEmpty)
            {
                Assert.Equal(cell1.Stack!.ItemType.Name, cell2.Stack!.ItemType.Name);
                Assert.Equal(cell1.Stack!.Count, cell2.Stack!.Count);
            }
        }
    }

    [Fact]
    public void CreateRandomStage1Game_UsesVariousItemTypes()
    {
        var itemTypes = CreateTestItemTypes();
        var usedNames = new HashSet<string>();

        for (int seed = 0; seed < 100; seed++)
        {
            var gameState = GameInitializer.CreateRandomStage1Game(itemTypes, new Random(seed));

            foreach (var cell in gameState.RootBag.Grid.Cells.Where(c => !c.IsEmpty))
            {
                usedNames.Add(cell.Stack!.ItemType.Name);
            }
        }

        Assert.Equal(itemTypes.Length, usedNames.Count);
    }

    private static ImmutableArray<ItemType> CreateTestItemTypes()
    {
        return ImmutableArray.Create(
            new ItemType("Iron Ore", Category.Material, IsStackable: true, MaxStackSize: 20),
            new ItemType("Gold Ore", Category.Material, IsStackable: true, MaxStackSize: 20),
            new ItemType("Health Potion", Category.Medicine, IsStackable: true, MaxStackSize: 10),
            new ItemType("Mana Potion", Category.Medicine, IsStackable: true, MaxStackSize: 10),
            new ItemType("Wood Plank", Category.Material, IsStackable: true, MaxStackSize: 50),
            new ItemType("Stone Brick", Category.Structure, IsStackable: true, MaxStackSize: 30),
            new ItemType("Arrow Bundle", Category.Weapon, IsStackable: true, MaxStackSize: 100),
            new ItemType("Iron Sword", Category.Weapon, IsStackable: false),
            new ItemType("Steel Axe", Category.Tool, IsStackable: false),
            new ItemType("Magic Staff", Category.Weapon, IsStackable: false),
            new ItemType("Leather Bag", Category.Bag, IsStackable: false),
            new ItemType("Ancient Amulet", Category.Misc, IsStackable: false)
        );
    }
}
