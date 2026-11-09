using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using Meziantou.Framework.Diagrams.Layout;
using Meziantou.Framework.Diagrams.Mermaid;
using Meziantou.Framework.Fuzzing;

namespace Meziantou.Framework.Diagrams.Fuzz;

/// <summary>
/// Reading a Mermaid text only throws a <see cref="MermaidParseException"/>, at a position inside the text, and agrees with
/// TryParse. A diagram that reads writes back as a text that reads to the same diagram (the second writing is the same
/// as the first). Drawing a diagram gives finite coordinates and a well-formed, deterministic SVG. The layered layout
/// keeps the nodes apart, inside their clusters, at finite positions.
/// </summary>
public static partial class DiagramsFuzzTargets
{
    private static readonly string[] Tokens =
    [
        "flowchart", "graph", "TD", "TB", "BT", "LR", "RL", "subgraph", "end", "direction", "classDef", "class", "style", "linkStyle", "default", "interpolate",
        "click", "href", "call", "accTitle:", "accDescr:", "accDescr {", "---", "title:", "config:", "theme:", "themeVariables:", "%%", "%%{init: ", "}%%",
        "-->", "---", "-.->", "-.-", "==>", "===", "~~~", "--o", "--x", "<-->", "o--o", "x--x", "-- ", " -->", "-. ", " .->", "== ", " ==>", "|", "&", ";", "\n",
        "[", "]", "(", ")", "[[", "]]", "[(", ")]", "((", "))", "(((", ")))", "([", "])", "{", "}", "{{", "}}", "[/", "/]", "[\\", "\\]", ">", ":::", "@{", "e1@",
        "shape:", "label:", "rect", "cyl", "diam", "\"", "`", "**", "_", "<br>", "#quot;", "#35;", "&amp;", "fill:", "stroke:", "stroke-width:", "color:",
        "#f96", "rgb(1,2,3)", "hsl(0, 0%, 50%)", "curve:", "basis", "linear", "A", "B", "id", "é", "\U0001F600", "\uD800",
        "erDiagram", "||", "|o", "o|", "}o", "o{", "}|", "|{", "--", "..", ".-", "-.", "u--", "one or more", "zero or many", "only one", "many(0)", "1+", "0+",
        " to ", "optionally to", " : ", "PK", "FK", "UK", "PK, FK", "string", "List~int~", "`a b`", "\"comment\"", "[\"alias\"]",
        "requirementDiagram", "requirement", "functionalRequirement", "designConstraint", "element", "text:", "risk:", "verifymethod:", "type:", "docref:",
        "high", "test", " - satisfies -> ", " <- copies - ", "contains", "traces",
        "usecase-beta", "actor", "systemBoundary", "note for", "json", "@{ type: package }", "@{ type: hollow }", "@{ business: true }", "<<Core>>",
        "..> : include", "..> : extend", "--|>", "<--", "o--", "x--", "---->", "e1@--", "\"`**md**`\"", "{ \"a\": [1, {\"b\": null}] }",
        "C4Context", "C4Container", "C4Deployment", "C4Dynamic", "Person(", "System_Ext(", "ContainerDb(", "ComponentQueue_Ext(", "Boundary(", "System_Boundary(",
        "Deployment_Node(", "Node_R(", "Rel(", "BiRel(", "Rel_Back(", "RelIndex(", "UpdateElementStyle(", "UpdateRelStyle(", "UpdateLayoutConfig(", "$descr=\"", "$bgColor=\"",
        "$offsetX=\"", "$c4ShapeInRow=\"", ", ", ")", ") {",
        "block", "block-beta", "block:", "columns 3", "columns auto", "space", "space:2", "<[\"x\"]>(right)", "(x, down)", ":2", "[(\"db\")]", "((\"c\"))", "{{\"h\"}}", "end",
        "architecture-beta", "group", "service", "junction", "align row", "align column", " in ", "(cloud)", "(database)", "[Title]", "{group}", ":L", "R:", " -- ", " <--> ", "-[label]-",
        "railroad-beta", "railroad-ebnf-beta", "railroad-abnf-beta", "railroad-peg-beta", " = ", " ::= ", " <- ", " ;", ";\n", "terminal(\"a\")", "nonterminal(\"b\")",
        "sequence(", "choice(", "optional(", "zeroOrMore(", "oneOrMore(", "special(\"s\")", " | ", " / ", "[ ", " ]", "{ ", " }", "( ", " )", "? text ?", "?", "*", "+",
        " - ", "(* c *)", "/* c */", "; c\n", "# c\n", "2*4", "1*", "*1", "3", "%x30-39", "%d65.66", "&", "!", ".", "'a'", "\"\\n\"", "title \"t\"",
        "sequenceDiagram", "participant", "actor", " as ", "@{ \"type\": \"database\" }", "create participant", "destroy", "box", "->>", "-->>", "<<->>", "-x",
        "--)", "-|\\", "--|/", "-\\\\", "-//", "/|-", "\\\\--", "+", "()", "activate", "deactivate", "Note left of", "Note right of", "Note over", "A,B",
        "loop", "alt", "else", "opt", "par", "and", "par_over", "critical", "option", "break", "rect rgb(0, 255, 0)", "autonumber", "autonumber 10 5", "off",
        "#59;", "#35;", "wrap:", "link A: x @ https://e.com", "title x",
        "gitGraph", "gitGraph TB:", "gitGraph BT:", "commit", "commit id: \"a\"", " tag: \"v1\"", " msg: \"m\"", " type: HIGHLIGHT", " type: REVERSE", "branch develop", " order: 2", "checkout main", "switch develop", "merge develop", "cherry-pick id: \"a\"", " parent: \"b\"",
        "kanban", "\n  ", "\n    ", "[Text]", "(Text)", "((Text))", "{{Text}}", "@{ ticket: T-1 }", "@{ assigned: 'x' }", "@{ priority: 'Very High' }", ":::cls", "::icon(x)", "\"`**md**`\"", "ticketBaseUrl: 'https://x/#TICKET#'",
        "packet", "packet-beta", "0-15: \"Port\"", "16: \"F\"", "+8: \"Flags\"", "bitsPerRow: 8", "showBits: false", "bitOrder: descending",
        "gantt", "dateFormat ", "axisFormat ", "tickInterval ", "1week", "2day", "weekday monday", "weekend friday", "excludes ", "weekends", "includes ",
        "inclusiveEndDates", "topAxis", "todayMarker ", "off", "section ", "title ", "accDescription ", "displayMode: compact", "YYYY-MM-DD", "DD/MM/YYYY", "HH:mm",
        "X", "x", "[at] ", "Do", "dddd", "MMMM", "hh:mm A", "2024-01-01", "2024-02-29T10:30:00.000", "17:49", ":", "done, ", "active, ", "crit, ", "milestone, ",
        "vert, ", "after ", "until ", "3d", "1.5h", "2w", "1M", "1y", "250ms", "10s", "30m", "0d", "%b %d", "%Y-%m-%d", "%H:%M", "%-d", "click a href \"https://a\"",
        "timeline", "timeline LR", "timeline TD", "# comment", "disableMulticolor: true",
        "journey", "section ", "title ", ": 5: ", ": 1: Me, Cat", "# ", ";",
        "eventmodeling", "tf 01 ", "rf 02 ", "timeframe ", "resetframe ", "ui ", "cmd ", "evt ", "rmo ", "pcr ", "processor ", "readmodel ", " ->> 01", "[[D]]",
        "`json`", "{ a: 1 }", "data D {\n", "\n}\n", "entity ", "note 01 {\n", "gwt 01 given evt A when cmd B then evt C", "// ", "/* */", "Ns.Name",
        "ishikawa-beta", "ishikawa", "ISHIKAWA", "\n    ", "\n        ", "\n            ", "\n\t", "<br>", "<br/>", "<br />", "\n%% c\n", "diagramPadding: 8",
        "mindmap", "\n\t", "id[Text]", "id(Text)", "id((Text))", "id))Text((", "id)Text(", "id{{Text}}", "id(-Text-)", "::icon(fa fa-book)", ":::a b", "colorBy: depth", "padding: 5", "maxNodeWidth: 50",
        "treeView-beta", "├── ", "└── ", "│   ", "┣━━ ", "┗━━ ", "┃   ", "\t", "src/", "\"a b\"", "'a b'", " :::highlight", " icon(logos:react)", " icon()", " icon(none)", " ## ",
        "showIcons: true", "defaultIconPack: x", "filenameIcons:\n      a: b", "extensionIcons:\n      .ts: c", "rowIndent: 80", "paddingX: 2", "lineThickness: 3", "labelFontSize: '20px'", "highlightBg: red",
    ];

    public static IReadOnlyList<FuzzTarget> All { get; } =
    [
        new("mermaid-parse", ParseTarget) { Seeds = GetMermaidSeeds, Dictionary = Tokens },
        new("mermaid-round-trip", RoundTripTarget) { Seeds = GetMermaidSeeds, Dictionary = Tokens },
        new("mermaid-render", RenderTarget) { Seeds = GetMermaidSeeds, Dictionary = Tokens },
        new("diagrams-layered-layout", LayoutTarget) { Seeds = GetLayoutSeeds, Dictionary = ["\0", "\u0001", "\u0010", "ÿ"] },
    ];

    private static readonly MermaidParseOptions Options = new() { MaxEdges = 100, MaxNodes = 200, MaxTextSize = 20_000 };

    /// <summary><see cref="MermaidDocument.Parse(string, MermaidParseOptions?)"/> agrees with TryParse, and its errors are inside the text.</summary>
    private static void ParseTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        var succeeded = MermaidDocument.TryParse(text, Options, out var document, out var tryError);
        MermaidParseError? error = null;
        try
        {
            document = MermaidDocument.Parse(text, Options);
        }
        catch (MermaidParseException exception)
        {
            error = exception.Error;
        }

        FuzzAssert.Equal(succeeded, error is null, $"Parse and TryParse disagree on {FuzzAssert.Format(text)}");
        if (error is { } parseError)
        {
            FuzzAssert.Equal(parseError, tryError, "Parse and TryParse report different errors");
            var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
            FuzzAssert.True(parseError.Line >= 1 && parseError.Line <= lines.Length, $"The line of the error {parseError} is outside of the text");
            FuzzAssert.True(parseError.Column >= 1 && parseError.Column <= lines[parseError.Line - 1].Length + 1, $"The column of the error {parseError} is outside of its line");
        }
        else
        {
            FuzzAssert.True(document is not null, "Parse returned no document");
        }
    }

    /// <summary>A diagram that reads writes back as a text that reads, and writing it again gives the same text.</summary>
    private static void RoundTripTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (!MermaidDocument.TryParse(text, Options, out var document, out _))
            return;

        var written = MermaidWriter.Write(document.Diagram);
        if (!MermaidDocument.TryParse(written, Options, out var reparsed, out var error))
        {
            FuzzAssert.Fail($"The text written from {FuzzAssert.Format(text)} does not read back: {error}\n{written}");
            return;
        }

        var again = MermaidWriter.Write(reparsed.Diagram);
        FuzzAssert.Equal(written, again, $"Writing the diagram of {FuzzAssert.Format(text)} twice gives different texts");
    }

    /// <summary>Drawing a diagram gives finite coordinates, and a well-formed SVG that is the same each time.</summary>
    private static void RenderTarget(ReadOnlySpan<byte> data)
    {
        var text = FuzzText.Decode(data);
        if (!MermaidDocument.TryParse(text, Options, out var document, out _))
            return;

        var scene = document.Diagram.CreateScene();
        FuzzAssert.True(double.IsFinite(scene.Width) && double.IsFinite(scene.Height) && scene.Width > 0 && scene.Height > 0, $"The size of the scene is {scene.Width}x{scene.Height}");
        CheckFinite(scene.Root, "root");
        var svg = SvgSceneRenderer.Render(scene);
        FuzzAssert.Equal(svg, document.Diagram.ToSvg(new SvgRenderOptions { IdPrefix = "scene" }), "Drawing the diagram twice gives different outputs");
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
            case SceneText text:
                Check(text.X, path, "text x");
                Check(text.Y, path, "text y");
                break;
        }

        static void Check(double value, string path, string name) => FuzzAssert.True(double.IsFinite(value), $"The {name} of {path} is {value}");
    }

    /// <summary>
    /// The input is a list of nodes and edges: a byte selects what to add (a node, a cluster, an edge), and the next bytes
    /// give its size, its cluster or its ends. The layout keeps the nodes apart and inside their clusters.
    /// </summary>
    private static void LayoutTarget(ReadOnlySpan<byte> data)
    {
        var graph = new LayoutGraph { Direction = (LayoutDirection)(FuzzInput.ReadByte(ref data) % 4) };
        var nodes = new List<LayoutNode>();
        var clusters = new List<LayoutNode>();
        var count = 0;
        while (!data.IsEmpty && count++ < 120)
        {
            var kind = FuzzInput.ReadByte(ref data);
            var a = FuzzInput.ReadByte(ref data);
            var b = FuzzInput.ReadByte(ref data);
            switch (kind % 4)
            {
                case 0 or 1:
                    var node = graph.AddNode(1 + (a % 120), 1 + (b % 60));
                    if (clusters.Count > 0 && kind % 8 >= 4)
                    {
                        LayoutGraph.SetParent(node, clusters[b % clusters.Count]);
                    }

                    nodes.Add(node);
                    break;
                case 2:
                    if (clusters.Count >= 8)
                        break;

                    var cluster = graph.AddCluster();
                    cluster.TitleSpace = a % 40;
                    if (clusters.Count > 0 && kind % 8 >= 4)
                    {
                        LayoutGraph.SetParent(cluster, clusters[b % clusters.Count]);
                    }

                    clusters.Add(cluster);
                    break;
                default:
                    if (nodes.Count is 0)
                        break;

                    var edge = graph.AddEdge(nodes[a % nodes.Count], nodes[b % nodes.Count]);
                    edge.MinLength = 1 + (kind >> 6);
                    if ((kind & 0x10) != 0)
                    {
                        edge.LabelWidth = 1 + (a % 50);
                        edge.LabelHeight = 1 + (b % 20);
                    }

                    break;
            }
        }

        LayeredLayout.Run(graph);
        foreach (var node in graph.Nodes)
        {
            FuzzAssert.True(double.IsFinite(node.X) && double.IsFinite(node.Y) && double.IsFinite(node.Width) && double.IsFinite(node.Height), $"The node {node} is not finite");
        }

        for (var i = 0; i < nodes.Count; i++)
        {
            for (var j = i + 1; j < nodes.Count; j++)
            {
                var first = nodes[i];
                var second = nodes[j];
                var overlapX = Math.Abs(first.X - second.X) < ((first.Width + second.Width) / 2) - 1e-6;
                var overlapY = Math.Abs(first.Y - second.Y) < ((first.Height + second.Height) / 2) - 1e-6;
                FuzzAssert.False(overlapX && overlapY, $"The node {first} overlaps the node {second}");
            }
        }

        foreach (var node in nodes)
        {
            if (node.Parent is not { } parent || parent.Width is 0)
                continue;

            var inside = node.X - (node.Width / 2) >= parent.X - (parent.Width / 2) - 1e-6
                && node.X + (node.Width / 2) <= parent.X + (parent.Width / 2) + 1e-6
                && node.Y - (node.Height / 2) >= parent.Y - (parent.Height / 2) - 1e-6
                && node.Y + (node.Height / 2) <= parent.Y + (parent.Height / 2) + 1e-6;
            FuzzAssert.True(inside, $"The node {node} is outside of its cluster {parent}");
        }

        foreach (var edge in graph.Edges)
        {
            foreach (var point in edge.Points)
            {
                FuzzAssert.True(double.IsFinite(point.X) && double.IsFinite(point.Y), "An edge has a point that is not finite");
            }
        }
    }

    private static IEnumerable<FuzzSeed> GetMermaidSeeds()
    {
        var assembly = typeof(DiagramsFuzzTargets).Assembly;
        foreach (var name in assembly.GetManifestResourceNames().Where(name => name.EndsWith(".mmd", StringComparison.Ordinal)).Order(StringComparer.Ordinal))
        {
            yield return FuzzSeed.FromText("corpus-" + name.Replace('/', '-').Replace(".mmd", string.Empty, StringComparison.Ordinal), FuzzResources.ReadText(assembly, name));
        }

        foreach (var file in new[] { "FlowchartParserTests.cs", "MermaidDocumentTests.cs", "ClassDiagramParserTests.cs", "StateDiagramParserTests.cs", "ErDiagramParserTests.cs", "RequirementDiagramParserTests.cs", "UseCaseDiagramParserTests.cs", "C4DiagramParserTests.cs", "BlockDiagramParserTests.cs", "ArchitectureDiagramParserTests.cs", "RailroadDiagramParserTests.cs", "SequenceDiagramParserTests.cs", "GitGraphDiagramParserTests.cs", "KanbanDiagramParserTests.cs", "PacketDiagramParserTests.cs", "GanttDiagramParserTests.cs", "TimelineDiagramParserTests.cs", "UserJourneyDiagramParserTests.cs", "EventModelingDiagramParserTests.cs", "IshikawaDiagramParserTests.cs", "MindmapDiagramParserTests.cs", "TreeViewDiagramParserTests.cs" })
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

    private static IEnumerable<FuzzSeed> GetLayoutSeeds()
    {
        byte[][] seeds =
        [
            [0, 0, 40, 20, 0, 60, 20, 3, 0, 1],
            [2, 2, 10, 0, 6, 30, 0, 4, 20, 0, 0, 30, 30, 3, 0, 1, 19, 1, 2, 3, 1, 1],
            [1, 0, 1, 1, 0, 1, 1, 3, 0, 0, 3, 0, 1, 3, 1, 0],
        ];
        for (var i = 0; i < seeds.Length; i++)
        {
            yield return new FuzzSeed("layout-" + i.ToString(CultureInfo.InvariantCulture), seeds[i]);
        }
    }

    /// <summary>Removes the indentation of the closing line of a raw string literal from its lines.</summary>
    private static string Dedent(string value)
    {
        var lines = value.Split('\n');
        var indentation = lines[^1].Length - lines[^1].TrimStart().Length;
        var builder = new StringBuilder();
        for (var i = 1; i < lines.Length - 1; i++)
        {
            var line = lines[i];
            builder.Append(line.Length >= indentation ? line[indentation..] : line.TrimStart()).Append('\n');
        }

        return builder.ToString();
    }

    // A raw string literal of three quotes, as the Mermaid texts of the tests
    [GeneratedRegex("\"\"\"(?<value>.*?)\"\"\"", RegexOptions.Singleline | RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 10_000)]
    private static partial Regex RawStringLiteral();
}
