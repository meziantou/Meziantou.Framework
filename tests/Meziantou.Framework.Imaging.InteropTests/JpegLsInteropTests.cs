using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
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
/// Nothing is committed: each file is downloaded at the commit pinned here the first time a test needs it, checked
/// against its SHA-256, and kept in <c>tests/Meziantou.Framework.Imaging.External/jpegls-conformance</c>. A download
/// failure or a hash mismatch fails the test. The ISO reference decoder is not an oracle here: it decodes the lossless
/// 8-bit streams like the library, but not the near-lossless and 16-bit streams that CharLS, FFmpeg and the library agree on.
/// </remarks>
public sealed class JpegLsInteropTests
{
    private const string Commit = "e682efabdf5f55bb6758b56902dd741958db0e70";

    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromMinutes(5) };
    private static readonly ConcurrentDictionary<string, Lazy<Task<FullPath>>> Downloads = new(StringComparer.Ordinal);

    private static readonly Dictionary<string, string> Files = new(StringComparer.Ordinal)
    {
        ["conformance/t8c0e0.jls"] = "8c564fbd3a8667bd071cc8d994952fdfae3d62db5c359be4b6d6734e89acea6d",
        ["conformance/t8c1e0.jls"] = "fdd6fa22f94135f7c3db7932da2154aefc79085fec3b3f65da8a62d6964b8078",
        ["conformance/t8c2e0.jls"] = "2cbf1d38b9d186a06ea7b19cc74df6259d238c789f49ed7329a8e34afd6ba5ae",
        ["conformance/t8c0e3.jls"] = "6356737dbf5168000cebc5e4056e04eb687664cd15797de324fa0845eb407dc3",
        ["conformance/t8c1e3.jls"] = "be41c9c2687542d452171ae629c76905b7af7073d9db56f9a549b6323df6ed1e",
        ["conformance/t8c2e3.jls"] = "df1fa8e1ac3256a2ea226996d27c8bd504a7ca08385674aedf77b6edd42be8de",
        ["conformance/t8nde0.jls"] = "c3e1244dfc035626cbdea7a89a8120fde3ae4deb22847695928cfbd5f36884ae",
        ["conformance/t8nde3.jls"] = "0597c16d6d60d89f0aa9e71a8fd6bbf982ef1ae22d4b8afc897dafa68efd90e8",
        ["conformance/t8sse0.jls"] = "bee806d92fb11dcdaf4ad1dbcc695622f95fdd4d8a7d6d77f72fc1a0d98f6d26",
        ["conformance/t8sse3.jls"] = "215e3e176124749917ba9676c459709725721cc26e0851b4f97c1aa5f677fb0f",
        ["conformance/t16e0.jls"] = "0169aab6eb839925cc781016e3c3ed19d323fadee99d9747375e787b88e4d23f",
        ["conformance/t16e3.jls"] = "e3b7327d232247949bd6aa4520d3a2627bb60c952ff23d700c92900a70863813",
        ["conformance/t16e3.pgm"] = "1f607209dc3284c57efe9bbf53055b5e22182a4f3690929b88f19f277b7ed0ef",
        ["conformance/test8.ppm"] = "a7ecaa841b8a7dc131a73007f0d6c07732e901029810e45ca3cc788fdf9e9593",
        ["conformance/test16.pgm"] = "1eb2001a0fe66c9d44776b40a35aaa3b68a4fe74cb749e6271d96523378149d2",
        ["conformance/test8r.pgm"] = "9474fbec2fe54221b0943f4f43014f70469a2478654d1f4ac1de05bed3ceb182",
        ["conformance/test8gr4.pgm"] = "9cb5b43912836fce8b1f40eb2ad67fed9d1c303421641e67d6759909d10e37d3",
        ["conformance/test8bs2.pgm"] = "6cf4289f0afd89d0622ff0bfc04a830770b104b969ba69e8e952b1834faf69a4",
        ["banny.ppm"] = "0e097784018403e4dc7179a99d773bf269f88f89d033cce2d1bca4579ca322fa",
        ["banny.jls"] = "0b032c12ab05f8c00cb761519648e5944d4b7a13e76de50e46367781e071831d",
        ["banny-hp1.jls"] = "f9e2ae0bf1611e988b41cab4c6c5f5a02d545a2eeaf7d6899f6247116e489ddf",
        ["banny-hp2.jls"] = "64afd59d634fc805e9964068bb32509a09e52d4c2fd9a95947a25386d71c106b",
        ["banny-hp3.jls"] = "efe28ac61d56b22206cd56b6f70744446bac3be1e6e5661d9cffc23e9199ad37",
        ["test8_ilv_line_rm_7.jls"] = "4765957339a2af0220b22802574a19e566331fdd8b8f6b5bf6554df94087affc",
        ["test16_rm_5.jls"] = "d29bbb0fdd5ab7cfecfe90e6e5e3dbcff373172b6300bd0e48b9d910d7572853",
    };

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
    /// interleaved; it refuses near-lossless 16-bit samples): the library decodes them like the FFmpeg decoder, within NEAR
    /// of the source samples.
    /// </summary>
    [Theory]
    [InlineData("gray", 8, 0)]
    [InlineData("gray", 8, 2)]
    [InlineData("gray16le", 16, 0)]
    [InlineData("gray", 8, 5)]
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
        var reference = await DecodeWithFFmpegAsync(ffmpeg, path, pixelFormat, Width, Height, channels);
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
        Assert.Equal(SamplesOf(image, maxValue), await DecodeWithFFmpegAsync(ffmpeg, path, pixelFormat, Width, Height, channels));
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

    private static async Task<int[]> DecodeWithFFmpegAsync(FFmpegTool ffmpeg, FullPath path, string pixelFormat, int width, int height, int channels)
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
        var sha256 = Files[path];
        var lazy = Downloads.GetOrAdd(sha256, _ => new Lazy<Task<FullPath>>(() => DownloadAsync(sha256, $"https://raw.githubusercontent.com/team-charls/charls/{Commit}/test/data/{path}")));
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
}
