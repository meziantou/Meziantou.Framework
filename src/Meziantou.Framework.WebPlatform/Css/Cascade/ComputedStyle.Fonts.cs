using Meziantou.Framework.WebPlatform.Css.Fonts;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>A font-relative length unit that depends on the font metrics (https://drafts.csswg.org/css-values-4/#font-relative-lengths).</summary>
internal enum FontRelativeUnit
{
    /// <summary>The x-height of the first available font (<c>ex</c>).</summary>
    Ex,

    /// <summary>The cap height of the first available font (<c>cap</c>).</summary>
    Cap,

    /// <summary>The advance measure of the glyph "0" (<c>ch</c>).</summary>
    Ch,

    /// <summary>The advance measure of the glyph "水" (<c>ic</c>).</summary>
    Ic,
}

internal sealed partial class ComputedStyle
{
    private FontFallbackList? _fonts;
    private TextShapingOptions? _shapingOptions;

    /// <summary>The fonts of the element (https://drafts.csswg.org/css-fonts-4/#font-matching-algorithm), from its computed font properties.</summary>
    public FontFallbackList Fonts => _fonts ??= Engine.Document.FontSelector.GetFonts(FontDescription.FromStyle(this));

    /// <summary>The options of the text shaping of the text of the element (<see cref="TextShapingOptions.FromStyle"/>): the font size, the direction, the orientation, the spacing and the kerning.</summary>
    public TextShapingOptions ShapingOptions => _shapingOptions ??= TextShapingOptions.FromStyle(this);

    /// <summary>
    /// Returns the advance in CSS px of a text rendered with the style of the element on a single line (with its fonts, its
    /// direction, its orientation, its spacing and its kerning), without the text transformations (<see cref="TextShaper.MeasureWidth"/>).
    /// </summary>
    public double MeasureText(ReadOnlySpan<char> text) => TextShaper.MeasureWidth(text, Fonts, ShapingOptions);

    /// <summary>Shapes a text with the style of the element (<see cref="TextShaper.Shape"/>).</summary>
    public ShapedText ShapeText(ReadOnlySpan<char> text) => TextShaper.Shape(text, Fonts, ShapingOptions);

    /// <summary>The metrics of the first available font (https://drafts.csswg.org/css-fonts-4/#first-available-font) at the computed font size.</summary>
    public UsedFontMetrics PrimaryFontMetrics => Fonts.PrimaryFont.GetMetrics(FontSize);

    /// <summary>The used line height of the initial values of the font properties (for the root element).</summary>
    private double InitialLineHeight => Engine.Document.FontSelector.InitialFonts.PrimaryFont.GetMetrics(CssValueComputer.MediumFontSize).NormalLineHeight;

    /// <summary>
    /// The used value of line-height (https://drafts.csswg.org/css-inline-3/#line-height-property): normal is the sum of the
    /// ascent, the descent and the line gap of the first available font.
    /// </summary>
    private double ComputeLineHeightPixels()
    {
        var value = GetComputedValue("line-height");
        return CssValueComputer.GetLineHeightPixels(value, FontSize) ?? PrimaryFontMetrics.NormalLineHeight;
    }

    /// <summary>
    /// Returns the length in px of a font-relative unit (https://drafts.csswg.org/css-values-4/#font-relative-lengths) for the
    /// fonts and the font size of the style: <c>ex</c> and <c>cap</c> come from the first available font; <c>ch</c> and
    /// <c>ic</c> are the advance measures (in the inline axis) of "0" and "水" in the fonts that render them.
    /// </summary>
    public double GetFontRelativeLength(FontRelativeUnit unit)
    {
        var vertical = (unit is FontRelativeUnit.Ch or FontRelativeUnit.Ic) && GetComputedValue("writing-mode") is not "horizontal-tb";

        // In vertical text, "0" is set sideways unless text-orientation is upright, and the ideographs are upright
        var upright = vertical && (unit == FontRelativeUnit.Ic || GetComputedValue("text-orientation") == "upright");
        return GetFontRelativeLength(Fonts, FontSize, unit, upright);
    }

    /// <summary>Returns the length in px of a font-relative unit for the initial values of the font properties (e.g. for the root's font-size).</summary>
    public static double GetInitialFontRelativeLength(StyleEngine engine, FontRelativeUnit unit)
        => GetFontRelativeLength(engine.Document.FontSelector.InitialFonts, CssValueComputer.MediumFontSize, unit, upright: false);

    private static double GetFontRelativeLength(FontFallbackList fonts, double fontSize, FontRelativeUnit unit, bool upright)
    {
        switch (unit)
        {
            case FontRelativeUnit.Ex:
                return fonts.PrimaryFont.GetMetrics(fontSize).XHeight;
            case FontRelativeUnit.Cap:
                return fonts.PrimaryFont.GetMetrics(fontSize).CapHeight;
            default:
                // When there is no glyph, "0" is 0.5em wide by 1em tall and "水" is 1em (https://drafts.csswg.org/css-values-4/#ch)
                var codePoint = unit == FontRelativeUnit.Ch ? '0' : MatchedFont.WaterIdeograph;
                if (fonts.FindFontForCharacter(codePoint) is not { } font)
                    return unit == FontRelativeUnit.Ch && !upright ? fontSize / 2 : fontSize;

                return font.GetAdvance(font.GetGlyph(codePoint), font.GetEmSize(fontSize), upright);
        }
    }
}
