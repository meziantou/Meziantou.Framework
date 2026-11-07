using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The four physical sides of a box (margins, paddings, borders, insets...).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Sides<T>(T Top, T Right, T Bottom, T Left);

/// <summary>The kind of a <see cref="LineHeight"/>.</summary>
internal enum LineHeightKind : byte
{
    Normal,

    /// <summary>A &lt;number&gt;, multiplied by the font size.</summary>
    Number,

    /// <summary>A length in px (the computed percentages are lengths).</summary>
    Length,
}

/// <summary>The computed value of line-height (https://drafts.csswg.org/css-inline-3/#line-height-property).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LineHeight(LineHeightKind Kind, double Value)
{
    public static LineHeight Normal => new(LineHeightKind.Normal, 0);

    /// <summary>
    /// The used line height in px for a used font size. normal is <paramref name="normalLineHeight"/>, the line height
    /// given by the metrics of the first available font.
    /// </summary>
    public double Resolve(double fontSize, double normalLineHeight) => Kind switch
    {
        LineHeightKind.Number => Value * fontSize,
        LineHeightKind.Length => Value,
        _ => normalLineHeight,
    };

    /// <summary>Parses a computed line-height: <c>normal</c>, a number, or a length in px (multiplied by the zoom).</summary>
    public static LineHeight Parse(string value, double zoom)
    {
        if (value == "normal")
            return Normal;

        if (LengthPercentage.TryParseNumber(value, out var number))
            return new LineHeight(LineHeightKind.Number, number);

        return LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) && length.IsFixed ? new LineHeight(LineHeightKind.Length, length.Length) : Normal;
    }
}

/// <summary>
/// The baseline shift of vertical-align (https://drafts.csswg.org/css-inline-3/#baseline-shift-property): a keyword, or a
/// &lt;length-percentage&gt; whose percentages refer to the line-height.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BaselineShift(BaselineShiftKind Kind, LengthPercentage Length = default)
{
    public static BaselineShift Parse(string value, double zoom) => value switch
    {
        "sub" => new(BaselineShiftKind.Sub),
        "super" => new(BaselineShiftKind.Super),
        "top" => new(BaselineShiftKind.Top),
        "center" => new(BaselineShiftKind.Center),
        "bottom" => new(BaselineShiftKind.Bottom),
        _ => new(BaselineShiftKind.Length, LengthPercentage.TryParse(value, zoom, nonNegative: false, out var length) ? length : LengthPercentage.Zero),
    };
}

/// <summary>A gap (https://drafts.csswg.org/css-align-3/#column-row-gap): normal or a &lt;length-percentage&gt;.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct Gap(bool IsNormal, LengthPercentage Length)
{
    public static Gap Normal => new(IsNormal: true, default);

    /// <summary>The used gap: normal is <paramref name="normalValue"/> (0, or 1em in multi-column containers).</summary>
    public double Resolve(double percentageBasis, double normalValue = 0) => IsNormal ? normalValue : Length.Resolve(percentageBasis);

    public static Gap Parse(string value, double zoom) => value != "normal" && LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) ? new Gap(IsNormal: false, length) : Normal;
}

/// <summary>tab-size (https://drafts.csswg.org/css-text-4/#tab-size-property): a number of spaces or a length in px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TabSize(double Value, bool IsLength)
{
    public static TabSize Parse(string value, double zoom)
    {
        if (LengthPercentage.TryParseNumber(value, out var number))
            return new TabSize(Math.Max(0, number), IsLength: false);

        return LengthPercentage.TryParse(value, zoom, nonNegative: true, out var length) && length.IsFixed ? new TabSize(length.Length, IsLength: true) : new TabSize(8, IsLength: false);
    }
}

/// <summary>
/// hyphenate-limit-chars (https://drafts.csswg.org/css-text-4/#hyphenate-limit-chars-property): the minimum number of characters
/// of a hyphenated word, before and after the hyphenation point; 0 is <c>auto</c>.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct HyphenateLimitChars(int Word, int Before, int After)
{
    public static HyphenateLimitChars Parse(string value)
    {
        if (value == "auto")
            return default;

        // "If the third value is missing, it is the same as the second. If the second value is missing, then it is auto"
        Span<int> values = [0, 0, -1];
        var count = 0;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (count == 3)
                break;

            values[count++] = LengthPercentage.TryParseNumber(part, out var number) ? (int)Math.Max(0, number) : 0;
        }

        return new HyphenateLimitChars(values[0], values[1], values[2] < 0 ? values[1] : values[2]);
    }
}

/// <summary>
/// A &lt;text-edge&gt; (https://drafts.csswg.org/css-inline-3/#typedef-text-edge): the metrics of the over and under edges of
/// the text ("If only one value is specified, both edges are assigned that same keyword if possible; else text is assumed").
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextEdge(TextEdgeMetric Over, TextEdgeMetric Under)
{
    public static TextEdge Text => new(TextEdgeMetric.Text, TextEdgeMetric.Text);

    /// <summary>Parses text-box-edge; <c>auto</c> uses <paramref name="lineFitEdge"/> (whose <c>leading</c> is <c>text</c>).</summary>
    public static TextEdge Parse(string value, string lineFitEdge)
    {
        if (value == "auto")
        {
            if (lineFitEdge is "leading" or "")
                return Text;

            value = lineFitEdge;
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var over = parts.Length > 0 ? LayoutKeywords.ParseTextEdgeMetric(parts[0]) ?? TextEdgeMetric.Text : TextEdgeMetric.Text;
        if (parts.Length > 1)
            return new TextEdge(over, LayoutKeywords.ParseTextEdgeMetric(parts[1]) ?? TextEdgeMetric.Text);

        // cap and ex are only over edges, alphabetic only an under edge
        return over switch
        {
            TextEdgeMetric.Cap or TextEdgeMetric.Ex => new TextEdge(over, TextEdgeMetric.Text),
            TextEdgeMetric.Alphabetic => new TextEdge(TextEdgeMetric.Text, TextEdgeMetric.Alphabetic),
            _ => new TextEdge(over, over),
        };
    }
}

/// <summary>contain-intrinsic-width/height (https://drafts.csswg.org/css-sizing-4/#intrinsic-size-override): <c>auto? [ none | &lt;length&gt; ]</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ContainIntrinsicSize(bool IsAuto, double? Length)
{
    public static ContainIntrinsicSize None => new(IsAuto: false, Length: null);

    public static ContainIntrinsicSize Parse(string value, double zoom)
    {
        if (value == "none")
            return None;

        var isAuto = false;
        double? length = null;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "auto")
            {
                isAuto = true;
            }
            else if (LengthPercentage.TryParse(part, zoom, nonNegative: true, out var parsed) && parsed.IsFixed)
            {
                length = parsed.Length;
            }
        }

        return new ContainIntrinsicSize(isAuto, length);
    }
}

/// <summary>A side of overflow-clip-margin (https://drafts.csswg.org/css-overflow-4/#overflow-clip-margin): a visual box and a length.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct OverflowClipMargin(VisualBox? Box, double Length)
{
    public static OverflowClipMargin Parse(string value, double zoom)
    {
        VisualBox? box = null;
        double length = 0;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (LayoutKeywords.ParseVisualBox(part) is { } visualBox)
            {
                box = visualBox;
            }
            else if (LengthPercentage.TryParse(part, zoom, nonNegative: true, out var parsed) && parsed.IsFixed)
            {
                length = parsed.Length;
            }
        }

        return new OverflowClipMargin(box, length);
    }
}

/// <summary>A side of text-overflow (https://drafts.csswg.org/css-overflow-4/#text-overflow): its kind, and the string of a &lt;string&gt; value (without quotes).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextOverflowSide(TextOverflowKind Kind, string? String = null)
{
    public static TextOverflowSide Clip => default;

    /// <summary>Whether the side elides the overflowing content: <c>ellipsis</c>, or a &lt;string&gt; (<c>clip</c> and <c>fade</c> only clip it).</summary>
    public bool Elides => Kind is TextOverflowKind.Ellipsis or TextOverflowKind.String;
}

/// <summary>
/// text-overflow (https://drafts.csswg.org/css-overflow-4/#text-overflow): "If there is one value, it applies only to the end
/// line box edge. If there are two values, the first value applies to the line-left edge, and the second value applies to the
/// line-right edge." <see cref="Second"/> is null for one value.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextOverflow(TextOverflowSide First, TextOverflowSide? Second = null)
{
    public static TextOverflow Clip => default;

    /// <summary>Whether a side elides the overflowing content (see <see cref="TextOverflowSide.Elides"/>).</summary>
    public bool Elides => First.Elides || Second is { Elides: true };

    /// <summary>Returns the values of the line-left and line-right edges of a line whose paragraph has the given direction.</summary>
    public (TextOverflowSide LineLeft, TextOverflowSide LineRight) GetLineSides(bool isRightToLeft)
    {
        if (Second is { } lineRight)
            return (First, lineRight);

        return isRightToLeft ? (First, TextOverflowSide.Clip) : (TextOverflowSide.Clip, First);
    }

    public static TextOverflow Parse(string value)
    {
        if (value == "clip")
            return Clip;

        var parts = CssValueComputer.SplitComponents(value);
        if (parts.Length == 0)
            return Clip;

        var first = ParseSide(parts[0]);
        return parts.Length > 1 ? new TextOverflow(first, ParseSide(parts[1])) : new TextOverflow(first);
    }

    private static TextOverflowSide ParseSide(string value) => new(LayoutKeywords.ParseTextOverflowSide(value), UnquoteString(value));

    private static string? UnquoteString(string value) => value.Length >= 2 && value[0] is '"' or '\''
        && Syntax.CssParser.ParseListOfComponentValues(value) is [Syntax.CssPreservedToken { Type: Syntax.CssTokenType.String } token] ? token.Token.Value : null;
}

/// <summary>vertical-align (https://drafts.csswg.org/css-inline-3/#transverse-alignment): its three longhands.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct VerticalAlign(AlignmentBaseline AlignmentBaseline, BaselineShift BaselineShift, BaselineSource BaselineSource);

/// <summary>font-style (https://drafts.csswg.org/css-fonts-4/#font-style-prop): normal, italic, or oblique with an angle in degrees.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontStyle(FontStyleKind Kind, double ObliqueAngle)
{
    /// <summary>The default angle of oblique (https://drafts.csswg.org/css-fonts-4/#valdef-font-style-oblique-angle).</summary>
    public const double DefaultObliqueAngle = 14;

    public static FontStyle Normal => new(FontStyleKind.Normal, 0);

    public static FontStyle Parse(string value)
    {
        switch (value)
        {
            case "normal":
                return Normal;
            case "italic":
                return new FontStyle(FontStyleKind.Italic, DefaultObliqueAngle);
            case "oblique":
                return new FontStyle(FontStyleKind.Oblique, DefaultObliqueAngle);
        }

        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts is ["oblique", var angle] && angle.EndsWith("deg", StringComparison.Ordinal) && LengthPercentage.TryParseNumber(angle.AsSpan(0, angle.Length - 3), out var degrees))
            return new FontStyle(FontStyleKind.Oblique, degrees);

        return Normal;
    }
}

/// <summary>A family of font-family (https://drafts.csswg.org/css-fonts-4/#font-family-prop): a family name or a generic family.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontFamily(string Name, bool IsGeneric);

/// <summary>font-size-adjust (https://drafts.csswg.org/css-fonts-5/#font-size-adjust-prop): none, or a font metric and a number (or from-font).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct FontSizeAdjust(string? Metric, double? Value, bool FromFont)
{
    public static FontSizeAdjust None => new(Metric: null, Value: null, FromFont: false);

    public bool IsNone => Metric is null && Value is null && !FromFont;

    public static FontSizeAdjust Parse(string value)
    {
        if (value == "none")
            return None;

        var metric = "ex-height";
        double? number = null;
        var fromFont = false;
        foreach (var part in value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            if (part == "from-font")
            {
                fromFont = true;
            }
            else if (double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            {
                number = parsed;
            }
            else
            {
                metric = part;
            }
        }

        return new FontSizeAdjust(metric, number, fromFont);
    }
}

/// <summary>text-combine-upright (https://drafts.csswg.org/css-writing-modes-4/#text-combine-upright): none, all, or digits with the maximum number of digits.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TextCombineUpright(bool IsAll, int Digits)
{
    public static TextCombineUpright None => new(IsAll: false, 0);

    public bool IsNone => !IsAll && Digits == 0;

    public static TextCombineUpright Parse(string value)
    {
        if (value == "none")
            return None;

        if (value == "all")
            return new TextCombineUpright(IsAll: true, 0);

        // digits <integer [2,4]>? (2 by default)
        var parts = value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts is ["digits", var count] && int.TryParse(count, NumberStyles.Integer, CultureInfo.InvariantCulture, out var digits) ? new TextCombineUpright(IsAll: false, digits) : new TextCombineUpright(IsAll: false, 2);
    }
}

/// <summary>
/// image-orientation (https://drafts.csswg.org/css-images-3/#the-image-orientation): <c>from-image</c> (the EXIF orientation of
/// the image is applied) or <c>none</c>. The deprecated angle and <c>flip</c> values are not supported, as in the browsers.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ImageOrientation(bool FromImage)
{
    public static ImageOrientation Parse(string value) => new(FromImage: value != "none");
}
