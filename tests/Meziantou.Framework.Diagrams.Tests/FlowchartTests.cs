using System.Xml.Linq;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.VectorGraphics;

namespace Meziantou.Framework.Diagrams.Tests;

public sealed class FlowchartTests
{
    [Fact]
    public void Flowchart()
    {
        #region Diagrams_Flowchart
        var flowchart = new FlowchartDiagram { Title = "Order processing" };
        var start = flowchart.AddNode("start", "Order received", FlowchartNodeShape.Stadium);
        var check = flowchart.AddNode("check", "Is the payment valid?", FlowchartNodeShape.Diamond);
        var ship = flowchart.AddNode("ship", "Ship the order");
        var cancel = flowchart.AddNode("cancel", "Cancel the order");

        flowchart.AddEdge(start, check);
        flowchart.AddEdge(check, ship, "yes");
        flowchart.AddEdge(check, cancel, "no");

        var svg = flowchart.ToSvg();
        #endregion

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\"", svg, StringComparison.Ordinal);
        Assert.Contains(">Order received</text>", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg, "Flowchart");
    }

    [Fact]
    public void Subgraphs()
    {
        #region Diagrams_FlowchartSubgraphs
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        var backend = flowchart.AddSubgraph("backend", "Backend");
        var api = new FlowchartNode("api", "API");
        var database = new FlowchartNode("db", "Orders", FlowchartNodeShape.Cylinder);
        backend.Nodes.Add(api);
        backend.Nodes.Add(database);

        var client = flowchart.AddNode("client", "Web app");
        flowchart.AddEdge(client, api, "HTTPS");
        flowchart.AddEdge(api, database);
        #endregion

        DiagramAssert.Svg(flowchart, "FlowchartSubgraphs");
    }

    [Fact]
    public void Edges()
    {
        #region Diagrams_FlowchartEdges
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        flowchart.AddEdge("a", "b", "solid");
        flowchart.AddEdge("b", "c", "dotted").LineStyle = FlowchartLineStyle.Dotted;
        flowchart.AddEdge("c", "d", "thick").LineStyle = FlowchartLineStyle.Thick;

        var both = flowchart.AddEdge("d", "e", "both ends");
        both.StartArrowHead = FlowchartArrowHead.Circle;
        both.EndArrowHead = FlowchartArrowHead.Circle;

        flowchart.AddEdge("e", "f").EndArrowHead = FlowchartArrowHead.Cross;
        flowchart.AddEdge("f", "g", "longer").Length = 2;
        #endregion

        DiagramAssert.Svg(flowchart, "FlowchartEdges");
    }

    [Fact]
    public void Swimlanes()
    {
        #region Diagrams_Swimlanes
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight, Swimlanes = true };
        var customer = flowchart.AddSubgraph("customer", "Customer");
        var support = flowchart.AddSubgraph("support", "Support");
        var request = new FlowchartNode("request", "Open a ticket");
        var triage = new FlowchartNode("triage", "Triage");
        var answer = new FlowchartNode("answer", "Answer");
        customer.Nodes.Add(request);
        support.Nodes.Add(triage);
        support.Nodes.Add(answer);

        flowchart.AddEdge(request, triage);
        flowchart.AddEdge(triage, answer, "known issue");
        flowchart.AddEdge(answer, request);
        #endregion

        DiagramAssert.Svg(flowchart, "Swimlanes");
    }

    [Fact]
    public void SwimlanesTopToBottom()
    {
        var flowchart = new FlowchartDiagram { Swimlanes = true };
        var first = flowchart.AddSubgraph("first", "First lane");
        var second = flowchart.AddSubgraph("second", "Second lane");
        var nested = new FlowchartSubgraph("nested", "Nested");
        second.Subgraphs.Add(nested);
        first.Nodes.Add(new FlowchartNode("a"));
        nested.Nodes.Add(new FlowchartNode("b"));
        flowchart.AddEdge("a", "b");
        flowchart.AddEdge("b", "b");
        flowchart.AddEdge("outside", "a");
        flowchart.AddEdge(flowchart.Find("a")!, second);
        DiagramAssert.Svg(flowchart);
    }

    [Fact]
    public void Shapes()
    {
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        FlowchartNode? previous = null;
        var index = 0;
        foreach (var shape in Enum.GetValues<FlowchartNodeShape>())
        {
            var node = flowchart.AddNode("n" + index.ToString(System.Globalization.CultureInfo.InvariantCulture), shape.ToString(), shape);
            if (previous is not null && index % 5 != 0)
            {
                flowchart.AddEdge(previous, node);
            }

            previous = node;
            index++;
        }

        DiagramAssert.Svg(flowchart, "FlowchartShapes");
    }

    [Fact]
    public void Styles()
    {
        #region Diagrams_FlowchartStyles
        var flowchart = new FlowchartDiagram();
        flowchart.ClassDefinitions["important"] = new DiagramStyle { Fill = (SceneColor)"#fde68a", Stroke = (SceneColor)"#b45309", StrokeWidth = 2 };

        var start = flowchart.AddNode("start", "Start");
        var alert = flowchart.AddNode("alert", "Page the on-call engineer");
        alert.CssClasses.Add("important");
        var done = flowchart.AddNode("done", "Done");
        done.Style = new DiagramStyle { Fill = (SceneColor)"#bbf7d0", TextColor = (SceneColor)"#14532d", Bold = true };

        flowchart.AddEdge(start, alert).Style = new DiagramStyle { Stroke = (SceneColor)"#dc2626", StrokeWidth = 2 };
        flowchart.AddEdge(alert, done);
        #endregion

        var svg = flowchart.ToSvg();
        Assert.Contains("fill=\"#fde68a\"", svg, StringComparison.Ordinal);
        Assert.Contains("class=\"node important\"", svg, StringComparison.Ordinal);
        Assert.Contains("stroke=\"#dc2626\"", svg, StringComparison.Ordinal);
        DiagramAssert.Svg(svg, "FlowchartStyles");
    }

    [Fact]
    public void Themes()
    {
        #region Diagrams_Themes
        var flowchart = new FlowchartDiagram { Theme = DiagramTheme.Dark, Direction = DiagramDirection.LeftToRight };
        flowchart.AddEdge("build", "test");
        flowchart.AddEdge("test", "deploy");

        // A theme is a record: change some of its colors with "with"
        var custom = new FlowchartDiagram { Theme = DiagramTheme.Default with { NodeBackground = (SceneColor)"#e0f2fe", NodeBorderColor = (SceneColor)"#0369a1" } };
        custom.AddEdge("build", "test");
        #endregion

        Assert.Contains("fill=\"#333333\"", flowchart.ToSvg(), StringComparison.Ordinal);
        Assert.Contains("fill=\"#e0f2fe\"", custom.ToSvg(), StringComparison.Ordinal);
        DiagramAssert.Svg(flowchart, "FlowchartDarkTheme");
    }

    [Fact]
    public void ThemePresets()
    {
        foreach (var theme in new[] { DiagramTheme.Default, DiagramTheme.Dark, DiagramTheme.Forest, DiagramTheme.Neutral, DiagramTheme.Base })
        {
            Assert.Equal(12, theme.Palette.Count);
            Assert.Equal(theme.Palette[1], theme.GetPaletteColor(13));
            Assert.Equal(theme.Palette[11], theme.GetPaletteColor(-1));
        }

        Assert.Equal((SceneColor)"#ececff", DiagramTheme.Default.NodeBackground);
        Assert.Equal((SceneColor)"#9370db", DiagramTheme.Default.NodeBorderColor);
        Assert.True(DiagramTheme.Dark.IsDark);
        Assert.Equal(DiagramTheme.Default.PrimaryColor, (DiagramTheme.Default with { Palette = [] }).GetPaletteColor(3));
    }

    [Fact]
    public void MarkdownLabelsAndWrapping()
    {
        var flowchart = new FlowchartDiagram { WrappingWidth = 100 };
        var node = flowchart.AddNode("a", DiagramText.Markdown("A **bold** and *italic* label that wraps"));
        var plain = flowchart.AddNode("b", "first line\nsecond line");
        flowchart.AddEdge(node, plain);
        var document = XDocument.Parse(flowchart.ToSvg());
        var texts = document.Descendants().Where(element => element.Name.LocalName == "text").ToList();
        Assert.Contains(texts, text => text.Value == "bold" && (string?)text.Attribute("font-weight") == "bold");
        Assert.Contains(texts, text => text.Value == "italic" && (string?)text.Attribute("font-style") == "italic");
        Assert.Contains(texts, text => text.Value == "first line");
        Assert.Contains(texts, text => text.Value == "second line");

        // Each line of the wrapped label fits the wrapping width
        Assert.Contains(texts, text => text.Value.Contains("wraps", StringComparison.Ordinal));
        DiagramAssert.Svg(document.ToString(SaveOptions.DisableFormatting) + "\n");
    }

    [Fact]
    public void LinksTooltipsAndDataAttributes()
    {
        var flowchart = new FlowchartDiagram();
        var node = flowchart.AddNode("docs", "Documentation");
        node.Link = "https://example.com/docs";
        node.Tooltip = "Open the documentation";
        var svg = flowchart.ToSvg();
        Assert.Contains("<a href=\"https://example.com/docs\">", svg, StringComparison.Ordinal);
        Assert.Contains("<title>Open the documentation</title>", svg, StringComparison.Ordinal);
        Assert.Contains("data-id=\"docs\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SubgraphDirection()
    {
        // A subgraph with its own direction and no edge across its border is laid out on its own
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        var inner = flowchart.AddSubgraph("inner", "Inner");
        inner.Direction = DiagramDirection.TopToBottom;
        var top = new FlowchartNode("top");
        var bottom = new FlowchartNode("bottom");
        inner.Nodes.Add(top);
        inner.Nodes.Add(bottom);
        flowchart.AddEdge(top, bottom);
        flowchart.AddEdge(flowchart.AddNode("outside"), inner);

        var scene = flowchart.CreateScene();
        var positions = FindTexts(scene).ToDictionary(text => text.Text, text => (text.X, text.Y), StringComparer.Ordinal);
        Assert.True(positions["bottom"].Y > positions["top"].Y + 10);
        Assert.Equal(positions["top"].X, positions["bottom"].X, 1e-6);
        Assert.True(positions["top"].X > positions["outside"].X);
        DiagramAssert.Svg(flowchart);
    }

    [Fact]
    public void EmptyDiagram()
    {
        var flowchart = new FlowchartDiagram();
        var scene = flowchart.CreateScene();
        Assert.Equal(16, scene.Width);
        Assert.Equal(16, scene.Height);
        DiagramAssert.Svg(flowchart);
    }

    [Fact]
    public void FindAndAddEdgeById()
    {
        var flowchart = new FlowchartDiagram();
        var subgraph = flowchart.AddSubgraph("group");
        var nested = new FlowchartSubgraph("nested");
        subgraph.Subgraphs.Add(nested);
        nested.Nodes.Add(new FlowchartNode("deep"));
        var edge = flowchart.AddEdge("a", "deep");
        Assert.Same(flowchart.Nodes[0], edge.Source);
        Assert.Same(nested.Nodes[0], edge.Target);
        Assert.Same(nested, flowchart.Find("nested"));
        Assert.Null(flowchart.Find("missing"));
        Assert.Equal(["group", "nested"], flowchart.EnumerateSubgraphs().Select(item => item.Id));
        Assert.Equal("deep", edge.Target.ToString());
    }

    [Fact]
    public void InvalidArguments()
    {
        var flowchart = new FlowchartDiagram();
        Assert.Throws<ArgumentOutOfRangeException>("value", () => flowchart.NodeSpacing = -1);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => flowchart.RankSpacing = double.NaN);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => flowchart.WrappingWidth = 0);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => flowchart.Padding = -1);
        Assert.Throws<ArgumentNullException>("value", () => flowchart.Theme = null!);
        Assert.Throws<ArgumentOutOfRangeException>("value", () => flowchart.AddEdge("a", "b").Length = 0);
        Assert.Throws<ArgumentException>("id", () => new FlowchartNode(string.Empty));
        Assert.Throws<ArgumentNullException>("text", () => new DiagramText(null!));
        Assert.Throws<ArgumentOutOfRangeException>("format", () => new DiagramText("a", (DiagramTextFormat)42));
    }

    [Fact]
    public void DiagramTextEquality()
    {
        DiagramText text = "label";
        Assert.Equal(new DiagramText("label"), text);
        Assert.NotEqual(DiagramText.Markdown("label"), text);
        Assert.Equal(text.GetHashCode(), new DiagramText("label").GetHashCode());
        Assert.Null((DiagramText?)(string?)null);
        Assert.Equal("label", text.ToString());
        Assert.False(text.Equals(null));
    }

    [Fact]
    public void StylesMerge()
    {
        var first = new DiagramStyle { Fill = SceneColor.White, StrokeWidth = 2 };
        var second = new DiagramStyle { Fill = SceneColor.Black };
        var merged = first.Merge(second);
        Assert.Equal(SceneColor.Black, merged.Fill);
        Assert.Equal(2, merged.StrokeWidth);
        Assert.Same(first, first.Merge(null));
    }

    [Fact]
    public void ToImage()
    {
        #region Diagrams_ToImage
        var flowchart = new FlowchartDiagram { Direction = DiagramDirection.LeftToRight };
        flowchart.AddEdge("draft", "review");
        flowchart.AddEdge("review", "publish");

        // Meziantou.Framework.Imaging.VectorGraphics draws any diagram: image.Save("flow.png")
        using var image = flowchart.ToImage(new SceneImageOptions { Scale = 2 });
        #endregion

        var scene = flowchart.CreateScene(SceneImageRenderer.CreateMeasurer());
        Assert.Equal((int)Math.Ceiling(scene.Width * 2), image.Width);
        Assert.Equal(Colors.White.R, image.Frames[0][1, 1].R);
    }

    [Fact]
    public void DrawOnCanvas()
    {
        #region Diagrams_DrawOnCanvas
        var flowchart = new FlowchartDiagram();
        flowchart.AddEdge("start", "end");

        // The size depends on the texts: measure the diagram to place it
        var size = SceneImageRenderer.MeasureScene(flowchart);
        using var report = new Image<Rgba32>(400, 400, Colors.White); // or Image.Load<Rgba32>("report.png")
        var position = new PointD(report.Width - size.Width - 10, 10);
        report.Draw(canvas => canvas.DrawScene(flowchart, position));
        #endregion

        var drawn = default(RectangleD);
        using (var other = new Image<Rgba32>(400, 400, Colors.White))
        {
            other.Draw(canvas => drawn = canvas.DrawScene(flowchart, position));
        }

        Assert.Equal(new RectangleD(position.X, position.Y, size.Width, size.Height), drawn);
        var scene = flowchart.CreateScene(SceneImageRenderer.CreateMeasurer());
        Assert.Equal((scene.Width, scene.Height), (size.Width, size.Height));
        Assert.Equal(new RectangleD(0, 0, size.Width * 2, size.Height * 2), SceneImageRenderer.MeasureScene(flowchart, new SceneImageOptions { Scale = 2 }));
    }

    [Fact]
    public void WriteSvgToStream()
    {
        var flowchart = new FlowchartDiagram();
        flowchart.AddNode("a");
        using var stream = new MemoryStream();
        flowchart.WriteSvg(stream);
        Assert.Equal(flowchart.ToSvg(), System.Text.Encoding.UTF8.GetString(stream.ToArray()));
        Assert.Equal(flowchart.ToSvg(), flowchart.ToSvg());
    }

    internal static IEnumerable<(string Text, double X, double Y)> FindTexts(Scene scene)
    {
        var stack = new Stack<(SceneElement Element, double X, double Y)>();
        stack.Push((scene.Root, 0, 0));
        while (stack.Count > 0)
        {
            var (element, x, y) = stack.Pop();
            switch (element)
            {
                case SceneGroup group:
                    var (dx, dy) = group.Transform is { } transform ? (transform.E, transform.F) : (0, 0);
                    foreach (var child in group.Children)
                    {
                        stack.Push((child, x + dx, y + dy));
                    }

                    break;
                case SceneText text:
                    yield return (text.Text, x + text.X, y + text.Y);
                    break;
            }
        }
    }

    [Fact]
    public void ThemeFontFamilyIsMeasuredAndDrawnWithTheFonts()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");
        var fonts = SceneFontCollection.Load(Path.Combine(testData, "LiberationSans-Regular.woff2"), Path.Combine(testData, "LiberationMono-Regular.woff2"));
        var measurer = new FontFaceTextMeasurer(fonts);

        var sans = new FlowchartDiagram();
        sans.AddNode("a", "iiiiiiiiiiiiiiii");
        var mono = new FlowchartDiagram { Theme = DiagramTheme.Default with { FontFamily = "Liberation Mono" } };
        mono.AddNode("a", "iiiiiiiiiiiiiiii");

        // The node is laid out with the family of the theme, which the scene draws with its fonts
        var sansScene = sans.CreateScene(measurer);
        var monoScene = mono.CreateScene(measurer);
        Assert.Same(fonts, monoScene.Fonts);
        Assert.Equal("Liberation Mono", monoScene.FontFamily);
        Assert.Equal(fonts.FontFamily, sansScene.FontFamily);
        Assert.True(monoScene.Width > sansScene.Width + 50);

        // The image draws the node with Liberation Mono: its text is as wide as it was measured
        using var image = mono.ToImage(new SceneImageOptions { Fonts = fonts });
        Assert.Equal((int)Math.Ceiling(monoScene.Width), image.Width);
    }
}
