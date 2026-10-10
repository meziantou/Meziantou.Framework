using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Samples;

namespace Meziantou.Framework.Imaging.Tests.Documentation;

/// <summary>
/// Runs the published usage examples (samples/Meziantou.Framework.Imaging.Samples/Examples.cs, linked into this project) and
/// checks that they do what the documentation says. Inputs are built by the examples themselves.
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
}
