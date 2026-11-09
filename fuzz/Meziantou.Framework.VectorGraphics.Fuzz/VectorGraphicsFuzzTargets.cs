using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

/// <summary>
/// A color parses back from its notation. Wrapping a text keeps its characters in order, and gives lines that fit the
/// width unless they hold a single grapheme cluster. The SVG of any scene is a well-formed XML document whose root is
/// <c>svg</c>, whatever the texts, tooltips, hyperlinks and data attributes hold, and the output is deterministic.
/// </summary>
public static partial class VectorGraphicsFuzzTargets
{
    private static readonly string[] ColorTokens = ["#", "0", "9", "a", "f", "A", "F", "g", " ", "\t", "\u00A0", "\uFF10", "steelblue", "red", "black", "white", "transparent", "rebeccapurple"];

    private static readonly string[] TextTokens =
    [
        " ", "\n", "\r\n", "\r", "\t", "-", "\u00A0", "\u200B", "\u2028", "\u0301", "\U0001F600", "\U0001F468\u200D\U0001F469", "\u4E2D", "\u05E9", "\uD800",
        "word", "Supercalifragilisticexpialidocious", "a", "W", "i", "/", "?", "(", ")", "\"", "'",
    ];

    private static readonly string[] SvgTokens = ["<", ">", "&", "\"", "'", "\0", "\u0001", "\uFFFE", "\uFFFF", "\uD800", "\uDC00", "\U0001F600", "]]>", "-->", "javascript:", "https://", "a-b", "x"];

    private static readonly double[] Widths = [0, 1, 5, 10, 20, 40, 60, 100, 200, 1000, double.PositiveInfinity, double.NaN, -1, 0.5, 3, 15];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("vectorgraphics-color", ColorTarget) { Seeds = GetColorSeeds, Dictionary = ColorTokens },
        new("vectorgraphics-wrap", WrapTarget) { Seeds = GetWrapSeeds, Dictionary = TextTokens },
        new("vectorgraphics-svg", SvgTarget) { Seeds = GetSvgSeeds, Dictionary = SvgTokens },
    ];

    /// <summary>
    /// <see cref="SceneColor.Parse"/> and <see cref="SceneColor.TryParse"/> agree; a hexadecimal notation is accepted
    /// exactly when it has 3, 4, 6 or 8 ASCII hexadecimal digits, and gives their channels; the notation of a color
    /// (<see cref="SceneColor.ToString"/>) parses back to it.
    /// </summary>
    private static void ColorTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var succeeded = SceneColor.TryParse(text, out var color);

        SceneColor? parsed;
        try
        {
            parsed = SceneColor.Parse(text);
        }
        catch (FormatException)
        {
            parsed = null;
        }

        FuzzAssert.Equal(succeeded ? color : (SceneColor?)null, parsed, $"Parse and TryParse disagree on {FuzzAssert.Format(text)}");

        var trimmed = text.Trim();
        if (trimmed.StartsWith('#', StringComparison.Ordinal))
        {
            var digits = trimmed[1..];
            var isValid = digits.Length is 3 or 4 or 6 or 8 && digits.All(char.IsAsciiHexDigit);
            FuzzAssert.Equal(isValid, succeeded, $"TryParse({FuzzAssert.Format(text)}) is wrong");
            if (isValid)
            {
                FuzzAssert.Equal(DecodeHex(digits), color, $"The channels of {FuzzAssert.Format(text)} are wrong");
            }
        }

        if (succeeded)
        {
            var notation = color.ToString();
            FuzzAssert.Equal(color, SceneColor.Parse(notation), $"The notation {FuzzAssert.Format(notation)} of {FuzzAssert.Format(text)} does not parse back to the color");
            FuzzAssert.Equal(color with { A = 255 }, SceneColor.Parse(color.ToHexRgb()), $"The #rrggbb notation of {FuzzAssert.Format(text)} does not parse back to the color");
        }
    }

    private static SceneColor DecodeHex(string digits)
    {
        var channels = new byte[] { 0, 0, 0, 255 };
        var width = digits.Length <= 4 ? 1 : 2;
        for (var i = 0; i < digits.Length / width; i++)
        {
            var value = byte.Parse(digits.AsSpan(i * width, width), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            channels[i] = width == 1 ? (byte)(value * 17) : value;
        }

        return new SceneColor(channels[0], channels[1], channels[2], channels[3]);
    }

    /// <summary>
    /// The input is a byte that selects the width, a byte of font options and the text. <see cref="TextFitting.Wrap"/>
    /// keeps the characters of the text in order, except the line terminators and the spaces at the end of the lines;
    /// every line fits the width, or is a single grapheme cluster; and with no width, the lines are the lines of the text.
    /// <see cref="TextFitting.Truncate"/> returns the text, a prefix of it followed by an ellipsis that fits, or nothing.
    /// </summary>
    private static void WrapTarget(ReadOnlySpan<byte> data)
    {
        var width = FuzzInput.Pick(Widths, FuzzInput.ReadByte(ref data));
        var flags = FuzzInput.ReadByte(ref data);
        var font = new SceneFont(1 + (flags & 0x1F), Bold: (flags & 0x20) != 0, Italic: (flags & 0x40) != 0);
        var text = FuzzText.Decode(data);
        var measurer = AfmTextMeasurer.Default;

        var lines = TextFitting.Wrap(measurer, text, font, width);
        FuzzAssert.True(lines.Count > 0, "Wrap returned no line");
        FuzzAssert.Equal(RemoveSpaces(text), RemoveSpaces(string.Concat(lines)), $"Wrap({FuzzAssert.Format(text)}, {width}) lost or moved characters");
        var limited = width > 0 && double.IsFinite(width);
        foreach (var line in lines)
        {
            FuzzAssert.False(line.Length > 0 && IsTrimmed(line[^1]), $"The line {FuzzAssert.Format(line)} ends with a space");
            if (limited && measurer.Measure(line, font).Width > width)
            {
                FuzzAssert.Equal(line.Length, Unicode.GetGraphemeClusterLength(line), $"The line {FuzzAssert.Format(line)} is wider than {width} and is not a single grapheme cluster");
            }
        }

        if (!limited)
        {
            var expected = LineTerminators().Split(text).Select(line => line.TrimEnd(TrimmedCharacters.ToCharArray())).ToList();
            if (expected.Count > 1 && expected[^1].Length is 0 && LineTerminators().IsMatch(text[^1].ToString()))
            {
                expected.RemoveAt(expected.Count - 1);
            }

            FuzzAssert.Equal(string.Join("|", expected), string.Join("|", lines), $"Wrap({FuzzAssert.Format(text)}) without a width does not give the lines of the text");
        }

        if (!text.Contains('\n', StringComparison.Ordinal))
        {
            var truncated = TextFitting.Truncate(measurer, text, font, width);
            if (truncated is not null && truncated != text)
            {
                FuzzAssert.True(truncated.EndsWith(TextFitting.Ellipsis, StringComparison.Ordinal), $"Truncate({FuzzAssert.Format(text)}) has no ellipsis");
                FuzzAssert.True(text.StartsWith(truncated[..^1], StringComparison.Ordinal), $"Truncate({FuzzAssert.Format(text)}) is not a prefix of the text");
                FuzzAssert.True(measurer.Measure(truncated, font).Width <= width, $"Truncate({FuzzAssert.Format(text)}) is wider than {width}");
            }
        }
    }

    private const string TrimmedCharacters = " \t\r\n\u000B\u000C\u0085\u2028\u2029";

    private static bool IsTrimmed(char c) => TrimmedCharacters.Contains(c, StringComparison.Ordinal);

    private static string RemoveSpaces(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (!IsTrimmed(c))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    // A regular C# string literal
    [GeneratedRegex(""""(?<!@)"(?<value>(?:[^"\\\n]|\\.)*)"""", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex StringLiteral();

    private static string Unescape(string literal)
    {
        var builder = new StringBuilder(literal.Length);
        for (var i = 0; i < literal.Length; i++)
        {
            if (literal[i] is not '\\' || i + 1 >= literal.Length)
            {
                builder.Append(literal[i]);
                continue;
            }

            i++;
            switch (literal[i])
            {
                case 'n':
                    builder.Append('\n');
                    break;
                case 'r':
                    builder.Append('\r');
                    break;
                case 't':
                    builder.Append('\t');
                    break;
                case 'u' when i + 4 < literal.Length:
                    builder.Append((char)int.Parse(literal.AsSpan(i + 1, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                    i += 4;
                    break;
                case 'U' when i + 8 < literal.Length:
                    builder.Append(char.ConvertFromUtf32(int.Parse(literal.AsSpan(i + 1, 8), NumberStyles.HexNumber, CultureInfo.InvariantCulture)));
                    i += 8;
                    break;
                default:
                    builder.Append(literal[i]);
                    break;
            }
        }

        return builder.ToString();
    }

    // The mandatory breaks of UAX #14: CR LF, CR, LF, NEL, VT, FF, LS, PS
    [GeneratedRegex("\r\n|[\r\n\u0085\u000B\u000C\u2028\u2029]", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex LineTerminators();

    /// <summary>
    /// The input is a list of elements: a byte selects the kind of element (rectangle, circle, line, path, text, group,
    /// end of group) and its options, and the text up to the next NUL byte gives its text, tooltip, CSS class, hyperlink or
    /// data attribute. The SVG is a well-formed XML document whose root is <c>svg</c>, and rendering twice gives the same text.
    /// </summary>
    private static void SvgTarget(ReadOnlySpan<byte> data)
    {
        var scene = new Scene(100, 100, FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0)));
        var groups = new Stack<SceneGroup>();
        groups.Push(scene.Root);
        var count = 0;
        while (!data.IsEmpty && count++ < 200)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var text = FuzzText.Decode(FuzzInput.ReadUntil(ref data, 0));
            var value = (kind >> 3) - 8.5;
            var parent = groups.Peek();
            SceneElement element;
            switch (kind & 7)
            {
                case 0:
                    element = parent.Add(new SceneRectangle(value, value, kind % 13, kind % 7) { Fill = new LinearGradientPaint(0, 0, 1, 1, [new(0, SceneColor.White), new(1, new SceneColor(1, 2, 3, kind))]), CornerRadius = kind % 3 });
                    break;
                case 1:
                    element = parent.Add(new SceneCircle(value, value, kind % 5) { Stroke = new SceneColor(kind, 0, 0, kind), StrokeDashArray = [1, value] });
                    break;
                case 2:
                    element = parent.Add(new SceneLine(0, value, value, 0) { Stroke = SceneColor.Black, LineCap = (SceneLineCap)(kind % 3), LineJoin = (SceneLineJoin)(kind % 3) });
                    break;
                case 3:
                    element = parent.Add(new ScenePath(new PathBuilder().MoveTo(0, 0).LineTo(value, 1).ArcTo(value, 2, 3, largeArc: (kind & 8) != 0, sweep: (kind & 16) != 0, 4, 5).Close().Build()) { FillRule = (SceneFillRule)(kind % 2) });
                    break;
                case 4:
                    element = parent.Add(new SceneText(value, value, text, new SceneFont(kind % 20, Bold: (kind & 8) != 0, Italic: (kind & 16) != 0), SceneColor.Black) { Anchor = (SceneTextAnchor)(kind % 3), Rotation = value });
                    break;
                case 5:
                    var group = parent.Add(new SceneGroup
                    {
                        Hyperlink = (kind & 8) != 0 ? text : null,
                        Clip = (kind & 16) != 0 ? new SceneBounds(value, value, 10, 10) : null,
                        Transform = (kind & 32) != 0 ? SceneTransform.Translate(value, value) : null,
                    });
                    groups.Push(group);
                    element = group;
                    break;
                case 6:
                    if (groups.Count > 1)
                    {
                        groups.Pop();
                    }

                    continue;
                default:
                    element = parent;
                    break;
            }

            element.Tooltip = (kind & 64) != 0 ? text : element.Tooltip;
            element.CssClass = (kind & 128) != 0 ? text : element.CssClass;
            element.Opacity = (kind % 11) / 10.0;
            if (SceneElement.IsValidDataAttributeName(text))
            {
                element.SetDataAttribute(text, text);
            }
        }

        scene.Title = count % 2 == 0 ? FuzzText.Decode(data) : null;
        var svg = SvgSceneRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "p" + count.ToString(CultureInfo.InvariantCulture) });
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "p" + count.ToString(CultureInfo.InvariantCulture) }), "Rendering the scene twice gives different outputs");

        var document = new XmlDocument { XmlResolver = null };
        try
        {
            using var reader = XmlReader.Create(new StringReader(svg), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            document.Load(reader);
        }
        catch (XmlException exception)
        {
            FuzzAssert.Fail($"The SVG is not well-formed: {exception.Message}");
        }

        FuzzAssert.Equal("svg", document.DocumentElement?.LocalName, "The root of the SVG is not svg");
    }

    private static IEnumerable<FuzzSeed> GetColorSeeds()
    {
        string[] colors = ["#fff", "#FFFA", "#336699", "#33669980", " #abc ", "#ggg", "#12345", "#", "", " ", "steelblue", "SteelBlue", "transparent", "notacolor", "#+12345", "#\uFF10\uFF10\uFF10", "\u00A0red\u00A0", "#0x1234"];
        for (var i = 0; i < colors.Length; i++)
        {
            yield return FuzzSeed.FromText("color-" + i.ToString(CultureInfo.InvariantCulture), colors[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetWrapSeeds()
    {
        string[] texts =
        [
            "The quick brown fox jumps over the lazy dog", "Supercalifragilisticexpialidocious is long\nend", "first\n\nthird", "a\r\nb", "😀😀",
            "e\u0301e\u0301", "", " ", "\n", "word   \n  indented", "中文字符的换行", "שלום עולם", "a-b-c-d-e-f", "http://example.com/a/very/long/path",
        ];
        for (var i = 0; i < texts.Length; i++)
        {
            yield return FuzzSeed.FromText("wrap-" + i.ToString(CultureInfo.InvariantCulture), [(byte)i, (byte)(9 + (i * 16))], texts[i]);
        }

        // The texts the unit tests measure and wrap
        var source = FuzzResources.ReadText(typeof(VectorGraphicsFuzzTargets).Assembly, "TextMeasurerTests.cs");
        var index = 0;
        foreach (Match match in StringLiteral().Matches(source))
        {
            yield return FuzzSeed.FromText("test-" + index.ToString(CultureInfo.InvariantCulture), [5, 10], Unescape(match.Groups["value"].Value));
            index++;
        }
    }

    private static IEnumerable<FuzzSeed> GetSvgSeeds()
    {
        byte[][] seeds =
        [
            [.. "sans-serif"u8, 0, 0, 0, 1, 0, 2, 0, 3, 0, 4, .. "Hello & <world>"u8, 0],
            [.. "Helvetica, \"Arial\""u8, 0, 5 | 8 | 16 | 32, .. "https://example.com/?a=1&b=2"u8, 0, 4 | 64, .. "tooltip"u8, 0, 6, 0, 7 | 128, .. "css"u8, 0],
            [0, 4, 0xFF, 0xFE, 0, 5 | 64, 0xED, 0xA0, 0x80, 0, 7, .. "node-id"u8, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("svg-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
