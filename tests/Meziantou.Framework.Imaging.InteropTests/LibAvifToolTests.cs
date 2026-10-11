using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Verifies the pinned libavif tools (downloaded from meziantou/prebuilt) and the helper that reads what <c>avifdec</c>
/// decodes, without the library under test.
/// </summary>
public sealed class LibAvifToolTests
{
    [Fact]
    public async Task LibAvifReportsThePinnedVersionAndAv1Codecs()
    {
        var avif = await RequiredTools.LibAvifAsync();

        // Both tools report the version the package declares (checked for each of them when they are located)
        Assert.Equal(PrebuiltTools.Avifenc.Version, avif.Version);
        Assert.Equal(PrebuiltTools.Avifdec.Version, avif.Version);
        Assert.True(avif.Encoders.Count > 0, $"avifenc {avif.Version} reports no AV1 encoder.");
        Assert.True(avif.Decoders.Count > 0, $"avifdec {avif.Version} reports no AV1 decoder.");
    }

    /// <summary>
    /// The plumbing of the AVIF reference: raw samples that FFmpeg wrote as a PNG file come back, bit for bit, from a lossless
    /// encode by avifenc decoded by avifdec to a 16-bit PNG file (8-bit samples are multiplied by 257) and read by
    /// <see cref="AvifDecImage"/>. Distinct values per channel and pixel detect channel swaps, byte swaps and row-order errors.
    /// </summary>
    [Theory]
    [InlineData("rgba", 4)]
    [InlineData("rgb24", 3)]
    [InlineData("gray", 1)]
    public async Task AvifDecOutputIsReadExactly(string pixelFormat, int channels)
    {
        var avif = await RequiredTools.LibAvifAsync();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(AvifDecOutputIsReadExactly));
        var rawPath = directory / $"pattern-5x3-{pixelFormat}.raw";
        var pngPath = directory / $"pattern-5x3-{pixelFormat}.png";
        var avifPath = directory / $"pattern-5x3-{pixelFormat}.avif";

        var samples = new byte[5 * 3 * channels];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = (byte)((i * 37) + 11);
        }

        await File.WriteAllBytesAsync(rawPath, samples, XunitCancellationToken);
        var encode = await ffmpeg.RunAsync(["-y", "-f", "rawvideo", "-pixel_format", pixelFormat, "-video_size", "5x3", "-i", rawPath, "-frames:v", "1", "-pix_fmt", pixelFormat, pngPath], XunitCancellationToken);
        Assert.Equal(0, encode.ExitCode, message: encode.StandardError);
        await avif.EncodeAsync(["--lossless", pngPath], avifPath, XunitCancellationToken);

        var info = await avif.GetInfoAsync(avifPath, XunitCancellationToken);
        Assert.Equal((5, 3, 8), (info.Width, info.Height, info.BitDepth));
        Assert.Equal(channels == 1 ? "YUV400" : "YUV444", info.Format);
        Assert.Equal(channels == 4, info.HasAlpha);
        Assert.Equal(1, info.FrameCount);
        Assert.Equal([new AvifDecFrame(0, 0, 1, 5, 3)], info.Frames);

        var decoded = await avif.DecodeAsync(avifPath, XunitCancellationToken);
        Assert.Equal((5, 3), (decoded.Width, decoded.Height));
        Assert.Equal(channels == 1, decoded.IsGray);
        Assert.Equal(channels == 4, decoded.HasAlpha);
        for (var pixel = 0; pixel < 5 * 3; pixel++)
        {
            for (var channel = 0; channel < 4; channel++)
            {
                var expected = (channels, channel) switch
                {
                    (_, 3) => channels == 4 ? samples[(pixel * 4) + 3] * 257 : ushort.MaxValue,
                    (1, _) => samples[pixel] * 257,
                    _ => samples[(pixel * channels) + channel] * 257,
                };

                Assert.Equal(expected, decoded.Pixels.GetSample(pixel % 5, pixel / 5, channel), message: $"{pixelFormat}: pixel {pixel} channel {channel}.");
            }
        }
    }
}
