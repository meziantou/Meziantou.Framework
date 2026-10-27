using System.Buffers.Binary;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Bmp;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// BMP encoder output decoded by FFmpeg's independent codec (libavcodec <c>bmp</c>): the output of the lossless layouts,
/// in every option combination, decodes exactly in FFmpeg; the indexed layouts (uncompressed and run-length encoded)
/// decode in FFmpeg to the pixels the independent reader of the harness finds in the same file; and FFmpeg reads the
/// 16-bit layouts as the very 16-bit values the file stores. The decoder is covered by the golden corpus (FFmpeg-written
/// inputs included); the committed references are re-verified against the installed FFmpeg by the corpus generator
/// (<c>verify</c>).
/// </summary>
public sealed class BmpInteropTests
{
    private static readonly BmpEncoder[] IndexedEncoders =
    [
        new() { PixelLayout = BmpPixelLayout.Indexed1, AllowColorReduction = true, MetadataHandling = MetadataHandling.Strip },
        new() { PixelLayout = BmpPixelLayout.Indexed4, AllowColorReduction = true, MetadataHandling = MetadataHandling.Strip },
        new() { PixelLayout = BmpPixelLayout.Indexed4, AllowColorReduction = true, Compression = BmpCompression.RunLength, MetadataHandling = MetadataHandling.Strip },
        new() { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, MetadataHandling = MetadataHandling.Strip },
        new() { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, Compression = BmpCompression.RunLength, MetadataHandling = MetadataHandling.Strip },
        new() { PixelLayout = BmpPixelLayout.Indexed8, AllowColorReduction = true, RowOrder = BmpRowOrder.TopDown, Quantization = new QuantizeOptions { Dithering = DitheringMode.FloydSteinberg }, MetadataHandling = MetadataHandling.Strip },
    ];

    public static TheoryData<string, string> EncoderCases() => LosslessEncoderInterop.EncoderCases();

    /// <summary>The cases whose pixel format has no alpha: the layouts that discard alpha need no background for them.</summary>
    public static TheoryData<string, string> OpaqueEncoderCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default).Where(item => item.Format is PixelFormat.Rgb24 or PixelFormat.Gray8))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public Task EncoderOutputDecodesExactlyInFFmpeg(string id, string format) =>
        LosslessEncoderInterop.AssertEncoderOutputDecodesExactlyInFFmpegAsync(id, format, nameof(BmpInteropTests),
            [
                (new BmpEncoder { MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Auto),
                (new BmpEncoder { PixelLayout = BmpPixelLayout.Bgra32, MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Bgra32),
                (new BmpEncoder { RowOrder = BmpRowOrder.TopDown, MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Auto + "-" + BmpRowOrder.TopDown),
                (new BmpEncoder { PixelLayout = BmpPixelLayout.Bgra32, RowOrder = BmpRowOrder.TopDown, MetadataHandling = MetadataHandling.Strip }, "bmp", "-" + BmpPixelLayout.Bgra32 + "-" + BmpRowOrder.TopDown),
            ],
            // FFmpeg's BMP decoder forces an all-zero alpha channel to opaque (a workaround for writers that leave the
            // fourth byte at zero); our output declares an explicit alpha mask, so the color samples are what must agree
            source => IsFullyTransparent(source) ? WithOpaqueAlpha(source) : source);

    [Theory]
    [MemberData(nameof(OpaqueEncoderCases))]
    public async Task IndexedOutputDecodesInFFmpegLikeTheReferenceReader(string id, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, source) in EncoderSources.GetStills(GoldenCorpus.Default.Get(id), pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            foreach (var encoder in IndexedEncoders)
            {
                var description = $"{encoder.PixelLayout}-{encoder.Compression}-{encoder.RowOrder}-{encoder.Quantization.Dithering}";
                var path = InteropSettings.GetArtifactsDirectory(nameof(BmpInteropTests)) / $"{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}-{description}.bmp";
                await image.SaveAsync(path, encoder, XunitCancellationToken);

                // Colors may have been reduced: the palette and the indexes of the file are what both readers must agree on
                var reference = ReferenceBmp.Parse(await File.ReadAllBytesAsync(path, XunitCancellationToken));
                var expected = new byte[source.Width * source.Height * 3];
                for (var i = 0; i < source.Width * source.Height; i++)
                {
                    reference.Rgba.Span.Slice(i * 4, 3).CopyTo(expected.AsSpan(i * 3));
                }

                var frames = await ffmpeg.DecodeToRawFramesAsync(path, "rgb24", source.Width, source.Height, XunitCancellationToken);
                Assert.True(expected.AsSpan().SequenceEqual(Assert.Single(frames)), $"{id} {name} as {format} ({description}): FFmpeg {ffmpeg.Version} decodes different samples.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(OpaqueEncoderCases))]
    public async Task SixteenBitOutputHoldsTheSameValuesInFFmpeg(string id, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, source) in EncoderSources.GetStills(GoldenCorpus.Default.Get(id), pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            foreach (var (layout, rowOrder) in new[] { (BmpPixelLayout.Bgr565, BmpRowOrder.BottomUp), (BmpPixelLayout.Bgr565, BmpRowOrder.TopDown), (BmpPixelLayout.Bgr555, BmpRowOrder.BottomUp), (BmpPixelLayout.Bgr555, BmpRowOrder.TopDown) })
            {
                var description = $"{layout}-{rowOrder}";
                var path = InteropSettings.GetArtifactsDirectory(nameof(BmpInteropTests)) / $"{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}-{description}.bmp";
                await image.SaveAsync(path, new BmpEncoder { PixelLayout = layout, RowOrder = rowOrder, AllowBitDepthReduction = true, MetadataHandling = MetadataHandling.Strip }, XunitCancellationToken);

                // The 16-bit values of the file, in display order (FFmpeg expands them to 8 bits by replicating bits, where
                // the references round the exact ratio, so the stored values are what is compared)
                var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
                var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(10));
                var rowLength = ((source.Width * 16) + 31) / 32 * 4;
                var expected = new byte[source.Width * source.Height * 2];
                for (var y = 0; y < source.Height; y++)
                {
                    var storedRow = rowOrder == BmpRowOrder.TopDown ? y : source.Height - 1 - y;
                    data.AsSpan(offset + (storedRow * rowLength), source.Width * 2).CopyTo(expected.AsSpan(y * source.Width * 2));
                }

                var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, layout == BmpPixelLayout.Bgr565 ? "rgb565le" : "rgb555le", source.Width, source.Height, XunitCancellationToken));
                if (layout == BmpPixelLayout.Bgr555)
                {
                    // The sixteenth bit of a 5-5-5 pixel is unused
                    for (var i = 1; i < frame.Length; i += 2)
                    {
                        frame[i] &= 0x7F;
                    }
                }

                Assert.True(expected.AsSpan().SequenceEqual(frame), $"{id} {name} as {format} ({description}): FFmpeg {ffmpeg.Version} reads different 16-bit values.");
            }
        }
    }

    private static bool IsFullyTransparent(RawPixelBuffer source)
    {
        if (source.Layout.ChannelCount != 4)
            return false;

        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                if (source.GetSample(x, y, 3) != 0)
                    return false;
            }
        }

        return true;
    }

    private static RawPixelBuffer WithOpaqueAlpha(RawPixelBuffer source)
    {
        var builder = new RawPixelBufferBuilder(source.Width, source.Height, source.Layout);
        for (var y = 0; y < source.Height; y++)
        {
            for (var x = 0; x < source.Width; x++)
            {
                for (var c = 0; c < 3; c++)
                {
                    builder.SetSample(x, y, c, source.GetSample(x, y, c));
                }

                builder.SetSample(x, y, 3, source.Layout.MaxSampleValue);
            }
        }

        return builder.Build();
    }
}
