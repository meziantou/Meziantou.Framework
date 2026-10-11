using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Streams;
using Meziantou.Framework.Imaging.TestHarness.WebP;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// WebP encoding through the public save and writer APIs. Output is validated with the independent
/// harness reader (<see cref="ReferenceWebP"/>: strict container rules, its own VP8L and ALPH decoder) and the raw fields
/// with <see cref="EncodedFieldInspector"/>; the library decoder is a supplementary round trip. libwebp and FFmpeg decode
/// the same kinds of output in the interoperability tests.
/// </summary>
public sealed class WebPEncoderTests
{
    [Fact]
    public void SettingsHaveDocumentedDefaultsAndRanges()
    {
        var encoder = new WebPEncoder();
        Assert.Equal(ImageFormat.WebP, encoder.Format);
        Assert.Equal(WebPCompression.Lossless, encoder.Compression);
        Assert.Equal(75, encoder.Quality);
        Assert.Equal(5, encoder.Effort);
        Assert.False(encoder.ClearTransparentColors);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(FrameDurationRounding.RoundToNearest, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);

        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Effort = 10 });
        _ = new WebPEncoder { Quality = 0, Effort = 0 };
        _ = new WebPEncoder { Quality = 100, Effort = 9 };
    }

    [Fact]
    public void PathSavesInferWebPFromTheExtension()
    {
        var directory = FullPath.FromFileSystemInfo(Directory.CreateTempSubdirectory("meziantou-webp-"));
        try
        {
            using var image = CreateImage(5, 3, seed: 1, transparent: false);
            var path = directory / "image.webp";
            image.Save(path);
            var reader = ReferenceWebP.Parse(File.ReadAllBytes(path));
            Assert.True(Assert.Single(reader.Frames).IsLossless);
            Assert.Equal(ImageFormat.WebP, Image.Identify(path).Format);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LosslessOutputPreservesHiddenColorsUnlessClearingIsRequested(bool clear)
    {
        using var image = CreateImage(7, 5, seed: 2, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new WebPEncoder { ClearTransparentColors = clear });
        var reader = ReferenceWebP.Parse(data);
        Assert.False(reader.IsExtended);
        Assert.True(reader.GetLosslessAlphaHint(0));
        var decoded = reader.DecodeFrame(0).Span.ToArray();
        for (var i = 0; i < source.Length; i += 4)
        {
            var expected = source.AsSpan(i, 4).ToArray();
            if (clear && expected[3] == 0)
            {
                expected = [0, 0, 0, 0];
            }

            Assert.Equal(expected, decoded.AsSpan(i, 4).ToArray());
        }

        Assert.Contains(source.Chunk(4), pixel => pixel[3] == 0 && (pixel[0] | pixel[1] | pixel[2]) != 0);
    }

    [Fact]
    public void OpaqueImagesClearTheAlphaHint()
    {
        using var image = CreateImage(4, 4, seed: 3, transparent: false);
        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder()));
        Assert.False(reader.GetLosslessAlphaHint(0));
        using var decoded = Image.Load(Encode(image, new WebPEncoder()));
        Assert.Equal(PixelFormat.Rgb24, decoded.PixelFormat);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void LossyOutputStoresAlphaLosslessly(int quality)
    {
        using var image = CreateImage(19, 11, seed: 4, transparent: true);
        var source = GetBytes(image);
        var data = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality });
        var reader = ReferenceWebP.Parse(data);
        Assert.True(reader.IsExtended);
        Assert.True(reader.HasAlphaFlag);
        var frame = Assert.Single(reader.Frames);
        Assert.False(frame.IsLossless);
        Assert.NotNull(frame.Alpha);
        Assert.Equal(source.Where((_, index) => index % 4 == 3).ToArray(), reader.DecodeAlpha(0));
    }

    [Fact]
    public void QualityTradesSizeForFidelity()
    {
        using var image = CreateGradient(48, 32);
        var source = GetBytes(image);
        var previousSize = 0;
        var previousError = double.MaxValue;
        foreach (var quality in new[] { 10, 50, 90 })
        {
            var data = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy, Quality = quality });
            using var decoded = Image.Load<Rgba32>(data);
            var error = MeanAbsoluteError(source, GetBytes(decoded));
            Assert.HasCountGreaterThan(previousSize, data);
            Assert.True(error < previousError, $"quality {quality}: mean error {error}");
            previousSize = data.Length;
            previousError = error;
        }

        Assert.True(previousError < 3, $"quality 90: mean error {previousError}");
    }

    [Fact]
    public void EffortNeverChangesLosslessPixels()
    {
        using var image = CreateGradient(37, 23);
        var source = GetBytes(image);
        for (var effort = 0; effort <= 9; effort++)
        {
            var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder { Effort = effort }));
            var decoded = reader.DecodeFrame(0).Span.ToArray();
            for (var i = 0; i < source.Length; i++)
            {
                Assert.Equal(i % 4 == 3 ? (byte)255 : source[i], decoded[i]);
            }
        }
    }

    [Fact]
    public void SixteenBitInputRequiresExplicitReduction()
    {
        using var image = Image.ImportPixelData<Rgba64>([new Rgba64(0x1234, 0x8000, 0xFFFF, 0x7FFF), new Rgba64(0, 0x00FF, 0x0080, 0xFFFF)], 2, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new WebPEncoder()));
        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder { AllowBitDepthReduction = true }));

        // (v * 255 + 32767) / 65535: nearest 8-bit value
        Assert.Equal(new byte[] { 0x12, 0x80, 0xFF, 0x7F, 0x00, 0x01, 0x00, 0xFF }, reader.DecodeFrame(0).Span.ToArray());
    }

    [Fact]
    public void GrayInputIsWrittenAsRgb()
    {
        using var image = Image.ImportPixelData<Gray8>([new Gray8(0), new Gray8(77), new Gray8(255)], 3, 1);
        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder()));
        Assert.Equal(new byte[] { 0, 0, 0, 255, 77, 77, 77, 255, 255, 255, 255, 255 }, reader.DecodeFrame(0).Span.ToArray());
    }

    [Fact]
    public void MetadataIsWrittenInTheExtendedLayout()
    {
        using var image = CreateImage(4, 3, seed: 5, transparent: false);
        var icc = JpegEncoderTests.CreateIccProfile("RGB ", 200);
        image.Metadata.IccProfile = new IccProfile(new MetadataBlob(icc));
        image.Metadata.XmpProfile = new XmpProfile(new MetadataBlob("<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"/>"u8));
        image.Metadata.Orientation = ExifOrientation.RightTop;
        var data = Encode(image, new WebPEncoder());
        var reader = ReferenceWebP.Parse(data);
        Assert.True(reader.IsExtended);
        Assert.Equal(["VP8X", "ICCP", "VP8L", "EXIF", "XMP "], reader.Chunks.Select(chunk => chunk.FourCC));
        Assert.False(reader.HasAlphaFlag);
        Assert.Equal(icc, reader.Icc!.Value.ToArray());

        var fields = EncodedFieldInspector.Inspect("webp", data);
        Assert.NotNull(fields.Exif);
        Assert.True(fields.Exif.Value.Span.StartsWith("II*\0"u8) || fields.Exif.Value.Span.StartsWith("MM\0*"u8), "The EXIF chunk starts with the TIFF header (no Exif\\0\\0 prefix).");

        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.Equal(ExifOrientation.RightTop, info.Metadata.Orientation);
        Assert.Equal(icc, info.Metadata.IccProfile!.Data.ToArray());
        Assert.NotNull(info.Metadata.XmpProfile);
    }

    [Fact]
    public void UnrepresentableMetadataFollowsTheHandlingPolicy()
    {
        using var image = CreateImage(2, 2, seed: 6, transparent: false);
        image.Metadata.TextEntries.Add(new ImageTextEntry("Title", "lorem ipsum"));
        image.Metadata.Resolution = new ImageResolution(300, 300);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new WebPEncoder()));

        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder { MetadataHandling = MetadataHandling.DiscardUnsupported }));
        Assert.False(reader.IsExtended);
    }

    [Fact]
    public void AnimationsAreFullCanvasFramesWithPatchedSizes()
    {
        using var image = CreateImage(6, 4, seed: 7, transparent: false);
        using var second = CreateImage(6, 4, seed: 8, transparent: true);
        image.AppendFrame(second.Frames[0]);
        image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
        image.Frames[1].Metadata.Duration = new FrameDuration(7, 1000);
        image.Animation = new AnimationMetadata { TotalPlays = 3 };

        var data = Encode(image, new WebPEncoder());
        Assert.Equal(data.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4)));
        var reader = ReferenceWebP.Parse(data);
        Assert.Equal(3, reader.Animation!.LoopCount);
        Assert.True(reader.HasAlphaFlag); // patched: only the second frame has transparency
        Assert.Equal([100, 7], reader.Frames.Select(frame => frame.DurationMilliseconds));
        Assert.All(reader.Frames, frame => Assert.Equal((0, 0, 6, 4, false, false), (frame.X, frame.Y, frame.Width, frame.Height, frame.AlphaBlend, frame.DisposeToBackground)));
        Assert.Equal(GetBytes(image, 1), reader.DecodeFrame(1).Span.ToArray());

        using var decoded = Image.Load<Rgba32>(data);
        Assert.Equal(2, decoded.Frames.Count);
        Assert.Equal(3, decoded.Animation!.TotalPlays);
        Assert.Equal(new FrameDuration(7, 1000), decoded.Frames[1].Metadata.Duration);
    }

    [Fact]
    public void OpaqueAnimationsClearTheAlphaFlag()
    {
        using var image = CreateImage(3, 3, seed: 9, transparent: false);
        using var second = CreateImage(3, 3, seed: 10, transparent: false);
        image.AppendFrame(second.Frames[0]);
        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy }));
        Assert.False(reader.HasAlphaFlag);
        Assert.Equal(0, reader.Animation!.LoopCount); // no animation settings: infinite
        Assert.All(reader.Frames, frame => Assert.Null(frame.Alpha));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData(1, 1)]
    [InlineData(65535, 65535)]
    public void TotalPlaysMapToTheLoopCount(int? totalPlays, int loopCount)
    {
        using var image = CreateAnimation(2);
        image.Animation = new AnimationMetadata { TotalPlays = totalPlays };
        Assert.Equal(loopCount, ReferenceWebP.Parse(Encode(image, new WebPEncoder())).Animation!.LoopCount);
    }

    [Fact]
    public void UnrepresentablePlayCountsFailBeforeAnyOutput()
    {
        using var image = CreateAnimation(2);
        image.Animation = new AnimationMetadata { TotalPlays = 65536 };
        using var stream = new TestOutputStream { Seekable = true, AllowPatching = true };
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new WebPEncoder()));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Theory]
    [InlineData(0, 1, 0)]
    [InlineData(1, 3, 333)]
    [InlineData(2, 3, 667)]
    [InlineData(1, 2000, 1)] // 0.5 ms: ties round up
    [InlineData(16777215, 1000, 16777215)]
    public void DurationsAreRoundedToTheNearestMillisecond(long numerator, long denominator, int milliseconds)
    {
        using var image = CreateAnimation(2);
        image.Frames[1].Metadata.Duration = new FrameDuration(numerator, denominator);
        Assert.Equal(milliseconds, ReferenceWebP.Parse(Encode(image, new WebPEncoder())).Frames[1].DurationMilliseconds);
    }

    [Fact]
    public void InexactDurationsFailWhenExactnessIsRequired()
    {
        using var image = CreateAnimation(2);
        image.Frames[1].Metadata.Duration = new FrameDuration(1, 3);
        using var stream = new TestOutputStream { Seekable = true, AllowPatching = true };
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new WebPEncoder { DurationRounding = FrameDurationRounding.RequireExact }));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void DurationsBeyondTheFieldFail()
    {
        using var image = CreateAnimation(2);
        image.Frames[1].Metadata.Duration = new FrameDuration(16777216, 1000);
        using var stream = new TestOutputStream { Seekable = true, AllowPatching = true };
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(stream, new WebPEncoder()));
    }

    [Fact]
    public async Task AnimationsRejectNonSeekableOutputBeforeWriting()
    {
        using var image = CreateAnimation(2);
        using var stream = new TestOutputStream();
        var exception = Assert.Throws<ArgumentException>(() => image.Save(stream, new WebPEncoder()));
        Assert.Equal("stream", exception.ParamName);
        await Assert.ThrowsAsync<ArgumentException>(() => image.SaveAsync(stream, new WebPEncoder(), XunitCancellationToken));
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = new WebPEncoder() })); // unknown frame count
        Assert.Throws<ArgumentException>(() => Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(2, 2) { Encoder = new WebPEncoder(), ExpectedFrameCount = 3 }));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void StillImagesStreamToNonSeekableOutput()
    {
        using var image = CreateImage(9, 7, seed: 11, transparent: true);
        var seekable = Encode(image, new WebPEncoder());
        using var stream = new TestOutputStream();
        image.Save(stream, new WebPEncoder());
        Assert.Equal(seekable, stream.ToArray());

        // A writer that declares one frame and no animation settings is a still image
        using var writerOutput = new TestOutputStream();
        using (var writer = Image.CreateWriter<Rgba32>(writerOutput, new ImageWriterOptions(9, 7) { Encoder = new WebPEncoder(), ExpectedFrameCount = 1 }))
        {
            writer.WriteFrame((ImageFrame<Rgba32>)image.Frames[0]);
            writer.Complete();
        }

        Assert.Equal(seekable, writerOutput.ToArray());
    }

    [Fact]
    public async Task WritersStreamFramesAndPatchTheHeaderAtCompletion()
    {
        using var frames = CreateAnimation(3);
        var stream = new TestOutputStream { Seekable = true, AllowPatching = true };
        await using (var writer = Image.CreateWriter<Rgba32>(stream, new ImageWriterOptions(frames.Width, frames.Height) { Encoder = new WebPEncoder(), Animation = new AnimationMetadata { TotalPlays = 2 } }))
        {
            var written = new List<long>();
            foreach (var frame in frames.Frames)
            {
                await writer.WriteFrameAsync((ImageFrame<Rgba32>)frame, XunitCancellationToken);
                written.Add(stream.BytesWritten);
            }

            Assert.True(written[0] > 0 && written[1] > written[0] && written[2] > written[1], "Frames are written as they arrive.");
            await writer.CompleteAsync(XunitCancellationToken);
        }

        var reader = ReferenceWebP.Parse(stream.ToArray());
        Assert.Equal(3, reader.Frames.Count);
        Assert.Equal(2, reader.Animation!.LoopCount);
    }

    [Fact]
    public void LossyDimensionsAreLimitedTo16383()
    {
        using var image = Image.ImportPixelData<Rgba32>(new Rgba32[16384], 16384, 1);
        Assert.Throws<UnsupportedImageFeatureException>(() => image.Save(new ThrowingStream(), new WebPEncoder { Compression = WebPCompression.Lossy }));
        ReferenceWebP.Parse(Encode(image, new WebPEncoder { Effort = 0 })); // lossless allows 16384
    }

    [Fact]
    public async Task CanceledSavesThrowAndWriteNothing()
    {
        using var image = CreateGradient(64, 64);
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var stream = new TestOutputStream();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => image.SaveAsync(stream, new WebPEncoder(), cancellation.Token));
        Assert.Equal(0, stream.BytesWritten);
    }

    [Fact]
    public void EncodingIsDeterministic()
    {
        using var image = CreateGradient(33, 17);
        foreach (var encoder in new[] { new WebPEncoder(), new WebPEncoder { Compression = WebPCompression.Lossy, Effort = 9 } })
        {
            Assert.Equal(Encode(image, encoder), Encode(image, encoder));
        }
    }

    private static byte[] Encode(Image image, WebPEncoder encoder)
    {
        using var stream = new MemoryStream();
        image.Save(stream, encoder);
        return stream.ToArray();
    }

    private static byte[] GetBytes(Image image, int frame = 0)
    {
        var bytes = new byte[image.Width * image.Height * 4];
        image.Frames[frame].CopyPixelBytesTo(bytes);
        return bytes;
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static Image<Rgba32> CreateImage(int width, int height, int seed, bool transparent)
    {
        ReadOnlySpan<byte> alphas = [0, 255, 128, 1, 254];
        var random = new Random(seed);
        var pixels = new Rgba32[width * height];
        for (var i = 0; i < pixels.Length; i++)
        {
            var alpha = transparent ? alphas[i % alphas.Length] : (byte)255;
            pixels[i] = new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha);
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static Image<Rgba32> CreateGradient(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = new Rgba32((byte)(x * 255 / (width - 1)), (byte)(255 - (y * 255 / (height - 1))), (byte)((x + y) * 3), 255);
            }
        }

        return Image.ImportPixelData<Rgba32>(pixels, width, height);
    }

    private static Image<Rgba32> CreateAnimation(int frameCount)
    {
        var image = CreateImage(4, 4, seed: 100, transparent: false);
        for (var i = 1; i < frameCount; i++)
        {
            using var frame = CreateImage(4, 4, seed: 100 + i, transparent: false);
            image.AppendFrame(frame.Frames[0]);
        }

        return image;
    }

    private static double MeanAbsoluteError(byte[] expected, byte[] actual)
    {
        double sum = 0;
        var count = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            if (i % 4 == 3)
                continue;

            sum += Math.Abs(expected[i] - actual[i]);
            count++;
        }

        return sum / count;
    }
}
