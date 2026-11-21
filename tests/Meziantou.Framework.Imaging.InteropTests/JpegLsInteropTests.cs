using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Meziantou.Framework.Imaging.TestHarness;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using Meziantou.Framework.Imaging.Tests.Codecs;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// The conformance streams of ITU-T T.87 (Annex E), as the CharLS repository keeps them (<c>test/data</c>, BSD-3-Clause),
/// with the images they code: lossless and near-lossless (<c>NEAR = 3</c>) streams of an 8-bit RGB image in the three
/// interleave modes, preset coding parameters (LSE), subsampled components, 12-bit samples; and files of CharLS with
/// restart intervals and with the HP color transforms. The library must decode a lossless stream to its image exactly,
/// and a near-lossless one within <c>NEAR</c> of it (exactly to the decoded image CharLS keeps for the 12-bit one).
/// </summary>
/// <remarks>
/// Nothing is committed: each file is downloaded at the commit pinned in <c>JpegLsConformance/corpus.json</c> the first
/// time a test needs it, checked against its SHA-256, and kept in <c>tests/Meziantou.Framework.Imaging.External/jpegls-conformance</c>
/// (where <c>eng/imaging/fetch-tests.cs</c> fetches them all from the mirror of the corpora). A download failure or a
/// hash mismatch fails the test. The ISO reference decoder is not an oracle here: it decodes the lossless
/// 8-bit streams like the library, but not the near-lossless and 16-bit streams that CharLS, FFmpeg and the library agree on.
/// </remarks>
public sealed class JpegLsInteropTests
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<FullPath>>> Downloads = new(StringComparer.Ordinal);

    private static readonly Pins CorpusPins = ReadPins();

    private static FullPath ObjectsDirectory => RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.External" / "jpegls-conformance";

    [Theory]
    [InlineData("conformance/t8c0e0.jls", "conformance/test8.ppm", 0)]
    [InlineData("conformance/t8c1e0.jls", "conformance/test8.ppm", 0)]
    [InlineData("conformance/t8c2e0.jls", "conformance/test8.ppm", 0)]
    [InlineData("conformance/t8c0e3.jls", "conformance/test8.ppm", 3)]
    [InlineData("conformance/t8c1e3.jls", "conformance/test8.ppm", 3)]
    [InlineData("conformance/t8c2e3.jls", "conformance/test8.ppm", 3)]
    [InlineData("conformance/t8nde0.jls", "conformance/test8bs2.pgm", 0)]
    [InlineData("conformance/t8nde3.jls", "conformance/test8bs2.pgm", 3)]
    [InlineData("conformance/t16e0.jls", "conformance/test16.pgm", 0)]
    [InlineData("conformance/t16e3.jls", "conformance/t16e3.pgm", 0)]
    [InlineData("conformance/t16e3.jls", "conformance/test16.pgm", 3)]
    [InlineData("banny.jls", "banny.ppm", 0)]
    [InlineData("banny-hp1.jls", "banny.ppm", 0)]
    [InlineData("banny-hp2.jls", "banny.ppm", 0)]
    [InlineData("banny-hp3.jls", "banny.ppm", 0)]
    [InlineData("test8_ilv_line_rm_7.jls", "conformance/test8.ppm", 0)]
    [InlineData("test16_rm_5.jls", "conformance/test16.pgm", 0)]
    public async Task StreamsDecodeToTheirImage(string stream, string image, int near)
    {
        var data = await File.ReadAllBytesAsync(await GetAsync(stream), XunitCancellationToken);
        var reference = ReferencePnm.Parse(await File.ReadAllBytesAsync(await GetAsync(image), XunitCancellationToken));
        var info = Image.Identify(data);
        Assert.Equal(ImageFormat.Jpeg, info.Format);
        Assert.Equal((reference.Width, reference.Height), (info.Width, info.Height));
        Assert.Equal(reference.MaxValue, (1 << info.BitsPerComponent) - 1);
        using var decoded = Image.Load(data);
        AssertWithin(JpegDecoderDifferentialTests.GetSamples(reference, reference.MaxValue), JpegDecoderDifferentialTests.GetSamples(decoded, reference.MaxValue), near, stream);
    }

    /// <summary>
    /// The streams of subsampled components (T.87 E.2): a 256x256 component, a 256x64 one and a 128x128 one, line
    /// interleaved. The first two are upsampled by replication (factors of 1 and 4), so the image holds their samples.
    /// </summary>
    [Theory]
    [InlineData("conformance/t8sse0.jls", 0)]
    [InlineData("conformance/t8sse3.jls", 3)]
    public async Task SubsampledComponentsDecodeToTheirPlanes(string stream, int near)
    {
        var data = await File.ReadAllBytesAsync(await GetAsync(stream), XunitCancellationToken);
        using var decoded = Image.Load(data);
        Assert.Equal(new Size(256, 256), decoded.Size);
        var samples = JpegDecoderDifferentialTests.GetSamples(decoded, 255);
        var first = ReferencePnm.Parse(await File.ReadAllBytesAsync(await GetAsync("conformance/test8r.pgm"), XunitCancellationToken));
        var second = ReferencePnm.Parse(await File.ReadAllBytesAsync(await GetAsync("conformance/test8gr4.pgm"), XunitCancellationToken));
        Assert.Equal((256, 64), (second.Width, second.Height));
        var expected = JpegDecoderDifferentialTests.GetSamples(first, 255);
        AssertWithin([.. Enumerable.Range(0, 256 * 256).Select(pixel => expected[pixel * 3])], [.. Enumerable.Range(0, 256 * 256).Select(pixel => samples[pixel * 3])], near, stream + " (first component)");
        var expectedSecond = JpegDecoderDifferentialTests.GetSamples(second, 255);
        AssertWithin([.. Enumerable.Range(0, 256 * 64).Select(pixel => expectedSecond[pixel * 3])], [.. Enumerable.Range(0, 256 * 64).Select(pixel => samples[((((pixel / 256) * 4 * 256) + (pixel % 256)) * 3) + 1])], near, stream + " (second component)");
    }

    /// <summary>
    /// Files of the JPEG-LS encoder of FFmpeg (its <c>-pred</c> option sets NEAR; gray samples are one scan, RGB ones line
    /// interleaved; the option takes 0 to 2): the library decodes them like the FFmpeg decoder, within NEAR
    /// of the source samples.
    /// </summary>
    [Theory]
    [InlineData("gray", 8, 0)]
    [InlineData("gray", 8, 2)]
    [InlineData("gray16le", 16, 0)]
    [InlineData("gray16le", 16, 2)]
    [InlineData("rgb24", 8, 0)]
    [InlineData("rgb24", 8, 1)]
    public async Task FilesOfFFmpegDecodeLikeFFmpeg(string pixelFormat, int precision, int near)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(FilesOfFFmpegDecodeLikeFFmpeg));
        const int Width = 37;
        const int Height = 23;
        var channels = pixelFormat == "rgb24" ? 3 : 1;
        var maxValue = (1 << precision) - 1;
        var source = JpegLsTestImage.CreateRandom(Width, Height, channels == 1 ? [(1, 1)] : [(1, 1), (1, 1), (1, 1)], precision, seed: near + precision);
        source.Encode();
        var expected = SamplesOf(source, maxValue);
        var name = string.Create(CultureInfo.InvariantCulture, $"{pixelFormat}-near{near}");
        var sourcePath = directory / (name + (channels == 1 ? ".pgm" : ".ppm"));
        await File.WriteAllBytesAsync(sourcePath, WritePnm(Width, Height, channels, maxValue, expected), XunitCancellationToken);
        var path = directory / (name + ".jls");
        var result = await ffmpeg.RunAsync(["-y", "-f", "image2", "-i", sourcePath, "-pix_fmt", pixelFormat, "-c:v", "jpegls", "-pred", near.ToString(CultureInfo.InvariantCulture), "-f", "image2", path], XunitCancellationToken);
        Assert.Equal(0, result.ExitCode);

        var data = await File.ReadAllBytesAsync(path, XunitCancellationToken);
        var scan = data.AsSpan().IndexOf([(byte)0xFF, (byte)0xDA]);
        Assert.Equal(near, data[scan + 5 + (2 * channels)]);
        var reference = await DecodeWithFFmpegAsync(ffmpeg, path, pixelFormat, Width, Height);
        using var decoded = Image.Load(data);
        var samples = ToChannels(JpegDecoderDifferentialTests.GetSamples(decoded, maxValue), channels);
        Assert.Equal(reference, samples);
        AssertWithin(expected, samples, near, name);
    }

    /// <summary>
    /// Files of the test-side encoder (<see cref="JpegLsTestImage"/>) decode in FFmpeg to the samples the encoder
    /// reconstructs, which the library decodes them to (Jpeg.Tests). FFmpeg does not read the sample-interleaved mode
    /// (<c>ILV = 2</c>) right: it misreads the conformance stream <c>t8c2e0</c> too, which the library decodes exactly.
    /// </summary>
    [Theory]
    [InlineData(8, 1, 0, 0, 0, 0)]
    [InlineData(8, 1, 3, 0, 0, 0)]
    [InlineData(8, 1, 0, 0, 3, 0)]
    [InlineData(16, 1, 0, 0, 0, 0)]
    [InlineData(16, 1, 5, 0, 2, 0)]
    [InlineData(8, 3, 0, 0, 0, 0)]
    [InlineData(8, 3, 0, 1, 0, 0)]
    [InlineData(8, 3, 2, 1, 4, 0)]
    [InlineData(8, 3, 0, 1, 0, 1)]
    [InlineData(8, 3, 0, 1, 0, 2)]
    [InlineData(8, 3, 1, 1, 0, 3)]
    public async Task FilesOfTheTestEncoderDecodeInFFmpeg(int precision, int channels, int near, int interleave, int restartInterval, int colorTransform)
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory(nameof(FilesOfTheTestEncoderDecodeInFFmpeg));
        const int Width = 41;
        const int Height = 19;
        var maxValue = (1 << precision) - 1;
        var image = JpegLsTestImage.CreateRandom(Width, Height, channels == 1 ? [(1, 1)] : [(1, 1), (1, 1), (1, 1)], precision, seed: precision + near + interleave + restartInterval + colorTransform);
        var data = image.Encode(new JpegLsTestOptions { Near = near, Interleave = interleave, RestartInterval = restartInterval, ColorTransform = colorTransform });
        var path = directory / string.Create(CultureInfo.InvariantCulture, $"p{precision}-c{channels}-n{near}-i{interleave}-r{restartInterval}-t{colorTransform}.jls");
        await File.WriteAllBytesAsync(path, data, XunitCancellationToken);
        var pixelFormat = channels == 3 ? "rgb24" : precision == 8 ? "gray" : "gray16le";
        Assert.Equal(SamplesOf(image, maxValue), await DecodeWithFFmpegAsync(ffmpeg, path, pixelFormat, Width, Height));
    }

    /// <summary>Gets the samples of the last encoding of a test image, one or three per pixel, at the precision of <paramref name="maxValue"/>.</summary>
    private static int[] SamplesOf(JpegLsTestImage image, int maxValue)
    {
        var pixels = image.GetExpectedPixels();
        if (maxValue == 255)
            return [.. pixels.Select(value => (int)value)];

        // 16-bit gray samples, native byte order
        return [.. Enumerable.Range(0, pixels.Length / 2).Select(i => (int)BitConverter.ToUInt16(pixels, 2 * i))];
    }

    private static async Task<int[]> DecodeWithFFmpegAsync(FFmpegTool ffmpeg, FullPath path, string pixelFormat, int width, int height)
    {
        var frame = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(path, pixelFormat, width, height, XunitCancellationToken));
        return pixelFormat == "gray16le"
            ? [.. Enumerable.Range(0, frame.Length / 2).Select(i => (int)BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(2 * i)))]
            : [.. frame.Select(value => (int)value)];
    }

    /// <summary>Keeps one sample of the three that <see cref="JpegDecoderDifferentialTests.GetSamples(Image, int)"/> gives per gray pixel.</summary>
    private static int[] ToChannels(int[] rgb, int channels) => channels == 3 ? rgb : [.. Enumerable.Range(0, rgb.Length / 3).Select(i => rgb[i * 3])];

    private static byte[] WritePnm(int width, int height, int channels, int maxValue, int[] samples)
    {
        var header = Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"{(channels == 1 ? "P5" : "P6")}\n{width} {height}\n{maxValue}\n"));
        var wide = maxValue > 255;
        var data = new byte[header.Length + (samples.Length * (wide ? 2 : 1))];
        header.CopyTo(data, 0);
        for (var i = 0; i < samples.Length; i++)
        {
            if (wide)
            {
                BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(header.Length + (2 * i)), (ushort)samples[i]);
            }
            else
            {
                data[header.Length + i] = (byte)samples[i];
            }
        }

        return data;
    }

    private static void AssertWithin(int[] expected, int[] actual, int near, string name)
    {
        Assert.HasCount(expected.Length, actual);
        var max = 0;
        var index = -1;
        for (var i = 0; i < expected.Length; i++)
        {
            var difference = Math.Abs(expected[i] - actual[i]);
            if (difference > max)
            {
                max = difference;
                index = i;
            }
        }

        Assert.True(max <= near, $"{name}: sample {index} is {(index < 0 ? 0 : actual[index])} instead of {(index < 0 ? 0 : expected[index])} (difference {max}, allowed {near}).");
    }

    private static Task<FullPath> GetAsync(string path)
    {
        // One download per file and per process; the cancellation of the first caller does not cancel the others
        var sha256 = CorpusPins.Paths[path];
        var url = CorpusPins.FileUrl.Replace("{commit}", CorpusPins.Commit, StringComparison.Ordinal).Replace("{path}", path, StringComparison.Ordinal);
        var lazy = Downloads.GetOrAdd(sha256, _ => new Lazy<Task<FullPath>>(() => DownloadAsync(sha256, url)));
        return lazy.Value.WaitAsync(XunitCancellationToken);
    }

    private static async Task<FullPath> DownloadAsync(string sha256, string url)
    {
        var path = ObjectsDirectory / sha256;
        if (File.Exists(path) && await HashAsync(path).ConfigureAwait(false) == sha256)
            return path;

        Directory.CreateDirectory(ObjectsDirectory);
        const int MaxAttempts = 3;
        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            // Another test process (the other target framework) may download the same file: each writes its own
            // temporary file and moves it into place once its hash is checked
            var temporary = ObjectsDirectory / $"{sha256}.{Guid.NewGuid():N}.tmp";
            try
            {
                var bytes = await HttpClient.GetByteArrayAsync(url).ConfigureAwait(false);
                var actual = Convert.ToHexStringLower(SHA256.HashData(bytes));
                if (actual != sha256)
                    throw new InvalidDataException($"'{url}' has the SHA-256 {actual} instead of the pinned {sha256}.");

                await File.WriteAllBytesAsync(temporary, bytes).ConfigureAwait(false);
                File.Move(temporary, path, overwrite: true);
                return path;
            }
            catch (Exception ex) when (attempt < MaxAttempts && ex is HttpRequestException or IOException or TaskCanceledException)
            {
                await Task.Delay(TimeSpan.FromSeconds(2 << attempt)).ConfigureAwait(false);
            }
            finally
            {
                File.Delete(temporary);
            }
        }

        // The last attempt rethrows its exception
        throw new UnreachableException();
    }

    private static async Task<string> HashAsync(FullPath path)
    {
        var stream = File.OpenRead(path);
        await using (stream.ConfigureAwait(false))
        {
            return Convert.ToHexStringLower(await SHA256.HashDataAsync(stream).ConfigureAwait(false));
        }
    }

    private static Pins ReadPins()
    {
        var path = RepositoryPaths.Root / "tests" / "Meziantou.Framework.Imaging.InteropTests" / "JpegLsConformance" / "corpus.json";
        using var document = JsonDocument.Parse(File.ReadAllBytes(path));
        var root = document.RootElement;
        return new Pins(
            root.GetProperty("commit").GetString()!,
            root.GetProperty("files").GetString()!,
            root.GetProperty("paths").EnumerateObject().ToDictionary(property => property.Name, property => property.Value.GetString()!, StringComparer.Ordinal));
    }

    private sealed record Pins(string Commit, string FileUrl, Dictionary<string, string> Paths);
}
