using System.Diagnostics.CodeAnalysis;
using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// QOI decoding of hand-written chunk streams with literal expected pixels (QOI specification 1.0): every chunk type,
/// bias extremes and wrap-around, the running index (unset slots, the initial pixel), runs across rows, header validation,
/// the end marker, truncation at every byte, limits, readers, input variants, bounded memory and cancellation. The
/// reference-encoded corpus (Tests/Conformance) covers the same decoder against qoi.h and FFmpeg.
/// </summary>
public sealed class QoiDecoderTests
{
    private const byte OpRgb = 0xFE;
    private const byte OpRgba = 0xFF;

    private static readonly byte[] EndMarker = [0, 0, 0, 0, 0, 0, 0, 1];

    [Fact]
    public void EveryChunkTypeProducesTheSpecifiedPixel()
    {
        byte[] chunks =
        [
            0xC1,                         // run of 2 of the initial pixel (0, 0, 0, 255)
            OpRgb, 200, 100, 50,          // (200, 100, 50, 255)
            0x40,                         // diff -2 -2 -2: (198, 98, 48, 255)
            0x7F,                         // diff +1 +1 +1: (199, 99, 49, 255)
            0x80, 0x0F,                   // luma dg -32, dr-dg -8, db-dg +7: (159, 67, 24, 255)
            0xBF, 0xF0,                   // luma dg +31, dr-dg +7, db-dg -8: (197, 98, 47, 255)
            OpRgba, 10, 20, 30, 0,        // (10, 20, 30, 0): a defined color under alpha 0
            OpRgb, 1, 2, 3,               // RGB keeps alpha: (1, 2, 3, 0)
            0x4E,                         // diff -2 +1 0, red wraps: (255, 3, 3, 0)
            0x9C, 0xB0,                   // luma dg -4, dr-dg +3, db-dg -8, wraps: (254, 255, 247, 0)
            (byte)Hash(200, 100, 50, 255),// index: (200, 100, 50, 255)
            (byte)Hash(0, 0, 0, 255),     // index of the initial pixel, stored after the leading run
            0x02,                         // index slot 2, never written: (0, 0, 0, 0)
        ];
        Rgba32[] expected =
        [
            new(0, 0, 0, 255), new(0, 0, 0, 255), new(200, 100, 50, 255), new(198, 98, 48, 255), new(199, 99, 49, 255),
            new(159, 67, 24, 255), new(197, 98, 47, 255), new(10, 20, 30, 0), new(1, 2, 3, 0), new(255, 3, 3, 0),
            new(254, 255, 247, 0), new(200, 100, 50, 255), new(0, 0, 0, 255), new(0, 0, 0, 0),
        ];
        Assert.DoesNotContain(expected[..^1], pixel => Hash(pixel.R, pixel.G, pixel.B, pixel.A) == 2); // slot 2 is never written

        using var image = Image.Load<Rgba32>(Qoi(7, 2, 4, 0, chunks));
        Assert.Equal(expected, GetPixels(image.Frames[0]));
        Assert.Equal(ImageFormat.Qoi, image.Metadata.SourceFormat);
        Assert.Equal(ColorTransferFunction.Srgb, image.Metadata.TransferFunction);
        Assert.False(image.IsAnimated);
    }

    [Fact]
    public void RunsContinueAcrossRowsAndReachTheLastPixel()
    {
        // 5x3: one RGBA pixel, a run of 13 (crossing two row ends) and a run of 1 ending the image
        using var image = Image.Load<Rgba32>(Qoi(5, 3, 4, 0, [OpRgba, 9, 8, 7, 6, 0xCC, 0xC0]));
        Assert.All(GetPixels(image.Frames[0]), pixel => Assert.Equal(new Rgba32(9, 8, 7, 6), pixel));

        // The longest run (62) with an odd width
        var data = Qoi(7, 9, 3, 0, [OpRgb, 1, 2, 3, 0xFD]);
        using var rgb = Image.Load(data);
        Assert.Equal(PixelFormat.Rgb24, rgb.PixelFormat);
        var bytes = new byte[7 * 9 * 3];
        rgb.Frames[0].CopyPixelBytesTo(bytes);
        for (var i = 0; i < bytes.Length; i += 3)
        {
            Assert.Equal([1, 2, 3], bytes[i..(i + 3)]);
        }
    }

    [Fact]
    public void ThreeChannelStreamsAreOpaqueRgbAndKeepAlphaInTheDecoderState()
    {
        // RGBA chunks change alpha: the index hash and later RGB chunks see it, but the image is opaque RGB
        byte[] chunks =
        [
            OpRgb, 10, 20, 30,                       // (10, 20, 30) alpha 255
            OpRgba, 10, 20, 30, 7,                   // same color, alpha 7: another index slot
            (byte)Hash(10, 20, 30, 255),             // back to the opaque entry
            (byte)Hash(10, 20, 30, 7),
            OpRgb, 40, 50, 60,                       // alpha 7 kept in the state
            (byte)Hash(40, 50, 60, 7),
        ];
        var data = Qoi(3, 2, 3, 0, chunks);
        var info = Image.Identify(data);
        Assert.Equal(PixelFormat.Rgb24, info.PixelFormat);
        Assert.Equal(ImageColorModel.Rgb, info.ColorModel);
        Assert.False(info.MayHaveTransparency);

        using var image = Image.Load<Rgba32>(data);
        Assert.Equal(
            [new Rgba32(10, 20, 30, 255), new Rgba32(10, 20, 30, 255), new Rgba32(10, 20, 30, 255), new Rgba32(10, 20, 30, 255), new Rgba32(40, 50, 60, 255), new Rgba32(40, 50, 60, 255)],
            GetPixels(image.Frames[0]));
    }

    [Fact]
    public void IdentifyReportsTheHeaderAndTheTransferFunction()
    {
        var data = Qoi(3, 2, 4, 1, [OpRgba, 1, 2, 3, 4, 0xC4]);
        foreach (var mode in new[] { ImageIdentifyMode.Header, ImageIdentifyMode.FullScan })
        {
            var info = Image.Identify(data, new ImageIdentifyOptions { Mode = mode });
            Assert.Equal(ImageFormat.Qoi, info.Format);
            Assert.Equal((3, 2), (info.Width, info.Height));
            Assert.Equal(PixelFormat.Rgba32, info.PixelFormat);
            Assert.Equal(ImageColorModel.Rgba, info.ColorModel);
            Assert.Equal(8, info.BitsPerComponent);
            Assert.Equal(1, info.FrameCount);
            Assert.False(info.IsAnimated);
            Assert.False(info.HasPosterFrame);
            Assert.True(info.MayHaveTransparency);
            Assert.Null(info.Animation);
            Assert.Equal(ColorTransferFunction.Linear, info.Metadata.TransferFunction);
            Assert.Equal(mode, info.IdentifyMode);
        }

        // The header walk reads 14 bytes only: an invalid chunk stream is found by the full scan
        var broken = Qoi(3, 2, 4, 0, [OpRgba, 1, 2, 3, 4, 0xC5]); // a run of 6 where 5 pixels are left
        Assert.Equal(1, Image.Identify(broken).FrameCount);
        Assert.Throws<InvalidImageContentException>(() => Image.Identify(broken, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
    }

    [Fact]
    public void LinearSamplesAreLabeledNotConverted()
    {
        using var image = Image.Load<Rgba32>(Qoi(1, 1, 4, 1, [OpRgba, 64, 128, 192, 32]));
        Assert.Equal(ColorTransferFunction.Linear, image.Metadata.TransferFunction);
        Assert.Equal([new Rgba32(64, 128, 192, 32)], GetPixels(image.Frames[0]));
        using var clone = image.Clone();
        Assert.Equal(ColorTransferFunction.Linear, clone.Metadata.TransferFunction);
    }

    [Theory]
    [InlineData(0u, 1u, (byte)4, (byte)0)]
    [InlineData(1u, 0u, (byte)4, (byte)0)]
    [InlineData(1u, 1u, (byte)2, (byte)0)]
    [InlineData(1u, 1u, (byte)5, (byte)0)]
    [InlineData(1u, 1u, (byte)4, (byte)2)]
    [InlineData(1u, 1u, (byte)3, (byte)255)]
    public void InvalidHeaderFieldsAreMalformedData(uint width, uint height, byte channels, byte colorSpace)
    {
        var data = Qoi(width, height, channels, colorSpace, [OpRgb, 1, 2, 3]);
        var exception = Assert.Throws<InvalidImageContentException>(() => Image.Identify(data));
        Assert.Equal(ImageFormat.Qoi, exception.Format);
        Assert.Throws<InvalidImageContentException>(() => Image.Load(data));
    }

    [Fact]
    public void TheEndMarkerIsRequired()
    {
        var valid = Qoi(2, 1, 4, 0, [OpRgb, 1, 2, 3, 0xC0]);
        using (Image.Load(valid))
        {
        }

        // Missing, truncated, different, or preceded by an extra chunk
        foreach (var data in new[]
        {
            valid[..^8],
            valid[..^1],
            [.. valid[..^1], 2],
            [.. valid[..^8], OpRgb, 4, 5, 6, .. EndMarker],
            [.. valid[..^8], 0, 0, 0, 0, 0, 0, 0, 0, 1],
        })
        {
            Assert.Equal(ImageFormat.Qoi, Assert.Throws<InvalidImageContentException>(() => Image.Load(data)).Format);
            Assert.Throws<InvalidImageContentException>(() => Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }));
        }

        // Bytes after the end marker are not read
        using var image = Image.Load<Rgba32>([.. valid, 0xDE, 0xAD]);
        Assert.Equal([new Rgba32(1, 2, 3, 255), new Rgba32(1, 2, 3, 255)], GetPixels(image.Frames[0]));
    }

    [Fact]
    public void RunsPastTheLastPixelAreMalformedData()
    {
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Qoi(2, 2, 4, 0, [0xC4])));
        Assert.Throws<InvalidImageContentException>(() => Image.Load(Qoi(2, 2, 4, 0, [OpRgb, 1, 2, 3, 0xC3])));
        using (Image.Load(Qoi(2, 2, 4, 0, [OpRgb, 1, 2, 3, 0xC2])))
        {
        }
    }

    [Fact]
    public async Task TruncationAtEveryByteIsNeverACleanEnd()
    {
        var data = Qoi(4, 3, 4, 0, [OpRgba, 1, 2, 3, 4, 0x80, 0x88, OpRgb, 9, 9, 9, 0xC3, 0x55, (byte)Hash(1, 2, 3, 4), 0xC2]);
        using (Image.Load(data))
        {
        }

        for (var length = 8; length < data.Length; length++)
        {
            var truncated = data[..length];
            Assert.Throws<InvalidImageContentException>(() => Image.Load(truncated));
            await Assert.ThrowsAsync<InvalidImageContentException>(async () =>
            {
                await using var stream = new TestInputStream(truncated) { Seekable = false, MaxBytesPerRead = 1 };
                using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
            });
            Assert.Throws<InvalidImageContentException>(() =>
            {
                using var reader = Image.OpenReader<Rgba32>(new MemoryStream(truncated));
                using var frame = reader.ReadFrame();
            });
        }
    }

    [Fact]
    public async Task EveryInputVariantDecodesTheSamePixels()
    {
        var data = CreatePattern(37, 23, alpha: true);
        using var expected = Image.Load<Rgba32>(data);
        foreach (var variant in InputVariants.All)
        {
            using var image = await InputVariants.LoadAsync<Rgba32>(variant, data, ImageFormat.Qoi, options: null, XunitCancellationToken);
            Assert.Equal(GetPixels(expected.Frames[0]), GetPixels(image.Frames[0]));
        }
    }

    [Fact]
    public async Task ReadersReturnOneFrameAfterTheEndMarkerIsValidated()
    {
        var data = CreatePattern(9, 4, alpha: false);
        using var eager = Image.Load<Rgb24>(data);
        await using (var reader = await Image.OpenReaderAsync<Rgb24>(new MemoryStream(data), cancellationToken: XunitCancellationToken))
        {
            Assert.Equal(ImageFormat.Qoi, reader.Info.Format);
            Assert.Equal(1, reader.Info.FrameCount);
            Assert.Null(await reader.ReadPosterFrameAsync(XunitCancellationToken));
            using var frame = await reader.ReadFrameAsync(XunitCancellationToken);
            Assert.NotNull(frame);
            Assert.Equal(GetPixels(eager.Frames[0]), GetPixels(frame.Frames[0]));
            Assert.Null(await reader.ReadFrameAsync(XunitCancellationToken));
        }

        // The frame is handed over only once the end marker is checked
        using var broken = Image.OpenReader<Rgb24>(new MemoryStream([.. data[..^1], 0]));
        Assert.Throws<InvalidImageContentException>(() => broken.ReadFrame());
    }

    [Fact]
    public void TypedLoadsConvertAndEnforceTheAlphaPolicy()
    {
        var data = Qoi(2, 1, 4, 0, [OpRgba, 10, 20, 30, 128, OpRgba, 40, 50, 60, 255]);
        using (var bgra = Image.Load<Bgra32>(data))
        {
            Assert.Equal([new Bgra32(10, 20, 30, 128), new Bgra32(40, 50, 60, 255)], GetPixels(bgra.Frames[0]));
        }

        using (var wide = Image.Load<Rgba64>(data))
        {
            Assert.Equal(new Rgba64(10 * 257, 20 * 257, 30 * 257, 128 * 257), GetPixels(wide.Frames[0])[0]);
        }

        // Alpha is never discarded silently
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load<Rgb24>(data));
    }

    [Fact]
    public void LimitsAreCheckedFromTheHeader()
    {
        var data = CreatePattern(33, 17, alpha: true);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxWidth = 32 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxHeight = 16 } } })).Kind);
        Assert.Equal(ImageResourceLimitKind.FramePixels, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxFramePixels = 33 * 17 - 1 }))).Kind);
        Assert.Equal(ImageResourceLimitKind.EncodedBytes, Assert.Throws<ImageResourceLimitException>(() => Image.Load(data, Limits(new ImageResourceLimits { MaxEncodedBytes = data.Length - 1 }))).Kind);
        using (Image.Load(data, Limits(new ImageResourceLimits { MaxEncodedBytes = data.Length, MaxWidth = 33, MaxHeight = 17, MaxFramePixels = 33 * 17 })))
        {
        }

        // 32-bit dimensions above 2^31 - 1 are reported as limits before any allocation
        var huge = Qoi(0x80000000, 1, 4, 0, [0xFD]);
        Assert.Equal(ImageResourceLimitKind.Width, Assert.Throws<ImageResourceLimitException>(() => Image.Identify(huge)).Kind);
        var tall = Qoi(1, uint.MaxValue, 4, 0, [0xFD]);
        Assert.Equal(ImageResourceLimitKind.Height, Assert.Throws<ImageResourceLimitException>(() => Image.Load(tall)).Kind);

        static ImageDecodeOptions Limits(ImageResourceLimits limits) => new() { Configuration = new ImageConfiguration { Limits = limits } };
    }

    [Fact]
    public async Task StreamingKeepsOneRowOfDecoderStateAndNeverBuffersTheFile()
    {
        // A 1024x512 image read 1 KiB at a time from a non-seekable stream: the decoder state is one row and the input buffer
        // never grows to the file size
        var data = CreatePattern(1024, 512, alpha: true);
        await using var stream = new TestInputStream(data) { Seekable = false, MaxBytesPerRead = 1024 };
        using var image = await Image.LoadAsync<Rgba32>(stream, cancellationToken: XunitCancellationToken);
        var scope = image.Owner.Scope;
        Assert.Equal(0, scope.GetLiveBytes(AllocationKind.DecoderState));
        var state = scope.GetDiagnostics().PeakLiveBytes - scope.GetLiveBytes(AllocationKind.ImagePixels);
        Assert.True(state < 64 * 1024, $"Peak decoder state and input buffer {state} bytes for a {data.Length}-byte input.");
    }

    [Fact]
    public async Task CancellationStopsDecoding()
    {
        var data = CreatePattern(256, 256, alpha: true);
        using var source = new CancellationTokenSource();
        await using var stream = new TestInputStream(data) { CancellationSource = source, CancelAtPosition = data.Length / 2, MaxBytesPerRead = 256 };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgba32>(stream, cancellationToken: source.Token));

        // A span is parsed in one call: the token is checked between rows
        using var canceled = new CancellationTokenSource();
        await canceled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Image.LoadAsync<Rgba32>(new MemoryStream(data), cancellationToken: canceled.Token));
    }

    [Fact]
    public void DetectionNeedsTheMagic()
    {
        Assert.Equal(ImageFormat.Qoi, Image.DetectFormat("qoif\0\0\0\u0001"u8));
        Assert.Equal(ImageFormat.Unknown, Image.DetectFormat("qoiF\0\0\0\u0001"u8));
        Assert.Throws<UnknownImageFormatException>(() => Image.Load("qoif"u8));
    }

    internal static byte[] Qoi(uint width, uint height, byte channels, byte colorSpace, byte[] chunks)
    {
        var data = new byte[14 + chunks.Length + EndMarker.Length];
        "qoif"u8.CopyTo(data);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(4), width);
        BinaryPrimitives.WriteUInt32BigEndian(data.AsSpan(8), height);
        data[12] = channels;
        data[13] = colorSpace;
        chunks.CopyTo(data, 14);
        EndMarker.CopyTo(data, 14 + chunks.Length);
        return data;
    }

    internal static int Hash(int r, int g, int b, int a) => ((r * 3) + (g * 5) + (b * 7) + (a * 11)) % 64;

    /// <summary>A pattern with flat areas, gradients and noise, encoded by the library (the decoder tests above use literal streams).</summary>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static byte[] CreatePattern(int width, int height, bool alpha)
    {
        var random = new Random(width * 31 + height);
        var pixels = new Rgba32[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                pixels[(y * width) + x] = (y % 7) switch
                {
                    0 => new Rgba32(10, 20, 30, 255),
                    1 => new Rgba32((byte)x, (byte)y, (byte)(x + y), alpha ? (byte)(x * 3) : (byte)255),
                    _ => new Rgba32((byte)random.Next(256), (byte)random.Next(256), (byte)random.Next(256), alpha && x % 5 == 0 ? (byte)random.Next(256) : (byte)255),
                };
            }
        }

        using var image = Image.ImportPixelData<Rgba32>(pixels, width, height);
        using var stream = new MemoryStream();
        if (alpha)
        {
            image.Save(stream, new Formats.QoiEncoder());
        }
        else
        {
            using var rgb = image.CloneAs<Rgb24>();
            rgb.Save(stream, new Formats.QoiEncoder());
        }

        return stream.ToArray();
    }

    private static TPixel[] GetPixels<TPixel>(ImageFrame<TPixel> frame)
        where TPixel : unmanaged
    {
        var pixels = new TPixel[frame.Width * frame.Height];
        frame.CopyPixelDataTo(pixels);
        return pixels;
    }
}
