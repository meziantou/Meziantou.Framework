using System.Net;
using System.Text.RegularExpressions;

namespace SpecGen;

/// <summary>
/// Generates the built-in configurations of the HTML Sanitizer API (Html/Sanitizer/SanitizerDefaults.g.cs) from the HTML
/// specification source (https://html.spec.whatwg.org/multipage/dynamic-markup-insertion.html#sanitization-constants):
/// the sanitization category of the HTML elements and their allowed and navigating URL attributes, and the constant
/// tables of the "Sanitization constants" section.
/// </summary>
internal static partial class SanitizerGenerator
{
    private const string HtmlNamespace = "http://www.w3.org/1999/xhtml";
    private const string SvgNamespace = "http://www.w3.org/2000/svg";
    private const string MathMLNamespace = "http://www.w3.org/1998/Math/MathML";
    private const string XLinkNamespace = "http://www.w3.org/1999/xlink";

    private sealed record ElementSanitization(string Name, string Category, List<string> Attributes, List<string> NavigatingUrlAttributes);

    public static void Generate(GeneratorContext context)
    {
        var source = File.ReadAllText(context.SpecData("html-source", "source"));
        var elements = ParseElements(source);

        var constantsStart = source.IndexOf("<h4>Sanitization constants</h4>", StringComparison.Ordinal);
        var constantsEnd = source.IndexOf("<h4 id=\"sanitizer-security-considerations\">", constantsStart, StringComparison.Ordinal);
        if (constantsStart < 0 || constantsEnd < 0)
            throw new InvalidOperationException("The sanitization constants section is not found");

        var constants = source[constantsStart..constantsEnd];

        // The built-in safe default configuration: the global attributes and the MathML and SVG elements
        var attributesList = Between(constants, "<code data-x=\"dom-SanitizerConfig-attributes\">attributes</code></dt>", "</ul>");
        var defaultAttributes = CodeRegex().Matches(attributesList).Select(m => Text(m.Groups["text"].Value)).ToList();

        var tables = TableRegex().Matches(constants).Select(m => ParseTable(m.Value)).ToList();
        if (tables.Count != 4)
            throw new InvalidOperationException($"Expected 4 tables in the sanitization constants, found {tables.Count}");

        var foreignElements = tables[0].Select(row => (Name: row[0], Namespace: ParseNamespace(row[1]), Attributes: row[2].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).ToList())).ToList();
        var navigatingForeign = tables[1].Select(row => (Element: row[0], ElementNamespace: ParseNamespace(row[1]), Attribute: row[2], AttributeNamespace: row[3] == "no namespace" ? null : ParseNamespace(row[3]))).ToList();
        var animating = tables[2].Select(row => (Element: row[0], ElementNamespace: ParseNamespace(row[1]), Attribute: row[2])).ToList();
        var nonReplaceable = tables[3].Select(row => (Name: row[0], Namespace: ParseNamespace(row[1]))).ToList();

        // The built-in safe baseline configuration: the unsafe HTML elements, and the elements listed in the prose
        var baseline = Between(constants, "<dfn>built-in safe baseline configuration</dfn>", "</p>");
        var unsafeElements = elements.Where(e => e.Category == "Unsafe").Select(e => (e.Name, Namespace: HtmlNamespace)).ToList();
        foreach (Match match in BaselineElementRegex().Matches(baseline))
        {
            var ns = match.Groups["svg"].Success ? SvgNamespace : HtmlNamespace;
            var name = match.Groups["name"].Value;
            if (!unsafeElements.Contains((name, ns)))
            {
                unsafeElements.Add((name, ns));
            }
        }

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/html-source");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html;");
        writer.Line();
        using (writer.Block("internal static partial class SanitizerDefaults"))
        {
            writer.Line("/// <summary>The global attributes of the built-in safe default configuration.</summary>");
            writer.Line($"public static readonly string[] DefaultAttributes = [{string.Join(", ", defaultAttributes.Select(CodeWriter.Literal))}];");
            writer.Line();
            writer.Line("/// <summary>The elements of the built-in safe default configuration and their allowed attributes: the HTML elements whose sanitization category is Default, and the MathML and SVG elements.</summary>");
            writer.Line("public static readonly (string Name, string Namespace, string[] Attributes)[] DefaultElements =");
            writer.Line("[");
            foreach (var element in elements.Where(e => e.Category == "Default"))
            {
                writer.Line($"    ({CodeWriter.Literal(element.Name)}, {CodeWriter.Literal(HtmlNamespace)}, [{string.Join(", ", element.Attributes.Select(CodeWriter.Literal))}]),");
            }

            foreach (var element in foreignElements)
            {
                writer.Line($"    ({CodeWriter.Literal(element.Name)}, {CodeWriter.Literal(element.Namespace)}, [{string.Join(", ", element.Attributes.Select(CodeWriter.Literal))}]),");
            }

            writer.Line("];");
            writer.Line();
            writer.Line("/// <summary>The removeElements list of the built-in safe baseline configuration.</summary>");
            writer.Line("public static readonly (string Name, string Namespace)[] UnsafeElements =");
            writer.Line("[");
            foreach (var (name, ns) in unsafeElements)
            {
                writer.Line($"    ({CodeWriter.Literal(name)}, {CodeWriter.Literal(ns)}),");
            }

            writer.Line("];");
            writer.Line();
            writer.Line("/// <summary>The built-in navigating URL attributes list.</summary>");
            writer.Line("public static readonly (string Element, string ElementNamespace, string Attribute, string? AttributeNamespace)[] NavigatingUrlAttributes =");
            writer.Line("[");
            foreach (var element in elements)
            {
                foreach (var attribute in element.NavigatingUrlAttributes)
                {
                    writer.Line($"    ({CodeWriter.Literal(element.Name)}, {CodeWriter.Literal(HtmlNamespace)}, {CodeWriter.Literal(attribute)}, null),");
                }
            }

            foreach (var row in navigatingForeign)
            {
                writer.Line($"    ({CodeWriter.Literal(row.Element)}, {CodeWriter.Literal(row.ElementNamespace)}, {CodeWriter.Literal(row.Attribute)}, {(row.AttributeNamespace is null ? "null" : CodeWriter.Literal(row.AttributeNamespace))}),");
            }

            writer.Line("];");
            writer.Line();
            writer.Line("/// <summary>The built-in animating URL attributes list (the attributes are in no namespace).</summary>");
            writer.Line("public static readonly (string Element, string ElementNamespace, string Attribute)[] AnimatingUrlAttributes =");
            writer.Line("[");
            foreach (var row in animating)
            {
                writer.Line($"    ({CodeWriter.Literal(row.Element)}, {CodeWriter.Literal(row.ElementNamespace)}, {CodeWriter.Literal(row.Attribute)}),");
            }

            writer.Line("];");
            writer.Line();
            writer.Line("/// <summary>The built-in non-replaceable elements list.</summary>");
            writer.Line("public static readonly (string Name, string Namespace)[] NonReplaceableElements =");
            writer.Line("[");
            foreach (var (name, ns) in nonReplaceable)
            {
                writer.Line($"    ({CodeWriter.Literal(name)}, {CodeWriter.Literal(ns)}),");
            }

            writer.Line("];");
        }

        context.WriteFile(context.Source("Html", "Sanitizer", "SanitizerDefaults.g.cs"), writer.ToString());
    }

    /// <summary>Parses the "Sanitization" entry of the element definitions (e.g. "Default with attributes cite.").</summary>
    private static List<ElementSanitization> ParseElements(string source)
    {
        var result = new List<ElementSanitization>();
        var headings = HeadingRegex().Matches(source).ToList();
        foreach (Match match in SanitizationRegex().Matches(source))
        {
            var heading = headings.LastOrDefault(h => h.Index < match.Index);
            if (heading is null)
                continue;

            var names = ElementDfnRegex().Matches(heading.Value).Select(m => m.Groups["name"].Value).ToList();
            if (names.Count == 0)
                continue; // e.g. the custom elements

            var text = Text(match.Groups["dd"].Value);
            var category = text.Split(' ', '.')[0];
            if (category is not ("Default" or "Unsafe" or "Uncategorized"))
                throw new InvalidOperationException($"Unknown sanitization category '{text}' for {string.Join(", ", names)}");

            var definition = match.Groups["dd"].Value;
            var navigatingMatch = NavigatingRegex().Match(definition);
            var navigatingIndex = navigatingMatch.Success ? navigatingMatch.Index : -1;
            var attributesPart = navigatingIndex < 0 ? definition : definition[..navigatingIndex];
            var navigatingPart = navigatingIndex < 0 ? "" : definition[navigatingIndex..];
            var attributes = attributesPart.Contains("with attributes", StringComparison.Ordinal) ? CodeRegex().Matches(attributesPart).Select(m => Text(m.Groups["text"].Value)).ToList() : [];
            var navigating = CodeRegex().Matches(navigatingPart).Select(m => Text(m.Groups["text"].Value)).ToList();
            foreach (var name in names)
            {
                result.Add(new ElementSanitization(name, category, attributes, navigating));
            }
        }

        return result;
    }

    private static List<string[]> ParseTable(string table)
    {
        var body = table[table.IndexOf("<tbody>", StringComparison.Ordinal)..];
        var rows = new List<string[]>();
        foreach (var row in body.Split("<tr>", StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var cells = row.Split("<td>").Skip(1).Select(c => Text(Regex.Replace(c, "</td>.*", "", RegexOptions.Singleline))).ToArray();
            rows.Add(cells);
        }

        return rows;
    }

    private static string ParseNamespace(string value) => value switch
    {
        "HTML" => HtmlNamespace,
        "SVG" => SvgNamespace,
        "MathML" => MathMLNamespace,
        "XLink" => XLinkNamespace,
        _ => throw new InvalidOperationException($"Unknown namespace '{value}'"),
    };

    private static string Between(string text, string start, string end)
    {
        var startIndex = text.IndexOf(start, StringComparison.Ordinal);
        if (startIndex < 0)
            throw new InvalidOperationException($"'{start}' is not found");

        var endIndex = text.IndexOf(end, startIndex + start.Length, StringComparison.Ordinal);
        return text[(startIndex + start.Length)..endIndex];
    }

    /// <summary>The text of an HTML fragment of the specification source, whitespace normalized.</summary>
    private static string Text(string html) => WhitespaceRegex().Replace(WebUtility.HtmlDecode(TagRegex().Replace(html, "")), " ").Trim();

    [GeneratedRegex(@"<h4[^>]*>.*?</h4>", RegexOptions.Singleline)]
    private static partial Regex HeadingRegex();

    [GeneratedRegex(@"<dfn\b[^>]*\belement\b[^>]*><code>(?<name>[^<]+)</code></dfn>", RegexOptions.Singleline)]
    private static partial Regex ElementDfnRegex();

    [GeneratedRegex(@"<dt><span data-x=""concept-element-sanitization"">Sanitization</span>:</dt>\s*<dd>(?<dd>.*?)</dd>", RegexOptions.Singleline)]
    private static partial Regex SanitizationRegex();

    [GeneratedRegex(@"<span>navigating\s+URL\s+attributes</span>")]
    private static partial Regex NavigatingRegex();

    [GeneratedRegex(@"<code\b[^>]*>(?<text>.*?)</code>", RegexOptions.Singleline)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(@"<table>.*?</table>", RegexOptions.Singleline)]
    private static partial Regex TableRegex();

    [GeneratedRegex(@"(?<svg><span>SVG\s+)?<code>(?<name>[a-z]+)</code>")]
    private static partial Regex BaselineElementRegex();

    [GeneratedRegex(@"<[^>]+>", RegexOptions.Singleline)]
    private static partial Regex TagRegex();

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespaceRegex();
}
