using System.Collections.Immutable;
using Godot;
using Pockets.Core.Models;
using Pockets.Core.Rendering;
using Pockets.Realization;

namespace Pockets.Godot.Scripts;

/// <summary>
/// Custom-drawn Control for the inventory grid. Renders all cells in a single _Draw() call
/// with colored backgrounds, borders, cursor highlight, and abbreviated item text.
/// Slice 2.5: the drawing is a pure function of (grid, poses). Cells draw their frame and border; each stack draws as
/// a tile at its pose (PoseProjection: cell + Sub/16 · cellSize, Height as −y), its content read from the grid at the
/// pose's rest cell. The cursor highlight follows the body pose in world bags and a live cursor glide in inventory bags.
/// </summary>
public partial class GridDrawControl : Control
{
    private Grid _grid = Grid.Create(8, 4);
    private Position _cursorPos = new(0, 0);
    private int _hoveredCell = -1;
    private System.Func<int, FullnessPip?>? _pipResolver;
    private ImmutableArray<DrawnPose>? _poses;   // Slice 2.5: null until the first SetPoses → rest poses of the grid
    private System.Guid _bag;
    private LocationId? _cursorLoc;

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

    /// <summary>
    /// Sets the sampled poses (Slice 2.5) and the bag this panel shows; only that bag's poses are drawn.
    /// <paramref name="cursorLoc"/> is the location whose body or cursor glide moves the highlight (null: the grid
    /// cursor cell only, e.g. the hand).
    /// </summary>
    public void SetPoses(ImmutableArray<DrawnPose> poses, System.Guid bag, LocationId? cursorLoc)
    {
        _poses = poses;
        _bag = bag;
        _cursorLoc = cursorLoc;
        QueueRedraw();
    }

    /// <summary>
    /// Sets the fullness-pip resolver (Slice 5): a per-cell-index lookup of the Core-computed pip
    /// state, or null to draw no pips. Both frontends render only from this Core state.
    /// </summary>
    public void SetPips(System.Func<int, FullnessPip?>? pipResolver)
    {
        _pipResolver = pipResolver;
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

        // Cells: background, border, frame marker, hover. The stacks are drawn below, at their poses.
        for (int i = 0; i < _grid.Cells.Length; i++)
        {
            int col = i % Columns;
            int row = i / Columns;
            var rect = new Rect2(col * cw, row * ch, cw, ch);
            var cell = _grid.Cells[i];

            DrawRect(rect, EmptyBgColor);
            DrawRect(rect, GetBorderColor(cell), false, 1.0f);

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

            if (i == _hoveredCell)
                DrawRect(rect, new Color(1f, 1f, 1f, 0.1f));
        }

        // Stacks, one tile per pose in this bag: stationary first, moving on top (PoseProjection.ForPanel).
        var poses = _poses ?? PoseProjection.Rest(_grid, _bag);
        var drawn = PoseProjection.ForPanel(poses, _bag, Columns);
        foreach (var d in drawn)
        {
            if (PoseProjection.ContentCell(d) is not { } i || i < 0 || i >= _grid.Cells.Length) continue;
            var cell = _grid.Cells[i];
            if (cell.IsEmpty) continue;

            var (x, y) = PoseProjection.TileOrigin(d.Pose, (int)cw, (int)ch);
            var rect = new Rect2(x + 1, y + 1, cw - 2, ch - 2);
            DrawRect(rect, GetCategoryBgColor(cell));

            var stack = cell.Stack!;
            var abbr = RenderHelpers.AbbreviateName(stack.ItemType.Name);
            var countText = stack.ItemType.IsStackable ? $"x{stack.Count}" : "";

            // Name line (centered)
            var nameSize = font.GetStringSize(abbr, HorizontalAlignment.Left, -1, fontSize);
            var nameX = x + (cw - nameSize.X) / 2f;
            var nameY = y + fontSize + 6f;
            DrawString(font, new Vector2(nameX, nameY), abbr,
                HorizontalAlignment.Left, -1, fontSize, Colors.White);

            // Count line (centered, below name)
            if (countText.Length > 0)
            {
                var countSize = font.GetStringSize(countText, HorizontalAlignment.Left, -1, fontSize);
                var countX = x + (cw - countSize.X) / 2f;
                var countY = nameY + fontSize + 4f;
                DrawString(font, new Vector2(countX, countY), countText,
                    HorizontalAlignment.Left, -1, fontSize, new Color(0.8f, 0.8f, 0.8f));
            }

            // Fullness pip (Slice 5): drawn top-right on every bag cell once the eye core is slotted.
            // Empty ○ / partial ◐ / full ● — the same glyphs the TUI paints, from the same Core state.
            if (_pipResolver?.Invoke(i) is { } pip)
            {
                var pipText = Fullness.Glyph(pip).ToString();
                var pipPos = new Vector2(x + cw - 14f, y + fontSize + 2f);
                DrawString(font, pipPos, pipText, HorizontalAlignment.Left, -1, fontSize, Colors.White);
            }
        }

        // Cursor highlight: the body pose (world bag) or a live glide (inventory bag), else the grid cursor cell.
        var cursorPose = _cursorLoc is { } loc ? PoseProjection.CursorOf(poses, loc, _bag) : null;
        var (cx, cy) = cursorPose is { } cp
            ? PoseProjection.TileOrigin(cp, (int)cw, (int)ch)
            : ((int)(_cursorPos.Col * cw), (int)(_cursorPos.Row * ch));
        var cursorRect = new Rect2(cx, cy, cw, ch);
        DrawRect(cursorRect, new Color(0.4f, 0.4f, 0.4f), false, 3.0f);
        var inner = new Rect2(cursorRect.Position + new Vector2(2, 2), cursorRect.Size - new Vector2(4, 4));
        DrawRect(inner, new Color(1f, 1f, 0f, 0.8f), false, 2.0f);
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

    private static readonly Color EmptyBgColor = new(0.12f, 0.12f, 0.15f);

    private static Color GetCategoryBgColor(Cell cell)
    {
        if (cell.IsEmpty)
            return EmptyBgColor;

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
