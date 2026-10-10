using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// QOI encoder output decoded by FFmpeg's independent QOI decoder (libavcodec <c>qoi</c>); the decoder is covered by the
/// golden corpus (FFmpeg-written inputs included):
/// <list type="bullet">
/// <item><description>encoder output of every 8-bit corpus reference (RGBA and, for opaque images, RGB) decodes exactly in FFmpeg, hidden colors included;</description></item>
/// <item><description>linear-labeled output keeps its samples (FFmpeg ignores the colorspace field, as the specification allows).</description></item>
/// </list>
/// </summary>
public sealed class QoiInteropTests
{
    public static TheoryData<string, string> EncoderCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default).Where(item => item.Format is PixelFormat.Rgba32 or PixelFormat.Rgb24))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputDecodesExactlyInFFmpeg(string id, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var path = await SaveAsync(image, new QoiEncoder { MetadataHandling = MetadataHandling.Strip }, $"output-{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}");
            var frames = await ffmpeg.DecodeToRawFramesAsync(path, pixelFormat == PixelFormat.Rgb24 ? "rgb24" : "rgba", source.Width, source.Height, XunitCancellationToken);
            Assert.True(source.Span.SequenceEqual(Assert.Single(frames)), $"{id} {name} as {format}: FFmpeg {ffmpeg.Version} decodes different samples.");
        }
    }

    [Fact]
    public async Task LinearLabeledOutputKeepsItsSamples()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get("qoi/ref-rgba-alpha-hidden");
        var source = fixture.GetFrame(0, RawPixelLayout.Rgba8);
        using var image = EncoderSources.CreateImage(source, PixelFormat.Rgba32);
        image.Metadata.TransferFunction = ColorTransferFunction.Linear;
        var path = await SaveAsync(image, new QoiEncoder(), "linear");
        Assert.Equal(1, (await File.ReadAllBytesAsync(path, XunitCancellationToken))[13]);
        var frames = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", source.Width, source.Height, XunitCancellationToken);
        Assert.True(source.Span.SequenceEqual(Assert.Single(frames)), $"FFmpeg {ffmpeg.Version} decodes different samples from a linear-labeled file.");
    }

    private static async Task<FullPath> SaveAsync(Image image, QoiEncoder encoder, string name)
    {
        var path = InteropSettings.GetArtifactsDirectory("QoiInteropTests") / (name + ".qoi");
        await image.SaveAsync(path, encoder, XunitCancellationToken);
        return path;
    }

}
