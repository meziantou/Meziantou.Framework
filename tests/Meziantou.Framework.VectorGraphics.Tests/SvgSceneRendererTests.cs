namespace Meziantou.Framework.VectorGraphics.Tests;

public sealed class SvgSceneRendererTests
{
    [Fact]
    public void CreateAScene()
    {
        #region VectorGraphics_CreateScene
        var scene = new Scene(width: 200, height: 80, fontFamily: AfmTextMeasurer.Default.FontFamily) { Title = "A box", Background = SceneColor.White };

        // Shapes are filled and stroked; texts are positioned by their baseline
        scene.Root.Add(new SceneRectangle(10, 10, 180, 60) { Fill = (SceneColor)"#e0f2fe", Stroke = (SceneColor)"#0369a1", StrokeWidth = 2, CornerRadius = 8 });
        scene.Root.Add(new SceneText(100, 45, "Hello", new SceneFont(16, Bold: true), (SceneColor)"#0c4a6e") { Anchor = SceneTextAnchor.Middle });

        // Paths use the commands of SVG
        var arrow = new PathBuilder().MoveTo(20, 60).LineTo(60, 60).LineTo(55, 55).MoveTo(60, 60).LineTo(55, 65).Build();
        scene.Root.Add(new ScenePath(arrow) { Stroke = (SceneColor)"#0369a1" });

        var svg = SvgSceneRenderer.Render(scene);
        #endregion

        Assert.StartsWith("<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"200\" height=\"80\"", svg, StringComparison.Ordinal);
        Assert.Contains("<title>A box</title>", svg, StringComparison.Ordinal);
        Assert.Contains(">Hello</text>", svg, StringComparison.Ordinal);
        Assert.Contains("d=\"M20,60 L60,60 L55,55 M60,60 L55,65\"", svg, StringComparison.Ordinal);
    }

    [Fact]
    public void SvgNumbers()
    {
        Assert.Equal("0", SvgNumber.Format(-0.001, 2));
        Assert.Equal("1.13", SvgNumber.Format(1.125, 2));
        Assert.Equal("1", SvgNumber.Format(1.005, 2)); // 1.005 is 1.00499999999999989 in binary
        Assert.Equal("-1.5", SvgNumber.Format(-1.5, 2));
        Assert.Equal("2", SvgNumber.Format(1.6, 0));
        Assert.Equal("0", SvgNumber.Format(double.NaN, 2));
        var path = new PathBuilder().MoveTo(0, 0).LineTo(1, 1).CubicTo(1, 2, 3, 4, 5, 6).QuadraticTo(7, 8, 9, 10).ArcTo(5, 5, 30, largeArc: true, sweep: false, 11, 12).Close().Build();
        Assert.Equal("M0,0 L1,1 C1,2 3,4 5,6 Q7,8 9,10 A5,5 30 1,0 11,12 Z", path.ToString());
        Assert.Equal(6, path.Commands.Count);
        Assert.Equal("M0,0 L1,0 L1,1 L0,1 Z", new PathBuilder().AddRectangle(0, 0, 1, 1).Build().ToString());
    }

    [Fact]
    public void PathShapes()
    {
        #region VectorGraphics_PathShapes
        // A triangle, an ellipse and a rounded rectangle in a single path
        var path = new PathBuilder()
            .AddPolygon((0, 0), (10, 0), (5, 8))
            .AddEllipse(centerX: 30, centerY: 5, radiusX: 10, radiusY: 5)
            .AddRoundedRectangle(x: 50, y: 0, width: 20, height: 10, radius: 2)
            .Build();

        // Paths can be moved, and combined into a new path
        var moved = path.Translate(offsetX: 0, offsetY: 20);
        var both = new PathBuilder().Append(path).Append(moved).Build();
        Assert.HasCount(2 * path.Commands.Count, both.Commands);
        #endregion

        Assert.Equal(
            "M0,0 L10,0 L5,8 Z M40,5 C40,7.76 35.52,10 30,10 C24.48,10 20,7.76 20,5 C20,2.24 24.48,0 30,0 C35.52,0 40,2.24 40,5 Z " +
            "M52,0 L68,0 C69.1,0 70,0.9 70,2 L70,8 C70,9.1 69.1,10 68,10 L52,10 C50.9,10 50,9.1 50,8 L50,2 C50,0.9 50.9,0 52,0 Z",
            path.ToString());
        Assert.Equal(
            "M0,20 L10,20 L5,28 Z M40,25 C40,27.76 35.52,30 30,30 C24.48,30 20,27.76 20,25 C20,22.24 24.48,20 30,20 C35.52,20 40,22.24 40,25 Z " +
            "M52,20 L68,20 C69.1,20 70,20.9 70,22 L70,28 C70,29.1 69.1,30 68,30 L52,30 C50.9,30 50,29.1 50,28 L50,22 C50,20.9 50.9,20 52,20 Z",
            moved.ToString());
    }

    [Fact]
    public void PathShapesEdgeCases()
    {
        Assert.True(new PathBuilder().AddPolygon().IsEmpty);
        Assert.Equal("M1,2 Z", new PathBuilder().AddPolygon((1, 2)).Build().ToString());

        // The radius is limited to half the smaller side, and no radius gives a plain rectangle
        Assert.Equal("M0,0 L10,0 L10,4 L0,4 Z", new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 0).Build().ToString());
        Assert.Equal("M0,0 L10,0 L10,4 L0,4 Z", new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, -3).Build().ToString());
        Assert.Equal(new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 2).Build().ToString(), new PathBuilder().AddRoundedRectangle(0, 0, 10, 4, 50).Build().ToString());

        // Translating keeps the radii and the rotation of the arcs, and Close has no coordinates
        var arc = new PathBuilder().MoveTo(0, 0).ArcTo(5, 6, 30, largeArc: true, sweep: false, 10, 0).QuadraticTo(1, 2, 3, 4).Close().Build();
        Assert.Equal("M1,2 A5,6 30 1,0 11,2 Q2,4 4,6 Z", arc.Translate(1, 2).ToString());
        Assert.Equal(new PathCommand(PathCommandKind.Close, 0, 0), arc.Translate(1, 2).Commands[^1]);

        Assert.Throws<ArgumentNullException>("path", () => new PathBuilder().Append(null!));
    }

    [Fact]
    public void Transforms()
    {
        #region VectorGraphics_Transforms
        // Scale by 2 around the origin, then move by (10, 20)
        var transform = SceneTransform.Scale(2, 2).Then(SceneTransform.Translate(10, 20));
        Assert.Equal((16.0, 28.0), transform.Apply(3, 4));
        Assert.Equal(new SceneTransform(2, 0, 0, 2, 10, 20), transform);
        #endregion

        // A scale around a point keeps the point in place
        Assert.Equal((5.0, 5.0), SceneTransform.Scale(3, 4, 5, 5).Apply(5, 5));

        // Then applies the transforms in order
        var rotate = SceneTransform.Rotate(90);
        var translate = SceneTransform.Translate(1, 0);
        var (x, y) = rotate.Then(translate).Apply(1, 0);
        Assert.Equal(1.0, x, 1e-12);
        Assert.Equal(1.0, y, 1e-12);
        (x, y) = translate.Then(rotate).Apply(1, 0);
        Assert.Equal(0.0, x, 1e-12);
        Assert.Equal(2.0, y, 1e-12);

        Assert.Equal(rotate, rotate.Then(SceneTransform.Identity));
        Assert.Equal(rotate, SceneTransform.Identity.Then(rotate));
    }

    [Fact]
    public void ItalicTextAndHyperlinks()
    {
        var scene = new Scene(100, 50, "sans-serif");
        var group = scene.Root.Add(new SceneGroup { Hyperlink = "https://example.com/?a=1&b=2" });
        group.Add(new SceneText(10, 20, "Italic", new SceneFont(12, Italic: true), SceneColor.Black));
        group.Add(new SceneText(10, 40, "Both", new SceneFont(12, Bold: true, Italic: true), SceneColor.Black));
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Equal(
            """
            <svg xmlns="http://www.w3.org/2000/svg" width="100" height="50" viewBox="0 0 100 50" role="img" font-family="sans-serif">
            <a href="https://example.com/?a=1&amp;b=2">
            <g>
            <text x="10" y="20" font-size="12" font-style="italic" fill="#000000">Italic</text>
            <text x="10" y="40" font-size="12" font-weight="bold" font-style="italic" fill="#000000">Both</text>
            </g>
            </a>
            </svg>

            """.ReplaceLineEndings("\n"),
            svg);
    }

    [Fact]
    public void IdentifiersUseThePrefix()
    {
        var scene = new Scene(10, 10, "sans-serif");
        scene.Root.Add(new SceneGroup { Clip = new SceneBounds(0, 0, 5, 5) });
        Assert.Contains("id=\"scene-clip0\"", SvgSceneRenderer.Render(scene));
        Assert.Contains("id=\"diagram-clip0\"", SvgSceneRenderer.Render(scene, new SvgRenderOptions { IdPrefix = "diagram" }));
    }

    [Theory]
    [InlineData("rgb(255, 0, 0)", "#ff0000")]
    [InlineData("RGB(255,128,0)", "#ff8000")]
    [InlineData("rgba(232,232,232, 0.8)", "#e8e8e8cc")]
    [InlineData("rgb(255 0 0 / 50%)", "#ff000080")]
    [InlineData("rgb(100%, 50%, 0%)", "#ff8000")]
    [InlineData("hsl(240, 60%, 86.2745098039%)", "#c7c7f1")]
    [InlineData("hsl(-79.4117647059, 100%, 93.3333333333%)", "#f4ddff")]
    [InlineData("hsla(0, 0%, 0%, 0.5)", "#00000080")]
    [InlineData("hsl(120deg 100% 25%)", "#008000")]
    [InlineData("rebeccapurple", "#663399")]
    [InlineData("LightGoldenRodYellow", "#fafad2")]
    [InlineData(" #ABC ", "#aabbcc")]
    public void CssColors(string value, string expected)
    {
        Assert.True(SceneColor.TryParse(value, out var color));
        Assert.Equal(expected, color.ToString());
    }

    [Theory]
    [InlineData("rgb(1, 2)")]
    [InlineData("rgb(1, 2, 3, 4, 5)")]
    [InlineData("rgb(1 2, 3)")]
    [InlineData("rgb(1, 2, 3")]
    [InlineData("rgb(a, b, c)")]
    [InlineData("foo(1, 2, 3)")]
    [InlineData("rgb(1 2 3 4)")]
    [InlineData("rgb(NaN, 0, 0)")]
    [InlineData("notacolor")]
    public void InvalidCssColors(string value)
    {
        Assert.False(SceneColor.TryParse(value, out _));
    }

    [Fact]
    public void DataAttributeNames()
    {
        Assert.True(SceneElement.IsValidDataAttributeName("node-id.v_2"));
        Assert.False(SceneElement.IsValidDataAttributeName("Node"));
        Assert.False(SceneElement.IsValidDataAttributeName(string.Empty));
        Assert.False(SceneElement.IsValidDataAttributeName(null));
        var element = new SceneCircle(0, 0, 1);
        element.SetDataAttribute("id", "a");
        Assert.Equal("a", element.DataAttributes["id"]);
        Assert.Throws<ArgumentException>("name", () => element.SetDataAttribute("Id", "a"));
        Assert.Throws<ArgumentException>("paramName", () => SceneElement.ThrowIfInvalidDataAttributeName("node id", "paramName"));
    }
}
