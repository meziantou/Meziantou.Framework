using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The glyphs the fonts of the corpus draw with their own colors, against the images the corpus generator recorded from
/// the reference renderer, <c>hb-raster</c> (see the Fonts section of the fixtures README).
/// </summary>
/// <remarks>
/// The two renderers do not give the same pixels: the edges of the shapes are anti-aliased by two different
/// rasterizers, and the reference one interpolates and composes colors with 8-bit values. What is compared is that
/// almost every pixel is close, that no region of an image differs, and that the images are the same on average.
/// </remarks>
public sealed class ColorFontConformanceTests
{
    public static TheoryData<string> FontsWithRenders()
    {
        var data = new TheoryData<string>();
        foreach (var font in LoadFonts())
        {
            if (font.Renders is { Count: > 0 })
            {
                data.Add(font.Id);
            }
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(FontsWithRenders))]
    public void TheColorGlyphsAreTheOnesOfTheReferenceRenderer(string id)
    {
        var root = FixtureRoot.GetDirectory();
        var entry = LoadFonts().Single(font => font.Id == id);
        var font = Font.Load(root / entry.File.Path);
        Assert.True(font.HasColorGlyphs);
        var failures = new List<string>();
        foreach (var render in entry.Renders!)
        {
            using var expected = Image.Load<Rgba32>(root / render.File.Path);
            var instance = render.Variations is null ? font : FontTestSupport.WithVariations(font, render.Variations);
            var layout = TextLayout.Create(render.Characters, new TextStyle(instance, render.Size) { ColorPaletteIndex = render.Palette });
            var foreground = render.Foreground is null ? new Rgba32(0, 0, 0) : ParseColor(render.Foreground);
            using var actual = new Image<Rgba32>(expected.Width, expected.Height, new Rgba32(255, 255, 255));

            // The origin of the first glyph is at the margin, on the baseline of the box of the line. The reference
            // renderer draws the images of bitmap glyphs one pixel higher than the font places them: the renders of
            // the bitmap fonts, which have nothing else, say so.
            var origin = new PointD(render.Margin, render.Margin + render.Ascent - layout.Baseline + render.ImageShift);

            // The reference renderer gives the alpha of the color of the text to the parts of a glyph that take that
            // color, where a solid brush fades the whole glyph as browsers do: a gradient of that one color does what
            // the reference renderer does
            Brush brush = foreground.A == 255 ? new SolidBrush(foreground) : new LinearGradientBrush(default, new PointD(1, 0), [new GradientStop(0, foreground), new GradientStop(1, foreground)]);
            actual.Draw(canvas => canvas.FillText(brush, layout, origin), TestContext.Current.CancellationToken);

            var statistics = Compare(expected, actual);
            if (statistics.Mean > MaxMean || statistics.FarFraction > MaxFarFraction || statistics.LargestFarBlock > MaxFarBlock)
            {
                var artifacts = TestArtifacts.GetDirectory("color-fonts");
                var name = TestArtifacts.SanitizeFileName(id + "-" + Path.GetFileNameWithoutExtension(render.File.Path));
                actual.Save(artifacts / (name + ".actual.png"));
                failures.Add(string.Create(CultureInfo.InvariantCulture, $"{render.File.Path} [{render.Text}] {render.Variations}: mean {statistics.Mean:0.###}, {statistics.FarFraction:P3} of the pixels differ by more than {FarDifference}, the largest block of them is {statistics.LargestFarBlock} pixels wide (see {artifacts / (name + ".actual.png")})"));
            }
        }

        Assert.Empty(failures, $"{failures.Count} of {entry.Renders.Count} images differ:\n  " + string.Join("\n  ", failures));
    }

    [Fact]
    public void TheCorpusHasRendersOfColorFonts()
    {
        Assert.True(FontsWithRenders().Count >= 4, "The renders of the color fonts of the corpus were not found");
    }

    /// <summary>The difference of a channel from which a pixel is counted as far from the reference.</summary>
    private const int FarDifference = 24;

    /// <summary>The greatest mean difference of the channels of an image, on 255.</summary>
    private const double MaxMean = 1.0;

    /// <summary>The greatest part of the pixels of an image that are far from the reference: pixels of edges.</summary>
    private const double MaxFarFraction = 0.01;

    /// <summary>The greatest side of a square of pixels that are all far from the reference: an edge is thinner.</summary>
    private const int MaxFarBlock = 2;

    private static (double Mean, double FarFraction, int LargestFarBlock) Compare(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var width = expected.Width;
        var height = expected.Height;
        var far = new bool[width * height];
        var sum = 0L;
        var farCount = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var e = expected.Frames[0][x, y];
                var a = actual.Frames[0][x, y];
                var difference = Math.Max(Math.Abs(e.R - a.R), Math.Max(Math.Abs(e.G - a.G), Math.Abs(e.B - a.B)));
                sum += Math.Abs(e.R - a.R) + Math.Abs(e.G - a.G) + Math.Abs(e.B - a.B);
                if (difference > FarDifference)
                {
                    far[(y * width) + x] = true;
                    farCount++;
                }
            }
        }

        // The side of the largest square of far pixels: the size of the square that ends at each pixel.
        var sizes = new int[width * height];
        var largest = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                if (!far[(y * width) + x])
                    continue;

                var size = x == 0 || y == 0 ? 1 : 1 + Math.Min(sizes[((y - 1) * width) + x], Math.Min(sizes[(y * width) + x - 1], sizes[((y - 1) * width) + x - 1]));
                sizes[(y * width) + x] = size;
                largest = Math.Max(largest, size);
            }
        }

        return (sum / (3.0 * width * height), (double)farCount / (width * height), largest);
    }

    private static Rgba32 ParseColor(string value)
    {
        var number = Convert.ToUInt32(value, 16);
        return value.Length == 8
            ? new Rgba32((byte)(number >> 24), (byte)(number >> 16), (byte)(number >> 8), (byte)number)
            : new Rgba32((byte)(number >> 16), (byte)(number >> 8), (byte)number);
    }

    private static IReadOnlyList<FontEntry> LoadFonts()
    {
        var root = FixtureRoot.GetDirectory();
        return FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName).Fonts ?? [];
    }
}
