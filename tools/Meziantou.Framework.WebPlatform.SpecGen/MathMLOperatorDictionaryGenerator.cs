using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Generates the tables of MathML Core used by the layout of the MathML elements (Css/Layout/MathML/MathOperatorDictionary.g.cs):
/// the operator dictionary (https://w3c.github.io/mathml-core/#operator-dictionary) from its compact tables (the categories of
/// the (Content, Form) keys, the values of the categories, the 2-character ASCII operators), the list of the operators whose
/// intrinsic stretch axis is inline (https://w3c.github.io/mathml-core/#dfn-intrinsic-stretch-axis), and the italic mappings of
/// <c>text-transform: math-auto</c> (https://w3c.github.io/mathml-core/#italic-mappings).
/// </summary>
internal static partial class MathMLOperatorDictionaryGenerator
{
    private static readonly string[] Forms = ["infix", "prefix", "postfix"];

    public static void Generate(GeneratorContext context)
    {
        var compact = File.ReadAllText(context.SpecData("mathml-core", "tables", "operator-dictionary-compact.html"));
        var inlineAxis = File.ReadAllText(context.SpecData("mathml-core", "tables", "inline-axis-operators.html"));
        var italic = File.ReadAllText(context.SpecData("mathml-core", "tables", "mathvariants-italic.html"));

        // https://w3c.github.io/mathml-core/#operator-dictionary-compact-special-tables: Operators_2_ascii_chars
        var asciiStart = compact.IndexOf("<code>Operators_2_ascii_chars</code>", StringComparison.Ordinal);
        var asciiCode = compact.IndexOf("<code>", asciiStart + 10, StringComparison.Ordinal) + "<code>".Length;
        var asciiEnd = compact.IndexOf("</code>", asciiCode, StringComparison.Ordinal);
        var twoAsciiChars = AsciiPairRegex().Matches(compact[asciiCode..asciiEnd]).Select(match => WebUtility.HtmlDecode(match.Groups["value"].Value)).ToList();
        if (asciiStart < 0 || twoAsciiChars.Count == 0 || twoAsciiChars.Any(value => value.Length != 2))
            throw new InvalidOperationException("Operators_2_ascii_chars is not found");

        // https://w3c.github.io/mathml-core/#operator-dictionary-category-table: the (Content, Form) keys of each category
        var tableStart = compact.IndexOf("id=\"operator-dictionary-category-table\"", StringComparison.Ordinal);
        var tableEnd = compact.IndexOf("</table>", tableStart, StringComparison.Ordinal);
        var entries = new List<(int Form, int Start, int End, string Category)>();
        foreach (Match row in CategoryRowRegex().Matches(compact[tableStart..tableEnd]))
        {
            var form = Array.IndexOf(Forms, row.Groups["form"].Value);
            if (form < 0)
                throw new InvalidOperationException("Unknown form " + row.Groups["form"].Value);

            foreach (Match range in RangeRegex().Matches(row.Groups["entries"].Value))
            {
                var start = int.Parse(range.Groups["start"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                var end = range.Groups["end"].Success ? int.Parse(range.Groups["end"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture) : start;
                entries.Add((form, start, end, row.Groups["category"].Value));
            }
        }

        // https://w3c.github.io/mathml-core/#operator-dictionary-categories-values
        var valuesStart = compact.IndexOf("id=\"operator-dictionary-categories-values\"", StringComparison.Ordinal);
        var valuesEnd = compact.IndexOf("</table>", valuesStart, StringComparison.Ordinal);
        var categories = new List<(string Name, string LSpace, string RSpace, string[] Properties)>();
        foreach (Match row in CategoryValueRegex().Matches(compact[valuesStart..valuesEnd]))
        {
            var properties = row.Groups["properties"].Value.Trim();
            categories.Add((row.Groups["name"].Value, row.Groups["lspace"].Value, row.Groups["rspace"].Value, properties == "N/A" ? [] : properties.Split(' ', StringSplitOptions.RemoveEmptyEntries)));
        }

        if (categories.Count < 3 || categories[0].Name != "Default" || entries.Count == 0)
            throw new InvalidOperationException("The operator dictionary is not found");

        var inlineCodePoints = RangeRegex().Matches(inlineAxis).Select(match => int.Parse(match.Groups["start"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).Order().ToList();

        // https://w3c.github.io/mathml-core/#italic-mappings: the Original and italic columns
        var italicMappings = ItalicRowRegex().Matches(italic)
            .Select(match => (Original: int.Parse(match.Groups["original"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture), Italic: int.Parse(match.Groups["italic"].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture)))
            .OrderBy(pair => pair.Original)
            .ToList();
        if (inlineCodePoints.Count == 0 || italicMappings.Count == 0)
            throw new InvalidOperationException("The inline stretch axis or the italic mappings are not found");

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/mathml-core/tables (https://w3c.github.io/mathml-core/#operator-dictionary)");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Css.Layout;");
        writer.Line();
        writer.Summary("The categories of the operator dictionary (https://w3c.github.io/mathml-core/#operator-dictionary-categories-values).");
        using (writer.Block("internal enum MathOperatorCategory : byte"))
        {
            foreach (var category in categories)
            {
                writer.Line(category.Name + ",");
            }
        }

        writer.Line();
        using (writer.Block("internal static partial class MathOperatorDictionary"))
        {
            writer.Summary("The lspace of each category, in em.");
            writer.Line("private static ReadOnlySpan<double> CategoryLSpace => [" + string.Join(", ", categories.Select(c => FormatEm(c.LSpace))) + "];");
            writer.Line();
            writer.Summary("The rspace of each category, in em.");
            writer.Line("private static ReadOnlySpan<double> CategoryRSpace => [" + string.Join(", ", categories.Select(c => FormatEm(c.RSpace))) + "];");
            writer.Line();
            writer.Summary("The properties of each category (<see cref=\"MathOperatorFlags\"/>).");
            writer.Line("private static ReadOnlySpan<byte> CategoryFlags => [" + string.Join(", ", categories.Select(c => FormatFlags(c.Properties))) + "];");
            writer.Line();
            writer.Summary("The strings of Operators_2_ascii_chars: the content U+0320 + index is looked up for them.");
            writer.Line("private static readonly string[] TwoAsciiChars = [" + string.Join(", ", twoAsciiChars.Select(CodeWriter.Literal)) + "];");
            writer.Line();

            entries.Sort((a, b) => ((a.Form << 20) | a.Start).CompareTo((b.Form << 20) | b.Start));
            writer.Summary("The (Content, Form) keys of the category table: the form (0 infix, 1 prefix, 2 postfix) &lt;&lt; 20 | the first code point of a range, sorted.");
            writer.Line("private static ReadOnlySpan<int> KeyRangeStarts =>");
            writer.Line("[");
            writer.Numbers(entries.Select(e => (e.Form << 20) | e.Start), 12);
            writer.Line("];");
            writer.Line();
            writer.Summary("The length - 1 of each range of <see cref=\"KeyRangeStarts\"/>.");
            writer.Line("private static ReadOnlySpan<byte> KeyRangeLengths =>");
            writer.Line("[");
            writer.Numbers(entries.Select(e => checked((byte)(e.End - e.Start))));
            writer.Line("];");
            writer.Line();
            writer.Summary("The category (<see cref=\"MathOperatorCategory\"/>) of each range of <see cref=\"KeyRangeStarts\"/>.");
            writer.Line("private static ReadOnlySpan<byte> KeyRangeCategories =>");
            writer.Line("[");
            writer.Numbers(entries.Select(e => (byte)categories.FindIndex(c => c.Name == e.Category)));
            writer.Line("];");
            writer.Line();
            writer.Summary("The characters whose intrinsic stretch axis is inline (https://w3c.github.io/mathml-core/#dfn-intrinsic-stretch-axis), sorted.");
            writer.Line("private static ReadOnlySpan<int> InlineStretchAxisCharacters =>");
            writer.Line("[");
            writer.Numbers(inlineCodePoints, 12);
            writer.Line("];");
        }

        writer.Line();
        writer.Summary("The italic mappings of <c>text-transform: math-auto</c> (https://w3c.github.io/mathml-core/#italic-mappings).");
        using (writer.Block("internal static partial class MathItalicMapping"))
        {
            writer.Summary("The original characters, sorted.");
            writer.Line("private static ReadOnlySpan<int> Originals =>");
            writer.Line("[");
            writer.Numbers(italicMappings.Select(m => m.Original), 12);
            writer.Line("];");
            writer.Line();
            writer.Summary("The italic character of each original character.");
            writer.Line("private static ReadOnlySpan<int> Italics =>");
            writer.Line("[");
            writer.Numbers(italicMappings.Select(m => m.Italic), 12);
            writer.Line("];");
        }

        context.WriteFile(context.Source("Css", "Layout", "MathML", "MathOperatorDictionary.g.cs"), writer.ToString());
    }

    private static string FormatEm(string value)
    {
        if (value == "0")
            return "0";

        if (!value.EndsWith("em", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected spacing " + value);

        return double.Parse(value[..^2], CultureInfo.InvariantCulture).ToString("R", CultureInfo.InvariantCulture);
    }

    private static string FormatFlags(string[] properties)
    {
        var flags = 0;
        foreach (var property in properties)
        {
            flags |= property switch
            {
                "stretchy" => 1,
                "symmetric" => 2,
                "largeop" => 4,
                "movablelimits" => 8,
                _ => throw new InvalidOperationException("Unknown operator property " + property),
            };
        }

        return flags.ToString(CultureInfo.InvariantCulture);
    }

    [GeneratedRegex(@"'(?<value>[^']{2})'")]
    private static partial Regex AsciiPairRegex();

    [GeneratedRegex(@"<tr><td>[^<]*<strong>(?<form>\w+)</strong> form: <code>(?<entries>[^<]*)</code></td><td>(?<category>\w+)</td></tr>")]
    private static partial Regex CategoryRowRegex();

    [GeneratedRegex(@"U\+(?<start>[0-9A-F]{4,6})(?:–U\+(?<end>[0-9A-F]{4,6}))?")]
    private static partial Regex RangeRegex();

    [GeneratedRegex(@"<tr><td>(?<name>\w+)</td><td>[^<]*</td><td>[^<]*</td><td><code>(?<lspace>[^<]*)</code></td><td><code>(?<rspace>[^<]*)</code></td><td>(?<properties>[^<]*)</td></tr>")]
    private static partial Regex CategoryValueRegex();

    [GeneratedRegex(@"<td>[^<]* U\+(?<original>[0-9A-F]{4,6})</td><td>[^<]* U\+(?<italic>[0-9A-F]{4,6})</td>")]
    private static partial Regex ItalicRowRegex();
}
