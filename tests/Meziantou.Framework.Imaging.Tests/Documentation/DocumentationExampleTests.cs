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
        var animated = GetPath("animation.jxl");
        Assert.Equal("JpegXl 32x16 Rgba64, 16 bits", Examples.SaveJpegXl(photo, jxl, apngPath, animated));
        using (var image = Image.Load<Rgba64>(jxl))
        {
            Assert.Equal(ImageFormat.JpegXl, image.Metadata.SourceFormat);
            Assert.Equal(color, image.Frames[0][16, 8]); // 16 bits, lossless
        }

        using var animation = Image.Load<Rgba32>(animated);
        using var expected = Image.Load<Rgba32>(apngPath);
        Assert.Equal(2, animation.Frames.Count);
        Assert.Equal(3, animation.Animation!.TotalPlays);
        Assert.Equal(new FrameDuration(33, 1000), animation.Frames[1].Metadata.Duration); // nearest millisecond
        Assert.Equal(expected.Frames[1][63, 63], animation.Frames[1][63, 63]);
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
    public void ConvertAvifDoesNotRelabelAnHdrImage()
    {
        // BT.2020 primaries with the PQ transfer characteristics: PNG cannot store that description, and the strict save
        // of the example fails instead of writing the samples as if they were sRGB
        var fixture = GoldenCorpus.Default.Get("avif/lossy-420-bt2020-limited-10bit");
        var png = GetPath("hdr.png");
        Assert.Throws<UnsupportedImageFeatureException>(() => Examples.ConvertAvif(fixture.InputPath, png));
        Assert.False(File.Exists(png));

        using var image = Image.Load(fixture.InputPath);
        Assert.Equal(new CicpColorDescription(9, 16, 9, fullRange: false), image.Metadata.Cicp);
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
