using System.Globalization;
using System.Text;
using Meziantou.Framework.Fuzzing;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Svg;

namespace Meziantou.Framework.WebPlatform.Fuzz;

/// <summary>
/// The target of the SVG texts laid out in the shapes of <c>shape-inside</c>: the input is decoded to the shapes (SVG shape
/// elements and basic shapes), the subtracted shapes and their margins, the padding, the writing mode, the direction, the
/// alignment, the font size and the text. The characters that are rendered (other than the spaces, which hang at the ends
/// of the lines) are in the bounds of the shapes and have finite positions, the ones that are not rendered are the end of the text, and the layout is the same when it is computed again.
/// </summary>
public static partial class WebPlatformFuzzTargets
{
    private static readonly string[] SvgTextWritingModes = ["horizontal-tb", "vertical-rl", "vertical-lr", "sideways-lr", "sideways-rl"];
    private static readonly string[] SvgTextAlignments = ["start", "end", "left", "right", "center", "justify"];

    /// <summary>The characters of the text: words, spaces, a forced line break (with white-space: pre-wrap), right-to-left letters.</summary>
    private const string SvgTextAlphabet = "ab cdefg hij\nklm אב ال";

    /// <summary>
    /// The tokens of the target: the kinds of the shapes followed by their sizes, and words (a property: the list of the
    /// targets, in another file, is initialized before the fields of this one).
    /// </summary>
    private static string[] SvgTextShapeTokens => ["\u0000\u0010\u0010\u0080\u0080", "\u0001\u0080\u0080@", "\u0003  À@", "aaa a", "\u000c\u000c"];

    /// <summary>Decodes the input to an SVG text with a shape-inside, and checks its layout.</summary>
    public static void SvgTextShapes(ReadOnlySpan<byte> data)
    {
        var position = 0;
        byte Next(ReadOnlySpan<byte> input) => position < input.Length ? input[position++] : (byte)0;

        var svg = new StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' width='300' height='300'>");
        var bounds = (MinX: double.PositiveInfinity, MinY: double.PositiveInfinity, MaxX: double.NegativeInfinity, MaxY: double.NegativeInfinity);
        var inside = new List<string>();
        var shapeCount = (Next(data) % 3) + 1;
        for (var i = 0; i < shapeCount; i++)
        {
            var (value, box) = AddShape(svg, "s" + i.ToString(CultureInfo.InvariantCulture), Next(data), Next(data), Next(data), Next(data), Next(data), margin: null);
            inside.Add(value);
            bounds = (Math.Min(bounds.MinX, box.MinX), Math.Min(bounds.MinY, box.MinY), Math.Max(bounds.MaxX, box.MaxX), Math.Max(bounds.MaxY, box.MaxY));
        }

        var subtracted = new List<string>();
        var subtractCount = Next(data) % 3;
        for (var i = 0; i < subtractCount; i++)
        {
            subtracted.Add(AddShape(svg, "x" + i.ToString(CultureInfo.InvariantCulture), Next(data), Next(data), Next(data), Next(data), Next(data), margin: Next(data) % 30).Value);
        }

        var style = string.Create(CultureInfo.InvariantCulture, $"font: {(Next(data) % 37) + 4}px/{(Next(data) % 3) + 1} sans-serif; shape-inside: {string.Join(' ', inside)}; shape-padding: {Next(data) % 30}px; writing-mode: {SvgTextWritingModes[Next(data) % SvgTextWritingModes.Length]}; direction: {((Next(data) & 1) == 0 ? "ltr" : "rtl")}; text-align: {SvgTextAlignments[Next(data) % SvgTextAlignments.Length]}; white-space: {((Next(data) & 1) == 0 ? "normal" : "pre-wrap")}");
        if (subtracted.Count > 0)
        {
            style += "; shape-subtract: " + string.Join(' ', subtracted);
        }

        var text = new StringBuilder();
        while (position < data.Length && text.Length < 200)
        {
            text.Append(SvgTextAlphabet[Next(data) % SvgTextAlphabet.Length]);
        }

        svg.Append("<text id='t' style='").Append(style).Append("'>").Append(text).Append("</text></svg>");

        using var context = new BrowsingContext();
        var document = context.Navigate("https://example.com/", _ => svg.ToString());
        context.EventLoop.RunUntilIdle();
        var element = document.GetElementById("t")!;
        var layout = SvgTextLayout.Get(element);
        FuzzAssert.True(layout is not null, "the text is laid out");

        var characters = layout.Characters;
        var hidden = false;
        foreach (ref readonly var character in characters.AsSpan())
        {
            FuzzAssert.True(!hidden || character.IsHidden, "the characters that are not rendered are the end of the text");
            hidden |= character.IsHidden;
            // The spaces at the ends of the lines hang: they may be outside of the shapes
            if (character.IsHidden || !character.IsClusterStart || char.IsWhiteSpace(character.TextNode.Data[character.TextOffset]))
                continue;

            FuzzAssert.True(double.IsFinite(character.Position.X) && double.IsFinite(character.Position.Y), "the position of a rendered character is finite");

            // The start of a character is in its segment, which is inside a shape (one unit of tolerance for the sampling of
            // the edges and the rounding of the sums of the advances)
            FuzzAssert.True(character.Position.X >= bounds.MinX - 1 && character.Position.X <= bounds.MaxX + 1 && character.Position.Y >= bounds.MinY - 1 && character.Position.Y <= bounds.MaxY + 1, string.Create(CultureInfo.InvariantCulture, $"a rendered character ({character.Position.X}, {character.Position.Y}) is in the bounds of the shapes ({bounds.MinX}, {bounds.MinY}, {bounds.MaxX}, {bounds.MaxY}): {svg}"));
        }

        // The layout does not depend on the previous layouts (e.g. the advances widened by a justification)
        element.SetAttribute("data-relayout", "1");
        var again = SvgTextLayout.Get(element)!.Characters;
        FuzzAssert.Equal(characters.Length, again.Length, "the number of characters of the layout computed again");
        for (var i = 0; i < characters.Length; i++)
        {
            FuzzAssert.Equal(characters[i].Position, again[i].Position, "the position of a character of the layout computed again");
            FuzzAssert.Equal(characters[i].IsHidden, again[i].IsHidden, "the visibility of a character of the layout computed again");
        }

        // Adds a shape: an SVG shape element referenced by a URL (rect, circle, ellipse) or a basic shape (polygon(), circle())
        static (string Value, (double MinX, double MinY, double MaxX, double MaxY) Box) AddShape(StringBuilder svg, string id, byte kind, byte a, byte b, byte c, byte d, int? margin)
        {
            var marginStyle = margin is { } value ? string.Create(CultureInfo.InvariantCulture, $" style='shape-margin: {value}px'") : "";
            switch (kind % 5)
            {
                case 0:
                    svg.Append(CultureInfo.InvariantCulture, $"<rect id='{id}' x='{a}' y='{b}' width='{c}' height='{d}'{marginStyle}/>");
                    return ($"url(#{id})", (a, b, a + c, b + d));

                case 1:
                    svg.Append(CultureInfo.InvariantCulture, $"<circle id='{id}' cx='{a}' cy='{b}' r='{c}'{marginStyle}/>");
                    return ($"url(#{id})", (a - c, b - c, a + c, b + c));

                case 2:
                    svg.Append(CultureInfo.InvariantCulture, $"<ellipse id='{id}' cx='{a}' cy='{b}' rx='{c}' ry='{d}'{marginStyle}/>");
                    return ($"url(#{id})", (a - c, b - d, a + c, b + d));

                case 3:
                    return (string.Create(CultureInfo.InvariantCulture, $"polygon({a}px {b}px, {a + c}px {b}px, {a}px {b + d}px)"), (a, b, a + c, b + d));

                default:
                    return (string.Create(CultureInfo.InvariantCulture, $"circle({c}px at {a}px {b}px)"), (a - c, b - c, a + c, b + c));
            }
        }
    }

    /// <summary>The seeds: the scenarios of the unit tests (a rectangle, shapes that overflow into the next one around a subtracted shape, a padding, a circle) and the WPT ones.</summary>
    private static IEnumerable<FuzzSeed> GetSvgTextShapesSeeds()
    {
        byte[][] scenarios =
        [
            // A rectangle, horizontal, start-aligned
            [0, 0, 10, 20, 100, 100, 0, 16, 1, 0, 0, 0, 0, 0, .. "aaa bbbb cc"u8],

            // Two rectangles and a subtracted rectangle with a margin, centered
            [1, 0, 0, 0, 100, 30, 0, 200, 0, 100, 60, 1, 0, 240, 0, 20, 30, 5, 16, 1, 0, 0, 0, 4, 0, .. "aaaa bb cc dddd eeeeeeee"u8],

            // A circle with a padding, justified, right-to-left
            [0, 1, 150, 150, 100, 0, 0, 12, 0, 20, 0, 1, 5, 0, .. "lorem ipsum dolor sit amet consectetur"u8],

            // A polygon in vertical-rl text, and a basic circle overflowing into an ellipse in sideways-lr text
            [0, 3, 20, 20, 200, 200, 0, 16, 1, 0, 1, 0, 1, 1, .. "aa bb cc dd\nee"u8],
            [1, 4, 100, 100, 60, 0, 2, 200, 150, 50, 90, 0, 10, 2, 5, 3, 1, 3, 0, .. "words in two shapes"u8],
        ];

        for (var i = 0; i < scenarios.Length; i++)
        {
            yield return new FuzzSeed("svg-text-shapes-" + i.ToString(CultureInfo.InvariantCulture), scenarios[i]);
        }
    }
}
