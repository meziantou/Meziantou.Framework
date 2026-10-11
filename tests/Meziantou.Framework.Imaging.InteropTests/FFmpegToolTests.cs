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

    /// <summary>
    /// What the AVIF and AV1 interoperability tests need from the pinned build: two AV1 encoders, two independent AV1 decoders,
    /// raw AV1 streams (the <c>obu</c> muxer and demuxer), FFmpeg's own AVIF muxer, the header parser and the planar formats
    /// that hold AV1 planes without conversion.
    /// </summary>
    [Fact]
    public async Task FFmpegHasAv1Capabilities()
    {
        var ffmpeg = await RequireFFmpegAsync();

        var encoders = await ffmpeg.GetEncodersAsync(XunitCancellationToken);
        foreach (var encoder in new[] { "libaom-av1", "libsvtav1" })
        {
            Assert.True(encoders.Contains(encoder), $"ffmpeg {ffmpeg.Version} does not provide the '{encoder}' encoder.");
        }

        var decoders = await ffmpeg.GetDecodersAsync(XunitCancellationToken);
        foreach (var decoder in new[] { Av1ReferenceDecoder.Dav1d, Av1ReferenceDecoder.Libaom })
        {
            Assert.True(decoders.Contains(decoder), $"ffmpeg {ffmpeg.Version} does not provide the '{decoder}' decoder.");
        }

        var muxers = await ffmpeg.GetMuxersAsync(XunitCancellationToken);
        foreach (var muxer in new[] { "obu", "avif" })
        {
            Assert.True(muxers.Contains(muxer), $"ffmpeg {ffmpeg.Version} does not provide the '{muxer}' muxer.");
        }

        var demuxers = await ffmpeg.GetDemuxersAsync(XunitCancellationToken);
        Assert.True(demuxers.Contains("obu"), $"ffmpeg {ffmpeg.Version} does not provide the 'obu' demuxer.");

        var filters = await ffmpeg.GetBitstreamFiltersAsync(XunitCancellationToken);
        Assert.True(filters.Contains("trace_headers"), $"ffmpeg {ffmpeg.Version} does not provide the 'trace_headers' bitstream filter.");

        var pixelFormats = await ffmpeg.GetPixelFormatsAsync(XunitCancellationToken);
        foreach (var layout in new[] { "yuv420p", "yuv422p", "yuv444p", "gbrp", "gray" })
        {
            foreach (var depth in new[] { "", "10le", "12le" })
            {
                Assert.True(pixelFormats.Contains(layout + depth), $"ffmpeg {ffmpeg.Version} does not support the raw pixel format '{layout}{depth}'.");
            }
        }
    }

    /// <summary>
    /// The plumbing of the AV1 references: a stream encoded by libaom is decoded by dav1d and by libaom to the same planes,
    /// which are, for a lossless encode, the planes that were given to the encoder.
    /// </summary>
    [Fact]
    public async Task Av1ReferenceDecodersReturnThePlanesOfALosslessStream()
    {
        var ffmpeg = await RequireFFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(Av1ReferenceDecodersReturnThePlanesOfALosslessStream));
        var rawPath = directory / "planes-6x4.yuv";
        var streamPath = directory / "planes-6x4.obu";

        // 4:2:0, 8 bits: a 6x4 luma plane and two 3x2 chroma planes with distinct values
        var expected = new byte[(6 * 4) + (2 * 3 * 2)];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)((i * 29) + 7);
        }

        await File.WriteAllBytesAsync(rawPath, expected, XunitCancellationToken);
        var encode = await ffmpeg.RunAsync(["-y", "-f", "rawvideo", "-pixel_format", "yuv420p", "-video_size", "6x4", "-i", rawPath, "-frames:v", "1", "-c:v", "libaom-av1", "-crf", "0", "-aom-params", "lossless=1", "-threads", "1", "-f", "obu", streamPath], XunitCancellationToken);
        Assert.Equal(0, encode.ExitCode, message: encode.StandardError);

        foreach (var name in new[] { Av1ReferenceDecoder.Dav1d, Av1ReferenceDecoder.Libaom })
        {
            var decoder = new Av1ReferenceDecoder(ffmpeg, name);
            Assert.Equal([(6, 4, "yuv420p", "tv,unknown,unknown,unknown,unspecified")], await decoder.ProbeAsync(streamPath, XunitCancellationToken));
            var frames = await decoder.DecodeAsync(streamPath, "yuv420p", directory / name, XunitCancellationToken);
            Assert.Equal(expected, Assert.Single(frames));
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
