using Godot;
using Pockets.Core.Models;
using Pockets.Core.Rendering;

namespace Pockets.Godot.Scripts;

/// <summary>
/// Custom-drawn Control for the inventory grid. Renders all cells in a single _Draw() call
/// with colored backgrounds, borders, cursor highlight, and abbreviated item text.
/// </summary>
public partial class GridDrawControl : Control
{
    private Grid _grid = Grid.Create(8, 4);
    private Position _cursorPos = new(0, 0);
    private int _hoveredCell = -1;

    public float CellWidth { get; set; } = 80f;
    public float CellHeight { get; set; } = 56f;
    public int Columns => _grid.Columns;
    public int Rows => _grid.Rows;

    [Signal]
    public delegate void CellClickedEventHandler(int index, int button);

    [Signal]
    public delegate void CellHoveredEventHandler(int index);

    public void SetState(Grid grid, Position cursorPos)
    {
        _grid = grid;
        _cursorPos = cursorPos;
        CustomMinimumSize = new Vector2(Columns * CellWidth, Rows * CellHeight);
        QueueRedraw();
    }

    public override void _Ready()
    {
        MouseFilter = MouseFilterEnum.Stop;
    }

    public override void _Draw()
    {
        var font = ThemeDB.FallbackFont;
        int fontSize = 14;
        float cw = CellWidth;
        float ch = CellHeight;

        for (int i = 0; i < _grid.Cells.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            var rect = new Rect2(col * cw, row * ch, cw, ch);
            var cell = _grid.Cells[i];
            bool isCursor = row == _cursorPos.Row && col == _cursorPos.Col;

            // Background
            var bgColor = GetCategoryBgColor(cell);
            DrawRect(rect, bgColor);

            // Border
            var borderColor = GetBorderColor(cell);
            DrawRect(rect, borderColor, false, isCursor ? 3.0f : 1.0f);

            // Cursor highlight (bright outline)
            if (isCursor)
            {
                var inner = new Rect2(rect.Position + new Vector2(2, 2), rect.Size - new Vector2(4, 4));
                DrawRect(inner, new Color(1f, 1f, 0f, 0.8f), false, 2.0f);
            }

            // Hover highlight
            if (i == _hoveredCell && !isCursor)
            {
                DrawRect(rect, new Color(1f, 1f, 1f, 0.1f));
            }

            // Cell text
            if (!cell.IsEmpty)
            {
                var stack = cell.Stack!;
                var abbr = RenderHelpers.AbbreviateName(stack.ItemType.Name);
                var countText = stack.ItemType.IsStackable ? $"x{stack.Count}" : "";

                // Name line (centered)
                var nameSize = font.GetStringSize(abbr, HorizontalAlignment.Left, -1, fontSize);
                var nameX = rect.Position.X + (cw - nameSize.X) / 2f;
                var nameY = rect.Position.Y + fontSize + 6f;
                DrawString(font, new Vector2(nameX, nameY), abbr,
                    HorizontalAlignment.Left, -1, fontSize, Colors.White);

                // Count line (centered, below name)
                if (countText.Length > 0)
                {
                    var countSize = font.GetStringSize(countText, HorizontalAlignment.Left, -1, fontSize);
                    var countX = rect.Position.X + (cw - countSize.X) / 2f;
                    var countY = nameY + fontSize + 4f;
                    DrawString(font, new Vector2(countX, countY), countText,
                        HorizontalAlignment.Left, -1, fontSize, new Color(0.8f, 0.8f, 0.8f));
                }
            }

            // Frame indicator (input/output slot marker)
            if (cell.HasFrame)
            {
                var frameColor = cell.Frame switch
                {
                    InputSlotFrame => new Color(1f, 1f, 0f),  // yellow
                    OutputSlotFrame => new Color(0f, 1f, 0f),  // green
                    _ => Colors.White
                };
                var markerSize = 6f;
                var markerPos = new Vector2(rect.Position.X + cw - markerSize - 3, rect.Position.Y + 3);
                DrawRect(new Rect2(markerPos, new Vector2(markerSize, markerSize)), frameColor);
            }
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is InputEventMouseButton mb && mb.Pressed)
        {
            int index = HitTest(mb.Position);
            if (index >= 0)
                EmitSignal(SignalName.CellClicked, index, (int)mb.ButtonIndex);
        }
        else if (@event is InputEventMouseMotion motion)
        {
            int newHover = HitTest(motion.Position);
            if (newHover != _hoveredCell)
            {
                _hoveredCell = newHover;
                EmitSignal(SignalName.CellHovered, newHover);
                QueueRedraw();
            }
        }
    }

    private int HitTest(Vector2 localPos)
    {
        float cw = CellWidth;
        float ch = CellHeight;
        int col = (int)(localPos.X / cw);
        int row = (int)(localPos.Y / ch);
        if (col < 0 || col >= Columns || row < 0 || row >= Rows)
            return -1;
        return row * Columns + col;
    }

    private static Color GetCategoryBgColor(Cell cell)
    {
        if (cell.IsEmpty)
            return new Color(0.12f, 0.12f, 0.15f);

        return cell.Stack!.ItemType.Category switch
        {
            Category.Material   => new Color(0.2f, 0.2f, 0.2f),
            Category.Weapon     => new Color(0.3f, 0.12f, 0.12f),
            Category.Structure  => new Color(0.25f, 0.18f, 0.1f),
            Category.Medicine   => new Color(0.1f, 0.25f, 0.1f),
            Category.Tool       => new Color(0.1f, 0.1f, 0.3f),
            Category.Bag        => new Color(0.25f, 0.1f, 0.25f),
            Category.Consumable => new Color(0.1f, 0.22f, 0.25f),
            _                   => new Color(0.15f, 0.15f, 0.15f),
        };
    }

    private static Color GetBorderColor(Cell cell)
    {
        if (cell.HasFrame)
            return cell.Frame switch
            {
                InputSlotFrame  => new Color(1f, 1f, 0f, 0.6f),
                OutputSlotFrame => new Color(0f, 1f, 0f, 0.6f),
                _               => new Color(0.4f, 0.4f, 0.4f),
            };

        return new Color(0.4f, 0.4f, 0.4f);
    }
}
