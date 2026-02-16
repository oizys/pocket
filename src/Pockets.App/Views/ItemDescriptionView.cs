using Terminal.Gui;
using Pockets.Core.Models;

namespace Pockets.App.Views;

/// <summary>
/// Displays name, category, count, and description for the item at the cursor.
/// </summary>
public class ItemDescriptionView : FrameView
{
    private readonly Label _content;

    public ItemDescriptionView()
    {
        Title = "Item";
        X = 0;
        Width = Dim.Fill();
        Height = Dim.Fill();

        _content = new Label("")
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill()
        };
        Add(_content);
    }

    public void UpdateState(GameState state)
    {
        var cell = state.CurrentCell;
        if (cell.IsEmpty)
        {
            _content.Text = "(empty)";
        }
        else
        {
            var stack = cell.Stack!;
            var lines = new List<string>
            {
                stack.ItemType.Name,
                $"Category: {stack.ItemType.Category}",
                stack.ItemType.IsStackable
                    ? $"Count: {stack.Count} / {stack.ItemType.EffectiveMaxStackSize}"
                    : "Unique item"
            };
            if (!string.IsNullOrEmpty(stack.ItemType.Description))
                lines.Add($"\n{stack.ItemType.Description}");
            _content.Text = string.Join("\n", lines);
        }
        SetNeedsDisplay();
    }
}
