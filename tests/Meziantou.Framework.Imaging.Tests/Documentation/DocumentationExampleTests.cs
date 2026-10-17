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

        // Laplacian (-4 in the middle, 1 on the four sides): the dot is 4 * 100 - 4 * 200 = -400 -> 0, its side neighbors
        // 3 * 100 + 200 - 4 * 100 = 100, and flat areas are 0; alpha is kept
        using var edges = Image.Load<Rgba32>(edgesPath);
        Assert.Equal(new Rgba32(0, 0, 0), edges.Frames[0][2, 2]);
        Assert.Equal(new Rgba32(100, 100, 100), edges.Frames[0][1, 2]);
        Assert.Equal(new Rgba32(100, 100, 100), edges.Frames[0][2, 3]);
        Assert.Equal(new Rgba32(0, 0, 0), edges.Frames[0][1, 1]);
        Assert.Equal(new Rgba32(0, 0, 0), edges.Frames[0][0, 0]);
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
    [InlineData("avif/lossless-rgb10", "8x8 Rgba64, frames: 1, color: 1/13/0 full")] // PNG keeps the 16-bit samples
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
}
