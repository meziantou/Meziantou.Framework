using System.Globalization;
using System.Text;
using System.Text.Json;

namespace SpecGen;

/// <summary>
/// Generates the tag name atoms used by the HTML parser and the DOM, and the named character reference trie.
/// </summary>
internal static class HtmlNamesGenerator
{
    // Names referenced by the HTML parsing algorithm that are not (or no longer) defined as elements by the specifications.
    private static readonly string[] ParserNames =
    [
        "acronym", "applet", "basefont", "bgsound", "big", "blink", "center", "dir", "font", "frame", "frameset",
        "image", "isindex", "keygen", "listing", "marquee", "menuitem", "multicol", "nextid", "nobr", "noembed",
        "noframes", "plaintext", "rb", "rtc", "spacer", "strike", "tt", "xmp", "param",

        // MathML
        "math", "mi", "mo", "mn", "ms", "mtext", "mglyph", "malignmark", "annotation-xml",

        // SVG names that the tree builder checks (as they appear in tokens, i.e. lowercase)
        "svg", "desc", "title", "script", "foreignobject",
    ];

    public static void Generate(GeneratorContext context)
    {
        GenerateTagNames(context);
        GenerateNamedCharacterReferences(context);
    }

    private static void GenerateTagNames(GeneratorContext context)
    {
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in new[] { "html.json", "mathml-core.json" })
        {
            using var document = JsonDocument.Parse(File.ReadAllText(context.SpecData("webref", "ed", "elements", file)));
            foreach (var element in document.RootElement.GetProperty("elements").EnumerateArray())
            {
                var name = element.GetProperty("name").GetString()!;
                if (name.Any(char.IsAsciiLetterUpper))
                    continue;

                names.Add(name);
            }
        }

        foreach (var name in ParserNames)
        {
            names.Add(name);
        }

        var identifiers = new HashSet<string>(StringComparer.Ordinal);
        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/webref/ed/elements and the HTML parsing algorithm");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html;").Line();
        writer.Line("/// <summary>Well-known lowercase tag names. <see cref=\"Unknown\"/> is used for any other name.</summary>");
        using (writer.Block("internal enum TagId : ushort"))
        {
            writer.Line("Unknown = 0,");
            foreach (var name in names)
            {
                var identifier = ToIdentifier(name);
                if (!identifiers.Add(identifier))
                    throw new InvalidOperationException($"Duplicate identifier {identifier}");

                writer.Line($"/// <summary>{name}</summary>");
                writer.Line(identifier + ",");
            }
        }

        writer.Line();
        using (writer.Block("internal static partial class TagNames"))
        {
            writer.Line($"public const int Count = {names.Count + 1};").Line();
            writer.Line("/// <summary>Tag names indexed by <see cref=\"TagId\"/>.</summary>");
            writer.Line("public static readonly string[] Names =");
            writer.Line("[");
            writer.Line("    \"\",");
            foreach (var name in names)
            {
                writer.Line("    " + CodeWriter.Literal(name) + ",");
            }

            writer.Line("];");
        }

        context.WriteFile(context.Source("Html", "TagNames.g.cs"), writer.ToString());
    }

    private static string ToIdentifier(string name)
    {
        var builder = new StringBuilder();
        var upper = true;
        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c))
            {
                upper = true;
                continue;
            }

            builder.Append(upper ? char.ToUpperInvariant(c) : c);
            upper = false;
        }

        return builder.ToString();
    }

    private static void GenerateNamedCharacterReferences(GeneratorContext context)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(context.SpecData("html", "entities.json")));
        var entities = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in document.RootElement.EnumerateObject())
        {
            var name = property.Name;
            if (!name.StartsWith('&', StringComparison.Ordinal))
                throw new InvalidOperationException($"Unexpected entity name {name}");

            var value = string.Concat(property.Value.GetProperty("codepoints").EnumerateArray().Select(cp => char.ConvertFromUtf32(cp.GetInt32())));
            entities.Add(name[1..], value);
        }

        // Build the trie
        var root = new TrieNode();
        foreach (var (name, value) in entities)
        {
            var node = root;
            foreach (var c in name)
            {
                if (!node.Children.TryGetValue(c, out var child))
                {
                    child = new TrieNode();
                    node.Children.Add(c, child);
                }

                node = child;
            }

            node.Value = value;
        }

        // Breadth-first numbering so the children of a node are contiguous
        var nodes = new List<TrieNode> { root };
        for (var i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            node.FirstChild = nodes.Count;
            foreach (var child in node.Children.Values)
            {
                nodes.Add(child);
            }
        }

        var values = new StringBuilder();
        var valueOffsets = new Dictionary<string, int>(StringComparer.Ordinal);
        var nodeFirstChild = new List<int>();
        var nodeChildCount = new List<int>();
        var nodeValueOffset = new List<int>();
        var nodeValueLength = new List<int>();
        var nodeChar = new List<int> { 0 };
        foreach (var node in nodes)
        {
            nodeFirstChild.Add(node.FirstChild);
            nodeChildCount.Add(node.Children.Count);
            foreach (var c in node.Children.Keys)
            {
                nodeChar.Add(c);
            }

            if (node.Value is null)
            {
                nodeValueOffset.Add(0);
                nodeValueLength.Add(0);
            }
            else
            {
                if (!valueOffsets.TryGetValue(node.Value, out var offset))
                {
                    offset = values.Length;
                    valueOffsets.Add(node.Value, offset);
                    values.Append(node.Value);
                }

                nodeValueOffset.Add(offset);
                nodeValueLength.Add(node.Value.Length);
            }
        }

        var writer = new CodeWriter("tests/Meziantou.Framework.WebPlatform.SpecData/html/entities.json");
        writer.Line("namespace Meziantou.Framework.WebPlatform.Html.Parsing;").Line();
        writer.Line("/// <summary>");
        writer.Line($"/// Trie of the {entities.Count.ToString(CultureInfo.InvariantCulture)} named character references (without the leading ampersand).");
        writer.Line("/// Node 0 is the root. The children of a node are stored contiguously and sorted by character.");
        writer.Line("/// </summary>");
        using (writer.Block("internal static partial class NamedCharacterReferences"))
        {
            writer.Line($"public const int LongestName = {entities.Keys.Max(k => k.Length).ToString(CultureInfo.InvariantCulture)};").Line();
            WriteArray(writer, "NodeFirstChild", "ushort", nodeFirstChild);
            WriteArray(writer, "NodeChildCount", "byte", nodeChildCount);
            writer.Line("/// <summary>Character of each node (index 0, the root, has no character).</summary>");
            WriteArray(writer, "NodeCharacter", "byte", nodeChar);
            WriteArray(writer, "NodeValueOffset", "ushort", nodeValueOffset);
            writer.Line("/// <summary>Length of the replacement in UTF-16 code units; 0 when the node is not the end of a name.</summary>");
            WriteArray(writer, "NodeValueLength", "byte", nodeValueLength);
            writer.Line($"public const string Values = {CodeWriter.Literal(values.ToString())};");
        }

        if (nodeChar.Any(c => c > 0x7F))
            throw new InvalidOperationException("Entity names must be ASCII");

        if (nodes.Count > ushort.MaxValue)
            throw new InvalidOperationException("Too many trie nodes");

        context.WriteFile(context.Source("Html", "Parsing", "NamedCharacterReferences.g.cs"), writer.ToString());
    }

    private static void WriteArray(CodeWriter writer, string name, string type, IEnumerable<int> values)
    {
        writer.Line($"public static ReadOnlySpan<{type}> {name} =>");
        writer.Line("[");
        writer.Numbers(values, perLine: 24);
        writer.Line("];").Line();
    }

    private sealed class TrieNode
    {
        public SortedDictionary<char, TrieNode> Children { get; } = [];

        public string? Value { get; set; }

        public int FirstChild { get; set; }
    }
}
