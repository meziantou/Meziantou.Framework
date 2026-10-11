using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Streams;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Writer output decoded by the pinned independent tool (PNG, APNG, GIF, JPEG, QOI, BMP, TGA and PNM encoders): frames are
/// written one by one to a non-seekable stream and through an atomically published path writer, completed explicitly, and
/// ffmpeg must decode exactly the written frames in one pass.
/// </summary>
public sealed class WriterInteropTests
{
    private const int Width = 6;
    private const int Height = 4;

    [Theory]
    [InlineData("png", false)]
    [InlineData("png", true)]
    [InlineData("apng", false)]
    [InlineData("apng", true)]
    [InlineData("gif", false)]
    [InlineData("gif", true)]
    [InlineData("jpeg", false)]
    [InlineData("jpeg", true)]
    [InlineData("qoi", false)]
    [InlineData("qoi", true)]
    [InlineData("bmp", false)]
    [InlineData("bmp", true)]
    [InlineData("tga", false)]
    [InlineData("tga", true)]
    [InlineData("pnm", false)]
    [InlineData("pnm", true)]
    public async Task WriterOutputDecodesIndependently(string output, bool toPath)
    {
        var frames = CreateFrames(output is "apng" or "gif" ? 3 : 1);
        var directory = InteropSettings.GetArtifactsDirectory(nameof(WriterOutputDecodesIndependently));
        var path = directory / $"{output}-{(toPath ? "path" : "stream")}{GetExtension(output)}";
        File.Delete(path);
        var options = new ImageWriterOptions(Width, Height)
        {
            Encoder = output switch
            {
                "png" => new PngEncoder { AnimationMode = PngAnimationMode.Static },
                "apng" => new PngEncoder { AnimationMode = PngAnimationMode.Animated },
                "gif" => new GifEncoder(),
                "qoi" => new QoiEncoder(),
                "bmp" => new BmpEncoder(),
                "tga" => new TgaEncoder { Compression = TgaCompression.RunLength },
                "pnm" => new PnmEncoder(),
                _ => new JpegEncoder(),
            },
            ExpectedFrameCount = output == "gif" ? null : frames.Length, // GIF: unknown count
        };

        try
        {
            var ffmpeg = await RequiredTools.FFmpegAsync();
            if (toPath)
            {
                await using var writer = Image.CreateWriter<Rgba32>(path, options);
                foreach (var frame in frames)
                {
                    await writer.WriteFrameAsync(frame.Frames[0], XunitCancellationToken);
                }

                Assert.False(File.Exists(path)); // published only by Complete
                await writer.CompleteAsync(XunitCancellationToken);
            }
            else
            {
                using var stream = new TestOutputStream(); // non-seekable: one pass, never sought
                using (var writer = Image.CreateWriter<Rgba32>(stream, options))
                {
                    foreach (var frame in frames)
                    {
                        writer.WriteFrame(frame.Frames[0]);
                    }

                    writer.Complete();
                }

                await File.WriteAllBytesAsync(path, stream.ToArray(), XunitCancellationToken);
            }

            var decoded = await ffmpeg.DecodeToRawFramesAsync(path, "rgba", Width, Height, XunitCancellationToken);
            Assert.Equal(frames.Length, decoded.Count);
            if (output != "jpeg")
            {
                // Lossless outputs (the GIF patterns use four opaque colors): exact pixels
                for (var i = 0; i < frames.Length; i++)
                {
                    Assert.Equal(GetBytes(frames[i]), decoded[i]);
                }
            }
        }
        finally
        {
            foreach (var frame in frames)
            {
                frame.Dispose();
            }
        }
    }

    private static string GetExtension(string output) => output switch
    {
        "jpeg" => ".jpg",
        "gif" => ".gif",
        "apng" => ".apng",
        "qoi" => ".qoi",
        "bmp" => ".bmp",
        "tga" => ".tga",
        "pnm" => ".pam",
        _ => ".png",
    };

    private static Image<Rgba32>[] CreateFrames(int count)
    {
        Rgba32[] palette = [new(255, 0, 0, 255), new(0, 255, 0, 255), new(0, 0, 255, 255), new(255, 255, 255, 255)];
        var frames = new Image<Rgba32>[count];
        for (var i = 0; i < count; i++)
        {
            var image = new Image<Rgba32>(Width, Height);
            var seed = i;
            image.Frames[0].ProcessPixelRows(pixels =>
            {
                for (var y = 0; y < pixels.Height; y++)
                {
                    var row = pixels.GetRowSpan(y);
                    for (var x = 0; x < row.Length; x++)
                    {
                        row[x] = palette[(x + y + seed) % palette.Length];
                    }
                }
            });
            image.Frames[0].Metadata.Duration = new FrameDuration(1, 10);
            frames[i] = image;
        }

        return frames;
    }

    private static byte[] GetBytes(Image<Rgba32> image)
    {
        var bytes = new byte[Width * Height * 4];
        image.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }
}
