using System.Buffers.Binary;
using Meziantou.Framework.WebPlatform.Css.Animations;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The color fonts: the COLR and CPAL tables, <c>font-palette</c> and <c>@font-palette-values</c>
/// (https://drafts.csswg.org/css-fonts-4/#font-palette-prop). The font is the one of the web-platform-tests: the glyph of
/// each letter from A to H followed by a digit has two layers (the glyphs 81 and 277); the layers of <c>A</c> use the
/// entries 3 and 7 of the palette. The font has 16 palettes of 8 colors: the palette 2 is marked as usable with a light
/// background and the palette 3 with a dark background.
/// </summary>
public sealed class ColorFontTests : IDisposable
{
    private static readonly byte[] ColorFont = File.ReadAllBytes(TestPaths.External("wpt", "css", "css-fonts", "resources", "COLR-palettes-test-font.ttf"));

    private static readonly DisplayColor Black = Rgb(0, 0, 0);
    private static readonly DisplayColor White = Rgb(255, 255, 255);
    private static readonly DisplayColor Red = Rgb(255, 0, 0);
    private static readonly DisplayColor Green = Rgb(0, 255, 0);
    private static readonly DisplayColor Blue = Rgb(0, 0, 255);
    private static readonly DisplayColor Yellow = Rgb(255, 255, 0);
    private static readonly DisplayColor Magenta = Rgb(255, 0, 255);
    private static readonly DisplayColor Cyan = Rgb(0, 255, 255);

    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private static DisplayColor Rgb(byte r, byte g, byte b) => DisplayColor.FromRgb(r, g, b);

    /// <summary>The glyph of x, which is not a color glyph.</summary>
    private static ushort PlainGlyph => FontFile.Load(ColorFont)!.Faces[0].GetGlyphId('x');

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new FontLoader(), RenderingBackend = new NullBackend() });
        var document = context.Navigate("https://example.com/", _ => "<!DOCTYPE html><style>@font-face { font-family: Color; src: url(color.ttf) } body { margin: 0; font: 10px/1 Color }</style>" + html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private sealed class FontLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(request.Url.EndsWith(".ttf", StringComparison.Ordinal) ? new DocumentResource(ColorFont) : null);
    }

    /// <summary>A rendering backend without services: the documents of its browsing contexts are rendered.</summary>
    private sealed class NullBackend : RenderingBackend
    {
        internal override RenderingSurface? CreateSurface(int width, int height, RenderingSurfaceOptions options) => null;

        internal override RenderingImageDecoder? CreateImageDecoder(ReadOnlyMemory<byte> data, string? mimeType) => null;
    }

    /// <summary>The glyph runs of the text of an element, in the painting order.</summary>
    private static DrawGlyphsItem[] GetGlyphs(Document document, string id)
        => [.. document.GetDisplayList()!.Items.ToArray().OfType<DrawGlyphsItem>().Where(item => (item.Source as Element ?? item.Source?.ParentElement)?.GetAttributeValue("id") == id)];

    /// <summary>The colors of the glyph runs of the text of an element, in the painting order.</summary>
    private static DisplayColor[] GetColors(Document document, string id) => [.. GetGlyphs(document, id).Select(item => item.Paint.Color)];

    [Fact]
    public void Tables_LayersAndPalettes()
    {
        var font = FontFile.Load(ColorFont)!.Faces[0];
        var glyphs = font.ColorGlyphs;
        Assert.NotNull(glyphs);
        Assert.Equal(16, glyphs.PaletteCount);
        Assert.Equal(8, glyphs.PaletteEntryCount);
        Assert.Equal(2, glyphs.LightPalette);
        Assert.Equal(3, glyphs.DarkPalette);

        // The colors are blue, green, red, alpha in the font
        Assert.Equal(new PaletteColor(0, 0, 0, 255), glyphs.GetPalette(0)[0]);
        Assert.Equal(new PaletteColor(255, 0, 0, 255), glyphs.GetPalette(0)[1]);
        Assert.Equal(new PaletteColor(255, 255, 0, 255), glyphs.GetPalette(0)[3]);
        Assert.Equal(new PaletteColor(0, 0, 255, 255), glyphs.GetPalette(1)[3]);

        var a = font.GetGlyphId('A');
        Assert.True(glyphs.IsColorGlyph(a));
        Assert.True(glyphs.TryGetLayers(a, out var layers));
        Assert.Equal([new ColorGlyphLayer(81, 3), new ColorGlyphLayer(277, 7)], layers);

        // The glyphs of the layers and the other glyphs are not color glyphs
        Assert.False(glyphs.IsColorGlyph(81));
        Assert.False(glyphs.TryGetLayers(font.GetGlyphId('x'), out _));

        // A font without color tables
        Assert.Null(BundledFonts.Ahem.Font.ColorGlyphs);
    }

    [Fact]
    public void Tables_ForegroundColorAndInvalidLayers()
    {
        // COLR (version 0): the glyph 5 has the layers (10, entry 1), (11, the color of the text), (12, an entry that does not
        // exist); the glyph 9 has the last layer only. CPAL (version 0): 1 palette of 2 colors
        var colr = new byte[14 + (2 * 6) + (3 * 4)];
        Write16(colr, 2, 2);
        Write32(colr, 4, 14);
        Write32(colr, 8, 26);
        Write16(colr, 12, 3);
        Write16(colr, 14, 5);
        Write16(colr, 16, 0);
        Write16(colr, 18, 3);
        Write16(colr, 20, 9);
        Write16(colr, 22, 2);
        Write16(colr, 24, 1);
        Write16(colr, 26, 10);
        Write16(colr, 28, 1);
        Write16(colr, 30, 11);
        Write16(colr, 32, 0xFFFF);
        Write16(colr, 34, 12);
        Write16(colr, 36, 7);

        var cpal = new byte[12 + 2 + (2 * 4)];
        Write16(cpal, 2, 2);
        Write16(cpal, 4, 1);
        Write16(cpal, 6, 2);
        Write32(cpal, 8, 14);
        cpal.AsSpan(14).Clear();
        new byte[] { 1, 2, 3, 255, 40, 50, 60, 128 }.CopyTo(cpal, 14);

        var font = FontFile.Load(AddTables(BundledFonts.Ahem.Font.Data, ("COLR", colr), ("CPAL", cpal)))!.Faces[0];
        var glyphs = font.ColorGlyphs;
        Assert.NotNull(glyphs);
        Assert.Equal(1, glyphs.PaletteCount);
        Assert.Null(glyphs.LightPalette);
        Assert.Null(glyphs.DarkPalette);
        Assert.Equal(new PaletteColor(60, 50, 40, 128), glyphs.GetPalette(0)[1]);

        Assert.True(glyphs.TryGetLayers(5, out var layers));
        Assert.Equal([new ColorGlyphLayer(10, 1), new ColorGlyphLayer(11, OpenTypeColorGlyphs.ForegroundPaletteIndex)], layers);

        // A color glyph whose layers are all invalid draws nothing
        Assert.True(glyphs.TryGetLayers(9, out layers));
        Assert.Empty(layers);
        Assert.False(glyphs.IsColorGlyph(6));

        // Without CPAL table, only the layers in the color of the text are drawn
        glyphs = FontFile.Load(AddTables(BundledFonts.Ahem.Font.Data, ("COLR", colr)))!.Faces[0].ColorGlyphs;
        Assert.NotNull(glyphs);
        Assert.Equal(0, glyphs.PaletteCount);
        Assert.True(glyphs.TryGetLayers(5, out layers));
        Assert.Equal([new ColorGlyphLayer(11, OpenTypeColorGlyphs.ForegroundPaletteIndex)], layers);

        // A table without base glyph record (e.g. only the paint graphs of the version 1) has no color glyph; a font whose
        // records are outside of the table is malformed, and is not loaded
        Write16(colr, 2, 0);
        Assert.Null(FontFile.Load(AddTables(BundledFonts.Ahem.Font.Data, ("COLR", colr)))!.Faces[0].ColorGlyphs);
        Write16(colr, 2, 200);
        Assert.Null(FontFile.Load(AddTables(BundledFonts.Ahem.Font.Data, ("COLR", colr))));
        Assert.Null(BundledFonts.Ahem.Font.ColorGlyphs);

        static void Write16(byte[] data, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(offset), value);
        static void Write32(byte[] data, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(offset), value);
    }

    /// <summary>Adds tables to an OpenType font (or replaces them): the table directory is written again, without checksums.</summary>
    private static byte[] AddTables(byte[] font, params (string Tag, byte[] Data)[] added)
    {
        var tables = new SortedDictionary<uint, byte[]>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(font.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = font.AsSpan(12 + (i * 16), 16);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            tables[BinaryPrimitives.ReadUInt32BigEndian(record)] = font.AsSpan(offset, length).ToArray();
        }

        foreach (var (tag, data) in added)
        {
            tables[BinaryPrimitives.ReadUInt32BigEndian(System.Text.Encoding.ASCII.GetBytes(tag))] = data;
        }

        var output = new List<byte>();
        var header = new byte[12 + (16 * tables.Count)];
        BinaryPrimitives.WriteUInt32BigEndian(header, BinaryPrimitives.ReadUInt32BigEndian(font));
        BinaryPrimitives.WriteUInt16BigEndian(header.AsSpan(4), (ushort)tables.Count);
        output.AddRange(header);
        var index = 0;
        foreach (var (tag, data) in tables)
        {
            var record = 12 + (index++ * 16);
            BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[record..], tag);
            BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[(record + 8)..], (uint)output.Count);
            BinaryPrimitives.WriteUInt32BigEndian(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(output)[(record + 12)..], (uint)data.Length);
            output.AddRange(data);
            while (output.Count % 4 != 0)
            {
                output.Add(0);
            }
        }

        return [.. output];
    }

    [Fact]
    public void Painting_LayersOfTheDefaultPalette()
    {
        var document = Load("<div id=a>AxB</div>");
        var items = GetGlyphs(document, "a");

        // The layers of A (the entries 3 and 7 of the palette 0), x (not a color glyph, in the color of the text), the layers of B
        Assert.Equal([Yellow, White, Black, Blue, Black], items.Select(item => item.Paint.Color));
        Assert.Equal([[81], [277], [PlainGlyph], [81], [277]], items.Select(item => item.Run.Glyphs));

        // The layers are at the position of their glyph; the text is on the first glyph run only
        Assert.Equal([0, 0, 10, 20, 20], items.Select(item => item.Run.Positions[0].X));
        Assert.Equal(["AxB", "", "", "", ""], items.Select(item => item.Run.Text));
    }

    [Theory]
    [InlineData("", "normal")]
    [InlineData("", "--unknown")]
    [InlineData("@font-palette-values --p { font-family: Other; base-palette: 1 }", "--p")]
    [InlineData("@font-palette-values --P { font-family: Color; base-palette: 1 }", "--p")]
    [InlineData("@font-palette-values --p { font-family: Color; base-palette: 99 }", "--p")]
    [InlineData("@media print { @font-palette-values --p { font-family: Color; base-palette: 1 } }", "--p")]
    public void FontPalette_DefaultPalette(string rules, string palette)
    {
        var document = Load($"<style>{rules}</style><div id=a style='font-palette: {palette}'>A</div>");
        Assert.Equal([Yellow, White], GetColors(document, "a"));
    }

    [Fact]
    public void FontPalette_LightAndDark()
    {
        var document = Load("<div id=a style='font-palette: light'>A</div><div id=b style='font-palette: dark'>A</div><div id=c style='color-scheme: dark'>A</div><div id=d style='color-scheme: light'>A</div>");
        Assert.Equal([Magenta, Red], GetColors(document, "a"));
        Assert.Equal([Cyan, Green], GetColors(document, "b"));

        // normal: "If the color-scheme property is set to a value other than normal, user-agents should select the first light or dark palette as appropriate"
        Assert.Equal([Cyan, Green], GetColors(document, "c"));
        Assert.Equal([Magenta, Red], GetColors(document, "d"));
    }

    [Fact]
    public void FontPaletteValues_BasePaletteAndOverrideColors()
    {
        var document = Load("""
            <style>
            @font-palette-values --one { font-family: Color; base-palette: 1 }
            @font-palette-values --dark { font-family: Other, "color"; base-palette: dark }
            @font-palette-values --override { font-family: Color; override-colors: 3 #f00, 99 lime, 7 blue, calc(3 + 4) rgb(0 255 255) }
            @font-palette-values --last { font-family: Color; base-palette: 1 }
            @font-palette-values --last { font-family: Color; override-colors: 3 lime }
            </style>
            <div id=a style='font-palette: --one'>A</div>
            <div id=b style='font-palette: --dark'>A</div>
            <div id=c style='font-palette: --override'>A</div>
            <div id=d style='font-palette: --last'>A</div>
            """);
        Assert.Equal([Blue, Black], GetColors(document, "a"));

        // The names of the families are compared as the family names are
        Assert.Equal([Cyan, Green], GetColors(document, "b"));

        // The entries that are not in the palette are ignored; the last color of an entry wins
        Assert.Equal([Red, Cyan], GetColors(document, "c"));

        // The last rule of a name for a family replaces the other ones
        Assert.Equal([Green, White], GetColors(document, "d"));
    }

    [Fact]
    public void FontPaletteValues_DynamicChanges()
    {
        var document = Load("<style id=s>@font-palette-values --p { font-family: Color; base-palette: 1 }</style><div id=a style='font-palette: --p'>A</div>");
        Assert.Equal([Blue, Black], GetColors(document, "a"));

        var sheet = (CssStyleSheet)document.StyleSheets[1];
        sheet.InsertRule("@font-palette-values --p { font-family: Color; base-palette: light }", 1);
        Assert.Equal([Magenta, Red], GetColors(document, "a"));

        sheet.DeleteRule(1);
        sheet.DeleteRule(0);
        Assert.Equal([Yellow, White], GetColors(document, "a"));

        document.GetElementById("a")!.SetAttribute("style", "font-palette: dark");
        Assert.Equal([Cyan, Green], GetColors(document, "a"));
    }

    [Fact]
    public void FontPalette_PaletteMix()
    {
        var document = Load("<div id=a style='font-palette: palette-mix(in srgb, light, dark)'>A</div><div id=b style='font-palette: palette-mix(in srgb, light 25%, palette-mix(in srgb, dark, dark))'>A</div>");

        // Each color is the color-mix() of the colors of the entry: magenta and cyan, red and green
        var colors = GetColors(document, "a");
        Assert.Equal(2, colors.Length);
        AssertColor(0.5, 0.5, 1, colors[0]);
        AssertColor(0.5, 0.5, 0, colors[1]);

        colors = GetColors(document, "b");
        AssertColor(0.25, 0.75, 1, colors[0]);
        AssertColor(0.25, 0.75, 0, colors[1]);

        static void AssertColor(double r, double g, double b, DisplayColor color)
        {
            Assert.Equal(r, color.R, 3);
            Assert.Equal(g, color.G, 3);
            Assert.Equal(b, color.B, 3);
            Assert.Equal(1, color.A, 3);
        }
    }

    [Fact]
    public void Painting_AlphaShadowAndStroke()
    {
        var document = Load("<div id=a style='color: rgb(0 0 0 / 50%)'>A</div><div id=b style='text-shadow: 1px 1px red'>A</div><div id=c style='-webkit-text-stroke: 1px red'>Ax</div>");

        // The alpha of the color of the text applies to the colors of the palette
        Assert.Equal([Yellow.MultiplyAlpha(0.5), White.MultiplyAlpha(0.5)], GetColors(document, "a"));

        // The shadow is the one of the layers, in the color of the shadow (the two layers are one glyph run)
        var shadow = GetGlyphs(document, "b");
        Assert.Equal([Red, Yellow, White], shadow.Select(item => item.Paint.Color));
        Assert.Equal([81, 277], shadow[0].Run.Glyphs);

        // The color glyphs are not stroked
        var stroke = GetGlyphs(document, "c").Where(item => item.Paint.Style == PaintStyle.Stroke).ToArray();
        var single = Assert.Single(stroke);
        Assert.Equal([PlainGlyph], single.Run.Glyphs);
    }

    [Fact]
    public void FontPalette_Interpolation()
    {
        var pair = CssInterpolation.Prepare(CssInterpolationContext.Empty, "font-palette", "light", "--custom");
        Assert.False(pair.IsDiscrete);
        Assert.Equal("light", pair.Interpolate(-1));
        Assert.Equal("light", pair.Interpolate(0));
        Assert.Equal("palette-mix(light 70%, --custom 30%)", pair.Interpolate(0.3));
        Assert.Equal("palette-mix(light, --custom)", pair.Interpolate(0.5));
        Assert.Equal("--custom", pair.Interpolate(1));
        Assert.Equal("--custom", pair.Interpolate(2));

        // The palettes are not additive
        Assert.Equal("dark", CssInterpolation.Add(CssInterpolationContext.Empty, "font-palette", "light", "dark"));
        Assert.Equal("dark", CssInterpolation.Accumulate(CssInterpolationContext.Empty, "font-palette", "light", "dark"));
    }
}
