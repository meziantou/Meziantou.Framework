using System.Globalization;
using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Layout;

using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The font of a 2D context (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font): its serialization and
/// the style that holds its computed values (the font properties of the <c>font</c> shorthand, inheriting from the canvas
/// element), which gives the fonts, the metrics and the font-relative lengths.
/// </summary>
internal sealed class CanvasFont(string serialization, ComputedStyle style)
{
    private ComputedStyle? _stretchedStyle;
    private string? _stretch;

    public string Serialization { get; } = serialization;

    public ComputedStyle Style { get; } = style;

    /// <summary>The computed font size in CSS px.</summary>
    public double Size => Style.FontSize;

    /// <summary>Returns the style of the font with another value of <c>font-width</c> (<c>fontStretch</c>).</summary>
    public ComputedStyle GetStyle(string? fontWidth)
    {
        if (fontWidth is null || fontWidth == Style.GetComputedValue("font-width"))
            return Style;

        if (_stretch != fontWidth)
        {
            _stretch = fontWidth;
            _stretchedStyle = Style.WithDeclarations([new CssLonghandValue("font-width", fontWidth)]);
        }

        return _stretchedStyle!;
    }
}

/// <summary>
/// The measurements of a text (https://html.spec.whatwg.org/multipage/canvas.html#textmetrics), returned by <c>measureText()</c>:
/// the distances are in CSS px, relative to the alignment point and to the baseline of the text attributes of the context.
/// </summary>
[IdlInterface("TextMetrics", "https://html.spec.whatwg.org/multipage/canvas.html#textmetrics")]
public sealed class TextMetrics
{
    internal TextMetrics()
    {
    }

    /// <summary>Gets the advance width of the text.</summary>
    [IdlMember("width", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-width")]
    public double Width { get; internal init; }

    /// <summary>Gets the distance from the alignment point to the left of the bounding box of the glyphs (positive to the left).</summary>
    [IdlMember("actualBoundingBoxLeft", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-actualboundingboxleft")]
    public double ActualBoundingBoxLeft { get; internal init; }

    /// <summary>Gets the distance from the alignment point to the right of the bounding box of the glyphs (positive to the right).</summary>
    [IdlMember("actualBoundingBoxRight", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-actualboundingboxright")]
    public double ActualBoundingBoxRight { get; internal init; }

    /// <summary>Gets the distance from the baseline to the ascent of the first available font (positive upwards).</summary>
    [IdlMember("fontBoundingBoxAscent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-fontboundingboxascent")]
    public double FontBoundingBoxAscent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the descent of the first available font (positive downwards).</summary>
    [IdlMember("fontBoundingBoxDescent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-fontboundingboxdescent")]
    public double FontBoundingBoxDescent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the top of the bounding box of the glyphs (positive upwards).</summary>
    [IdlMember("actualBoundingBoxAscent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-actualboundingboxascent")]
    public double ActualBoundingBoxAscent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the bottom of the bounding box of the glyphs (positive downwards).</summary>
    [IdlMember("actualBoundingBoxDescent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-actualboundingboxdescent")]
    public double ActualBoundingBoxDescent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the top of the em square (positive when the baseline is below it).</summary>
    [IdlMember("emHeightAscent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-emheightascent")]
    public double EmHeightAscent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the bottom of the em square (positive when the baseline is above it).</summary>
    [IdlMember("emHeightDescent", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-emheightdescent")]
    public double EmHeightDescent { get; internal init; }

    /// <summary>Gets the distance from the baseline to the hanging baseline (positive when the baseline is below it).</summary>
    [IdlMember("hangingBaseline", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-hangingbaseline")]
    public double HangingBaseline { get; internal init; }

    /// <summary>Gets the distance from the baseline to the alphabetic baseline (positive when the baseline is below it).</summary>
    [IdlMember("alphabeticBaseline", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-alphabeticbaseline")]
    public double AlphabeticBaseline { get; internal init; }

    /// <summary>Gets the distance from the baseline to the ideographic-under baseline (positive when the baseline is below it).</summary>
    [IdlMember("ideographicBaseline", "https://html.spec.whatwg.org/multipage/canvas.html#dom-textmetrics-ideographicbaseline")]
    public double IdeographicBaseline { get; internal init; }
}

// The text of the 2D contexts (https://html.spec.whatwg.org/multipage/canvas.html#text-styles,
// https://html.spec.whatwg.org/multipage/canvas.html#drawing-text-to-the-bitmap) and the filters (https://html.spec.whatwg.org/multipage/canvas.html#filters)
public abstract partial class CanvasRenderingContext2DBase
{
    /// <summary>The name of the pseudo-element of the styles of the fonts of the canvases (no rule matches it).</summary>
    private const string FontPseudoElement = "-internal-canvas-font";

    /// <summary>The default font (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font).</summary>
    private const string DefaultFont = "10px sans-serif";

    /// <summary>The documents that hold the fonts of the canvases of the global objects that are neither a window nor a worker, e.g. a worklet (the bundled fonts only).</summary>
    private static readonly ConditionalWeakTable<GlobalScope, Document> WorkerFontDocuments = [];

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasTextDrawingStyles (https://html.spec.whatwg.org/multipage/canvas.html#text-styles)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the font (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font): a value of the CSS
    /// <c>font</c> shorthand (the relative values are resolved against the font of the canvas element, or <c>10px sans-serif</c>),
    /// serialized with its size in px and without line height. The values that cannot be parsed are ignored. Setting the font
    /// resets <see cref="FontStretch"/> and <see cref="FontVariantCaps"/> to the values of the shorthand (as in the browsers, <see cref="FontKerning"/> is kept).
    /// </summary>
    [IdlMember("font", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font")]
    public string Font
    {
        get => _state.Font?.Serialization ?? DefaultFont;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (ResolveFont(value) is not { } font)
                return;

            _state.Font = font;
            _state.FontStretch = GetFontStretch(font.Style.GetComputedValue("font-width")) ?? CanvasFontStretch.Normal;
            _state.FontVariantCaps = font.Style.GetComputedValue("font-variant-caps") == "small-caps" ? CanvasFontVariantCaps.SmallCaps : CanvasFontVariantCaps.Normal;
        }
    }

    /// <summary>Gets or sets the alignment of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign).</summary>
    [IdlMember("textAlign", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign")]
    public CanvasTextAlign TextAlign
    {
        get => _state.TextAlign;
        set => _state.TextAlign = value;
    }

    /// <summary>Gets or sets the baseline of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline).</summary>
    [IdlMember("textBaseline", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline")]
    public CanvasTextBaseline TextBaseline
    {
        get => _state.TextBaseline;
        set => _state.TextBaseline = value;
    }

    /// <summary>Gets or sets the direction of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction).</summary>
    [IdlMember("direction", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction")]
    public CanvasDirection Direction
    {
        get => _state.Direction;
        set => _state.Direction = value;
    }

    /// <summary>Gets or sets the language of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lang): a BCP 47 language tag, or <c>inherit</c>.</summary>
    [IdlMember("lang", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lang")]
    public string Lang
    {
        get => _state.Lang;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _state.Lang = value;
        }
    }

    /// <summary>
    /// Gets or sets the spacing between the letters (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-letterspacing):
    /// a CSS length (the font-relative lengths are resolved against the current font when the text is drawn); other values are ignored.
    /// </summary>
    [IdlMember("letterSpacing", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-letterspacing")]
    public string LetterSpacing
    {
        get => _state.LetterSpacing;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (NormalizeSpacing(value) is { } spacing)
            {
                _state.LetterSpacing = spacing;
            }
        }
    }

    /// <summary>
    /// Gets or sets the spacing between the words (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-wordspacing):
    /// a CSS length (the font-relative lengths are resolved against the current font when the text is drawn); other values are ignored.
    /// </summary>
    [IdlMember("wordSpacing", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-wordspacing")]
    public string WordSpacing
    {
        get => _state.WordSpacing;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (NormalizeSpacing(value) is { } spacing)
            {
                _state.WordSpacing = spacing;
            }
        }
    }

    /// <summary>Gets or sets the kerning of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontkerning).</summary>
    [IdlMember("fontKerning", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontkerning")]
    public CanvasFontKerning FontKerning
    {
        get => _state.FontKerning;
        set => _state.FontKerning = value;
    }

    /// <summary>Gets or sets the width of the font (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch).</summary>
    [IdlMember("fontStretch", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch")]
    public CanvasFontStretch FontStretch
    {
        get => _state.FontStretch;
        set => _state.FontStretch = value;
    }

    /// <summary>Gets or sets the capitals of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps): the OpenType features of the capitals (the small capitals are not synthesized).</summary>
    [IdlMember("fontVariantCaps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps")]
    public CanvasFontVariantCaps FontVariantCaps
    {
        get => _state.FontVariantCaps;
        set => _state.FontVariantCaps = value;
    }

    /// <summary>Gets or sets the rendering hint of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering): <c>optimizeSpeed</c> disables the kerning, as <c>text-rendering</c>.</summary>
    [IdlMember("textRendering", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering")]
    public CanvasTextRendering TextRendering
    {
        get => _state.TextRendering;
        set => _state.TextRendering = value;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasText (https://html.spec.whatwg.org/multipage/canvas.html#drawing-text-to-the-bitmap)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Fills a text at a point with the fill style (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-filltext).</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The x coordinate of the alignment point.</param>
    /// <param name="y">The y coordinate of the baseline.</param>
    /// <param name="maxWidth">The maximum width: a wider text is compressed horizontally; nothing is drawn when it is not positive.</param>
    [IdlMember("fillText", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-filltext")]
    public void FillText(string text, double x, double y, double? maxWidth = null) => DrawText(text, x, y, maxWidth, stroke: false);

    /// <summary>Strokes a text at a point with the stroke style and the line styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroketext).</summary>
    /// <param name="text">The text.</param>
    /// <param name="x">The x coordinate of the alignment point.</param>
    /// <param name="y">The y coordinate of the baseline.</param>
    /// <param name="maxWidth">The maximum width: a wider text is compressed horizontally; nothing is drawn when it is not positive.</param>
    [IdlMember("strokeText", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-stroketext")]
    public void StrokeText(string text, double x, double y, double? maxWidth = null) => DrawText(text, x, y, maxWidth, stroke: true);

    /// <summary>Measures a text with the current font and text styles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-measuretext).</summary>
    [IdlMember("measureText", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-measuretext")]
    public TextMetrics MeasureText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var layout = PrepareText(text);
        var alignment = GetAlignmentOffset(layout);
        var baseline = GetBaselineOffset(layout);

        // The bounding box of the glyphs (the union of the outlines of the visible glyphs), relative to the alignment point
        double left = 0, right = 0, top = 0, bottom = 0;
        var hasInk = false;
        foreach (var glyph in layout.Glyphs)
        {
            if (!glyph.Font.Font.TryGetGlyphBounds(glyph.GlyphId, out var bounds) || (bounds.XMin == bounds.XMax && bounds.YMin == bounds.YMax))
                continue;

            var scale = glyph.EmSize / glyph.Font.Font.UnitsPerEm;
            var glyphLeft = glyph.X + (bounds.XMin * scale);
            var glyphRight = glyph.X + (bounds.XMax * scale);
            var glyphTop = (bounds.YMax * scale) - glyph.OffsetY;
            var glyphBottom = (bounds.YMin * scale) - glyph.OffsetY;
            if (!hasInk)
            {
                (left, right, top, bottom) = (glyphLeft, glyphRight, glyphTop, glyphBottom);
                hasInk = true;
            }
            else
            {
                left = Math.Min(left, glyphLeft);
                right = Math.Max(right, glyphRight);
                top = Math.Max(top, glyphTop);
                bottom = Math.Min(bottom, glyphBottom);
            }
        }

        var metrics = layout.Metrics;
        return new TextMetrics
        {
            Width = layout.Width,
            ActualBoundingBoxLeft = hasInk ? alignment - left : 0,
            ActualBoundingBoxRight = hasInk ? right - alignment : 0,
            ActualBoundingBoxAscent = hasInk ? top - baseline : -baseline,
            ActualBoundingBoxDescent = hasInk ? baseline - bottom : baseline,
            FontBoundingBoxAscent = metrics.Ascent - baseline,
            FontBoundingBoxDescent = metrics.Descent + baseline,
            EmHeightAscent = layout.EmAscent - baseline,
            EmHeightDescent = layout.EmDescent + baseline,
            HangingBaseline = layout.Hanging - baseline,
            AlphabeticBaseline = -baseline,
            IdeographicBaseline = layout.Ideographic - baseline,
        };
    }

    /// <summary>The drawing of fillText() and strokeText(): the glyphs of the text are drawn through the drawing model, in user space.</summary>
    private void DrawText(string text, double x, double y, double? maxWidth, bool stroke)
    {
        ArgumentNullException.ThrowIfNull(text);

        // 1. Non-finite arguments (and a maximum width that is not positive) draw nothing
        if (!double.IsFinite(x) || !double.IsFinite(y) || maxWidth is { } max && !(max > 0))
            return;

        if (maxWidth is { } infinite && double.IsPositiveInfinity(infinite))
        {
            maxWidth = null;
        }

        if (CreatePaint(stroke ? _state.StrokeStyle : _state.FillStyle, stroke) is not { } paint || Invert(_state.Transform) is null)
            return;

        var layout = PrepareText(text);
        if (layout.Glyphs.Count == 0)
            return;

        // 2-3. The anchor point (the alignment point on the baseline); a text wider than the maximum width is compressed horizontally
        var scaleX = maxWidth is { } limit && layout.Width > limit ? limit / layout.Width : 1;
        var originX = x - (GetAlignmentOffset(layout) * scaleX);
        var originY = y + GetBaselineOffset(layout);
        var runs = new List<(GlyphRun Run, FontPalette? Palette)>();
        var start = 0;
        while (start < layout.Glyphs.Count)
        {
            var font = layout.Glyphs[start].Font;
            var emSize = layout.Glyphs[start].EmSize;
            var end = start + 1;
            while (end < layout.Glyphs.Count && ReferenceEquals(layout.Glyphs[end].Font, font) && layout.Glyphs[end].EmSize == emSize)
            {
                end++;
            }

            var ids = new ushort[end - start];
            var positions = new PhysicalOffset[end - start];
            for (var i = start; i < end; i++)
            {
                var glyph = layout.Glyphs[i];
                ids[i - start] = glyph.GlyphId;
                positions[i - start] = new PhysicalOffset(originX + (glyph.X * scaleX), originY + glyph.OffsetY);
            }

            runs.Add((new GlyphRun(DisplayFont.From(font, emSize), ids, positions, text) { ScaleX = scaleX }, FontPalette.GetDefault(font)));
            start = end;
        }

        DrawShape(builder =>
        {
            builder.Save();
            builder.Transform(_state.Transform);
            foreach (var (run, palette) in runs)
            {
                if (palette is null || !palette.HasColorGlyph(run))
                {
                    builder.Add(new DrawGlyphsItem(run, paint));
                    continue;
                }

                // The color glyphs are filled with the colors of the default palette of their font, and are not stroked
                foreach (var (layerRun, layerPaint) in palette.SplitColorGlyphs(run, paint, colorize: !stroke))
                {
                    builder.Add(new DrawGlyphsItem(layerRun, layerPaint));
                }
            }

            builder.Restore();
        });
    }

    /// <summary>The distance of the alignment point from the left of the text (https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm, step 9).</summary>
    private static double GetAlignmentOffset(TextLayout layout)
    {
        var align = layout.Align switch
        {
            CanvasTextAlign.Start => layout.IsRightToLeft ? CanvasTextAlign.Right : CanvasTextAlign.Left,
            CanvasTextAlign.End => layout.IsRightToLeft ? CanvasTextAlign.Left : CanvasTextAlign.Right,
            var value => value,
        };

        return align switch
        {
            CanvasTextAlign.Right => layout.Width,
            CanvasTextAlign.Center => layout.Width / 2,
            _ => 0,
        };
    }

    /// <summary>
    /// The distance of the baseline of <c>textBaseline</c> above the alphabetic baseline (https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm,
    /// step 10): the text is drawn this distance below the given y coordinate.
    /// </summary>
    private static double GetBaselineOffset(TextLayout layout) => layout.Baseline switch
    {
        CanvasTextBaseline.Top => layout.EmAscent,
        CanvasTextBaseline.Hanging => layout.Hanging,
        CanvasTextBaseline.Middle => (layout.EmAscent - layout.EmDescent) / 2,
        CanvasTextBaseline.Ideographic => layout.Ideographic,
        CanvasTextBaseline.Bottom => -layout.EmDescent,
        _ => 0,
    };

    /// <summary>
    /// The text preparation algorithm (https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm): the white
    /// space is replaced with spaces, the text is shaped with the font and the text styles in the visual order of the bidi
    /// algorithm. The glyphs are positioned from the left of the text, on the alphabetic baseline.
    /// </summary>
    private TextLayout PrepareText(string text)
    {
        // 1. The ASCII white space characters are spaces (and, as in the browsers, the null characters have no width)
        var prepared = string.Create(text.Length, text, static (span, source) =>
        {
            for (var i = 0; i < source.Length; i++)
            {
                var c = source[i];
                span[i] = c is '\t' or '\n' or '\f' or '\r' ? ' ' : c;
            }
        });

        if (prepared.Contains('\0', StringComparison.Ordinal))
        {
            prepared = prepared.Replace("\0", "", StringComparison.Ordinal);
        }

        // 2-4. The font, the direction and the spacing
        // The fonts are matched each time (a web font may have been loaded since the font was set)
        var font = GetFont();
        var style = font.GetStyle(GetFontWidth(_state.FontStretch));
        var fonts = style.Engine.Document.FontSelector.GetFonts(FontDescription.FromStyle(style));
        var isRightToLeft = _state.Direction switch
        {
            CanvasDirection.Rtl => true,
            CanvasDirection.Ltr => false,
            _ => IsInheritedDirectionRightToLeft(),
        };

        // https://html.spec.whatwg.org/multipage/canvas.html#text-preparation-algorithm: the font features of the font, of
        // fontVariantCaps, of textRendering and of letterSpacing, and the language of lang
        var letterSpacing = ResolveSpacing(style, "letter-spacing", _state.LetterSpacing);
        var textRendering = _state.TextRendering == CanvasTextRendering.OptimizeSpeed ? "optimizeSpeed" : "auto";
        var options = new TextShapingOptions
        {
            FontSize = style.FontSize,
            LetterSpacing = letterSpacing,
            WordSpacing = ResolveSpacing(style, "word-spacing", _state.WordSpacing),
            Kerning = _state.FontKerning != CanvasFontKerning.None && !(_state.FontKerning == CanvasFontKerning.Auto && _state.TextRendering == CanvasTextRendering.OptimizeSpeed),
            KerningRequested = _state.FontKerning == CanvasFontKerning.Normal,
            Features = TextShapingOptions.GetFeatures(
                textRendering,
                style.GetComputedValue("font-variant-ligatures"),
                GetVariantCaps(_state.FontVariantCaps),
                style.GetComputedValue("font-variant-numeric"),
                style.GetComputedValue("font-variant-east-asian"),
                style.GetComputedValue("font-variant-position"),
                alternates: null,
                disableOptionalLigatures: letterSpacing != 0,
                style.GetComputedValue("font-feature-settings")),
            Language = GetLanguage(),
            Caps = TextShapingOptions.ParseCaps(GetVariantCaps(_state.FontVariantCaps)),
            Position = TextShapingOptions.ParsePosition(style.GetComputedValue("font-variant-position")),
            SynthesizeSmallCaps = style.GetComputedValue("font-synthesis-small-caps") != "none",
            SynthesizePosition = style.GetComputedValue("font-synthesis-position") != "none",
            Emoji = TextShapingOptions.ParseEmoji(style.GetComputedValue("font-variant-emoji")),
        };

        // The metrics of the first available font: the em square is the ascent and the descent scaled to the font size
        var primary = fonts.PrimaryFont;
        var metrics = primary.GetMetrics(style.FontSize);
        var unitMetrics = metrics.FontUnitMetrics;
        var emSquare = unitMetrics.Ascent + unitMetrics.Descent;
        var emAscent = emSquare > 0 ? style.FontSize * unitMetrics.Ascent / emSquare : style.FontSize;
        var emDescent = emSquare > 0 ? style.FontSize * unitMetrics.Descent / emSquare : 0;
        var baselines = primary.Font.GetBaselines();
        var hanging = baselines.Hanging is { } hang ? hang * metrics.Scale : metrics.Ascent * 0.8;
        var ideographic = baselines.Ideographic is { } ideo ? ideo * metrics.Scale : -metrics.Descent;

        // 5. The bidi algorithm: the runs of the text are shaped in their direction and placed in visual order
        var glyphs = new List<PositionedGlyph>(prepared.Length);
        var x = 0d;
        if (prepared.Length > 0)
        {
            var levels = BidiResolver.GetLevels(prepared, isRightToLeft ? BidiParagraphDirection.RightToLeft : BidiParagraphDirection.LeftToRight);
            var runs = new List<BidiRun>();
            BidiResolver.GetVisualRuns(levels, runs);
            foreach (var run in runs)
            {
                var runText = prepared.AsSpan(run.Start, run.End - run.Start);
                if (run.IsRightToLeft)
                {
                    runText = Mirror(runText);
                }

                var shaped = TextShaper.Shape(runText, fonts, options with { IsRightToLeft = run.IsRightToLeft });
                var pens = shaped.Glyphs;
                var pen = 0d;
                foreach (var fontRun in shaped.Runs)
                {
                    for (var i = fontRun.GlyphStart; i < fontRun.GlyphStart + fontRun.GlyphCount; i++)
                    {
                        var glyph = pens[i];
                        var glyphX = run.IsRightToLeft ? x + shaped.Width - pen - glyph.Advance : x + pen;
                        pen += glyph.Advance;
                        if (!glyph.IsInvisible)
                        {
                            glyphs.Add(new PositionedGlyph(fontRun.Font, fontRun.EmSize, glyph.GlyphId, glyphX + glyph.OffsetX, glyph.OffsetY));
                        }
                    }
                }

                x += shaped.Width;
            }
        }

        // The glyphs are drawn from left to right (the right-to-left runs were added in logical order)
        glyphs.Sort(static (a, b) => a.X.CompareTo(b.X));
        return new TextLayout(glyphs, x, isRightToLeft, _state.TextAlign, _state.TextBaseline, metrics, emAscent, emDescent, hanging, ideographic);

        static ReadOnlySpan<char> Mirror(ReadOnlySpan<char> text)
        {
            char[]? mirrored = null;
            for (var i = 0; i < text.Length; i++)
            {
                if (UnicodeProperties.IsBidiMirrored(text[i]) && UnicodeProperties.GetBidiMirroringGlyph(text[i]) is var mirror and >= 0 and <= 0xFFFF)
                {
                    mirrored ??= text.ToArray();
                    mirrored[i] = (char)mirror;
                }
            }

            return mirrored ?? text;
        }
    }

    /// <summary>The value of <c>font-variant-caps</c> of a value of <see cref="FontVariantCaps"/>.</summary>
    private static string GetVariantCaps(CanvasFontVariantCaps caps) => caps switch
    {
        CanvasFontVariantCaps.SmallCaps => "small-caps",
        CanvasFontVariantCaps.AllSmallCaps => "all-small-caps",
        CanvasFontVariantCaps.PetiteCaps => "petite-caps",
        CanvasFontVariantCaps.AllPetiteCaps => "all-petite-caps",
        CanvasFontVariantCaps.Unicase => "unicase",
        CanvasFontVariantCaps.TitlingCaps => "titling-caps",
        _ => "normal",
    };

    /// <summary>
    /// The language of the texts (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-lang): the value of
    /// <see cref="Lang"/>, or for <c>inherit</c> the language of the canvas element (for an <see cref="OffscreenCanvas"/>, the
    /// language of its placeholder canvas element when its control was transferred, else of the document element of its window).
    /// </summary>
    private string? GetLanguage()
    {
        if (!string.Equals(_state.Lang, "inherit", StringComparison.Ordinal))
            return _state.Lang.Length == 0 ? null : _state.Lang;

        if (CanvasElement is { } element)
            return Css.Selectors.HtmlElementStates.GetLanguage(element);

        if (this is OffscreenCanvasRenderingContext2D { Canvas.InheritedLanguage: { } inherited })
            return inherited.Length == 0 ? null : inherited;

        // An OffscreenCanvas of a window inherits the language of the document element
        return Global is Window { Document.DocumentElement: { } root } ? Css.Selectors.HtmlElementStates.GetLanguage(root) : null;
    }

    /// <summary>
    /// The direction of <c>direction: inherit</c> (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction-inherit):
    /// the computed direction of the canvas element, or its directionality when it is not connected; the direction of the
    /// placeholder canvas element of an <see cref="OffscreenCanvas"/> when its control was transferred, else left to right.
    /// </summary>
    private bool IsInheritedDirectionRightToLeft()
    {
        if (CanvasElement is { } element)
            return GetElementDirection(element);

        return this is OffscreenCanvasRenderingContext2D { Canvas.InheritsRightToLeft: true };
    }

    /// <summary>Returns whether the direction of an element is right to left: its computed direction when it is connected, else its directionality.</summary>
    internal static bool GetElementDirection(Element element)
        => element.IsConnected ? element.NodeDocument.GetStyleEngine().GetStyle(element).GetComputedValue("direction") == "rtl" : !Css.Selectors.HtmlElementStates.IsLeftToRight(element);

    /// <summary>A glyph of a prepared text: its font, its id, and its position from the left of the text and below the alphabetic baseline.</summary>
    private readonly record struct PositionedGlyph(MatchedFont Font, double EmSize, ushort GlyphId, double X, double OffsetY);

    /// <summary>A prepared text: the glyphs, the width, the direction and the metrics (above the alphabetic baseline, in CSS px).</summary>
    private sealed record TextLayout(List<PositionedGlyph> Glyphs, double Width, bool IsRightToLeft, CanvasTextAlign Align, CanvasTextBaseline Baseline, UsedFontMetrics Metrics, double EmAscent, double EmDescent, double Hanging, double Ideographic);

    /// <summary>Returns the font of the drawing state, resolving the default font the first time.</summary>
    private CanvasFont GetFont()
    {
        if (_state.Font is { } font)
            return font;

        _state.Font = ResolveFont(DefaultFont) ?? throw new InvalidOperationException("The default font cannot be resolved");
        return _state.Font;
    }

    /// <summary>
    /// Parses a value of the <c>font</c> shorthand and computes it (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font),
    /// or returns null when it is invalid (the CSS-wide keywords and the variables are invalid). The relative values inherit
    /// from the computed font of the canvas element when it is connected, else from <c>10px sans-serif</c>.
    /// </summary>
    private CanvasFont? ResolveFont(string value)
    {
        var components = CssParser.ParseListOfComponentValues(value, out var source);
        if (!IsValidFontValue(components) || CssShorthands.ParseValue("font", components, source.Trim()) is not { } longhands)
            return null;

        var (engine, element, parent) = GetFontEnvironment();
        var style = CreateFontStyle(engine, element, parent, longhands);
        return new CanvasFont(SerializeFont(style), style);
    }

    /// <summary>Whether a value can be a value of a canvas attribute (<c>font</c>, <c>filter</c>): not a CSS-wide keyword, without variable nor element-dependent function.</summary>
    private static bool IsValidFontValue(List<CssComponentValue> components)
    {
        var significant = ValueMatcher.RemoveWhitespace(components);
        if (significant.Count == 0)
            return false;

        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } ident] && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(ident.Token.Value)))
            return false;

        return !ContainsFunction(components);

        static bool ContainsFunction(List<CssComponentValue> values)
        {
            foreach (var value in values)
            {
                // The variables and the element-dependent functions (the canvas has no element style)
                if (value is CssFunction function && (InfraAscii.ToAsciiLowercase(function.Name) is "var" or "env" or "attr" or "sibling-index" or "sibling-count" || ContainsFunction(function.Value)))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the style engine, an element and the parent style the fonts of the canvas are computed with: the computed style of
    /// the canvas element when it is connected, else a style of the default font (in the document of the canvas element, of the
    /// window of an <c>OffscreenCanvas</c>, or in the document of the font source of the worker, see <see cref="WorkerGlobalScope.FontSourceDocument"/>).
    /// </summary>
    private (StyleEngine Engine, Element Element, ComputedStyle Parent) GetFontEnvironment()
    {
        if (CanvasElement is { } canvas)
        {
            var engine = canvas.NodeDocument.GetStyleEngine();
            if (canvas.IsConnected)
                return (engine, canvas, engine.GetStyle(canvas));

            return (engine, canvas, CreateDefaultFontStyle(engine, canvas));
        }

        var document = Global switch
        {
            Window window => window.Document,
            WorkerGlobalScope worker => worker.FontSourceDocument,
            { } global => WorkerFontDocuments.GetValue(global, static _ => new Document()),
            _ => new Document(),
        };

        var documentEngine = document.GetStyleEngine();
        var element = document.DocumentElement ?? document.CreateElement("canvas");
        return (documentEngine, element, CreateDefaultFontStyle(documentEngine, element));
    }

    private static ComputedStyle CreateDefaultFontStyle(StyleEngine engine, Element element)
    {
        var longhands = CssShorthands.ParseValue("font", CssParser.ParseListOfComponentValues(DefaultFont), DefaultFont)!;
        return CreateFontStyle(engine, element, parent: null, longhands);
    }

    private static ComputedStyle CreateFontStyle(StyleEngine engine, Element element, ComputedStyle? parent, List<CssLonghandValue> longhands)
    {
        var cascaded = new Dictionary<string, CascadedValue>(longhands.Count + 1, StringComparer.Ordinal);
        var rank = 0L;
        foreach (var longhand in longhands)
        {
            cascaded[longhand.Name] = new CascadedValue(longhand, rank++, source: null);
        }

        // https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font: "with the 'line-height' component forced to 'normal'"
        cascaded["line-height"] = new CascadedValue(new CssLonghandValue("line-height", "normal"), rank, source: null);
        return new ComputedStyle(engine, element, FontPseudoElement, parent, cascaded);
    }

    /// <summary>
    /// Serializes the computed font (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-font): the style, the
    /// small capitals, the weight and the width when they are not normal, the size in px and the families.
    /// </summary>
    private static string SerializeFont(ComputedStyle style)
    {
        var parts = new List<string>(6);
        var fontStyle = style.GetComputedValue("font-style");
        if (fontStyle != "normal")
        {
            parts.Add(fontStyle);
        }

        if (style.GetComputedValue("font-variant-caps") == "small-caps")
        {
            parts.Add("small-caps");
        }

        // As in the browsers, the weight 700 is serialized as bold
        var weight = style.GetComputedValue("font-weight");
        if (weight != "400" && weight != "normal")
        {
            parts.Add(weight == "700" ? "bold" : weight);
        }

        if (GetFontStretch(style.GetComputedValue("font-width")) is { } stretch && stretch != CanvasFontStretch.Normal)
        {
            parts.Add(GetFontWidth(stretch));
        }

        parts.Add(CssValueComputer.FormatPixels(style.FontSize));
        parts.Add(style.GetComputedValue("font-family"));
        return string.Join(' ', parts);
    }

    /// <summary>Returns the keyword of a computed value of <c>font-width</c>, or null when it is not one of the keywords.</summary>
    private static CanvasFontStretch? GetFontStretch(string fontWidth) => fontWidth switch
    {
        "50%" or "ultra-condensed" => CanvasFontStretch.UltraCondensed,
        "62.5%" or "extra-condensed" => CanvasFontStretch.ExtraCondensed,
        "75%" or "condensed" => CanvasFontStretch.Condensed,
        "87.5%" or "semi-condensed" => CanvasFontStretch.SemiCondensed,
        "100%" or "normal" => CanvasFontStretch.Normal,
        "112.5%" or "semi-expanded" => CanvasFontStretch.SemiExpanded,
        "125%" or "expanded" => CanvasFontStretch.Expanded,
        "150%" or "extra-expanded" => CanvasFontStretch.ExtraExpanded,
        "200%" or "ultra-expanded" => CanvasFontStretch.UltraExpanded,
        _ => null,
    };

    private static string GetFontWidth(CanvasFontStretch stretch) => stretch switch
    {
        CanvasFontStretch.UltraCondensed => "ultra-condensed",
        CanvasFontStretch.ExtraCondensed => "extra-condensed",
        CanvasFontStretch.Condensed => "condensed",
        CanvasFontStretch.SemiCondensed => "semi-condensed",
        CanvasFontStretch.SemiExpanded => "semi-expanded",
        CanvasFontStretch.Expanded => "expanded",
        CanvasFontStretch.ExtraExpanded => "extra-expanded",
        CanvasFontStretch.UltraExpanded => "ultra-expanded",
        _ => "normal",
    };

    /// <summary>
    /// Validates and normalizes a value of <c>letterSpacing</c> or <c>wordSpacing</c>: a CSS length (a dimension with a length
    /// unit, or zero), serialized with its unit in lowercase; null when it is not a length.
    /// </summary>
    private static string? NormalizeSpacing(string value)
    {
        if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)) is not [CssPreservedToken token])
            return null;

        if (token.Type == CssTokenType.Number && token.Token.NumericValue == 0)
            return "0px";

        if (token.Type != CssTokenType.Dimension)
            return null;

        var unit = InfraAscii.ToAsciiLowercase(token.Token.Value);
        if (!ValueMatcher.IsLengthUnit(unit))
            return null;

        return CssValueComputer.SerializeNumber(token.Token.NumericValue) + unit;
    }

    /// <summary>Resolves a spacing length to CSS px with the font of the text (its font-relative lengths).</summary>
    private static double ResolveSpacing(ComputedStyle style, string property, string value)
    {
        if (value == "0px")
            return 0;

        if (value.EndsWith("px", StringComparison.Ordinal) && double.TryParse(value.AsSpan(0, value.Length - 2), NumberStyles.Float, CultureInfo.InvariantCulture, out var pixels))
            return pixels;

        return CssValueComputer.ParsePixels(style.WithDeclarations([new CssLonghandValue(property, value)]).GetComputedValue(property)) ?? 0;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasFilters (https://html.spec.whatwg.org/multipage/canvas.html#filters)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Gets or sets the filter of the drawing operations (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-filter):
    /// <c>none</c> or a list of CSS filter functions (the lengths are resolved against the current font); the other values are
    /// ignored. The references to SVG filter elements are not supported (the drawing is not filtered).
    /// </summary>
    [IdlMember("filter", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-filter")]
    public string Filter
    {
        get => _state.Filter;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value));
            if (components is [CssPreservedToken { Type: CssTokenType.Ident } ident] && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "none"))
            {
                _state.Filter = "none";
                return;
            }

            // A <filter-value-list> (https://drafts.fxtf.org/filter-effects/#typedef-filter-value-list): a valid value of the
            // filter property that is not a CSS-wide keyword and has no variable
            if (!IsValidFontValue(components) || CssShorthands.ParseValue("filter", CssParser.ParseListOfComponentValues(value), value.Trim()) is null)
                return;

            _state.Filter = value;
        }
    }

    /// <summary>Returns the filters of the drawing state, or null for <c>none</c>.</summary>
    private DisplayFilter[]? GetFilters()
    {
        if (_state.Filter == "none")
            return null;

        var components = ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(_state.Filter));
        return FilterEffects.Get(components, GetFont().Style, space: null);
    }
}
