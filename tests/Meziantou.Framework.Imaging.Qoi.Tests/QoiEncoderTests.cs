using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.Tests.Codecs;
using Meziantou.Framework.Imaging.TestHarness.Qoi;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// QOI encoding through the public save and writer APIs: literal chunk sequences for hand-computed inputs, exact round
/// trips checked with the independent harness reader (<see cref="ReferenceQoi"/>), header fields per pixel format, the
/// transfer-function label, the precision, metadata and animation policies, path inference and bounded streaming memory.
/// FFmpeg decodes the outputs in the interop job; the corpus checks byte equality with the qoi.h reference encoder.
/// </summary>
public sealed class QoiEncoderTests
{
    private static readonly byte[] EndMarker = [0, 0, 0, 0, 0, 0, 0, 1];

    [Fact]
    public void SettingsHaveDocumentedDefaults()
    {
        var encoder = new QoiEncoder();
        Assert.Equal(ImageFormat.Qoi, encoder.Format);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);
    }

    [Fact]
    public void ChunksAreSelectedAsSpecified()
    {
        // (1,1,1) from the initial (0,0,0,255): DIFF +1 +1 +1; (200,0,0): too far for DIFF/LUMA, RGB; (1,1,1) again: INDEX of
        // its slot; alpha changes: RGBA
        Assert.Equal(
            [0x7F, 0xFE, 200, 0, 0, Hash(1, 1, 1, 255), 0xFF, 1, 1, 1, 0],
            EncodeChunks([new(1, 1, 1, 255), new(200, 0, 0, 255), new(1, 1, 1, 255), new(1, 1, 1, 0)], 4, 1));

        // The initial pixel repeated: one run; a run is flushed at the last pixel
        Assert.Equal([0xC2], EncodeChunks([new(0, 0, 0, 255), new(0, 0, 0, 255), new(0, 0, 0, 255)], 3, 1));

        // 64 equal pixels: LUMA (dg +5), then runs of 62 and 1 (the longest run is 62)
        Assert.Equal([0xA5, 0x88, 0xFD, 0xC0], EncodeChunks([.. Enumerable.Repeat(new Rgba32(5, 5, 5, 255), 64)], 8, 8));

        // DIFF bias extremes and LUMA extremes
        Assert.Equal(
            [0x40, 0x80, 0x0F, 0xBF, 0xF0],
            EncodeChunks([new(254, 254, 254, 255), new(214, 222, 229, 255), new(252, 253, 252, 255)], 3, 1));
    }

    [Theory]
    [InlineData(PixelFormat.Rgba32, 4)]
    [InlineData(PixelFormat.Bgra32, 4)]
    [InlineData(PixelFormat.Rgb24, 3)]
    [InlineData(PixelFormat.Gray8, 3)]
    public void EightBitPixelFormatsRoundTripExactly(PixelFormat format, int channels)
    {
        using var source = CreateImage(23, 11, seed: (int)format);
        using var image = ConvertTo(source, format);
        var data = Save(image, new QoiEncoder());
        var reference = ReferenceQoi.Parse(data);
        Assert.Equal((23u, 11u), (reference.Width, reference.Height));
        Assert.Equal(channels, reference.Channels);
        Assert.Equal(0, reference.ColorSpace);
        Assert.Equal(0, reference.TrailingBytes);

        // The reference reading equals the source converted to RGBA (gray replicated, opaque without alpha)
        using var expected = image.CloneAs<Rgba32>();
        var bytes = new byte[23 * 11 * 4];
        expected.Frames[0].CopyPixelBytesTo(bytes);
        Assert.Equal(bytes, reference.Rgba.ToArray());

        // The library decoder agrees, in the declared layout
        using var decoded = Image.Load(data);
        Assert.Equal(channels == 4 ? PixelFormat.Rgba32 : PixelFormat.Rgb24, decoded.PixelFormat);
        var declared = new byte[23 * 11 * channels];
        decoded.Frames[0].CopyPixelBytesTo(declared);
        Assert.Equal(reference.GetDeclaredPixels(), declared);
    }

    [Fact]
    public void HiddenColorsOfTransparentPixelsArePreserved()
    {
        Rgba32[] pixels = [new(10, 20, 30, 0), new(200, 100, 50, 0), new(10, 20, 30, 0), new(0, 0, 0, 0)];
        using var image = Image.ImportPixelData<Rgba32>(pixels, 2, 2);
        using var decoded = Image.Load<Rgba32>(Save(image, new QoiEncoder()));
        var actual = new Rgba32[4];
        decoded.Frames[0].CopyPixelDataTo(actual);
        Assert.Equal(pixels, actual);
    }

    [Theory]
    [InlineData(PixelFormat.Rgba64, 4)]
    [InlineData(PixelFormat.Gray16, 3)]
    public void SixteenBitPixelFormatsRequireAnExplicitReduction(PixelFormat format, int channels)
    {
        using var source = CreateImage(7, 5, seed: 3);
        using var image = ConvertTo(source, format);
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new QoiEncoder()));
        Assert.Equal("Bit depth reduction", exception.Feature);
        Assert.Equal(0, stream.BytesWritten);

        var data = Save(image, new QoiEncoder { AllowBitDepthReduction = true });
        var reference = ReferenceQoi.Parse(data);
        Assert.Equal(channels, reference.Channels);
        using var expected = image.CloneAs<Rgba32>(); // nearest rounding, as every 16-to-8-bit conversion
        var bytes = new byte[7 * 5 * 4];
        expected.Frames[0].CopyPixelBytesTo(bytes);
        Assert.Equal(bytes, reference.Rgba.ToArray());
    }

    [Fact]
    public void TheTransferFunctionIsTheColorspaceField()
    {
        using var image = CreateImage(4, 4, seed: 9);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var linear = Save(image, new QoiEncoder());
        Assert.Equal(1, linear[13]);
        using (var decoded = Image.Load(linear))
        {
            Assert.Equal(ColorTransferFunction.Linear, decoded.Metadata.TransferFunction);
        }

        // Strip writes no optional metadata: the default sRGB indicator, the samples unchanged
        var stripped = Save(image, new QoiEncoder { MetadataHandling = MetadataHandling.Strip });
        Assert.Equal(0, stripped[13]);
        Assert.Equal(linear[14..], stripped[14..]);

        image.Metadata.TransferFunction = ColorTransferFunction.Srgb;
        Assert.Equal(0, Save(image, new QoiEncoder())[13]);
    }

    [Fact]
    public void LinearSamplesAreNeverSilentlyRelabeledAsSrgb()
    {
        using var image = CreateImage(4, 4, seed: 4);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        foreach (var encoder in new ImageEncoder[] { new PngEncoder(), new JpegEncoder(), new WebPEncoder(), new GifEncoder { AlphaMode = GifAlphaMode.Threshold } })
        {
            using var stream = new TestOutputStream();
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, encoder));
            Assert.StartsWith("Metadata: linear transfer function", exception.Feature, StringComparison.Ordinal);
            Assert.Equal(0, stream.BytesWritten);
        }

        // An explicit discard is accepted
        using var png = Image.Load(Save(image, new PngEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        Assert.Equal(ColorTransferFunction.Srgb, png.Metadata.TransferFunction);
    }

    [Fact]
    public void OtherMetadataFollowsTheMetadataPolicy()
    {
        var cases = new (string Feature, Action<ImageMetadata> Set)[]
        {
            ("Metadata: ICC color profile", metadata => metadata.IccProfile = new IccProfile(new MetadataBlob(TestRawImage.CreateIccHeader("RGB "u8)))),
            ("Metadata: EXIF profile", metadata => metadata.ExifProfile = new ExifProfile(new MetadataBlob([0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00]))),
            ("Metadata: EXIF orientation other than TopLeft", metadata => metadata.Orientation = ExifOrientation.RightTop),
            ("Metadata: XMP packet", metadata => metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x='adobe:ns:meta/'/>"u8))),
            ("Metadata: resolution", metadata => metadata.Resolution = new ImageResolution(72, 72)),
            ("Metadata: text entry 'Comment'", metadata => metadata.TextEntries.Add(new ImageTextEntry(ImageTextEntry.CommentKeyword, "lorem ipsum"))),
        };
        foreach (var (feature, set) in cases)
        {
            using var image = CreateImage(3, 2, seed: 1);
            set(image.Metadata);
            using var stream = new TestOutputStream();
            Assert.Equal(feature, Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new QoiEncoder())).Feature);
            Assert.Equal(0, stream.BytesWritten);

            var discarded = Save(image, new QoiEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported });
            var stripped = Save(image, new QoiEncoder { MetadataHandling = MetadataHandling.Strip });
            Assert.Equal(discarded, stripped);
            ReferenceQoi.Parse(discarded);
        }
    }

    [Fact]
    public void QoiStoresOneStillImage()
    {
        using var image = CreateImage(4, 3, seed: 5);
        image.AppendFrame();
        using var stream = new TestOutputStream();
        Assert.Equal("Animation", Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new QoiEncoder())).Feature);
        Assert.Equal(0, stream.BytesWritten);

        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(new MemoryStream(), new ImageWriterOptions(4, 3) { Encoder = new QoiEncoder(), ExpectedFrameCount = 2 }));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(new MemoryStream(), new ImageWriterOptions(4, 3) { Encoder = new QoiEncoder(), Animation = new AnimationMetadata() }));

        // A writer without an expected count accepts exactly one frame
        using var still = CreateImage(4, 3, seed: 6);
        using var output = new MemoryStream();
        using (var writer = Image.CreateWriter<Rgba32>(output, new ImageWriterOptions(4, 3) { Encoder = new QoiEncoder() }))
        {
            writer.WriteFrame(still.Frames[0]);
            Assert.Throws<InvalidOperationException>(() => writer.WriteFrame(still.Frames[0]));
            writer.Complete();
        }

        Assert.Equal(EndMarker, output.ToArray()[^8..]);
        ReferenceQoi.Parse(output.ToArray());
    }

    [Fact]
    public void PathSavesInferQoiFromTheExtension()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-qoi-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1);
            var path = directory / "image.QOI";
            image.Save(path);
            Assert.Equal(ImageFormat.Qoi, Image.Identify(path).Format);
            ReferenceQoi.Parse(File.ReadAllBytes(path));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task WritersStreamBoundedBandsOfPixels(bool asynchronous)
    {
        const int Width = 1024;
        const int Height = 768;
        using var image = CreateImage(Width, Height, seed: 11);
        using var stream = new TestOutputStream();
        long peak;
        using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(Width, Height) { Encoder = new QoiEncoder() }))
        {
            Assert.Equal(0, stream.BytesWritten);
            if (asynchronous)
            {
                await writer.WriteFrameAsync(image.Frames[0], XunitCancellationToken);
                await writer.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                writer.WriteFrame(image.Frames[0]);
                writer.Complete();
            }

            peak = writer.Core.Scope.GetDiagnostics().PeakLiveBytes;
            Assert.Equal(0, writer.Core.Scope.LiveBytes);
        }

        // Rgba32 rows are read in place: the output buffer and its bounded growth, never the image
        var imageBytes = (long)Width * Height * 4;
        Assert.True(peak < imageBytes / 8, $"Peak writer memory {peak} bytes for a {imageBytes}-byte image.");
        Assert.InRange(asynchronous ? stream.AsynchronousWriteCount : stream.SynchronousWriteCount, 4, int.MaxValue);
        var reference = ReferenceQoi.Parse(stream.ToArray());
        var bytes = new byte[imageBytes];
        image.Frames[0].CopyPixelBytesTo(bytes);
        Assert.Equal(bytes, reference.Rgba.ToArray());
    }

    [Fact]
    public void LinearLightResizingFiltersLinearSamplesDirectly()
    {
        using var linear = CreateImage(16, 12, seed: 2);
        linear.Metadata.TransferFunction = ColorTransferFunction.Linear;
        using var encoded = linear.Clone();
        using var srgb = linear.Clone();
        srgb.Metadata.TransferFunction = ColorTransferFunction.Srgb;

        linear.Resize(new ResizeOptions(7, 5) { Mode = ResizeMode.Stretch, WorkingSpace = ResizeWorkingSpace.LinearSrgb }, XunitCancellationToken);
        encoded.Resize(new ResizeOptions(7, 5) { Mode = ResizeMode.Stretch, WorkingSpace = ResizeWorkingSpace.Encoded }, XunitCancellationToken);
        srgb.Resize(new ResizeOptions(7, 5) { Mode = ResizeMode.Stretch, WorkingSpace = ResizeWorkingSpace.LinearSrgb }, XunitCancellationToken);

        Assert.Equal(GetPixels(encoded.Frames[0]), GetPixels(linear.Frames[0]));
        Assert.NotEqual(GetPixels(srgb.Frames[0]), GetPixels(linear.Frames[0]));
        Assert.Equal(ColorTransferFunction.Linear, linear.Metadata.TransferFunction);
    }

    [Fact]
    public void TheTransferFunctionIsValidatedAndCloned()
    {
        var metadata = new ImageMetadata();
        Assert.Equal(ColorTransferFunction.Srgb, metadata.TransferFunction);
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.TransferFunction = (ColorTransferFunction)2);
        metadata.TransferFunction = ColorTransferFunction.Linear;
        Assert.Equal(ColorTransferFunction.Linear, metadata.Clone().TransferFunction);
    }

    /// <summary>Converts the test image (flattened on white when the format has no alpha).</summary>
    private static Image ConvertTo(Image<Rgba32> source, PixelFormat format)
    {
        var flatten = new PixelConversionOptions { BackgroundColor = new Rgba64(65535, 65535, 65535, 65535) };
        return format switch
        {
            PixelFormat.Rgba32 => source.Clone(),
            PixelFormat.Bgra32 => source.CloneAs<Bgra32>(),
            PixelFormat.Rgba64 => source.CloneAs<Rgba64>(),
            PixelFormat.Rgb24 => source.CloneAs<Rgb24>(flatten),
            PixelFormat.Gray8 => source.CloneAs<Gray8>(flatten),
            PixelFormat.Gray16 => source.CloneAs<Gray16>(flatten),
            _ => throw new ArgumentOutOfRangeException(nameof(format)),
        };
    }

    private static byte[] EncodeChunks(Rgba32[] pixels, int width, int height)
    {
        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        var data = Save(image, new QoiEncoder());
        Assert.Equal("qoif"u8.ToArray(), data[..4]);
        Assert.Equal((uint)width, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(4)));
        Assert.Equal((uint)height, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(8)));
        Assert.Equal(4, data[12]);
        Assert.Equal(0, data[13]);
        Assert.Equal(EndMarker, data[^8..]);
        return data[14..^8];
    }

    private static byte Hash(int r, int g, int b, int a) => (byte)QoiDecoderTests.Hash(r, g, b, a);

    private static byte[] Save(Image image, ImageEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    /// <summary>Flat areas, gradients, noise, translucent and fully transparent pixels with defined colors.</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateImage(int width, int height, int seed)
    {
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (((x / 3) + y) % 4) switch
                {
                    0 => new Rgba32(30, 60, 90, 255),
                    1 => new Rgba32((byte)(x * 5), (byte)(y * 7), (byte)(x + y), 255),
                    2 => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256)),
                    _ => new Rgba32((byte)random.Next(256), (byte)x, (byte)y, 0),
                };
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }
}
