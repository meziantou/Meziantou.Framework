using System.Buffers.Binary;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// OpenEXR files read by the OpenEXR reference library (<c>exr-to-raw</c>) and exchanged with FFmpeg's independent EXR
/// codec (libavcodec <c>exr</c>):
/// <list type="bullet">
/// <item><description>the output of the encoder, for every compression and both sample types, decodes in OpenEXR to the samples it stores, bit for bit;</description></item>
/// <item><description>the output of the encoder, for every compression and both sample types, decodes in FFmpeg to the samples it stores (colors premultiplied by alpha), values above 1 and below 0 included;</description></item>
/// <item><description>the files FFmpeg writes (every compression it has, half and float samples) decode in the library to the samples FFmpeg was given (FFmpeg multiplies them by alpha, the library divides them back).</description></item>
/// </list>
/// The decoder is compared bit for bit with OpenEXR on the files of the corpus.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class ExrInteropTests
{
    public static TheoryData<OpenExrCompression, OpenExrPixelType> EncoderCases()
    {
        var data = new TheoryData<OpenExrCompression, OpenExrPixelType>();
        foreach (var compression in Enum.GetValues<OpenExrCompression>())
        {
            data.Add(compression, OpenExrPixelType.Half);
            data.Add(compression, OpenExrPixelType.Float);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputDecodesExactlyInFFmpeg(OpenExrCompression compression, OpenExrPixelType pixelType)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (70, 33) })
        {
            using var image = CreateImage(width, height, pixelType, seed: (width * 100) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"output-{compression}-{pixelType}-{width}x{height}.exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression }, XunitCancellationToken);

            // The green, blue, red and alpha planes of the samples as they are stored (FFmpeg does not divide them by alpha),
            // in the type of the file: FFmpeg converts half samples to 32-bit floats through 16-bit integers
            var half = pixelType == OpenExrPixelType.Half;
            var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, half ? "gbrapf16le" : "gbrapf32le", width, height, XunitCancellationToken));
            var expected = GetPremultipliedSamples(image);
            var plane = width * height;
            for (var i = 0; i < plane; i++)
            {
                var actual = (Read(frame, (2 * plane) + i, half), Read(frame, i, half), Read(frame, plane + i, half), Read(frame, (3 * plane) + i, half));
                Assert.Equal(expected[i], actual);
            }
        }
    }

    [Theory]
    [MemberData(nameof(EncoderCases))]
    public async Task EncoderOutputDecodesExactlyInOpenExr(OpenExrCompression compression, OpenExrPixelType pixelType)
    {
        var openExr = await RequiredTools.OpenExrAsync();
        foreach (var (width, height) in new[] { (1, 1), (37, 45), (70, 33), (5, 130) })
        {
            using var image = CreateImage(width, height, pixelType, seed: (width * 100) + (int)compression);
            var path = InteropSettings.GetArtifactsDirectory("ExrInteropTests") / string.Create(CultureInfo.InvariantCulture, $"openexr-{compression}-{pixelType}-{width}x{height}.exr");
            await image.SaveAsync(path, new OpenExrEncoder { Compression = compression }, XunitCancellationToken);

            var part = await openExr.DecodeAsync(path, XunitCancellationToken);
            Assert.Equal(compression.ToString().ToUpperInvariant(), part.Compression);
            Assert.Equal((width, height), (part.Width, part.Height));
            var expected = GetPremultipliedSamples(image);
            for (var i = 0; i < expected.Length; i++)
            {
                var actual = (part.Channels["R"][i], part.Channels["G"][i], part.Channels["B"][i], part.Channels["A"][i]);
                Assert.Equal(expected[i], actual);
            }
        }
    }

    [Theory]
    [InlineData("none", "half")]
    [InlineData("rle", "half")]
    [InlineData("zip1", "half")]
    [InlineData("zip16", "half")]
    [InlineData("none", "float")]
    [InlineData("rle", "float")]
    [InlineData("zip1", "float")]
    [InlineData("zip16", "float")]
    public async Task FilesWrittenByFFmpegDecodeExactly(string compression, string format)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        const int Width = 23;
        const int Height = 41;

        // Planes of values that a half holds exactly, above 1 and below 0 included; alphas of 0, 1/2 and 1, by which FFmpeg
        // multiplies the colors exactly
        var random = new Random(7);
        var plane = Width * Height;
        var input = new byte[plane * 4 * 4];
        for (var i = 0; i < 4 * plane; i++)
        {
            var value = i < 3 * plane ? (float)(Half)((random.NextDouble() * 20) - 4) : (i % 3) * 0.5f;
            BinaryPrimitives.WriteSingleLittleEndian(input.AsSpan(4 * i), value);
        }

        var directory = InteropSettings.GetArtifactsDirectory("ExrInteropTests");
        var raw = directory / $"ffmpeg-{compression}-{format}.raw";
        var exr = directory / $"ffmpeg-{compression}-{format}.exr";
        await File.WriteAllBytesAsync(raw, input, XunitCancellationToken);
        var result = await ffmpeg.RunAsync(["-hide_banner", "-y", "-f", "rawvideo", "-pix_fmt", "gbrapf32le", "-s", $"{Width}x{Height}", "-i", raw, "-c:v", "exr", "-compression", compression, "-format", format, "-frames:v", "1", exr], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode, result.StandardError);

        // The library divides the colors by alpha again: only a pixel whose alpha is 0 lost its color
        using var image = Image.Load<RgbaFloat>(exr);
        for (var i = 0; i < plane; i++)
        {
            var pixel = image.Frames[0][i % Width, i / Width];
            var alpha = Read(input, (3 * plane) + i, half: false);
            float Color(int index) => alpha == 0 ? 0f : Read(input, index, half: false);
            Assert.Equal((Color((2 * plane) + i), Color(i), Color(plane + i), alpha), (pixel.R, pixel.G, pixel.B, pixel.A));
        }
    }

    private static Image CreateImage(int width, int height, OpenExrPixelType pixelType, int seed)
    {
        var random = new Random(seed);
        float Next() => (float)(Half)((random.NextDouble() * 40) - 8);
        var full = new Image<RgbaFloat>(width, height);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                full.Frames[0][x, y] = (x + y) % 5 == 0 ? new RgbaFloat(1f, 1f, 1f) : new RgbaFloat(Next(), Next(), Next(), (float)(Half)random.NextDouble());
            }
        }

        if (pixelType == OpenExrPixelType.Float)
            return full;

        using (full)
        {
            return full.CloneAs<RgbaHalf>();
        }
    }

    /// <summary>The samples the file stores: the colors multiplied by alpha, rounded to the type of the file, unless alpha is 0.</summary>
    private static (float R, float G, float B, float A)[] GetPremultipliedSamples(Image image)
    {
        var half = image is Image<RgbaHalf>;
        using var full = image is Image<RgbaFloat> source ? source.CloneAs<RgbaFloat>() : ((Image<RgbaHalf>)image).CloneAs<RgbaFloat>();
        var result = new (float, float, float, float)[image.Width * image.Height];
        for (var i = 0; i < result.Length; i++)
        {
            var pixel = full.Frames[0][i % image.Width, i / image.Width];
            float Store(float color)
            {
                var value = pixel.A == 0 ? color : (double)color * pixel.A;
                return half ? (float)(Half)value : (float)value;
            }

            result[i] = (Store(pixel.R), Store(pixel.G), Store(pixel.B), pixel.A);
        }

        return result;
    }

    private static float Read(byte[] data, int index, bool half)
        => half ? (float)BinaryPrimitives.ReadHalfLittleEndian(data.AsSpan(2 * index)) : BinaryPrimitives.ReadSingleLittleEndian(data.AsSpan(4 * index));
}
