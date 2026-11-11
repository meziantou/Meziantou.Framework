using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.VectorGraphics.Fuzz;

/// <summary>
/// A color parses back from its notation. Wrapping a text keeps its characters in order, and gives lines that fit the
/// width unless they hold a single grapheme cluster. The SVG of any scene is a well-formed XML document whose root is
/// <c>svg</c>, whatever the texts, tooltips, hyperlinks and data attributes hold, and the output is deterministic. The
/// shapes of <see cref="PathBuilder"/> have the expected commands and stay in their bounds, and moving a path or
/// combining transforms agrees with moving or transforming the points; converting a path to cubic curves keeps its ends
/// and its shape. A font file loads or throws a
/// <see cref="Fonts.FontException"/>; the fonts of a scene resolve every family to one of their faces, measure a text
/// as the sum of its runs, and give a well-formed SVG whatever the families hold.
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

    private static readonly double[] PathValues = [0, 1, -1, 0.5, 2.5, 10, -10, 100, -0.0, 1e-9, 1e6, -1e6, 3.3, double.NaN, double.PositiveInfinity, double.NegativeInfinity];

    private static readonly double[] Widths = [0, 1, 5, 10, 20, 40, 60, 100, 200, 1000, double.PositiveInfinity, double.NaN, -1, 0.5, 3, 15];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("vectorgraphics-color", ColorTarget) { Seeds = GetColorSeeds, Dictionary = ColorTokens },
        new("vectorgraphics-wrap", WrapTarget) { Seeds = GetWrapSeeds, Dictionary = TextTokens },
        new("vectorgraphics-svg", SvgTarget) { Seeds = GetSvgSeeds, Dictionary = SvgTokens },
        new("vectorgraphics-fontface", FontFaceTarget) { Seeds = GetFontFaceSeeds, Dictionary = FontFileTokens },
        new("vectorgraphics-fonts", FontsTarget) { Seeds = GetFontsSeeds, Dictionary = FontFamilyTokens },
        new("vectorgraphics-path", PathTarget) { Seeds = GetPathSeeds },
        new("vectorgraphics-curves", CurvesTarget) { Seeds = GetCurvesSeeds },
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
    /// end of group, image) and its options, and the text up to the next NUL byte gives its text, tooltip, CSS class, hyperlink or
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
                    ScenePaint gradient = (kind & 8) != 0
                        ? new RadialGradientPaint(value, 1, kind % 9, 2, value, [new(0, SceneColor.White), new(1, new SceneColor(1, 2, 3, kind))])
                        : new LinearGradientPaint(0, 0, 1, 1, [new(0, SceneColor.White), new(1, new SceneColor(1, 2, 3, kind))]);
                    element = parent.Add(new SceneRectangle(value, value, kind % 13, kind % 7) { Fill = gradient, CornerRadius = kind % 3 });
                    break;
                case 1:
                    element = parent.Add(new SceneCircle(value, value, kind % 5) { Stroke = new SceneColor(kind, 0, 0, kind), StrokeDashArray = [1, value], StrokeDashOffset = value, MiterLimit = value });
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
                        ClipPath = (kind & 128) != 0 ? new PathBuilder().AddCircle(value, 5, kind % 7).Build() : null,
                        ClipRule = (SceneFillRule)(kind % 2),
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
                    // An image whose bytes have the signature of a PNG file, and the text as their content
                    element = (kind & 8) != 0 ? parent.Add(new SceneImage(value, value, kind % 9, kind % 5, (byte[])[0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, .. Encoding.UTF8.GetBytes(text)])) : parent;
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

    /// <summary>
    /// The input is a list of shapes: a byte selects the shape (polygon, ellipse, rounded rectangle) and the next bytes
    /// pick its numbers from <see cref="PathValues"/>; the last bytes give an offset and a transform. Each shape adds the
    /// expected commands and, when its numbers are finite, stays in its bounds. <see cref="PathData.Translate"/> moves
    /// every point and keeps the arcs, <see cref="PathBuilder.Append"/> concatenates the commands, and
    /// <see cref="SceneTransform.Then"/> agrees with applying the two transforms one after the other.
    /// </summary>
    private static void PathTarget(ReadOnlySpan<byte> data)
    {
        var builder = new PathBuilder();
        var count = 0;
        while (data.Length > 8 && count++ < 50)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var before = builder.Build().Commands.Count;
            switch (kind % 3)
            {
                case 0:
                    {
                        var points = new (double X, double Y)[(kind >> 2) % 8];
                        for (var i = 0; i < points.Length; i++)
                        {
                            points[i] = (ReadPathValue(ref data), ReadPathValue(ref data));
                        }

                        builder.AddPolygon(points);
                        var added = builder.Build().Commands.Skip(before).ToArray();
                        FuzzAssert.Equal(points.Length is 0 ? 0 : points.Length + 1, added.Length, "AddPolygon added a wrong number of commands");
                        for (var i = 0; i < points.Length; i++)
                        {
                            FuzzAssert.Equal(new PathCommand(i is 0 ? PathCommandKind.MoveTo : PathCommandKind.LineTo, points[i].X, points[i].Y), added[i], $"The command {i} of the polygon is wrong");
                        }

                        if (points.Length > 0)
                        {
                            FuzzAssert.Equal(PathCommandKind.Close, added[^1].Kind, "The polygon is not closed");
                        }

                        break;
                    }

                case 1:
                    {
                        var (centerX, centerY, radiusX, radiusY) = (ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
                        builder.AddEllipse(centerX, centerY, radiusX, radiusY);
                        var added = builder.Build().Commands.Skip(before).ToArray();
                        FuzzAssert.Equal("MoveTo CubicTo CubicTo CubicTo CubicTo Close", string.Join(' ', added.Select(command => command.Kind)), "The commands of the ellipse are wrong");
                        FuzzAssert.Equal((added[0].X, added[0].Y), (added[4].X, added[4].Y), "The ellipse does not end at its start");
                        AssertInBounds(added, centerX - Math.Abs(radiusX), centerY - Math.Abs(radiusY), centerX + Math.Abs(radiusX), centerY + Math.Abs(radiusY), "ellipse");
                        break;
                    }

                default:
                    {
                        var (x, y, width, height, radius) = (ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
                        builder.AddRoundedRectangle(x, y, width, height, radius);
                        var added = builder.Build().Commands.Skip(before).ToArray();
                        if (!(Math.Min(radius, Math.Min(width, height)) > 0))
                        {
                            FuzzAssert.True(added.SequenceEqual(new PathBuilder().AddRectangle(x, y, width, height).Build().Commands), "A rounded rectangle without a radius is not a rectangle");
                        }

                        FuzzAssert.True(added.Length is 5 or 10, $"The rounded rectangle has {added.Length} commands");
                        FuzzAssert.Equal(PathCommandKind.Close, added[^1].Kind, "The rounded rectangle is not closed");
                        if (width >= 0 && height >= 0)
                        {
                            AssertInBounds(added, x, y, x + width, y + height, "rounded rectangle");
                        }

                        break;
                    }
            }
        }

        var path = builder.Build();
        var (offsetX, offsetY) = (ReadPathValue(ref data), ReadPathValue(ref data));
        var moved = path.Translate(offsetX, offsetY);
        FuzzAssert.Equal(path.Commands.Count, moved.Commands.Count, "Translate changed the number of commands");
        for (var i = 0; i < path.Commands.Count; i++)
        {
            var (original, translated) = (path.Commands[i], moved.Commands[i]);
            FuzzAssert.Equal(original.Kind, translated.Kind, $"Translate changed the kind of the command {i}");
            FuzzAssert.Equal(original.Kind is PathCommandKind.Close ? original : original with { X = original.X + offsetX, Y = original.Y + offsetY }, translated with { X1 = original.X1, Y1 = original.Y1, X2 = original.X2, Y2 = original.Y2 }, $"Translate moved the end of the command {i} wrongly");
            if (original.Kind is PathCommandKind.ArcTo or PathCommandKind.Close or PathCommandKind.MoveTo or PathCommandKind.LineTo)
            {
                FuzzAssert.Equal((original.X1, original.Y1, original.X2, original.Y2), (translated.X1, translated.Y1, translated.X2, translated.Y2), $"Translate changed the parameters of the command {i}");
            }
            else
            {
                FuzzAssert.Equal((original.X1 + offsetX, original.Y1 + offsetY), (translated.X1, translated.Y1), $"Translate moved the first control point of the command {i} wrongly");
            }
        }

        // The shapes have no arc and no quadratic curve: ToCubicCurves keeps their commands
        FuzzAssert.True(path.ToCubicCurves().Commands.SequenceEqual(path.Commands), "ToCubicCurves changed a path without arcs and quadratic curves");

        var combined = new PathBuilder().Append(path).Append(moved).Build();
        FuzzAssert.True(combined.Commands.SequenceEqual(path.Commands.Concat(moved.Commands)), "Append does not concatenate the commands");
        FuzzAssert.Equal(combined.ToString(), combined.ToString(), "The path data is not deterministic");

        var first = SceneTransform.Scale(ReadPathValue(ref data), ReadPathValue(ref data), offsetX, offsetY).Then(SceneTransform.Rotate(ReadPathValue(ref data)));
        var second = SceneTransform.Translate(ReadPathValue(ref data), ReadPathValue(ref data));
        var (pointX, pointY) = (ReadPathValue(ref data), ReadPathValue(ref data));
        var stepwise = first.Apply(pointX, pointY);
        stepwise = second.Apply(stepwise.X, stepwise.Y);
        var combinedPoint = first.Then(second).Apply(pointX, pointY);
        if (double.IsFinite(stepwise.X) && double.IsFinite(stepwise.Y))
        {
            FuzzAssert.True(AreClose(stepwise.X, combinedPoint.X) && AreClose(stepwise.Y, combinedPoint.Y), $"Then gives {combinedPoint} instead of {stepwise}");
        }

        if (double.IsFinite(first.A) && double.IsFinite(first.B) && double.IsFinite(first.C) && double.IsFinite(first.D) && double.IsFinite(first.E) && double.IsFinite(first.F))
        {
            FuzzAssert.Equal(first, first.Then(SceneTransform.Identity), "Then with the identity changed the transform");
            FuzzAssert.Equal(first, SceneTransform.Identity.Then(first), "The identity then the transform is not the transform");
        }
    }

    /// <summary>
    /// The input picks a start point, the radii, the rotation, the flags and the end point of an arc, and the control
    /// point of a quadratic curve back to the start. <see cref="PathData.ToCubicCurves"/> leaves only lines and cubic
    /// curves, at most four curves for the arc, which ends at its end point; the cubic curve of the quadratic curve has its
    /// shape.
    /// </summary>
    private static void CurvesTarget(ReadOnlySpan<byte> data)
    {
        var (startX, startY) = (ReadPathValue(ref data), ReadPathValue(ref data));
        var (arcX, arcY, controlX, controlY) = (ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data));
        var arcFlags = FuzzInput.ReadByte(ref data);
        var curves = new PathBuilder().MoveTo(startX, startY)
            .ArcTo(ReadPathValue(ref data), ReadPathValue(ref data), ReadPathValue(ref data), largeArc: (arcFlags & 1) != 0, sweep: (arcFlags & 2) != 0, arcX, arcY)
            .QuadraticTo(controlX, controlY, startX, startY)
            .Build().ToCubicCurves();
        FuzzAssert.True(curves.Commands.All(command => command.Kind is PathCommandKind.MoveTo or PathCommandKind.LineTo or PathCommandKind.CubicTo), "ToCubicCurves left an arc or a quadratic curve");
        FuzzAssert.True(curves.Commands.Count <= 6, $"An arc gave {curves.Commands.Count - 2} curves");
        if (startX != arcX || startY != arcY)
        {
            FuzzAssert.Equal((arcX, arcY), (curves.Commands[^2].X, curves.Commands[^2].Y), "The arc does not end at its end point");
        }

        var quadratic = curves.Commands[^1];
        FuzzAssert.Equal((startX, startY), (quadratic.X, quadratic.Y), "The quadratic curve does not end at its end point");
        if (double.IsFinite(arcX) && double.IsFinite(arcY) && double.IsFinite(controlX) && double.IsFinite(controlY) && double.IsFinite(startX) && double.IsFinite(startY))
        {
            // The middle of a quadratic curve is (P0 + 2 Q + P2) / 4, and the middle of a cubic curve (P0 + 3 C1 + 3 C2 + P3) / 8
            var middleX = (arcX + (3 * quadratic.X1) + (3 * quadratic.X2) + startX) / 8;
            var middleY = (arcY + (3 * quadratic.Y1) + (3 * quadratic.Y2) + startY) / 8;
            FuzzAssert.True(AreClose(middleX, (arcX + (2 * controlX) + startX) / 4) && AreClose(middleY, (arcY + (2 * controlY) + startY) / 4), "The cubic curve of a quadratic curve has another shape");
        }

    }

    private static double ReadPathValue(ref ReadOnlySpan<byte> data) => FuzzInput.Pick(PathValues, FuzzInput.ReadByte(ref data));

    private static void AssertInBounds(PathCommand[] commands, double left, double top, double right, double bottom, string shape)
    {
        if (!double.IsFinite(left) || !double.IsFinite(top) || !double.IsFinite(right) || !double.IsFinite(bottom))
            return;

        foreach (var command in commands.Where(command => command.Kind is not PathCommandKind.Close))
        {
            var points = command.Kind is PathCommandKind.CubicTo ? new[] { (command.X, command.Y), (command.X1, command.Y1), (command.X2, command.Y2) } : [(command.X, command.Y)];
            foreach (var (x, y) in points)
            {
                FuzzAssert.True(x >= left - Tolerance(left, right) && x <= right + Tolerance(left, right) && y >= top - Tolerance(top, bottom) && y <= bottom + Tolerance(top, bottom), $"The point ({x}, {y}) of the {shape} is outside of ({left}, {top}, {right}, {bottom})");
            }
        }
    }

    private static double Tolerance(double a, double b) => 1e-9 * (1 + Math.Max(Math.Abs(a), Math.Abs(b)));

    private static bool AreClose(double a, double b) => Math.Abs(a - b) <= 1e-6 * (1 + Math.Max(Math.Abs(a), Math.Abs(b)));

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
            [.. "sans-serif"u8, 0, 0 | 8, 0, 5 | 128 | 32, 0, 7 | 8, .. "IHDR"u8, 0, 6, 0, 0 | 8 | 16, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("svg-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetPathSeeds()
    {
        // The shapes of the unit tests: a triangle, an ellipse and a rounded rectangle, moved by (0, 20)
        byte[][] seeds =
        [
            [0 | (3 << 2), 0, 0, 5, 0, 4, 7, 1, 4, 5, 5, 4, 2, 5, 0, 5, 5, 4, 0, 0, 5, 1, 1, 2, 3, 4, 5],
            [1, 13, 13, 5, 14, 2, 0, 0, 10, 15, 4, 0, 0, 0, 0, 1, 2],
            [2, 2, 10, 1, 1, 8, 2, 7, 7, 6, 6, 0, 0, 0, 0, 0, 0, 0],
            [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("path-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    private static IEnumerable<FuzzSeed> GetCurvesSeeds()
    {
        // A half circle (start, end, control, flags, radii, rotation), a large arc with small radii, a rotated ellipse, an arc of no radius, and an arc that ends at its start
        byte[][] seeds =
        [
            [0, 0, 5, 0, 5, 5, 2, 1, 1, 0],
            [0, 0, 7, 7, 1, 2, 3, 3, 3, 12],
            [1, 2, 7, 8, 4, 6, 1, 5, 4, 5],
            [0, 0, 5, 5, 3, 3, 0, 0, 5, 0],
            [5, 5, 5, 5, 0, 0, 3, 5, 5, 0],
            [13, 14, 5, 15, 0, 0, 1, 13, 14, 15],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("curves-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }
}
