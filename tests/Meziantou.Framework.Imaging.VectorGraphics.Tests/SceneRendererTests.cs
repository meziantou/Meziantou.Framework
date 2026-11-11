using Meziantou.Framework.Imaging.VectorGraphics.Internal;

namespace Meziantou.Framework.Imaging.VectorGraphics.Tests;

public sealed class SceneRendererTests
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);

    [Fact]
    public void RenderASceneAsAnImage()
    {
        #region ImagingVectorGraphics_ToImage
        // Lay the scene out with the fonts that draw the image, so its texts fit
        var measurer = SceneImageRenderer.CreateMeasurer();
        var label = "Hello";
        var font = new SceneFont(16);
        var width = measurer.Measure(label, font).Width + 20;

        var scene = new Scene(width, 40, measurer.FontFamily) { Background = SceneColor.White };
        scene.Root.Add(new SceneText(10, 26, label, font, SceneColor.Black));

        using var image = scene.ToImage(new SceneImageOptions { Scale = 2 });
        Assert.Equal(80, image.Height);

        // Or draw it on an existing image
        using var poster = new Image<Rgba32>(300, 200, Colors.White);
        poster.Draw(canvas => canvas.DrawScene(scene, new PointD(50, 50)));
        #endregion
    }

    [Fact]
    public void RectanglesAreFilledAndStroked()
    {
        var scene = new Scene(40, 20, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 20, 20) { Fill = (SceneColor)"#ff0000" });
        scene.Root.Add(new SceneRectangle(22, 2, 16, 16) { Fill = (SceneColor)"#0000ff", CornerRadius = 3, Stroke = (SceneColor)"#ff0000", StrokeWidth = 2 });
        scene.Root.Add(new SceneRectangle(20, 0, 2, 0) { Stroke = (SceneColor)"#00ff00" });
        scene.Root.Add(new SceneRectangle(0, 0, 0, 0) { Fill = (SceneColor)"#00ff00" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][10, 10]);
        Assert.Equal(Blue, image.Frames[0][30, 10]);
        Assert.Equal(Red, image.Frames[0][30, 2]);
        Assert.Equal(Colors.White, image.Frames[0][39, 0]);
    }

    [Fact]
    public void CirclesLinesAndPaths()
    {
        var scene = new Scene(60, 20, "sans-serif");
        scene.Root.Add(new SceneCircle(10, 10, 8) { Fill = (SceneColor)"#ff0000", Stroke = (SceneColor)"#0000ff" });
        scene.Root.Add(new SceneCircle(10, 10, 0) { Fill = (SceneColor)"#00ff00" });
        scene.Root.Add(new SceneLine(20, 10, 40, 10) { Stroke = (SceneColor)"#0000ff", StrokeWidth = 4, StrokeDashArray = [2, 2], LineCap = SceneLineCap.Square, LineJoin = SceneLineJoin.Bevel });
        scene.Root.Add(new SceneLine(20, 0, 40, 0));
        var path = new PathBuilder().MoveTo(42, 2).LineTo(58, 2).QuadraticTo(58, 18, 50, 18).CubicTo(45, 18, 42, 12, 42, 2).Close().Build();
        scene.Root.Add(new ScenePath(path) { Fill = (SceneColor)"#ff0000", Stroke = (SceneColor)"#0000ff", LineCap = SceneLineCap.Round, LineJoin = SceneLineJoin.Round });
        scene.Root.Add(new ScenePath(new PathBuilder().Build()) { Fill = (SceneColor)"#00ff00" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][10, 10]);
        Assert.Equal(Blue, image.Frames[0][21, 10]);
        Assert.Equal(Red, image.Frames[0][50, 8]);
    }

    [Fact]
    public void EvenOddRingHasAHole()
    {
        var scene = new Scene(40, 40, "sans-serif");
        var ring = new PathBuilder().AddCircle(20, 20, 18).AddCircle(20, 20, 8).Build();
        scene.Root.Add(new ScenePath(ring) { Fill = (SceneColor)"#ff0000", FillRule = SceneFillRule.EvenOdd });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][20, 6]);
        Assert.Equal(0, image.Frames[0][20, 20].A);
    }

    [Fact]
    public void ArcsAreTranslatedOneToOne()
    {
        var scene = new Scene(40, 40, "sans-serif");
        var half = new PathBuilder().MoveTo(20, 2).ArcTo(18, 18, 0, largeArc: false, sweep: true, 20, 38).Close().Build();
        scene.Root.Add(new ScenePath(half) { Fill = (SceneColor)"#ff0000" });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][30, 20]);
        Assert.Equal(0, image.Frames[0][10, 20].A);
    }

    [Fact]
    public void GradientsGoFromTheFirstStopToTheLast()
    {
        var scene = new Scene(100, 10, "sans-serif");
        scene.Root.Add(new SceneRectangle(0, 0, 100, 10) { Fill = new LinearGradientPaint(0, 0, 100, 0, [new SceneGradientStop(0, SceneColor.Parse("#ff0000")), new SceneGradientStop(1, SceneColor.Parse("#0000ff"))]) });
        using var image = scene.ToImage();
        Assert.True(image.Frames[0][1, 5].R > 240);
        Assert.True(image.Frames[0][98, 5].B > 240);
    }

    [Fact]
    public void GroupsApplyOpacityClipAndTransform()
    {
        var scene = new Scene(40, 40, "sans-serif");
        var faded = scene.Root.Add(new SceneGroup { Opacity = 0.5 });
        faded.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#ff0000" });
        var hidden = scene.Root.Add(new SceneGroup { Opacity = 0 });
        hidden.Add(new SceneRectangle(0, 20, 10, 10) { Fill = (SceneColor)"#ff0000" });
        var clipped = scene.Root.Add(new SceneGroup { Clip = new SceneBounds(20, 0, 5, 40), Transform = SceneTransform.Translate(10, 0) });
        clipped.Add(new SceneRectangle(5, 0, 20, 10) { Fill = (SceneColor)"#0000ff" });
        var rotated = scene.Root.Add(new SceneGroup { Transform = SceneTransform.Rotate(90, 30, 30) });
        rotated.Add(new SceneRectangle(30, 25, 10, 2) { Fill = (SceneColor)"#ff0000" });
        using var image = scene.ToImage();
        Assert.InRange(image.Frames[0][5, 5].A, 120, 135);
        Assert.Equal(0, image.Frames[0][5, 25].A);
        Assert.Equal(Blue, image.Frames[0][22, 5]);
        Assert.Equal(0, image.Frames[0][27, 5].A);
        Assert.Equal(Red, image.Frames[0][34, 35]);
    }

    [Fact]
    public void ElementsHaveTransforms()
    {
        // A rectangle moved by its transform, masked in the coordinates of its parent: only its left half is drawn
        var scene = new Scene(40, 20, "sans-serif") { Background = SceneColor.White };
        var mask = new SceneMask();
        mask.Content.Add(new SceneRectangle(0, 0, 25, 20) { Fill = SceneColor.White });
        scene.Root.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#ff0000", Transform = SceneTransform.Translate(20, 5), Mask = mask });
        scene.Root.Add(new SceneRectangle(0, 0, 10, 10) { Fill = (SceneColor)"#0000ff", Transform = new SceneTransform(double.NaN, 0, 0, 1, 0, 0) });
        using var image = scene.ToImage();
        Assert.Equal(Red, image.Frames[0][22, 10]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][27, 10]);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][5, 5]);
    }

    [Fact]
    public void TextIsDrawnFromItsBaselineAndAnchor()
    {
        var scene = new Scene(200, 100, "sans-serif");
        scene.Root.Add(new SceneText(100, 30, "MMMM", new SceneFont(20, Weight: SceneFont.BoldWeight), SceneColor.Black) { Anchor = SceneTextAnchor.Middle });
        scene.Root.Add(new SceneText(200, 60, "MMMM", new SceneFont(20), SceneColor.Black) { Anchor = SceneTextAnchor.End });
        scene.Root.Add(new SceneText(20, 95, "MM", new SceneFont(20), SceneColor.Black) { Rotation = -90 });
        scene.Root.Add(new SceneText(0, 0, string.Empty, new SceneFont(20), SceneColor.Black));
        using var image = scene.ToImage();

        // Centered on 100, with its baseline at 30
        var (left, top, right, bottom) = GetInkBounds(image, 0, 0, 200, 40);
        Assert.InRange((left + right) / 2.0, 98, 102);
        Assert.InRange(bottom, 28, 30);
        Assert.InRange(top, 14, 18);

        // Ends at the right edge
        (_, _, right, bottom) = GetInkBounds(image, 0, 40, 200, 62);
        Assert.InRange(right, 196, 199);
        Assert.InRange(bottom, 58, 60);

        // Rotated by -90 degrees around its anchor: it goes up from (20, 95), its glyphs on the left of x = 20
        (left, top, right, bottom) = GetInkBounds(image, 0, 62, 30, 100);
        Assert.InRange(right, 18, 20);
        Assert.InRange(bottom, 90, 95); // The side bearing of the first glyph
        Assert.True(left < right);
        Assert.True(top < 80);
    }

    [Fact]
    public void LetterSpacingAndOverline()
    {
        // 3 spaces of 10 pixels between the 4 letters
        var font = new SceneFont(20);
        var scene = new Scene(200, 100, "sans-serif");
        scene.Root.Add(new SceneText(10, 30, "MMMM", font, SceneColor.Black));
        scene.Root.Add(new SceneText(10, 70, "MMMM", font, SceneColor.Black) { LetterSpacing = 10 });
        scene.Root.Add(new SceneText(150, 90, "xx", font, SceneColor.Black) { Decorations = SceneTextDecorations.Overline });
        using var image = scene.ToImage();
        var (left, _, right, _) = GetInkBounds(image, 0, 0, 140, 40);
        var (spacedLeft, _, spacedRight, _) = GetInkBounds(image, 0, 40, 140, 80);
        Assert.Equal(left, spacedLeft);
        Assert.InRange(spacedRight - right, 29, 31);

        // The overline is above the x-height, at the ascent of the font
        var (_, top, _, bottom) = GetInkBounds(image, 140, 60, 200, 100);
        Assert.InRange(bottom, 88, 90);
        Assert.InRange(top, 70, 76);
    }

    [Fact]
    public void TextOnAPath()
    {
        // A text along a horizontal path from 10 to 60: the characters beyond its end are not drawn
        var scene = new Scene(200, 100, "sans-serif");
        scene.Root.Add(new SceneText(0, 0, "MMMMMMMMMM", new SceneFont(20), SceneColor.Black) { Path = new PathBuilder().MoveTo(10, 50).LineTo(60, 50).Build() });

        // Vertical, going down, centered on the middle of the path: the tops of the glyphs are on its right, as in SVG
        scene.Root.Add(new SceneText(0, 0, "MM", new SceneFont(20), SceneColor.Black) { Path = new PathBuilder().MoveTo(150, 10).LineTo(150, 90).Build(), PathStartOffset = 40, Anchor = SceneTextAnchor.Middle });
        using var image = scene.ToImage();
        var (left, top, right, bottom) = GetInkBounds(image, 0, 0, 100, 100);
        Assert.InRange(left, 10, 13);
        Assert.InRange(right, 50, 62);
        Assert.InRange(bottom, 48, 50);
        Assert.InRange(top, 33, 38);

        (left, top, right, bottom) = GetInkBounds(image, 100, 0, 200, 100);
        Assert.InRange(left, 150, 152);
        Assert.InRange(right, 163, 170);
        Assert.InRange((top + bottom) / 2.0, 48, 52);
    }

    [Fact]
    public void Patterns()
    {
        // A tile of 10 by 10 pixels with a red square in its top-left quarter, repeated, and scaled by 2
        var pattern = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        pattern.Content.Add(new SceneRectangle(0, 0, 5, 5) { Fill = (SceneColor)"#ff0000" });
        var scaled = new PatternPaint(new SceneBounds(0, 0, 10, 10)) { Transform = SceneTransform.Scale(2, 2) };
        scaled.Content.Add(new SceneRectangle(0, 0, 5, 5) { Fill = (SceneColor)"#0000ff" });

        // A pattern that paints with itself does not draw its content again
        var recursive = new PatternPaint(new SceneBounds(0, 0, 10, 10));
        recursive.Content.Add(new SceneRectangle(0, 0, 10, 10) { Fill = recursive });
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(0, 0, 40, 40) { Fill = pattern });
        scene.Root.Add(new SceneGroup { Transform = SceneTransform.Translate(50, 0) }).Add(new SceneRectangle(0, 0, 40, 40) { Fill = scaled });
        scene.Root.Add(new SceneRectangle(0, 45, 10, 10) { Fill = recursive });
        scene.Root.Add(new SceneRectangle(20, 45, 10, 10) { Fill = new PatternPaint(new SceneBounds(0, 0, 0, 10)) });
        using var image = scene.ToImage();
        var pixels = image.Frames[0];
        Assert.Equal(Red, pixels[2, 2]);
        Assert.Equal(Red, pixels[12, 22]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[7, 7]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[17, 2]);
        Assert.Equal(Blue, pixels[58, 8]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[62, 2]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[5, 50]);
        Assert.Equal(new Rgba32(255, 255, 255), pixels[25, 50]);
    }

    private static (int Left, int Top, int Right, int Bottom) GetInkBounds(Image<Rgba32> image, int x0, int y0, int x1, int y1)
    {
        var (left, top, right, bottom) = (int.MaxValue, int.MaxValue, -1, -1);
        for (var y = y0; y < y1; y++)
        {
            for (var x = x0; x < x1; x++)
            {
                if (image.Frames[0][x, y].A > 128)
                {
                    left = Math.Min(left, x);
                    top = Math.Min(top, y);
                    right = Math.Max(right, x);
                    bottom = Math.Max(bottom, y);
                }
            }
        }

        return (left, top, right, bottom);
    }

    [Fact]
    public void ItalicTextUsesTheItalicFont()
    {
        var measurer = SceneImageRenderer.CreateMeasurer();
        Assert.Equal("Liberation Sans", measurer.FontFamily);
        Assert.Same(SceneFonts.SansItalic, measurer.GetFont(new SceneFont(10, Italic: true)));
        Assert.Same(SceneFonts.SansBoldItalic, measurer.GetFont(new SceneFont(10, Weight: SceneFont.BoldWeight, Italic: true)));
        Assert.Same(SceneFonts.SansBold, measurer.GetFont(new SceneFont(10, Weight: SceneFont.BoldWeight)));

        // An italic glyph leans to the right: the ink of the top of an I is right of the ink of its bottom
        var scene = new Scene(60, 60, "sans-serif");
        scene.Root.Add(new SceneText(20, 50, "I", new SceneFont(48, Italic: true), SceneColor.Black));
        using var image = scene.ToImage();
        var (topLeft, _, _, _) = GetInkBounds(image, 0, 10, 60, 20);
        var (bottomLeft, _, _, _) = GetInkBounds(image, 0, 40, 60, 50);
        Assert.True(topLeft > bottomLeft);

        // A custom regular font is used for every style that has no font of its own
        var options = new SceneImageOptions { Font = SceneFonts.SansBold };
        Assert.Same(SceneFonts.SansBold, SceneImageRenderer.CreateMeasurer(options).GetFont(new SceneFont(10, Italic: true)));
        Assert.Same(SceneFonts.SansBold, SceneImageRenderer.CreateMeasurer(options).GetFont(new SceneFont(10, Weight: SceneFont.BoldWeight, Italic: true)));
    }

    private static SceneFontFace LoadFace(string fileName) => SceneFontFace.Load(Path.Combine(AppContext.BaseDirectory, "TestData", fileName))[0];

    [Fact]
    public void EachTextIsDrawnWithTheFamilyOfTheFonts()
    {
        var fonts = new SceneFontCollection(LoadFace("LiberationSans-Regular.woff2"), LoadFace("LiberationMono-Regular.woff2"));
        var measurer = new ImageTextMeasurer(fonts);
        Assert.Same(fonts, measurer.Fonts);
        Assert.Equal("Liberation Sans, Liberation Mono", measurer.FontFamily);

        // The widths are the ones the measurer of VectorGraphics gives with the same fonts
        var mono = new SceneFont(20, Family: "Liberation Mono");
        Assert.Equal(new FontFaceTextMeasurer(fonts).Measure("iiiii", mono).Width, measurer.Measure("iiiii", mono).Width, 0.01);
        Assert.Equal(new FontFaceTextMeasurer(fonts).Measure("iiiii", new SceneFont(20)).Width, measurer.Measure("iiiii", new SceneFont(20)).Width, 0.01);

        // The scene draws the first text with Liberation Sans and the second with Liberation Mono: 'i' is 0.6 em wide in Liberation Mono
        var scene = new Scene(200, 60, measurer.FontFamily) { Fonts = fonts };
        scene.Root.Add(new SceneText(0, 25, "iiiii", new SceneFont(20), SceneColor.Black));
        scene.Root.Add(new SceneText(0, 55, "iiiii", mono, SceneColor.Black));
        using var image = scene.ToImage();
        var (_, _, sansRight, _) = GetInkBounds(image, 0, 0, 200, 30);
        var (_, _, monoRight, _) = GetInkBounds(image, 0, 30, 200, 60);
        Assert.InRange(sansRight, 15, 25);
        Assert.InRange(monoRight, 50, 60);

        // The fonts of the options draw a scene that has none
        scene.Fonts = null;
        using var withOptions = scene.ToImage(new SceneImageOptions { Fonts = fonts });
        Assert.Equal(monoRight, GetInkBounds(withOptions, 0, 30, 200, 60).Right);
        Assert.Same(fonts, SceneImageRenderer.CreateMeasurer(new SceneImageOptions { Fonts = fonts }).Fonts);
        Assert.Null(SceneImageRenderer.CreateMeasurer().Fonts);
    }

    [Fact]
    public void CharactersAFamilyDoesNotHaveAreDrawnWithTheNextFamily()
    {
        var sans = LoadFace("LiberationSans-Regular.woff2");
        var fonts = new SceneFontCollection(sans, LoadFace("NotoSansThai-Regular.ttf"));
        const string Text = "ab \u0E2A\u0E27\u0E31\u0E2A\u0E14\u0E35";
        var font = new SceneFont(20);
        Assert.Equal(new FontFaceTextMeasurer(fonts).Measure(Text, font).Width, new ImageTextMeasurer(fonts).Measure(Text, font).Width, 0.01);

        // Liberation Sans alone draws missing glyphs, which are not the Thai glyphs
        Image<Rgba32> Draw(SceneFontCollection collection)
        {
            var scene = new Scene(200, 40, collection.FontFamily) { Fonts = collection };
            scene.Root.Add(new SceneText(0, 30, Text, font, SceneColor.Black));
            return scene.ToImage();
        }

        using var withFallback = Draw(fonts);
        using var withoutFallback = Draw(new SceneFontCollection(sans));
        Assert.NotEqual(GetInkBounds(withFallback, 30, 0, 200, 40), GetInkBounds(withoutFallback, 30, 0, 200, 40));
    }

    private static readonly byte[] Png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEklEQVR4nGP4zwAE/0Ho////AB/uBftt3M6tAAAAAElFTkSuQmCC");

    [Fact]
    public void ImagesClipPathsAndRadialGradients()
    {
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };

        // A 2 by 2 image (red, blue / green, white), stretched to 40 by 40 and clipped to a circle
        var clipped = scene.Root.Add(new SceneGroup { ClipPath = new PathBuilder().AddCircle(20, 20, 20).Build() });
        clipped.Add(new SceneImage(0, 0, 40, 40, Png));

        // A radial gradient from black at its focal point to white on its circle
        scene.Root.Add(new SceneRectangle(50, 50, 50, 50) { Fill = new RadialGradientPaint(75, 75, 25, [new(0, SceneColor.Black), new(1, SceneColor.White)]) });

        // An image that is not valid is not drawn
        scene.Root.Add(new SceneImage(50, 0, 40, 40, new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3 }));
        using var image = scene.ToImage();

        var red = image.Frames[0][8, 8];
        Assert.True(red.R > 200 && red.G < 80 && red.B < 80, red.ToString());
        var blue = image.Frames[0][32, 8];
        Assert.True(blue.B > 200 && blue.R < 80, blue.ToString());
        Assert.Equal(Colors.White, image.Frames[0][1, 1]);
        Assert.True(image.Frames[0][75, 75].R < 30);
        Assert.True(image.Frames[0][98, 75].R > 220);
        Assert.Equal(Colors.White, image.Frames[0][70, 20]);
    }

    [Fact]
    public void DashOffsetAndMiterLimit()
    {
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };

        // Dashes of 10 and gaps of 10: the offset of 10 starts the line with a gap
        scene.Root.Add(new SceneLine(0, 5, 40, 5) { Stroke = SceneColor.Black, StrokeWidth = 4, StrokeDashArray = [10, 10] });
        scene.Root.Add(new SceneLine(0, 15, 40, 15) { Stroke = SceneColor.Black, StrokeWidth = 4, StrokeDashArray = [10, 10], StrokeDashOffset = 10 });

        // A corner of about 20 degrees needs a miter 5.6 times the width of the line: a limit of 4 bevels it, 10 does not
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(15, 95).LineTo(25, 40).LineTo(35, 95).Build()) { Stroke = SceneColor.Black, StrokeWidth = 6 });
        scene.Root.Add(new ScenePath(new PathBuilder().MoveTo(65, 95).LineTo(75, 40).LineTo(85, 95).Build()) { Stroke = SceneColor.Black, StrokeWidth = 6, MiterLimit = 10 });
        using var image = scene.ToImage();

        Assert.True(image.Frames[0][5, 5].R < 100);
        Assert.True(image.Frames[0][15, 5].R > 200);
        Assert.True(image.Frames[0][5, 15].R > 200);
        Assert.True(image.Frames[0][15, 15].R < 100);
        Assert.True(image.Frames[0][25, 30].R > 200);
        Assert.True(image.Frames[0][75, 30].R < 160);
    }

    [Fact]
    public void GradientsBlendModesMasksAndDecorations()
    {
        var red = (SceneColor)"#ff0000";
        var blue = (SceneColor)"#0000ff";
        var green = (SceneColor)"#00ff00";
        var scene = new Scene(100, 100, "sans-serif") { Background = SceneColor.White };

        // A gradient of 10 pixels from red to blue, repeated, reflected, and stretched over 100 pixels by its transform
        scene.Root.Add(new SceneRectangle(0, 0, 100, 10) { Fill = new LinearGradientPaint(0, 0, 10, 0, [new(0, red), new(1, blue)]) { Spread = SceneGradientSpread.Repeat } });
        scene.Root.Add(new SceneRectangle(0, 10, 100, 10) { Fill = new LinearGradientPaint(0, 0, 10, 0, [new(0, red), new(1, blue)]) { Spread = SceneGradientSpread.Reflect } });
        scene.Root.Add(new SceneRectangle(0, 20, 100, 10) { Fill = new LinearGradientPaint(0, 0, 1, 0, [new(0, red), new(1, blue)]) { Transform = SceneTransform.Scale(100, 1) } });

        // Red multiplied with blue is black, with white it is red
        scene.Root.Add(new SceneRectangle(0, 30, 50, 10) { Fill = blue });
        scene.Root.Add(new SceneRectangle(0, 30, 100, 10) { Fill = red, BlendMode = SceneBlendMode.Multiply });

        // A luminance mask shows the right half (white), an alpha mask the left half
        var luminance = scene.Root.Add(new SceneGroup { Mask = new SceneMask() });
        luminance.Mask!.Content.Add(new SceneRectangle(50, 40, 50, 10) { Fill = SceneColor.White });
        luminance.Add(new SceneRectangle(0, 40, 100, 10) { Fill = green });
        var alpha = new SceneMask { Mode = SceneMaskMode.Alpha };
        alpha.Content.Add(new SceneRectangle(0, 50, 50, 10) { Fill = SceneColor.Black });
        scene.Root.Add(new SceneRectangle(0, 50, 100, 10) { Fill = green, Mask = alpha });

        // An underline
        scene.Root.Add(new SceneText(10, 80, "ii", new SceneFont(20), SceneColor.Black) { Decorations = SceneTextDecorations.Underline });
        using var image = scene.ToImage();

        static bool IsRed(Rgba32 color) => color.R > 150 && color.B < 100;
        static bool IsBlue(Rgba32 color) => color.B > 150 && color.R < 100;
        var pixels = image.Frames[0];
        Assert.True(IsRed(pixels[1, 5]) && IsRed(pixels[11, 5]) && IsBlue(pixels[9, 5]), "repeat");
        Assert.True(IsRed(pixels[1, 15]) && IsBlue(pixels[11, 15]) && IsRed(pixels[19, 15]), "reflect");
        Assert.True(IsRed(pixels[5, 25]) && IsBlue(pixels[95, 25]), "transform");
        Assert.True(pixels[25, 35].R < 30 && pixels[25, 35].B < 30, "multiply with blue");
        Assert.True(IsRed(pixels[75, 35]), "multiply with white");
        Assert.True(pixels[25, 45].G > 200 && pixels[25, 45].R > 200, "luminance mask hides");
        Assert.True(pixels[75, 45].G > 200 && pixels[75, 45].R < 50, "luminance mask shows");
        Assert.True(pixels[25, 55].G > 200 && pixels[25, 55].R < 50, "alpha mask shows");
        Assert.True(pixels[75, 55].G > 200 && pixels[75, 55].R > 200, "alpha mask hides");
        Assert.Contains(Enumerable.Range(81, 3), y => pixels[13, y].R < 200);
        Assert.True(pixels[13, 87].R > 200, "nothing below the underline");
    }

    [Fact]
    public void BlurAndDropShadow()
    {
        var scene = new Scene(100, 60, "sans-serif") { Background = SceneColor.White };
        scene.Root.Add(new SceneRectangle(20, 20, 20, 20) { Fill = SceneColor.Black, Filter = new SceneBlurFilter(3) });
        scene.Root.Add(new SceneRectangle(60, 10, 20, 20) { Fill = SceneColor.Black, Filter = new SceneDropShadowFilter(10, 10, 0, (SceneColor)"#ff0000") });
        using var image = scene.ToImage();

        // The blur spreads the edge of the rectangle; the shadow is red, 10 pixels right and down
        var edge = image.Frames[0][19, 30];
        Assert.InRange(edge.R, 40, 220);
        Assert.Equal(Colors.Black, image.Frames[0][30, 30]);
        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][85, 35]);
        Assert.Equal(Colors.Black, image.Frames[0][70, 20]);
    }

    [Fact]
    public void WeightsSelectTheNearestFace()
    {
        var fonts = new SceneFontCollection(LoadFace("LiberationSans-Regular.woff2"), LoadFace("LiberationSans-Bold.woff2"));
        var measurer = new ImageTextMeasurer(fonts);
        var regular = measurer.Measure("Weights", new SceneFont(20)).Width;
        var bold = measurer.Measure("Weights", new SceneFont(20, Weight: SceneFont.BoldWeight)).Width;
        Assert.NotEqual(regular, bold);
        Assert.Equal(regular, measurer.Measure("Weights", new SceneFont(20, Weight: 500)).Width);
        Assert.Equal(bold, measurer.Measure("Weights", new SceneFont(20, Weight: 600)).Width);
        Assert.Equal(bold, measurer.Measure("Weights", new SceneFont(20, Weight: 900)).Width);

        // Without fonts, the weights from 600 are drawn with the bold font
        var defaults = SceneImageRenderer.CreateMeasurer();
        Assert.Same(SceneFonts.SansBold, defaults.GetFont(new SceneFont(10, Weight: 600)));
        Assert.Same(SceneFonts.Sans, defaults.GetFont(new SceneFont(10, Weight: 599)));
    }

    [Fact]
    public void TextSpansAndColorMatrices()
    {
        var scene = new Scene(200, 60, "sans-serif") { Background = SceneColor.White };
        var font = new SceneFont(30, Weight: SceneFont.BoldWeight);
        scene.Root.Add(new SceneText(10, 40, [new SceneTextSpan("MM") { Color = (SceneColor)"#ff0000" }, new SceneTextSpan("MM") { Color = (SceneColor)"#0000ff", Decorations = SceneTextDecorations.Underline }], font, SceneColor.Black));
        scene.Root.Add(new SceneRectangle(150, 10, 40, 40) { Fill = (SceneColor)"#ff0000", Filter = SceneColorMatrixFilter.Grayscale() });
        using var image = scene.ToImage();

        // The first two letters are red, the last two blue and underlined
        var pixels = image.Frames[0];
        Assert.Contains(Enumerable.Range(10, 40), x => pixels[x, 30].R > 200 && pixels[x, 30].B < 80);
        Assert.Contains(Enumerable.Range(60, 60), x => pixels[x, 30].B > 200 && pixels[x, 30].R < 80);
        Assert.DoesNotContain(Enumerable.Range(60, 60), x => pixels[x, 30].R > 200 && pixels[x, 30].B < 80);
        Assert.Contains(Enumerable.Range(41, 5), y => pixels[100, y].B > 150 && pixels[100, y].R < 150);

        // Red in gray is 0.2126 of white
        Assert.InRange(pixels[170, 30].R, 50, 58);
        Assert.Equal(pixels[170, 30].R, pixels[170, 30].G);
        Assert.Equal(pixels[170, 30].R, pixels[170, 30].B);
    }

    [Fact]
    public void SpansAreDrawnAsTheyAreMeasured()
    {
        // Each span is laid out alone, as the measurer measures it: the text, anchored at its end, starts where its measured width says
        var measurer = SceneImageRenderer.CreateMeasurer();
        var font = new SceneFont(40);
        SceneTextSpan[] spans = [new("A") { Color = (SceneColor)"#ff0000" }, new("V") { Color = (SceneColor)"#0000ff" }];
        var width = measurer.Measure(spans, font).Width;
        Assert.Equal(measurer.Measure("A", font).Width + measurer.Measure("V", font).Width, width);

        var scene = new Scene(200, 60, "sans-serif");
        scene.Root.Add(new SceneText(100, 45, spans, font, SceneColor.Black) { Anchor = SceneTextAnchor.End });
        using var image = scene.ToImage();
        var (left, _, right, _) = GetInkBounds(image, 0, 0, 200, 60);
        Assert.InRange(right, 96, 100);
        Assert.InRange(left, 100 - width - 2, 100 - width + 4);
    }

    [Fact]
    public void ColorMatricesWithAConstantAlphaApplyToTheElementOnly()
    {
        // The alpha of the result is 1 for every pixel, the transparent ones too: only the bounds of the rectangle are painted
        double[] opaqueRed = [0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1];
        var scene = new Scene(100, 100, "sans-serif");
        scene.Root.Add(new SceneRectangle(10, 10, 20, 20) { Fill = SceneColor.Black, Filter = new SceneColorMatrixFilter(opaqueRed) });
        using var image = scene.ToImage();
        Assert.Equal(new Rgba32(255, 0, 0), image.Frames[0][20, 20]);
        Assert.Equal(0, image.Frames[0][60, 60].A);
    }

    [Fact]
    public void ColorMatricesWithAConstantAlphaContainTheInkOfTheTexts()
    {
        // An italic f overhangs its advance on the right: the region of the filter contains its ink
        double[] opaqueRed = [0, 0, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 1];
        var scene = new Scene(100, 100, "sans-serif");
        scene.Root.Add(new SceneText(20, 60, "f", new SceneFont(60, Italic: true), SceneColor.Black) { Filter = new SceneColorMatrixFilter(opaqueRed) });
        var layout = Meziantou.Framework.Imaging.Typography.TextLayout.Create("f", SceneFonts.SansItalic, 60);
        Assert.True(layout.InkBounds.Right > layout.Width + 1);
        using var image = scene.ToImage();
        var right = (int)Math.Floor(20 + layout.InkBounds.Right);
        Assert.Equal(255, image.Frames[0][right, 60 - (int)layout.Baseline + (int)layout.InkBounds.Top + 2].A);
    }

    [Fact]
    public async Task ImagesCanBeDrawnByConcurrentRenders()
    {
        var scene = new Scene(40, 40, "sans-serif");
        scene.Root.Add(new SceneImage(0, 0, 40, 40, Png));
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() =>
        {
            for (var i = 0; i < 20; i++)
            {
                using var image = scene.ToImage();
                Assert.True(image.Frames[0][2, 2].R > 200);
            }
        }, XunitCancellationToken)));
    }

    [Fact]
    public void SourcesAreLaidOutWithTheFontsOfTheImage()
    {
        var source = new TestSource();
        using var image = source.ToImage(new SceneImageOptions { Scale = 2 });
        Assert.Equal((20, 10), (image.Width, image.Height));
        Assert.IsType<ImageTextMeasurer>(source.Measurer);

        using var canvasImage = new Image<Rgba32>(40, 40, Colors.White);
        var bounds = default(RectangleD);
        canvasImage.Draw(canvas => bounds = canvas.DrawScene(source, new PointD(5, 5)));
        Assert.Equal(new RectangleD(5, 5, 10, 5), bounds);
        Assert.Equal(Red, canvasImage.Frames[0][6, 6]);
        Assert.Equal(Colors.White, canvasImage.Frames[0][16, 6]);
    }

    private sealed class TestSource : ISceneSource
    {
        public ITextMeasurer? Measurer { get; private set; }

        public Scene CreateScene(ITextMeasurer? textMeasurer = null)
        {
            Measurer = textMeasurer;
            var scene = new Scene(10, 5, textMeasurer?.FontFamily ?? "sans-serif");
            scene.Root.Add(new SceneRectangle(0, 0, 10, 5) { Fill = (SceneColor)"#ff0000" });
            return scene;
        }
    }

    [Fact]
    public void ScaleMultipliesThePixels()
    {
        var scene = new Scene(10, 10, "sans-serif") { Background = (SceneColor)"#0000ff" };
        scene.Root.Add(new SceneRectangle(0, 0, 5, 10) { Fill = (SceneColor)"#ff0000" });
        using var image = scene.ToImage(new SceneImageOptions { Scale = 3 });
        Assert.Equal((30, 30), (image.Width, image.Height));
        Assert.Equal(Red, image.Frames[0][14, 15]);
        Assert.Equal(Blue, image.Frames[0][16, 15]);
    }

    [Fact]
    public void ToVectorPathOfEmptyPathIsNull()
    {
        Assert.Null(SceneRenderer.ToVectorPath(new PathBuilder().Build()));
        Assert.NotNull(SceneRenderer.ToVectorPath(new PathBuilder().AddRectangle(0, 0, 1, 1).Build()));
    }
}
