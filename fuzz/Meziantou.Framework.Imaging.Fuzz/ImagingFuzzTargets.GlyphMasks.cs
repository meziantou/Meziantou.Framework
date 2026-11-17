using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Fuzz;

public static partial class ImagingFuzzTargets
{
    /// <summary>The longest text of <see cref="GlyphMasksTarget"/>.</summary>
    private const int MaxGlyphMaskText = 64;

    /// <summary>The side of the canvas of <see cref="GlyphMasksTarget"/>, in pixels.</summary>
    private const int GlyphMaskCanvas = 160;

    /// <summary>The font of <see cref="GlyphMasksTarget"/>: the subset of Noto Sans of the golden corpus, whose glyphs have curves and hinting instructions.</summary>
    private static readonly Lazy<Font> GlyphMaskFont = new(() =>
    {
        using var stream = typeof(ImagingFuzzTargets).Assembly.GetManifestResourceStream("Fonts/NotoSans-Regular.subset.ttf")!;
        return Font.Load(stream);
    });

    /// <summary>The letters of the font, the characters that end lines, and the ligature and the accents of the subset.</summary>
    /// <remarks>A property: <see cref="All"/>, in another part of the class, may be initialized first.</remarks>
    private static string[] GlyphMaskTokens =>
    [
        "Hamburgefonstiv", "AV", "Ty", "To", "fi", "é", "À", "€", "42", " ", "\n", ".", "!", ":", "Wave",
        "\u0001", "\u007F", "ÿ",
    ];

    /// <summary>
    /// The texts of a <see cref="DrawingCanvas.GlyphMasks(GlyphCache, DrawingAction)"/> scope: the pixels are the same
    /// without a cache, with an empty cache, with the cache the previous drawing filled and under a limit of the live bytes
    /// that leaves little room for the cache of the scope, and a horizontal layout that is not hinted and has no decoration
    /// gets the pixels that <see cref="DrawingCanvas.FillGlyphs"/> gives its glyphs at their origins, one run after the
    /// other. The input is a byte of size (6 to 37.875 pixels per em), a byte of the
    /// horizontal and one of the vertical position of the layout (in 1/64 of a pixel from 2), a byte of options (bit 0:
    /// synthetic bold, bit 1: synthetic oblique, bits 2 and 3: the hinting, none, light or full, bit 4: vertical lines,
    /// bit 5: hard edges, bit 6: a canvas that swaps the axes, bit 7: an underline and a translucent paint), then the text.
    /// </summary>
    private static void GlyphMasksTarget(ReadOnlySpan<byte> data)
    {
        var size = 6 + (FuzzInput.ReadByte(ref data) / 8.0);
        var origin = new PointD(2 + (FuzzInput.ReadByte(ref data) / 64.0), 2 + (FuzzInput.ReadByte(ref data) / 64.0));
        var flags = FuzzInput.ReadByte(ref data);
        var text = FuzzText.Decode(data);
        if (text.Length > MaxGlyphMaskText)
        {
            text = text[..MaxGlyphMaskText];
            if (char.IsHighSurrogate(text[^1]))
            {
                text = text[..^1];
            }
        }

        if (text.Length == 0)
            return;

        var synthesis = ((flags & 1) != 0 ? FontSynthesis.Bold : FontSynthesis.None) | ((flags & 2) != 0 ? FontSynthesis.Oblique : FontSynthesis.None);
        var hinting = ((flags >> 2) & 3) switch
        {
            1 => TextHinting.Light,
            2 => TextHinting.Full,
            _ => TextHinting.None,
        };
        var isVertical = (flags & 16) != 0;
        var isAliased = (flags & 32) != 0;
        var swapsAxes = (flags & 64) != 0;
        var isDecorated = (flags & 128) != 0;
        var style = new TextStyle(GlyphMaskFont.Value, size)
        {
            Synthesis = synthesis,
            Decorations = isDecorated ? TextDecorations.Underline : TextDecorations.None,
        };
        var options = new TextLayoutOptions
        {
            Hinting = hinting,
            WritingMode = isVertical ? TextWritingMode.VerticalRightToLeft : TextWritingMode.Horizontal,
            MaxWidth = isVertical ? double.PositiveInfinity : GlyphMaskCanvas,
            MaxHeight = isVertical ? GlyphMaskCanvas : double.PositiveInfinity,
        };
        var layout = TextLayout.Create(text, style, options);
        var brush = new SolidBrush(isDecorated ? new Rgba32(250, 120, 10, 150) : new Rgba32(20, 40, 250));
        var transform = swapsAxes ? new AffineTransform(0, 1, -1, 0, GlyphMaskCanvas, 0) : new AffineTransform(1, 0, 0, 1, 0, 0);

        Rgba32[] Draw(GlyphCache? cache, long? limit, out long peak) => Render(canvas => canvas.Transform(transform, moved => moved.Antialiasing(!isAliased, edges => edges.GlyphMasks(cache, masked => masked.FillText(brush, layout, origin)))), limit, out peak);

        var uncached = Draw(cache: null, limit: null, out _);
        var cache = new GlyphCache(1 << 24);
        FuzzAssert.True(uncached.AsSpan().SequenceEqual(Draw(cache, limit: null, out var peak)), "The masks of an empty cache do not give the pixels of the masks without a cache");
        FuzzAssert.True(uncached.AsSpan().SequenceEqual(Draw(cache, limit: null, out _)), "The masks of a cache do not give the pixels of the masks without a cache");

        // The cache of a scope that was given none is charged to the image: under a limit that leaves little room for it,
        // its masks are released or not kept, and the pixels are the same
        FuzzAssert.True(uncached.AsSpan().SequenceEqual(Draw(cache: null, peak + 1024, out _)), "The masks of a scope under a limit do not give the pixels of the masks without a limit");
        if (isVertical || isDecorated || hinting != TextHinting.None)
            return;

        var runs = Render(canvas => canvas.Transform(transform, moved => moved.Antialiasing(!isAliased, edges => FillGlyphRuns(edges, brush, layout, origin))), limit: null, out _);
        FuzzAssert.True(uncached.AsSpan().SequenceEqual(runs), "The glyphs of a layout drawn from masks are not those of a glyph run");

        static Rgba32[] Render(DrawingAction action, long? limit, out long peak)
        {
            using var image = limit is { } bytes
                ? new Image<Rgba32>(GlyphMaskCanvas, GlyphMaskCanvas, new Rgba32(0, 0, 0, 0), new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = bytes } })
                : new Image<Rgba32>(GlyphMaskCanvas, GlyphMaskCanvas);
            image.Draw(action);
            peak = image.Owner.Scope.GetDiagnostics().PeakLiveBytes;
            var pixels = new Rgba32[GlyphMaskCanvas * GlyphMaskCanvas];
            image.Frames[0].CopyPixelDataTo(pixels);
            return pixels;
        }
    }

    /// <summary>Fills the glyphs of a layout of upright glyphs with <see cref="DrawingCanvas.FillGlyphs"/>, one run after the other.</summary>
    private static void FillGlyphRuns(DrawingCanvas canvas, Brush brush, TextLayout layout, PointD origin)
    {
        foreach (var run in layout.Runs)
        {
            var glyphs = new PositionedGlyph[run.Count];
            for (var i = 0; i < run.Count; i++)
            {
                var position = layout.GetGlyphOrigin(run.Start + i);
                glyphs[i] = new PositionedGlyph(layout.GetGlyph(run.Start + i), new PointD(origin.X + position.X, origin.Y + position.Y));
            }

            canvas.FillGlyphs(layout.BrushCount == 0 ? brush : layout.GetBrush(run.Brush) ?? brush, layout.GetFont(run.Font), run.Size, glyphs, new GlyphRunOptions { Synthesis = run.Synthesis });
        }
    }

    private static IEnumerable<FuzzSeed> GetGlyphMaskSeeds()
    {
        // The texts of the unit tests: two lines of curves, ligatures and accents at 15.25 pixels per em, with each option,
        // and short labels at the sizes of the comparisons with other libraries
        const string Text = "Hamburgefonstiv AV. Tyé\nÀ la carte: fi €42!";
        yield return Seed("paragraph", 15.25, 19, 38, 0, Text);
        yield return Seed("bold-oblique", 15.25, 19, 38, 3, Text);
        yield return Seed("hinted-light", 15.25, 19, 38, 4, Text);
        yield return Seed("hinted-full", 15.25, 19, 38, 8, Text);
        yield return Seed("vertical", 15.25, 19, 38, 16, Text);
        yield return Seed("hard-edges", 15.25, 19, 38, 32, Text);
        yield return Seed("axes-swapped", 15.25, 19, 38, 64, Text);
        yield return Seed("underlined-translucent", 15.25, 19, 38, 128, Text);
        yield return Seed("all-options", 15.25, 19, 38, 0xFF, Text);
        yield return Seed("label", 12, 0, 0, 0, "Wave To. 42");
        yield return Seed("heading", 37.875, 32, 16, 0, "ToÀV");

        static FuzzSeed Seed(string name, double size, byte x, byte y, byte flags, string text) => new(name, (byte[])[(byte)Math.Round((size - 6) * 8, MidpointRounding.AwayFromZero), x, y, flags, .. FuzzText.Encode(text)]);
    }
}
