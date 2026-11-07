using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Fonts;

/// <summary>
/// The font matching (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm) and the font-relative units computed from
/// the metrics of the fonts (https://drafts.csswg.org/css-values-4/#font-relative-lengths). The expected metrics are the ones
/// of the bundled fonts: Ahem (x-height and cap height 0.8em, glyphs 1em wide, ascent 0.8em and descent 0.2em), Tinos
/// (serif, the default font), Arimo (sans-serif) and Cousine (monospace).
/// </summary>
public sealed class FontMatchingTests : IDisposable
{
    private readonly BrowsingContextScope _contexts = new();

    public void Dispose() => _contexts.Dispose();

    private Document Load(string html)
    {
        var context = _contexts.Create(new BrowsingContextOptions { DocumentLoader = new FontLoader() });
        var document = context.Navigate("https://example.com/", _ => html);
        _contexts.EventLoop.RunUntilIdle();
        return document;
    }

    private sealed class FontLoader : DocumentLoader
    {
        public override ValueTask<DocumentResource?> LoadAsync(DocumentRequest request, CancellationToken cancellationToken)
            => ValueTask.FromResult<DocumentResource?>(request.Url.EndsWith(".ttf", StringComparison.Ordinal) ? new DocumentResource(TestFonts.Ahem) : null);
    }

    private static string Get(Document document, string property, string selector = "#target")
        => document.DefaultView!.GetComputedStyle(document.QuerySelector(selector)!).GetPropertyValue(property);

    private static ComputedStyle GetStyle(Document document, string selector = "#target")
        => document.GetStyleEngine().GetStyle(document.QuerySelector(selector)!);

    private static double Ratio(OpenTypeFont font, double value) => value / font.UnitsPerEm;

    private static string Pixels(double value) => CssValueComputer.FormatPixels(value);

    // ----------------------------------------------------------------------------------------------------------------
    // Font matching algorithm
    // ----------------------------------------------------------------------------------------------------------------

    private static FontSelectionCapabilities Face(double weight, string style = "normal", double width = 100)
        => FontSelectionCapabilities.FromDescriptors(weight.ToString(System.Globalization.CultureInfo.InvariantCulture), width.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%", style, font: null);

    private static List<int> Select(FontSelectionCapabilities[] faces, double weight, FontSlope style, double width = 100)
    {
        var selected = new List<int>();
        FontMatcher.Select(faces, weight, style, width, selected);
        return selected;
    }

    [Theory]
    [InlineData(400, 500)] // From 400 to 500: the heavier weights up to 500 first
    [InlineData(450, 500)]
    [InlineData(500, 500)]
    [InlineData(600, 700)] // Above 500: the heavier weights first
    [InlineData(900, 700)] // Then the lighter weights in descending order
    [InlineData(350, 300)] // Below 400: the lighter weights first
    [InlineData(200, 300)] // Then the heavier weights in ascending order
    public void Match_Weight(double weight, double expected)
    {
        FontSelectionCapabilities[] faces = [Face(300), Face(500), Face(700)];
        Assert.Equal([Array.FindIndex(faces, face => face.Weight.Min == expected)], Select(faces, weight, FontSlope.Normal));
    }

    [Fact]
    public void Match_WeightBetween400And500()
    {
        // From 400 to 500, the lighter weights come before the weights above 500
        FontSelectionCapabilities[] faces = [Face(300), Face(600)];
        Assert.Equal([0], Select(faces, 450, FontSlope.Normal));
    }

    [Fact]
    public void Match_WeightRange()
    {
        // A range (a variable font) supports all its values: the desired weight is selected
        var selected = new List<int>();
        var values = FontMatcher.Select([Face(400), FontSelectionCapabilities.FromDescriptors("100 900", "normal", "normal", font: null)], 450, FontSlope.Normal, 100, selected);
        Assert.Equal([1], selected);
        Assert.Equal(450, values.Weight);

        // Out of the range: the closest value of the range
        values = FontMatcher.Select([FontSelectionCapabilities.FromDescriptors("900 300", "normal", "normal", font: null)], 100, FontSlope.Normal, 100, selected);
        Assert.Equal(300, values.Weight);
    }

    [Fact]
    public void Match_WidthIsMatchedBeforeWeight()
    {
        FontSelectionCapabilities[] faces = [Face(400, width: 75), Face(700, width: 100), Face(400, width: 125)];
        Assert.Equal([1], Select(faces, 400, FontSlope.Normal, 100));
        Assert.Equal([0], Select(faces, 400, FontSlope.Normal, 87.5)); // Narrower widths first
        Assert.Equal([2], Select(faces, 400, FontSlope.Normal, 112.5)); // Wider widths first
    }

    [Fact]
    public void Match_Style()
    {
        var normal = Face(400);
        var italic = Face(400, "italic");
        var oblique = Face(400, "oblique 12deg");

        Assert.Equal([1], Select([normal, italic, oblique], 400, FontSlope.Italic));
        Assert.Equal([2], Select([normal, italic, oblique], 400, new FontSlope(FontSlopeKind.Oblique, 14)));

        // italic: the oblique faces are preferred to the normal faces, and normal is preferred to nothing
        Assert.Equal([1], Select([normal, oblique], 400, FontSlope.Italic));
        Assert.Equal([0], Select([normal], 400, FontSlope.Italic));

        // oblique: no fallback to the italic faces (https://github.com/w3c/csswg-drafts/issues/9389), unless there is no other face
        Assert.Equal([0], Select([normal, italic], 400, new FontSlope(FontSlopeKind.Oblique, 20)));
        Assert.Equal([0], Select([normal, italic], 400, new FontSlope(FontSlopeKind.Oblique, 5)));
        Assert.Equal([0], Select([italic], 400, new FontSlope(FontSlopeKind.Oblique, 20)));

        // italic: the oblique angles are searched from the default angle (14deg): above it first, then below it
        Assert.Equal([1], Select([Face(400, "oblique 12deg"), Face(400, "oblique 20deg"), Face(400, "oblique 30deg")], 400, FontSlope.Italic));
        Assert.Equal([0], Select([Face(400, "oblique 12deg"), Face(400, "oblique 5deg")], 400, FontSlope.Italic));

        // italic among ranges of oblique angles: the faces that support the default angle remain, the weight selects one
        Assert.Equal([0], Select([Face(400, "oblique 14deg 30deg"), Face(700, "oblique 5deg 14deg")], 400, FontSlope.Italic));

        // normal: an italic face only when there is no upright face
        Assert.Equal([0], Select([normal, italic], 400, FontSlope.Normal));
        Assert.Equal([0], Select([italic], 400, FontSlope.Normal));

        // A range of oblique angles
        var range = FontSelectionCapabilities.FromDescriptors("normal", "normal", "oblique 0deg 20deg", font: null);
        var selected = new List<int>();
        Assert.Equal(new FontSelectedSlope(IsItalic: false, 10), FontMatcher.Select([range], 400, new FontSlope(FontSlopeKind.Oblique, 10), 100, selected).Slope);
        Assert.Equal(new FontSelectedSlope(IsItalic: false, 0), FontMatcher.Select([range], 400, FontSlope.Normal, 100, selected).Slope);
    }

    [Fact]
    public void Match_StyleObliqueOnly()
    {
        // font-synthesis-style: oblique-only prevents the use of the oblique faces as a fallback for italic
        // (https://github.com/w3c/csswg-drafts/issues/9390)
        var normal = Face(400);
        var italic = Face(400, "italic");
        var oblique = Face(400, "oblique");
        var selected = new List<int>();

        FontMatcher.Select([normal, oblique], 400, FontSlope.Italic, 100, selected, FontStyleSynthesis.None);
        Assert.Equal([1], selected);
        FontMatcher.Select([normal, oblique], 400, FontSlope.Italic, 100, selected, FontStyleSynthesis.ObliqueOnly);
        Assert.Equal([0], selected);
        FontMatcher.Select([normal, oblique, italic], 400, FontSlope.Italic, 100, selected, FontStyleSynthesis.ObliqueOnly);
        Assert.Equal([2], selected);

        // The oblique faces are the last resort; an oblique style is not affected
        FontMatcher.Select([Face(400, "oblique 14deg 30deg"), Face(700, "oblique 5deg 14deg")], 400, FontSlope.Italic, 100, selected, FontStyleSynthesis.ObliqueOnly);
        Assert.Equal([0], selected);
        FontMatcher.Select([normal, oblique], 400, new FontSlope(FontSlopeKind.Oblique, 14), 100, selected, FontStyleSynthesis.ObliqueOnly);
        Assert.Equal([1], selected);
    }

    [Fact]
    public void Match_BundledFonts()
    {
        var document = Load("");
        static FontDescription Description(string family, double weight = 400, FontSlope style = default) => FontDescription.Initial with { Family = family, Weight = weight, Style = style };

        var selector = document.FontSelector;
        Assert.Equal("Tinos Regular", selector.InitialFonts.PrimaryFont.Font.FullName);
        Assert.Equal("Tinos Bold", selector.GetFonts(Description("serif", 700)).PrimaryFont.Font.FullName);
        Assert.Equal("Tinos Bold Italic", selector.GetFonts(Description("Unknown, serif", 900, FontSlope.Italic)).PrimaryFont.Font.FullName);
        Assert.Equal("Arimo Italic", selector.GetFonts(Description("sans-serif", 400, FontSlope.Italic)).PrimaryFont.Font.FullName);
        Assert.Equal("Cousine Regular", selector.GetFonts(Description("monospace")).PrimaryFont.Font.FullName);
        Assert.Equal("Ahem", selector.GetFonts(Description("ahem")).PrimaryFont.Font.FullName);

        // A generic family without bundled font and an unknown family fall back to the default font
        Assert.Equal("Tinos Regular", selector.GetFonts(Description("cursive, Unknown")).PrimaryFont.Font.FullName);

        // The families that are not installed and that a bundled family is metric-compatible with use it (as fontconfig)
        Assert.Equal("Arimo Regular", selector.GetFonts(Description("Arial")).PrimaryFont.Font.FullName);
        Assert.Equal("Arimo Italic", selector.GetFonts(Description("helvetica", 400, FontSlope.Italic)).PrimaryFont.Font.FullName);
        Assert.Equal("Tinos Bold", selector.GetFonts(Description("\"Times New Roman\"", 700)).PrimaryFont.Font.FullName);
        Assert.Equal("Cousine Regular", selector.GetFonts(Description("Courier New, serif")).PrimaryFont.Font.FullName);
        Assert.Equal("Cousine Regular", selector.GetFonts(Description("Unknown, Courier")).PrimaryFont.Font.FullName);

        // A quoted generic family name is a family name (there is no family named "monospace")
        Assert.Equal("Tinos Regular", selector.GetFonts(Description("\"monospace\"")).PrimaryFont.Font.FullName);

        // The per-character fallback: the next family, then the bundled fonts; no font has a glyph for a private use character
        var ahem = selector.GetFonts(Description("Ahem, monospace"));
        Assert.Equal("Ahem", ahem.GetFontForCharacter('A').Font.FullName);
        Assert.Equal("Cousine Regular", ahem.GetFontForCharacter('Ж').Font.FullName);
        Assert.Equal("Tinos Regular", selector.GetFonts(Description("Ahem")).GetFontForCharacter('Ж').Font.FullName);
        Assert.Null(ahem.FindFontForCharacter(0x10FFFD));
        Assert.Same(ahem.PrimaryFont, ahem.GetFontForCharacter(0x10FFFD));
    }

    [Fact]
    public void Match_Synthesis()
    {
        var document = Load("");
        var selector = document.FontSelector;

        // Ahem has a single regular face: bold and italic are synthesized when font-synthesis allows it
        var font = selector.GetFonts(FontDescription.Initial with { Family = "Ahem", Weight = 700, Style = FontSlope.Italic }).PrimaryFont;
        Assert.True(font.SyntheticBold);
        Assert.Equal(14, font.SyntheticObliqueAngle);

        font = selector.GetFonts(FontDescription.Initial with { Family = "Ahem", Weight = 700, Style = FontSlope.Italic, SynthesizeWeight = false, SynthesizeStyle = FontStyleSynthesis.ObliqueOnly }).PrimaryFont;
        Assert.False(font.SyntheticBold);
        Assert.Equal(0, font.SyntheticObliqueAngle);

        font = selector.GetFonts(FontDescription.Initial with { Family = "Ahem", Style = new FontSlope(FontSlopeKind.Oblique, 20), SynthesizeStyle = FontStyleSynthesis.ObliqueOnly }).PrimaryFont;
        Assert.Equal(20, font.SyntheticObliqueAngle);

        // The bold face of Tinos is not synthesized, and the weight 900 of the variable Arimo (400 to 700) is the instance 700
        Assert.False(selector.GetFonts(FontDescription.Initial with { Weight = 900 }).PrimaryFont.SyntheticBold);
        var arimo = selector.GetFonts(FontDescription.Initial with { Family = "sans-serif", Weight = 900 }).PrimaryFont;
        Assert.False(arimo.SyntheticBold);
        Assert.NotNull(arimo.GetVariationInstance(16));

        // An oblique style does not fall back to the italic face of a family: the upright face is sheared
        var tinos = selector.GetFonts(FontDescription.Initial with { Style = new FontSlope(FontSlopeKind.Oblique, 14) }).PrimaryFont;
        Assert.Equal("Tinos Regular", tinos.Font.FullName);
        Assert.Equal(14, tinos.SyntheticObliqueAngle);
    }

    [Fact]
    public void Match_SynthesisOutOfRange()
    {
        // Nothing is synthesized outside of the range of a face that supports a range of weights or of oblique angles
        // (https://github.com/w3c/csswg-drafts/issues/7999), here ranges given by the descriptors of the font faces
        var document = Load("""
            <style>
            @font-face { font-family: range; src: url(ahem.ttf); font-weight: 400 500; font-style: oblique 0deg 10deg }
            @font-face { font-family: single; src: url(ahem.ttf); font-weight: 400; font-style: normal }
            </style>
            <span style="font-family: range">a</span><span style="font-family: single">a</span>
            """);
        foreach (var face in document.Fonts)
        {
            _ = face.Load();
        }

        _contexts.EventLoop.RunUntilIdle();
        var selector = document.FontSelector;

        var font = selector.GetFonts(FontDescription.Initial with { Family = "range", Weight = 900, Style = new FontSlope(FontSlopeKind.Oblique, -10) }).PrimaryFont;
        Assert.Equal("Ahem", font.Font.FullName);
        Assert.False(font.SyntheticBold);
        Assert.Equal(0, font.SyntheticObliqueAngle);

        font = selector.GetFonts(FontDescription.Initial with { Family = "single", Weight = 900, Style = new FontSlope(FontSlopeKind.Oblique, -10) }).PrimaryFont;
        Assert.Equal("Ahem", font.Font.FullName);
        Assert.True(font.SyntheticBold);
        Assert.Equal(-10, font.SyntheticObliqueAngle);
    }

    [Theory]
    [InlineData("U+0-7F", 0x41, true)]
    [InlineData("U+0-7F", 0x80, false)]
    [InlineData("U+4??", 0x4FF, true)]
    [InlineData("U+4??", 0x500, false)]
    [InlineData("U+20, U+30-39", 0x35, true)]
    [InlineData("U+20, U+30-39", 0x41, false)]
    public void UnicodeRange(string value, int codePoint, bool expected) => Assert.Equal(expected, UnicodeRangeSet.Parse(value).Contains(codePoint));

    // ----------------------------------------------------------------------------------------------------------------
    // Font-relative units
    // ----------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("width: 1ex", "16px")]
    [InlineData("width: 1cap", "16px")]
    [InlineData("width: 1ch", "20px")]
    [InlineData("width: 1ic", "20px")]
    [InlineData("width: 1lh", "20px")] // line-height: normal is the ascent plus the descent
    [InlineData("width: calc(2ex + 1ch)", "52px")]
    [InlineData("font-size: 2ex", "16px")] // The font metrics of the parent (font-size: 10px)
    [InlineData("font-size-adjust: 0.5; width: 1ex", "10px")] // The used font size makes the x-height 0.5em
    [InlineData("font-size-adjust: ch-width 0.5; width: 1ch", "10px")]
    [InlineData("font-size-adjust: from-font; width: 1ex", "16px")]
    public void Units_Ahem(string declarations, string expected)
    {
        var document = Load($"<div style='font: 10px Ahem'><div id=target style='font-size: 20px; {declarations}'></div></div>");
        var property = declarations.Split(':')[^2].Split(';')[^1].Trim();
        Assert.Equal(expected, Get(document, property));
    }

    [Fact]
    public void Units_DefaultFonts()
    {
        var document = Load("<div id=serif style='width: 1ex; height: 1ch; max-width: 1cap; margin-left: 1lh'></div><div id=mono style='font-family: monospace; height: 1ch'></div>");
        var tinos = BundledFonts.GetFamilyFonts("Tinos")[0].Font;
        var metrics = tinos.Metrics;
        Assert.Equal(Pixels(16 * Ratio(tinos, metrics.XHeight!.Value)), Get(document, "width", "#serif"));
        Assert.Equal(Pixels(16 * Ratio(tinos, metrics.CapHeight!.Value)), Get(document, "max-width", "#serif"));
        Assert.Equal(Pixels(16 * Ratio(tinos, tinos.GetAdvanceWidth(tinos.GetGlyphId('0')))), Get(document, "height", "#serif"));
        // The ascent, the descent and the line gap of the normal line height are rounded to whole pixels, as in Blink
        Assert.Equal(Pixels(Math.Round(16 * Ratio(tinos, metrics.Ascent), MidpointRounding.AwayFromZero) + Math.Round(16 * Ratio(tinos, metrics.Descent), MidpointRounding.AwayFromZero) + Math.Round(16 * Ratio(tinos, metrics.LineGap), MidpointRounding.AwayFromZero)), Get(document, "margin-left", "#serif"));

        // The default font size of the monospace generic family is 13px
        var cousine = BundledFonts.GetFamilyFonts("Cousine")[0].Font;
        Assert.Equal(Pixels(13 * Ratio(cousine, cousine.GetAdvanceWidth(cousine.GetGlyphId('0')))), Get(document, "height", "#mono"));

        // The root units and the media queries use the default font
        Assert.Equal(16 * BundledFonts.DefaultRatios.XHeight, GetStyle(document, "#serif").GetFontRelativeLength(FontRelativeUnit.Ex), 6);
    }

    [Fact]
    public void Units_VerticalWritingMode()
    {
        // In vertical text, ch is the advance height of "0" when it is upright (Tinos has no vmtx table: the ascent plus the descent)
        var document = Load("<div id=target style='writing-mode: vertical-rl; text-orientation: upright; width: 1ch'></div><div id=sideways style='writing-mode: vertical-rl; width: 1ch'></div>");
        var tinos = BundledFonts.GetFamilyFonts("Tinos")[0].Font;
        Assert.Equal(Pixels(16 * Ratio(tinos, tinos.GetAdvanceHeight(tinos.GetGlyphId('0')))), Get(document, "width"));
        Assert.Equal(Pixels(16 * Ratio(tinos, tinos.GetAdvanceWidth(tinos.GetGlyphId('0')))), Get(document, "width", "#sideways"));
    }

    [Fact]
    public void Units_RootUnits()
    {
        var document = Load("<html style='font: 10px Ahem'><body><div id=target style='font-family: serif; width: 1rex; height: 1rch; min-width: 1rcap; max-width: 1ric; margin-left: 1rlh'></div></body></html>");
        Assert.Equal("8px", Get(document, "width"));
        Assert.Equal("10px", Get(document, "height"));
        Assert.Equal("8px", Get(document, "min-width"));
        Assert.Equal("10px", Get(document, "max-width"));
        Assert.Equal("10px", Get(document, "margin-left"));
    }

    [Fact]
    public void FontSizeAdjust_FromFont()
    {
        var document = Load("<div id=target style='font: 20px Ahem; font-size-adjust: from-font'></div><div id=cap style='font-family: Ahem; font-size-adjust: cap-height from-font'></div><div id=ic style='font-family: Ahem; font-size-adjust: ic-width from-font'></div><div id=negative style='font-size-adjust: cap-height calc(-0.5)'></div>");
        Assert.Equal("0.8", Get(document, "font-size-adjust"));
        Assert.Equal("cap-height 0.8", Get(document, "font-size-adjust", "#cap"));
        Assert.Equal("ic-width 1", Get(document, "font-size-adjust", "#ic"));
        Assert.Equal("cap-height 0", Get(document, "font-size-adjust", "#negative"));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Web fonts
    // ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void WebFont_UnitsAreRecomputedWhenTheFontLoads()
    {
        var document = Load("<style>@font-face { font-family: Custom; src: url(ahem.ttf) }</style><div id=target style='font: 20px Custom, monospace; width: 1ch'></div>");

        // The font is loaded when its metrics are needed; while it loads, the next family is used
        var face = document.Fonts.Single();
        var cousine = BundledFonts.GetFamilyFonts("Cousine")[0].Font;
        Assert.Equal(Pixels(20 * Ratio(cousine, cousine.GetAdvanceWidth(cousine.GetGlyphId('0')))), Get(document, "width"));
        Assert.Equal(FontFaceLoadStatus.Loading, face.Status);

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(FontFaceLoadStatus.Loaded, face.Status);
        Assert.Equal("20px", Get(document, "width"));
    }

    [Fact]
    public void WebFont_FailedFontsAreMissing()
    {
        // A family whose faces failed to load is missing: the bundled font of the same name is not used
        var document = Load("<style>@font-face { font-family: Ahem; src: url(missing.woff) }</style><div id=target style='font: 20px Ahem, monospace; width: 1ch'></div>");
        var face = document.Fonts.Single();
        _ = face.Load();
        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal(FontFaceLoadStatus.Error, face.Status);
        Assert.Equal("Cousine Regular", GetStyle(document).Fonts.PrimaryFont.Font.FullName);
    }

    [Fact]
    public void WebFont_SubstitutedWhileLoading()
    {
        // While the faces of a family load, the bundled font of the same name is the substituted font
        var document = Load("<style>@font-face { font-family: Ahem; src: url(ahem.ttf) }</style><div id=target style='font: 20px Ahem, monospace; width: 1ex'></div>");
        Assert.Equal("16px", Get(document, "width"));
        var fonts = GetStyle(document).Fonts;
        Assert.True(fonts.PrimaryFont.IsBundled);

        _contexts.EventLoop.RunUntilIdle();
        fonts = GetStyle(document).Fonts;
        Assert.False(fonts.PrimaryFont.IsBundled);
        Assert.Same(document.Fonts.Single(), fonts.PrimaryFont.Face);
    }

    [Fact]
    public void WebFont_UnicodeRangeAndFirstAvailableFont()
    {
        // The first available font is the first font whose unicode-range includes the space
        var document = Load("<style>@font-face { font-family: Digits; src: local(Ahem); unicode-range: U+30-39 }</style><div id=target style='font: 20px Digits, monospace; width: 1ex; height: 1ch'></div>");
        var cousine = BundledFonts.GetFamilyFonts("Cousine")[0].Font;
        _ = Get(document, "width");
        _ = document.Fonts.Load("20px Digits", "0");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(Pixels(20 * Ratio(cousine, cousine.Metrics.XHeight!.Value)), Get(document, "width"));

        // ch uses the font that renders "0": the face of the family whose unicode-range includes it
        Assert.Equal("20px", Get(document, "height"));
    }

    [Fact]
    public void WebFont_LocalSourcesAreTheBundledFonts()
    {
        var document = Load("<style>@font-face { font-family: A; src: local('Tinos Bold'), url(ahem.ttf) } @font-face { font-family: B; src: local(Unknown), local(Ahem) }</style>");
        var faces = document.Fonts.ToList();
        _ = document.Fonts.Load("10px A, B");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal(FontFaceLoadStatus.Loaded, faces[0].Status);
        Assert.Equal("Tinos Bold", faces[0].FontFile!.Faces[0].FullName);
        Assert.Equal("Ahem", faces[1].FontFile!.Faces[0].FullName);
    }

    [Fact]
    public void WebFont_DescriptorsSelectTheFace()
    {
        var document = Load("""
            <style>
            @font-face { font-family: F; src: local(Tinos-Regular) }
            @font-face { font-family: F; src: local(Tinos-Bold); font-weight: 600 900 }
            @font-face { font-family: F; src: local(Ahem); font-style: italic }
            @font-face { font-family: Over; src: local(Ahem); ascent-override: 50%; descent-override: 10%; line-gap-override: 20%; size-adjust: 50% }
            </style>
            <div id=regular style='font-family: F'>x</div><div id=bold style='font-family: F; font-weight: 800'>x</div><div id=italic style='font-family: F; font-style: italic'>x</div>
            <div id=override style='font: 20px Over; width: 1lh; height: 1ex'>x</div>
            """);
        foreach (var face in document.Fonts)
        {
            _ = face.Load();
        }

        _contexts.EventLoop.RunUntilIdle();
        Assert.Equal("Tinos Regular", GetStyle(document, "#regular").Fonts.PrimaryFont.Font.FullName);
        Assert.Equal("Tinos Bold", GetStyle(document, "#bold").Fonts.PrimaryFont.Font.FullName);
        Assert.Equal("Ahem", GetStyle(document, "#italic").Fonts.PrimaryFont.Font.FullName);

        // The overrides are fractions of the em size (20px * size-adjust 50%)
        Assert.Equal("8px", Get(document, "width", "#override"));
        Assert.Equal("8px", Get(document, "height", "#override"));
    }

    [Fact]
    public void WebFont_ShadowRootFontFacesAreScoped()
    {
        var document = Load("<div id=host><span id=slotted style='font-family: Shadow'>x</span></div><div id=outside style='font-family: Shadow'>x</div>");
        var host = document.GetElementById("host")!;
        var shadow = host.AttachShadow(new ShadowRootInit { Mode = ShadowRootMode.Open });
        shadow.SetHtmlUnsafe("<style>@font-face { font-family: Shadow; src: local(Ahem) } #inner { font-family: Shadow } :host { font-family: Shadow }</style><slot></slot><span id=inner>x</span>");
        var engine = document.GetStyleEngine();
        _ = engine.GetStyle(shadow.GetElementById("inner")!).Fonts.PrimaryFont;
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal("Ahem", engine.GetStyle(shadow.GetElementById("inner")!).Fonts.PrimaryFont.Font.FullName);

        // The family of the host (declared in the shadow tree) is inherited by the slotted element with its tree scope
        Assert.Equal("Ahem", engine.GetStyle(host).Fonts.PrimaryFont.Font.FullName);

        // The @font-face rules of a shadow tree do not apply to the declarations of the document
        Assert.Equal("Tinos Regular", engine.GetStyle(document.GetElementById("slotted")!).Fonts.PrimaryFont.Font.FullName);
        Assert.Equal("Tinos Regular", engine.GetStyle(document.GetElementById("outside")!).Fonts.PrimaryFont.Font.FullName);
    }

    [Fact]
    public async Task FontFaceSet_CheckUsesTheFontMatching()
    {
        var document = Load("<style>@font-face { font-family: A; src: url(a.ttf); font-weight: 100 500 } @font-face { font-family: A; src: url(b.ttf); font-weight: 600 900 } @font-face { font-family: A; src: url(c.ttf); font-style: italic }</style>");
        var faces = document.Fonts.ToList();
        var task = document.Fonts.Load("300 10px A");
        _contexts.EventLoop.RunUntilIdle();

        Assert.Equal([faces[0]], await task);
        Assert.True(document.Fonts.Check("300 10px A"));
        Assert.True(document.Fonts.Check("450 10px A"));
        Assert.False(document.Fonts.Check("bold 10px A"));
        Assert.False(document.Fonts.Check("italic 10px A"));
        Assert.True(document.Fonts.Check("10px sans-serif"));
    }
}
