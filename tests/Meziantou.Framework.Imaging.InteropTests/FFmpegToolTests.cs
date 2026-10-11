using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Verifies the pinned reference tool (downloaded from meziantou/prebuilt) and the raw-decoding helper used by encoder
/// interoperability tests.
/// </summary>
public sealed class FFmpegToolTests
{
    [Fact]
    public async Task FFmpegHasRequiredCapabilities()
    {
        var ffmpeg = await RequireFFmpegAsync();

        var pixelFormats = await ffmpeg.GetPixelFormatsAsync(XunitCancellationToken);
        foreach (var format in new[] { "rgba", "rgba64le", "gray", "gray16le", "rgb24" })
        {
            Assert.True(pixelFormats.Contains(format), $"ffmpeg {ffmpeg.Version} does not support the raw pixel format '{format}'.");
        }

        var decoders = await ffmpeg.GetDecodersAsync(XunitCancellationToken);
        foreach (var decoder in new[] { "png", "apng", "gif", "mjpeg", "qoi" })
        {
            Assert.True(decoders.Contains(decoder), $"ffmpeg {ffmpeg.Version} does not provide the '{decoder}' decoder.");
        }
    }

    [Fact]
    public async Task DecodeToRawFramesProducesExactBytes()
    {
        var ffmpeg = await RequireFFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(DecodeToRawFramesProducesExactBytes));
        var rawPath = directory / "pattern-3x2.rgba";
        var pngPath = directory / "pattern-3x2.png";

        // Distinct values per channel and pixel detect channel swaps and row-order errors.
        // ffmpeg encodes the exact RGBA input losslessly; this validates the helper plumbing, not our codecs.
        var expected = new byte[3 * 2 * 4];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)((i * 37) + 11);
        }

        await File.WriteAllBytesAsync(rawPath, expected, XunitCancellationToken);
        var encode = await ffmpeg.RunAsync(["-y", "-f", "rawvideo", "-pixel_format", "rgba", "-video_size", "3x2", "-i", rawPath, "-frames:v", "1", "-pix_fmt", "rgba", pngPath], XunitCancellationToken);
        Assert.Equal(0, encode.ExitCode);

        var frames = await ffmpeg.DecodeToRawFramesAsync(pngPath, "rgba", 3, 2, XunitCancellationToken);
        var frame = Assert.Single(frames);
        Assert.Equal(expected, frame);
    }

    private static Task<FFmpegTool> RequireFFmpegAsync() => RequiredTools.FFmpegAsync();
}
