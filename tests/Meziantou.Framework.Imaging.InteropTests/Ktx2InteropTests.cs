using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// KTX 2.0 files written by the reference tools from new inputs, decoded by the library and by the tools:
/// <list type="bullet">
/// <item><description>ASTC of every block size, written by <c>ktx create</c> (astcenc): the sRGB texels of <c>ktx extract</c>, and the decode_float16 halves of <c>astcenc -dh</c> for UNORM;</description></item>
/// <item><description>UASTC of every quality level and ETC1S of several compression levels, linear: <c>ktx extract --transcode rgba8</c>; sRGB UASTC: its lossless transcoding to ASTC 4x4 by basisu, decoded as ASTC sRGB by <c>ktx extract</c>;</description></item>
/// <item><description>ETC2 and EAC blocks of random bytes: Apple ImageIO (macOS), which reads the RGB, RGBA and unsigned EAC formats as the specification does.</description></item>
/// </list>
/// The PNG files of the tools are read by FFmpeg.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class Ktx2InteropTests
{
    private const int Width = 41;
    private const int Height = 23;

    public static TheoryData<string> AstcSizes => ["4x4", "5x4", "5x5", "6x5", "6x6", "8x5", "8x6", "8x8", "10x5", "10x6", "10x8", "10x10", "12x10", "12x12"];

    private static byte[] Pattern(int seed)
    {
        var data = new byte[Width * Height * 4];
        var state = (uint)seed + 7;
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                state ^= state << 13;
                state ^= state >> 17;
                state ^= state << 5;
                var offset = ((y * Width) + x) * 4;
                data[offset] = (byte)(127 + (120 * Math.Sin((x / 4.0) + seed)));
                data[offset + 1] = (byte)(x * 255 / (Width - 1));
                data[offset + 2] = (x + y) % 9 == 0 ? (byte)(state >> 24) : (byte)(y * 11);
                data[offset + 3] = (byte)((x + y + seed) % 5 == 0 ? y * 10 : 255);
            }
        }

        return data;
    }

    private static async Task<FullPath> WritePamAsync(FullPath directory, string name, byte[] rgba)
    {
        var path = directory / name;
        await File.WriteAllBytesAsync(path, [.. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {Width}\nHEIGHT {Height}\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n")), .. rgba], XunitCancellationToken);
        return path;
    }

    private static async Task<byte[]> ExtractRgba8Async(Ktx2Tool ktx, FFmpegTool ffmpeg, FullPath file, string[] options, FullPath scratch)
    {
        var png = scratch / (file.NameWithoutExtension + "-extract.png");
        File.Delete(png);
        await ktx.RunKtxAsync(["extract", .. options, file, png], XunitCancellationToken);
        return Assert.Single(await ffmpeg.DecodeToRawFramesAsync(png, "rgba", Width, Height, XunitCancellationToken));
    }

    private static byte[] Decode(FullPath file, Func<Image, Image> convert)
    {
        using var image = Image.Load(file);
        using var converted = convert(image);
        var bytes = new byte[converted.Width * converted.Height * PixelFormats.GetBytesPerPixel(converted.PixelFormat)];
        converted.Frames[0].CopyPixelBytesTo(bytes);
        return bytes;
    }

    [Theory]
    [MemberData(nameof(AstcSizes))]
    public async Task AstcDecodesAsKtxSoftwareAndAstcenc(string size)
    {
        var ktx = await RequiredTools.Ktx2Async();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("Ktx2InteropTests") / ("astc-" + size);
        Directory.CreateDirectory(directory);
        var pam = await WritePamAsync(directory, "input.pam", Pattern(size.Length));
        var dimensions = size.Split('x').Select(value => int.Parse(value, CultureInfo.InvariantCulture)).ToArray();

        var srgb = directory / "srgb.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--format", $"ASTC_{size}_SRGB_BLOCK", "--astc-quality", "thorough", pam, srgb], XunitCancellationToken);
        Assert.Equal(await ExtractRgba8Async(ktx, ffmpeg, srgb, [], directory), Decode(srgb, image => image.CloneAs<Rgba32>()));

        var unorm = directory / "unorm.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--format", $"ASTC_{size}_UNORM_BLOCK", "--astc-quality", "thorough", pam, unorm], XunitCancellationToken);
        var halves = await ktx.DecodeAstcHalvesAsync(unorm, dimensions[0], dimensions[1], Width, Height, directory, XunitCancellationToken);
        using var image = Assert.IsType<Image<RgbaHalf>>(Image.Load(unorm));
        for (var y = 0; y < Height; y++)
        {
            for (var x = 0; x < Width; x++)
            {
                var offset = ((y * Width) + x) * 4;
                Assert.Equal(new RgbaHalf(halves[offset], halves[offset + 1], halves[offset + 2], halves[offset + 3]), image.Frames[0][x, y]);
            }
        }
    }

    public static TheoryData<string, string[]> BasisEncodings => new()
    {
        { "uastc-0", ["--encode", "uastc", "--uastc-quality", "0"] },
        { "uastc-1-zstd", ["--encode", "uastc", "--uastc-quality", "1", "--zstd", "5"] },
        { "uastc-2", ["--encode", "uastc", "--uastc-quality", "2"] },
        { "uastc-3", ["--encode", "uastc", "--uastc-quality", "3"] },
        { "uastc-4", ["--encode", "uastc", "--uastc-quality", "4"] },
        { "etc1s-1", ["--encode", "basis-lz", "--clevel", "1"] },
        { "etc1s-3-q64", ["--encode", "basis-lz", "--clevel", "3", "--qlevel", "64"] },
        { "etc1s-5-q255", ["--encode", "basis-lz", "--clevel", "5", "--qlevel", "255"] },
    };

    [Theory]
    [MemberData(nameof(BasisEncodings))]
    public async Task LinearBasisUniversalDecodesAsKtxSoftware(string name, string[] options)
    {
        var ktx = await RequiredTools.Ktx2Async();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("Ktx2InteropTests") / name;
        Directory.CreateDirectory(directory);
        var pam = await WritePamAsync(directory, "input.pam", Pattern(name.Length * 3));
        var file = directory / "linear.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--format", "R8G8B8A8_UNORM", .. options, pam, file], XunitCancellationToken);
        Assert.Equal(await ExtractRgba8Async(ktx, ffmpeg, file, ["--transcode", "rgba8"], directory), Decode(file, image => image.CloneAs<Rgba32>()));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    [InlineData(4)]
    public async Task SrgbUastcDecodesAsItsAstcTranscoding(int quality)
    {
        var ktx = await RequiredTools.Ktx2Async();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("Ktx2InteropTests") / ("uastc-srgb-" + quality.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(directory);
        var pam = await WritePamAsync(directory, "input.pam", Pattern(quality + 40));
        var file = directory / "srgb.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--format", "R8G8B8A8_SRGB", "--encode", "uastc", "--uastc-quality", quality.ToString(CultureInfo.InvariantCulture), pam, file], XunitCancellationToken);

        // basisu transcodes the UASTC blocks to ASTC 4x4 without loss; ktx extract decodes them in the sRGB operation mode
        await ktx.RunBasisuAsync(["-unpack", file, "-output_path", directory], XunitCancellationToken);
        var transcoded = Directory.GetFiles(directory, "*_transcoded_ASTC_LDR_4X4_RGBA_*.ktx").Order(StringComparer.Ordinal).First();
        var blocks = directory / "astc.raw";
        await File.WriteAllBytesAsync(blocks, Ktx2Tool.ReadKtx1Image(await File.ReadAllBytesAsync(transcoded, XunitCancellationToken)), XunitCancellationToken);
        var astc = directory / "astc.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--raw", "--format", "ASTC_4x4_SRGB_BLOCK", "--width", Width.ToString(CultureInfo.InvariantCulture), "--height", Height.ToString(CultureInfo.InvariantCulture), blocks, astc], XunitCancellationToken);
        Assert.Equal(await ExtractRgba8Async(ktx, ffmpeg, astc, [], directory), Decode(file, image => image.CloneAs<Rgba32>()));
    }

    [Theory]
    [InlineData("ETC2_R8G8B8_UNORM_BLOCK", 8)]
    [InlineData("ETC2_R8G8B8_SRGB_BLOCK", 8)]
    [InlineData("ETC2_R8G8B8A8_UNORM_BLOCK", 16)]
    [InlineData("ETC2_R8G8B8A8_SRGB_BLOCK", 16)]
    [InlineData("EAC_R11_UNORM_BLOCK", 8)]
    [InlineData("EAC_R11G11_UNORM_BLOCK", 16)]
    public async Task Etc2DecodesAsAppleImageIO(string format, int blockBytes)
    {
        RequiredTools.AppleImageIO();
        var ktx = await RequiredTools.Ktx2Async();
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var directory = InteropSettings.GetArtifactsDirectory("Ktx2InteropTests") / format;
        Directory.CreateDirectory(directory);

        // Random blocks reach every mode: individual, differential, T, H and planar
        var blocks = new byte[11 * 6 * blockBytes];
        new Random(format.Length).NextBytes(blocks);
        var raw = directory / "blocks.raw";
        await File.WriteAllBytesAsync(raw, blocks, XunitCancellationToken);
        var file = directory / "random.ktx2";
        await ktx.RunKtxAsync(["create", "--testrun", "--raw", "--format", format, "--width", Width.ToString(CultureInfo.InvariantCulture), "--height", Height.ToString(CultureInfo.InvariantCulture), raw, file], XunitCancellationToken);
        var png = directory / "imageio.png";
        File.Delete(png);
        var sips = await ProcessRunner.RunAsync("sips", ["-s", "format", "png", file, "--out", png], XunitCancellationToken);
        Assert.Equal(0, sips.ExitCode);
        var expected = Assert.Single(await ffmpeg.DecodeToRawFramesAsync(png, "rgba64le", Width, Height, XunitCancellationToken));
        Assert.Equal(expected, Decode(file, image => image.CloneAs<Rgba64>()));
    }
}
