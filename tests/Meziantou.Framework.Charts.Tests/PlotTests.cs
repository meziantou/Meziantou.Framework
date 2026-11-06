namespace Meziantou.Framework.Charts.Tests;

public sealed class PlotTests
{
    [Fact]
    public void Plot_ValidatesItsArguments()
    {
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new Plot { Width = 0 });
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new Plot { Height = double.NaN });
        Assert.Throws<ArgumentNullException>("mark", () => new Plot().Add(null!));
        Assert.Throws<ArgumentNullException>("stream", () => new Plot().WriteSvg((Stream)null!));
        Assert.Throws<ArgumentOutOfRangeException>("value", () => new SvgRenderOptions { Precision = 16 });
        Assert.Throws<ArgumentNullException>("scene", () => SvgChartRenderer.Render(null!));
        Assert.Throws<ArgumentNullException>("writer", () => SvgChartRenderer.Render(new ChartScene(1, 1, "a"), (TextWriter)null!));
        Assert.Throws<ArgumentNullException>("data", () => new BarMark<int>(null!, x: i => i, y: i => i));
        Assert.Throws<ArgumentNullException>("x", () => new BarMark<int>([], x: null!, y: i => i));
        Assert.Throws<ArgumentNullException>("root", () => new PackMark<int>(null!));
        Assert.Throws<ArgumentNullException>("data", () => new ScenePath(null!));
    }

    [Fact]
    public void Plot_AddReturnsThePlotAndKeepsTheOrder()
    {
        var first = new RuleYMark(1);
        var second = new RuleXMark(2);
        var plot = new Plot().Add(first).Add(second);
        Assert.Equal([first, second], plot.Marks);
        Assert.Equal(640, plot.Width);
        Assert.Equal(400, plot.Height);
    }

    [Fact]
    public void Scene_IsRendererNeutral()
    {
        #region Charts_CreateScene
        var plot = new Plot { Title = "Sales" };
        plot.Add(new BarMark<Sale>(SampleData.Sales.Where(sale => sale.Year == 2024), x: sale => sale.Region, y: sale => sale.Amount));

        // The layout, as drawing primitives: rectangles, paths, lines, texts and groups, in pixels
        var scene = plot.CreateScene();
        var bars = scene.Root.Children.OfType<SceneGroup>().Single(group => group.CssClass == "marks");
        Assert.HasCount(4, bars.Children.OfType<SceneGroup>().Single().Children.OfType<SceneRectangle>());
        #endregion

        Assert.Equal("Sales", scene.Title);
        Assert.Equal(ChartColor.White, scene.Background);
        Assert.Equal(SvgChartRenderer.Render(scene), plot.ToSvg());
    }

    [Fact]
    public void Svg_EscapesTextAndRemovesInvalidCharacters()
    {
        var scene = new ChartScene(100, 50, "\"Font\" & <co>") { Title = "a < b & \"c\"", Description = "d\u0001e\uD800f😀" };
        scene.Root.Add(new SceneText(0, 10, "x > y\u0000", new ChartFont(10), ChartColor.Black) { Tooltip = "tip & <tip>" });
        var svg = SvgChartRenderer.Render(scene);
        Assert.Contains("font-family=\"&quot;Font&quot; &amp; &lt;co&gt;\"", svg);
        Assert.Contains("<title>a &lt; b &amp; &quot;c&quot;</title>", svg);
        Assert.Contains("<desc>def😀</desc>", svg);
        Assert.Contains("<title>tip &amp; &lt;tip&gt;</title>x &gt; y</text>", svg);
        _ = System.Xml.Linq.XDocument.Parse(svg);
    }

    [Fact]
    public void Svg_WritesEveryPrimitive()
    {
        var scene = new ChartScene(100, 100, "sans-serif");
        var gradient = new LinearGradientPaint(0, 0, 100, 0, [new SceneGradientStop(0, ChartColor.Black), new SceneGradientStop(1, ChartColor.White.WithOpacity(0.5))]);
        var group = scene.Root.Add(new SceneGroup { Clip = new ChartRectangle(0, 0, 50, 50), Transform = SceneTransform.Rotate(45), Opacity = 0.5, CssClass = "g", Tooltip = "group" });
        group.Add(new SceneRectangle(0, 0, 10, 10) { Fill = gradient, Stroke = gradient, StrokeWidth = 2, StrokeDashArray = [1, 2], LineCap = SceneLineCap.Square, LineJoin = SceneLineJoin.Bevel, CornerRadius = 2 });
        group.Add(new SceneCircle(5, 5, 5) { Fill = ChartColor.Black.WithOpacity(0.25), Stroke = (ChartColor)"#ff0000", LineCap = SceneLineCap.Round, LineJoin = SceneLineJoin.Round, Tooltip = "circle" });
        group.Add(new SceneLine(0, 0, 1, 1) { Stroke = (ChartColor)"#00ff00" });
        group.Add(new ScenePath(new PathBuilder().AddCircle(5, 5, 2).Build()) { FillRule = SceneFillRule.EvenOdd, Fill = (ChartColor)"#0000ff" });
        scene.Root.Add(new SceneGroup { Clip = new ChartRectangle(1, 2, 3, 4) });
        scene.Root.Add(new SceneGroup { Transform = SceneTransform.Translate(1, 2) });
        scene.Root.Add(new SceneText(10, 10, "rotated", new ChartFont(10, Bold: true), ChartColor.Black) { Rotation = 30, Anchor = SceneTextAnchor.End, Opacity = 0.5, CssClass = "label" });
        scene.Root.Add(new SceneText(10, 10, "middle", new ChartFont(10), ChartColor.Black) { Anchor = SceneTextAnchor.Middle });

        var svg = SvgChartRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "p", Precision = 3 });
        Assert.Contains("<linearGradient id=\"p-gradient0\" gradientUnits=\"userSpaceOnUse\" x1=\"0\" y1=\"0\" x2=\"100\" y2=\"0\">", svg);
        Assert.Contains("<stop offset=\"1\" stop-color=\"#ffffff\" stop-opacity=\"0.502\"/>", svg);
        Assert.Contains("<g clip-path=\"url(#p-clip0)\">\n<g opacity=\"0.5\" class=\"g\" transform=\"matrix(0.707107 0.707107 -0.707107 0.707107 0 0)\">\n<title>group</title>\n", svg);
        Assert.Contains("<g clip-path=\"url(#p-clip1)\">\n</g>", svg);
        Assert.Contains("<g transform=\"translate(1 2)\">", svg);
        Assert.Contains("fill=\"url(#p-gradient0)\" stroke=\"url(#p-gradient0)\" stroke-width=\"2\" stroke-dasharray=\"1 2\" stroke-linecap=\"square\" stroke-linejoin=\"bevel\"", svg);
        Assert.Contains("<circle cx=\"5\" cy=\"5\" r=\"5\" fill=\"#000000\" fill-opacity=\"0.251\" stroke=\"#ff0000\" stroke-linecap=\"round\" stroke-linejoin=\"round\"><title>circle</title></circle>", svg);
        Assert.Contains("<line x1=\"0\" y1=\"0\" x2=\"1\" y2=\"1\" stroke=\"#00ff00\"/>", svg);
        Assert.Contains("fill-rule=\"evenodd\"", svg);
        Assert.Contains("text-anchor=\"end\" font-size=\"10\" font-weight=\"bold\" fill=\"#000000\" transform=\"rotate(30 10 10)\" opacity=\"0.5\" class=\"label\"", svg);
        Assert.Contains("text-anchor=\"middle\"", svg);
        Assert.DoesNotContain("<rect width=\"100\"", svg);
        _ = System.Xml.Linq.XDocument.Parse(svg);
    }

    [Fact]
    public void Hierarchy_Stratify()
    {
        #region Charts_HierarchyStratify
        var rows = new[]
        {
            (Id: "root", Parent: (string?)null, Size: 0.0),
            (Id: "a", Parent: "root", Size: 0.0),
            (Id: "a1", Parent: "a", Size: 3.0),
            (Id: "a2", Parent: "a", Size: 5.0),
            (Id: "b", Parent: "root", Size: 4.0),
        };
        var root = Hierarchy.Stratify(rows, row => row.Id, row => row.Parent, row => row.Size);
        #endregion

        Assert.Equal(12, root.Value);
        Assert.Equal(2, root.Height);
        Assert.Equal(["root", "a", "a1", "a2", "b"], root.Descendants().Select(node => node.Name).ToArray());
        Assert.Equal(["a1", "a2", "b"], root.Leaves().Select(node => node.Name).ToArray());
        var a1 = root.Descendants().Single(node => node.Name == "a1");
        Assert.Equal(["a1", "a", "root"], a1.Ancestors().Select(node => node.Name).ToArray());
        Assert.Equal("root/a/a1", a1.Path);
        Assert.Equal("root/a/a1", a1.ToString());
        Assert.Equal(2, a1.Depth);
        Assert.Equal(3, root.Count().Value);
        Assert.Equal(["b", "a"], root.Sort((x, y) => string.CompareOrdinal(y.Name, x.Name)).Children.Select(node => node.Name).ToArray());
        Assert.Equal(-1, root.Sum(_ => -1).Value is 0 ? -1 : 0);
    }

    [Fact]
    public void Hierarchy_StratifyRejectsInvalidTrees()
    {
        Assert.Throws<ArgumentException>("rows", () => Hierarchy.Stratify(["a", "b"], id => id, _ => "x"));
        Assert.Throws<ArgumentException>("rows", () => Hierarchy.Stratify(["a", "b"], id => id, _ => null));
        Assert.Throws<ArgumentException>("rows", () => Hierarchy.Stratify(["a", "a"], id => id, id => id is "a" ? null : "a"));
        Assert.Throws<ArgumentException>("rows", () => Hierarchy.Stratify(["root", "a"], id => id, id => id is "root" ? null : "missing"));
        Assert.Throws<ArgumentException>("rows", () => Hierarchy.Stratify(["root", "a", "b"], id => id, id => id switch { "root" => null, "a" => "b", _ => "a" }));
        Assert.Throws<ArgumentException>("children", () => Hierarchy.FromTree(0, i => [i + 1]));
    }

    [Fact]
    public void Hierarchy_FromPaths()
    {
        var root = Hierarchy.FromPaths(["a/b", "a/c", "/a//d/", "e"], path => path, value: _ => 1, separator: '/');
        Assert.Equal(string.Empty, root.Name);
        Assert.Null(root.Data);
        Assert.Equal(4, root.Value);
        Assert.Equal(["a", "e"], root.Children.Select(node => node.Name).ToArray());
        Assert.Equal("e", root.Children[1].Data);
        Assert.Null(root.Children[0].Data);

        var unvalued = Hierarchy.FromPaths(["x.y"], path => path, separator: '.');
        Assert.Equal(0, unvalued.Value);
        Assert.Equal("/x/y", unvalued.Leaves().Single().Path);

        var tree = Hierarchy.FromTree("root", item => item is "root" ? ["child"] : null);
        Assert.Equal("child", tree.Children.Single().Name);
    }
}
