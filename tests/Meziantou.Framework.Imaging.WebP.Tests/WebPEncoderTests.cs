using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;
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
        Assert.Equal(100, encoder.AlphaQuality);
        Assert.Equal(100, encoder.NearLossless);
        Assert.False(encoder.ClearTransparentColors);
        Assert.False(encoder.AllowBitDepthReduction);
        Assert.Equal(FrameDurationRounding.RoundToNearest, encoder.DurationRounding);
        Assert.Equal(MetadataHandling.Strict, encoder.MetadataHandling);

        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Quality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Quality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Effort = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { Effort = 10 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { AlphaQuality = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { AlphaQuality = 101 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { NearLossless = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new WebPEncoder { NearLossless = 101 });
        _ = new WebPEncoder { Quality = 0, Effort = 0, AlphaQuality = 0, NearLossless = 0 };
        _ = new WebPEncoder { Quality = 100, Effort = 9, AlphaQuality = 100, NearLossless = 100 };
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

    [Theory]
    [InlineData(3)]
    [InlineData(5)]
    [InlineData(9)]
    public void LossyAlphaOfSoftMasksIsExactAtEveryEffort(int effort)
    {
        // Every alpha filter is tried from effort 3: each must decode to the source values
        using var image = CreateSoftMask(96, 64);
        var source = GetBytes(image);
        var reader = ReferenceWebP.Parse(Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy, Effort = effort }));
        Assert.Equal(source.Where((_, index) => index % 4 == 3).ToArray(), reader.DecodeAlpha(0));
    }

    [Theory]
    [InlineData(0, 2)]
    [InlineData(50, 12)]
    public void AlphaQualityReducesTheAlphaLevels(int alphaQuality, int maxLevels)
    {
        using var image = CreateSoftMask(96, 64);
        var source = GetBytes(image).Where((_, index) => index % 4 == 3).ToArray();
        var exact = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy });
        var reduced = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy, AlphaQuality = alphaQuality });
        var alpha = ReferenceWebP.Parse(reduced).DecodeAlpha(0);
        Assert.HasCountLessThanOrEqual(maxLevels, alpha.Distinct());
        Assert.HasCountGreaterThan(maxLevels, source.Distinct());
        Assert.HasCountLessThan(exact.Length, reduced);
        for (var i = 0; i < source.Length; i++)
        {
            // Fully transparent and fully opaque pixels stay exact
            if (source[i] is 0 or 255)
            {
                Assert.Equal(source[i], alpha[i]);
            }
        }
    }

    [Fact]
    public void ReducedAlphaIsKeptOnlyWhenSmaller()
    {
        // 176 levels break the ramps of this mask more than they save: the exact alpha is kept
        using var image = CreateSoftMask(96, 64);
        var source = GetBytes(image).Where((_, index) => index % 4 == 3).ToArray();
        var exact = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy });
        var data = Encode(image, new WebPEncoder { Compression = WebPCompression.Lossy, AlphaQuality = 90 });
        Assert.Equal(exact, data);
        Assert.Equal(source, ReferenceWebP.Parse(data).DecodeAlpha(0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(30)]
    [InlineData(60)]
    [InlineData(99)]
    public void NearLosslessBoundsTheColorError(int level)
    {
        using var image = CreateImage(64, 48, seed: 6, transparent: true);
        var source = GetBytes(image);
        var exact = Encode(image, new WebPEncoder());
        var data = Encode(image, new WebPEncoder { NearLossless = level });
        Assert.HasCountLessThan(exact.Length, data);
        var decoded = ReferenceWebP.Parse(data).DecodeFrame(0).Span.ToArray();
        var maxError = WebPNearLossless.GetMaxError(level);
        Assert.Equal(1 << (WebPNearLossless.GetBits(level) - 1), maxError);
        var largest = 0;
        for (var i = 0; i < source.Length; i++)
        {
            var error = Math.Abs(source[i] - decoded[i]);
            var pixel = i / 4;
            var border = pixel % 64 is 0 or 63 || pixel / 64 is 0 or 47;
            if (i % 4 == 3 || border)
            {
                Assert.Equal(0, error);
            }

            largest = Math.Max(largest, error);
        }

        Assert.True(largest <= maxError && largest > 0, $"largest error {largest}, bound {maxError}");
    }

    [Fact]
    public void NearLosslessKeepsSmoothAreas()
    {
        // Neighbors differ by at most 1: smooth even for the finest step
        var pixels = new Rgba32[200 * 40];
        for (var i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Rgba32((byte)(i % 200), (byte)(i / 200 * 2 / 3), (byte)((i % 200 / 2) + (i / 200)), 255);
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, 200, 40);
        Assert.Equal(GetBytes(image), ReferenceWebP.Parse(Encode(image, new WebPEncoder { NearLossless = 0 })).DecodeFrame(0).Span.ToArray());
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 0)]
    [InlineData(15, 0)]
    [InlineData(17, 32)]
    [InlineData(31, 32)]
    [InlineData(48, 64)]
    [InlineData(16, 0)]
    [InlineData(250, 255)]
    [InlineData(255, 255)]
    public void NearLosslessRoundsToTheNearestEvenMultiple(int value, int expected)
    {
        Assert.Equal(expected, WebPNearLossless.Round((byte)value, bits: 5));
    }

    [Fact]
    public void HiddenAreasCostAlmostNothingInLossyOutput()
    {
        // The same visible pixels, with stripes or a flat color under the transparent right half
        const int Width = 128;
        const int Height = 96;
        var striped = new Rgba32[Width * Height];
        var flat = new Rgba32[Width * Height];
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var visible = new Rgba32((byte)(x * 2), (byte)(y * 2), 90, 255);
                var hidden = x >= Width / 2;
                striped[(y * Width) + x] = hidden ? new Rgba32((byte)((x + y) % 7 * 36), (byte)(x * 3 % 256), (byte)(y * 5 % 256), 0) : visible;
                flat[(y * Width) + x] = hidden ? new Rgba32(128, 128, 128, 0) : visible;
            }
        }

        using var stripedImage = Image.ImportPixelData<Rgba32>(striped, Width, Height);
        using var flatImage = Image.ImportPixelData<Rgba32>(flat, Width, Height);
        var encoder = new WebPEncoder { Compression = WebPCompression.Lossy };
        var stripedSize = Encode(stripedImage, encoder).Length;
        var flatSize = Encode(flatImage, encoder).Length;
        // Hidden stripes used to cost 5 times the flat color, and 1.3 times with one value per run of a block row
        Assert.True(stripedSize <= flatSize * 1.15, $"{stripedSize} bytes with hidden stripes, {flatSize} with a hidden flat color");
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
        var icc = IccTestBlobs.Create("RGB ", 200);
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

    [Fact]
    public void ModeBudgetFitsEveryLossySize()
    {
        Assert.True(Vp8Encoder.TryGetModeBitBudget(Macroblocks(4096, 4096), Vp8Encoder.MaxFirstPartitionSize, out var budget, out var skipFlag));
        Assert.True(skipFlag);
        Assert.True(budget > Macroblocks(4096, 4096) * 30, $"budget: {budget} bits");

        // The largest frames leave the skip flag out to afford the cheapest modes
        Assert.True(Vp8Encoder.TryGetModeBitBudget(Macroblocks(16_383, 16_383), Vp8Encoder.MaxFirstPartitionSize, out _, out skipFlag));
        Assert.False(skipFlag);
    }

    [Fact]
    public void FirstPartitionOverflowDegradesGradually()
    {
        // 1,024 macroblocks whose free modes need a first partition of about 4.5 KB; the limits are scaled down from 512 KiB
        const int Width = 512;
        const int Height = 512;
        var rgba = CreateTexture(Width, Height);
        var free = EncodeVp8(rgba, Width, Height, Vp8Encoder.MaxFirstPartitionSize);
        var freePsnr = LumaPsnr(rgba, DecodeVp8(free, Width, Height));

        // The smallest limit: the cheapest modes everywhere, without the skip flag
        var smallest = GetSmallestFirstPartitionLimit(Macroblocks(Width, Height));
        Assert.True(Vp8Encoder.TryGetModeBitBudget(Macroblocks(Width, Height), smallest, out _, out var skipFlag));
        Assert.False(skipFlag);
        Assert.True(GetFirstPartitionSize(free) > smallest);
        var cheapest = EncodeVp8(rgba, Width, Height, smallest);
        Assert.True(GetFirstPartitionSize(cheapest) <= smallest);
        var cheapestPsnr = LumaPsnr(rgba, DecodeVp8(cheapest, Width, Height));

        // In between, 4x4 prediction is kept where it saves most: the frame is smaller than with the cheapest modes, at the
        // same quality
        var limit = (smallest + GetFirstPartitionSize(free)) / 2;
        Assert.True(Vp8Encoder.TryGetModeBitBudget(Macroblocks(Width, Height), limit, out _, out skipFlag));
        Assert.True(skipFlag);
        var constrained = EncodeVp8(rgba, Width, Height, limit);
        Assert.True(GetFirstPartitionSize(constrained) <= limit);
        Assert.True(GetFirstPartitionSize(constrained) > GetFirstPartitionSize(cheapest));
        var constrainedPsnr = LumaPsnr(rgba, DecodeVp8(constrained, Width, Height));
        var summary = $"{free.Length} bytes at {freePsnr:0.00} dB without limit, {constrained.Length} bytes at {constrainedPsnr:0.00} dB with a {limit}-byte limit, {cheapest.Length} bytes at {cheapestPsnr:0.00} dB with the cheapest modes";
        Assert.True(free.Length < constrained.Length && constrained.Length < cheapest.Length, summary);
        Assert.True(Math.Abs(constrainedPsnr - freePsnr) < 0.25 && Math.Abs(cheapestPsnr - freePsnr) < 0.25, summary);

        // Below the smallest limit, the error names the first partition
        var scope = new AllocationScope(ImageResourceLimits.Default);
        var error = Assert.Throws<UnsupportedImageFeatureException>(() => Vp8Encoder.Encode(scope, rgba, Width, Height, quality: 75, effort: 3, smallest - 1, CancellationToken.None));
        Assert.Contains("first partition", error.Message, StringComparison.Ordinal);
        Console.WriteLine(summary);
    }

    [Fact]
    public void LosslessTokenReserveFitsTheLargestImage()
    {
        // At most 2 bytes per pixel: the parse of a 16,384 x 16,384 image fits in one buffer
        Assert.Equal(536_871_936L, Vp8LEncoder.GetTokenCapacity(16_384 * 16_384) * sizeof(uint));
        Assert.True(Vp8LEncoder.GetTokenCapacity(16_384 * 16_384) * sizeof(uint) <= Array.MaxLength);
    }

    [Theory]
    [InlineData("noise", 1100, 1000, 0)] // more than 2^20 consecutive literals
    [InlineData("noise", 16_384, 3, 5)]
    [InlineData("noise", 257, 129, 9)]
    [InlineData("short-repeats", 509, 67, 0)]
    [InlineData("short-repeats", 509, 67, 3)]
    [InlineData("short-repeats", 509, 67, 9)]
    [InlineData("long-repeats", 300, 200, 5)]
    [InlineData("long-repeats", 300, 200, 9)]
    [InlineData("runs", 5000, 7, 5)] // references longer than 4,096 pixels and taken whole by the second parse
    [InlineData("runs", 5000, 7, 9)]
    [InlineData("single-pixel", 1, 1, 5)]
    [InlineData("single-pixel", 1, 1, 9)]
    public void LosslessTokensRoundTrip(string pattern, int width, int height, int effort)
    {
        var source = CreateArgb(pattern, width, height);
        var argb = source.ToArray();
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var writer = new LsbBitWriter(scope);
        Vp8LEncoder.Encode(scope, writer, argb, width, height, effort, CancellationToken.None);
        writer.Finish();

        var decoded = ReferenceVp8L.Decode(writer.WrittenSpan);
        Assert.Equal(width, decoded.Width);
        Assert.Equal(height, decoded.Height);
        Assert.True(source.AsSpan().SequenceEqual(decoded.Argb));
    }

    [Fact]
    public void LosslessWorkingMemoryIsBounded()
    {
        // Noise is mostly literals, the largest parse, and does not compress. The input stays with the caller: the working
        // memory is the hash chains (4 bytes per pixel and a 1 MiB table, released after the parse), the tokens (at most 2)
        // and the output (about 4), whose buffer doubles as it grows (12 while it is copied)
        const int Width = 512;
        const int Height = 512;
        var argb = CreateArgb("noise", Width, Height);
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var writer = new LsbBitWriter(scope);
        Vp8LEncoder.Encode(scope, writer, argb, Width, Height, effort: 5, CancellationToken.None);
        writer.Finish();

        var peak = scope.GetDiagnostics().PeakLiveBytes;
        Assert.True(peak <= 16L * Width * Height, $"peak live bytes: {peak}");
    }

    [Fact]
    public void LosslessWorkingMemoryIsBoundedAtTheHighestEffort()
    {
        // Effort 9 adds the second parse (the arrivals, 4 bytes per pixel, while the hash chains are held), the histograms
        // of the tiles of the entropy image (at most 8 bytes per pixel, after the parses) and the ranking of the
        // configurations (a copy of the image and the state of one encoding into a counting writer): the peak stays that of
        // effort 5
        const int Width = 512;
        const int Height = 512;
        foreach (var pattern in new[] { "noise", "long-repeats", "mixed" })
        {
            var argb = CreateArgb(pattern, Width, Height);
            var scope = new AllocationScope(ImageResourceLimits.Default);
            using var writer = new LsbBitWriter(scope);
            Vp8LEncoder.Encode(scope, writer, argb, Width, Height, effort: 9, CancellationToken.None);
            writer.Finish();

            var peak = scope.GetDiagnostics().PeakLiveBytes;
            Assert.True(peak <= 16L * Width * Height, $"{pattern}: peak live bytes: {peak}");
        }
    }

    [Theory]
    [InlineData("ramp", "Palette", 0, 3)]
    [InlineData("ramp", "PredictedPalette", 2, 5)]
    [InlineData("ramp", "PredictedPalette", 6, 9)]
    [InlineData("graphics", "Palette", 0, 5)]
    [InlineData("graphics", "PredictedPalette", 4, 5)] // 16 colors or fewer: the predictor applies to the bundled indexes
    [InlineData("graphics", "SubtractGreen", 0, 3)]
    [InlineData("gradient", "Spatial", 2, 5)]
    [InlineData("gradient", "Spatial", 3, 9)]
    [InlineData("gradient", "Spatial", 6, 0)]
    [InlineData("gradient", "SubtractGreen", 0, 9)]
    [InlineData("mixed", "Spatial", 5, 5)]
    [InlineData("mixed", "SubtractGreen", 0, 7)]
    public void LosslessConfigurationsRoundTrip(string pattern, string mode, int bits, int effort)
    {
        const int Width = 157;
        const int Height = 131;
        var source = CreateArgb(pattern, Width, Height);
        var argb = source.ToArray();
        var configuration = new Vp8LConfiguration(Enum.Parse<Vp8LTransformMode>(mode), bits, EntropyPredictors: effort >= 6);
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var writer = new LsbBitWriter(scope);
        Vp8LEncoder.Encode(scope, writer, argb, Width, Height, effort, CancellationToken.None, configuration);
        writer.Finish();

        var decoded = ReferenceVp8L.Decode(writer.WrittenSpan);
        Assert.True(source.AsSpan().SequenceEqual(decoded.Argb));

        // Transforms: 0 predictor, 1 color, 2 subtract green, 3 color indexing
        var structure = ReferenceVp8L.Describe(writer.WrittenSpan);
        int[] expected = configuration.Mode switch
        {
            Vp8LTransformMode.Palette => [3],
            Vp8LTransformMode.PredictedPalette => [3, 0],
            Vp8LTransformMode.Spatial => effort >= 3 ? [2, 0, 1] : [2, 0],
            _ => [2],
        };

        Assert.Equal(expected, structure.Transforms);
        Assert.Equal(expected.Contains(0) ? bits : 0, structure.PredictorBits);
    }

    [Fact]
    public void LosslessEntropyImageSplitsRegionsWithDifferentStatistics()
    {
        // Noise on the left, flat graphics on the right: from effort 3, the two halves get prefix codes of their own
        const int Width = 256;
        const int Height = 192;
        var source = CreateArgb("mixed", Width, Height);
        var sizes = new long[2];
        foreach (var effort in new[] { 2, 3 })
        {
            var scope = new AllocationScope(ImageResourceLimits.Default);
            using var writer = new LsbBitWriter(scope);
            Vp8LEncoder.Encode(scope, writer, source.ToArray(), Width, Height, effort, CancellationToken.None, new Vp8LConfiguration(Vp8LTransformMode.SubtractGreen, 0, EntropyPredictors: false));
            writer.Finish();

            Assert.True(source.AsSpan().SequenceEqual(ReferenceVp8L.Decode(writer.WrittenSpan).Argb));
            var structure = ReferenceVp8L.Describe(writer.WrittenSpan);
            if (effort == 2)
            {
                Assert.Equal(1, structure.GroupCount);
            }
            else
            {
                Assert.True(structure.GroupCount > 1, $"groups: {structure.GroupCount}");
                Assert.True(structure.PrefixBits is >= 2 and <= 9, $"prefix bits: {structure.PrefixBits}");
            }

            sizes[effort - 2] = writer.WrittenSpan.Length;
        }

        Assert.True(sizes[1] < sizes[0], $"one group: {sizes[0]} bytes, entropy image: {sizes[1]} bytes");
    }

    [Theory]
    [InlineData("alpha-ramp", "Palette")] // 256 colors along a smooth ramp: predicting the indexes or the colors wins
    [InlineData("blocks", "Spatial")] // flat blocks of 300 colors: the pixels as they are win
    public void LosslessRankingAvoidsTheFixedChoiceWhenItIsLarger(string pattern, string fixedMode)
    {
        const int Width = 256;
        const int Height = 128;
        var source = CreateArgb(pattern, Width, Height);
        var scope = new AllocationScope(ImageResourceLimits.Default);
        var ranked = Vp8LEncoder.RankConfigurations(scope, source, Width, Height, effort: 5, CancellationToken.None);
        Assert.NotEqual(Enum.Parse<Vp8LTransformMode>(fixedMode), ranked[0].Mode);

        var chosen = EncodeSize(ranked[0]);
        var fixedChoice = EncodeSize(new Vp8LConfiguration(Enum.Parse<Vp8LTransformMode>(fixedMode), 5, EntropyPredictors: false));
        Assert.True(chosen < fixedChoice, $"{ranked[0]}: {chosen} bits, {fixedMode}: {fixedChoice} bits");

        long EncodeSize(Vp8LConfiguration configuration)
        {
            using var counter = LsbBitWriter.CreateCounter(scope);
            Vp8LEncoder.Encode(scope, counter, source.ToArray(), Width, Height, effort: 5, CancellationToken.None, configuration);
            return counter.BitCount;
        }
    }

    [Fact]
    public void LosslessCandidatesFollowTheEffortAndTheImage()
    {
        // Below effort 3: one fixed choice
        Assert.Equal([new Vp8LConfiguration(Vp8LTransformMode.Palette, 0, false)], Vp8LEncoder.GetCandidates(2, 200, flat: true));
        Assert.Equal([new Vp8LConfiguration(Vp8LTransformMode.Spatial, 6, false)], Vp8LEncoder.GetCandidates(0, 257, flat: true));

        // 16 colors or fewer: bundled color indexing below effort 7
        Assert.Equal([new Vp8LConfiguration(Vp8LTransformMode.Palette, 0, false)], Vp8LEncoder.GetCandidates(5, 16, flat: true));
        Assert.Contains(Vp8LEncoder.GetCandidates(7, 16, flat: true), c => c.Mode == Vp8LTransformMode.Spatial);

        // The pixels as they are only for flat images
        Assert.DoesNotContain(Vp8LEncoder.GetCandidates(5, 257, flat: false), c => c.Mode == Vp8LTransformMode.SubtractGreen);
        Assert.Contains(Vp8LEncoder.GetCandidates(5, 257, flat: true), c => c.Mode == Vp8LTransformMode.SubtractGreen);

        // Effort 9: four tile sizes, each with both predictor criteria
        var spatial = Vp8LEncoder.GetCandidates(9, 257, flat: false);
        Assert.HasCount(8, spatial);
        Assert.All(spatial, c => Assert.Equal(Vp8LTransformMode.Spatial, c.Mode));
    }

    [Fact]
    public void CountingBitWriterMeasuresTheStreamWithoutStoringIt()
    {
        const int Width = 120;
        const int Height = 90;
        var source = CreateArgb("mixed", Width, Height);
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var writer = new LsbBitWriter(scope);
        using var counter = LsbBitWriter.CreateCounter(scope);
        Vp8LEncoder.Encode(scope, writer, source.ToArray(), Width, Height, effort: 5, CancellationToken.None);
        Vp8LEncoder.Encode(scope, counter, source.ToArray(), Width, Height, effort: 5, CancellationToken.None);
        Assert.Equal(writer.BitCount, counter.BitCount);

        writer.Finish();
        counter.Finish();
        Assert.Equal(writer.Length, counter.Length);
        Assert.True(counter.WrittenSpan.IsEmpty);
    }

    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static uint[] CreateArgb(string pattern, int width, int height)
    {
        var random = new Random(width ^ (height << 16));
        var argb = new uint[width * height];
        uint[] colors = [0xFFFAFAFA, 0xFF1E1E1E, 0xFFDC2828, 0xFF2878DC, 0xFFFAC81E, 0xFF3CAA50];
        var tile = new uint[300];
        for (var i = 0; i < tile.Length; i++)
        {
            tile[i] = 0xFF000000u | (uint)random.Next(1 << 24);
        }

        for (var i = 0; i < argb.Length; i++)
        {
            var x = i % width;
            var y = i / width;
            argb[i] = pattern switch
            {
                "noise" or "single-pixel" => (uint)random.Next() | ((uint)random.Next(2) << 31),

                // Runs of 3 or 4 repeated colors between new ones: alternating literals and short backward references
                "short-repeats" => i % 7 < 3 ? (uint)random.Next() : argb[i - 3 - (i % 7 == 6 ? 1 : 0)],
                "long-repeats" => i < width || i % 97 == 0 ? (uint)random.Next() : argb[i - width],
                "runs" => (uint)(i / 6000) * 0x01030507u,

                // 120 colors along a ramp that shifts every 3 rows
                "ramp" => 0xFF000000u | ((uint)(((x + (y / 3)) / 6) % 120) * 0x020103u),

                // 6 flat colors: blocks, lines and glyph-like dots
                "graphics" => colors[(x / 16) + (y / 12) % 5 == 1 ? 2 + (x / 16 % 4) : y % 24 < 12 && x % 9 < 6 && (x / 9 + y / 24) % 3 != 0 ? 1 : 0],
                "gradient" => 0xFF000000u | ((uint)(x * 255 / (width - 1)) << 16) | ((uint)(y * 255 / (height - 1)) << 8) | (uint)((x + y) & 0xFF),
                "mixed" => x < width / 2 ? 0xFF000000u | (uint)random.Next(1 << 24) : colors[(x / 9 + y / 24) % 3 == 0 ? 1 : 0],
                "alpha-ramp" => 0x001E78DCu | ((uint)(x * 255 / (width - 1)) << 24),
                "blocks" => tile[(((x / 16) * 7) + ((y / 16) * 13)) % 300],
                _ => throw new ArgumentOutOfRangeException(nameof(pattern)),
            };
        }

        return argb;
    }

    private static int Macroblocks(int width, int height) => ((width + 15) / 16) * ((height + 15) / 16);

    /// <summary>The smallest first partition in which the cheapest modes of every macroblock fit.</summary>
    private static int GetSmallestFirstPartitionLimit(int macroblocks)
    {
        var limit = 1;
        while (!Vp8Encoder.TryGetModeBitBudget(macroblocks, limit, out _, out _))
        {
            limit++;
        }

        return limit;
    }

    private static byte[] EncodeVp8(byte[] rgba, int width, int height, int maxFirstPartitionSize)
    {
        var scope = new AllocationScope(ImageResourceLimits.Default);
        using var payload = Vp8Encoder.Encode(scope, rgba, width, height, quality: 75, effort: 3, maxFirstPartitionSize, CancellationToken.None);
        return payload.WrittenMemory.ToArray();
    }

    /// <summary>The first-partition size of the frame tag (RFC 6386 section 9.1).</summary>
    private static int GetFirstPartitionSize(byte[] vp8) => (vp8[0] | (vp8[1] << 8) | (vp8[2] << 16)) >> 5;

    private static byte[] DecodeVp8(byte[] vp8, int width, int height)
    {
        var padded = vp8.Length + (vp8.Length & 1);
        var file = new byte[20 + padded];
        "RIFF"u8.CopyTo(file);
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(4), (uint)(file.Length - 8));
        "WEBPVP8 "u8.CopyTo(file.AsSpan(8));
        BinaryPrimitives.WriteUInt32LittleEndian(file.AsSpan(16), (uint)vp8.Length);
        vp8.CopyTo(file, 20);
        using var image = Image.Load<Rgba32>(file);
        Assert.Equal(width, image.Width);
        Assert.Equal(height, image.Height);
        return GetBytes(image);
    }

    private static double LumaPsnr(byte[] expected, byte[] actual)
    {
        double squares = 0;
        for (var i = 0; i < expected.Length; i += 4)
        {
            var difference = (0.299 * (expected[i] - actual[i])) + (0.587 * (expected[i + 1] - actual[i + 1])) + (0.114 * (expected[i + 2] - actual[i + 2]));
            squares += difference * difference;
        }

        return 10 * Math.Log10(255.0 * 255.0 * (expected.Length / 4) / squares);
    }

    /// <summary>Oriented stripes of varying angle and period, with soft circles: detail that 4x4 prediction codes well.</summary>
    private static byte[] CreateTexture(int width, int height)
    {
        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var angle = (x / 64 * 0.7) + (y / 64 * 1.3);
                var stripes = Math.Sin(((x * Math.Cos(angle)) + (y * Math.Sin(angle))) * (0.3 + ((x + y) % 5 * 0.1)));
                var circles = Math.Cos(Math.Sqrt(((x - 200) * (x - 200)) + ((y - 150) * (y - 150))) * 0.15);
                var i = ((y * width) + x) * 4;
                rgba[i] = (byte)(128 + (90 * stripes));
                rgba[i + 1] = (byte)(128 + (60 * circles) + (30 * stripes));
                rgba[i + 2] = (byte)(128 - (70 * stripes * circles));
                rgba[i + 3] = 255;
            }
        }

        return rgba;
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

    /// <summary>An opaque disc fading to transparency (alpha ramps) over a gradient.</summary>
    private static Image<Rgba32> CreateSoftMask(int width, int height)
    {
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var distance = Math.Sqrt(Math.Pow(x - (width / 2.0), 2) + Math.Pow(y - (height / 2.0), 2));
                var alpha = (byte)Math.Clamp((28 - distance) * 13, 0, 255);
                pixels[(y * width) + x] = new Rgba32((byte)(x * 2), (byte)(y * 3), 128, alpha);
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
