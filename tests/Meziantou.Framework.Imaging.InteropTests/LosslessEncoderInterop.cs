using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>The cases and the FFmpeg round trip shared by the interop tests of the lossless BMP, TGA and Netpbm encoders.</summary>
internal static class LosslessEncoderInterop
{
    public static TheoryData<string, string> EncoderCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var (fixture, format) in EncoderSources.GetCases(GoldenCorpus.Default).Where(item => item.Format is PixelFormat.Rgba32 or PixelFormat.Rgb24 or PixelFormat.Gray8))
        {
            data.Add(fixture.Id, format.ToString());
        }

        return data;
    }

    /// <summary>Encodes every still of the fixture with each encoder and checks that FFmpeg decodes exactly the source samples.</summary>
    /// <param name="encoders">The encoders to check, with the file extension of their output and a suffix describing their options.</param>
    /// <param name="expected">The samples FFmpeg must decode from a source (the source itself by default).</param>
    public static async Task AssertEncoderOutputDecodesExactlyInFFmpegAsync(string id, string format, string artifactsName,
        IEnumerable<(ImageEncoder Encoder, string Extension, string Description)> encoders, Func<RawPixelBuffer, RawPixelBuffer>? expected = null)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var fixture = GoldenCorpus.Default.Get(id);
        var pixelFormat = Enum.Parse<PixelFormat>(format);
        var ffmpegFormat = pixelFormat switch
        {
            PixelFormat.Rgb24 => "rgb24",
            PixelFormat.Gray8 => "gray",
            _ => "rgba",
        };

        var encoderList = encoders.ToList();
        foreach (var (name, source) in EncoderSources.GetStills(fixture, pixelFormat))
        {
            using var image = EncoderSources.CreateImage(source, pixelFormat);
            var stem = $"{id.Replace('/', '-')}-{name.Replace(' ', '-')}-{format}";
            foreach (var (encoder, extension, description) in encoderList)
            {
                var path = InteropSettings.GetArtifactsDirectory(artifactsName) / $"{stem}-{encoder.Format}{description}.{extension}";
                await image.SaveAsync(path, encoder, XunitCancellationToken);
                var frames = await ffmpeg.DecodeToRawFramesAsync(path, ffmpegFormat, source.Width, source.Height, XunitCancellationToken);
                var samples = expected?.Invoke(source) ?? source;
                Assert.True(samples.Span.SequenceEqual(Assert.Single(frames)), $"{id} {name} as {format} ({encoder.Format}{description}): FFmpeg {ffmpeg.Version} decodes different samples.");
            }
        }
    }
}
