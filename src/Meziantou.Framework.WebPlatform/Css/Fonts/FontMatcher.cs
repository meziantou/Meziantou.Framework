using System.Globalization;
using System.Runtime.InteropServices;
using SystemFontDescriptor = Meziantou.Framework.Fonts.SystemFontDescriptor;

namespace Meziantou.Framework.WebPlatform.Css.Fonts;

/// <summary>A range of values of a font descriptor (e.g. <c>font-weight: 100 900</c>), with <see cref="Min"/> ≤ <see cref="Max"/>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontValueRange(double Min, double Max)
{
    public static FontValueRange Single(double value) => new(value, value);

    /// <summary>Creates a range from two values in any order (https://drafts.csswg.org/css-fonts-4/#font-prop-desc: the reversed ranges are swapped).</summary>
    public static FontValueRange Create(double first, double second) => first <= second ? new(first, second) : new(second, first);

    public bool Contains(double value) => value >= Min && value <= Max;

    public double Clamp(double value) => Math.Clamp(value, Min, Max);

    public FontValueRange Negate() => new(-Max, -Min);
}

/// <summary>
/// The styles a font face supports (https://drafts.csswg.org/css-fonts-4/#font-style-matching): ranges of weights and of
/// widths, and ranges of italic values (<c>ital</c>, 1 for an italic face) and of oblique angles (in degrees, positive
/// clockwise; an upright face has the oblique angle 0). A face without italic values or without oblique angles has a null
/// range.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSelectionCapabilities(FontValueRange Weight, FontValueRange Width, FontValueRange? Italic, FontValueRange? Oblique)
{
    /// <summary>The capabilities of an upright face of weight 400 and width 100%.</summary>
    public static FontSelectionCapabilities Normal { get; } = new(FontValueRange.Single(400), FontValueRange.Single(100), FontValueRange.Single(0), FontValueRange.Single(0));

    /// <summary>
    /// The capabilities of a font: its weight class, width class and style (<c>OS/2</c>), or the ranges of its <c>wght</c>,
    /// <c>wdth</c>, <c>ital</c> and <c>slnt</c> variation axes (https://drafts.csswg.org/css-fonts-4/#font-face-weight-auto).
    /// </summary>
    public static FontSelectionCapabilities FromFont(OpenTypeFont font)
    {
        var variations = font.Variations;
        return FromStyle(
            variations.GetAxis(OpenTypeTag.WeightAxis) is { } wght ? new FontValueRange(wght.MinValue, wght.MaxValue) : null,
            variations.GetAxis(OpenTypeTag.WidthAxis) is { } wdth ? new FontValueRange(wdth.MinValue, wdth.MaxValue) : null,
            variations.GetAxis(OpenTypeTag.ItalicAxis) is { } ital ? new FontValueRange(ital.MinValue, ital.MaxValue) : null,
            variations.GetAxis(OpenTypeTag.SlantAxis) is { } slnt ? new FontValueRange(slnt.MinValue, slnt.MaxValue) : null,
            font.WeightClass,
            font.WidthPercentage,
            font.IsItalic,
            font.IsOblique,
            font.ItalicAngle);
    }

    /// <summary>
    /// The capabilities of a font installed on the machine, from what the system font index read of its tables without
    /// loading it: the same values as <see cref="FromFont"/> gives once the font is loaded.
    /// </summary>
    public static FontSelectionCapabilities FromSystemFont(SystemFontDescriptor font)
    {
        return FromStyle(
            GetAxis(font, OpenTypeTag.WeightAxis),
            GetAxis(font, OpenTypeTag.WidthAxis),
            GetAxis(font, OpenTypeTag.ItalicAxis),
            GetAxis(font, OpenTypeTag.SlantAxis),
            font.HasOs2 ? font.Weight : 400,
            OpenTypeFont.GetWidthPercentage(font.WidthClass),
            OpenTypeFont.IsItalicStyle(font.Selection, font.MacStyle),
            OpenTypeFont.IsObliqueStyle(font.Selection),
            font.ItalicAngle);

        static FontValueRange? GetAxis(SystemFontDescriptor font, uint tag)
        {
            foreach (var axis in font.Axes)
            {
                if (axis.Tag == tag)
                    return new FontValueRange(axis.Minimum / 65536.0, axis.Maximum / 65536.0);
            }

            return null;
        }
    }

    /// <summary>
    /// The capabilities of the style of a font: the ranges of its <c>wght</c>, <c>wdth</c>, <c>ital</c> and <c>slnt</c>
    /// variation axes when it has them, else its weight class, width and style.
    /// </summary>
    private static FontSelectionCapabilities FromStyle(FontValueRange? weightAxis, FontValueRange? widthAxis, FontValueRange? italicAxis, FontValueRange? slantAxis, int weightClass, double widthPercentage, bool isItalic, bool isOblique, double italicAngle)
    {
        var weight = weightAxis ?? FontValueRange.Single(weightClass is >= 1 and <= 1000 ? weightClass : 400);
        var width = widthAxis ?? FontValueRange.Single(widthPercentage);

        // The slnt axis is counter-clockwise: an oblique angle is the opposite of the value of the axis
        var italic = italicAxis;
        var oblique = slantAxis?.Negate();
        if (italic is null && oblique is null)
        {
            if (isOblique)
            {
                oblique = FontValueRange.Single(italicAngle != 0 ? -italicAngle : FontSlope.DefaultObliqueAngle);
            }
            else if (isItalic)
            {
                italic = FontValueRange.Single(1);
            }
        }

        if ((italic is null && oblique is null) || (oblique is null && italic is { } range && range.Contains(0)))
        {
            oblique = FontValueRange.Single(0);
        }

        if (italic is null && oblique is { } angles && angles.Contains(0))
        {
            italic = FontValueRange.Single(0);
        }

        return new FontSelectionCapabilities(weight, width, italic, oblique);
    }

    /// <summary>
    /// The capabilities of the descriptors of a font face (https://drafts.csswg.org/css-fonts-4/#font-prop-desc): the values
    /// of <c>font-weight</c>, <c>font-width</c> and <c>font-style</c> (an empty string or <c>auto</c> uses the value of the
    /// font when it is loaded, else the initial value).
    /// </summary>
    public static FontSelectionCapabilities FromDescriptors(string weight, string width, string style, OpenTypeFont? font)
    {
        var fromFont = font is null ? Normal : FromFont(font);
        var weightRange = ParseRange(weight, ParseWeight) ?? fromFont.Weight;
        var widthRange = ParseRange(width, ParseWidth) ?? fromFont.Width;

        FontValueRange? italic = fromFont.Italic;
        FontValueRange? oblique = fromFont.Oblique;
        var value = style.Trim();
        switch (value)
        {
            case "" or "auto":
                break;
            case "normal":
                italic = FontValueRange.Single(0);
                oblique = FontValueRange.Single(0);
                break;
            case "italic":
                italic = FontValueRange.Single(1);
                oblique = null;
                break;
            case "oblique" or "left":
                italic = null;
                oblique = FontValueRange.Single(FontSlope.DefaultObliqueAngle);
                break;
            case "right":
                italic = null;
                oblique = FontValueRange.Single(-FontSlope.DefaultObliqueAngle);
                break;
            default:
                if (value.StartsWith("oblique ", StringComparison.Ordinal))
                {
                    var angles = value[8..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (angles.Length is 1 or 2 && FontSlope.TryParseAngle(angles[0], out var first))
                    {
                        var second = first;
                        if (angles.Length == 1 || FontSlope.TryParseAngle(angles[1], out second))
                        {
                            oblique = FontValueRange.Create(Math.Clamp(first, -90, 90), Math.Clamp(second, -90, 90));
                            italic = oblique.Value.Contains(0) ? FontValueRange.Single(0) : null;
                        }
                    }
                }

                break;
        }

        return new FontSelectionCapabilities(weightRange, widthRange, italic, oblique);

        static FontValueRange? ParseRange(string value, Func<string, double?> parse)
        {
            var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts is [var single] && parse(single) is { } number)
                return FontValueRange.Single(number);

            if (parts is [var first, var second] && parse(first) is { } min && parse(second) is { } max)
                return FontValueRange.Create(min, max);

            return null;
        }
    }

    /// <summary>Parses an absolute weight (<c>normal</c>, <c>bold</c> or a number).</summary>
    public static double? ParseWeight(string value) => value switch
    {
        "normal" => 400,
        "bold" => 700,
        _ => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? Math.Clamp(number, 1, 1000) : null,
    };

    /// <summary>Parses a width (a keyword of <c>font-width</c> or a percentage) to a percentage.</summary>
    public static double? ParseWidth(string value) => value switch
    {
        "ultra-condensed" => 50,
        "extra-condensed" => 62.5,
        "condensed" => 75,
        "semi-condensed" => 87.5,
        "normal" => 100,
        "semi-expanded" => 112.5,
        "expanded" => 125,
        "extra-expanded" => 150,
        "ultra-expanded" => 200,
        _ => FontDescription.ParsePercentage(value),
    };
}

/// <summary>The style selected by the font matching: an italic value or an oblique angle (https://drafts.csswg.org/css-fonts-4/#font-style-matching).</summary>
/// <param name="IsItalic">Whether the value is an italic value (else it is an oblique angle).</param>
/// <param name="Value">The italic value (1 for italic) or the oblique angle in degrees.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSelectedSlope(bool IsItalic, double Value)
{
    /// <summary>Whether the selected style is upright (the italic value 0 or the oblique angle 0).</summary>
    public bool IsUpright => Value == 0;
}

/// <summary>The values of the font properties selected by the font matching for a set of faces.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSelectedValues(double Width, FontSelectedSlope Slope, double Weight);

/// <summary>
/// The selection of the faces of a family that match the font properties (https://drafts.csswg.org/css-fonts-4/#font-style-matching):
/// <c>font-width</c> is matched first, then <c>font-style</c>, then <c>font-weight</c>. The faces that support a range of
/// values (the variable fonts and the @font-face rules with ranges) are matched as if each value was a different face.
/// </summary>
internal static class FontMatcher
{
    /// <summary>
    /// Selects the faces that match the desired values, and returns the selected values (the values to apply to the
    /// variable fonts). <paramref name="selected"/> receives the indices of the faces that support them, in order.
    /// <paramref name="styleSynthesis"/> is the value of <c>font-synthesis-style</c>: <c>oblique-only</c> prevents the use of
    /// the oblique faces as a fallback for italic.
    /// </summary>
    public static FontSelectedValues Select(IReadOnlyList<FontSelectionCapabilities> faces, double weight, FontSlope style, double width, List<int> selected, FontStyleSynthesis styleSynthesis = FontStyleSynthesis.Auto)
    {
        selected.Clear();
        for (var i = 0; i < faces.Count; i++)
        {
            selected.Add(i);
        }

        if (faces.Count == 0)
            return new FontSelectedValues(width, new FontSelectedSlope(IsItalic: false, 0), weight);

        // font-width: the faces whose width includes the closest width
        var selectedWidth = SelectClosest(faces, selected, static face => face.Width, value => WidthDistance(width, value));
        selected.RemoveAll(index => !faces[index].Width.Contains(selectedWidth));

        // font-style
        var slope = SelectSlope(faces, selected, style, styleSynthesis == FontStyleSynthesis.ObliqueOnly);
        selected.RemoveAll(index => (slope.IsItalic ? faces[index].Italic : faces[index].Oblique) is not { } range || !range.Contains(slope.Value));

        // font-weight
        var selectedWeight = SelectClosest(faces, selected, static face => face.Weight, value => WeightDistance(weight, value));
        selected.RemoveAll(index => !faces[index].Weight.Contains(selectedWeight));
        return new FontSelectedValues(selectedWidth, slope, selectedWeight);
    }

    /// <summary>
    /// Returns the value of the faces closest to the desired value: the distance function returns the distance of the
    /// closest value of a range, and the value.
    /// </summary>
    private static double SelectClosest(IReadOnlyList<FontSelectionCapabilities> faces, List<int> indices, Func<FontSelectionCapabilities, FontValueRange> getRange, Func<FontValueRange, (double Distance, double Value)> distance)
    {
        var best = double.PositiveInfinity;
        var bestValue = 0d;
        foreach (var index in indices)
        {
            var (d, value) = distance(getRange(faces[index]));
            if (d < best)
            {
                best = d;
                bestValue = value;
            }
        }

        return bestValue;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-style-matching (font-width): when the desired width is at most 100%, the
    /// narrower widths are checked in descending order, then the wider widths in ascending order; otherwise the wider widths
    /// in ascending order, then the narrower widths in descending order.
    /// </summary>
    private static (double Distance, double Value) WidthDistance(double desired, FontValueRange range)
    {
        if (range.Contains(desired))
            return (0, desired);

        // The large offset ranks the values in the second direction after all the values in the first one
        const double SecondDirection = 100_000;
        if (desired <= 100)
            return range.Max < desired ? (desired - range.Max, range.Max) : (SecondDirection + range.Min - desired, range.Min);

        return range.Min > desired ? (range.Min - desired, range.Min) : (SecondDirection + desired - range.Max, range.Max);
    }

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-style-matching (font-weight): from 400 to 500, the heavier weights up to 500
    /// are checked in ascending order, then the lighter weights in descending order, then the weights above 500; below
    /// 400, the lighter weights in descending order, then the heavier weights in ascending order; above 500, the heavier
    /// weights in ascending order, then the lighter weights in descending order.
    /// </summary>
    private static (double Distance, double Value) WeightDistance(double desired, FontValueRange range)
    {
        if (range.Contains(desired))
            return (0, desired);

        const double SecondDirection = 10_000;
        const double ThirdDirection = 20_000;
        if (desired is >= 400 and <= 500)
        {
            if (range.Min > desired)
                return range.Min <= 500 ? (range.Min - desired, range.Min) : (ThirdDirection + range.Min - desired, range.Min);

            return (SecondDirection + desired - range.Max, range.Max);
        }

        if (desired < 400)
            return range.Max < desired ? (desired - range.Max, range.Max) : (SecondDirection + range.Min - desired, range.Min);

        return range.Min > desired ? (range.Min - desired, range.Min) : (SecondDirection + desired - range.Max, range.Max);
    }

    /// <summary>A step of the search of a style: the italic values or the oblique angles of an interval, in a direction.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct SlopeStep(bool Italic, double Low, bool LowInclusive, double High, bool HighInclusive, bool Ascending);

    /// <summary>
    /// https://drafts.csswg.org/css-fonts-4/#font-style-matching (font-style), with the resolutions of the CSS working group
    /// that the tests of the specification expect: an oblique style does not fall back to the italic faces
    /// (https://github.com/w3c/csswg-drafts/issues/9389: they are only used when the family has no other face), and
    /// <c>font-synthesis-style: oblique-only</c> prevents the use of the oblique faces as a fallback for italic
    /// (https://github.com/w3c/csswg-drafts/issues/9390: they are only used when the family has no upright face). The oblique
    /// angles that replace italic are searched from the default oblique angle (the angles above it in ascending order, then the
    /// ones below it), as in Chrome and Safari, which match italic as that angle; the specification starts at 11deg.
    /// </summary>
    private static FontSelectedSlope SelectSlope(IReadOnlyList<FontSelectionCapabilities> faces, List<int> indices, FontSlope style, bool obliqueOnly)
    {
        const double Infinity = double.PositiveInfinity;
        const double Threshold = 11;
        const double Default = FontSlope.DefaultObliqueAngle;

        // A negative oblique angle is matched as the positive angle, with the oblique angles of the faces negated
        var negate = style.Kind == FontSlopeKind.Oblique && style.Angle < 0;
        var angle = Math.Abs(style.Angle);
        SlopeStep[] steps = style.Kind switch
        {
            FontSlopeKind.Italic when obliqueOnly =>
            [
                new(Italic: true, 1, true, 1, true, Ascending: true),
                new(Italic: true, 1, false, Infinity, false, Ascending: true),
                new(Italic: true, 0, false, 1, false, Ascending: false),
                new(Italic: true, -Infinity, false, 0, true, Ascending: false),
                new(Italic: false, 0, true, 0, true, Ascending: true),
                new(Italic: false, Default, true, Default, true, Ascending: true),
                new(Italic: false, Default, false, Infinity, false, Ascending: true),
                new(Italic: false, 0, false, Default, false, Ascending: false),
                new(Italic: false, -Infinity, false, 0, false, Ascending: false),
            ],
            FontSlopeKind.Italic =>
            [
                new(Italic: true, 1, true, 1, true, Ascending: true),
                new(Italic: true, 1, false, Infinity, false, Ascending: true),
                new(Italic: true, 0, false, 1, false, Ascending: false),
                new(Italic: false, Default, true, Default, true, Ascending: true),
                new(Italic: false, Default, false, Infinity, false, Ascending: true),
                new(Italic: false, 0, false, Default, false, Ascending: false),
                new(Italic: true, -Infinity, false, 0, true, Ascending: false),
                new(Italic: false, -Infinity, false, 0, true, Ascending: false),
            ],
            FontSlopeKind.Oblique when angle >= Threshold =>
            [
                new(Italic: false, angle, true, angle, true, Ascending: true),
                new(Italic: false, angle, false, Infinity, false, Ascending: true),
                new(Italic: false, 0, false, angle, false, Ascending: false),
                new(Italic: false, -Infinity, false, 0, true, Ascending: false),
                new(Italic: true, 1, true, Infinity, false, Ascending: true),
                new(Italic: true, 0, false, 1, false, Ascending: false),
                new(Italic: true, -Infinity, false, 0, true, Ascending: false),
            ],
            FontSlopeKind.Oblique =>
            [
                new(Italic: false, angle, true, angle, true, Ascending: true),
                new(Italic: false, 0, false, angle, false, Ascending: false),
                new(Italic: false, angle, false, Infinity, false, Ascending: true),
                new(Italic: false, -Infinity, false, 0, true, Ascending: false),
                new(Italic: true, 0, false, 1, false, Ascending: false),
                new(Italic: true, 1, true, Infinity, false, Ascending: true),
                new(Italic: true, -Infinity, false, 0, true, Ascending: false),
            ],
            _ =>
            [
                new(Italic: false, 0, true, Infinity, false, Ascending: true),
                new(Italic: true, 0, true, Infinity, false, Ascending: true),
                new(Italic: false, -Infinity, false, 0, false, Ascending: false),
                new(Italic: true, -Infinity, false, 0, false, Ascending: false),
            ],
        };

        foreach (var step in steps)
        {
            double? best = null;
            foreach (var index in indices)
            {
                var face = faces[index];
                if ((step.Italic ? face.Italic : face.Oblique) is not { } range)
                    continue;

                if (negate && !step.Italic)
                {
                    range = range.Negate();
                }

                // The intersection of the range with the interval of the step, and its value closest to the start of the step
                var low = Math.Max(range.Min, step.Low);
                var high = Math.Min(range.Max, step.High);
                if (low > high || (!step.LowInclusive && high <= step.Low) || (!step.HighInclusive && low >= step.High))
                    continue;

                var value = step.Ascending ? low : high;
                if (best is null || (step.Ascending ? value < best : value > best))
                {
                    best = value;
                }
            }

            if (best is { } selected)
                return new FontSelectedSlope(step.Italic, negate && !step.Italic ? -selected : selected);
        }

        // There is no style (the faces have neither italic values nor oblique angles)
        return new FontSelectedSlope(IsItalic: false, 0);
    }
}

/// <summary>The value of the <c>unicode-range</c> descriptor of a font face (https://drafts.csswg.org/css-fonts-4/#unicode-range-desc).</summary>
internal sealed class UnicodeRangeSet
{
    private readonly (int Start, int End)[] _ranges;

    private UnicodeRangeSet((int Start, int End)[] ranges) => _ranges = ranges;

    /// <summary>The range of all the code points (the initial value).</summary>
    public static UnicodeRangeSet All { get; } = new([(0, 0x10FFFF)]);

    public bool IsAll => _ranges is [(0, >= 0x10FFFF)];

    /// <summary>
    /// Parses a comma-separated list of <c>&lt;unicode-range-token&gt;</c> (<c>U+26</c>, <c>U+0-7F</c>, <c>U+4??</c>); the
    /// invalid ranges are ignored.
    /// </summary>
    public static UnicodeRangeSet Parse(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value is "U+0-10FFFF" or "U+0-10ffff")
            return All;

        var ranges = new List<(int Start, int End)>();
        foreach (var part in value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (part.Length < 3 || part[0] is not ('U' or 'u') || part[1] != '+')
                continue;

            var text = part[2..];
            var dash = text.IndexOf('-', StringComparison.Ordinal);
            int start, end;
            if (dash >= 0)
            {
                if (!TryParseHex(text[..dash], out start) || !TryParseHex(text[(dash + 1)..], out end))
                    continue;
            }
            else if (text.Contains('?', StringComparison.Ordinal))
            {
                if (!TryParseHex(text.Replace('?', '0'), out start) || !TryParseHex(text.Replace('?', 'F'), out end))
                    continue;
            }
            else
            {
                if (!TryParseHex(text, out start))
                    continue;

                end = start;
            }

            if (start <= end)
            {
                ranges.Add((start, Math.Min(end, 0x10FFFF)));
            }
        }

        return new UnicodeRangeSet([.. ranges]);

        static bool TryParseHex(string value, out int result) => int.TryParse(value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out result) && value.Length <= 6;
    }

    /// <summary>Returns whether a code point is in one of the ranges.</summary>
    public bool Contains(int codePoint)
    {
        foreach (var (start, end) in _ranges)
        {
            if (codePoint >= start && codePoint <= end)
                return true;
        }

        return false;
    }

    /// <summary>Returns whether a code point of the text is in one of the ranges.</summary>
    public bool ContainsAny(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (Contains(rune.Value))
                return true;
        }

        return false;
    }
}
