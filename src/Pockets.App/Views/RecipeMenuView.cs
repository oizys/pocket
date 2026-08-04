using System.Text;
using Pockets.Core.Models;
using Terminal.Gui;

namespace Pockets.App.Views;

/// <summary>
/// The modal recipe menu (playtest feature, 2026-08-04): a real centered modal that REPLACES the old
/// R-to-cycle affordance. Drawn only while <see cref="GameSession.RecipeMenu"/> is open (GameView gates
/// visibility on the Core state). Lists every recipe the facility can build, with the selected row
/// marked; ↑/↓ move the selection, Enter sets it, Esc/Q closes. Pure renderer — the list, the selection,
/// and the facility name all come from Core (so the parity stream and this render can never diverge).
///
/// Aaron reversed the old no-modal-dialogs rule for this (see design/parity-drift-report.md and
/// design/tui-redesign.md #20): a modal is the honest shape for "pick one from a list."
/// </summary>
public class RecipeMenuView : FrameView
{
    private readonly Label _body;

    public RecipeMenuView()
    {
        Title = "Recipe";
        // Centered modal box.
        X = Pos.Center();
        Y = Pos.Center();
        Width = 40;
        Height = Dim.Sized(12);

        _body = new Label("")
        {
            X = 1,
            Y = 0,
            Width = Dim.Fill(1),
            Height = Dim.Fill(1)
        };
        Add(_body);
    }

    /// <summary>Renders the menu for the given state, or hides it when there is no open menu.</summary>
    public void Render(RecipeMenuState? menu)
    {
        if (menu is null)
        {
            Visible = false;
            SetNeedsDisplay();
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"Craft at the {menu.FacilityEnvironment}:");
        sb.AppendLine();
        if (menu.RecipeNames.IsEmpty)
        {
            sb.AppendLine("  (no recipes known yet)");
        }
        else
        {
            for (var i = 0; i < menu.RecipeNames.Length; i++)
            {
                var marker = i == menu.SelectedIndex ? "▸ " : "  ";
                sb.AppendLine($"{marker}{menu.RecipeNames[i]}");
            }
        }
        sb.AppendLine();
        sb.Append("↑/↓ select · Enter set · Esc close");

        _body.Text = sb.ToString();
        Visible = true;
        SetNeedsDisplay();
    }
}
