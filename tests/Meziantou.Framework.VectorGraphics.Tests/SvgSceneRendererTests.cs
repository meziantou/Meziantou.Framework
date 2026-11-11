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

    [Fact]
    public void SceneFonts()
    {
        var testData = Path.Combine(AppContext.BaseDirectory, "TestData");

        #region VectorGraphics_SceneFonts
        // The fonts of the texts: the family of the first face is the default one
        var fonts = SceneFontCollection.Load(
            Path.Combine(testData, "LiberationSans-Regular.woff2"),
            Path.Combine(testData, "LiberationSans-Bold.woff2"),
            Path.Combine(testData, "LiberationMono-Regular.woff2"));

        // The layout measures the texts with the fonts, and the scene keeps them so every renderer draws with them
        var measurer = new FontFaceTextMeasurer(fonts);
        var scene = new Scene(width: 300, height: 60, fontFamily: measurer.FontFamily) { Fonts = measurer.Fonts };

        // Each text has its own family, or the family of the scene
        var code = new SceneFont(12, Family: "Liberation Mono");
        scene.Root.Add(new SceneText(10, 20, "Title", new SceneFont(16, Bold: true), SceneColor.Black));
        scene.Root.Add(new SceneText(10, 45, "var x = 1;", code, SceneColor.Black) { MeasuredWidth = measurer.Measure("var x = 1;", code).Width });

        // The SVG can embed the fonts, so every viewer draws the texts with them
        var svg = SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true });
        #endregion

        Assert.Contains("font-family=\"Liberation Sans, Liberation Mono\"", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"45\" font-family=\"Liberation Mono, Liberation Sans\" font-size=\"12\"", svg, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"20\" font-size=\"16\" font-weight=\"bold\"", svg, StringComparison.Ordinal);

        // Only the faces that draw a character are embedded: the regular face of Liberation Sans draws none
        Assert.Contains("<style>@font-face{font-family:&quot;Liberation Sans&quot;;font-weight:700;font-style:normal;src:url(data:font/woff2;base64,", svg, StringComparison.Ordinal);
        Assert.Contains("@font-face{font-family:&quot;Liberation Mono&quot;;font-weight:400;", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("font-family:&quot;Liberation Sans&quot;;font-weight:400", svg, StringComparison.Ordinal);
        Assert.Equal(svg, SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true }));

        // Without the option, or without fonts, nothing is embedded
        Assert.DoesNotContain("@font-face", SvgSceneRenderer.Render(scene), StringComparison.Ordinal);
        scene.Fonts = null;
        var withoutFonts = SvgSceneRenderer.Render(scene, new SvgRenderOptions { EmbedFonts = true });
        Assert.DoesNotContain("@font-face", withoutFonts, StringComparison.Ordinal);
        Assert.Contains("<text x=\"10\" y=\"45\" font-family=\"Liberation Mono\" font-size=\"12\"", withoutFonts, StringComparison.Ordinal);
    }

    [Fact]
    public void TextWithTheFamilyOfTheSceneHasNoFontFamily()
    {
        var scene = new Scene(100, 20, "Helvetica");
        scene.Root.Add(new SceneText(0, 10, "a", new SceneFont(10, Family: "Helvetica"), SceneColor.Black));
        scene.Root.Add(new SceneText(0, 10, "b", new SceneFont(10, Family: "Courier \"New\" & <Co>"), SceneColor.Black));
        var svg = SvgSceneRenderer.Render(scene);
        Assert.Contains("<text x=\"0\" y=\"10\" font-size=\"10\"", svg, StringComparison.Ordinal);
        Assert.Contains("font-family=\"Courier &quot;New&quot; &amp; &lt;Co&gt;\"", svg, StringComparison.Ordinal);
    }
}
