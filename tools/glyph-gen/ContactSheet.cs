using Pockets.Core.Cosmology.Glyphs;
using SkiaSharp;

namespace Pockets.GlyphGen;

/// <summary>
/// Renders the 12 glyphs into a single labeled contact-sheet PNG with a light
/// panel and a dark panel, for reviewing the set against Aaron's sketches on a
/// phone. Glyphs are drawn straight from the shared <see cref="Primitive"/>
/// geometry (same source as the SVGs), so the sheet and the SVG files agree.
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

    private const float CellW = GlyphBox + 2 * CellPadX;
    private const float CellH = GlyphBox + CellPadTop + LabelH;
    private const float GridW = Cols * CellW;

    private sealed record Theme(SKColor Panel, SKColor Stroke, SKColor Label, SKColor Guide);

    private static readonly Theme Light = new(
        Panel: new SKColor(250, 250, 248),
        Stroke: new SKColor(22, 22, 26),
        Label: new SKColor(70, 70, 78),
        Guide: new SKColor(0, 0, 0, 16));

    private static readonly Theme Dark = new(
        Panel: new SKColor(20, 20, 24),
        Stroke: new SKColor(232, 232, 236),
        Label: new SKColor(150, 150, 160),
        Guide: new SKColor(255, 255, 255, 20));

    public static void Render(IReadOnlyList<GlyphSpec> glyphs, GlyphParams p, string outPath)
    {
        int rows = (glyphs.Count + Cols - 1) / Cols;
        float sectionH = SectionTitleH + rows * CellH;
        int width = (int)(GridW + 2 * Margin);
        int height = (int)(Margin + HeaderH + sectionH + SectionGap + sectionH + Margin);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(236, 236, 232));

        using var typeface = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        using var typefaceBold =
            SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ?? typeface;

        // Main header, auto-sized to fit the content width.
        using (var header = new SKPaint
        {
            Color = new SKColor(30, 30, 34), IsAntialias = true,
            TextSize = 32, Typeface = typefaceBold
        })
        {
            const string headerText = "Pockets · Entropy Glyphs — 8 basis + 4 parent (v1)";
            while (header.MeasureText(headerText) > GridW && header.TextSize > 12)
                header.TextSize -= 1;
            canvas.DrawText(headerText, Margin, Margin + 44, header);
        }

        float y = Margin + HeaderH;
        y = DrawSection(canvas, glyphs, p, Light, "LIGHT", y, typeface, typefaceBold);
        y += SectionGap;
        DrawSection(canvas, glyphs, p, Dark, "DARK", y, typeface, typefaceBold);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(outPath);
        data.SaveTo(fs);
    }

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
        using (var stroke = new SKPaint
        {
            Color = theme.Stroke, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)p.StrokeWidth,
            StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        })
        {
            foreach (var prim in glyph.Primitives)
                using (var path = ToPath(prim))
                    canvas.DrawPath(path, stroke);
        }
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
