using System.Collections.Immutable;
using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Glyphs;
using SkiaSharp;

namespace Pockets.GlyphGen;

/// <summary>
/// Renders the 12 glyphs into a single labeled contact-sheet PNG with a light
/// panel and a dark panel, for reviewing the set against Aaron's sketches on a
/// phone. Glyphs are drawn straight from the shared <see cref="Primitive"/>
/// geometry (same source as the SVGs), so the sheet and the SVG files agree.
///
/// <para>
/// v2 adds a <b>family</b> section (one panel per quadrant) that overlays the
/// parent wifi-rainbow on its two children — the "+" horizontal staircase and the
/// "−" vertical staircase — positioned so each arc end meets the child line it
/// bridges. That coincidence is the thing Aaron asked to see, and it is asserted
/// exactly in <c>GlyphGeometryTests.Parent_ArcEnds_AlignWithChildLineGeometry</c>.
/// </para>
/// </summary>
public static class ContactSheet
{
    private const int Cols = 4;
    private const float GlyphBox = 190;
    private const float CellPadX = 24;
    private const float CellPadTop = 16;
    private const float LabelH = 62;
    private const float Margin = 36;
    private const float HeaderH = 74;
    private const float SectionTitleH = 46;
    private const float SectionGap = 24;

    // Family panels: bigger boxes (the overlay needs room), two per row.
    private const int FamCols = 2;
    private const float FamBox = 300;
    private const float FamPadX = 40;
    private const float FamPadTop = 18;
    private const float FamLabelH = 58;
    private const float FamCellW = FamBox + 2 * FamPadX;
    private const float FamCellH = FamBox + FamPadTop + FamLabelH;

    private const float CellW = GlyphBox + 2 * CellPadX;
    private const float CellH = GlyphBox + CellPadTop + LabelH;
    private const float GridW = Cols * CellW;

    private sealed record Theme(
        SKColor Panel, SKColor Stroke, SKColor Label, SKColor Guide,
        SKColor PlusChild, SKColor MinusChild, SKColor Dot);

    private static readonly Theme Light = new(
        Panel: new SKColor(250, 250, 248),
        Stroke: new SKColor(22, 22, 26),
        Label: new SKColor(70, 70, 78),
        Guide: new SKColor(0, 0, 0, 16),
        PlusChild: new SKColor(198, 96, 40),     // warm — the "+" horizontal child
        MinusChild: new SKColor(46, 118, 168),   // cool — the "−" vertical child
        Dot: new SKColor(22, 22, 26));

    private static readonly Theme Dark = new(
        Panel: new SKColor(20, 20, 24),
        Stroke: new SKColor(232, 232, 236),
        Label: new SKColor(150, 150, 160),
        Guide: new SKColor(255, 255, 255, 20),
        PlusChild: new SKColor(232, 150, 92),
        MinusChild: new SKColor(120, 180, 224),
        Dot: new SKColor(244, 244, 248));

    public static void Render(IReadOnlyList<GlyphSpec> glyphs, GlyphParams p, string outPath)
    {
        int rows = (glyphs.Count + Cols - 1) / Cols;
        float gridSectionH = SectionTitleH + rows * CellH;

        int famRows = (EntropyMatrix.Quadrants.Length + FamCols - 1) / FamCols;
        float famGridW = FamCols * FamCellW;
        float famSectionH = SectionTitleH + famRows * FamCellH;

        int width = (int)(Math.Max(GridW, famGridW) + 2 * Margin);
        int height = (int)(
            Margin + HeaderH
            + famSectionH + SectionGap                 // families (light)
            + famSectionH + SectionGap                 // families (dark)
            + gridSectionH + SectionGap                // 12-grid (light)
            + gridSectionH + Margin);                  // 12-grid (dark)

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(236, 236, 232));

        using var typeface = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        using var typefaceBold =
            SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ?? typeface;

        // Main header, auto-sized to fit the content width.
        float contentW = Math.Max(GridW, famGridW);
        using (var header = new SKPaint
        {
            Color = new SKColor(30, 30, 34), IsAntialias = true,
            TextSize = 32, Typeface = typefaceBold
        })
        {
            const string headerText =
                "Pockets · Entropy Glyphs — parent arcs bridge their children (v2)";
            while (header.MeasureText(headerText) > contentW && header.TextSize > 12)
                header.TextSize -= 1;
            canvas.DrawText(headerText, Margin, Margin + 44, header);
        }

        float y = Margin + HeaderH;
        y = DrawFamilySection(canvas, p, Light,
            "FAMILIES · LIGHT — each arc end meets the child line it bridges",
            y, typeface, typefaceBold);
        y += SectionGap;
        y = DrawFamilySection(canvas, p, Dark, "FAMILIES · DARK", y, typeface, typefaceBold);
        y += SectionGap;
        y = DrawSection(canvas, glyphs, p, Light, "ALL 12 · LIGHT", y, typeface, typefaceBold);
        y += SectionGap;
        DrawSection(canvas, glyphs, p, Dark, "ALL 12 · DARK", y, typeface, typefaceBold);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(outPath);
        data.SaveTo(fs);
    }

    // ---- Family (parent + two children) overlay section ----

    private static float DrawFamilySection(
        SKCanvas canvas, GlyphParams p, Theme theme, string title, float top,
        SKTypeface typeface, SKTypeface typefaceBold)
    {
        var quadrants = EntropyMatrix.Quadrants;
        int rows = (quadrants.Length + FamCols - 1) / FamCols;
        float sectionH = SectionTitleH + rows * FamCellH;
        float gridW = FamCols * FamCellW;

        using (var titlePaint = new SKPaint
        {
            Color = new SKColor(90, 90, 96), IsAntialias = true,
            TextSize = 20, Typeface = typefaceBold
        })
        {
            canvas.DrawText(title, Margin, top + 28, titlePaint);
        }

        float panelTop = top + SectionTitleH;
        var panelRect = new SKRect(Margin, panelTop, Margin + gridW, panelTop + rows * FamCellH);
        using (var panel = new SKPaint { Color = theme.Panel, IsAntialias = true })
            canvas.DrawRoundRect(panelRect, 14, 14, panel);

        for (int i = 0; i < quadrants.Length; i++)
        {
            int col = i % FamCols, row = i / FamCols;
            float cx = Margin + col * FamCellW;
            float cy = panelTop + row * FamCellH;
            DrawFamilyCell(canvas, quadrants[i], p, theme, cx, cy, typeface);
        }

        return top + sectionH;
    }

    private static void DrawFamilyCell(
        SKCanvas canvas, Quadrant quadrant, GlyphParams p, Theme theme,
        float cellX, float cellY, SKTypeface typeface)
    {
        float boxX = cellX + FamPadX;
        float boxY = cellY + FamPadTop;

        using (var guide = new SKPaint
        {
            Color = theme.Guide, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1
        })
        {
            canvas.DrawRoundRect(new SKRect(boxX, boxY, boxX + FamBox, boxY + FamBox), 10, 10, guide);
        }

        var (arcs, plus, minus) = FamilyGeometry(quadrant, p);

        canvas.Save();
        canvas.Translate(boxX, boxY);
        float scale = FamBox / (float)p.ViewBox;
        canvas.Scale(scale);

        // Children first (so the parent arcs sit on top at the joins).
        DrawStrokes(canvas, plus, theme.PlusChild, (float)p.StrokeWidth * 0.72f);
        DrawStrokes(canvas, minus, theme.MinusChild, (float)p.StrokeWidth * 0.72f);
        DrawStrokes(canvas, arcs, theme.Stroke, (float)p.StrokeWidth);

        // Coincidence dots: where each arc end meets its child line's tip.
        using (var dot = new SKPaint { Color = theme.Dot, IsAntialias = true, Style = SKPaintStyle.Fill })
        {
            foreach (var a in arcs.Cast<Arc>())
            {
                canvas.DrawCircle((float)a.Start.X, (float)a.Start.Y, 2.4f, dot);
                canvas.DrawCircle((float)a.End.X, (float)a.End.Y, 2.4f, dot);
            }
        }
        canvas.Restore();

        // Caption: parent = + flavor ⌣ − flavor.
        var plusInfo = EntropyMatrix.Positive(quadrant);
        var minusInfo = EntropyMatrix.Negative(quadrant);
        using var strong = new SKPaint
        {
            Color = theme.Label, IsAntialias = true, TextSize = 16,
            Typeface = typeface, TextAlign = SKTextAlign.Center
        };
        using var weak = new SKPaint
        {
            Color = theme.Label.WithAlpha(190), IsAntialias = true, TextSize = 13,
            Typeface = typeface, TextAlign = SKTextAlign.Center
        };
        float textCx = boxX + FamBox / 2;
        float textY = boxY + FamBox + 24;
        canvas.DrawText($"{quadrant} parent", textCx, textY, strong);
        canvas.DrawText($"+ {plusInfo.Flavor}   ·   − {minusInfo.Flavor}", textCx, textY + 20, weak);
    }

    // The overlay geometry for one quadrant: the parent arcs plus its two children
    // positioned so each child line's tip lands on the arc end that bridges it. Built
    // canonically then flipped by the quadrant's parent orientation, so all four
    // families are exact flips of one construction (same as the glyphs themselves).
    private static (ImmutableArray<Primitive> Arcs, List<Segment> Plus, List<Segment> Minus)
        FamilyGeometry(Quadrant quadrant, GlyphParams p)
    {
        var orientation = EntropyMatrix.ParentOrientation(quadrant);
        double center = p.ViewBox / 2;
        double anchor = center + p.ParentAnchorOffset;
        var rows = GlyphGeometry.BasisRows(center, p);
        double[] lengths =
        {
            p.BasisLongLength,
            p.BasisLongLength * p.BasisMidRatio,
            p.BasisLongLength * p.BasisShortRatio
        };

        var plus = new List<Segment>();
        var minus = new List<Segment>();
        for (int i = 0; i < rows.Length; i++)
        {
            double v = rows[i], len = lengths[i];
            // "+" child: horizontal line at height v, right tip at the arc end (anchor, v).
            plus.Add(new Segment(new Pt(anchor - len, v), new Pt(anchor, v))
                .Transform(orientation, center) as Segment ?? throw new InvalidOperationException());
            // "−" child: vertical line at x = v, bottom tip at the arc start (v, anchor).
            minus.Add(new Segment(new Pt(v, anchor - len), new Pt(v, anchor))
                .Transform(orientation, center) as Segment ?? throw new InvalidOperationException());
        }

        return (GlyphGeometry.Parent(orientation, p), plus, minus);
    }

    private static void DrawStrokes(
        SKCanvas canvas, IEnumerable<Primitive> prims, SKColor color, float strokeWidth)
    {
        using var paint = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = strokeWidth, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        foreach (var prim in prims)
            using (var path = ToPath(prim))
                canvas.DrawPath(path, paint);
    }

    // ---- The reference 12-glyph grid (unchanged from v1) ----

    private static float DrawSection(
        SKCanvas canvas, IReadOnlyList<GlyphSpec> glyphs, GlyphParams p, Theme theme,
        string title, float top, SKTypeface typeface, SKTypeface typefaceBold)
    {
        int rows = (glyphs.Count + Cols - 1) / Cols;
        float sectionH = SectionTitleH + rows * CellH;

        using (var titlePaint = new SKPaint
        {
            Color = new SKColor(90, 90, 96), IsAntialias = true,
            TextSize = 20, Typeface = typefaceBold
        })
        {
            canvas.DrawText(title, Margin, top + 28, titlePaint);
        }

        float panelTop = top + SectionTitleH;
        var panelRect = new SKRect(Margin, panelTop, Margin + GridW, panelTop + rows * CellH);
        using (var panel = new SKPaint { Color = theme.Panel, IsAntialias = true })
            canvas.DrawRoundRect(panelRect, 14, 14, panel);

        for (int i = 0; i < glyphs.Count; i++)
        {
            int col = i % Cols, row = i / Cols;
            float cx = Margin + col * CellW;
            float cy = panelTop + row * CellH;
            DrawCell(canvas, glyphs[i], p, theme, cx, cy, typeface);
        }

        return top + sectionH;
    }

    private static void DrawCell(
        SKCanvas canvas, GlyphSpec glyph, GlyphParams p, Theme theme,
        float cellX, float cellY, SKTypeface typeface)
    {
        float boxX = cellX + CellPadX;
        float boxY = cellY + CellPadTop;

        // Subtle guide box so each glyph reads as a distinct tile.
        using (var guide = new SKPaint
        {
            Color = theme.Guide, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1
        })
        {
            canvas.DrawRoundRect(new SKRect(boxX, boxY, boxX + GlyphBox, boxY + GlyphBox), 10, 10, guide);
        }

        // Draw the glyph, scaling canonical viewBox units into the guide box.
        canvas.Save();
        canvas.Translate(boxX, boxY);
        float scale = GlyphBox / (float)p.ViewBox;
        canvas.Scale(scale);
        DrawStrokes(canvas, glyph.Primitives, theme.Stroke, (float)p.StrokeWidth);
        canvas.Restore();

        // Two-line label under the glyph.
        var (line1, line2) = SplitTitle(glyph.Title);
        using var label = new SKPaint
        {
            Color = theme.Label, IsAntialias = true, TextSize = 15,
            Typeface = typeface, TextAlign = SKTextAlign.Center
        };
        float textCx = boxX + GlyphBox / 2;
        float textY = boxY + GlyphBox + 22;
        canvas.DrawText(line1, textCx, textY, label);
        if (line2.Length > 0)
            canvas.DrawText(line2, textCx, textY + 20, label);
    }

    // Convert a glyph primitive to an SKPath, mirroring SVG arc semantics so the
    // PNG matches the emitted SVG files.
    private static SKPath ToPath(Primitive prim)
    {
        var path = new SKPath();
        switch (prim)
        {
            case Segment s:
                path.MoveTo((float)s.A.X, (float)s.A.Y);
                path.LineTo((float)s.B.X, (float)s.B.Y);
                break;
            case Arc a:
                path.MoveTo((float)a.Start.X, (float)a.Start.Y);
                path.ArcTo(
                    new SKPoint((float)a.Radius, (float)a.Radius),
                    0,
                    a.LargeArcFlag == 1 ? SKPathArcSize.Large : SKPathArcSize.Small,
                    a.SweepFlag == 1 ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                    new SKPoint((float)a.End.X, (float)a.End.Y));
                break;
        }
        return path;
    }

    // "Quiet+ · Dust/Death · Right-Down" -> ("Quiet+ · Dust/Death", "Right-Down").
    private static (string, string) SplitTitle(string title)
    {
        var parts = title.Split(" · ");
        return parts.Length >= 3
            ? ($"{parts[0]} · {parts[1]}", string.Join(" · ", parts.Skip(2)))
            : (title, "");
    }
}
