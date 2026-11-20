using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Graphviz.Fuzz;

/// <summary>
/// Reading a DOT text only throws a <see cref="GraphvizParseException"/>, agrees with TryParse, and reports its errors and
/// warnings inside the text. A graph that reads writes back as a DOT text that reads to a graph written the same way.
/// Drawing a graph gives finite coordinates and a well-formed, deterministic SVG.
/// </summary>
public static partial class GraphvizFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "digraph", "graph", "strict", "subgraph", "node", "edge", "{", "}", "[", "]", "=", ";", ",", ":", "+", "->", "--", "\"", "<", ">", "\n", "\r\n", " ",
        "//", "/*", "*/", "\n#", "\\", "\\\"", "\\\\", "\\\n", "\\n", "\\l", "\\r", "\\N", "\\G", "\\E", "\\T", "\\H", ((char)0xFEFF).ToString(), "-1.5", ".5", "2a",
        "cluster_", "cluster=true", "label=", "xlabel=", "shape=", "style=", "color=", "fillcolor=", "bgcolor=", "pencolor=", "fontcolor=", "fontsize=", "penwidth=",
        "URL=", "href=", "javascript:", "https:", "mailto:", "tooltip=", "class=", "sides=", "rankdir=LR", "rankdir=BT", "nodesep=", "ranksep=", "splines=ortho", "splines=line", "compound=true",
        "lhead=", "ltail=", "dir=both", "dir=back", "dir=none", "arrowhead=", "arrowtail=", "minlen=", "rank=same", "layout=neato", "headport=", ":n", ":sw",
        "box", "ellipse", "circle", "doublecircle", "point", "diamond", "record", "Mrecord", "plaintext", "polygon", "note", "cylinder", "star", "none",
        "filled", "rounded", "dashed", "dotted", "bold", "invis", "solid", "setlinewidth(2)", "normal", "dot", "tee", "diamond", "odot", "vee",
        "red", "green", "gray50", "#ff0000", "#ff000080", "0.5 1 1", "red:blue", "/x11/navyblue", "transparent",
        "<f0>", "|", "\\{", "\\|", "{a|b}", "<b>", "</b>", "<br/>", "<table>", "<tr>", "<td>", "</td>", "</tr>", "</table>", "&amp;", "&#65;", "<!-- -->",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("graphviz-parse", ParseTarget) { Seeds = GetSeeds, Dictionary = Tokens },
        new("graphviz-round-trip", RoundTripTarget) { Seeds = GetSeeds, Dictionary = Tokens },
        new("graphviz-render", RenderTarget) { Seeds = GetSeeds, Dictionary = Tokens },
    ];

    private static readonly GraphvizParseOptions Options = new() { MaxEdges = 400, MaxNodes = 200, MaxTextSize = 20_000, MaxDepth = 16, MaxInheritedAttributes = 20_000 };

    // The written text of an expanded graph can be longer than the text it was read from
    private static readonly GraphvizParseOptions WrittenOptions = new() { MaxEdges = 400, MaxNodes = 200, MaxTextSize = int.MaxValue, MaxDepth = 16, MaxInheritedAttributes = int.MaxValue };

    /// <summary><see cref="GraphvizDocument.Parse(string, GraphvizParseOptions?)"/> agrees with TryParse, and its errors and warnings are inside the text.</summary>
    private static void ParseTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var succeeded = GraphvizDocument.TryParse(text, Options, out var document, out var tryError);
        GraphvizParseError? error = null;
        try
        {
            document = GraphvizDocument.Parse(text, Options);
        }
        catch (GraphvizParseException exception)
        {
            error = exception.Error;
        }

        FuzzAssert.Equal(succeeded, error is null, $"Parse and TryParse disagree on {FuzzAssert.Format(text)}");
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        if (error is { } parseError)
        {
            FuzzAssert.Equal(parseError, tryError, "Parse and TryParse report different errors");
            CheckPosition(lines, parseError.Line, parseError.Column, parseError.ToString());
            return;
        }

        FuzzAssert.True(document is not null, "Parse returned no document");
        foreach (var warning in document!.Warnings)
        {
            CheckPosition(lines, warning.Line, warning.Column, warning.ToString());
        }

        CheckLinks(document.Diagram.Nodes, document.Diagram.Subgraphs);
    }

    /// <summary>The links of the nodes and the clusters cannot run a script: they are relative, or use http, https or mailto.</summary>
    private static void CheckLinks(IEnumerable<FlowchartNode> nodes, IEnumerable<FlowchartSubgraph> subgraphs)
    {
        foreach (var node in nodes)
        {
            CheckLink(node.Link);
        }

        foreach (var subgraph in subgraphs)
        {
            CheckLink(subgraph.Link);
            CheckLinks(subgraph.Nodes, subgraph.Subgraphs);
        }

        static void CheckLink(string? link)
        {
            if (link is null)
                return;

            var trimmed = link.Trim();
            var colon = trimmed.IndexOf(':', StringComparison.Ordinal);
            var separator = trimmed.IndexOfAny(['/', '?', '#']);
            if (colon < 0 || (separator >= 0 && separator < colon))
                return;

            var scheme = trimmed[..colon];
            FuzzAssert.True(
                scheme.Equals("http", StringComparison.OrdinalIgnoreCase) || scheme.Equals("https", StringComparison.OrdinalIgnoreCase) || scheme.Equals("mailto", StringComparison.OrdinalIgnoreCase),
                $"The link {FuzzAssert.Format(link)} has the scheme '{scheme}'");
        }
    }

    private static void CheckPosition(string[] lines, int line, int column, string description)
    {
        FuzzAssert.True(line >= 1 && line <= lines.Length, $"The line of {description} is outside of the text");
        FuzzAssert.True(column >= 1 && column <= lines[line - 1].Length + 1, $"The column of {description} is outside of its line");
    }

    /// <summary>A graph that reads writes back as a text that reads, and writing it again gives the same text.</summary>
    private static void RoundTripTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (!GraphvizDocument.TryParse(text, Options, out var document, out _))
            return;

        var written = GraphvizWriter.Write(document.Diagram);
        if (!GraphvizDocument.TryParse(written, WrittenOptions, out var reparsed, out var error))
        {
            FuzzAssert.Fail($"The text written from {FuzzAssert.Format(text)} does not read back: {error}\n{written}");
            return;
        }

        var again = GraphvizWriter.Write(reparsed.Diagram);
        FuzzAssert.Equal(written, again, $"Writing the graph of {FuzzAssert.Format(text)} twice gives different texts");
    }

    /// <summary>Drawing a graph gives finite coordinates, and a well-formed SVG that is the same each time.</summary>
    private static void RenderTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (!GraphvizDocument.TryParse(text, Options, out var document, out _))
            return;

        var scene = document.Diagram.CreateScene();
        FuzzAssert.True(double.IsFinite(scene.Width) && double.IsFinite(scene.Height) && scene.Width > 0 && scene.Height > 0, $"The size of the scene is {scene.Width}x{scene.Height}");
        CheckFinite(scene.Root, "root");
        var svg = SvgSceneRenderer.Render(scene);
        FuzzAssert.Equal(svg, SvgSceneRenderer.Render(document.Diagram.CreateScene()), "Drawing the graph twice gives different outputs");
        try
        {
            var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
            using var reader = XmlReader.Create(new StringReader(svg), settings);
            while (reader.Read())
            {
            }
        }
        catch (XmlException exception)
        {
            FuzzAssert.Fail("The SVG is not well-formed: " + exception.Message);
        }
    }

    private static void CheckFinite(SceneElement element, string path)
    {
        switch (element)
        {
            case SceneGroup group:
                if (group.Transform is { } transform)
                {
                    Check(transform.E, path, "translate x");
                    Check(transform.F, path, "translate y");
                }

                for (var i = 0; i < group.Children.Count; i++)
                {
                    CheckFinite(group.Children[i], path + "/" + i.ToString(CultureInfo.InvariantCulture));
                }

                break;
            case SceneRectangle rectangle:
                Check(rectangle.X, path, "x");
                Check(rectangle.Y, path, "y");
                Check(rectangle.Width, path, "width");
                Check(rectangle.Height, path, "height");
                break;
            case SceneCircle circle:
                Check(circle.CenterX, path, "cx");
                Check(circle.CenterY, path, "cy");
                Check(circle.Radius, path, "r");
                break;
            case SceneLine line:
                Check(line.X1, path, "x1");
                Check(line.Y1, path, "y1");
                Check(line.X2, path, "x2");
                Check(line.Y2, path, "y2");
                break;
            case ScenePath scenePath:
                foreach (var command in scenePath.Data.Commands)
                {
                    Check(command.X, path, "path x");
                    Check(command.Y, path, "path y");
                    Check(command.X1, path, "path x1");
                    Check(command.Y1, path, "path y1");
                    Check(command.X2, path, "path x2");
                    Check(command.Y2, path, "path y2");
                }

                break;
            case SceneText sceneText:
                Check(sceneText.X, path, "text x");
                Check(sceneText.Y, path, "text y");
                break;
        }

        static void Check(double value, string path, string name) => FuzzAssert.True(double.IsFinite(value), $"The {name} of {path} is {value}");
    }

    private static IEnumerable<FuzzSeed> GetSeeds()
    {
        yield return FuzzSeed.FromText("simple", "digraph { a -> b }");
        yield return FuzzSeed.FromText("undirected", "graph G { a -- b -- c; c -- a [label=\"x\"] }");
        yield return FuzzSeed.FromText("html", "digraph { a [label=<<b>x</b><br/>y>]; a -> b }");
        yield return FuzzSeed.FromText("record", "digraph { r [shape=record, label=\"<f0> a|{b|<f1> c}\"]; r:f0 -> r:f1 }");

        var assembly = typeof(GraphvizFuzzTargets).Assembly;
        foreach (var file in new[] { "GraphvizDocumentTests.cs", "GraphvizWriterTests.cs" })
        {
            var source = FuzzResources.ReadText(assembly, file);
            var index = 0;
            foreach (Match match in RawStringLiteral().Matches(source))
            {
                yield return FuzzSeed.FromText(Path.GetFileNameWithoutExtension(file) + "-" + index.ToString(CultureInfo.InvariantCulture), Dedent(match.Groups["value"].Value));
                index++;
            }
        }
    }

    /// <summary>Removes the indentation of the closing line of a raw string literal from its lines.</summary>
    private static string Dedent(string value)
    {
        var lines = value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length is 1)
            return value;

        var indentation = lines[^1].Length - lines[^1].TrimStart().Length;
        var builder = new StringBuilder();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var line = lines[i];
            builder.Append(line.Length >= indentation ? line[indentation..] : line.TrimStart()).Append('\n');
        }

        return builder.ToString();
    }

    // A raw string literal of three quotes, as the DOT texts of the tests
    [GeneratedRegex("\"\"\"(?<value>.*?)\"\"\"", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RawStringLiteral();
}
