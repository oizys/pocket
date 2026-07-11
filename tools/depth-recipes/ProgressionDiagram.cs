using Pockets.Core.Cosmology;
using Pockets.Core.Cosmology.Glyphs;
using Pockets.Core.Cosmology.Recipes;
using SkiaSharp;

namespace Pockets.DepthRecipes;

/// <summary>
/// Renders the depth-recipe progression graph as the nested-circle cosmology: the
/// Core at center, four quadrant arms fanning into their screen corners, depth as
/// radius (deeper = more nested = further out), within-zone recipe edges tracing
/// each arm, the tunable cross-zone semi-linearization arrows sweeping around the
/// circle, and hero-piece gates hung off their source nodes. The 12 entropy glyphs
/// (8 basis + 4 parent) are placed on the plate as the zone/quadrant iconography.
///
/// <para>Out-of-solution SkiaSharp tool (same posture as tools/glyph-gen); it only
/// draws what <see cref="RecipeBook"/> already computes.</para>
/// </summary>
public static class ProgressionDiagram
{
    private const float InnerR = 175;      // radius of depth 1
    private const float RingStep = 44;     // radius added per depth
    private const float FanSpread = 62;    // degrees an arm fans across its sector
    private const float NodeR = 15;
    private const float GateOffset = 96;   // how far a gate sits beyond its source node

    // Screen-space (y-down) center angle of each quadrant's corner.
    private static float CenterAngle(Quadrant q) => q switch
    {
        Quadrant.Quiet => 45,    // bottom-right
        Quadrant.Gloam => 135,   // bottom-left
        Quadrant.Flux => 225,    // top-left
        Quadrant.Jitter => 315,  // top-right
        _ => 0
    };

    private sealed record Palette(SKColor Positive, SKColor Negative, SKColor Arm);

    private static Palette PaletteFor(Quadrant q) => q switch
    {
        Quadrant.Quiet => new(new SKColor(198, 160, 92), new SKColor(120, 168, 92), new SKColor(150, 140, 110)),
        Quadrant.Gloam => new(new SKColor(150, 120, 205), new SKColor(92, 190, 205), new SKColor(130, 120, 165)),
        Quadrant.Flux => new(new SKColor(214, 128, 74), new SKColor(150, 186, 214), new SKColor(170, 140, 120)),
        Quadrant.Jitter => new(new SKColor(206, 96, 168), new SKColor(110, 110, 200), new SKColor(150, 120, 160)),
        _ => new(SKColors.Gray, SKColors.Gray, SKColors.Gray)
    };

    private static readonly SKColor Bg = new(18, 18, 24);
    private static readonly SKColor CoreColor = new(240, 236, 220);
    private static readonly SKColor CrossEdge = new(232, 196, 96);   // gold — semi-linearization
    private static readonly SKColor GateColor = new(236, 120, 120);  // hero gates
    private static readonly SKColor Ink = new(232, 232, 236);
    private static readonly SKColor Faint = new(255, 255, 255, 26);

    public static void Render(RecipeBook book, string outPath)
    {
        using var face = SKTypeface.FromFamilyName("DejaVu Sans") ?? SKTypeface.Default;
        using var faceBold = SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold) ?? face;

        // 1. Lay out node positions (center at origin), then measure bounds over the
        // nodes AND the outer label / gate anchors so nothing clips off the plate.
        var pos = LayOut(book);
        var extents = pos.Values.Concat(AnchorExtents(book, pos)).ToList();

        float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
        foreach (var p in extents)
        {
            minX = Math.Min(minX, p.X); maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y); maxY = Math.Max(maxY, p.Y);
        }
        const float pad = 110, topBand = 250;
        float offX = -minX + pad;
        float offY = -minY + topBand;
        int graphWidth = (int)(maxX - minX + 2 * pad);
        int height = (int)(maxY - minY + pad + topBand);

        // Ensure the canvas is at least wide enough for the title/subtitle band.
        int minWidth = (int)MeasureChromeWidth(faceBold, face) + 92;
        int width = Math.Max(graphWidth, minWidth);

        using var surface = SKSurface.Create(new SKImageInfo(width, height));
        var canvas = surface.Canvas;
        canvas.Clear(Bg);

        DrawTitleAndLegend(canvas, book, width, face, faceBold);

        canvas.Save();
        canvas.Translate(offX, offY);

        DrawDepthRings(canvas, book, face);
        DrawWithinZoneArms(canvas, book, pos);
        DrawCrossEdges(canvas, book, pos);
        DrawHeroGates(canvas, book, pos, face, faceBold);
        DrawNodes(canvas, book, pos, face);
        DrawGlyphs(canvas, book, face, faceBold);
        DrawCore(canvas, face, faceBold);

        canvas.Restore();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var fs = File.OpenWrite(outPath);
        data.SaveTo(fs);
    }

    // ---- layout ----

    private static Dictionary<ZoneDepth, SKPoint> LayOut(RecipeBook book)
    {
        var pos = new Dictionary<ZoneDepth, SKPoint>();
        foreach (var chain in book.Chains)
        {
            int n = chain.TotalDepths;
            float center = CenterAngle(chain.Quadrant);
            for (int d = 1; d <= n; d++)
            {
                float t = n == 1 ? 0.5f : (d - 1) / (float)(n - 1);
                float angle = center + (t - 0.5f) * FanSpread;
                float r = InnerR + (d - 1) * RingStep;
                pos[chain.NodeAt(d)] = Polar(angle, r);
            }
        }
        return pos;
    }

    private static SKPoint Polar(float angleDeg, float r)
    {
        double a = angleDeg * Math.PI / 180.0;
        return new SKPoint((float)(r * Math.Cos(a)), (float)(r * Math.Sin(a)));
    }

    // Extent points for the outer labels/glyphs and gate captions, so bounds include
    // everything the draw pass will actually paint (else the longest arm clips).
    private static IEnumerable<SKPoint> AnchorExtents(RecipeBook book, Dictionary<ZoneDepth, SKPoint> pos)
    {
        // The outermost depth ring's cardinal points, so the nested-circle cosmology
        // (and its west depth labels) render fully rather than clipping at the edges.
        float maxR = InnerR + (book.Chains.Max(c => c.TotalDepths) - 1) * RingStep;
        yield return new SKPoint(-maxR - 16, 0);
        yield return new SKPoint(maxR, 0);
        yield return new SKPoint(0, -maxR);
        yield return new SKPoint(0, maxR);

        foreach (var chain in book.Chains)
        {
            float outerR = InnerR + (chain.TotalDepths - 1) * RingStep + 118;
            var a = Polar(CenterAngle(chain.Quadrant), outerR);
            yield return new SKPoint(a.X - 90, a.Y - 60);   // glyph top-left
            yield return new SKPoint(a.X + 90, a.Y + 96);   // quadrant name below
        }
        foreach (var gate in book.Gates)
        {
            var sp = pos[gate.Sources.First()];
            float len = sp.Length == 0 ? 1 : sp.Length;
            var gp = new SKPoint(sp.X + sp.X / len * GateOffset, sp.Y + sp.Y / len * GateOffset);
            float sign = gp.X < 0 ? -1 : 1;                   // caption points outward
            yield return new SKPoint(gp.X + sign * 300, gp.Y - 30);
            yield return new SKPoint(gp.X - sign * 30, gp.Y + 28);
        }
    }

    private static float MeasureChromeWidth(SKTypeface bold, SKTypeface regular)
    {
        using var title = new SKPaint { TextSize = 40, Typeface = bold };
        using var sub = new SKPaint { TextSize = 22, Typeface = regular };
        float t = title.MeasureText("Pockets · Depth-Recipe Progression (v1)");
        float s = sub.MeasureText(
            "Navigation is crafting: reach zone×depth n+1 by crafting from depth n. Depth = radius (deeper = nested).");
        return 46 + Math.Max(t, s);
    }

    // ---- graph elements ----

    private static void DrawDepthRings(SKCanvas canvas, RecipeBook book, SKTypeface face)
    {
        int maxDepth = book.Chains.Max(c => c.TotalDepths);
        using var ring = new SKPaint { Color = Faint, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 1 };
        using var label = new SKPaint
        {
            Color = Faint.WithAlpha(130), IsAntialias = true, TextSize = 19, Typeface = face,
            TextAlign = SKTextAlign.Center
        };
        for (int d = 1; d <= maxDepth; d += 5)
        {
            float r = InnerR + (d - 1) * RingStep;
            canvas.DrawCircle(0, 0, r, ring);
            // Label on the due-west band (empty gutter between Flux and Gloam arms).
            var at = Polar(180, r);
            canvas.DrawText($"depth {d}", at.X + 4, at.Y - 6, label);
        }
    }

    private static void DrawWithinZoneArms(SKCanvas canvas, RecipeBook book, Dictionary<ZoneDepth, SKPoint> pos)
    {
        foreach (var chain in book.Chains)
        {
            var pal = PaletteFor(chain.Quadrant);
            using var arm = new SKPaint
            {
                Color = pal.Arm, IsAntialias = true, Style = SKPaintStyle.Stroke,
                StrokeWidth = 3, StrokeCap = SKStrokeCap.Round
            };
            for (int d = 2; d <= chain.TotalDepths; d++)
                canvas.DrawLine(pos[chain.NodeAt(d - 1)], pos[chain.NodeAt(d)], arm);
        }
    }

    private static void DrawCrossEdges(SKCanvas canvas, RecipeBook book, Dictionary<ZoneDepth, SKPoint> pos)
    {
        using var edge = new SKPaint
        {
            Color = CrossEdge, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = 4, StrokeCap = SKStrokeCap.Round
        };
        foreach (var e in book.Edges)
        {
            var a = pos[e.Source];
            var b = pos[e.Target];
            // Bow the connector toward the center so around-the-circle edges read as arcs.
            var mid = new SKPoint((a.X + b.X) / 2 * 0.6f, (a.Y + b.Y) / 2 * 0.6f);
            using var path = new SKPath();
            path.MoveTo(a);
            path.QuadTo(mid, b);
            canvas.DrawPath(path, edge);
            DrawArrowHead(canvas, mid, b, CrossEdge);
        }
    }

    private static void DrawHeroGates(
        SKCanvas canvas, RecipeBook book, Dictionary<ZoneDepth, SKPoint> pos,
        SKTypeface face, SKTypeface faceBold)
    {
        using var connector = new SKPaint
        {
            Color = GateColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3,
            PathEffect = SKPathEffect.CreateDash(new[] { 8f, 6f }, 0)
        };
        using var fill = new SKPaint { Color = GateColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var nameP = new SKPaint { Color = GateColor, IsAntialias = true, TextSize = 24, Typeface = faceBold };
        using var beatP = new SKPaint { Color = Ink.WithAlpha(190), IsAntialias = true, TextSize = 18, Typeface = face };

        foreach (var gate in book.Gates)
        {
            var src = gate.Sources.First();
            var sp = pos[src];
            float len = sp.Length == 0 ? 1 : sp.Length;
            var dir = new SKPoint(sp.X / len, sp.Y / len);
            var gp = new SKPoint(sp.X + dir.X * GateOffset, sp.Y + dir.Y * GateOffset);

            // Caption points outward — away from the Core — so it clears the arm.
            bool left = gp.X < 0;
            nameP.TextAlign = beatP.TextAlign = left ? SKTextAlign.Right : SKTextAlign.Left;
            float tx = gp.X + (left ? -24 : 24);

            canvas.DrawLine(sp, gp, connector);
            DrawDiamond(canvas, gp, 16, fill);
            canvas.DrawText(gate.Name, tx, gp.Y - 2, nameP);
            canvas.DrawText($"{gate.Beat.Type} · {gate.Requires.First().Quantity}× {src}", tx, gp.Y + 20, beatP);
        }
    }

    private static void DrawNodes(SKCanvas canvas, RecipeBook book, Dictionary<ZoneDepth, SKPoint> pos, SKTypeface face)
    {
        var roots = book.Roots.ToImmutableHashSet();
        using var label = new SKPaint { Color = Ink, IsAntialias = true, TextSize = 17, Typeface = face, TextAlign = SKTextAlign.Center };
        using var outline = new SKPaint { Color = Bg, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };
        using var rootRing = new SKPaint { Color = CoreColor, IsAntialias = true, Style = SKPaintStyle.Stroke, StrokeWidth = 3 };

        foreach (var (node, p) in pos)
        {
            var pal = PaletteFor(node.Quadrant);
            var color = node.Aspect == Aspect.Positive ? pal.Positive : pal.Negative;
            using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawCircle(p, NodeR, fill);
            canvas.DrawCircle(p, NodeR, outline);
            if (roots.Contains(node))
                canvas.DrawCircle(p, NodeR + 5, rootRing);   // world-start halo
            canvas.DrawText(node.Depth.ToString(), p.X, p.Y + 6, label);
        }
    }

    // ---- glyphs (bonus): the 8 basis + 4 parent icons placed on the plate ----

    private static void DrawGlyphs(SKCanvas canvas, RecipeBook book, SKTypeface face, SKTypeface faceBold)
    {
        var gp = GlyphParams.Default;
        foreach (var chain in book.Chains)
        {
            float center = CenterAngle(chain.Quadrant);
            var pal = PaletteFor(chain.Quadrant);

            // Parent glyph + quadrant name, beyond the deepest node along the center angle.
            float outerR = InnerR + (chain.TotalDepths - 1) * RingStep + 118;
            var labelPt = Polar(center, outerR);
            DrawGlyph(canvas, GlyphGeometry.Parent(chain.Quadrant, gp), gp, labelPt, 76, CoreColor);
            using var name = new SKPaint
            {
                Color = CoreColor, IsAntialias = true, TextSize = 30, Typeface = faceBold,
                TextAlign = SKTextAlign.Center
            };
            canvas.DrawText(chain.Quadrant.ToString(), labelPt.X, labelPt.Y + 78, name);

            // Basis glyph per aspect, near the shallow end of that aspect's arm, pushed out.
            DrawAspectGlyph(canvas, chain, Aspect.Positive, gp, pal.Positive);
            DrawAspectGlyph(canvas, chain, Aspect.Negative, gp, pal.Negative);
        }
    }

    private static void DrawAspectGlyph(SKCanvas canvas, QuadrantChain chain, Aspect aspect, GlyphParams gp, SKColor color)
    {
        var zone = aspect == Aspect.Positive ? chain.PositiveZone : chain.NegativeZone;
        int lo = aspect == Aspect.Positive ? 1 : chain.NegativeStartDepth;
        int hi = aspect == Aspect.Positive ? chain.PositiveDepths : chain.TotalDepths;
        if (hi < lo) return;
        int mid = (lo + hi) / 2;
        float center = CenterAngle(chain.Quadrant);
        float n = chain.TotalDepths;
        float t = n == 1 ? 0.5f : (mid - 1) / (n - 1);
        float angle = center + (t - 0.5f) * FanSpread;
        float r = InnerR + (mid - 1) * RingStep;
        // Push perpendicular-outward from the arm so the icon clears the nodes.
        var at = Polar(angle, r + 60);
        DrawGlyph(canvas, GlyphGeometry.Basis(zone, gp), gp, at, 46, color);
    }

    private static void DrawGlyph(SKCanvas canvas, IEnumerable<Primitive> prims, GlyphParams gp, SKPoint at, float size, SKColor color)
    {
        canvas.Save();
        canvas.Translate(at.X - size / 2, at.Y - size / 2);
        canvas.Scale(size / (float)gp.ViewBox);
        using var paint = new SKPaint
        {
            Color = color, IsAntialias = true, Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)gp.StrokeWidth * 0.8f, StrokeCap = SKStrokeCap.Round, StrokeJoin = SKStrokeJoin.Round
        };
        foreach (var prim in prims)
            using (var path = ToPath(prim))
                canvas.DrawPath(path, paint);
        canvas.Restore();
    }

    private static void DrawCore(SKCanvas canvas, SKTypeface face, SKTypeface faceBold)
    {
        using var fill = new SKPaint { Color = CoreColor, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var ink = new SKPaint { Color = Bg, IsAntialias = true, TextSize = 26, Typeface = faceBold, TextAlign = SKTextAlign.Center };
        using var sub = new SKPaint { Color = Bg.WithAlpha(200), IsAntialias = true, TextSize = 16, Typeface = face, TextAlign = SKTextAlign.Center };
        canvas.DrawCircle(0, 0, 62, fill);
        canvas.DrawText("Core", 0, -2, ink);
        canvas.DrawText("no entropy", 0, 20, sub);
    }

    // ---- chrome ----

    private static void DrawTitleAndLegend(SKCanvas canvas, RecipeBook book, int width, SKTypeface face, SKTypeface faceBold)
    {
        using var title = new SKPaint { Color = Ink, IsAntialias = true, TextSize = 40, Typeface = faceBold };
        canvas.DrawText("Pockets · Depth-Recipe Progression (v1)", 46, 60, title);

        using var sub = new SKPaint { Color = Ink.WithAlpha(180), IsAntialias = true, TextSize = 22, Typeface = face };
        canvas.DrawText(
            "Navigation is crafting: reach zone×depth n+1 by crafting from depth n. "
            + "Depth = radius (deeper = nested).",
            46, 96, sub);

        // Legend row.
        float lx = 46, ly = 140;
        LegendSwatch(canvas, ref lx, ly, new SKColor(198, 160, 92), "within-zone recipe (n ← n-1)", face, filledLine: true);
        LegendSwatch(canvas, ref lx, ly, CrossEdge, "cross-zone semi-linearization →", face, filledLine: true);
        LegendSwatch(canvas, ref lx, ly, GateColor, "hero-piece gate ◆", face, filledLine: false);
        LegendSwatch(canvas, ref lx, ly, CoreColor, "○ world start", face, filledLine: false);

        using var counts = new SKPaint { Color = Ink.WithAlpha(150), IsAntialias = true, TextSize = 18, Typeface = face };
        canvas.DrawText(
            $"{book.Nodes.Length} nodes · {book.Edges.Length} cross-edges · {book.Gates.Length} hero gates · "
            + "glyphs = the 8+4 entropy matrix (EntropyMatrix SSOT) · TUNABLE design data",
            46, 176, counts);
    }

    private static void LegendSwatch(SKCanvas canvas, ref float x, float y, SKColor color, string text, SKTypeface face, bool filledLine)
    {
        using var swatch = new SKPaint { Color = color, IsAntialias = true, Style = filledLine ? SKPaintStyle.Stroke : SKPaintStyle.Fill, StrokeWidth = 4 };
        if (filledLine) canvas.DrawLine(x, y - 6, x + 34, y - 6, swatch);
        else canvas.DrawCircle(x + 17, y - 6, 9, swatch);
        using var label = new SKPaint { Color = Ink.WithAlpha(200), IsAntialias = true, TextSize = 20, Typeface = face };
        canvas.DrawText(text, x + 44, y, label);
        x += 44 + label.MeasureText(text) + 40;
    }

    // ---- primitives ----

    private static void DrawArrowHead(SKCanvas canvas, SKPoint from, SKPoint to, SKColor color)
    {
        var d = new SKPoint(to.X - from.X, to.Y - from.Y);
        float len = (float)Math.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 0.001f) return;
        d = new SKPoint(d.X / len, d.Y / len);
        var perp = new SKPoint(-d.Y, d.X);
        float s = 16;
        // Land the arrowhead just short of the target node.
        var tip = new SKPoint(to.X - d.X * (NodeR + 2), to.Y - d.Y * (NodeR + 2));
        using var fill = new SKPaint { Color = color, IsAntialias = true, Style = SKPaintStyle.Fill };
        using var path = new SKPath();
        path.MoveTo(tip);
        path.LineTo(tip.X - d.X * s + perp.X * s * 0.6f, tip.Y - d.Y * s + perp.Y * s * 0.6f);
        path.LineTo(tip.X - d.X * s - perp.X * s * 0.6f, tip.Y - d.Y * s - perp.Y * s * 0.6f);
        path.Close();
        canvas.DrawPath(path, fill);
    }

    private static void DrawDiamond(SKCanvas canvas, SKPoint c, float r, SKPaint fill)
    {
        using var path = new SKPath();
        path.MoveTo(c.X, c.Y - r);
        path.LineTo(c.X + r, c.Y);
        path.LineTo(c.X, c.Y + r);
        path.LineTo(c.X - r, c.Y);
        path.Close();
        canvas.DrawPath(path, fill);
    }

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
                    new SKPoint((float)a.Radius, (float)a.Radius), 0,
                    a.LargeArcFlag == 1 ? SKPathArcSize.Large : SKPathArcSize.Small,
                    a.SweepFlag == 1 ? SKPathDirection.Clockwise : SKPathDirection.CounterClockwise,
                    new SKPoint((float)a.End.X, (float)a.End.Y));
                break;
        }
        return path;
    }
}
