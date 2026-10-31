using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Samples;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Tests.Documentation;

/// <summary>
/// Runs the published usage examples (samples/Meziantou.Framework.Imaging.Samples/Examples.cs, linked into this project) and
/// checks that they do what the documentation says. Inputs are built by the examples themselves, except the AVIF inputs
/// of the decoding example, which are committed fixtures of the golden corpus (written by another encoder).
/// </summary>
public sealed class DocumentationExampleTests : IDisposable
{
    private static readonly Rgba32 Red = new(255, 0, 0);
    private static readonly Rgba32 Blue = new(0, 0, 255);

    private readonly FullPath _directory = FullPath.GetTempPath() / "Meziantou.Framework.Imaging.Tests" / ("examples-" + Guid.NewGuid().ToString("N"));

    public DocumentationExampleTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private FullPath GetPath(string name) => _directory / name;

    /// <summary>Saves a gradient of 1,024 colors, more than a palette holds.</summary>
    private FullPath SaveGradient(string name)
    {
        var path = GetPath(name);
        using var gradient = new Image<Rgba32>(32, 32);
        gradient.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    row[x] = new Rgba32((byte)(x * 8), (byte)(y * 8), 0);
                }
            }
        });

        gradient.Save(path);
        return path;
    }

    private static int CountColors(ImageFrame<Rgba32> frame)
    {
        var colors = new HashSet<Rgba32>();
        frame.ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                colors.UnionWith(pixels.GetRowSpan(y).ToArray());
            }
        });

        return colors.Count;
    }

    private (string Apng, string Gif) BuildAnimation()
    {
        var apng = GetPath("animation.apng");
        var gif = GetPath("animation.gif");
        Examples.BuildAnimation(apng, gif);
        return (apng, gif);
    }

    [Fact]
    public void BuildAnimationKeepsExactTimingAndPlays()
    {
        var (apngPath, gifPath) = BuildAnimation();

        using var apng = Image.Load<Rgba32>(apngPath);
        Assert.Equal(ImageFormat.Png, apng.Metadata.SourceFormat);
        Assert.Equal(2, apng.Frames.Count);
        Assert.Equal(new FrameDuration(1, 10), apng.Frames[0].Metadata.Duration);
        Assert.Equal(new FrameDuration(1, 30), apng.Frames[1].Metadata.Duration); // exact rational delay
        Assert.Equal(3, apng.Animation!.TotalPlays);
        Assert.Equal(Red, apng.Frames[0][0, 0]);
        Assert.Equal(Blue, apng.Frames[1][63, 63]);

        using var gif = Image.Load<Rgba32>(gifPath);
        Assert.Equal(ImageFormat.Gif, gif.Metadata.SourceFormat);
        Assert.Equal(2, gif.Frames.Count);
        Assert.Equal(new FrameDuration(1, 10), gif.Frames[0].Metadata.Duration);
        Assert.Equal(new FrameDuration(3, 100), gif.Frames[1].Metadata.Duration); // GIF default: nearest hundredth
        Assert.Equal(3, gif.Animation!.TotalPlays);
        Assert.Equal(Red, gif.Frames[0][0, 0]);
        Assert.Equal(Blue, gif.Frames[1][63, 63]);
    }

    [Fact]
    public void SaveOptimizedAnimationKeepsTheDisplayedFrames()
    {
        // A square moves over a background: the frames after the first one only store the region that changes
        var input = GetPath("scene.apng");
        using (var scene = new Image<Rgba32>(64, 48, new Rgba32(20, 140, 60)))
        {
            for (var i = 0; i < 6; i++)
            {
                var frame = i == 0 ? scene.Frames[0] : scene.AppendFrame(scene.Frames[0]);
                frame.Metadata.Duration = new FrameDuration(i + 1, 20);
                frame.ProcessPixelRows(pixels =>
                {
                    for (var y = 10; y < 18; y++)
                    {
                        pixels.GetRowSpan(y).Slice(i * 8, 8).Fill(Blue);
                    }
                });
            }

            scene.Animation!.TotalPlays = 2;
            scene.Save(input);
        }

        var (gifPath, apngPath, webpPath) = (GetPath("optimized.gif"), GetPath("optimized.apng"), GetPath("optimized.webp"));
        Examples.SaveOptimizedAnimation(input, gifPath, apngPath, webpPath);

        using var source = Image.Load<Rgba32>(input);
        foreach (var (path, full) in new (FullPath, ImageEncoder)[] { (gifPath, new GifEncoder()), (apngPath, new PngEncoder()), (webpPath, new WebPEncoder()) })
        {
            using var optimized = Image.Load<Rgba32>(path);
            Assert.Equal(source.Frames.Count, optimized.Frames.Count);
            Assert.Equal(2, optimized.Animation!.TotalPlays);
            for (var i = 0; i < source.Frames.Count; i++)
            {
                Assert.Equal(source.Frames[i].Metadata.Duration, optimized.Frames[i].Metadata.Duration);
                Assert.Equal(source.Frames[i][i * 8, 10], optimized.Frames[i][i * 8, 10]);
                Assert.Equal(source.Frames[i][63, 47], optimized.Frames[i][63, 47]);
            }

            var fullPath = GetPath("full" + path.Extension);
            source.Save(fullPath, full);
            Assert.True(new FileInfo(path).Length < new FileInfo(fullPath).Length, $"{path.Name}: {new FileInfo(path).Length} bytes, {new FileInfo(fullPath).Length} with full-canvas frames");
        }
    }

    [Fact]
    public void ResizeAnimationResizesEveryFrameAndKeepsTiming()
    {
        var (_, gifPath) = BuildAnimation();
        var output = GetPath("resized.gif");

        Examples.ResizeAnimation(gifPath, output);

        using var resized = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(240, 240), resized.Size); // Contain within 320x240, upscaling allowed by default
        Assert.Equal(2, resized.Frames.Count);
        Assert.Equal(new FrameDuration(3, 100), resized.Frames[1].Metadata.Duration);
        Assert.Equal(3, resized.Animation!.TotalPlays);
        Assert.Equal(Blue, resized.Frames[1][120, 120]);
    }

    [Fact]
    public void LetterboxPadsToTheBoxAndAddsTheBorder()
    {
        var input = GetPath("wide.png");
        using (var source = new Image<Rgb24>(400, 200, new Rgb24(200, 30, 30)))
        {
            source.Save(input);
        }

        var output = GetPath("letterboxed.png");
        Examples.Letterbox(input, output);

        // 400x200 -> 320x160 at (0, 40) on 320x240, then the 4-pixel border
        using var image = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(328, 248), image.Size);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][327, 247]);
        Assert.Equal(new Rgb24(0, 0, 0), image.Frames[0][4, 43]);
        Assert.Equal(new Rgb24(200, 30, 30), image.Frames[0][4, 44]);
        Assert.Equal(new Rgb24(200, 30, 30), image.Frames[0][323, 203]);
        Assert.Equal(new Rgb24(0, 0, 0), image.Frames[0][323, 204]);
    }

    [Fact]
    public void ComposeCardDrawsThePhotoInsideTheCircle()
    {
        var photoPath = GetPath("photo.png");
        using (var photo = new Image<Rgba32>(40, 40, new Rgba32(200, 30, 30)))
        {
            photo.Save(photoPath);
        }

        var output = GetPath("card.png");
        Examples.ComposeCard(photoPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(320, 120), image.Size);
        var frame = image.Frames[0];

        // The corners are outside the rounded rectangle; inside, the gradient is opaque and starts with its first color
        Assert.Equal(0, frame[0, 0].A);
        Assert.Equal(0, frame[319, 119].A);
        Assert.Equal(255, frame[160, 100].A);
        Assert.Equal(new Rgba32(0x2B, 0x58, 0x76), frame[160, 0]);

        // The photo fills the circle, and nothing of the square it was stretched to outside the circle
        Assert.Equal(new Rgba32(200, 30, 30), frame[60, 60]);
        Assert.Equal(new Rgba32(200, 30, 30), frame[40, 45]);
        Assert.Equal(frame[150, 30], frame[25, 30]);

        // The white ring is on the circle
        Assert.Equal(new Rgba32(255, 255, 255), frame[99, 60]);
        Assert.Equal(new Rgba32(255, 255, 255), frame[60, 20]);

        // The dashes start at 120: 120 to 126, a gap to 130, and so on
        Assert.True(frame[122, 60].R > frame[127, 60].R);
        Assert.Equal(frame[122, 60], frame[132, 60]);
        Assert.Equal(frame[127, 60], frame[137, 60]);
        Assert.Equal(frame[127, 60], frame[310, 60]);

        // The gauge is gold from the top of its circle to its left, clockwise; the quarter between them is not drawn
        Assert.Equal(Colors.Gold, frame[270, 16]);
        Assert.Equal(Colors.Gold, frame[283, 30]);
        Assert.Equal(Colors.Gold, frame[270, 43]);
        Assert.Equal(Colors.Gold, frame[256, 30]);
        Assert.Equal(frame[200, 20], frame[260, 20]);
    }

    [Fact]
    public void DrawIconFillsTheIconAndStrokesAPartOfItsOutline()
    {
        var output = GetPath("icon.png");
        var data = Examples.DrawIcon(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(96, 96), image.Size);
        var frame = image.Frames[0];
        Assert.Equal(Colors.MistyRose, frame[48, 48]);
        Assert.Equal(Colors.White, frame[48, 10]);
        Assert.Equal(Colors.White, frame[4, 90]);

        // The path that is written is the icon, four times larger
        Assert.StartsWith("M48 85.4 L42.2 80.12 C", data, StringComparison.Ordinal);
        Assert.EndsWith(" Z", data, StringComparison.Ordinal);
        var icon = Drawing.VectorPath.ParseSvgPathData(data);
        var length = icon.GetLength();

        // The outline is stroked over its first three quarters only
        Assert.True(icon.TryGetPointAtDistance(length * 0.4, out var stroked, out _));
        Assert.Equal(Colors.Crimson, frame[(int)stroked.X, (int)stroked.Y]);
        Assert.True(icon.TryGetPointAtDistance(length * 0.9, out var bare, out _));
        Assert.NotEqual(Colors.Crimson, frame[(int)bare.X, (int)bare.Y]);
    }

    [Fact]
    public void DrawChartDrawsACurveItsAreaAndItsAxes()
    {
        var output = GetPath("chart.png");
        Examples.DrawChart(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(240, 140), image.Size);
        var frame = image.Frames[0];

        // The curve goes through its values, and has a disk of 9 pixels across at both ends
        Assert.Equal(Colors.Crimson, frame[110, 80]);
        Assert.Equal(Colors.Crimson, frame[150, 30]);
        Assert.Equal(Colors.Crimson, frame[27, 97]);
        Assert.Equal(Colors.Crimson, frame[192, 52]);

        // The axes end with the head of an arrow: its tip is the end of the line, and it is wider than the line
        Assert.Equal(Colors.DimGray, frame[100, 120]);
        Assert.Equal(Colors.DimGray, frame[216, 122]);
        Assert.Equal(Colors.White, frame[218, 126]);
        Assert.Equal(Colors.White, frame[227, 120]);
        Assert.Equal(Colors.DimGray, frame[20, 17]);
        Assert.Equal(Colors.White, frame[20, 9]);

        // The area is hatched: stripes of its color and of the background, along diagonals
        var stripes = Enumerable.Range(60, 60).Select(x => frame[x, 110]).ToArray();
        Assert.Contains(Colors.LightSteelBlue, stripes);
        Assert.Contains(Colors.White, stripes);

        // Its outline is on the inside of its path: two pixels from its left side, none beyond it
        Assert.Equal(Colors.SteelBlue, frame[31, 110]);
        Assert.Equal(Colors.White, frame[28, 110]);
        Assert.Equal(Colors.White, frame[192, 110]);
        Assert.Equal(Colors.SteelBlue, frame[188, 110]);
    }

    [Fact]
    public void ApplyPhotoEffectsWritesEveryEffect()
    {
        // The left 12 columns are opaque red, the others a half-transparent green
        var photoPath = GetPath("photo.png");
        using (var photo = new Image<Rgba32>(32, 24, new Rgba32(40, 200, 60, 128)))
        {
            photo.Draw(canvas => canvas.FillRectangle(new Rgba32(220, 40, 40), new Rectangle(0, 0, 12, 24)));
            photo.Save(photoPath);
        }

        Examples.ApplyPhotoEffects(photoPath, _directory);

        // The Lomograph red is (255, 71, 23), darker the farther it is from the center: 0.23 of the half-diagonal of 20 pixels
        // keeps 77% of it, 0.78 keeps 22%, and the corners are nearly black
        using (var lomo = Image.Load<Rgba32>(GetPath("lomo.png")))
        {
            Assert.InRange(lomo.Frames[0][11, 12].R, 190, 205);
            Assert.InRange(lomo.Frames[0][0, 12].R, 50, 65);
            Assert.InRange(lomo.Frames[0][0, 0].R, 0, 15);
        }

        // A block that straddles the two colors: alpha (4 * 255 + 4 * 128) / 8 = 191.5, colors averaged premultiplied
        using (var mosaic = Image.Load<Rgba32>(GetPath("mosaic.png")))
        {
            Assert.Equal(new Rgba32(160, 93, 47, 192), mosaic.Frames[0][8, 0]);
            Assert.Equal(new Rgba32(160, 93, 47, 192), mosaic.Frames[0][15, 7]);
            Assert.Equal(new Rgba32(220, 40, 40), mosaic.Frames[0][7, 23]);
        }

        // Inside a flat area, the oil painting keeps the color
        using (var painting = Image.Load<Rgba32>(GetPath("painting.png")))
        {
            Assert.Equal(new Size(32, 24), painting.Size);
            Assert.Equal(new Rgba32(220, 40, 40), painting.Frames[0][2, 10]);
            Assert.Equal(new Rgba32(40, 200, 60, 128), painting.Frames[0][28, 10]);
        }

        // Deuteranopia: 0.367322 * 220 + 0.860646 * 40 - 0.227968 * 40 = 106.12, 0.280085 * 220 + 0.672501 * 40 + 0.047413 * 40 = 90.42,
        // -0.011820 * 220 + 0.042940 * 40 + 0.968881 * 40 = 37.87
        using (var preview = Image.Load<Rgba32>(GetPath("deuteranopia.png")))
        {
            Assert.Equal(new Rgba32(106, 90, 38), preview.Frames[0][0, 0]);
        }

        // On white: (40 * 128 + 255 * 127 + 127) / 255 = 147
        using var flattened = Image.Load<Rgba32>(GetPath("flattened.png"));
        Assert.Equal(new Rgba32(147, 227, 157), flattened.Frames[0][20, 5]);
        Assert.Equal(new Rgba32(220, 40, 40), flattened.Frames[0][0, 0]);
    }

    [Fact]
    public void ApplyColorFiltersRecolorsAnImageAPartOfItAndADrawing()
    {
        var photoPath = GetPath("plain.png");
        using (var photo = new Image<Rgba32>(60, 60, new Rgba32(200, 100, 40)))
        {
            photo.Save(photoPath);
        }

        // An icon: a black square of 8 pixels in a transparent image of 16
        var iconPath = GetPath("square.png");
        using (var icon = new Image<Rgba32>(16, 16))
        {
            icon.Draw(canvas => canvas.FillRectangle(Colors.Black, new Rectangle(4, 4, 8, 8)));
            icon.Save(iconPath);
        }

        var output = GetPath("filtered.png");
        Examples.ApplyColorFilters(photoPath, iconPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(60, 60), image.Size);
        var frame = image.Frames[0];

        // The whole photo has new colors: less vivid, so its red and its blue are nearer to each other
        var top = frame[30, 10];
        Assert.Equal(top, frame[5, 39]);
        Assert.NotEqual(new Rgba32(200, 100, 40), top);
        Assert.True(top.R > top.G && top.G > top.B);
        Assert.True(top.R - top.B < 160);

        // The lower third is half as bright: each sample is halved, halves upward
        var bottom = frame[50, 50];
        Assert.Equal(new Rgba32((byte)((top.R + 1) / 2), (byte)((top.G + 1) / 2), (byte)((top.B + 1) / 2)), bottom);
        Assert.Equal(bottom, frame[5, 40]);

        // The icon is at (8, 48): its square is gold, and what is transparent around it shows the photo
        Assert.Equal(Colors.Gold, frame[16, 56]);
        Assert.Equal(Colors.Gold, frame[12, 52]);
        Assert.Equal(bottom, frame[9, 49]);
        Assert.Equal(bottom, frame[22, 58]);
    }

    [Fact]
    public void DrawGlassPanelBlursWhatIsBehindThePanelOnly()
    {
        // A photo of stripes of one pixel: as sharp as a picture gets
        var photoPath = GetPath("stripes.png");
        using (var photo = new Image<Rgba32>(200, 120))
        {
            photo.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = (x & 1) == 0 ? new Rgba32(20, 40, 160) : new Rgba32(230, 240, 255);
                    }
                }
            });
            photo.Save(photoPath);
        }

        var output = GetPath("glass.png");
        Examples.DrawGlassPanel(photoPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(200, 120), image.Size);
        var frame = image.Frames[0];

        // Outside the panel, the photo is untouched
        Assert.Equal(new Rgba32(20, 40, 160), frame[100, 110]);
        Assert.Equal(new Rgba32(230, 240, 255), frame[101, 110]);
        Assert.Equal(new Rgba32(20, 40, 160), frame[16, 16]);

        // Inside, the stripes are blurred into one color, lightened by the glass
        Assert.InRange(Math.Abs(frame[120, 40].R - frame[121, 40].R), 0, 2);
        Assert.InRange(frame[120, 40].R, 140, 190);

        // The light is the brightest at its center, and still shows 10 pixels away
        Assert.True(frame[46, 40].R > frame[120, 40].R + 20);
        Assert.True(frame[46, 40].B < frame[120, 40].B);
        Assert.True(frame[58, 40].R > frame[120, 40].R);

        // The level of the gauge is green at its left and gone at its right, where the track shows
        var left = frame[40, 72];
        var right = frame[164, 72];
        Assert.True(left.G > left.R + 40);
        Assert.InRange(right.R, 225, 245);
        Assert.InRange(right.G - right.R, 0, 4);

        // The shadow of the edge falls inside the track, from its top: its first rows are darker
        Assert.True(frame[164, 63].R < right.R - 20);
    }

    [Fact]
    public void DrawTiltedCardShowsTheSideThatComesCloserLarger()
    {
        var output = GetPath("tilted.png");
        Examples.DrawTiltedCard(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(240, 160), image.Size);
        var frame = image.Frames[0];

        // Turned by 40 degrees, the card is 120 cos(40) = 92 pixels wide in the plane of the image, and the perspective
        // moves its sides: the left one comes toward the viewer (from 74 to 67), the right one goes away (from 166 to
        // 161). The left side is taller than the right one.
        Assert.True(IsCard(frame[69, 80]));
        Assert.False(IsCard(frame[64, 80]));
        Assert.True(IsCard(frame[158, 80]));
        Assert.False(IsCard(frame[163, 80]));
        Assert.True(Height(frame, 70) > Height(frame, 150) + 15);

        static bool IsCard(Rgba32 pixel) => pixel.B > pixel.R + 40;

        static int Height(ImageFrame<Rgba32> frame, int x)
        {
            var count = 0;
            for (var y = 0; y < frame.Height; y++)
            {
                count += IsCard(frame[x, y]) ? 1 : 0;
            }

            return count;
        }
    }

    [Fact]
    public void DrawGlowingRingGlowsAroundTheRingAndNotFarFromIt()
    {
        var output = GetPath("glow.png");
        Examples.DrawGlowingRing(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // The ring is white over its glow; 10 pixels outside it, the glow is blue; at the center, the background
        Assert.Equal(new Rgba32(255, 255, 255), frame[80, 40]);
        Assert.True(frame[80, 30].B > 80 && frame[80, 30].G > frame[80, 30].R + 20, $"{frame[80, 30]}");
        Assert.Equal(new Rgba32(16, 20, 40), frame[80, 80]);
        Assert.Equal(new Rgba32(16, 20, 40), frame[2, 2]);
    }

    [Fact]
    public void DrawFrostedCardBlursWhatIsBehindTheCardAndShadowsTheBadge()
    {
        // A photo of stripes of one pixel
        var photoPath = GetPath("stripes-card.png");
        using (var photo = new Image<Rgba32>(200, 130))
        {
            photo.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = (x & 1) == 0 ? new Rgba32(20, 40, 160) : new Rgba32(230, 240, 255);
                    }
                }
            });
            photo.Save(photoPath);
        }

        var output = GetPath("frosted.png");
        Examples.DrawFrostedCard(photoPath, output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Outside the card, the stripes are untouched; inside, they are one color, from its edge to its middle
        Assert.Equal(new Rgba32(20, 40, 160), frame[10, 10]);
        Assert.Equal(new Rgba32(230, 240, 255), frame[11, 10]);
        Assert.InRange(Math.Abs(frame[80, 60].R - frame[81, 60].R), 0, 2);
        Assert.InRange(Math.Abs(frame[30, 60].B - frame[31, 60].B), 0, 3);

        // The badge is gold, and its shadow darkens the card below it, more than above it
        Assert.Equal(Colors.Gold, frame[146, 74]);
        Assert.True(frame[146, 92].R < frame[146, 56].R - 10, $"{frame[146, 92]} {frame[146, 56]}");
    }

    [Fact]
    public void DrawOnScreenDrawsTheScreenshotBetweenItsFourCorners()
    {
        var photoPath = GetPath("desk.png");
        using (var photo = new Image<Rgba32>(220, 180, new Rgba32(90, 80, 70)))
        {
            photo.Save(photoPath);
        }

        // A screenshot of four quarters, each of a color
        var screenshotPath = GetPath("screenshot.png");
        Rgba32[] quarters = [Colors.Crimson, Colors.Gold, Colors.SeaGreen, Colors.SteelBlue];
        using (var screenshot = new Image<Rgba32>(640, 400))
        {
            screenshot.Draw(canvas =>
            {
                canvas.FillRectangle(quarters[0], new Rectangle(0, 0, 320, 200));
                canvas.FillRectangle(quarters[1], new Rectangle(320, 0, 320, 200));
                canvas.FillRectangle(quarters[2], new Rectangle(320, 200, 320, 200));
                canvas.FillRectangle(quarters[3], new Rectangle(0, 200, 320, 200));
            });
            screenshot.Save(screenshotPath);
        }

        var output = GetPath("screen.png");
        Examples.DrawOnScreen(photoPath, screenshotPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(220, 180), image.Size);
        var frame = image.Frames[0];

        // Each quarter is next to its corner of the quadrilateral, and the photo is untouched around it
        Assert.Equal(quarters[0], frame[46, 38]);
        Assert.Equal(quarters[1], frame[172, 50]);
        Assert.Equal(quarters[2], frame[170, 126]);
        Assert.Equal(quarters[3], frame[42, 140]);
        Assert.Equal(new Rgba32(90, 80, 70), frame[30, 30]);
        Assert.Equal(new Rgba32(90, 80, 70), frame[186, 140]);
        Assert.Equal(new Rgba32(90, 80, 70), frame[110, 34]);
    }

    [Fact]
    public void DrawSpriteKeepsTheColorsOfItsShapesAndMixesItsLineAsLight()
    {
        var output = GetPath("sprite.png");
        Examples.DrawSprite(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Under the line, every pixel is one of the three colors of the sprite
        for (var y = 8; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                Assert.Contains(frame[x, y], new[] { Colors.Black, Colors.Gold, Colors.Crimson });
            }
        }

        Assert.Equal(Colors.Gold, frame[16, 24]);
        Assert.Equal(Colors.Crimson, frame[16, 16]);

        // The line covers a part of two rows of each column: in linear light, the two grays add up to more than white
        var pair = frame[16, 3].R + frame[16, 4].R;
        Assert.InRange(frame[16, 3].R, 1, 254);
        Assert.True(pair > 300, $"{frame[16, 3].R} + {frame[16, 4].R}");
    }

    [Fact]
    public void DrawBadgeDrawsACardItsShadowAndAFadedMark()
    {
        var output = GetPath("badge.png");
        Examples.DrawBadge(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(220, 120), image.Size);
        var frame = image.Frames[0];

        // The background, far from the card and from its shadow
        Assert.Equal(Colors.WhiteSmoke, frame[4, 4]);
        Assert.Equal(Colors.WhiteSmoke, frame[215, 60]);

        // The card is blue, lighter at its top (the gradient, and the light over it) than at its bottom
        var top = frame[60, 30];
        var bottom = frame[60, 84];
        Assert.True(top.B > top.R && bottom.B > bottom.R);
        Assert.True(top.R + top.G + top.B > bottom.R + bottom.G + bottom.B);

        // The shadow is under the card, 6 pixels lower: it shows below it, darker than the background and gray
        var shadow = frame[110, 96];
        Assert.Equal(shadow.R, shadow.G);
        Assert.Equal(shadow.G, shadow.B);
        Assert.InRange(shadow.R, 150, 235);
        Assert.Equal(Colors.WhiteSmoke, frame[110, 118]);

        // Its bottom-right corner is nearly square, and the others are rounded: the card reaches that corner only
        Assert.True(frame[187, 85].B > frame[187, 85].R + 40);
        Assert.False(frame[32, 26].B > frame[32, 26].R + 40);

        // The two disks are faded as one shape: where they overlap, the mark is what it is elsewhere
        Assert.Equal(frame[90, 56], frame[107, 56]);
        Assert.Equal(frame[90, 56], frame[124, 56]);
        Assert.True(frame[107, 56].R > frame[140, 56].R);
    }

    [Fact]
    public void DrawWatermarkTurnsATextOverAnImage()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var input = GetPath("photo.png");
        var output = GetPath("watermarked.png");
        using (var photo = new Image<Rgba32>(300, 200, new Rgba32(20, 60, 120)))
        {
            photo.Save(input);
        }

        Examples.DrawWatermark(fontPath, input, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(300, 200), image.Size);
        var frame = image.Frames[0];

        // The ink lightens the pixels it covers, and leaves the others: the corners the turned text does not reach
        var background = new Rgba32(20, 60, 120);
        Assert.Equal(background, frame[2, 2]);
        Assert.Equal(background, frame[297, 197]);
        var inked = 0;
        long sumX = 0;
        long sumY = 0;
        long lowerLeft = 0;
        long upperLeft = 0;
        for (var y = 0; y < 200; y++)
        {
            for (var x = 0; x < 300; x++)
            {
                if (frame[x, y] == background)
                    continue;

                inked++;
                sumX += x;
                sumY += y;

                // Under or over the diagonal of the image that goes from its bottom-left corner to its top-right one
                var side = ((x - 150) * 2) + ((y - 100) * 3);
                lowerLeft += side < -150 ? 1 : 0;
                upperLeft += side > 150 ? 1 : 0;
            }
        }

        // The three lines are around the center, and the text climbs to the right
        Assert.InRange(inked, 3000, 30000);
        Assert.InRange(sumX / inked, 135, 165);
        Assert.InRange(sumY / inked, 85, 115);
        Assert.True(frame[150, 100].R >= background.R);
        Assert.True(lowerLeft > 0 && upperLeft > 0);
    }

    [Fact]
    public void DrawBrandColorsConvertsThePaintsToTheProfileOfTheImage()
    {
        // A photograph with the profile of a wider color space
        var profile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var photoPath = GetPath("wide.png");
        using (var photo = new Image<Rgb24>(120, 60, new Rgb24(10, 120, 60)))
        {
            photo.Metadata.IccProfile = profile;
            photo.Save(photoPath);
        }

        var output = GetPath("brand.png");
        Examples.DrawBrandColors(photoPath, output);

        using var image = Image.Load<Rgb24>(output);
        Assert.NotNull(image.Metadata.IccProfile);
        Assert.Equal(profile.Data, image.Metadata.IccProfile.Data);

        // Inside the banner, the samples are those that show the sRGB color #E63946 in the color space of the image:
        // less saturated samples than (230, 57, 70), since the primaries of the image are more saturated
        byte[] expected = [0xE6, 0x39, 0x46];
        IccColorTransform.Create(IccProfile.Srgb, profile).Convert(expected, expected);
        var banner = image.Frames[0][50, 20];
        Assert.InRange(banner.R, expected[0] - 1, expected[0] + 1);
        Assert.InRange(banner.G, expected[1] - 1, expected[1] + 1);
        Assert.InRange(banner.B, expected[2] - 1, expected[2] + 1);
        Assert.True(banner.R < 0xE6 && banner.G > 0x39);

        // White is the white of both color spaces, and the photograph is untouched around the banner
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][50, 8]);
        Assert.Equal(new Rgb24(10, 120, 60), image.Frames[0][110, 50]);
    }

    [Fact]
    public void DrawStickerGrowsAndShrinksTheOutlineOfAStar()
    {
        var output = GetPath("sticker.png");
        Examples.DrawSticker(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(120, 120), image.Size);
        var frame = image.Frames[0];

        // The tip of the star is at (60, 18). Rounded, it is the arc of a circle of radius 4 whose center is at
        // 4 / sin(18 degrees) = 12.9 from the tip: the star starts at 26.9, and its border 6 before, at 20.9
        Assert.Equal(Colors.Goldenrod, frame[60, 62]);
        Assert.Equal(Colors.Goldenrod, frame[60, 29]);
        Assert.Equal(Colors.White, frame[60, 23]);
        Assert.NotEqual(Colors.White, frame[60, 18]);
        Assert.Equal(0, frame[2, 2].A);
        Assert.Equal(0, frame[117, 3].A);

        // The shadow of the border is around it and under it: black, and translucent. The seam is a lighter line
        // inside the star.
        var shadow = 0;
        var lighter = 0;
        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 120; x++)
            {
                var pixel = frame[x, y];
                if (pixel is { R: 0, G: 0, B: 0, A: > 0 and <= 90 })
                {
                    shadow++;
                }
                else if (pixel.A == 255 && pixel != Colors.White && pixel != Colors.Goldenrod && pixel.B > Colors.Goldenrod.B)
                {
                    lighter++;
                }
            }
        }

        Assert.InRange(shadow, 200, 6000);
        Assert.InRange(lighter, 80, 2000);
    }

    [Fact]
    public void DrawRoutePlacesItsMarksAlongThePath()
    {
        var output = GetPath("route.png");
        Examples.DrawRoute(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(200, 120), image.Size);
        var frame = image.Frames[0];
        var route = new Drawing.VectorPathBuilder().MoveTo(20, 100).CubicBezierTo(60, 10, 120, 110, 176, 34).Build();
        var length = route.GetLength();
        Assert.InRange(length, 170, 260);

        // A milestone is a disk of radius 4 around each quarter of the way
        for (var i = 1; i <= 3; i++)
        {
            Assert.True(route.TryGetPointAtDistance(length * i / 4, out var milestone, out _));
            Assert.Equal(Colors.Crimson, frame[(int)milestone.X, (int)milestone.Y]);
        }

        // The arrow head is behind the end of the route, in its direction
        Assert.True(route.TryGetPointAtDistance(length, out var tip, out var direction));
        Assert.Equal(new PointD(176, 34), tip);
        var inside = tip - (direction * 6);
        Assert.Equal(Colors.SteelBlue, frame[(int)inside.X, (int)inside.Y]);
        var ahead = tip + (direction * 3);
        Assert.Equal(Colors.White, frame[(int)ahead.X, (int)ahead.Y]);
        Assert.Equal(Colors.White, frame[5, 5]);
    }

    [Fact]
    public void DrawLandscapePaintsShapesOverEachOther()
    {
        var output = GetPath("landscape.png");
        Examples.DrawLandscape(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(256, 160), image.Size);
        var frame = image.Frames[0];

        // The sky gradient made every pixel opaque
        frame.ProcessPixelRows(static pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    Assert.Equal(255, pixel.A);
                }
            }
        });

        // Inside the hill, away from its outline: the solid color exactly
        Assert.Equal(new Rgba32(60, 140, 60), frame[10, 150]);
        Assert.Equal(new Rgba32(60, 140, 60), frame[200, 140]);

        // The sky gets lighter toward the bottom, and is the same along a row
        Assert.True(frame[100, 10].B < frame[100, 60].B);
        Assert.Equal(frame[40, 30], frame[120, 30]);

        // The sun is nearly opaque at its center and fades into the sky; outside its radius the sky is untouched
        Assert.True(frame[200, 50].R > 240 && frame[200, 50].G > 240);
        Assert.True(frame[215, 50].R > frame[120, 50].R && frame[215, 50].R < frame[200, 50].R);
        Assert.Equal(frame[120, 50], frame[240, 50]);

        // The frame is a translucent black line from 0 to 2 around the canvas: darker than the pixels next to it
        Assert.True(frame[0, 60].B < frame[3, 60].B);
        Assert.Equal(frame[0, 60], frame[1, 60]);
        Assert.True(frame[100, 159].G < frame[100, 157].G);
    }

    [Fact]
    public void MarkAnimationDrawsOnEveryFrame()
    {
        var (apngPath, _) = BuildAnimation();
        var output = GetPath("marked.png");
        Examples.MarkAnimation(apngPath, output);

        using var marked = Image.Load<Rgba32>(output);
        Assert.Equal(2, marked.Frames.Count);
        Assert.Equal(new FrameDuration(1, 30), marked.Frames[1].Metadata.Duration);

        // The horizontal line, 4 pixels wide across the center: yellow at 200/255 over red, then over blue.
        //   over red: green = 255 * 200 / 255 = 200 | over blue: red = green = 200, blue = 255 * 55 / 255 = 55
        Assert.Equal(new Rgba32(255, 200, 0), marked.Frames[0][32, 32]);
        Assert.Equal(new Rgba32(200, 200, 55), marked.Frames[1][32, 32]);
        Assert.Equal(new Rgba32(200, 200, 55), marked.Frames[1][20, 30]);

        // Away from the circle and the line, the frames are untouched
        Assert.Equal(Red, marked.Frames[0][2, 2]);
        Assert.Equal(Blue, marked.Frames[1][32, 24]);
        Assert.Equal(Blue, marked.Frames[1][63, 63]);

        // The circle of radius 16 crosses the vertical through the center at 16 and 48
        Assert.Equal(new Rgba32(200, 200, 55), marked.Frames[1][32, 16]);
        Assert.Equal(new Rgba32(200, 200, 55), marked.Frames[1][31, 47]);
    }

    [Fact]
    public void LookTheSameAcceptsInvisibleChangesAndRejectsVisibleOnes()
    {
        // A white image with a black square, in PNG and in 16-bit TIFF: the same pixels in two formats
        var expected = GetPath("expected.png");
        var sameInTiff = GetPath("same.tiff");
        using (var image = new Image<Rgba32>(64, 64, Colors.White))
        {
            image.Draw(canvas => canvas.FillRectangle(Colors.Black, new Rectangle(16, 16, 32, 32)));
            image.Save(expected);
            using var wide = image.CloneAs<Rgba64>();
            wide.Save(sameInTiff);
        }

        // One pixel one level darker: invisible
        var invisible = GetPath("invisible.png");
        using (var image = Image.Load<Rgba32>(expected))
        {
            image.Frames[0][2, 2] = new Rgba32(254, 255, 255);
            image.Save(invisible);
        }

        // The square moved by 8 pixels
        var moved = GetPath("moved.png");
        using (var image = new Image<Rgba32>(64, 64, Colors.White))
        {
            image.Draw(canvas => canvas.FillRectangle(Colors.Black, new Rectangle(24, 16, 32, 32)));
            image.Save(moved);
        }

        var smaller = GetPath("smaller.png");
        using (var image = new Image<Rgba32>(32, 32, Colors.White))
        {
            image.Save(smaller);
        }

        Assert.True(Examples.LookTheSame(expected, sameInTiff));
        Assert.True(Examples.LookTheSame(expected, invisible));
        Assert.False(Examples.LookTheSame(expected, moved));
        Assert.False(Examples.LookTheSame(expected, smaller));
    }

    [Fact]
    public void StraightenTurnsTheScanAndFillsTheCornersWithWhite()
    {
        var input = GetPath("scan.jpg");
        using (var source = new Image<Rgb24>(40, 30, new Rgb24(0, 0, 255)))
        {
            source.Save(input);
        }

        var output = GetPath("straight.png");
        Examples.Straighten(input, output, -10);

        // 40 cos 10 + 30 sin 10 = 44.6 and 40 sin 10 + 30 cos 10 = 36.5, rounded up; the corners are white
        using var straight = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(45, 37), straight.Size);
        Assert.Equal(new Rgb24(255, 255, 255), straight.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), straight.Frames[0][44, 36]);
        var center = straight.Frames[0][22, 18];
        Assert.True(center.R < 8 && center.G < 8 && center.B > 247, center.ToString());
    }

    [Fact]
    public void FlattenPageMapsTheCornersOfThePageToTheCanvas()
    {
        // A photo whose page is the blue 20 x 20 square at (10, 5), on a red table
        var input = GetPath("photo.png");
        using (var source = new Image<Rgba32>(40, 30, Red))
        {
            for (var y = 5; y < 25; y++)
            {
                for (var x = 10; x < 30; x++)
                {
                    source.Frames[0][x, y] = Blue;
                }
            }

            source.Save(input);
        }

        var output = GetPath("page.png");
        Examples.FlattenPage(input, output, new PointD(10, 5), new PointD(30, 5), new PointD(30, 25), new PointD(10, 25), new Size(40, 40));

        // The page is enlarged twice: away from its edges, where the kernel reads the table, it is blue
        using var page = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(40, 40), page.Size);
        Assert.Equal(Blue, page.Frames[0][5, 5]);
        Assert.Equal(Blue, page.Frames[0][20, 20]);
        Assert.Equal(Blue, page.Frames[0][34, 34]);
    }

    [Fact]
    public void TrimBackgroundCropsToTheContentAndKeepsTheMargin()
    {
        // A 40x30 white image with a 10x6 red block at (2, 12)
        var white = new Rgba32(255, 255, 255);
        var input = GetPath("bordered.png");
        using (var source = new Image<Rgba32>(40, 30, white))
        {
            for (var y = 12; y < 18; y++)
            {
                for (var x = 2; x < 12; x++)
                {
                    source.Frames[0][x, y] = Red;
                }
            }

            source.Save(input);
        }

        var output = GetPath("trimmed.png");
        Assert.True(Examples.TrimBackground(input, output));

        // 8 pixels on each side of the block: (2 - 8, 12 - 8, 10 + 16, 6 + 16). The 6 columns left of the original canvas
        // are new and get the background color
        using var trimmed = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(26, 22), trimmed.Size);
        Assert.Equal(Red, trimmed.Frames[0][8, 8]);
        Assert.Equal(Red, trimmed.Frames[0][17, 13]);
        Assert.Equal(white, trimmed.Frames[0][7, 8]);
        Assert.Equal(white, trimmed.Frames[0][18, 13]);
        Assert.Equal(white, trimmed.Frames[0][8, 7]);
        Assert.Equal(white, trimmed.Frames[0][17, 14]);
        Assert.Equal(white, trimmed.Frames[0][0, 0]);
        Assert.Equal(white, trimmed.Frames[0][25, 21]);

        // A uniform image has no content: nothing is written
        var blank = GetPath("blank.png");
        using (var source = new Image<Rgba32>(8, 8, white))
        {
            source.Save(blank);
        }

        var unused = GetPath("unused.png");
        Assert.False(Examples.TrimBackground(blank, unused));
        Assert.False(File.Exists(unused));
    }

    [Fact]
    public void SharpenAndDetectEdgesApplyTheMatricesAsWritten()
    {
        // A 5x5 opaque gray image (100) with a brighter pixel (200) in the middle
        var input = GetPath("dot.png");
        using (var source = new Image<Rgba32>(5, 5, new Rgba32(100, 100, 100)))
        {
            source.Frames[0][2, 2] = new Rgba32(200, 200, 200);
            source.Save(input);
        }

        var sharpenedPath = GetPath("sharpened.png");
        var edgesPath = GetPath("edges.png");
        Examples.SharpenAndDetectEdges(input, sharpenedPath, edgesPath);

        // Sharpen (5 in the middle, -1 on the four sides): the dot is 5 * 200 - 4 * 100 = 600 -> 255, its side neighbors
        // 5 * 100 - 3 * 100 - 200 = 0, and flat areas keep their value (5 * 100 - 4 * 100)
        using var sharpened = Image.Load<Rgba32>(sharpenedPath);
        Assert.Equal(new Rgba32(255, 255, 255), sharpened.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(0, 0, 0), sharpened.Frames[0][1, 2]);
        Assert.Equal(new Rgba32(0, 0, 0), sharpened.Frames[0][2, 3]);
        Assert.Equal(new Rgba32(100, 100, 100), sharpened.Frames[0][1, 1]);
        Assert.Equal(new Rgba32(100, 100, 100), sharpened.Frames[0][0, 0]);

        // Laplacian (-4 in the middle, 1 on the four sides) plus 127.5: the dot is 4 * 100 - 4 * 200 + 127.5 -> 0, its side
        // neighbors 3 * 100 + 200 - 4 * 100 + 127.5 = 227.5 -> 228, and flat areas 127.5 -> 128; alpha is kept
        using var edges = Image.Load<Rgba32>(edgesPath);
        Assert.Equal(new Rgba32(0, 0, 0), edges.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(228, 228, 228), edges.Frames[0][1, 2]);
        Assert.Equal(new Rgba32(228, 228, 228), edges.Frames[0][2, 3]);
        Assert.Equal(new Rgba32(128, 128, 128), edges.Frames[0][1, 1]);
        Assert.Equal(new Rgba32(128, 128, 128), edges.Frames[0][0, 0]);
    }

    [Fact]
    public void DetectEdgesFindsTheEdgesOfTheBrightness()
    {
        // A 5x5 opaque image, 10 on the right of column 2 and 0 on its left
        var input = GetPath("step.png");
        using (var source = new Image<Rgba32>(5, 5, new Rgba32(0, 0, 0)))
        {
            for (var y = 0; y < 5; y++)
            {
                for (var x = 2; x < 5; x++)
                {
                    source.Frames[0][x, y] = new Rgba32(10, 10, 10);
                }
            }

            source.Save(input);
        }

        var edgesPath = GetPath("step-edges.png");
        Examples.DetectEdges(input, edgesPath);

        // The vertical gradient is 0 and the horizontal one (1 2 1 times right - left) is 40 on both sides of the step
        using var edges = Image.Load<Rgba32>(edgesPath);
        for (var y = 0; y < 5; y++)
        {
            Assert.Equal([new Rgba32(0, 0, 0), new Rgba32(40, 40, 40), new Rgba32(40, 40, 40), new Rgba32(0, 0, 0), new Rgba32(0, 0, 0)], Enumerable.Range(0, 5).Select(x => edges.Frames[0][x, y]).ToArray());
        }
    }

    [Fact]
    public void CleanUpScanRemovesSpecksAndSharpensEdges()
    {
        // A 9x9 opaque image: 50 on the left (columns 0 to 4), 200 on the right, and a white speck at (2, 2)
        var input = GetPath("scan.png");
        using (var source = new Image<Rgba32>(9, 9, new Rgba32(50, 50, 50)))
        {
            for (var y = 0; y < 9; y++)
            {
                for (var x = 5; x < 9; x++)
                {
                    source.Frames[0][x, y] = new Rgba32(200, 200, 200);
                }
            }

            source.Frames[0][2, 2] = new Rgba32(255, 255, 255);
            source.Save(input);
        }

        var cleanedPath = GetPath("cleaned.png");
        var backgroundPath = GetPath("background.png");
        Examples.CleanUpScan(input, cleanedPath, backgroundPath);

        // Every column is the same (the rows above and below are alike). The 11 weights of the deviation 1.5 put 0.367 of
        // the blur of column 4 on the columns at 200: 105.05, so it becomes 50 - 55.05 / 2 = 22.5, and column 5 200 + 27.5
        // = 227.5. Column 2 differs by 6.69 from its blur, over the threshold (5.1): 50 - 3.35 = 46.65. Column 1 differs
        // by 1.29 and keeps 50; the speck is gone
        using var cleaned = Image.Load<Rgba32>(cleanedPath);
        int[] expected = [50, 50, 47, 38, 22, 228, 212, 203, 200];
        for (var y = 0; y < 9; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.Equal(new Rgba32((byte)expected[x], (byte)expected[x], (byte)expected[x]), cleaned.Frames[0][x, y]);
            }
        }

        // A square of 51x51 pixels over a 9x9 image reflected about its edges reads it many times: every pixel is close
        // to its mean (about 115)
        using var background = Image.Load<Rgba32>(backgroundPath);
        for (var y = 0; y < 9; y++)
        {
            for (var x = 0; x < 9; x++)
            {
                Assert.InRange(background.Frames[0][x, y].R, 100, 130);
                Assert.Equal(255, background.Frames[0][x, y].A);
            }
        }
    }

    [Fact]
    public void InvertColorsRewritesEveryFrame()
    {
        var (apngPath, _) = BuildAnimation();

        Examples.InvertColors(apngPath);

        using var inverted = Image.Load<Rgba32>(apngPath);
        Assert.Equal(2, inverted.Frames.Count);
        Assert.Equal(new Rgba32(0, 255, 255), inverted.Frames[0][5, 5]);
        Assert.Equal(new Rgba32(255, 255, 0), inverted.Frames[1][5, 5]);
    }

    [Fact]
    public void ComputeLuminanceSumUsesExplicitState()
    {
        using var image = new Image<Gray16>(3, 2, new Gray16(1000));
        Assert.Equal(6000, Examples.ComputeLuminanceSum(image));
    }

    [Fact]
    public void ExportFrameAsJpegWritesOneOpaqueFrame()
    {
        var (_, gifPath) = BuildAnimation();
        var output = GetPath("frame.jpg");

        Examples.ExportFrameAsJpeg(gifPath, 1, output);

        using var jpeg = Image.Load(output);
        Assert.Equal(ImageFormat.Jpeg, jpeg.Metadata.SourceFormat);
        Assert.Equal(PixelFormat.Rgb24, jpeg.PixelFormat);
        Assert.Single(jpeg.Frames);
        Assert.False(jpeg.IsAnimated);
        var pixel = ((Image<Rgb24>)jpeg).Frames[0][32, 32];
        Assert.InRange(pixel.B, 240, 255); // lossy: blue within a few units
        Assert.InRange(pixel.R, 0, 15);
    }

    [Fact]
    public void SaveProgressiveJpegWritesTheSamePixelsInSeveralScans()
    {
        var source = GetPath("photo-source.png");
        using (var image = new Image<Rgb24>(40, 24))
        {
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgb24 { R = (byte)(x * 6), G = (byte)(y * 10), B = (byte)((x + y) * 4) };
                    }
                }
            });
            image.Save(source);
        }

        var output = GetPath("progressive.jpg");
        Examples.SaveProgressiveJpeg(source, output);

        // A progressive frame (SOF2) with ten scans, which decodes to the pixels of the baseline file of the same settings
        var data = File.ReadAllBytes(output);
        Assert.True(data.AsSpan().IndexOf([(byte)0xFF, (byte)0xC2, (byte)0x00, (byte)0x11, (byte)0x08]) > 0);
        Assert.Equal(-1, data.AsSpan().IndexOf([(byte)0xFF, (byte)0xC0, (byte)0x00, (byte)0x11, (byte)0x08]));
        using var progressive = Image.Load<Rgb24>(output);
        var baselinePath = GetPath("baseline.jpg");
        using (var image = Image.Load<Rgb24>(source))
        {
            image.Save(baselinePath, new JpegEncoder { Quality = 85 });
        }

        using var baseline = Image.Load<Rgb24>(baselinePath);
        Assert.Equal(new Size(40, 24), progressive.Size);
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                Assert.Equal(baseline.Frames[0][x, y], progressive.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void SaveOptimizedJpegWritesTheSamePixelsInFewerBytes()
    {
        var source = GetPath("optimized-source.png");
        using (var image = new Image<Rgb24>(40, 24))
        {
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgb24 { R = (byte)(x * 6), G = (byte)(y * 10), B = (byte)((x + y) * 4) };
                    }
                }
            });
            image.Save(source);
        }

        var output = GetPath("optimized.jpg");
        Examples.SaveOptimizedJpeg(source, output);
        var defaultPath = GetPath("default-tables.jpg");
        using (var image = Image.Load<Rgb24>(source))
        {
            image.Save(defaultPath, new JpegEncoder { Quality = 85, ChromaSubsampling = JpegChromaSubsampling.Ratio420 });
        }

        // A restart interval of 3 MCUs (one row of 16x16 MCUs), so one RST0 marker between the two rows
        var data = File.ReadAllBytes(output);
        var defaultData = File.ReadAllBytes(defaultPath);
        Assert.True(data.AsSpan().IndexOf([(byte)0xFF, (byte)0xDD, (byte)0x00, (byte)0x04, (byte)0x00, (byte)0x03]) > 0);
        Assert.True(data.AsSpan().IndexOf([(byte)0xFF, (byte)0xD0]) > 0);
        Assert.Equal(-1, defaultData.AsSpan().IndexOf([(byte)0xFF, (byte)0xDD]));
        Assert.HasCountLessThan(defaultData.Length, data);

        using var optimized = Image.Load<Rgb24>(output);
        using var typical = Image.Load<Rgb24>(defaultPath);
        Assert.Equal(new Size(40, 24), optimized.Size);
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 40; x++)
            {
                Assert.Equal(typical.Frames[0][x, y], optimized.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void SaveWebPWritesALossyPhotoAndALosslessAnimation()
    {
        var (apngPath, _) = BuildAnimation();
        var photo = GetPath("photo.png");
        using (var source = new Image<Rgba64>(32, 16, new Rgba64(65535, 32768, 0, 65535)))
        {
            source.Save(photo);
        }

        var webp = GetPath("photo.webp");
        var animated = GetPath("animation.webp");
        Examples.SaveWebP(photo, webp, apngPath, animated);

        using (var image = Image.Load(webp))
        {
            Assert.Equal(ImageFormat.WebP, image.Metadata.SourceFormat);
            Assert.Equal(PixelFormat.Rgb24, image.PixelFormat); // opaque lossy image
            var pixel = ((Image<Rgb24>)image).Frames[0][16, 8];
            Assert.InRange(pixel.R, 245, 255);
            Assert.InRange(pixel.G, 118, 138);
            Assert.InRange(pixel.B, 0, 10);
        }

        using var animation = Image.Load<Rgba32>(animated);
        using var expected = Image.Load<Rgba32>(apngPath);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(3, animation.Animation!.TotalPlays);
        Assert.Equal(new FrameDuration(33, 1000), animation.Frames[1].Metadata.Duration); // nearest millisecond
        Assert.Equal(expected.Frames[1][63, 63], animation.Frames[1][63, 63]); // lossless
    }

    [Fact]
    public void SaveAvifWritesALossyPhotoALosslessImageAndAnImageSequence()
    {
        var (apngPath, _) = BuildAnimation();
        var photo = GetPath("photo.png");
        using (var source = new Image<Rgba64>(32, 16, new Rgba64(65535, 32768, 0, 65535)))
        {
            source.Save(photo);
        }

        var avif = GetPath("photo.avif");
        var lossless = GetPath("lossless.avif");
        var animated = GetPath("animation.avif");
        Examples.SaveAvif(photo, avif, lossless, apngPath, animated);

        using (var image = Image.Load<Rgba64>(avif))
        {
            // A 12-bit lossy image: the samples are within the error of the quantizer
            Assert.Equal(ImageFormat.Avif, image.Metadata.SourceFormat);
            Assert.Equal(12, Image.Identify(avif).BitsPerComponent);
            var pixel = image.Frames[0][16, 8];
            Assert.InRange(pixel.R, 63000, 65535);
            Assert.InRange(pixel.G, 30500, 35000);
            Assert.InRange(pixel.B, 0, 2500);
        }

        using (var image = Image.Load<Rgba64>(lossless))
        {
            // 16-bit samples rounded to 12 bits: 65535 is 4095, and 32768 is 2048 (which is decoded as 32776)
            Assert.Equal(new Rgba64(65535, 32776, 0, 65535), image.Frames[0][16, 8]);
        }

        using var animation = Image.Load<Rgba32>(animated);
        using var expected = Image.Load<Rgba32>(apngPath);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(3, animation.Animation!.TotalPlays);
        Assert.Equal(expected.Frames[1].Metadata.Duration, animation.Frames[1].Metadata.Duration); // exact
        Assert.Equal(expected.Frames[1][63, 63], animation.Frames[1][63, 63]); // lossless
    }

    [Fact]
    public void ConvertOpenExrKeepsTheHighlightsAndToneMapsThem()
    {
        var exr = GetPath("highlights.exr");
        using (var source = new Image<RgbaHalf>(8, 4, new RgbaHalf((Half)0.25f, (Half)0.5f, (Half)1f)))
        {
            source.Frames[0][3, 2] = new RgbaHalf((Half)12f, (Half)6f, (Half)3f);
            source.Save(exr);
        }

        var piz = GetPath("highlights-piz.exr");
        using var toneMapped = Examples.ConvertOpenExr(exr, piz);
        using (var copy = Image.Load<RgbaHalf>(piz))
        {
            Assert.Equal(new RgbaHalf((Half)12f, (Half)6f, (Half)3f), copy.Frames[0][3, 2]);
        }

        // The highlight is the white of the tone mapping, and its hue is kept
        var pixel = toneMapped.Frames[0][3, 2];
        Assert.Equal(255, pixel.R);
        Assert.InRange(pixel.G, 120, 135);
        Assert.True(toneMapped.Frames[0][0, 0].B < 255);
    }

    [Fact]
    public void SaveJpeg2000WritesALosslessFileAndALossyCodestream()
    {
        var photo = GetPath("photo16-resolution.png");
        var color = new Rgba64(65535, 32768, 1, 32767);
        using (var source = new Image<Rgba64>(32, 16, color))
        {
            source.Metadata.Resolution = new ImageResolution(300, 300);
            source.Save(photo);
        }

        var jp2 = GetPath("photo.jp2");
        var codestream = GetPath("photo.j2k");
        Assert.Equal("Jpeg2000 32x16 Rgba64, 16 bits", Examples.SaveJpeg2000(photo, jp2, codestream));
        using (var image = Image.Load<Rgba64>(jp2))
        {
            Assert.Equal(ImageFormat.Jpeg2000, image.Metadata.SourceFormat);
            Assert.Equal(color, image.Frames[0][16, 8]); // 16 bits, lossless

            // The resolution of the PNG file, a whole number of pixels per meter, is stored exactly
            Assert.Equal(new ImageResolution(11811 * 0.0254, 11811 * 0.0254), image.Metadata.Resolution);
        }

        // The codestream has no box for the resolution; its opacity is exact and its colors are close
        using var lossy = Image.Load<Rgba64>(codestream);
        Assert.Null(lossy.Metadata.Resolution);
        var pixel = lossy.Frames[0][16, 8];
        Assert.Equal(color.A, pixel.A);
        Assert.InRange(Math.Abs(pixel.R - color.R), 0, 1024);
        Assert.InRange(Math.Abs(pixel.G - color.G), 0, 1024);
        Assert.InRange(Math.Abs(pixel.B - color.B), 0, 1024);
    }

    [Fact]
    public void SaveJpeg2000LayersFitsTheTargetAndKeepsTheLastLayerLossless()
    {
        var input = GetPath("pattern.png");
        var pixels = new Rgb24[640 * 480];
        for (var i = 0; i < pixels.Length; i++)
        {
            var (x, y) = (i % 640, i / 640);
            pixels[i] = new Rgb24((byte)(x / 3), (byte)((x * y) >> 9), (byte)((x ^ y) & 0xFF));
        }

        using (var source = Image.ImportPixelData<Rgb24>(pixels, 640, 480))
        {
            source.Save(input);
        }

        var small = GetPath("small.jp2");
        var layered = GetPath("layered.jp2");
        var (smallLength, layeredLength) = Examples.SaveJpeg2000Layers(input, small, layered);
        Assert.InRange(smallLength, 38_000, 40_000);
        Assert.Equal(new FileInfo(layered).Length, layeredLength);

        using var decoded = Image.Load<Rgb24>(layered);
        var decodedPixels = new Rgb24[pixels.Length];
        decoded.Frames[0].CopyPixelDataTo(decodedPixels);
        Assert.Equal(pixels, decodedPixels);
    }

    [Fact]
    public void LoadJpeg2000PartsDecodesAThumbnailAPreviewAndAViewport()
    {
        var path = GetPath("tiled.jp2");
        var pixels = new Rgb24[1024 * 768];
        for (var i = 0; i < pixels.Length; i++)
        {
            var (x, y) = (i % 1024, i / 1024);
            pixels[i] = new Rgb24((byte)(x / 4), (byte)(y / 3), (byte)((x ^ y) & 0xFF));
        }

        using (var source = Image.ImportPixelData<Rgb24>(pixels, 1024, 768))
        {
            source.Save(path, new Jpeg2000Encoder { TileWidth = 256, TileHeight = 256 });
        }

        var (thumbnail, preview, viewport) = Examples.LoadJpeg2000Parts(path);
        using (thumbnail)
        using (preview)
        using (viewport)
        {
            Assert.Equal((256, 192), (thumbnail.Width, thumbnail.Height));

            // 1/8 would give 128x96, smaller than the target
            Assert.Equal((256, 192), (preview.Width, preview.Height));

            // The pixels of the area, exactly (the file is lossless)
            Assert.Equal((400, 300), (viewport.Width, viewport.Height));
            using var area = viewport.CloneAs<Rgb24>();
            Assert.Equal(pixels[(200 * 1024) + 300], area.Frames[0][0, 0]);
            Assert.Equal(pixels[(499 * 1024) + 699], area.Frames[0][399, 299]);
        }
    }

    [Fact]
    public void SaveHeicWritesALossyPhotoALosslessImageAndAnImageSequence()
    {
        var (apngPath, _) = BuildAnimation();
        var photo = GetPath("photo.png");
        using (var source = new Image<Rgba64>(32, 16, new Rgba64(65535, 32768, 0, 65535)))
        {
            source.Save(photo);
        }

        var heic = GetPath("photo.heic");
        var lossless = GetPath("lossless.heic");
        var animated = GetPath("animation.heics");
        Examples.SaveHeic(photo, heic, lossless, apngPath, animated);

        using (var image = Image.Load<Rgba64>(heic))
        {
            // A 12-bit lossy image: the samples are within the error of the quantizer
            Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
            Assert.Equal(12, Image.Identify(heic).BitsPerComponent);
            var pixel = image.Frames[0][16, 8];
            Assert.InRange(pixel.R, 63000, 65535);
            Assert.InRange(pixel.G, 30500, 35000);
            Assert.InRange(pixel.B, 0, 2500);
        }

        using (var image = Image.Load<Rgba64>(lossless))
        {
            // 16-bit samples rounded to 12 bits: 65535 is 4095, and 32768 is 2048 (which is decoded as 32776)
            Assert.Equal(ImageFormat.Heic, image.Metadata.SourceFormat);
            Assert.Equal(new Rgba64(65535, 32776, 0, 65535), image.Frames[0][16, 8]);
        }

        // A lossy sequence: the frames, their exact durations and the plays of the animation
        using var animation = Image.Load<Rgba32>(animated);
        using var expected = Image.Load<Rgba32>(apngPath);
        Assert.Equal(ImageFormat.Heic, animation.Metadata.SourceFormat);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(3, animation.Animation!.TotalPlays);
        Assert.Equal(expected.Frames[1].Metadata.Duration, animation.Frames[1].Metadata.Duration);
        var (reference, actual) = (expected.Frames[1][63, 63], animation.Frames[1][63, 63]);
        Assert.InRange(actual.R, reference.R - 24, reference.R + 24);
        Assert.InRange(actual.G, reference.G - 24, reference.G + 24);
        Assert.InRange(actual.B, reference.B - 24, reference.B + 24);
    }

    [Fact]
    public void SaveJpegXlWritesLosslessFilesAndAnimations()
    {
        var (apngPath, _) = BuildAnimation();
        var photo = GetPath("photo16.png");
        var color = new Rgba64(65535, 32768, 1, 32767);
        using (var source = new Image<Rgba64>(32, 16, color))
        {
            source.Save(photo);
        }

        var jxl = GetPath("photo.jxl");
        var lossy = GetPath("photo-lossy.jxl");
        var animated = GetPath("animation.jxl");
        Assert.Equal("JpegXl 32x16 Rgba64, 16 bits", Examples.SaveJpegXl(photo, jxl, lossy, apngPath, animated));
        using (var image = Image.Load<Rgba64>(jxl))
        {
            Assert.Equal(ImageFormat.JpegXl, image.Metadata.SourceFormat);
            Assert.Equal(color, image.Frames[0][16, 8]); // 16 bits, lossless
        }

        using (var image = Image.Load<Rgba64>(lossy))
        {
            // Lossy colors are close, and the alpha channel is exact
            var pixel = image.Frames[0][16, 8];
            Assert.Equal(color.A, pixel.A);
            Assert.True(Math.Abs(pixel.R - color.R) < 2048 && Math.Abs(pixel.G - color.G) < 2048 && Math.Abs(pixel.B - color.B) < 2048, pixel.ToString());
        }

        using var animation = Image.Load<Rgba32>(animated);
        using var expected = Image.Load<Rgba32>(apngPath);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(3, animation.Animation!.TotalPlays);
        Assert.Equal(new FrameDuration(33, 1000), animation.Frames[1].Metadata.Duration); // nearest millisecond
        Assert.Equal(expected.Frames[1][63, 63], animation.Frames[1][63, 63]);
    }

    [Fact]
    public void ConvertHeicTurnsThePhotoUprightAndSavesItAsJpeg()
    {
        // The layout of the photos of a phone: a grid of tiles, a rotation property, an ICC profile and an EXIF block
        var fixture = GoldenCorpus.Default.Get("heic/phone-grid-420");
        var jpeg = GetPath("photo.jpg");
        Assert.Equal("64x24 Rgb24, orientation: RightTop", Examples.ConvertHeic(fixture.InputPath, jpeg));

        using var source = Image.Load<Rgb24>(fixture.InputPath);
        using var converted = Image.Load<Rgb24>(jpeg);
        Assert.Equal(ImageFormat.Jpeg, converted.Metadata.SourceFormat);
        Assert.Equal((24, 64), (converted.Width, converted.Height));
        Assert.Equal(ExifOrientation.TopLeft, converted.Metadata.Orientation);
        Assert.NotNull(source.Metadata.IccProfile);
        Assert.Equal(source.Metadata.IccProfile.Data.ToArray(), converted.Metadata.IccProfile?.Data.ToArray());
        Assert.NotNull(converted.Metadata.ExifProfile);

        // The row 0 of the stored image is the right column of the upright one
        var (stored, upright) = (source.Frames[0][10, 0], converted.Frames[0][23, 10]);
        Assert.InRange(upright.R, stored.R - 24, stored.R + 24);
        Assert.InRange(upright.G, stored.G - 24, stored.G + 24);
        Assert.InRange(upright.B, stored.B - 24, stored.B + 24);
    }

    [Fact]
    public void ShowWhileReadingGivesTheRenderingsOfAProgressiveFile()
    {
        // 300x270: several groups, so that a lossy progressive frame has the LF image and three passes
        var pixels = new Rgba32[300 * 270];
        for (var i = 0; i < pixels.Length; i++)
        {
            var (y, x) = Math.DivRem(i, 300);
            pixels[i] = new Rgba32((byte)(x ^ y), (byte)(x + y), (byte)(x * 3), 255);
        }

        using var source = Image.ImportPixelData<Rgba32>(pixels, 300, 270);
        using var stream = new MemoryStream();
        source.Save(stream, new JpegXlEncoder { Compression = JpegXlCompression.Lossy, Progressive = true });
        stream.Position = 0;
        var seen = new List<(bool IsFinal, int Downsampling)>();
        Assert.Equal(4, Examples.ShowWhileReading(stream, (image, refinement) =>
        {
            Assert.Equal((300, 270), (image.Width, image.Height));
            seen.Add((refinement.IsFinal, refinement.Downsampling));
        }));
        Assert.Equal([(false, 8), (false, 4), (false, 2), (true, 1)], seen);

        // Another format gives its frame at once
        using var png = new MemoryStream();
        source.Save(png, new PngEncoder());
        png.Position = 0;
        Assert.Equal(1, Examples.ShowWhileReading(png, (_, refinement) => Assert.True(refinement.IsFinal)));
    }

    [Fact]
    public void RecompressJpegStoresAJpegFileAndGivesItBack()
    {
        var jpeg = GoldenCorpus.Default.Get("jpeg/baseline-420").InputPath;
        var jxl = GetPath("recompressed.jxl");
        var rebuilt = GetPath("rebuilt.jpg");
        Assert.True(Examples.RecompressJpeg(jpeg, jxl, rebuilt));
        Assert.Equal(File.ReadAllBytes(jpeg), File.ReadAllBytes(rebuilt));
        Assert.True(JpegXlTranscoder.HasJpegReconstructionData((string)jxl));
        Assert.Equal(ImageFormat.JpegXl, Image.Identify(jxl).Format);
    }

    [Theory]
    [InlineData("avif/lossless-rgba8", "12x9 Rgba32, frames: 1, color: 1/13/0 full")]
    [InlineData("avif/lossless-rgb10", "8x8 Rgb48, frames: 1, color: 1/13/0 full")] // PNG keeps the 16-bit samples
    [InlineData("avif/lossless-gray8", "9x7 Gray8, frames: 1, color: 1/13/6 full")]
    [InlineData("avif/sequence-alpha-repeat-2", "8x8 Rgba32, frames: 2, color: 1/13/6 full")] // saved as an animated PNG
    public void ConvertAvifDescribesTheImageAndSavesItAsPng(string id, string expectedDescription)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var png = GetPath("converted.png");
        Assert.Equal(expectedDescription, Examples.ConvertAvif(fixture.InputPath, png));

        // The PNG file holds the frames, the durations and the plays of the AVIF file (its references, not a second decoding)
        using var converted = Image.Load(png);
        Assert.Equal(ImageFormat.Png, converted.Metadata.SourceFormat);
        var snapshot = ImageSnapshots.Capture(converted);
        Assert.Equal(fixture.Expected.FrameCount, snapshot.Frames.Count);
        Assert.Equal(fixture.Expected.Animation?.TotalPlays, converted.Animation?.TotalPlays);
        for (var i = 0; i < snapshot.Frames.Count; i++)
        {
            Assert.Equal(fixture.GetDuration(i), snapshot.Frames[i].Duration);
            GoldenAssert.FrameMatches(fixture, i, snapshot.Frames[i].Pixels);
        }
    }

    [Fact]
    public void ConvertAvifKeepsTheColorDescriptionOfAnHdrImage()
    {
        // BT.2020 primaries with the PQ transfer characteristics: PNG stores them in a cICP chunk, with the matrix of RGB
        // samples and their full range, so the samples are not relabeled as sRGB
        var fixture = GoldenCorpus.Default.Get("avif/lossy-420-bt2020-limited-10bit");
        var png = GetPath("hdr.png");
        Assert.EndsWith("color: 9/16/9 limited", Examples.ConvertAvif(fixture.InputPath, png), StringComparison.Ordinal);

        using var image = Image.Load(fixture.InputPath);
        using var converted = Image.Load(png);
        Assert.Equal(new CicpColorDescription(9, 16, 0, fullRange: true), converted.Metadata.Cicp);
        Assert.Equal(ImageSnapshots.CaptureFrame(image.Frames[0]).Span.ToArray(), ImageSnapshots.CaptureFrame(converted.Frames[0]).Span.ToArray());
    }

    [Fact]
    public void SaveQoiWritesTheFirstFrameAndRequiresADecisionForLinearSamples()
    {
        var (apngPath, _) = BuildAnimation();
        var linearPath = GetPath("linear.qoi");
        using (var source = new Image<Rgba32>(4, 2, new Rgba32(10, 20, 30, 40)))
        {
            source.Metadata.TransferFunction = ColorTransferFunction.Linear;
            source.Save(linearPath);
            Assert.Throws<UnsupportedImageFeatureException>(() => source.Save(GetPath("strict.png")));
        }

        var qoi = GetPath("frame.qoi");
        var png = GetPath("linear.png");
        Examples.SaveQoi(apngPath, qoi, linearPath, png);

        using (var image = Image.Load<Rgba32>(qoi))
        using (var expected = Image.Load<Rgba32>(apngPath))
        {
            Assert.Equal(ImageFormat.Qoi, image.Metadata.SourceFormat);
            Assert.Equal(expected.Frames[0][63, 63], image.Frames[0][63, 63]); // lossless
        }

        using var converted = Image.Load<Rgba32>(png);
        Assert.Equal(ColorTransferFunction.Srgb, converted.Metadata.TransferFunction);
        Assert.Equal(new Rgba32(10, 20, 30, 40), converted.Frames[0][3, 1]); // the samples are unchanged
    }

    [Fact]
    public void SaveAssetFormatsWritesTheFirstFrameInEveryAssetFormat()
    {
        var (apngPath, _) = BuildAnimation();
        var bmp = GetPath("asset.bmp");
        var tga = GetPath("asset.tga");
        var pnm = GetPath("asset.pam");
        Examples.SaveAssetFormats(apngPath, bmp, tga, pnm);

        using var expected = Image.Load<Rgba32>(apngPath);
        foreach (var (path, format) in new[] { (bmp, ImageFormat.Bmp), (tga, ImageFormat.Tga), (pnm, ImageFormat.Pnm) })
        {
            using var image = Image.Load<Rgba32>(path);
            Assert.Equal(format, image.Metadata.SourceFormat);
            Assert.Equal(1, image.Frames.Count);
            Assert.Equal(expected.Frames[0][63, 63], image.Frames[0][63, 63]); // the frames are opaque and lossless
        }

        // The variant of each output follows the pixel format and the explicit settings, never the extension
        Assert.Equal(24, File.ReadAllBytes(bmp)[28]); // biBitCount
        Assert.Equal(10, File.ReadAllBytes(tga)[2]); // run-length true color
        Assert.Equal("P7"u8.ToArray(), File.ReadAllBytes(pnm)[..2]); // the frame has alpha, so PAM is the only lossless variant
    }

    [Fact]
    public void SaveIndexedPngWritesAPaletteWhenTheColorsFit()
    {
        // Two frames of one color each: a 1-bit palette of two entries, shared by the frames
        var (apngPath, _) = BuildAnimation();
        var indexed = GetPath("indexed.png");
        Assert.True(Examples.SaveIndexedPng(apngPath, indexed));

        var data = File.ReadAllBytes(indexed);
        Assert.Equal((1, 3), (data[24], data[25])); // IHDR bit depth and color type
        Assert.True(data.AsSpan().IndexOf("PLTE"u8) > 0);
        var info = Image.Identify(indexed);
        Assert.Equal(ImageColorModel.Indexed, info.ColorModel);
        using (var expected = Image.Load<Rgba32>(apngPath))
        using (var image = Image.Load<Rgba32>(indexed))
        {
            Assert.Equal(2, image.Frames.Count);
            Assert.Equal(expected.Frames[1].Metadata.Duration, image.Frames[1].Metadata.Duration);
            Assert.Equal(Red, image.Frames[0][0, 0]);
            Assert.Equal(Blue, image.Frames[1][63, 63]);
        }

        // A gradient of 1,024 colors does not fit a palette: the example falls back to the layout of the pixel format
        var gradientPath = GetPath("gradient.png");
        using (var gradient = new Image<Rgba32>(32, 32))
        {
            gradient.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)(x * 8), (byte)(y * 8), 0);
                    }
                }
            });

            gradient.Save(gradientPath);
        }

        var fallback = GetPath("fallback.png");
        Assert.False(Examples.SaveIndexedPng(gradientPath, fallback));
        Assert.Equal((8, 6), (File.ReadAllBytes(fallback)[24], File.ReadAllBytes(fallback)[25]));
    }

    [Fact]
    public void QuantizePhotoKeepsAtMost64Colors()
    {
        var gradientPath = SaveGradient("quantize-input.png");
        var output = GetPath("quantized.png");
        Examples.QuantizePhoto(gradientPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.InRange(CountColors(image.Frames[0]), 2, 64);
    }

    [Fact]
    public void EnhanceContrastSpreadsTheLevelsOfADimImage()
    {
        // A dim, low-contrast image: lumas from 79 to 110
        var input = GetPath("dim.png");
        using (var dim = new Image<Rgb24>(64, 64))
        {
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    var level = (byte)(80 + ((x + (3 * y)) % 32));
                    dim.Frames[0][x, y] = new Rgb24(level, level, (byte)(level - 10));
                }
            }

            dim.Save(input);
        }

        var output = GetPath("enhanced.png");
        var histogram = Examples.EnhanceContrast(input, output);
        Assert.Equal(64 * 64, histogram.TotalCount);
        Assert.Equal(256, histogram.BinCount);
        Assert.InRange(histogram.Counts.LastIndexOfAnyExcept(0L) - histogram.Counts.IndexOfAnyExcept(0L), 200, 255);

        using var image = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(64, 64), image.Size);
    }

    [Fact]
    public void GradePhotoAppliesTheCurvesAndTheLookupTable()
    {
        var input = GetPath("photo.png");
        using (var photo = new Image<Rgb24>(32, 32))
        {
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 32; x++)
                {
                    photo.Frames[0][x, y] = new Rgb24((byte)(x * 8), (byte)(y * 8), 128);
                }
            }

            photo.Save(input);
        }

        // A look that swaps red and blue: a lattice of 2 points per axis
        var cube = GetPath("swap.cube");
        File.WriteAllText(cube, "TITLE \"Swap\"\nLUT_3D_SIZE 2\n0 0 0\n0 0 1\n0 1 0\n0 1 1\n1 0 0\n1 0 1\n1 1 0\n1 1 1\n");

        var output = GetPath("graded.png");
        Examples.GradePhoto(input, cube, output);
        using var image = Image.Load<Rgb24>(output);

        // The look swaps red and blue: the mid blue of the photo becomes red, and its brightest red becomes blue
        var corner = image.Frames[0][0, 0];
        Assert.InRange(corner.R, 100, 200);
        Assert.Equal(0, corner.G);
        Assert.Equal(0, corner.B);
        Assert.InRange(image.Frames[0][31, 0].B, 200, 255);
        Assert.Equal(0, image.Frames[0][31, 0].G);
    }

    [Fact]
    public void ExtractTextMakesTheTextWhiteOnBlack()
    {
        // A page lit from the right: the paper goes from 110 to 240, and lines of text are 90 levels darker
        var input = GetPath("page.png");
        using (var page = new Image<Rgb24>(160, 64))
        {
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 160; x++)
                {
                    var paper = 110 + (x * 130 / 159);
                    var text = y % 16 is >= 6 and < 10 && x % 6 < 3;
                    var level = (byte)(text ? paper - 90 : paper);
                    page.Frames[0][x, y] = new Rgb24(level, level, level);
                }
            }

            page.Save(input);
        }

        var output = GetPath("text.png");
        Examples.ExtractText(input, output);
        using var image = Image.Load<Rgb24>(output);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][1, 7]);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][151, 8]);
        Assert.Equal(new Rgb24(0, 0, 0), image.Frames[0][4, 1]);
        Assert.Equal(new Rgb24(0, 0, 0), image.Frames[0][150, 2]);
    }

    [Fact]
    public void StraightenDocumentGivesTheSizeOfThePage()
    {
        var input = GetPath("angle.png");
        using (var photo = new Image<Rgb24>(64, 48, new Rgb24(60, 60, 60)))
        {
            photo.Save(input);
        }

        var output = GetPath("straight.png");
        var size = Examples.StraightenDocument(input, output, new PointD(8, 6), new PointD(56, 10), new PointD(52, 42), new PointD(12, 40));
        Assert.Equal(new Size(48, 34), size);
        using var image = Image.Load<Rgb24>(output);
        Assert.Equal(size, image.Size);
        Assert.Equal(new Rgb24(60, 60, 60), image.Frames[0][24, 17]);
    }

    [Fact]
    public void SaveScanAsBilevelWritesTheTextInBlackOnWhite()
    {
        // A page lit from the right: the paper goes from 110 to 240, and dark lines of text are 90 levels darker
        var input = GetPath("scan.png");
        using (var scan = new Image<Rgb24>(160, 64))
        {
            for (var y = 0; y < 64; y++)
            {
                for (var x = 0; x < 160; x++)
                {
                    var paper = 110 + (x * 130 / 159);
                    var text = y % 16 is >= 6 and < 10 && x % 6 < 3;
                    var level = (byte)(text ? paper - 90 : paper);
                    scan.Frames[0][x, y] = new Rgb24(level, level, level);
                }
            }

            scan.Save(input);
        }

        var tiff = GetPath("scan.tif");
        var pbm = GetPath("scan.pbm");
        Examples.SaveScanAsBilevel(input, tiff, pbm);

        Assert.Equal("P4"u8.ToArray(), File.ReadAllBytes(pbm)[..2]);
        foreach (var path in new[] { tiff, pbm })
        {
            using var image = Image.Load<Gray8>(path);
            Assert.Equal(0, image.Frames[0][150, 7].Value);
            Assert.Equal(0, image.Frames[0][1, 7].Value);
            Assert.Equal(255, image.Frames[0][4, 7].Value);
            Assert.Equal(255, image.Frames[0][150, 2].Value);
        }
    }

    [Fact]
    public void SaveGifWithSharedPaletteKeepsTheColorsOfEveryFrame()
    {
        // Two frames of one color each: the shared palette holds both colors, and each frame keeps its own
        var (apngPath, _) = BuildAnimation();
        var gif = GetPath("shared.gif");
        Examples.SaveGifWithSharedPalette(apngPath, gif);

        using var image = Image.Load<Rgba32>(gif);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(Red, image.Frames[0][0, 0]);
        Assert.Equal(Blue, image.Frames[1][63, 63]);
    }

    [Fact]
    public void SavePalettePngReducesTheColorsThatDoNotFit()
    {
        var gradientPath = SaveGradient("palette-input.png");
        var output = GetPath("palette.png");
        Examples.SavePalettePng(gradientPath, output);

        Assert.Equal(3, File.ReadAllBytes(output)[25]); // IHDR color type: indexed
        using var image = Image.Load<Rgba32>(output);
        Assert.InRange(CountColors(image.Frames[0]), 2, 256);

        // An image whose colors fit is stored exactly
        var (apngPath, _) = BuildAnimation();
        var exact = GetPath("palette-exact.png");
        Examples.SavePalettePng(apngPath, exact);
        using var animation = Image.Load<Rgba32>(exact);
        Assert.Equal(Red, animation.Frames[0][0, 0]);
        Assert.Equal(Blue, animation.Frames[1][63, 63]);
    }

    [Fact]
    public void SaveSmallestPngStoresTheAnimationExactlyInFewerBytes()
    {
        // Two frames of one color each: a palette, and a second frame of one pixel (nothing else changes)
        var (apngPath, _) = BuildAnimation();
        var smallest = GetPath("smallest.png");
        Examples.SaveSmallestPng(apngPath, smallest);

        var data = File.ReadAllBytes(smallest);
        Assert.Equal(3, data[25]); // IHDR color type: indexed
        Assert.HasCountLessThan(File.ReadAllBytes(apngPath).Length, data);
        using var expected = Image.Load<Rgba32>(apngPath);
        using var image = Image.Load<Rgba32>(smallest);
        Assert.Equal(expected.Frames.Count, image.Frames.Count);
        Assert.Equal(expected.Animation!.TotalPlays, image.Animation!.TotalPlays);
        for (var i = 0; i < image.Frames.Count; i++)
        {
            Assert.Equal(expected.Frames[i].Metadata.Duration, image.Frames[i].Metadata.Duration);
            for (var y = 0; y < image.Height; y++)
            {
                for (var x = 0; x < image.Width; x++)
                {
                    Assert.Equal(expected.Frames[i][x, y], image.Frames[i][x, y]);
                }
            }
        }
    }

    [Fact]
    public void SaveIndexedBmpWritesARunLengthEncodedPalettedBitmap()
    {
        var (apngPath, _) = BuildAnimation();
        var bmp = GetPath("indexed.bmp");
        Examples.SaveIndexedBmp(apngPath, bmp);

        var data = File.ReadAllBytes(bmp);
        Assert.Equal(8, data[28]); // biBitCount
        Assert.Equal(1, data[30]); // biCompression: BI_RLE8
        Assert.Equal((uint)data.Length, BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(2))); // bfSize

        using var expected = Image.Load<Rgba32>(apngPath);
        using var image = Image.Load(bmp);
        Assert.Equal(ImageFormat.Bmp, image.Metadata.SourceFormat);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(expected.Size, image.Size);
        Assert.Equal(1, image.Frames.Count);
    }

    [Fact]
    public void WriteAndReadTiffDocumentKeepsThePagesIndependent()
    {
        var (apngPath, _) = BuildAnimation();
        var document = GetPath("document.tif");
        var size = Examples.WriteAndReadTiffDocument(apngPath, document);
        Assert.Equal(new Size(32, 32), size); // the second page is half the size of the first one

        var info = Image.Identify(document, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(ImageFormat.Tiff, info.Format);
        Assert.Equal(2, info.CollectionEntryCount); // two pages
        Assert.Equal(1, info.FrameCount); // never animation frames
        Assert.False(info.IsAnimated);

        using var collection = ImageCollection.Load(document);
        Assert.Equal(ImageCollectionKind.Pages, collection.Kind);
        Assert.Equal(new Size(64, 64), collection[0].Size);
        Assert.Equal(new Size(32, 32), collection[1].Size);
    }

    [Fact]
    public void WriteAndReadIconKeepsEveryRepresentationAndTheCursorHotspot()
    {
        var (apngPath, _) = BuildAnimation();
        var icon = GetPath("app.ico");
        var cursor = GetPath("pointer.cur");
        var hotspot = Examples.WriteAndReadIcon(apngPath, icon, cursor);
        Assert.Equal(new Point(8, 8), hotspot); // the hotspot of the 16x16 representation of the cursor

        using var representations = ImageCollection.Load(icon);
        Assert.Equal(ImageFormat.Ico, representations.Format);
        Assert.Equal(ImageCollectionKind.Representations, representations.Kind);
        Assert.Equal([new Size(16, 16), new Size(32, 32), new Size(256, 256)], representations.Entries.Select(entry => entry.Size));
        Assert.All(representations.Entries, entry => Assert.Null(entry.Hotspot)); // an icon stores no hotspot
        Assert.Equal(new Size(32, 32), representations.SelectBySize(new Size(24, 24)).Size);
    }

    [Fact]
    public void WriteAndReadAnimatedCursorKeepsTheStepsTheirTimingAndTheirHotspots()
    {
        var (apngPath, _) = BuildAnimation();
        var cursorPath = GetPath("busy.ani");
        var hotspot = Examples.WriteAndReadAnimatedCursor(apngPath, cursorPath);
        Assert.Equal(new Point(8, 4), hotspot); // (4, 2) on the 32x32 cursor, after doubling its size

        using var cursor = Image.Load(cursorPath);
        Assert.Equal(ImageFormat.Ani, cursor.Metadata.SourceFormat);
        Assert.Equal(new Size(32, 32), cursor.Size);
        Assert.Equal(2, cursor.Frames.Count);

        // 100 ms is 6 jiffies and 1/30 s is 2: both are stored exactly
        Assert.Equal([new FrameDuration(1, 10), new FrameDuration(1, 30)], cursor.Frames.Select(frame => frame.Metadata.Duration));
        Assert.All(cursor.Frames, frame => Assert.Equal(new Point(4, 2), frame.Metadata.Hotspot));
        Assert.Null(cursor.Animation!.TotalPlays);
    }

    [Fact]
    public async Task StreamingResizeAsyncProcessesOneFrameAtATime()
    {
        var (_, gifPath) = BuildAnimation();
        await using var input = File.OpenRead(gifPath);
        using var output = new MemoryStream();

        await Examples.StreamingResizeAsync(input, output, XunitCancellationToken);

        Assert.True(output.CanWrite); // caller streams stay open
        output.Position = 0;
        using var result = await Image.LoadAsync<Rgba32>(output, cancellationToken: XunitCancellationToken);
        Assert.Equal(new Size(32, 32), result.Size);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal(3, result.Animation!.TotalPlays);
        Assert.Equal(Red, result.Frames[0][16, 16]);
        Assert.Equal(Blue, result.Frames[1][16, 16]);
    }

    [Fact]
    public void StreamingWithReusedBufferRewritesTheAnimation()
    {
        var (apngPath, _) = BuildAnimation();
        var output = GetPath("gray.png");

        Examples.StreamingWithReusedBuffer(apngPath, output);

        using var result = Image.Load<Rgba32>(output);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal(3, result.Animation!.TotalPlays);
        Assert.Equal(new FrameDuration(1, 30), result.Frames[1].Metadata.Duration);
        Assert.Equal(new Rgba32(54, 54, 54), result.Frames[0][1, 1]); // Rec. 709: (2126 * 255 + 5000) / 10000
        Assert.Equal(new Rgba32(18, 18, 18), result.Frames[1][1, 1]); // (722 * 255 + 5000) / 10000
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp", new EnumerationOptions { AttributesToSkip = FileAttributes.None }));
    }

    [Fact]
    public void DescribeIdentifiesWithoutDecoding()
    {
        var (apngPath, gifPath) = BuildAnimation();

        using (var apng = File.OpenRead(apngPath))
        {
            Assert.Equal("Png 64x64 Rgba32, frames: 2, orientation: TopLeft", Examples.Describe(apng));
        }

        using var gif = File.OpenRead(gifPath);
        Assert.Equal("Gif 64x64 Rgba32, frames: 2, orientation: TopLeft", Examples.Describe(gif));
    }

    [Fact]
    public void DescribeFontReadsNamesAndMetrics()
    {
        // The same font in three containers: the values the corpus records for it (fonts/vectors/noto-sans-regular.json)
        // are 1000 units per em, an ascent of 1069 and a descent of 293, so 17.104 and 4.688 at 16 pixels.
        var root = FixtureRoot.GetDirectory() / "fonts" / "noto-sans";
        foreach (var name in (string[])["NotoSans-Regular.subset.ttf", "NotoSans-Regular.subset.woff", "NotoSans-Regular.subset.woff2"])
        {
            Assert.Equal("Noto Sans Regular: 425 glyphs, ascent 17.1, descent 4.69, euro sign: True", Examples.DescribeFont(root / name));
        }
    }

    [Fact]
    public void DrawWeightsDrawsHeavierAndHeavierText()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-variable" / "NotoSans-Variable.subset.ttf";
        var output = GetPath("weights.png");
        Examples.DrawWeights(fontPath, output);

        // Five lines of the same word, 38 pixels apart, from the lightest weight of the font to its heaviest: each
        // line has more ink than the one above it.
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal((220, 200), (image.Width, image.Height));
        var ink = new long[5];
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < 190; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    ink[y / 38] += 255 - pixel.R;
                }
            }
        });
        Assert.True(ink[0] > 50_000, $"The first line has an ink of {ink[0]}");
        for (var i = 1; i < ink.Length; i++)
        {
            Assert.True(ink[i] > ink[i - 1] * 1.1, $"The line {i} has an ink of {ink[i]} and the line above it {ink[i - 1]}");
        }
    }

    [Fact]
    public void DrawLabelDrawsTheTextOnAnImageOfItsSize()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("label.png");
        Examples.DrawLabel(fontPath, output);

        // Two lines of Noto Sans at 32 pixels: 1000 units per em, an ascent of 1069 and a descent of 293.
        var layout = TextLayout.Create("Hello, world!\nBonjour", Font.Load(fontPath), 32);
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(2 * 32 * 1.362, layout.Height, 1e-9);
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal((int)Math.Ceiling(layout.Width) + 16, image.Width);
        Assert.Equal(104, image.Height);

        // The margin is untouched, and the text is there: blue inside the stem of the H, which starts 8 pixels from the left.
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][2, 2]);
        var blue = 0;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    blue += pixel == new Rgba32(30, 60, 160) ? 1 : 0;
                }
            }
        });
        Assert.True(blue > 200, $"Only {blue} pixels have the color of the text");
    }

    [Fact]
    public void DrawSmallLabelHintsTheText()
    {
        var fontPath = FixtureRoot.GetDirectory() / "font-suites" / "truetype-hinting" / "DroidSans.subset.ttf";
        var output = GetPath("hinted-label.png");
        Examples.DrawSmallLabel(fontPath, output);

        // The advances are whole pixels, so the image is exactly 8 pixels wider than the text.
        var layout = TextLayout.Create("Settings and preferences", new TextStyle(Font.Load(fontPath), 12), new TextLayoutOptions { Hinting = TextHinting.Full });
        Assert.Equal(Math.Round(layout.Width, MidpointRounding.AwayFromZero), layout.Width);
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal((int)layout.Width + 8, image.Width);

        // Some pixels of the hinted stems are fully covered.
        var black = 0;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    black += pixel == new Rgba32(0, 0, 0) ? 1 : 0;
                }
            }
        });
        Assert.True(black > 20, $"Only {black} pixels are black");
    }

    [Fact]
    public void DrawPayButtonPlacesTheTextsByAPointOfTheirBox()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("pay-button.png");
        Examples.DrawPayButton(fontPath, output);

        var font = Font.Load(fontPath);
        Assert.False(TextLayout.Create("Pay now", font, 20).HasMissingGlyphs);
        Assert.False(TextLayout.Create("Total", font, 16).HasMissingGlyphs);
        Assert.False(TextLayout.Create("42.00", font, 24).HasMissingGlyphs);

        // The label is the light ink in the blue button, the caption the gray ink on its left, the amount the black ink on its right
        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];
        var label = GetInk(frame, new Rectangle(20, 64, 200, 40), pixel => pixel.R > 128);
        var caption = GetInk(frame, new Rectangle(0, 0, 120, 60), pixel => pixel.R < 200);
        var amount = GetInk(frame, new Rectangle(120, 0, 120, 60), pixel => pixel.R < 200);

        // The label is centered in the button, within a pixel
        Assert.InRange(label.Left + label.Right, 239, 241);
        Assert.InRange(label.Top + label.Bottom, 167, 169);

        // The caption starts at 20 and the amount ends at 220, within their side bearings; both sit on the baseline at 44
        Assert.InRange(caption.Left, 20, 22);
        Assert.InRange(amount.Right, 216, 220);
        Assert.InRange(caption.Bottom, 44, 45);
        Assert.InRange(amount.Bottom, 44, 45);
        Assert.True(amount.Top < caption.Top, "The amount is taller than the caption");

        static Rectangle GetInk(ImageFrame<Rgba32> frame, Rectangle area, Func<Rgba32, bool> isInk)
        {
            int left = int.MaxValue, top = int.MaxValue, right = int.MinValue, bottom = int.MinValue;
            for (var y = area.Top; y < area.Bottom; y++)
            {
                for (var x = area.Left; x < area.Right; x++)
                {
                    if (isInk(frame[x, y]))
                    {
                        left = Math.Min(left, x);
                        top = Math.Min(top, y);
                        right = Math.Max(right, x + 1);
                        bottom = Math.Max(bottom, y + 1);
                    }
                }
            }

            Assert.True(left <= right, $"No ink in {area}");
            return Rectangle.FromLTRB(left, top, right, bottom);
        }
    }

    [Fact]
    public void DrawParagraphWrapsAndPaintsTheSpans()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("paragraph.png");
        Examples.DrawParagraph(fontPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(320, image.Width);

        // Several lines of 18 pixels text, 1.362 times as tall: the height is a whole number of them, plus the margins.
        var lineCount = (image.Height - 20) / (18 * 1.362);
        Assert.True(lineCount is > 2.9 and < 6, $"The image is {image.Height} pixels tall");

        // The text is black, and the emphasized word and its underline are red; the margins are untouched.
        var black = 0;
        var red = 0;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    black += pixel == new Rgba32(0, 0, 0) ? 1 : 0;
                    red += pixel == new Rgba32(180, 30, 30) ? 1 : 0;
                }
            }
        });
        Assert.True(black > 500, $"Only {black} pixels are black");
        Assert.True(red > 20, $"Only {red} pixels are red");
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][5, 5]);
    }

    [Fact]
    public void DrawVerticalTextSetsColumnsFromTheRight()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf";
        var output = GetPath("vertical.png");
        Examples.DrawVerticalText(fontPath, output);

        // The font has every character of the text; 18 characters of 24 pixels and 4 narrower ones do not fit one
        // column of 160 pixels: there are several, each as wide as a line of the font is tall
        var font = Font.Load(fontPath);
        var layout = TextLayout.Create(
            "\u300C\u65E5\u672C\u8A9E\u300D\u306E\u30C6\u30AD\u30B9\u30C8\u3002ABC \u6771\u4EAC\u3001\u65E5\u672C\u3002",
            new TextStyle(font, 24),
            new TextLayoutOptions { WritingMode = TextWritingMode.VerticalRightToLeft, MaxHeight = 160 });
        Assert.False(layout.HasMissingGlyphs);
        Assert.InRange(layout.LineCount, 3, 4);
        var lineWidth = TextLayout.Create("\u65E5", font, 24).Height;
        Assert.Equal(layout.LineCount * lineWidth, layout.Width, 1e-9);
        Assert.InRange(layout.Height, 120, 160);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size((int)Math.Ceiling(layout.Width) + 20, 180), image.Size);

        // The first column is the rightmost one and is full; the last one is the leftmost one and is shorter: there
        // is ink at the bottom of the first one, and none at the bottom of the last one
        var first = layout.Lines[0];
        var last = layout.Lines[^1];
        Assert.True(first.X > last.X);
        Assert.True(first.Height > last.Height + 24);
        Assert.True(CountInk(first, 96, 150) > 20);
        Assert.Equal(0, CountInk(last, (int)Math.Ceiling(last.Height) + 12, 170));
        Assert.True(CountInk(last, 10, 40) > 20);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][5, 5]);

        int CountInk(TextLine line, int top, int bottom)
        {
            var count = 0;
            for (var y = top; y < bottom; y++)
            {
                for (var x = (int)(10 + line.X); x < (int)(10 + line.X + line.Width); x++)
                {
                    count += image.Frames[0][x, y] == new Rgba32(0, 0, 0) ? 1 : 0;
                }
            }

            return count;
        }
    }

    [Fact]
    public void DrawTitleCardFitsItsTextAndLeavesRoomForItsIcon()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var iconPath = GetPath("dot.png");
        var green = new Rgba32(0, 200, 80);
        using (var icon = new Image<Rgba32>(32, 32, green))
        {
            icon.Save(iconPath);
        }

        var output = GetPath("title.png");
        var scale = Examples.DrawTitleCard(fontPath, iconPath, output);

        // At 64 pixels per em the title is wider than the card: it is made smaller, and still fills the width
        Assert.InRange(scale, 0.2, 0.95);
        var font = Font.Load(fontPath);
        var style = new TextStyle(font, 64);
        var capHeight = font.GetMetrics(64).CapHeight;
        var text = new RichTextBuilder().AppendPlaceholder(capHeight, capHeight, capHeight, style).Append(" Draw more with less", style).Build();
        var layout = TextLayout.CreateToFit(text, 288, 96, new TextLayoutOptions { Alignment = TextAlignment.Center });
        Assert.Equal(layout.Scale, scale, 1e-9);
        Assert.False(layout.HasMissingGlyphs);
        Assert.False(layout.IsTruncated);
        Assert.InRange(layout.Width, 200, 288);
        Assert.InRange(layout.Height, 20, 96);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(320, 120), image.Size);
        var frame = image.Frames[0];
        var background = Rgba32.ParseHex("#14213D");
        var orange = Rgba32.ParseHex("#FCA311");
        Assert.Equal(background, frame[2, 2]);
        Assert.Equal(background, frame[317, 117]);

        // The icon is where the layout left room for it, as tall as a capital letter
        var place = layout.Placeholders[0];
        var originY = 12 + ((96 - layout.Height) / 2);
        Assert.Equal(green, frame[(int)(16 + place.X + (place.Width / 2)), (int)(originY + place.Y + (place.Height / 2))]);
        Assert.Equal(capHeight * scale, place.Height, 1e-9);

        // The highlighted word is on its orange background, the last word has an orange contour, and the letters
        // of both are there
        var orangePixels = 0;
        var whitePixels = 0;
        var darkOnOrange = 0;
        for (var y = 0; y < 120; y++)
        {
            for (var x = 0; x < 320; x++)
            {
                var pixel = frame[x, y];
                orangePixels += pixel == orange ? 1 : 0;
                whitePixels += pixel == Colors.White ? 1 : 0;
                darkOnOrange += pixel == background && x > 0 && x < 319 && frame[x - 1, y] != background && y > 12 && y < 108 && IsNearOrange(frame, x, y, orange) ? 1 : 0;
            }
        }

        Assert.InRange(orangePixels, 300, 20000);
        Assert.InRange(whitePixels, 300, 20000);
        Assert.InRange(darkOnOrange, 20, 20000);

        static bool IsNearOrange(ImageFrame<Rgba32> frame, int x, int y, Rgba32 orange)
        {
            for (var dx = -6; dx <= 6; dx++)
            {
                if (x + dx is >= 0 and < 320 && frame[x + dx, y] == orange)
                    return true;
            }

            return false;
        }
    }

    [Fact]
    public void DrawTextOnPathFollowsTheArch()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("arch.png");
        Examples.DrawTextOnPath(fontPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal((400, 170), (image.Width, image.Height));

        // The arch is symmetric around x = 200 and its top is at y = 45; its ends are at y = 150. The text is centered
        // on it and stands above it: nothing of it is under the ends, and it is as much on the left as on the right.
        // Only the pixels the turned letters cover entirely have the color of the text.
        var blue = 0;
        var sumX = 0L;
        var lowest = 0;
        var highest = int.MaxValue;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                var row = pixels.GetRowSpan(y);
                for (var x = 0; x < row.Length; x++)
                {
                    if (row[x] == new Rgba32(30, 60, 160))
                    {
                        blue++;
                        sumX += x;
                        lowest = Math.Max(lowest, y);
                        highest = Math.Min(highest, y);
                    }
                }
            }
        });
        Assert.True(blue > 200, $"Only {blue} pixels have the color of the text");
        Assert.True(lowest < 150, $"The text goes down to {lowest}");
        Assert.True(highest is > 15 and < 45, $"The text goes up to {highest}");
        Assert.Equal(200, (double)sumX / blue, 20.0);
    }

    [Fact]
    public void ConvertToGray16FlattensAlphaOntoTheBackground()
    {
        var input = GetPath("translucent.png");
        using (var source = new Image<Rgba64>(2, 1, new Rgba64(0, 0, 0, 0)))
        {
            source.Frames[0][1, 0] = new Rgba64(65535, 65535, 65535, 65535);
            source.Save(input);
        }

        var output = GetPath("gray16.png");
        Examples.ConvertToGray16(input, output);

        using var gray = Image.Load(output);
        Assert.Equal(PixelFormat.Gray16, gray.PixelFormat);
        Assert.Equal(new Gray16(65535), ((Image<Gray16>)gray).Frames[0][0, 0]); // transparent: the white background
        Assert.Equal(new Gray16(65535), ((Image<Gray16>)gray).Frames[0][1, 0]);
    }

    [Fact]
    public void ToneMapHighlightsCompressesOrClipsWhatIsBrighterThanTheWhite()
    {
        using var hdr = new Image<RgbaFloat>(2, 1);
        hdr.Frames[0][0, 0] = new RgbaFloat(0.5f, 0.25f, 0.125f);
        hdr.Frames[0][1, 0] = new RgbaFloat(4, 2, 1); // a highlight four times as bright as the white
        Assert.Equal(Metadata.ColorTransferFunction.Linear, hdr.Metadata.TransferFunction);
        Assert.Throws<UnsupportedImageFeatureException>(() => hdr.CloneAs<Rgba64>());

        var output = GetPath("highlights.png");
        using var toneMapped = Examples.ToneMapHighlights(hdr, output);

        // Reinhard with the peak of the image (4 whites): the highlight becomes the white, the other colors are compressed
        Assert.Equal(new Rgba64(65535, 32768, 16384), toneMapped.Frames[0][1, 0]);
        Assert.True(toneMapped.Frames[0][0, 0].R < 32768);
        using var saved = Image.Load<Rgba64>(output);
        Assert.Equal(new Rgba64(65535, 65535, 65535), saved.Frames[0][1, 0]);
        Assert.Equal(new Rgba64(32768, 16384, 8192), saved.Frames[0][0, 0]);
    }

    [Fact]
    public void ExtractAlphaMaskLoadsGrayWithAlphaAndKeepsTheAlpha()
    {
        var input = GetPath("gray-alpha.png");
        using (var source = new Image<La16>(2, 1, new La16(l: 200, a: 128)))
        {
            source.Frames[0][1, 0] = new La16(l: 10, a: 255);
            source.Save(input, new PngEncoder());
        }

        Assert.Equal(PixelFormat.La16, Image.Identify(input).PixelFormat);
        var output = GetPath("mask.png");
        using var mask = Examples.ExtractAlphaMask(input, output);
        Assert.Equal(new A8(128), mask.Frames[0][0, 0]);
        Assert.Equal(new A8(255), mask.Frames[0][1, 0]);

        // The mask is black gray with alpha in the file
        using var saved = Image.Load(output);
        Assert.Equal(PixelFormat.La16, saved.PixelFormat);
        Assert.Equal(new La16(0, 128), ((Image<La16>)saved).Frames[0][0, 0]);
    }

    [Fact]
    public void RoundTripRawPixelsCopiesTheCroppedHalf()
    {
        byte[] rgba = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];

        var result = Examples.RoundTripRawPixels(rgba, width: 2, height: 2);

        Assert.Equal([1, 2, 3, 4, 9, 10, 11, 12], result);
    }

    [Fact]
    public void TryLoadMapsTheErrorTaxonomy()
    {
        var notAnImage = GetPath("not-an-image.png");
        File.WriteAllText(notAnImage, "plain text, not an image");
        Assert.Null(Examples.TryLoad(notAnImage, ImageConfiguration.Default));

        var truncated = GetPath("truncated.png");
        using (var image = new Image<Rgb24>(8, 8))
        {
            using var stream = new MemoryStream();
            image.Save(stream, new PngEncoder());
            File.WriteAllBytes(truncated, stream.ToArray()[..^20]);
        }

        Assert.Null(Examples.TryLoad(truncated, ImageConfiguration.Default));

        var (apngPath, _) = BuildAnimation();
        var tiny = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 32 } };
        Assert.Null(Examples.TryLoad(apngPath, tiny));

        using var loaded = Examples.TryLoad(apngPath, ImageConfiguration.Default);
        Assert.NotNull(loaded);
        Assert.Equal(2, loaded.Frames.Count);
    }

    [Fact]
    public void EditMetadataWritesTextAndResolution()
    {
        var path = GetPath("metadata.png");
        using (var image = new Image<Rgb24>(4, 4))
        {
            image.Save(path);
        }

        Examples.EditMetadata(path);

        using var edited = Image.Load(path);
        var entry = Assert.Single(edited.Metadata.TextEntries);
        Assert.Equal(new ImageTextEntry("Title", "Sunset"), entry);
        Assert.Equal(300, edited.Metadata.Resolution!.HorizontalDpi, precision: 1); // PNG stores pixels per meter
    }

    [Fact]
    public void ConvertHdrToSrgbToneMapsTheImageAndSavesItAsPng()
    {
        // BT.2020 primaries with the PQ transfer characteristics, 10 bits: the image that ConvertAvif cannot save as PNG
        var fixture = GoldenCorpus.Default.Get("avif/lossy-420-bt2020-limited-10bit");
        var png = GetPath("hdr-to-srgb.png");
        Examples.ConvertHdrToSrgb(fixture.InputPath, png);

        using var converted = Image.Load<Rgba64>(png);
        Assert.Null(converted.Metadata.Cicp);
        Assert.NotNull(converted.Metadata.IccProfile);
        Assert.Equal(IccProfile.Srgb.Data.Span, converted.Metadata.IccProfile.Data.Span);

        // The same pixels as the conversion of the loaded image, which are not the stored samples
        using var source = Image.Load<Rgba64>(fixture.InputPath);
        using var expected = Image.Load<Rgba64>(fixture.InputPath);
        expected.ConvertColorProfile(IccProfile.Srgb, new IccColorTransformOptions { ToneMapping = HdrToneMappingOptions.Default }, TestContext.Current.CancellationToken);
        var changed = false;
        for (var y = 0; y < expected.Height; y++)
        {
            for (var x = 0; x < expected.Width; x++)
            {
                Assert.Equal(expected.Frames[0][x, y], converted.Frames[0][x, y]);
                changed |= source.Frames[0][x, y] != expected.Frames[0][x, y];
            }
        }

        Assert.True(changed);
    }

    [Fact]
    public void ConvertToSrgbConvertsTheColorsAndRemovesTheLabel()
    {
        // Display P3 (200, 100, 50) is sRGB (215, 93, 31); the result is saved to QOI, which stores no profile
        var input = GetPath("display-p3.png");
        var output = GetPath("srgb.qoi");
        using (var image = new Image<Rgb24>(2, 2, new Rgb24(200, 100, 50)))
        {
            image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
            image.Save(input);
        }

        Examples.ConvertToSrgb(input, output);
        using (var converted = Image.Load<Rgb24>(output))
        {
            Assert.Null(converted.Metadata.IccProfile);
            Assert.Equal(new Rgb24(215, 93, 31), converted.Frames[0][1, 1]);
        }

        // Grayscale pixels: gamma 563/256 gray 64 is sGray 62; an untagged image is left as it is
        var grayInput = GetPath("gray-gamma.png");
        var grayOutput = GetPath("sgray.png");
        using (var image = new Image<Gray8>(2, 2, new Gray8(64)))
        {
            image.Metadata.IccProfile = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
            image.Save(grayInput);
        }

        Examples.ConvertToSrgb(grayInput, grayOutput);
        using (var converted = Image.Load<Gray8>(grayOutput))
        {
            Assert.Null(converted.Metadata.IccProfile);
            Assert.Equal(new Gray8(62), converted.Frames[0][0, 0]);
        }

        Examples.ConvertToSrgb(grayOutput, grayInput);
        using var unchanged = Image.Load<Gray8>(grayInput);
        Assert.Equal(new Gray8(62), unchanged.Frames[0][0, 0]);
    }

    [Fact]
    public void LoadCmykJpegConvertsTheInks()
    {
        // A CMYK JPEG of the corpus without profile: rejected as it is, converted to sRGB by the example
        var path = GoldenCorpus.Default.Get("invalid/jpeg/cmyk-without-adobe-restart").InputPath;
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(path));
        using var image = Examples.LoadCmykJpeg(path);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Same(IccProfile.Srgb, image.Metadata.IccProfile);

        // The example leaves the other images alone
        var gray = GetPath("gray.png");
        using (var source = new Image<Gray8>(2, 2, new Gray8(64)))
        {
            source.Save(gray);
        }

        using var unchanged = Examples.LoadCmykJpeg(gray);
        Assert.Equal(PixelFormat.Gray8, unchanged.PixelFormat);
        Assert.Null(unchanged.Metadata.IccProfile);
    }

    [Fact]
    public void ConvertSamplesToSrgbConvertsInterleavedSamples()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        Assert.Equal([215, 93, 31, 128, 128, 128], Examples.ConvertSamplesToSrgb(displayP3, [200, 100, 50, 128, 128, 128]));

        // One gray sample becomes three sRGB samples
        var grayGamma = IccTestProfiles.Gray(IccTestProfiles.Gamma(563));
        Assert.Equal([62, 62, 62, 255, 255, 255], Examples.ConvertSamplesToSrgb(grayGamma, [64, 255]));
    }

    [Fact]
    public void DescribeColorsConvertsComparesAndMixesColors()
    {
        // The expected values were computed with the sample code of CSS Color 4: oklch(62% 0.2 29) is the sRGB color
        // (0.9004, 0.2687, 0.2147), lab(54.1369 62.1394 45.602), 58.6485 away from #2A9D8F by CIEDE2000
        var (lab, difference, palette, tint, inDisplayP3) = Examples.DescribeColors();
        var brand = new Rgba32(230, 69, 55);
        var accent = new Rgba32(0x2A, 0x9D, 0x8F);
        Assert.StartsWith("lab(54.13687720100", lab);
        Assert.Equal(58.648510019335816, difference, 1e-9);
        Assert.HasCount(5, palette);
        Assert.Equal(brand, palette[0]);
        Assert.Equal(accent, palette[4]);

        // The tint is lighter, of the same hue; the color takes other samples in Display P3, less saturated ones
        Assert.True(Hsl.FromRgb(tint).L > Hsl.FromRgb(brand).L);
        Assert.Equal(Hsl.FromRgb(brand).H, Hsl.FromRgb(tint).H, 1.0);
        Assert.True(inDisplayP3.R < brand.R && inDisplayP3.G > brand.G);
    }

    [Fact]
    public void CreateThumbnailTurnsThePhotoUprightAndFitsItWithoutEnlarging()
    {
        // A photo stored sideways, as a camera writes it, labeled Display P3
        var photo = GetPath("photo.jpg");
        using (var image = new Image<Rgb24>(400, 300, new Rgb24(200, 100, 50)))
        {
            image.Metadata.Orientation = ExifOrientation.RightTop;
            image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
            image.Save(photo);
        }

        var thumbnail = GetPath("thumbnail.jpg");
        GuideExamples.CreateThumbnail(photo, thumbnail);
        using (var result = Image.Load<Rgb24>(thumbnail))
        {
            Assert.Equal(ImageFormat.Jpeg, result.Metadata.SourceFormat);
            Assert.Equal(new Size(150, 200), result.Size); // upright 300x400, fitted inside 200x200
            Assert.Equal(ExifOrientation.TopLeft, result.Metadata.Orientation);
            Assert.Null(result.Metadata.ExifProfile);
            Assert.Null(result.Metadata.IccProfile);

            // Display P3 (200, 100, 50) is sRGB (215, 93, 31)
            var pixel = result.Frames[0][75, 100];
            Assert.InRange(pixel.R, 211, 219);
            Assert.InRange(pixel.G, 89, 97);
            Assert.InRange(pixel.B, 27, 35);
        }

        // A smaller image keeps its size, and its transparent pixels become white
        var icon = GetPath("icon.png");
        using (var image = new Image<Rgba32>(64, 48))
        {
            image.Save(icon);
        }

        GuideExamples.CreateThumbnail(icon, thumbnail);
        using var small = Image.Load<Rgb24>(thumbnail);
        Assert.Equal(new Size(64, 48), small.Size);
        Assert.Equal(new Rgb24(255, 255, 255), small.Frames[0][10, 10]);
    }

    [Fact]
    public void ConvertFileFollowsTheExtensionAndRejectsLosses()
    {
        var png = GetPath("translucent.png");
        using (var image = new Image<Rgba32>(8, 8, new Rgba32(10, 20, 30, 128)))
        {
            image.Save(png);
        }

        // WebP is lossless by default and keeps alpha
        var webp = GetPath("translucent.webp");
        GuideExamples.ConvertFile(png, webp);
        using (var converted = Image.Load<Rgba32>(webp))
        {
            Assert.Equal(ImageFormat.WebP, converted.Metadata.SourceFormat);
            Assert.Equal(new Rgba32(10, 20, 30, 128), converted.Frames[0][4, 4]);
        }

        // JPEG cannot store an animation: nothing is written
        var (_, gif) = BuildAnimation();
        var jpeg = GetPath("animation.jpg");
        Assert.Throws<UnsupportedImageFeatureException>(() => GuideExamples.ConvertFile(gif, jpeg));
        Assert.False(File.Exists(jpeg));
    }

    [Fact]
    public void ConvertFolderToJpegConvertsEveryImageAndSkipsTheOtherFiles()
    {
        var input = GetPath("input");
        var output = GetPath("output");
        Directory.CreateDirectory(input);

        var (apng, gif) = BuildAnimation();
        File.Copy(apng, Path.Combine(input, "apng.png"));
        File.Copy(gif, Path.Combine(input, "gif.gif"));
        using (var image = new Image<Rgba64>(4, 4)) // 16-bit, transparent
        {
            image.Save(Path.Combine(input, "transparent.png"));
        }

        File.WriteAllText(Path.Combine(input, "notes.txt"), "not an image");

        var skipped = GuideExamples.ConvertFolderToJpeg(input, output);

        Assert.Equal(Path.Combine(input, "notes.txt"), Assert.Single(skipped));
        Assert.HasCount(3, Directory.GetFiles(output));
        foreach (var name in new[] { "apng.jpg", "gif.jpg" })
        {
            using var first = Image.Load<Rgb24>(Path.Combine(output, name));
            Assert.Equal(ImageFormat.Jpeg, first.Metadata.SourceFormat);
            Assert.Single(first.Frames);
            Assert.InRange(first.Frames[0][32, 32].R, 250, 255); // the red first frame
            Assert.InRange(first.Frames[0][32, 32].B, 0, 5);
        }

        using var flattened = Image.Load<Rgb24>(Path.Combine(output, "transparent.jpg"));
        Assert.Equal(new Rgb24(255, 255, 255), flattened.Frames[0][2, 2]);
    }

    [Fact]
    public void PlayTwiceAsFastHalvesTheDurationsExactly()
    {
        var (apng, gif) = BuildAnimation();

        // APNG stores the exact fractions: 1/10 s and 1/30 s become 1/20 s and 1/60 s
        var fastApng = GetPath("fast.png");
        GuideExamples.PlayTwiceAsFast(apng, fastApng);
        using (var animation = Image.Load(fastApng))
        {
            Assert.Equal(2, animation.Frames.Count);
            Assert.Equal(new FrameDuration(1, 20), animation.Frames[0].Metadata.Duration);
            Assert.Equal(new FrameDuration(1, 60), animation.Frames[1].Metadata.Duration);
            Assert.Equal(3, animation.Animation!.TotalPlays);
        }

        // GIF stores hundredths: 3/100 s becomes 1.5 hundredths, rounded up to 2
        var fastGif = GetPath("fast.gif");
        GuideExamples.PlayTwiceAsFast(gif, fastGif);
        using (var animation = Image.Load(fastGif))
        {
            Assert.Equal(new FrameDuration(1, 20), animation.Frames[0].Metadata.Duration);
            Assert.Equal(new FrameDuration(2, 100), animation.Frames[1].Metadata.Duration);
            Assert.Equal(3, animation.Animation!.TotalPlays);
        }
    }

    [Fact]
    public void PreparePhotoForWebWritesAnUprightSrgbJpegWithoutMetadata()
    {
        // The layout of the photos of a phone: a grid of tiles, a rotation property, an ICC profile and an EXIF block
        var fixture = GoldenCorpus.Default.Get("heic/phone-grid-420");
        var jpeg = GetPath("photo.jpg");
        GuideExamples.PreparePhotoForWeb(fixture.InputPath, jpeg);
        using (var photo = Image.Load(jpeg))
        {
            Assert.Equal(ImageFormat.Jpeg, photo.Metadata.SourceFormat);
            Assert.Equal(new Size(24, 64), photo.Size);
            Assert.Equal(ExifOrientation.TopLeft, photo.Metadata.Orientation);
            Assert.Null(photo.Metadata.IccProfile);
            Assert.Null(photo.Metadata.ExifProfile);
            Assert.Null(photo.Metadata.XmpProfile);
        }

        // A high dynamic range photo (BT.2020 primaries with PQ samples) is tone mapped instead of being rejected
        var hdr = GoldenCorpus.Default.Get("avif/lossy-420-bt2020-limited-10bit");
        GuideExamples.PreparePhotoForWeb(hdr.InputPath, jpeg);
        using (var photo = Image.Load(jpeg))
        {
            Assert.Null(photo.Metadata.Cicp);
            Assert.Null(photo.Metadata.IccProfile);
        }

        // A large 16-bit photo is reduced to 2048 pixels on its long side, and written with 8 bits
        var large = GetPath("large.png");
        using (var image = new Image<Rgb48>(3000, 1000, new Rgb48(30000, 30000, 30000)))
        {
            image.Save(large);
        }

        GuideExamples.PreparePhotoForWeb(large, jpeg);
        using var resized = Image.Load(jpeg);
        Assert.Equal(new Size(2048, 683), resized.Size);
        Assert.Equal(PixelFormat.Rgb24, resized.PixelFormat);
    }

    [Fact]
    public void AddLogoWatermarkDrawsTheLogoInTheCornerOfEveryFrame()
    {
        var photo = GetPath("photo.png");
        using (var image = new Image<Rgb24>(400, 200, new Rgb24(100, 100, 100)))
        {
            image.Save(photo);
        }

        var logo = GetPath("logo.png");
        using (var image = new Image<Rgba32>(100, 100, new Rgba32(255, 255, 255)))
        {
            image.Save(logo);
        }

        // The logo fits in 100x50: 50x50, 10 pixels from the right and bottom edges, at an opacity of 0.6
        var output = GetPath("watermarked.png");
        GuideExamples.AddLogoWatermark(photo, logo, output);
        using (var watermarked = Image.Load<Rgb24>(output))
        {
            Assert.Equal(new Size(400, 200), watermarked.Size);
            Assert.InRange(watermarked.Frames[0][360, 160].R, 192, 194); // 100 + 0.6 * (255 - 100)
            Assert.Equal(new Rgb24(100, 100, 100), watermarked.Frames[0][339, 160]);
            Assert.Equal(new Rgb24(100, 100, 100), watermarked.Frames[0][390, 160]);
        }

        // Every frame of an animation gets the logo: 16x16 at (47, 47) on 64x64 frames
        var (apng, _) = BuildAnimation();
        var animated = GetPath("watermarked-animation.png");
        GuideExamples.AddLogoWatermark(apng, logo, animated);
        using var animation = Image.Load<Rgba32>(animated);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(new Rgba32(255, 153, 153), animation.Frames[0][55, 55]);
        Assert.Equal(new Rgba32(153, 153, 255), animation.Frames[1][55, 55]);
        Assert.Equal(Blue, animation.Frames[1][40, 40]);
    }

    [Fact]
    public void SaveUploadIdentifiesLimitsAndReencodes()
    {
        static MemoryStream Encode(Image image, ImageEncoder encoder)
        {
            var stream = new MemoryStream();
            image.Save(stream, encoder);
            stream.Position = 0;
            return stream;
        }

        var output = GetPath("upload.webp");

        // Accepted: the upload starts after 3 bytes of the request; the output is a still WebP without metadata
        using (var image = new Image<Rgba32>(64, 32, new Rgba32(200, 30, 30)))
        {
            image.Metadata.Orientation = ExifOrientation.RightTop;
            image.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "uploaded"));
            using var png = Encode(image, new PngEncoder());
            using var upload = new MemoryStream();
            upload.Write([1, 2, 3]);
            png.CopyTo(upload);
            upload.Position = 3;
            Assert.Null(GuideExamples.SaveUpload(upload, output));
        }

        using (var saved = Image.Load(output))
        {
            Assert.Equal(ImageFormat.WebP, saved.Metadata.SourceFormat);
            Assert.Equal(new Size(32, 64), saved.Size); // turned upright
            Assert.Equal(ExifOrientation.TopLeft, saved.Metadata.Orientation);
            Assert.Null(saved.Metadata.ExifProfile);
            Assert.Empty(saved.Metadata.TextEntries);
        }

        // An animation is accepted, and saved as its first frame
        var (_, gif) = BuildAnimation();
        using (var upload = File.OpenRead(gif))
        {
            Assert.Null(GuideExamples.SaveUpload(upload, output));
        }

        using (var saved = Image.Load(output))
        {
            Assert.Single(saved.Frames);
            Assert.False(saved.IsAnimated);
        }

        // Rejected
        using (var image = new Image<Rgb24>(32, 32))
        using (var bmp = Encode(image, new BmpEncoder()))
        {
            Assert.Equal("The Bmp format is not accepted", GuideExamples.SaveUpload(bmp, output));
        }

        using (var image = new Image<Rgb24>(8, 8))
        using (var png = Encode(image, new PngEncoder()))
        {
            Assert.Equal("The image is too small", GuideExamples.SaveUpload(png, output));
        }

        using (var image = new Image<Gray8>(9000, 16))
        using (var png = Encode(image, new PngEncoder()))
        {
            Assert.Equal("The image is too large (Width)", GuideExamples.SaveUpload(png, output));
        }

        using (var image = new Image<Rgb24>(32, 32, new Rgb24(10, 200, 30)))
        using (var png = Encode(image, new PngEncoder()))
        using (var truncated = new MemoryStream(png.ToArray()[..^30]))
        {
            Assert.Equal("The file is corrupted or truncated", GuideExamples.SaveUpload(truncated, output));
        }

        using (var text = new MemoryStream("plain text, not an image"u8.ToArray()))
        {
            Assert.Equal("The file is not an image", GuideExamples.SaveUpload(text, output));
        }
    }

    [Fact]
    public void RemovePrivateMetadataKeepsTheColorProfile()
    {
        var displayP3 = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
        var input = GetPath("photo.jpg");
        using (var image = new Image<Rgb24>(40, 20, new Rgb24(200, 100, 50)))
        {
            image.Metadata.Orientation = ExifOrientation.RightTop;
            image.Metadata.IccProfile = displayP3;
            image.Metadata.TextEntries.Add(new ImageTextEntry("Comment", "At home"));
            image.Save(input);
        }

        using (var source = Image.Load(input))
        {
            Assert.NotNull(source.Metadata.ExifProfile);
            Assert.NotEmpty(source.Metadata.TextEntries);
        }

        var output = GetPath("published.jpg");
        GuideExamples.RemovePrivateMetadata(input, output);
        using var published = Image.Load(output);
        Assert.Equal(new Size(20, 40), published.Size);
        Assert.Equal(ExifOrientation.TopLeft, published.Metadata.Orientation);
        Assert.Null(published.Metadata.ExifProfile);
        Assert.Null(published.Metadata.XmpProfile);
        Assert.Empty(published.Metadata.TextEntries);
        Assert.Equal(displayP3.Data.ToArray(), published.Metadata.IccProfile?.Data.ToArray());
    }

    [Fact]
    public void StripAllMetadataAppliesTheOrientationAndTheProfileFirst()
    {
        var input = GetPath("photo.png");
        using (var image = new Image<Rgb24>(4, 2, new Rgb24(200, 100, 50)))
        {
            image.Metadata.Orientation = ExifOrientation.RightTop;
            image.Metadata.IccProfile = IccTestProfiles.Rgb(IccTestProfiles.DisplayP3Colorants, IccTestProfiles.SrgbCurve());
            image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "Sunset"));
            image.Metadata.Resolution = new ImageResolution(300, 300);
            image.Save(input);
        }

        var output = GetPath("stripped.png");
        GuideExamples.StripAllMetadata(input, output);
        using var stripped = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(2, 4), stripped.Size);
        Assert.Equal(ExifOrientation.TopLeft, stripped.Metadata.Orientation);
        Assert.Null(stripped.Metadata.ExifProfile);
        Assert.Null(stripped.Metadata.IccProfile);
        Assert.Null(stripped.Metadata.Resolution);
        Assert.Empty(stripped.Metadata.TextEntries);
        Assert.Equal(new Rgb24(215, 93, 31), stripped.Frames[0][1, 1]); // Display P3 (200, 100, 50) in sRGB
    }

    [Fact]
    public void LoadFromEverySourceGivesTheSameImage()
    {
        var path = GetPath("animation.gif");
        using (var animation = new Image<Rgba32>(40, 30, new Rgba32(255, 0, 0)))
        {
            animation.AppendFrame();
            animation.Save(path);
        }

        var sizes = LoadingExamples.LoadFromEverySource(path);
        Assert.Equal([new Size(40, 30), new Size(40, 30), new Size(40, 30)], sizes);

        // Load closed the file: it can be deleted at once
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task LoadUploadAsyncLeavesTheStreamOpen()
    {
        using var upload = new MemoryStream();
        using (var source = new Image<Rgba32>(20, 10, new Rgba32(0, 0, 255)))
        {
            source.AppendFrame();
            source.Save(upload, new GifEncoder());
        }

        upload.Position = 0;
        using var image = await LoadingExamples.LoadUploadAsync(upload, XunitCancellationToken);
        Assert.Equal(new Size(20, 10), image.Size);
        Assert.Equal(2, image.Frames.Count); // every frame of the animation
        Assert.True(upload.CanRead); // the caller's stream is left open
    }

    [Fact]
    public void LoadOpaqueCompositesTransparentPixelsOverWhite()
    {
        var path = GetPath("transparent.png");
        using (var source = new Image<Rgba32>(8, 8, new Rgba32(0, 0, 0, 0)))
        {
            source.Frames[0][0, 0] = new Rgba32(255, 0, 0);
            source.Save(path);
        }

        using var image = LoadingExamples.LoadOpaque(path);
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(new Rgb24(255, 0, 0), image.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), image.Frames[0][1, 1]); // transparent, composited over white

        // Without a background color, the alpha cannot be dropped silently
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(path));
    }

    [Fact]
    public void DetectFormatReadsTheContentNotTheExtension()
    {
        var png = GetPath("image.png");
        var gifNamedPng = GetPath("animation.png.gif");
        var text = GetPath("notes.png");
        using (var image = new Image<Rgba32>(4, 4, new Rgba32(0, 255, 0)))
        {
            image.Save(png);
            image.Save(GetPath("image.gif"));
        }

        File.Copy(png, gifNamedPng);
        File.WriteAllText(text, "not an image");

        Assert.Equal(ImageFormat.Png, LoadingExamples.DetectFormat(png));
        Assert.Equal(ImageFormat.Gif, LoadingExamples.DetectFormat(GetPath("image.gif")));
        Assert.Equal(ImageFormat.Png, LoadingExamples.DetectFormat(gifNamedPng)); // a PNG file named .gif
        Assert.Equal(ImageFormat.Unknown, LoadingExamples.DetectFormat(text)); // a text file named .png
    }

    [Fact]
    public async Task EncodeAsPngAsyncWritesAPngInMemory()
    {
        using var image = new Image<Rgba32>(12, 6, new Rgba32(1, 2, 3));
        var bytes = await LoadingExamples.EncodeAsPngAsync(image, XunitCancellationToken);

        Assert.Equal(ImageFormat.Png, Image.DetectFormat(bytes));
        using var decoded = Image.Load<Rgba32>(bytes);
        Assert.Equal(new Size(12, 6), decoded.Size);
        Assert.Equal(new Rgba32(1, 2, 3), decoded.Frames[0][5, 3]);
    }

    [Fact]
    public void TrySaveAsJpegValidatesBeforeWriting()
    {
        var path = GetPath("photo.jpg");
        byte[] original = [1, 2, 3];
        File.WriteAllBytes(path, original);

        // Transparency: rejected before anything is written, the existing file is untouched
        using (var transparent = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0, 128)))
        {
            Assert.False(LoadingExamples.TrySaveAsJpeg(transparent, path));
        }

        Assert.Equal(original, File.ReadAllBytes(path));

        // An animation: JPEG stores one picture
        using (var animation = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0)))
        {
            animation.AppendFrame();
            Assert.False(LoadingExamples.TrySaveAsJpeg(animation, path));
        }

        Assert.Equal(original, File.ReadAllBytes(path));
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp", new EnumerationOptions { AttributesToSkip = FileAttributes.None }));

        // An opaque still image is saved, and replaces the file
        using (var opaque = new Image<Rgba32>(16, 16, new Rgba32(255, 0, 0)))
        {
            Assert.True(LoadingExamples.TrySaveAsJpeg(opaque, path));
        }

        Assert.Equal(ImageFormat.Jpeg, Image.Identify(path).Format);
    }

    [Fact]
    public void LoadAvatarAppliesTheLimitsToTheInputAndToTheImage()
    {
        using var small = new MemoryStream();
        using (var image = new Image<Rgba32>(64, 64, new Rgba32(0, 0, 255)))
        {
            image.Save(small, new PngEncoder());
        }

        small.Position = 0;
        using (var avatar = LoadingExamples.LoadAvatar(small))
        {
            Assert.Equal(new Size(64, 64), avatar.Size);
            Assert.Equal(4096, avatar.Configuration.Limits.MaxWidth); // the image keeps the configuration

            // Its later operations are bound by the same limits
            var exception = Assert.Throws<ImageResourceLimitException>(() => avatar.Resize(new ResizeOptions(5000, 64) { Mode = ResizeMode.Stretch, AllowUpscaling = true }));
            Assert.Equal(ImageResourceLimitKind.Width, exception.Kind);
        }

        using var wide = new MemoryStream();
        using (var image = new Image<Rgba32>(5000, 1, new Rgba32(0, 0, 255)))
        {
            image.Save(wide, new PngEncoder());
        }

        wide.Position = 0;
        var tooWide = Assert.Throws<ImageResourceLimitException>(() => LoadingExamples.LoadAvatar(wide));
        Assert.Equal(ImageResourceLimitKind.Width, tooWide.Kind);
        Assert.Equal(4096, tooWide.Limit);
        Assert.Equal(5000, tooWide.Requested);
    }

    [Fact]
    public void CreateThumbnailsProcessesTheImagesConcurrently()
    {
        var inputs = new List<string>();
        for (var i = 0; i < 4; i++)
        {
            var input = GetPath($"input{i}.gif");
            using (var image = new Image<Rgba32>(200 + (i * 10), 100, new Rgba32((byte)(i * 60), 0, 0)))
            {
                image.Save(input);
            }

            inputs.Add(input);
        }

        var output = GetPath("thumbnails");
        Directory.CreateDirectory(output);
        LoadingExamples.CreateThumbnails(inputs, output, XunitCancellationToken);

        for (var i = 0; i < 4; i++)
        {
            using var thumbnail = Image.Load(output / $"input{i}.png");
            Assert.Equal(128, thumbnail.Width); // fits inside 128x128, keeping the aspect ratio
            Assert.True(thumbnail.Height <= 128);
        }
    }

    [Fact]
    public void TryUpscaleLeavesTheImageUnchangedOnFailure()
    {
        var configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxWidth = 100 } };
        using var image = new Image<Rgba32>(50, 40, new Rgba32(255, 0, 0), configuration);
        var second = image.AppendFrame();
        second[0, 0] = new Rgba32(0, 0, 255);
        second.Metadata.Duration = new FrameDuration(1, 30);

        Assert.False(LoadingExamples.TryUpscale(image, 200, 80));
        Assert.Equal(new Size(50, 40), image.Size);
        Assert.Equal(2, image.Frames.Count);
        Assert.Same(second, image.Frames[1]); // the same frames
        Assert.Equal(new Rgba32(0, 0, 255), image.Frames[1][0, 0]);
        Assert.Equal(new FrameDuration(1, 30), image.Frames[1].Metadata.Duration);

        Assert.True(LoadingExamples.TryUpscale(image, 100, 80));
        Assert.Equal(new Size(100, 80), image.Size);
        Assert.Same(second, image.Frames[1]); // a frame reference still designates the same logical frame
    }

    [Fact]
    public void BlurFirstFrameChangesThatFrameOnly()
    {
        // Two frames whose left half is black and right half white
        var input = GetPath("halves.png");
        using (var animation = new Image<Rgba32>(16, 16, new Rgba32(255, 255, 255)))
        {
            animation.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            animation.AppendFrame(animation.Frames[0]).Metadata.Duration = new FrameDuration(1, 5);
            foreach (var frame in animation.Frames)
            {
                for (var y = 0; y < 16; y++)
                {
                    for (var x = 0; x < 8; x++)
                    {
                        frame[x, y] = new Rgba32(0, 0, 0);
                    }
                }
            }

            animation.Save(input, new PngEncoder { AnimationMode = PngAnimationMode.Animated });
        }

        var output = GetPath("blurred.png");
        ProcessingExamples.BlurFirstFrame(input, output);

        using var result = Image.Load<Rgba32>(output);
        Assert.Equal(2, result.Frames.Count);
        Assert.Equal(new FrameDuration(1, 10), result.Frames[0].Metadata.Duration);
        Assert.Equal(new FrameDuration(1, 5), result.Frames[1].Metadata.Duration);

        // The edge of the first frame is blurred to gray; the second frame keeps its sharp edge
        Assert.NotEqual(new Rgba32(0, 0, 0), result.Frames[0][7, 8]);
        Assert.NotEqual(new Rgba32(255, 255, 255), result.Frames[0][8, 8]);
        Assert.Equal(new Rgba32(0, 0, 0), result.Frames[1][7, 8]);
        Assert.Equal(new Rgba32(255, 255, 255), result.Frames[1][8, 8]);
    }

    [Fact]
    public void CropAndExtendCanvasKeepsTheCenteredSquareOnTheLeft()
    {
        // 30x20: the centered 20x20 square is x = 5 to 24, whose corners are green and yellow
        var input = GetPath("wide.png");
        using (var source = new Image<Rgb24>(30, 20, new Rgb24(0, 0, 255)))
        {
            source.Frames[0][4, 0] = new Rgb24(255, 0, 0);
            source.Frames[0][5, 0] = new Rgb24(0, 255, 0);
            source.Frames[0][24, 19] = new Rgb24(255, 255, 0);
            source.Frames[0][25, 19] = new Rgb24(255, 0, 0);
            source.Save(input);
        }

        var output = GetPath("extended.png");
        ProcessingExamples.CropAndExtendCanvas(input, output);

        using var result = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(40, 20), result.Size);
        Assert.Equal(new Rgb24(0, 255, 0), result.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 255, 0), result.Frames[0][19, 19]);
        Assert.Equal(new Rgb24(255, 255, 255), result.Frames[0][20, 0]);
        Assert.Equal(new Rgb24(255, 255, 255), result.Frames[0][39, 19]);
    }

    [Fact]
    public void ResizeToFillCoversTheBoxAndKeepsTheTop()
    {
        // A 100x200 portrait: red on top, blue at the bottom
        var input = GetPath("portrait.png");
        using (var source = new Image<Rgb24>(100, 200, new Rgb24(0, 0, 255)))
        {
            for (var y = 0; y < 100; y++)
            {
                for (var x = 0; x < 100; x++)
                {
                    source.Frames[0][x, y] = new Rgb24(255, 0, 0);
                }
            }

            source.Save(input);
        }

        var output = GetPath("filled.png");
        ProcessingExamples.ResizeToFill(input, output);

        // Scaled to 200x400, of which the top 200 rows are kept: the red half
        using var result = Image.Load<Rgb24>(output);
        Assert.Equal(new Size(200, 200), result.Size);
        Assert.Equal(new Rgb24(255, 0, 0), result.Frames[0][0, 0]);
        Assert.Equal(new Rgb24(255, 0, 0), result.Frames[0][100, 100]);
        Assert.Equal(new Rgb24(255, 0, 0), result.Frames[0][199, 190]);
    }

    [Fact]
    public void RotateAndFlipTransposesThePixels()
    {
        // A quarter turn clockwise then a horizontal mirror moves the pixel (x, y) to (y, x)
        var input = GetPath("pixels.png");
        using (var source = new Image<Rgba32>(3, 2))
        {
            for (var y = 0; y < 2; y++)
            {
                for (var x = 0; x < 3; x++)
                {
                    source.Frames[0][x, y] = new Rgba32((byte)(x * 80), (byte)(y * 200), 7);
                }
            }

            source.Save(input);
        }

        var output = GetPath("transposed.png");
        ProcessingExamples.RotateAndFlip(input, output);

        using var result = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(2, 3), result.Size);
        for (var y = 0; y < 2; y++)
        {
            for (var x = 0; x < 3; x++)
            {
                Assert.Equal(new Rgba32((byte)(x * 80), (byte)(y * 200), 7), result.Frames[0][y, x]);
            }
        }
    }

    [Fact]
    public void SkewAndTransformGrowTheCanvasWithTransparentCorners()
    {
        var input = GetPath("card.png");
        using (var source = new Image<Rgba32>(40, 20, new Rgba32(255, 0, 0)))
        {
            source.Save(input);
        }

        var skewedPath = GetPath("skewed.png");
        var transformedPath = GetPath("transformed.png");
        ProcessingExamples.SkewAndTransform(input, skewedPath, transformedPath);

        // 40 + 20 tan 20 = 47.3 columns, rounded up. The top row leans left and the bottom row right, which leaves the
        // top-right and bottom-left corners transparent
        using var skewed = Image.Load<Rgba32>(skewedPath);
        Assert.Equal(new Size(48, 20), skewed.Size);
        Assert.Equal(new Rgba32(255, 0, 0), skewed.Frames[0][24, 10]);
        Assert.Equal(0, skewed.Frames[0][47, 0].A);
        Assert.Equal(0, skewed.Frames[0][0, 19].A);

        // 20x10 turned by 45 degrees: (20 + 10) / sqrt(2) = 21.2, rounded up
        using var transformed = Image.Load<Rgba32>(transformedPath);
        Assert.Equal(new Size(22, 22), transformed.Size);
        Assert.Equal(new Rgba32(255, 0, 0), transformed.Frames[0][11, 11]);
        Assert.Equal(0, transformed.Frames[0][0, 0].A);
        Assert.Equal(0, transformed.Frames[0][21, 21].A);
    }

    [Fact]
    public void MakeBackgroundTransparentReplacesTheColorsCloseToWhite()
    {
        // An image without alpha: white, with a red pixel, a pixel within 4 levels of white and one 5 levels away
        var input = GetPath("logo.png");
        using (var source = new Image<Rgb24>(4, 1, new Rgb24(255, 255, 255)))
        {
            source.Frames[0][1, 0] = new Rgb24(200, 0, 0);
            source.Frames[0][2, 0] = new Rgb24(251, 253, 255);
            source.Frames[0][3, 0] = new Rgb24(250, 255, 255);
            source.Save(input);
        }

        var output = GetPath("transparent.png");
        ProcessingExamples.MakeBackgroundTransparent(input, output);

        using var result = Image.Load<Rgba32>(output);
        Assert.Equal(new Rgba32(0, 0, 0, 0), result.Frames[0][0, 0]);
        Assert.Equal(new Rgba32(200, 0, 0), result.Frames[0][1, 0]);
        Assert.Equal(new Rgba32(0, 0, 0, 0), result.Frames[0][2, 0]);
        Assert.Equal(new Rgba32(250, 255, 255), result.Frames[0][3, 0]);
    }

    [Fact]
    public void DitherToBlackAndWhiteMakesPatternsOfTheTwoColors()
    {
        // A mid-gray image: a threshold would make it all black or all white
        var input = GetPath("gray.png");
        using (var source = new Image<Rgb24>(32, 32, new Rgb24(128, 128, 128)))
        {
            source.Save(input);
        }

        var output = GetPath("dithered.png");
        ProcessingExamples.DitherToBlackAndWhite(input, output);

        using var result = Image.Load<Rgb24>(output);
        var pixels = new List<Rgb24>();
        for (var y = 0; y < 32; y++)
        {
            for (var x = 0; x < 32; x++)
            {
                pixels.Add(result.Frames[0][x, y]);
            }
        }

        Assert.All(pixels, pixel => pixel == new Rgb24(0, 0, 0) || pixel == new Rgb24(255, 255, 255));

        // About half of the pixels are white
        Assert.InRange(pixels.Count(pixel => pixel == new Rgb24(255, 255, 255)), 450, 574);
    }

    [Fact]
    public void FindSimilarImagesFindsTheResizedCopyOnly()
    {
        // A horizontal gradient with a dark block in its top-left corner, a half-size copy, and a mirrored copy
        var reference = GetPath("reference.png");
        var resized = GetPath("resized.png");
        var mirrored = GetPath("mirrored.png");
        using (var source = new Image<Rgb24>(64, 48))
        {
            for (var y = 0; y < 48; y++)
            {
                for (var x = 0; x < 64; x++)
                {
                    source.Frames[0][x, y] = x < 24 && y < 16 ? new Rgb24(10, 10, 10) : new Rgb24((byte)(x * 4), (byte)(x * 4), 200);
                }
            }

            source.Save(reference);
            using (var copy = source.Clone())
            {
                copy.Resize(new ResizeOptions(32, 24), XunitCancellationToken);
                copy.Save(resized);
            }

            source.Flip(FlipMode.Horizontal, XunitCancellationToken);
            source.Save(mirrored);
        }

        var similar = ProcessingExamples.FindSimilarImages(reference, [resized.Value, mirrored.Value]);
        Assert.Equal([resized.Value], similar);
    }

    [Fact]
    public void DrawBasicShapesFillsAndStrokesEachShape()
    {
        var output = GetPath("shapes.png");
        DrawingExamples.DrawBasicShapes(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(300, 140), image.Size);
        var frame = image.Frames[0];

        // Filled shapes: their colors inside, the background outside the rounded corner
        Assert.Equal(Colors.SteelBlue, frame[30, 37]);
        Assert.Equal(Colors.SeaGreen, frame[90, 37]);
        Assert.Equal(Colors.White, frame[71, 16]);
        Assert.Equal(Colors.Goldenrod, frame[150, 37]);
        Assert.Equal(Colors.Crimson, frame[210, 50]);

        // The slice goes clockwise from 30 degrees to 330 degrees: its opening is on the right of its center
        Assert.Equal(Colors.MediumPurple, frame[255, 37]);
        Assert.Equal(Colors.White, frame[285, 37]);

        // Stroked shapes: the pen is centered on the outline, the inside is not painted
        Assert.Equal(Colors.DimGray, frame[10, 100]);
        Assert.Equal(Colors.White, frame[30, 100]);

        // The arc has no sides: nothing between its center and its ends
        Assert.Equal(Colors.DimGray, frame[247, 102]);
        Assert.Equal(Colors.White, frame[280, 102]);
    }

    [Fact]
    public void DrawPinFillsTheOutlineAroundAHole()
    {
        var output = GetPath("pin.png");
        DrawingExamples.DrawPin(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Between the top of the arc and the circle, and between the two curves: inside the pin
        Assert.Equal(Colors.Crimson, frame[60, 24]);
        Assert.Equal(Colors.Crimson, frame[60, 85]);

        // The circle is a hole, and the corners are outside the pin
        Assert.Equal(Colors.White, frame[60, 45]);
        Assert.Equal(Colors.White, frame[5, 5]);
        Assert.Equal(Colors.White, frame[110, 100]);
    }

    [Fact]
    public void DrawFillRulesFillsTheMiddleOfTheStarWithTheNonZeroRuleOnly()
    {
        var output = GetPath("fill-rules.png");
        DrawingExamples.DrawFillRules(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // The tips are filled with both rules; the pentagon in the middle with the non-zero rule only
        Assert.Equal(Colors.Orange, frame[55, 20]);
        Assert.Equal(Colors.Orange, frame[165, 20]);
        Assert.Equal(Colors.Orange, frame[55, 60]);
        Assert.Equal(Colors.White, frame[165, 60]);
    }

    [Fact]
    public void DrawCombinedPathsFillsWhatEachModeKeeps()
    {
        var output = GetPath("combined.png");
        DrawingExamples.DrawCombinedPaths(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // For each cell: the part of the first circle only, the part both circles cover, the part of the second one only
        bool[][] expected =
        [
            [true, true, true], // union
            [false, true, false], // intersection
            [true, false, false], // exclusion
            [true, false, true], // xor
        ];
        for (var i = 0; i < expected.Length; i++)
        {
            var x = i * 75;
            Assert.Equal(expected[i][0], frame[x + 12, 45] == Colors.SteelBlue);
            Assert.Equal(expected[i][1], frame[x + 37, 45] == Colors.SteelBlue);
            Assert.Equal(expected[i][2], frame[x + 62, 45] == Colors.SteelBlue);
        }
    }

    [Fact]
    public void DrawGradientsPaintsEachKindOfGradient()
    {
        var output = GetPath("gradients.png");
        DrawingExamples.DrawGradients(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Linear: from crimson on the left to gold on the right, the same along a column
        Assert.True(frame[7, 40].R > 200 && frame[7, 40].G < 40);
        Assert.True(frame[69, 40].G > 200);
        Assert.Equal(frame[30, 10], frame[30, 70]);

        // Radial: white at the focal point, dark blue at the edge of the circle
        Assert.True(frame[102, 30].R > 240 && frame[102, 30].B > 240);
        Assert.True(frame[112, 70].B > frame[112, 70].R + 40);

        // Sweep: red on the right of the center, then green and blue clockwise, a third of a turn apart
        var red = frame[215, 42];
        var green = frame[175, 61];
        var blue = frame[175, 18];
        Assert.True(red.R > 200 && red.G < 80 && red.B < 80);
        Assert.True(green.G > green.R && green.G > green.B);
        Assert.True(blue.B > blue.R && blue.B > blue.G);

        // Path gradient: the color of its center (white) inside, the color of its top corner (red) near that corner
        Assert.True(frame[262, 50].R > 200 && frame[262, 50].G > 200 && frame[262, 50].B > 200);
        Assert.True(frame[262, 14].R > frame[262, 14].G && frame[262, 14].R > frame[262, 14].B);
    }

    [Fact]
    public void DrawGradientColorSpacesMixesTheColorsDifferently()
    {
        var output = GetPath("color-spaces.png");
        DrawingExamples.DrawGradientColorSpaces(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Every row starts blue and ends yellow
        for (var row = 0; row < 4; row++)
        {
            var y = 14 + (row * 24);
            Assert.True(frame[8, y].B > 240 && frame[8, y].R < 10);
            Assert.True(frame[247, y].R > 240 && frame[247, y].B < 10);
        }

        // Half way: a gray with the stored values, a lighter gray in linear light, a teal in Oklch
        var encoded = frame[128, 14];
        var linear = frame[128, 38];
        var oklch = frame[128, 86];
        Assert.InRange(encoded.R, 120, 136);
        Assert.InRange(encoded.G, 120, 136);
        Assert.InRange(encoded.B, 120, 136);
        Assert.True(linear.R > encoded.R + 30 && linear.G > encoded.G + 30);
        Assert.True(oklch.G > oklch.R + 40 && oklch.B > oklch.R + 40);
    }

    [Fact]
    public void DrawJoinsAndCapsShapesTheCornersAndTheEnds()
    {
        var output = GetPath("joins.png");
        DrawingExamples.DrawJoinsAndCaps(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Above the corner: the miter reaches higher than the round join, which reaches higher than the bevel
        Assert.Equal(Colors.SteelBlue, frame[60, 8]);
        Assert.Equal(Colors.White, frame[155, 8]);
        Assert.Equal(Colors.SteelBlue, frame[155, 10]);
        Assert.Equal(Colors.White, frame[250, 10]);

        // Before the start of the lines: nothing with a butt cap, a half disk with a round cap, a half square with a square cap
        Assert.Equal(Colors.White, frame[33, 92]);
        Assert.Equal(Colors.Coral, frame[36, 92]);
        Assert.Equal(Colors.Coral, frame[126, 92]);
        Assert.Equal(Colors.White, frame[125, 89]);
        Assert.Equal(Colors.Coral, frame[221, 92]);
        Assert.Equal(Colors.Coral, frame[220, 89]);
    }

    [Fact]
    public void DrawDashesCutsTheLinesIntoDashesAndDots()
    {
        var output = GetPath("dashes.png");
        DrawingExamples.DrawDashes(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // [10, 5]: a dash from 20 to 30, then a gap
        Assert.Equal(Colors.SteelBlue, frame[25, 20]);
        Assert.Equal(Colors.White, frame[32, 20]);

        // [0, 10] with round caps: a dot every 10 pixels
        Assert.Equal(Colors.SteelBlue, frame[30, 45]);
        Assert.Equal(Colors.White, frame[35, 45]);

        // [14, 4, 3, 4]: a dash from 20 to 34, a gap, a dash from 38 to 41, a gap
        Assert.Equal(Colors.SteelBlue, frame[27, 70]);
        Assert.Equal(Colors.White, frame[36, 70]);
        Assert.Equal(Colors.SteelBlue, frame[39, 70]);
        Assert.Equal(Colors.White, frame[43, 70]);

        // A disk at the start of the curve; the tip of the arrow head is its end
        Assert.Equal(Colors.Crimson, frame[20, 110]);
        Assert.Equal(Colors.White, frame[238, 94]);
    }

    [Fact]
    public void DrawPatternsRepeatsTheirTiles()
    {
        var output = GetPath("patterns.png");
        DrawingExamples.DrawPatterns(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // The hatches keep the pixels of their tiles: the two colors only, half of the pixels for Percent50
        var hatched = 0;
        var dark = 0;
        for (var y = 40; y < 48; y++)
        {
            for (var x = 40; x < 48; x++)
            {
                Assert.True(frame[x, y] == Colors.SteelBlue || frame[x, y] == Colors.White);
                if (frame[x, y] == Colors.SteelBlue)
                {
                    hatched++;
                }

                var halftone = frame[x + 100, y];
                Assert.True(halftone == Colors.DarkSlateGray || halftone == Colors.LightGray);
                if (halftone == Colors.DarkSlateGray)
                {
                    dark++;
                }
            }
        }

        Assert.InRange(hatched, 1, 63);
        Assert.Equal(32, dark);

        // The dots: crimson dots on a light pink background
        var dots = 0;
        var background = 0;
        for (var y = 20; y < 80; y++)
        {
            for (var x = 220; x < 280; x++)
            {
                var pixel = frame[x, y];
                if (pixel.R > 200 && pixel.G < 40)
                {
                    dots++;
                }
                else if (pixel.R > 240 && pixel.G > 210)
                {
                    background++;
                }
            }
        }

        Assert.True(dots > 200);
        Assert.True(background > dots);
    }

    [Fact]
    public void DrawClockTurnsTheTicksAndTheHandsAroundTheCenter()
    {
        var output = GetPath("clock.png");
        DrawingExamples.DrawClock(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // The tick of 12 o'clock, and the same tick turned by a quarter of a turn: 3 o'clock
        Assert.Equal(Colors.DimGray, frame[80, 25]);
        Assert.Equal(Colors.DimGray, frame[135, 80]);
        Assert.Equal(Colors.White, frame[80, 110]);

        // The minute hand points to 2, a sixth of a turn from the top
        Assert.Equal(Colors.Crimson, frame[114, 60]);
        Assert.Equal(Colors.Crimson, frame[80, 80]);
        Assert.Equal(Colors.WhiteSmoke, frame[2, 2]);
    }

    [Fact]
    public void DrawScaledIconsScalesThePenOfTheFirstRowOnly()
    {
        var output = GetPath("icons.png");
        DrawingExamples.DrawScaledIcons(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // The bottom side of the largest house, at the same scale in both rows: count the dark rows of the stroke
        var top = 0;
        var bottom = 0;
        for (var y = 70; y < 92; y++)
        {
            if (frame[146, y].R < 120)
            {
                top++;
            }

            if (frame[146, y + 92].R < 120)
            {
                bottom++;
            }
        }

        Assert.InRange(top, 4, 7);
        Assert.InRange(bottom, 1, 2);
    }

    [Fact]
    public void DrawBlendModesMixesThePaintWithTheBackground()
    {
        var output = GetPath("blend-modes.png");
        DrawingExamples.DrawBlendModes(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];
        var paint = Colors.MediumVioletRed;

        // The background is the same along a row: its color under each disk is the color between the disks
        var under = frame[50, 35];
        Assert.Equal(paint, frame[25, 35]);

        var multiply = frame[75, 35];
        var screen = frame[125, 35];
        var difference = frame[225, 35];
        Assert.True(multiply.R <= Math.Min(paint.R, under.R) + 1 && multiply.G <= Math.Min(paint.G, under.G) + 1 && multiply.B <= Math.Min(paint.B, under.B) + 1);
        Assert.True(screen.R >= Math.Max(paint.R, under.R) - 1 && screen.G >= Math.Max(paint.G, under.G) - 1 && screen.B >= Math.Max(paint.B, under.B) - 1);
        Assert.InRange(difference.R, Math.Abs(paint.R - under.R) - 1, Math.Abs(paint.R - under.R) + 1);
        Assert.InRange(difference.G, Math.Abs(paint.G - under.G) - 1, Math.Abs(paint.G - under.G) + 1);
        Assert.InRange(difference.B, Math.Abs(paint.B - under.B) - 1, Math.Abs(paint.B - under.B) + 1);
    }

    [Fact]
    public void DrawTicketErasesTheNotchesAndTheHoles()
    {
        var output = GetPath("ticket.png");
        DrawingExamples.DrawTicket(output);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        Assert.Equal(Colors.Goldenrod, frame[50, 50]);
        Assert.Equal(Colors.Goldenrod, frame[170, 38]);

        // The notches and the holes are transparent again, as the image was outside the ticket
        Assert.Equal(0, frame[170, 12].A);
        Assert.Equal(0, frame[170, 97].A);
        Assert.Equal(0, frame[170, 34].A);
        Assert.Equal(0, frame[11, 11].A);
    }

    [Fact]
    public void DrawSpotlightTurnsTheImageGrayOutsideTheCircle()
    {
        var photo = GetPath("photo.png");
        Examples.DrawLandscape(photo);
        var output = GetPath("spotlight.png");
        DrawingExamples.DrawSpotlight(photo, output);

        using var original = Image.Load<Rgba32>(photo);
        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];

        // Inside the circle, the colors are those of the photo
        Assert.Equal(original.Frames[0][179, 80], frame[179, 80]);
        Assert.Equal(original.Frames[0][179, 115], frame[179, 115]);

        // Around it, the sky and the hill are gray
        foreach (var (x, y) in new[] { (20, 20), (20, 150), (250, 150) })
        {
            Assert.NotEqual(original.Frames[0][x, y].G, original.Frames[0][x, y].B);
            Assert.Equal(frame[x, y].R, frame[x, y].G);
            Assert.Equal(frame[x, y].G, frame[x, y].B);
        }
    }

    [Fact]
    public void DrawEdgesPaintsHardEdgesWithThePaintsOnlyAndLighterEdgesInLinearLight()
    {
        var output = GetPath("edges.png");
        DrawingExamples.DrawEdges(output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(288, 96), image.Size);
        var frame = image.Frames[0];

        // Each pixel of the drawing is a square of 4 by 4 pixels; the three drawings are 24 pixels apart
        var partial = 0;
        var lighter = 0;
        for (var y = 0; y < 24; y++)
        {
            for (var x = 0; x < 24; x++)
            {
                var antialiased = frame[(x * 4) + 1, (y * 4) + 1];
                var hard = frame[((x + 24) * 4) + 1, (y * 4) + 1];
                var linear = frame[((x + 48) * 4) + 1, (y * 4) + 1];

                // Without antialiasing, a pixel is the background or one of the paints
                Assert.True(hard == Colors.Black || hard == Colors.Gold || hard == Colors.White);
                if (antialiased != Colors.Black && antialiased != Colors.Gold && antialiased != Colors.White)
                {
                    partial++;
                }

                // In linear light, a pixel the shapes cover in part is lighter
                Assert.True(linear.G >= antialiased.G);
                if (linear.G > antialiased.G)
                {
                    lighter++;
                }
            }
        }

        Assert.True(partial > 10);
        Assert.True(lighter > 10);
    }

    [Fact]
    public void DrawProgressBarDrawsADifferentBarOnEachFrame()
    {
        var (apngPath, _) = BuildAnimation();
        var output = GetPath("progress.png");
        DrawingExamples.DrawProgressBar(apngPath, output);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(2, image.Frames.Count);
        Assert.Equal(new FrameDuration(1, 30), image.Frames[1].Metadata.Duration);
        Assert.Equal(3, image.Animation!.TotalPlays);

        // The bar is filled to half of its track on the first frame, and entirely on the last one
        Assert.Equal(Colors.White, image.Frames[0][10, 56]);
        Assert.Equal(new Rgba32(135, 0, 0), image.Frames[0][40, 56]);
        Assert.Equal(Colors.White, image.Frames[1][40, 56]);

        // Above the bar, the frames are untouched
        Assert.Equal(Red, image.Frames[0][40, 20]);
        Assert.Equal(Blue, image.Frames[1][40, 20]);
    }

    [Fact]
    public void DrawFontStackDrawsEachCharacterWithAFontThatHasIt()
    {
        var fonts = FixtureRoot.GetDirectory() / "fonts";
        var latin = fonts / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var japanese = fonts / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf";
        var emoji = fonts / "noto-color-emoji" / "Noto-COLRv1.subset.ttf";
        var output = GetPath("font-stack.png");
        TextExamples.DrawFontStack(latin, japanese, emoji, output);

        // No font has every character, the stack has them all
        var stack = new FontStack(Font.Load(latin), Font.Load(japanese), Font.Load(emoji));
        Assert.True(TextLayout.Create("Tokyo \u6771\u4EAC \U0001F680", Font.Load(latin), 32).HasMissingGlyphs);
        var layout = TextLayout.Create("Tokyo \u6771\u4EAC \U0001F680", new TextStyle(stack, 32));
        Assert.False(layout.HasMissingGlyphs);

        // The text is black and the emoji keeps its own colors
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size((int)Math.Ceiling(layout.Width) + 20, (int)Math.Ceiling(layout.Height) + 20), image.Size);
        var black = 0;
        var colored = 0;
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    black += pixel == new Rgba32(0, 0, 0) ? 1 : 0;
                    colored += Math.Max(Math.Abs(pixel.R - pixel.G), Math.Max(Math.Abs(pixel.G - pixel.B), Math.Abs(pixel.R - pixel.B))) > 64 ? 1 : 0;
                }
            }
        });
        Assert.True(black > 200, $"Only {black} pixels are black");
        Assert.True(colored > 100, $"Only {colored} pixels have a color");
    }

    [Fact]
    public void DrawMixedDirectionsOrdersEachParagraphInItsDirection()
    {
        var latin = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var hebrew = FixtureRoot.GetDirectory() / "fonts" / "noto-sans-hebrew" / "NotoSansHebrew-Regular.ttf";
        var output = GetPath("bidi.png");
        TextExamples.DrawMixedDirections(latin, hebrew, output);

        // The first paragraph is left-to-right and starts on the left; the second one is right-to-left and is aligned
        // on the right of the 340 pixels of the layout
        const string Shalom = "\u05E9\u05DC\u05D5\u05DD";
        var layout = TextLayout.Create(
            $"The word {Shalom} means peace.\n{Shalom} (peace) 2026",
            new TextStyle(new FontStack(Font.Load(latin), Font.Load(hebrew)), 24),
            new TextLayoutOptions { MaxWidth = 340 });
        Assert.False(layout.HasMissingGlyphs);
        Assert.Equal(2, layout.LineCount);
        Assert.False(layout.Lines[0].IsRightToLeft);
        Assert.True(layout.Lines[1].IsRightToLeft);
        Assert.Equal(0, layout.Lines[0].X, 1e-9);
        Assert.Equal(340, layout.Lines[1].X + layout.Lines[1].Width, 1e-9);

        // The Hebrew word ends the second line on the right, in reading order its first word
        var word = layout.GetSelectionRectangles(layout.Lines[1].Start, Shalom.Length);
        var rectangle = Assert.Single(word);
        Assert.Equal(340, rectangle.Right, 1e-6);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(360, (int)Math.Ceiling(layout.Height) + 20), image.Size);
        Assert.Equal(new Rgba32(255, 255, 255), image.Frames[0][15, (int)(10 + layout.Lines[1].Y + layout.Lines[1].Baseline - 8)]);
    }

    [Fact]
    public void DrawSyntheticStylesThickensAndSlantsTheRegularFont()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("synthetic.png");
        TextExamples.DrawSyntheticStyles(fontPath, output);

        // The bold takes room (1/24 of the size more per glyph); the oblique keeps the advances
        var font = Font.Load(fontPath);
        var regular = TextLayout.Create("Synthetic", new TextStyle(font, 28));
        var bold = TextLayout.Create("Synthetic", new TextStyle(font, 28) { Synthesis = FontSynthesis.Bold });
        var oblique = TextLayout.Create("Synthetic", new TextStyle(font, 28) { Synthesis = FontSynthesis.Oblique });
        Assert.Equal(regular.Width + (9 * 28 / 24.0), bold.Width, 1e-6);
        Assert.Equal(regular.Width, oblique.Width, 1e-9);
        Assert.True(bold.InkBounds.Width > regular.InkBounds.Width);
        Assert.True(oblique.InkBounds.Right > regular.InkBounds.Right);

        // Four lines; the bold line has more ink than the regular one, though its text is longer by a few letters only
        using var image = Image.Load<Rgba32>(output);
        var lineHeight = 28 * 1.362;
        Assert.Equal((int)Math.Ceiling(4 * lineHeight) + 20, image.Height);
        var ink = new long[4];
        image.Frames[0].ProcessPixelRows(pixels =>
        {
            for (var y = 10; y < pixels.Height - 10; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    ink[Math.Min(3, (int)((y - 10) / lineHeight))] += 255 - pixel.R;
                }
            }
        });
        Assert.True(ink[1] > ink[0] * 2.5, $"The bold line has an ink of {ink[1]} and the regular one {ink[0]}");
    }

    [Fact]
    public void DrawProductCardCutsTheDescriptionAfterTwoLines()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("card.png");
        Assert.True(TextExamples.DrawProductCard(fontPath, output));

        // Two lines of 16 pixels text, the last one ended by an ellipsis; without the limit, the text needs more
        var font = Font.Load(fontPath);
        const string Description = "Noise cancelling, thirty hours of battery life, a case that folds flat and a cable for the plane.";
        var options = new TextLayoutOptions { MaxWidth = 220, MaxLines = 2, Trimming = TextTrimming.Word };
        var layout = TextLayout.Create(Description, new TextStyle(font, 16), options);
        Assert.Equal(2, layout.LineCount);
        Assert.True(layout.IsTruncated);
        Assert.True(layout.Lines[1].Width <= 220);
        Assert.True(TextLayout.Create(Description, new TextStyle(font, 16), new TextLayoutOptions { MaxWidth = 220 }).LineCount > 2);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(240, image.Width);
        Assert.Equal((int)Math.Ceiling(TextLayout.Create("Wireless headphones", font, 20).Height + layout.Height) + 24, image.Height);
    }

    [Fact]
    public void DrawMeasuredTextDrawsTheBoxesOfTheLayout()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("measured.png");
        var ink = TextExamples.DrawMeasuredText(fontPath, output);

        // Two lines of 40 pixels: the ink box is inside the layout box here, but it is not that box
        var layout = TextLayout.Create("Typography\nlayout", Font.Load(fontPath), 40);
        Assert.Equal(layout.InkBounds, ink);
        Assert.Equal(2, layout.LineCount);
        Assert.Equal(2 * 40 * 1.362, layout.Height, 1e-9);
        Assert.True(ink.Top > 0 && ink.Height < layout.Height);

        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(new Size((int)Math.Ceiling(layout.Width) + 32, (int)Math.Ceiling(layout.Height) + 32), image.Size);
        var frame = image.Frames[0];

        // The layout box is filled, the margin is not, and the ink box is stroked in red
        Assert.Equal(new Rgba32(255, 255, 255), frame[4, 4]);
        Assert.Equal(new Rgba32(225, 235, 250), frame[(int)(16 + layout.Width - 2), 18]);
        var red = 0;
        frame.ProcessPixelRows(pixels =>
        {
            for (var y = 0; y < pixels.Height; y++)
            {
                foreach (var pixel in pixels.GetRowSpan(y))
                {
                    red += pixel.R > 150 && pixel.G < 120 && pixel.B < 120 ? 1 : 0;
                }
            }
        });
        Assert.True(red > 200, $"Only {red} pixels are red");
    }

    [Fact]
    public void DrawSelectionFindsTheCharacterAndHighlightsTheRange()
    {
        var fontPath = FixtureRoot.GetDirectory() / "fonts" / "noto-sans" / "NotoSans-Regular.subset.ttf";
        var output = GetPath("selection.png");

        // A click on the left half of the o of "two", on the second line: the caret goes before it
        var hit = TextExamples.DrawSelection(fontPath, new PointD(120, 50), output);
        Assert.Equal(1, hit.LineIndex);
        Assert.True(hit.IsInside);
        Assert.False(hit.IsTrailing);
        Assert.Equal("Select a part of the text\nacross two lines".IndexOf("o lines", StringComparison.Ordinal), hit.TextIndex);
        Assert.Equal(hit.TextIndex, hit.CaretIndex);

        // The selection spans the end of the first line and the start of the second one: one rectangle per line
        const string Text = "Select a part of the text\nacross two lines";
        var layout = TextLayout.Create(Text, Font.Load(fontPath), 24);
        var start = Text.IndexOf("the text", StringComparison.Ordinal);
        var selection = layout.GetSelectionRectangles(start, Text.IndexOf(" two", StringComparison.Ordinal) - start);
        Assert.HasCount(2, selection);
        Assert.Equal(layout.Lines[0].X + layout.Lines[0].Width, selection[0].Right, 1e-6);
        Assert.Equal(layout.Lines[1].X, selection[1].Left, 1e-6);

        using var image = Image.Load<Rgba32>(output);
        var frame = image.Frames[0];
        var caret = layout.GetCaretRectangle(hit.CaretIndex);
        Assert.Equal(0, caret.Width);
        Assert.Equal(new Rgba32(200, 30, 30), frame[(int)Math.Floor(10 + caret.X), (int)(10 + caret.Y + 4)]);
        Assert.Equal(new Rgba32(180, 210, 255), frame[(int)(10 + selection[0].X + 1), (int)(10 + selection[0].Y + 2)]);
        Assert.Equal(new Rgba32(255, 255, 255), frame[(int)(10 + selection[0].X - 3), (int)(10 + selection[0].Y + 2)]);
    }

    [Fact]
    public void ApplyOrientationTurnsThePixelsUpright()
    {
        // A landscape picture, red on the left and blue on the right, that the camera says to turn a quarter clockwise
        var input = GetPath("rotated.png");
        using (var source = new Image<Rgba32>(40, 20, Blue))
        {
            source.Draw(canvas => canvas.FillRectangle(Red, new RectangleD(0, 0, 20, 20)), TestContext.Current.CancellationToken);
            source.Metadata.Orientation = ExifOrientation.RightTop;
            source.Save(input);
        }

        var output = GetPath("upright.png");
        Assert.Equal(ExifOrientation.RightTop, ColorExamples.ApplyOrientation(input, output));

        // Upright pixels: a portrait whose top is the left of the stored picture, and nothing left to apply
        using var image = Image.Load<Rgba32>(output);
        Assert.Equal(ExifOrientation.TopLeft, image.Metadata.Orientation);
        Assert.Equal(new Size(20, 40), image.Size);
        Assert.Equal(Red, image.Frames[0][10, 5]);
        Assert.Equal(Blue, image.Frames[0][10, 35]);
    }

    [Fact]
    public void SoftProofShowsTheColorsOfThePrinter()
    {
        // Saturated red, which the CMYK printer cannot reproduce, and a light blue it can
        var display = IccTestProfiles.Rgb(IccTestProfiles.SrgbColorants, IccTestProfiles.SrgbCurve());
        var printer = IccTestProfiles.CmykLabLutAToB();
        ushort[] rgb = [65535, 0, 0, 30000, 40000, 50000];
        var preview = ColorExamples.SoftProof(display, printer, rgb);

        // The preview is the conversion to the inks, then back to the display (up to the 16-bit rounding of the inks)
        var options = new IccColorTransformOptions { GamutMapping = GamutMapping.ReduceChroma };
        var inks = new ushort[8];
        var expected = new ushort[6];
        IccColorTransform.Create(display, printer, options).Convert(rgb, inks);
        IccColorTransform.Create(printer, display, options).Convert(inks, expected);
        for (var i = 0; i < expected.Length; i++)
        {
            Assert.InRange(preview[i], expected[i] - 2, expected[i] + 2);
        }

        // The red of the print is not the red of the display
        Assert.NotEqual(rgb[..3], preview[..3]);
    }

    [Fact]
    public void SaveGifThresholdsOrFlattensAlphaAndReducesColors()
    {
        // 1,024 colors whose alpha grows from 0 (left) to 248 (right), in steps of 8
        var input = GetPath("soft.png");
        using (var image = new Image<Rgba32>(32, 32))
        {
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)(x * 8), (byte)(y * 8), 128, (byte)(x * 8));
                    }
                }
            });

            image.Save(input);
        }

        var gifPath = GetPath("threshold.gif");
        var flattenedPath = GetPath("flattened.gif");
        FormatExamples.SaveGif(input, gifPath, flattenedPath);

        using var gif = Image.Load<Rgba32>(gifPath);
        Assert.Equal(ImageFormat.Gif, gif.Metadata.SourceFormat);
        Assert.InRange(CountColors(gif.Frames[0]), 2, 64); // 64 entries, the transparent one included
        Assert.Equal(0, gif.Frames[0][7, 10].A); // alpha 56, below the threshold of 64
        Assert.Equal(255, gif.Frames[0][8, 10].A); // alpha 64: opaque
        for (var y = 0; y < gif.Height; y++)
        {
            for (var x = 0; x < gif.Width; x++)
            {
                Assert.True(gif.Frames[0][x, y].A is 0 or 255); // no partial alpha
            }
        }

        using var flattened = Image.Load<Rgba32>(flattenedPath);
        Assert.Equal(ImageFormat.Gif, flattened.Metadata.SourceFormat);
        Assert.InRange(CountColors(flattened.Frames[0]), 2, 256);
        for (var y = 0; y < flattened.Height; y++)
        {
            for (var x = 0; x < flattened.Width; x++)
            {
                Assert.Equal(255, flattened.Frames[0][x, y].A); // composited over the background
            }
        }

        Assert.Equal(new Rgba32(255, 255, 255), flattened.Frames[0][0, 0]); // fully transparent pixels are the background
        var data = File.ReadAllBytes(flattenedPath);
        // The image descriptor: separator, left and top 0, width and height 32, then the flags with the interlace bit
        var descriptor = data.AsSpan().IndexOf(new byte[] { 0x2C, 0, 0, 0, 0, 32, 0, 32, 0 });
        Assert.True(descriptor > 0);
        Assert.Equal(0x40, data[descriptor + 9] & 0x40);
    }

    [Fact]
    public void SaveTgaWritesRunLengthPacketsDetectedFromTheHeader()
    {
        var (apngPath, _) = BuildAnimation();
        var tgaPath = GetPath("frame.tga");
        Assert.Equal(ImageFormat.Tga, FormatExamples.SaveTga(apngPath, tgaPath));
        Assert.Equal(10, File.ReadAllBytes(tgaPath)[2]); // run-length true color

        using var expected = Image.Load<Rgba32>(apngPath);
        using var tga = Image.Load<Rgba32>(tgaPath);
        Assert.Equal(ImageFormat.Tga, tga.Metadata.SourceFormat);
        Assert.Equal(1, tga.Frames.Count);
        Assert.Equal(expected.Frames[0][10, 10], tga.Frames[0][10, 10]); // lossless
    }

    [Fact]
    public void SavePlainNetpbmFollowsThePixelFormatAndDropsAlpha()
    {
        var grayPath = GetPath("gray.png");
        using (var gray = new Image<Gray8>(4, 2, new Gray8(200)))
        {
            gray.Save(grayPath);
        }

        var pgmPath = GetPath("gray.pgm");
        FormatExamples.SavePlainNetpbm(grayPath, pgmPath);
        Assert.StartsWith("P2", File.ReadAllText(pgmPath), StringComparison.Ordinal);
        using (var pgm = Image.Load<Gray8>(pgmPath))
        {
            Assert.Equal(new Gray8(200), pgm.Frames[0][3, 1]);
        }

        var (apngPath, _) = BuildAnimation();
        var ppmPath = GetPath("frame.ppm");
        FormatExamples.SavePlainNetpbm(apngPath, ppmPath);
        Assert.StartsWith("P3", File.ReadAllText(ppmPath), StringComparison.Ordinal);
        using (var ppm = Image.Load<Rgba32>(ppmPath))
        {
            Assert.Equal(Red, ppm.Frames[0][0, 0]);
        }

        // A transparent pixel is composited over the white background
        var transparentPath = GetPath("transparent.png");
        using (var transparent = new Image<Rgba32>(2, 2, new Rgba32(0, 0, 0, 0)))
        {
            transparent.Save(transparentPath);
        }

        var whitePath = GetPath("white.ppm");
        FormatExamples.SavePlainNetpbm(transparentPath, whitePath);
        Assert.StartsWith("P3", File.ReadAllText(whitePath), StringComparison.Ordinal);
        using var white = Image.Load<Rgba32>(whitePath);
        Assert.Equal(new Rgba32(255, 255, 255), white.Frames[0][1, 1]);
    }

    [Fact]
    public void SaveAnimatedCursorWritesPngStepsExactDurationsAndText()
    {
        var (apngPath, _) = BuildAnimation(); // 100 ms and 1/30 s: 6 and 2 jiffies
        var aniPath = GetPath("busy.ani");
        FormatExamples.SaveAnimatedCursor(apngPath, aniPath);

        using var cursor = Image.Load(aniPath);
        Assert.Equal(ImageFormat.Ani, cursor.Metadata.SourceFormat);
        Assert.Equal(2, cursor.Frames.Count);
        Assert.Equal([new FrameDuration(1, 10), new FrameDuration(1, 30)], cursor.Frames.Select(frame => frame.Metadata.Duration));
        Assert.Null(cursor.Animation!.TotalPlays); // always loops
        Assert.Equal("Busy", cursor.Metadata.TextEntries.Single(entry => entry.Keyword == "Title").Value);
        Assert.Equal("Contoso", cursor.Metadata.TextEntries.Single(entry => entry.Keyword == "Author").Value);
        Assert.True(File.ReadAllBytes(aniPath).AsSpan().IndexOf(new byte[] { 0x89, 0x50, 0x4E, 0x47 }) > 0); // PNG payloads

        // 40 ms is not a whole number of sixtieths of a second: RequireExact rejects it before writing the file
        var inexactPath = GetPath("inexact.apng");
        using (var inexact = new Image<Rgba32>(16, 16, Red))
        {
            inexact.Frames[0].Metadata.Duration = FrameDuration.FromMilliseconds(40);
            inexact.AppendFrame().Metadata.Duration = FrameDuration.FromMilliseconds(40);
            inexact.Save(inexactPath);
        }

        var rejectedPath = GetPath("inexact.ani");
        Assert.Throws<UnsupportedImageFeatureException>(() => FormatExamples.SaveAnimatedCursor(inexactPath, rejectedPath));
        Assert.False(File.Exists(rejectedPath));
    }

    [Fact]
    public void ResizeWithAllCoresGivesTheSequentialPixels()
    {
        // Large enough for the resize to be split into bands: 400x300 output pixels
        var input = GetPath("large.png");
        using (var image = new Image<Rgba32>(800, 600))
        {
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = new Rgba32((byte)x, (byte)y, (byte)(x ^ y));
                    }
                }
            });
            image.Save(input);
        }

        var output = GetPath("parallel.png");
        PerformanceExamples.ResizeWithAllCores(input, output);

        using var sequential = Image.Load<Rgba32>(input);
        sequential.Resize(new ResizeOptions(400, 300) { Filter = ResamplingFilter.Lanczos3 });
        using var parallel = Image.Load<Rgba32>(output);
        Assert.Equal(new Size(400, 300), parallel.Size);
        for (var y = 0; y < 300; y += 7)
        {
            for (var x = 0; x < 400; x += 7)
            {
                Assert.Equal(sequential.Frames[0][x, y], parallel.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void SavePreviewDecodesAndWritesTheFirstFrameOnly()
    {
        var (_, gifPath) = BuildAnimation();
        var output = GetPath("preview.png");
        PerformanceExamples.SavePreview(gifPath, output);

        using var preview = Image.Load<Rgba32>(output);
        Assert.False(preview.IsAnimated);
        Assert.Equal(1, preview.Frames.Count);

        // The 64x64 animation fits in 128x128 without being enlarged; its first frame is red
        Assert.Equal(new Size(64, 64), preview.Size);
        Assert.Equal(new Rgba32(255, 0, 0), preview.Frames[0][32, 32]);
    }
}
