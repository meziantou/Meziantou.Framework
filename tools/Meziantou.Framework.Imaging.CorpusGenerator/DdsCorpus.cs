using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Dds;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the DDS fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/dds, invalid/dds and
/// limit/dds entries). This generator is the reviewed, reproducible recipe of every committed DDS fixture. It is NEVER run
/// by the tests and does not use the library under test.
/// <para>
/// The files come from three writers: <see cref="DdsFileBuilder"/> (test harness, from the DDS reference of Microsoft Learn)
/// assembles every pixel format from stored samples (random bytes, floating-point patterns, random blocks whose modes cover
/// every BC6H and BC7 mode), partial cube maps, alpha modes and malformed headers; <c>texconv</c> and <c>texassemble</c> of
/// DirectXTex encode patterns to the block-compressed formats and assemble mipmaps, cube maps, arrays and volume textures;
/// ImageMagick writes the files of a common third-party writer.
/// </para>
/// <para>
/// The references: the uncompressed formats are hand-computed by <see cref="ReferenceDds"/> from the definitions of their
/// samples; BC1 to BC5 by <see cref="ReferenceS3tc"/>, from the rational numbers of the Khronos Data Format Specification
/// rounded once to the precision of the format; BC6H and BC7, which the specification defines in integers and half-float bit
/// patterns, are what DirectXTex decodes (<c>texconv</c>), cross-checked with the DDS reader of OpenImageIO. Every
/// hand-computed reference is cross-checked with <c>texconv</c>; the disagreements are recorded: DirectXTex rounds the
/// midpoints of the three-color mode of BC1 either way (single-precision arithmetic of an exact half), multiplies the
/// signed BC5 values by 1/127 instead of dividing them (within 1 ulp), and swaps the red and blue masks of the legacy
/// 10:10:10:2 layouts.
/// </para>
/// It only replaces the DDS entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "dds") and their
/// files, and keeps every other entry and file unchanged.
/// Usage (from the repository root, on Windows: texconv, texassemble and texdiag are Windows-only):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dds
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- dds --write
/// Requirements: DirectXTex (texconv, texassemble), OpenImageIO (oiiotool), ImageMagick and FFmpeg from the
/// meziantou/prebuilt release pinned by the Meziantou.Prebuilt package.
/// </summary>
internal static class DdsCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/DdsCorpus.cs";

    private const string PinnedDirectXTex = "may2026";
    private const string PinnedOpenImageIo = "3.2.2.0";
    private const string PinnedMagick = "7.1.2-35";

    private static readonly string[] FaceNames = ["PositiveX", "NegativeX", "PositiveY", "NegativeY", "PositiveZ", "NegativeZ"];

    private static bool IsDdsFixture(Obj fixture) => (string?)fixture["format"] == "dds";

    public static int Run(GeneratorOptions options)
    {
        if (!OperatingSystem.IsWindows())
            throw new FatalException("The dds generator runs on Windows: texconv and texassemble of DirectXTex, the reference of the BC6H and BC7 fixtures and the encoder of the block-compressed ones, are Windows-only.");

        var tools = new DdsTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-dds-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => $"{fixtures} DDS fixtures; corpus: {files} files, {bytes} bytes",
            "The committed DDS fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, DdsTools tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<DdsTools>(outDir, tools, ScriptPath))
        {
            BuildUncompressed(corpus);
            BuildLegacy(corpus);
            BuildRandomBlocks(corpus);
            BuildTexconv(corpus);
            BuildStructures(corpus);
            BuildMagick(corpus);
            BuildInvalid(corpus);
            BuildUnsupported(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsDdsFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsDdsFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Samples
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Deterministic bytes (xorshift32) for stored samples and blocks.</summary>
    private static byte[] RandomBytes(int length, uint seed)
    {
        var state = seed * 2654435761u | 1;
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            bytes[i] = (byte)(state >> 24);
        }

        return bytes;
    }

    /// <summary>
    /// A floating-point pattern: values from -0.25 to 3.25 in steps of 1/8 (exact in halves), the channels of a pixel
    /// different, alpha in [0, 1].
    /// </summary>
    private static double Pattern(int x, int y, int channel, int seed)
    {
        if (channel == 3)
            return ((x + (3 * y) + seed) % 9) / 8.0;

        return ((((x * 7) + (y * 13) + (channel * 5) + seed) % 29) / 8.0) - 0.25;
    }

    private static byte[] Halves(int width, int height, int channels, int seed)
    {
        var data = new byte[width * height * channels * 2];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    BinaryPrimitives.WriteHalfLittleEndian(data.AsSpan((((y * width) + x) * channels + c) * 2), (Half)Pattern(x, y, c == channels - 1 && channels == 4 ? 3 : c, seed));
                }
            }
        }

        return data;
    }

    private static byte[] Floats(int width, int height, int channels, int seed)
    {
        var data = new byte[width * height * channels * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    // A third of a unit makes values that are not halves
                    var value = Pattern(x, y, c == channels - 1 && channels == 4 ? 3 : c, seed) + (c == 1 ? 1 / 3.0 : 0);
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((((y * width) + x) * channels + c) * 4), (float)value);
                }
            }
        }

        return data;
    }

    /// <summary>R11G11B10_FLOAT pixels of random bits whose exponents are finite (below 31).</summary>
    private static byte[] SmallFloats(int width, int height, uint seed)
    {
        var data = RandomBytes(width * height * 4, seed);
        for (var i = 0; i < width * height; i++)
        {
            var value = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(i * 4));
            foreach (var (shift, mantissaBits) in new[] { (0, 6), (11, 6), (22, 5) })
            {
                var exponentShift = shift + mantissaBits;
                if (((value >> exponentShift) & 31) == 31)
                    value &= ~(1u << exponentShift);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(data.AsSpan(i * 4), value);
        }

        return data;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>An uncompressed DXGI format: its value, the bytes of a pixel, the samples, and how the library presents it.</summary>
    private sealed record Uncompressed(string Name, uint Dxgi, int BytesPerPixel, string Samples, string PixelFormat, string ColorModel, int Bits, string Channels, string Description);

    private static void BuildUncompressed(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildUncompressed);
        Uncompressed[] formats =
        [
            new("r8g8b8a8-unorm", 28, 4, "random", "Rgba32", "Rgba", 8, "rgba", "R8G8B8A8_UNORM: 8-bit red, green, blue and alpha."),
            new("r8g8b8a8-unorm-srgb", 29, 4, "random", "Rgba32", "Rgba", 8, "rgba", "R8G8B8A8_UNORM_SRGB: sRGB-encoded colors, labeled with the CICP description of sRGB (never converted)."),
            new("r8g8b8a8-typeless", 27, 4, "random", "Rgba32", "Rgba", 8, "rgba", "R8G8B8A8_TYPELESS, read as R8G8B8A8_UNORM."),
            new("b8g8r8a8-unorm", 87, 4, "random", "Rgba32", "Rgba", 8, "rgba", "B8G8R8A8_UNORM: blue, green, red and alpha bytes."),
            new("b8g8r8a8-unorm-srgb", 91, 4, "random", "Rgba32", "Rgba", 8, "rgba", "B8G8R8A8_UNORM_SRGB."),
            new("b8g8r8x8-unorm", 88, 4, "random", "Rgb24", "Rgb", 8, "rgb", "B8G8R8X8_UNORM: the fourth byte is not alpha (the image is opaque)."),
            new("b5g6r5-unorm", 85, 2, "random", "Rgb24", "Rgb", 8, "rgb", "B5G6R5_UNORM: 5-bit red and blue, 6-bit green, expanded to round(v × 255 / max)."),
            new("b5g5r5a1-unorm", 86, 2, "random", "Rgba32", "Rgba", 8, "rgba", "B5G5R5A1_UNORM: 5-bit colors and a 1-bit alpha."),
            new("b4g4r4a4-unorm", 115, 2, "random", "Rgba32", "Rgba", 8, "rgba", "B4G4R4A4_UNORM: 4-bit channels (multiples of 17)."),
            new("r10g10b10a2-unorm", 24, 4, "random", "Rgba64", "Rgba", 16, "rgba", "R10G10B10A2_UNORM: 10-bit colors and a 2-bit alpha, expanded to 16 bits (round(v × 65535 / max))."),
            new("r16g16b16a16-unorm", 11, 8, "random", "Rgba64", "Rgba", 16, "rgba", "R16G16B16A16_UNORM."),
            new("r16g16-unorm", 35, 4, "random", "Rgb48", "Rgb", 16, "rg", "R16G16_UNORM: red and green, a blue of 0."),
            new("r8g8-unorm", 49, 2, "random", "Rgb24", "Rgb", 8, "rg", "R8G8_UNORM: red and green, a blue of 0."),
            new("r8-unorm", 61, 1, "random", "Gray8", "Grayscale", 8, "r", "R8_UNORM: one channel, presented as gray."),
            new("r16-unorm", 56, 2, "random", "Gray16", "Grayscale", 16, "r", "R16_UNORM: one channel, presented as gray."),
            new("a8-unorm", 65, 1, "random", "A8", "GrayscaleAlpha", 8, "a", "A8_UNORM: an alpha mask (black)."),
            new("r16g16b16a16-float", 10, 8, "halves", "RgbaHalf", "Rgba", 16, "rgba", "R16G16B16A16_FLOAT: half floats, values outside [0, 1] included."),
            new("r16g16-float", 34, 4, "halves2", "RgbaHalf", "Rgb", 16, "rg", "R16G16_FLOAT: red and green half floats, a blue of 0."),
            new("r16-float", 54, 2, "halves1", "GrayHalf", "Grayscale", 16, "r", "R16_FLOAT: one half float, presented as gray."),
            new("r32g32b32a32-float", 2, 16, "floats", "RgbaFloat", "Rgba", 32, "rgba", "R32G32B32A32_FLOAT."),
            new("r32g32b32-float", 6, 12, "floats3", "RgbaFloat", "Rgb", 32, "rgb", "R32G32B32_FLOAT: an alpha of 1."),
            new("r32g32-float", 16, 8, "floats2", "RgbaFloat", "Rgb", 32, "rg", "R32G32_FLOAT: red and green, a blue of 0."),
            new("r32-float", 41, 4, "floats1", "GrayFloat", "Grayscale", 32, "r", "R32_FLOAT: one float, presented as gray."),
            new("r11g11b10-float", 26, 4, "small", "RgbaHalf", "Rgb", 16, "rgb", "R11G11B10_FLOAT: unsigned floats of 11, 11 and 10 bits (the exponent of a half), exact in halves."),
            new("r9g9b9e5-sharedexp", 67, 4, "random", "RgbaHalf", "Rgb", 16, "rgb", "R9G9B9E5_SHAREDEXP: three 9-bit mantissas and a shared exponent, every value exact in a half."),
        ];

        foreach (var (format, index) in formats.Select((f, i) => (f, i)))
        {
            const int Width = 7;
            const int Height = 5;
            var seed = 100 + (uint)index;
            var data = format.Samples switch
            {
                "random" => RandomBytes(Width * Height * format.BytesPerPixel, seed),
                "halves" => Halves(Width, Height, 4, index),
                "halves2" => Halves(Width, Height, 2, index),
                "halves1" => Halves(Width, Height, 1, index),
                "floats" => Floats(Width, Height, 4, index),
                "floats3" => Floats(Width, Height, 3, index),
                "floats2" => Floats(Width, Height, 2, index),
                "floats1" => Floats(Width, Height, 1, index),
                "small" => SmallFloats(Width, Height, seed),
                _ => throw new InvalidOperationException(format.Samples),
            };

            var builder = DdsFileBuilder.Dx10(Width, Height, format.Dxgi, data);
            builder.Flags |= DdsFileBuilder.FlagPitch;
            builder.PitchOrLinearSize = (uint)(Width * format.BytesPerPixel);
            AddHandComputed(c, format.Name, Function, builder.Build(), format.PixelFormat, format.ColorModel, format.Bits, format.Channels,
                $"A {Width}x{Height} surface of {format.Description} The stored samples are {(format.Samples == "random" ? "random bytes" : "a pattern")}.",
                ["dds.header=dx10", "dds.format=" + format.Name]);
        }

        // Alpha modes of the DX10 header: straight, opaque and custom keep the stored alpha
        foreach (var (mode, name) in new[] { (1u, "straight"), (3u, "opaque"), (4u, "custom") })
        {
            var data = RandomBytes(5 * 4 * 4, 140 + mode);
            if (mode == 3)
            {
                for (var i = 3; i < data.Length; i += 4)
                    data[i] = 255;
            }

            var builder = DdsFileBuilder.Dx10(5, 4, 28, data);
            builder.MiscFlags2 = mode;
            AddHandComputed(c, "alpha-mode-" + name, Function, builder.Build(), "Rgba32", "Rgba", 8, "rgba",
                $"An R8G8B8A8_UNORM surface whose DX10 header declares the alpha mode {name} (DDS_ALPHA_MODE {mode}): the stored alpha is kept.",
                ["dds.header=dx10", "dds.format=r8g8b8a8-unorm", "dds.alphaMode=" + name]);
        }

        // A 1D texture
        var line = DdsFileBuilder.Dx10(11, 1, 28, RandomBytes(11 * 4, 150));
        line.ResourceDimension = 2;
        AddHandComputed(c, "texture-1d", Function, line.Build(), "Rgba32", "Rgba", 8, "rgba", "A 1D texture of 11 texels (resourceDimension 2, height 1).", ["dds.header=dx10", "dds.format=r8g8b8a8-unorm", "dds.dimension=1d"]);
    }

    /// <summary>A legacy pixel format: its DDS_PIXELFORMAT flags, bit count and masks.</summary>
    private sealed record Legacy(string Name, uint Flags, uint Bits, uint Red, uint Green, uint Blue, uint Alpha, string PixelFormat, string ColorModel, int Bpc, string Channels, string? TexconvNote = null);

    private const string R3G3B2Note = "DirectXTex reads the 3-bit and 2-bit channels of the 3:3:2 layouts through a wider format, replicating their bits (the 3-bit 4 becomes the 5-bit 18, 18/31 instead of 4/7), so its values are not the fractions of the masks the DDS reference defines.";

    private const string LuminanceAlphaNote = "DirectXTex reads the luminance with alpha of a legacy header as a two-channel format (the luminance in red, the alpha in green): its decoding is opaque, the alpha in its green channel. Its red channel is the luminance of the reference.";

    private static void BuildLegacy(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildLegacy);
        const uint Rgb = DdsFileBuilder.PixelRgb;
        const uint Rgba = DdsFileBuilder.PixelRgb | DdsFileBuilder.PixelAlphaPixels;
        const uint L = DdsFileBuilder.PixelLuminance;
        const uint La = DdsFileBuilder.PixelLuminance | DdsFileBuilder.PixelAlphaPixels;
        Legacy[] formats =
        [
            new("legacy-a8r8g8b8", Rgba, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, "Rgba32", "Rgba", 8, "rgba"),
            new("legacy-x8r8g8b8", Rgb, 32, 0xFF0000, 0xFF00, 0xFF, 0, "Rgb24", "Rgb", 8, "rgb"),
            new("legacy-a8b8g8r8", Rgba, 32, 0xFF, 0xFF00, 0xFF0000, 0xFF000000, "Rgba32", "Rgba", 8, "rgba"),
            new("legacy-r8g8b8", Rgb, 24, 0xFF0000, 0xFF00, 0xFF, 0, "Rgb24", "Rgb", 8, "rgb"),
            new("legacy-r5g6b5", Rgb, 16, 0xF800, 0x7E0, 0x1F, 0, "Rgb24", "Rgb", 8, "rgb"),
            new("legacy-x1r5g5b5", Rgb, 16, 0x7C00, 0x3E0, 0x1F, 0, "Rgb24", "Rgb", 8, "rgb"),
            new("legacy-a1r5g5b5", Rgba, 16, 0x7C00, 0x3E0, 0x1F, 0x8000, "Rgba32", "Rgba", 8, "rgba"),
            new("legacy-a4r4g4b4", Rgba, 16, 0xF00, 0xF0, 0xF, 0xF000, "Rgba32", "Rgba", 8, "rgba"),
            new("legacy-x4r4g4b4", Rgb, 16, 0xF00, 0xF0, 0xF, 0, "Rgb24", "Rgb", 8, "rgb"),
            new("legacy-a8r3g3b2", Rgba, 16, 0xE0, 0x1C, 0x3, 0xFF00, "Rgba32", "Rgba", 8, "rgba", R3G3B2Note),
            new("legacy-r3g3b2", Rgb, 8, 0xE0, 0x1C, 0x3, 0, "Rgb24", "Rgb", 8, "rgb", R3G3B2Note),
            new("legacy-a2b10g10r10", Rgba, 32, 0x3FF, 0xFFC00, 0x3FF00000, 0xC0000000, "Rgba64", "Rgba", 16, "rgba",
                "DirectXTex reads the legacy masks of the 10:10:10:2 layouts with red and blue swapped (D3DX wrote them backwards); the library reads the masks as they are written, so texconv gives the red of the file as blue and its blue as red."),
            new("legacy-a2r10g10b10", Rgba, 32, 0x3FF00000, 0xFFC00, 0x3FF, 0xC0000000, "Rgba64", "Rgba", 16, "rgba",
                "DirectXTex reads the legacy masks of the 10:10:10:2 layouts with red and blue swapped (D3DX wrote them backwards); the library reads the masks as they are written, so texconv gives the red of the file as blue and its blue as red."),
            new("legacy-g16r16", Rgb, 32, 0xFFFF, 0xFFFF0000, 0, 0, "Rgb48", "Rgb", 16, "rg"),
            new("legacy-l8", L, 8, 0xFF, 0, 0, 0, "Gray8", "Grayscale", 8, "r"),
            new("legacy-l16", L, 16, 0xFFFF, 0, 0, 0, "Gray16", "Grayscale", 16, "r"),
            new("legacy-a8l8", La, 16, 0xFF, 0, 0, 0xFF00, "La16", "GrayscaleAlpha", 8, "ra", LuminanceAlphaNote),
            new("legacy-a4l4", La, 8, 0xF, 0, 0, 0xF0, "La16", "GrayscaleAlpha", 8, "ra", LuminanceAlphaNote),
            new("legacy-a8", DdsFileBuilder.PixelAlpha, 8, 0, 0, 0, 0xFF, "A8", "GrayscaleAlpha", 8, "a"),
        ];

        foreach (var (format, index) in formats.Select((f, i) => (f, i)))
        {
            const int Width = 6;
            const int Height = 5;
            var bytes = (int)format.Bits / 8;
            var data = RandomBytes(Width * Height * bytes, 200 + (uint)index);
            var builder = DdsFileBuilder.Masks(Width, Height, format.Flags, format.Bits, format.Red, format.Green, format.Blue, format.Alpha, data);
            builder.Flags |= DdsFileBuilder.FlagPitch;
            builder.PitchOrLinearSize = (uint)(Width * bytes);
            AddHandComputed(c, format.Name, Function, builder.Build(), format.PixelFormat, format.ColorModel, format.Bpc, format.Channels,
                string.Create(CultureInfo.InvariantCulture, $"A {Width}x{Height} surface of a legacy pixel format of {format.Bits}-bit pixels (flags 0x{format.Flags:X}, masks 0x{format.Red:X}, 0x{format.Green:X}, 0x{format.Blue:X}, 0x{format.Alpha:X}), random bytes; each mask is a fraction of its maximum."),
                ["dds.header=legacy", "dds.format=" + format.Name], texconvNote: format.TexconvNote);
        }

        // The numeric FourCC codes of the D3DFMT floating-point formats (and A16B16G16R16)
        foreach (var (code, name, channels, isHalf, pixelFormat, colorModel, bpc, compared) in new[]
        {
            (36u, "fourcc-a16b16g16r16", 4, false, "Rgba64", "Rgba", 16, "rgba"),
            (111u, "fourcc-r16f", 1, true, "GrayHalf", "Grayscale", 16, "r"),
            (112u, "fourcc-g16r16f", 2, true, "RgbaHalf", "Rgb", 16, "rg"),
            (113u, "fourcc-a16b16g16r16f", 4, true, "RgbaHalf", "Rgba", 16, "rgba"),
            (114u, "fourcc-r32f", 1, false, "GrayFloat", "Grayscale", 32, "r"),
            (115u, "fourcc-g32r32f", 2, false, "RgbaFloat", "Rgb", 32, "rg"),
            (116u, "fourcc-a32b32g32r32f", 4, false, "RgbaFloat", "Rgba", 32, "rgba"),
        })
        {
            var data = code == 36 ? RandomBytes(5 * 3 * 8, 260) : isHalf ? Halves(5, 3, channels, (int)code) : Floats(5, 3, channels, (int)code);
            AddHandComputed(c, name, Function, DdsFileBuilder.Legacy(5, 3, code, data).Build(), pixelFormat, colorModel, bpc, compared,
                string.Create(CultureInfo.InvariantCulture, $"A 5x3 surface whose FourCC is the D3DFORMAT code {code}."), ["dds.header=legacy", "dds.format=" + name]);
        }
    }

    /// <summary>A block-compressed format of random blocks: the header, and how the blocks are made.</summary>
    private sealed record BlockFormat(string Name, string Reference, uint? Dxgi, string? FourCC, int BlockLength, string PixelFormat, string ColorModel, int Bpc, string Channels, bool Srgb = false);

    private static readonly BlockFormat[] BlockFormats =
    [
        new("dxt1", "BC1", null, "DXT1", 8, "Rgba32", "Rgba", 8, "rgba"),
        new("dxt3", "BC2", null, "DXT3", 16, "Rgba32", "Rgba", 8, "rgba"),
        new("dxt5", "BC3", null, "DXT5", 16, "Rgba32", "Rgba", 8, "rgba"),
        new("ati1", "BC4", null, "ATI1", 8, "Gray8", "Grayscale", 8, "r"),
        new("bc4u", "BC4", null, "BC4U", 8, "Gray8", "Grayscale", 8, "r"),
        new("bc4s", "BC4S", null, "BC4S", 8, "GrayFloat", "Grayscale", 32, "r"),
        new("ati2", "BC5", null, "ATI2", 16, "Rgb24", "Rgb", 8, "rg"),
        new("bc5u", "BC5", null, "BC5U", 16, "Rgb24", "Rgb", 8, "rg"),
        new("bc5s", "BC5S", null, "BC5S", 16, "RgbaFloat", "Rgb", 32, "rg"),
        new("bc1-unorm", "BC1", 71, null, 8, "Rgba32", "Rgba", 8, "rgba"),
        new("bc1-unorm-srgb", "BC1", 72, null, 8, "Rgba32", "Rgba", 8, "rgba", Srgb: true),
        new("bc1-typeless", "BC1", 70, null, 8, "Rgba32", "Rgba", 8, "rgba"),
        new("bc2-unorm", "BC2", 74, null, 16, "Rgba32", "Rgba", 8, "rgba"),
        new("bc3-unorm-srgb", "BC3", 78, null, 16, "Rgba32", "Rgba", 8, "rgba", Srgb: true),
        new("bc4-unorm", "BC4", 80, null, 8, "Gray8", "Grayscale", 8, "r"),
        new("bc4-snorm", "BC4S", 81, null, 8, "GrayFloat", "Grayscale", 32, "r"),
        new("bc5-unorm", "BC5", 83, null, 16, "Rgb24", "Rgb", 8, "rg"),
        new("bc5-snorm", "BC5S", 84, null, 16, "RgbaFloat", "Rgb", 32, "rg"),
        new("bc6h-uf16", "BC6H", 95, null, 16, "RgbaHalf", "Rgb", 16, "rgb"),
        new("bc6h-sf16", "BC6HS", 96, null, 16, "RgbaHalf", "Rgb", 16, "rgb"),
        new("bc7-unorm", "BC7", 98, null, 16, "Rgba32", "Rgba", 8, "rgba"),
        new("bc7-unorm-srgb", "BC7", 99, null, 16, "Rgba32", "Rgba", 8, "rgba", Srgb: true),
    ];

    /// <summary>Random blocks, whose mode bits cycle through every BC7 mode (and the reserved byte 0) or every BC6H mode (and a reserved one).</summary>
    private static byte[] RandomBlocks(BlockFormat format, int width, int height, uint seed)
    {
        var count = ((width + 3) / 4) * ((height + 3) / 4);
        var data = RandomBytes(count * format.BlockLength, seed);
        byte[] bc6Modes = [0x00, 0x01, 0x02, 0x06, 0x0A, 0x0E, 0x12, 0x16, 0x1A, 0x1E, 0x03, 0x07, 0x0B, 0x0F, 0x13];
        for (var i = 0; i < count; i++)
        {
            var offset = i * format.BlockLength;
            if (format.Reference == "BC7")
            {
                var mode = i % 9;
                data[offset] = mode == 8 ? (byte)0 : (byte)(((data[offset] << (mode + 1)) & 0xFF) | (1 << mode));
            }
            else if (format.Reference is "BC6H" or "BC6HS")
            {
                var mode = bc6Modes[i % bc6Modes.Length];
                data[offset] = mode < 2 ? (byte)((data[offset] & ~3) | mode) : (byte)((data[offset] & ~31) | mode);
            }
        }

        return data;
    }

    private static DdsFileBuilder BlockFile(BlockFormat format, int width, int height, byte[] data)
    {
        var builder = format.Dxgi is { } dxgi ? DdsFileBuilder.Dx10((uint)width, (uint)height, dxgi, data) : DdsFileBuilder.Legacy((uint)width, (uint)height, DdsFileBuilder.FourCC(format.FourCC!), data);
        builder.Flags |= DdsFileBuilder.FlagLinearSize;
        builder.PitchOrLinearSize = (uint)data.Length;
        return builder;
    }

    private static void BuildRandomBlocks(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildRandomBlocks);
        foreach (var (format, index) in BlockFormats.Select((f, i) => (f, i)))
        {
            // 36 blocks: every BC7 mode 4 times, every BC6H mode at least twice
            const int Width = 24;
            const int Height = 24;
            var data = RandomBlocks(format, Width, Height, 300 + (uint)index);
            AddBlocks(c, "random-" + format.Name, Function, BlockFile(format, Width, Height, data).Build(), format,
                $"A {Width}x{Height} surface of random {format.Name.ToUpperInvariant()} blocks{(format.Reference is "BC7" ? " whose mode bits cycle through the eight modes and the reserved low byte 0" : format.Reference is "BC6H" or "BC6HS" ? " whose mode bits cycle through the 14 modes and the reserved mode 0x13" : "")}.",
                ["dds.blocks=random"]);
        }

        // Sizes that are not multiples of 4: the texels of the last blocks outside the surface are not part of the image
        foreach (var (name, width, height, formatName) in new[] { ("odd-size-bc1", 13, 9, "bc1-unorm"), ("odd-size-bc7", 5, 3, "bc7-unorm"), ("odd-size-bc3", 2, 7, "dxt5"), ("one-pixel-bc1", 1, 1, "dxt1"), ("odd-size-bc6h", 9, 6, "bc6h-uf16"), ("odd-size-bc5", 7, 2, "bc5u") })
        {
            var format = BlockFormats.Single(f => f.Name == formatName);
            var data = RandomBlocks(format, width, height, (uint)(400 + width + height));
            AddBlocks(c, name, Function, BlockFile(format, width, height, data).Build(), format,
                $"A {width}x{height} surface of random {format.Name.ToUpperInvariant()} blocks: the texels of the blocks outside the surface are not part of the image.",
                ["dds.blocks=random", "dds.oddSize"]);
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Files written by DirectXTex and ImageMagick
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>An RGBA pattern of 8-bit samples: gradients, alpha from 0 to 255, and a few hard edges.</summary>
    private static byte[] PatternRgba(int width, int height, int seed)
    {
        var data = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                data[offset] = (byte)((x * 255 / Math.Max(1, width - 1)) ^ (seed * 37));
                data[offset + 1] = (byte)(y * 255 / Math.Max(1, height - 1));
                data[offset + 2] = (byte)(((x + y + seed) % 4) * 85);
                data[offset + 3] = (byte)(((x / 3) + (y / 3) + seed) % 3 == 0 ? 255 : (x * 41 + y * 23 + seed) % 256);
            }
        }

        return data;
    }

    /// <summary>An R32G32B32A32_FLOAT file of a pattern of high dynamic range, the input of the BC6H encoder.</summary>
    private static byte[] PatternFloatFile(int width, int height, int seed, bool signed)
    {
        var data = new byte[width * height * 16];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var ch = 0; ch < 4; ch++)
                {
                    var value = ch == 3 ? 1 : Math.Pow(2, (((x * 3) + (y * 5) + (ch * 7) + seed) % 17) - 8) * (signed && (x + ch) % 3 == 0 ? -1 : 1);
                    BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan((((y * width) + x) * 4 + ch) * 4), (float)value);
                }
            }
        }

        return DdsFileBuilder.Dx10((uint)width, (uint)height, 2, data).Build();
    }

    private static void BuildTexconv(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildTexconv);
        var rgba = DdsFileBuilder.Dx10(20, 12, 28, PatternRgba(20, 12, 1)).Build();
        foreach (var (target, formatName, dx10) in new[]
        {
            ("BC1_UNORM", "dxt1", false), ("BC2_UNORM", "dxt3", false), ("BC3_UNORM", "dxt5", false), ("BC4_UNORM", "bc4u", false), ("BC5_UNORM", "bc5u", false),
            ("BC4_SNORM", "bc4-snorm", true), ("BC5_SNORM", "bc5-snorm", true), ("BC7_UNORM", "bc7-unorm", true), ("BC7_UNORM_SRGB", "bc7-unorm-srgb", true), ("BC1_UNORM_SRGB", "bc1-unorm-srgb", true),
        })
        {
            var format = BlockFormats.Single(f => f.Name == formatName);
            var (data, command) = c.Tools.Texconv(c, rgba, ["-m", "1", "-f", target, .. dx10 ? new[] { "-dx10" } : []]);
            AddBlocks(c, "texconv-" + target.ToLowerInvariant().Replace('_', '-'), Function, data, format,
                $"A 20x12 RGBA pattern encoded to {target} by texconv (DirectXTex, CPU encoder).", ["dds.writer=texconv"], commands: [command]);
        }

        foreach (var (target, formatName, signed) in new[] { ("BC6H_UF16", "bc6h-uf16", false), ("BC6H_SF16", "bc6h-sf16", true) })
        {
            var format = BlockFormats.Single(f => f.Name == formatName);
            var (data, command) = c.Tools.Texconv(c, PatternFloatFile(12, 8, 3, signed), ["-m", "1", "-f", target]);
            AddBlocks(c, "texconv-" + target.ToLowerInvariant().Replace('_', '-'), Function, data, format,
                $"A 12x8 pattern of high dynamic range (powers of two from 2^-8 to 2^8{(signed ? ", some negative" : "")}) encoded to {target} by texconv.", ["dds.writer=texconv"], commands: [command]);
        }

        // Mipmaps: the full chain of a size that is not a power of two, and of a square
        foreach (var (name, width, height, target, formatName) in new[] { ("texconv-mips-bc1", 13, 9, "BC1_UNORM", "dxt1"), ("texconv-mips-bc7", 16, 16, "BC7_UNORM", "bc7-unorm"), ("texconv-mips-rgba", 10, 6, "R8G8B8A8_UNORM", null) })
        {
            var source = DdsFileBuilder.Dx10((uint)width, (uint)height, 28, PatternRgba(width, height, 2)).Build();
            var (data, command) = c.Tools.Texconv(c, source, ["-m", "0", "-f", target]);
            var description = $"A {width}x{height} RGBA pattern with its full mipmap chain, generated and encoded to {target} by texconv.";
            if (formatName is null)
            {
                AddHandComputed(c, name, Function, data, "Rgba32", "Rgba", 8, "rgba", description, ["dds.writer=texconv", "dds.mips"], commands: [command]);
            }
            else
            {
                AddBlocks(c, name, Function, data, BlockFormats.Single(f => f.Name == formatName), description, ["dds.writer=texconv", "dds.mips"], commands: [command]);
            }
        }
    }

    private static void BuildStructures(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildStructures);

        // texassemble: a cube map, an array, a volume texture and an array of cube maps of R8G8B8A8 surfaces
        foreach (var (name, kind, count, size, description, features) in new[]
        {
            ("texassemble-cube", "cube", 6, 8, "A cube map of six 8x8 faces", new[] { "dds.cube" }),
            ("texassemble-array", "array", 3, 8, "An array of three 8x8 surfaces", new[] { "dds.array" }),
            ("texassemble-volume", "volume", 4, 8, "A volume texture of four 8x8 depth slices", new[] { "dds.volume" }),
            ("texassemble-cube-array", "cubearray", 12, 4, "An array of two cube maps of 4x4 faces", new[] { "dds.cube", "dds.array" }),
        })
        {
            var inputs = Enumerable.Range(0, count).Select(i => DdsFileBuilder.Dx10((uint)size, (uint)size, 28, PatternRgba(size, size, i + 5)).Build()).ToList();
            var (assembled, assembleCommand) = c.Tools.Texassemble(c, kind, inputs);
            AddHandComputed(c, name, Function, assembled, "Rgba32", "Rgba", 8, "rgba", description + " assembled by texassemble (R8G8B8A8_UNORM, DX10 header).", ["dds.writer=texassemble", .. features], commands: [assembleCommand]);

            // The same structure with its mipmaps, block-compressed
            var target = kind == "volume" ? "R8G8B8A8_UNORM" : "BC1_UNORM";
            var (compressed, command) = c.Tools.Texconv(c, assembled, ["-m", "0", "-f", target]);
            var mipName = name + (kind == "volume" ? "-mips" : "-mips-bc1");
            var mipDescription = description + $" assembled by texassemble, with its full mipmap chain, converted to {target} by texconv.";
            if (kind == "volume")
            {
                AddHandComputed(c, mipName, Function, compressed, "Rgba32", "Rgba", 8, "rgba", mipDescription, ["dds.writer=texconv", "dds.mips", .. features], commands: [assembleCommand, command]);
            }
            else
            {
                AddBlocks(c, mipName, Function, compressed, BlockFormats.Single(f => f.Name == "dxt1"), mipDescription, ["dds.writer=texconv", "dds.mips", .. features], commands: [assembleCommand, command]);
            }
        }

        // A cube map of the legacy header, all faces and partial, and a legacy volume texture with mipmaps
        // (ImageMagick, the cross-check of the partial cube map, misplaces the faces after the first when they have mipmaps)
        foreach (var (name, faces, mips, description) in new[] { ("legacy-cube", 0xFC00u, 3u, "three mipmap levels, all six faces"), ("legacy-cube-partial", 0x400u | 0x1000u | 0x8000u, 1u, "one level, the faces +X, +Y and -Z only (a partial cube map of Direct3D 9)") })
        {
            var faceCount = System.Numerics.BitOperations.PopCount(faces);
            var data = RandomBytes(faceCount * (mips == 3 ? (4 * 4) + (2 * 2) + 1 : 16) * 4, 500 + faces);
            var builder = DdsFileBuilder.Masks(4, 4, DdsFileBuilder.PixelRgb | DdsFileBuilder.PixelAlphaPixels, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, data);
            builder.Caps |= DdsFileBuilder.CapsComplex | (mips > 1 ? DdsFileBuilder.CapsMipMap : 0);
            builder.Caps2 = DdsFileBuilder.Caps2CubeMap | faces;
            builder.Flags |= mips > 1 ? DdsFileBuilder.FlagMipMapCount : 0;
            builder.MipMapCount = mips;
            AddHandComputed(c, name, Function, builder.Build(), "Rgba32", "Rgba", 8, "rgba", $"A legacy cube map (DDSCAPS2_CUBEMAP) of 4x4 faces with {description}, random A8R8G8B8 bytes.",
                ["dds.header=legacy", "dds.cube", .. mips > 1 ? new[] { "dds.mips" } : [], .. faces == 0xFC00u ? Array.Empty<string>() : ["dds.cube.partial"]],
                texconvRejectsNote: faces == 0xFC00u ? null : "texconv rejects partial cube maps (Direct3D 10 and later require the six faces), and OpenImageIO reads them as six faces. ImageMagick gives the top level of each stored face: they are the reference.");
        }

        var volume = DdsFileBuilder.Masks(4, 2, DdsFileBuilder.PixelRgb, 16, 0xF800, 0x7E0, 0x1F, 0, RandomBytes(((4 * 2 * 3) + (2 * 1 * 1) + 1) * 2, 520));
        volume.Caps2 = DdsFileBuilder.Caps2Volume;
        volume.Flags |= DdsFileBuilder.FlagDepth | DdsFileBuilder.FlagMipMapCount;
        volume.Depth = 3;
        volume.MipMapCount = 3;
        AddHandComputed(c, "legacy-volume", Function, volume.Build(), "Rgb24", "Rgb", 8, "rgb", "A legacy volume texture (DDSCAPS2_VOLUME) of 4x2x3 R5G6B5 texels with three mipmap levels (3, 1 and 1 depth slices).",
            ["dds.header=legacy", "dds.volume", "dds.mips"]);

        // A DX10 array of BC7 surfaces with mipmaps, built from random blocks
        var bc7 = BlockFormats.Single(f => f.Name == "bc7-unorm");
        var arrayBlocks = RandomBlocks(bc7, 8, 8, 530).Concat(RandomBlocks(bc7, 4, 4, 531)).Concat(RandomBlocks(bc7, 2, 2, 532)).Concat(RandomBlocks(bc7, 8, 8, 533)).Concat(RandomBlocks(bc7, 4, 4, 534)).Concat(RandomBlocks(bc7, 2, 2, 535)).ToArray();
        var array = DdsFileBuilder.Dx10(8, 8, 98, arrayBlocks);
        array.ArraySize = 2;
        array.MipMapCount = 3;
        array.Flags |= DdsFileBuilder.FlagMipMapCount;
        AddBlocks(c, "array-bc7-mips", Function, array.Build(), bc7, "A DX10 array of two 8x8 BC7 surfaces with three mipmap levels, random blocks.", ["dds.header=dx10", "dds.array", "dds.mips", "dds.blocks=random"]);
    }

    private static void BuildMagick(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildMagick);
        foreach (var (name, compression, width, height, mips, formatName) in new[]
        {
            ("magick-dxt1", "dxt1", 13, 9, 0, "dxt1"), ("magick-dxt5", "dxt5", 13, 9, 0, "dxt5"), ("magick-dxt5-mips", "dxt5", 16, 8, 4, "dxt5"), ("magick-none", "none", 7, 5, 0, null),
        })
        {
            var (data, command) = c.Tools.Magick(c, PatternRgba(width, height, 4), width, height, compression, mips);
            var description = $"A {width}x{height} RGBA pattern written by ImageMagick (dds:compression={compression}{(mips > 0 ? ", " + mips.ToString(CultureInfo.InvariantCulture) + " mipmap levels" : "")}).";
            List<string> features = ["dds.writer=magick", .. mips > 0 ? new[] { "dds.mips" } : []];
            if (formatName is null)
            {
                AddHandComputed(c, name, Function, data, "Rgba32", "Rgba", 8, "rgba", description, features, commands: [command]);
            }
            else
            {
                AddBlocks(c, name, Function, data, BlockFormats.Single(f => f.Name == formatName), description, features, commands: [command]);
            }
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Errors
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildInvalid(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildInvalid);
        DdsFileBuilder Valid() => DdsFileBuilder.Dx10(4, 4, 28, RandomBytes(64, 600));
        DdsFileBuilder LegacyValid() => DdsFileBuilder.Masks(4, 4, DdsFileBuilder.PixelRgb | DdsFileBuilder.PixelAlphaPixels, 32, 0xFF0000, 0xFF00, 0xFF, 0xFF000000, RandomBytes(64, 601));
        var valid = Valid().Build();

        AddError(c, "invalid", "truncated-header", Function, valid[..100], Invalid(), "The file ends in DDS_HEADER.");
        AddError(c, "invalid", "truncated-dx10-header", Function, valid[..140], Invalid(), "The file ends in DDS_HEADER_DXT10.");
        AddError(c, "invalid", "truncated-data", Function, valid[..^5], Invalid(), "The surface is cut.", structure: true);
        AddError(c, "invalid", "truncated-block-data", Function, BlockFile(BlockFormats.Single(f => f.Name == "dxt1"), 8, 8, RandomBytes(32, 602)).Build()[..^3], Invalid(), "The last block of a BC1 surface is cut.", structure: true);

        void Edit(string name, Action<DdsFileBuilder> edit, string notes, bool legacy = false)
        {
            var builder = legacy ? LegacyValid() : Valid();
            edit(builder);
            AddError(c, "invalid", name, Function, builder.Build(), Invalid(), notes);
        }

        Edit("header-size", b => b.Size = 120, "dwSize is 120, not 124.");
        Edit("pixel-format-size", b => b.PixelFormatSize = 24, "ddspf.dwSize is 24, not 32.");
        Edit("width-zero", b => b.Width = 0, "dwWidth is 0.");
        Edit("height-zero", b => b.Height = 0, "dwHeight is 0.");
        Edit("too-many-mips", b => b.MipMapCount = 4, "A 4x4 texture declares 4 mipmap levels; it has at most 3.");
        Edit("dimension-invalid", b => b.ResourceDimension = 5, "The DX10 resource dimension is 5.");
        Edit("array-size-zero", b => b.ArraySize = 0, "The DX10 array size is 0.");
        Edit("volume-array", b => { b.ResourceDimension = 4; b.Depth = 1; b.ArraySize = 2; }, "A DX10 volume texture with an array size of 2.");
        Edit("texture-1d-height", b => b.ResourceDimension = 2, "A DX10 1D texture of height 4.");
        Edit("cube-not-square", b => { b.Width = 4; b.Height = 2; b.MiscFlag = 4; b.ArraySize = 1; }, "A DX10 cube map of 4x2 faces.");
        Edit("alpha-mode-invalid", b => b.MiscFlags2 = 6, "The DX10 alpha mode is 6.");
        Edit("dxgi-unknown", b => b.DxgiFormat = 0, "The DX10 format is DXGI_FORMAT_UNKNOWN.");
        Edit("volume-depth-zero", b => { b.ResourceDimension = 4; b.Depth = 0; }, "A DX10 volume texture of depth 0.");
        Edit("cube-no-face", b => b.Caps2 = DdsFileBuilder.Caps2CubeMap, "A legacy cube map without any face flag.", legacy: true);
        Edit("cube-and-volume", b => { b.Caps2 = DdsFileBuilder.Caps2CubeMap | DdsFileBuilder.Caps2AllFaces | DdsFileBuilder.Caps2Volume; b.Depth = 1; }, "A legacy header that declares a cube map and a volume texture.", legacy: true);
        Edit("mask-not-contiguous", b => b.RedMask = 0xF0F000, "The red mask 0xF0F000 is not a contiguous run of bits.", legacy: true);
        Edit("mask-overlap", b => b.GreenMask = 0x1FF00, "The green mask overlaps the red one.", legacy: true);
        Edit("mask-outside-pixel", b => { b.RgbBitCount = 16; b.Data = RandomBytes(32, 603); }, "The masks of 32-bit pixels in a 16-bit pixel.", legacy: true);
        Edit("bit-count", b => b.RgbBitCount = 12, "A pixel of 12 bits.", legacy: true);
        Edit("no-pixel-format", b => b.PixelFormatFlags = 0, "The pixel format has no DDPF_FOURCC, DDPF_RGB, DDPF_LUMINANCE or DDPF_ALPHA flag.", legacy: true);
        Edit("masks-zero", b => { b.RedMask = 0; b.GreenMask = 0; b.BlueMask = 0; b.AlphaMask = 0; }, "Every mask of the RGB pixel format is 0.", legacy: true);

        // dwPitchOrLinearSize that contradicts the layout: rejected in strict mode, ignored by lenient decoding (as DirectXTex does)
        Edit("pitch-wrong", b => { b.Flags |= DdsFileBuilder.FlagPitch; b.PitchOrLinearSize = 20; }, "DDSD_PITCH with a pitch of 20 for rows of 16 bytes. Lenient decoding ignores the field, as texconv, OpenImageIO and ImageMagick do.");
        Edit("linear-size-wrong", b =>
        {
            b.DxgiFormat = 71;
            b.Data = RandomBytes(8, 604);
            b.Flags |= DdsFileBuilder.FlagLinearSize;
            b.PitchOrLinearSize = 16;
        }, "DDSD_LINEARSIZE of 16 for a BC1 surface of 8 bytes. Lenient decoding ignores the field, as texconv, OpenImageIO and ImageMagick do.");
    }

    private static void BuildUnsupported(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildUnsupported);
        void Legacy(string name, uint fourCC, string feature, string notes) => AddError(c, "unsupported", name, Function, DdsFileBuilder.Legacy(4, 4, fourCC, RandomBytes(16, 610 + fourCC % 7)).Build(), Unsupported(feature), notes);
        Legacy("dxt2", DdsFileBuilder.FourCC("DXT2"), "Premultiplied alpha", "DXT2: BC2 with colors premultiplied by alpha, which the library does not divide out.");
        Legacy("dxt4", DdsFileBuilder.FourCC("DXT4"), "Premultiplied alpha", "DXT4: BC3 with colors premultiplied by alpha.");
        Legacy("yuy2", DdsFileBuilder.FourCC("YUY2"), "Video format", "YUY2: a packed YUV video format.");
        Legacy("fourcc-unknown", DdsFileBuilder.FourCC("ABCD"), "FourCC 'ABCD'", "A FourCC the library does not know.");
        Legacy("fourcc-q16w16v16u16", 110, "Signed normalized format", "D3DFMT_Q16W16V16U16: signed 16-bit samples.");

        void Dxgi(string name, uint format, string notes, uint miscFlags2 = 0, string? feature = null)
        {
            var builder = DdsFileBuilder.Dx10(4, 4, format, RandomBytes(64, 620 + format));
            builder.MiscFlags2 = miscFlags2;
            AddError(c, "unsupported", name, Function, builder.Build(), Unsupported(feature ?? string.Create(CultureInfo.InvariantCulture, $"DXGI format {format}")), notes);
        }

        Dxgi("premultiplied-alpha", 28, "R8G8B8A8_UNORM whose DX10 header declares premultiplied alpha.", miscFlags2: 2, feature: "Premultiplied alpha");
        Dxgi("depth-d24-s8", 45, "D24_UNORM_S8_UINT: a depth and stencil format.");
        Dxgi("video-nv12", 103, "NV12: a planar video format.");
        Dxgi("typeless-r32g32b32a32", 1, "R32G32B32A32_TYPELESS: floats or integers, nothing says which.");
        Dxgi("bc6h-typeless", 94, "BC6H_TYPELESS: unsigned or signed, nothing says which.");
        Dxgi("uint-r8g8b8a8", 30, "R8G8B8A8_UINT: integers, not normalized samples.");

        AddError(c, "unsupported", "legacy-yuv", Function, DdsFileBuilder.Masks(4, 4, DdsFileBuilder.PixelYuv, 32, 0xFF, 0xFF00, 0xFF0000, 0, RandomBytes(64, 630)).Build(), Unsupported("YUV pixel format"), "A legacy pixel format of DDPF_YUV samples.");
    }

    private static void BuildLimits(Corpus<DdsTools> c)
    {
        const string Function = nameof(BuildLimits);
        var data = BlockFile(BlockFormats.Single(f => f.Name == "dxt1"), 16, 8, RandomBytes(64, 640)).Build();
        AddError(c, "limit", "frame-pixels-over-limit", Function, data, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" }, "The surface has 128 pixels.",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 127 } });
        AddError(c, "limit", "width-over-limit", Function, data, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "Width" }, "The surface is 16 pixels wide.",
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxWidth"] = 15 } });
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Entries
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The pixels of every surface of a texture, as real numbers, four per pixel.</summary>
    private sealed record Surfaces(DdsTexture Texture, IReadOnlyList<double[]> Pixels);

    /// <summary>Records a fixture of an uncompressed format, whose reference <see cref="ReferenceDds"/> computes, cross-checked with texconv.</summary>
    private static void AddHandComputed(Corpus<DdsTools> c, string name, string function, byte[] data, string pixelFormat, string colorModel, int bits, string channels, string description, IEnumerable<string> features, string? texconvNote = null, IList<string>? commands = null, string? texconvRejectsNote = null)
    {
        var texture = ReferenceDds.Read(data);
        var pixels = texture.Surfaces.Select(surface => ReferenceDds.ReadPixels(data, texture, surface)).ToList();
        var reference = new Surfaces(texture, pixels);
        var checks = new List<object?>();
        if (texconvRejectsNote is null)
        {
            checks.Add(TexconvCrossCheck(c, data, reference, channels, bits, texconvNote));
        }
        else
        {
            // texconv does not read the file: ImageMagick is the cross-check, and texconv must still reject it
            Py.Assert(TexconvRejects(c, data), name + ": texconv now reads the file");
            checks.Add(MagickCrossCheck(c, data, reference, texconvRejectsNote));
        }

        AddValid(c, name, function, data, reference, pixelFormat, colorModel, bits, features, commands,
            new Obj
            {
                ["method"] = "hand-computed",
                ["description"] = description + " Each sample is the fraction of its maximum (or the floating-point value it stores), as ReferenceDds (test harness, an independent reader of the DDS reference of Microsoft Learn) reads it; one channel is presented as gray, two as red and green with a blue of 0, a missing alpha is 1.",
                ["crossChecks"] = checks,
            });
    }

    /// <summary>Records a fixture of a block-compressed format.</summary>
    private static void AddBlocks(Corpus<DdsTools> c, string name, string function, byte[] data, BlockFormat format, string description, IEnumerable<string> features, IList<string>? commands = null)
    {
        var texture = ReferenceDds.Read(data);
        List<string> allFeatures = [.. features, "dds.format=" + format.Reference.ToLowerInvariant(), "dds.header=" + (texture.HeaderLength == 148 ? "dx10" : "legacy")];
        if (format.Reference is "BC6H" or "BC6HS" or "BC7")
        {
            // DirectXTex is the reference: it decodes these formats exactly (integers and half-float bit patterns)
            var (decoded, command) = TexconvDecode(c, data, texture, format.Reference == "BC7" ? (format.Srgb ? "R8G8B8A8_UNORM_SRGB" : "R8G8B8A8_UNORM") : "R16G16B16A16_FLOAT");
            var decodedTexture = ReferenceDds.Read(decoded);
            var pixels = decodedTexture.Surfaces.Select(surface => ReferenceDds.ReadPixels(decoded, decodedTexture, surface)).ToList();
            if (format.Reference != "BC7")
            {
                // BC6H has no alpha: the alpha of texconv is 1 (DirectXTex writes it so), as the library presents it
                foreach (var surface in pixels)
                {
                    for (var i = 3; i < surface.Length; i += 4)
                        Py.Assert(surface[i] == 1, name + ": texconv gives an alpha of 1");
                }
            }

            var reference = new Surfaces(texture, pixels);
            AddValid(c, name, function, data, reference, format.PixelFormat, format.ColorModel, format.Bpc, allFeatures, commands,
                new Obj
                {
                    ["method"] = "decoded",
                    ["description"] = description + (format.Reference == "BC7"
                        ? " The 8-bit samples DirectXTex decodes: BC7 is defined in integers (Equation 2 of the Khronos Data Format Specification), so every decoder that follows it gives these bytes."
                        : " The half floats DirectXTex decodes: BC6H is defined in half-float bit patterns, so every decoder that follows it gives these values; the alpha is 1."),
                    ["decoder"] = $"texconv (DirectXTex {c.Tools.DirectXTexVersion})",
                    ["backend"] = "DirectXTex BC6H and BC7 software decoders (texconv converts on the CPU; the GPU encoders and decoders are not built)",
                    ["command"] = command,
                    ["crossChecks"] = new List<object?> { OiioCrossCheck(c, data, reference, format) },
                });
            return;
        }

        var computed = new List<double[]>();
        var ties = 0;
        foreach (var surface in texture.Surfaces)
        {
            var blocks = data.AsSpan((int)surface.Offset, (int)surface.Length);
            computed.Add(ReferenceS3tc.Decode(blocks, surface.Width, surface.Height, format.Reference));
            ties += ReferenceS3tc.CountTies(blocks, surface.Width, surface.Height, format.Reference);
        }

        var handComputed = new Surfaces(texture, computed);
        string? note = null;
        if (ties > 0)
        {
            note = string.Create(CultureInfo.InvariantCulture, $"DirectXTex computes the midpoint of the three-color mode of BC1 in single precision: its {ties} samples that are exactly halfway between two 8-bit values (127.5, for example) come out one way or the other depending on the endpoints, where the specification gives the exact half, rounded up here. Every other sample is the same.");
        }
        else if (format.Reference == "BC5S")
        {
            note = "DirectXTex gives the signed BC5 values multiplied by 1/127 in single precision, which differs by 1 ulp from the nearest float to k/127 for some k; the multiples of 1/127 are the same.";
        }

        AddValid(c, name, function, data, handComputed, format.PixelFormat, format.ColorModel, format.Bpc, allFeatures, commands,
            new Obj
            {
                ["method"] = "hand-computed",
                ["description"] = description + (format.Reference is "BC4S" or "BC5S"
                    ? " ReferenceS3tc (test harness) computes the values of the Khronos Data Format Specification (section 19) and rounds them to the nearest multiple of 1/127, the precision of the format; the float is the one nearest to k/127."
                    : " ReferenceS3tc (test harness) computes the values of the Khronos Data Format Specification (sections 18 and 19) as fractions and rounds them once to 8 bits, halves up."),
                ["crossChecks"] = new List<object?> { TexconvCrossCheck(c, data, handComputed, format.Channels, format.Reference is "BC4S" or "BC5S" ? 127 : 8, note, format.Srgb) },
            });
    }

    private static void AddValid(Corpus<DdsTools> c, string name, string function, byte[] data, Surfaces reference, string pixelFormat, string colorModel, int bits, IEnumerable<string> features, IList<string>? commands, Obj referenceEntry)
    {
        var fixtureId = "dds/" + name;
        var texture = reference.Texture;
        // The layout of the snapshots of the pixel format (ImageSnapshots.GetLayout)
        var layout = pixelFormat switch
        {
            "RgbaHalf" or "RgbaFloat" or "GrayHalf" or "GrayFloat" => "rgbaf32le",
            "Rgb24" => "rgb8",
            "Gray8" => "gray8",
            "Gray16" => "gray16le",
            "Rgba64" or "Rgb48" or "La32" => "rgba16le",
            _ => "rgba8",
        };
        var (isSrgb, isLinear) = ReferenceDds.ReadColorLabels(data);
        var top = texture.Surfaces[0];
        var expected = new Obj
        {
            ["width"] = top.Width,
            ["height"] = top.Height,
            ["pixelFormat"] = pixelFormat,
            ["colorModel"] = colorModel,
            ["bitsPerComponent"] = bits == 127 ? 32 : bits,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
        };
        if (isSrgb)
        {
            expected["cicp"] = new Obj { ["colorPrimaries"] = 1, ["transferCharacteristics"] = 13, ["matrixCoefficients"] = 0, ["fullRange"] = true };
        }
        else if (isLinear)
        {
            expected["transferFunction"] = "linear";
        }

        expected["animation"] = null;
        expected["resolution"] = null;
        expected["profiles"] = new Obj { ["icc"] = null, ["exif"] = null, ["xmp"] = null };
        expected["text"] = new List<object?>();
        expected["frameCount"] = 1;
        // The layout of the snapshots, then the canonical RGBA layout of the suites when it is another one
        var canonical = layout switch { "rgb8" or "gray8" => "rgba8", "gray16le" => "rgba16le", _ => null };
        var buffers = new List<object?> { Buffer(c, fixtureId + ".frame-0", layout, top.Width, top.Height, reference.Pixels[0]) };
        if (canonical is not null)
            buffers.Add(Buffer(c, fixtureId + ".frame-0", canonical, top.Width, top.Height, reference.Pixels[0]));

        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = buffers } };
        expected["poster"] = null;

        var input = c.Write(fixtureId + ".dds", data);
        input["role"] = "encoded";
        var allFeatures = new SortedSet<string>(features, StringComparer.Ordinal);
        if (texture.Surfaces.Count > 1)
            allFeatures.Add("dds.trailingSurfaces");

        var provenance = c.Provenance("generated", function, null, [$"DirectXTex {c.Tools.DirectXTexVersion} (texconv, texassemble)", $"OpenImageIO {c.Tools.OpenImageIoVersion} (oiiotool, cross-check)", $"ImageMagick {c.Tools.MagickVersion}", ToolSet.RuntimeLabel], commands);
        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "dds",
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = allFeatures.ToList(),
            ["reference"] = referenceEntry,
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        };

        if (texture.Surfaces.Count > 1)
        {
            fixture["ddsSurfaces"] = texture.Surfaces.Select((surface, i) => (object?)new Obj
            {
                ["arrayIndex"] = surface.ArrayIndex,
                ["face"] = surface.Face < 0 ? null : FaceNames[surface.Face],
                ["mipLevel"] = surface.MipLevel,
                ["depthSlice"] = surface.DepthSlice,
                ["width"] = surface.Width,
                ["height"] = surface.Height,
                ["buffer"] = Buffer(c, string.Create(CultureInfo.InvariantCulture, $"{fixtureId}.surface-{i}"), layout, surface.Width, surface.Height, reference.Pixels[i]),
            }).ToList();
        }

        c.Fixtures.Add(fixture);
    }

    private static Obj Buffer(Corpus<DdsTools> c, string stem, string layout, int width, int height, double[] pixels)
    {
        var (channels, bytesPerSample) = layout switch { "rgba8" => (4, 1), "rgb8" => (3, 1), "gray8" => (1, 1), "rgba16le" => (4, 2), "gray16le" => (1, 2), _ => (4, 4) };
        var data = new byte[width * height * channels * bytesPerSample];
        for (var p = 0; p < width * height; p++)
        {
            for (var ch = 0; ch < channels; ch++)
            {
                var value = pixels[(p * 4) + ch];
                var i = (p * channels) + ch;
                switch (bytesPerSample)
                {
                    case 1:
                        data[i] = (byte)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
                        Py.Assert(Math.Abs((data[i] / 255.0) - value) < (0.5 / 255) + 1e-9, stem + ": an 8-bit sample");
                        break;
                    case 2:
                        BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(i * 2), (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535, MidpointRounding.AwayFromZero));
                        break;
                    default:
                        BinaryPrimitives.WriteSingleLittleEndian(data.AsSpan(i * 4), (float)value);
                        break;
                }
            }

            // A layout without alpha is opaque, a gray one has the same three colors
            if (channels < 4)
                Py.Assert(pixels[(p * 4) + 3] == 1, stem + ": an opaque pixel");

            if (channels == 1)
                Py.Assert(pixels[p * 4] == pixels[(p * 4) + 1] && pixels[p * 4] == pixels[(p * 4) + 2], stem + ": a gray pixel");
        }

        var entry = c.Write(stem + "." + layout + ".raw", data);
        entry["layout"] = layout;
        entry["width"] = width;
        entry["height"] = height;
        entry["rowBytes"] = width * channels * bytesPerSample;
        entry["byteLength"] = data.Length;
        entry["alpha"] = channels == 4 ? "straight" : "none";
        entry["rowOrder"] = "top-down";
        entry["compression"] = "none";
        return entry;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Cross-checks
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>Decodes a file with texconv to an uncompressed format, keeping its structure and mipmap levels.</summary>
    private static (byte[] Data, string Command) TexconvDecode(Corpus<DdsTools> c, byte[] data, DdsTexture texture, string format)
    {
        // -tu: a TYPELESS format is read as UNORM, as the library reads it
        var (decoded, command) = c.Tools.Texconv(c, data, ["-tu", "-m", texture.MipCount.ToString(CultureInfo.InvariantCulture), "-f", format]);
        return (decoded, command);
    }

    /// <summary>
    /// Compares a reference with the decoding of texconv, on the channels the format stores, at the precision of the
    /// format: 8 or 16 bits, multiples of 1/127, or the floats themselves.
    /// </summary>
    private static Obj TexconvCrossCheck(Corpus<DdsTools> c, byte[] data, Surfaces reference, string channels, int bits, string? note, bool srgb = false)
    {
        // The uncompressed formats are compared through floats: DirectXTex converts 5-bit and 6-bit channels to 8 bits by
        // replicating their bits (231 for 28/31), where the float is the exact fraction; sRGB formats stay 8-bit (a
        // conversion to float would linearize them), and so do the 8-bit samples of the blocks
        var isBlockCompressed = reference.Texture.Format.BlockLength > 0;
        var target = srgb || ReferenceDds.ReadColorLabels(data).IsSrgb ? "R8G8B8A8_UNORM_SRGB" : bits == 8 && isBlockCompressed ? "R8G8B8A8_UNORM" : "R32G32B32A32_FLOAT";
        var (decoded, command) = TexconvDecode(c, data, reference.Texture, target);
        var decodedTexture = ReferenceDds.Read(decoded);
        Py.Assert(decodedTexture.Surfaces.Count == reference.Texture.Surfaces.Count, "texconv keeps every surface");
        var differing = 0;
        var maxError = 0.0;
        var total = 0;
        string? first = null;
        for (var s = 0; s < reference.Pixels.Count; s++)
        {
            var expected = reference.Pixels[s];
            var actual = ReferenceDds.ReadPixels(decoded, decodedTexture, decodedTexture.Surfaces[s]);
            for (var i = 0; i < expected.Length; i++)
            {
                var channel = i % 4;
                var stored = channels.Contains("rgba"[channel], StringComparison.Ordinal);
                if (!stored)
                    continue;

                // The red of a gray or alpha-only format is the stored channel; texconv gives it in red
                total++;
                var (a, b) = (expected[i], actual[i]);
                var equal = bits switch
                {
                    8 => Round(a * 255) == Round(b * 255),
                    16 when !IsFloat(reference) => Round(a * 65535) == Round(b * 65535),
                    127 => Round(a * 127) == Round(b * 127) && (float)a == (float)b,
                    _ => (float)a == (float)b || (double.IsNaN(a) && double.IsNaN(b)),
                };
                if (!equal)
                {
                    first ??= string.Create(CultureInfo.InvariantCulture, $"surface {s} sample {i}: reference {a}, texconv {b}");
                    differing++;
                    maxError = Math.Max(maxError, Math.Abs(a - b) * (bits == 16 && !IsFloat(reference) ? 65535 : 255));
                }
            }
        }

        var check = new Obj
        {
            ["decoder"] = $"texconv (DirectXTex {c.Tools.DirectXTexVersion})",
            ["command"] = command,
        };
        if (differing == 0)
        {
            check["result"] = "exact";
            check["maxAbsoluteError"] = 0;
            check["meanAbsoluteError"] = 0.0;
            check["notes"] = $"texconv decodes the file to {target}: the {total} stored samples are the reference{(bits == 8 || bits == 16 ? " at the precision of the format" : ", bit for bit")}.";
            return check;
        }

        if (note is null)
            throw new FatalException(string.Create(CultureInfo.InvariantCulture, $"texconv differs from the reference of a {reference.Texture.Format.Name} file ({target}): {differing} of {total} samples, up to {maxError}; {first}."));

        check["result"] = "differs";
        check["excludedFromTolerance"] = true;
        check["notes"] = string.Create(CultureInfo.InvariantCulture, $"{differing} of the {total} stored samples differ. {note}");
        return check;
    }

    private static double Round(double value) => Math.Round(value, MidpointRounding.AwayFromZero);

    private static bool IsFloat(Surfaces reference) => reference.Texture.Format.Name.Contains("FLOAT", StringComparison.Ordinal) || reference.Texture.Format.Name.EndsWith("F", StringComparison.Ordinal) || reference.Texture.Format.Name.Contains("SHAREDEXP", StringComparison.Ordinal);

    /// <summary>Compares the top surface of a BC6H or BC7 reference with the raw samples OpenImageIO prints (<c>--dumpdata</c>).</summary>
    private static Obj OiioCrossCheck(Corpus<DdsTools> c, byte[] data, Surfaces reference, BlockFormat format)
        => OiioCrossCheck(c, data, reference, format.Channels, integer: format.Reference == "BC7", notes: reference.Texture.Surfaces.Count > 1
            ? "The raw samples OpenImageIO prints for the top surface are those of texconv (oiiotool reads the first surface only)."
            : "The raw samples OpenImageIO prints are those of texconv.");

    /// <summary>
    /// Compares the top surface of a reference with the raw samples OpenImageIO prints (<c>--dumpdata</c>): 8-bit integers,
    /// or half floats.
    /// </summary>
    private static Obj OiioCrossCheck(Corpus<DdsTools> c, byte[] data, Surfaces reference, string channels, bool integer, string notes)
    {
        var path = c.Scratch / "oiio-check.dds";
        File.WriteAllBytes(path, data);
        List<string> command = [c.Tools.Oiiotool, "-v", "--info", "--dumpdata", path.Value];
        var output = Proc.RunText(command).Replace("\r", "", StringComparison.Ordinal).Split('\n');
        var top = reference.Texture.Surfaces[0];
        var expected = reference.Pixels[0];
        var differing = 0;
        var count = 0;
        foreach (var line in output)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith("Pixel (", StringComparison.Ordinal))
                continue;

            var close = trimmed.IndexOf(')', StringComparison.Ordinal);
            var coordinates = trimmed[7..close].Split(',');
            var x = int.Parse(coordinates[0], CultureInfo.InvariantCulture);
            var y = int.Parse(coordinates[1], CultureInfo.InvariantCulture);
            var valuesText = trimmed[(close + 2)..];
            var paren = valuesText.IndexOf('(', StringComparison.Ordinal);
            if (paren >= 0)
                valuesText = valuesText[..paren];

            var values = valuesText.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
            for (var ch = 0; ch < values.Length && ch < 4; ch++)
            {
                if (!channels.Contains("rgba"[ch], StringComparison.Ordinal))
                    continue;

                var e = expected[(((y * top.Width) + x) * 4) + ch];
                var equal = integer ? values[ch] == Round(e * 255) : (Half)values[ch] == (Half)e;
                count++;
                if (!equal)
                    differing++;
            }
        }

        Py.Assert(count >= top.Width * top.Height * Math.Min(3, channels.Length), "OpenImageIO prints every pixel of the top surface");
        if (differing != 0)
            throw new FatalException(string.Create(CultureInfo.InvariantCulture, $"OpenImageIO differs from the reference of a {reference.Texture.Format.Name} file: {differing} of {count} samples."));

        return new Obj
        {
            ["decoder"] = $"OpenImageIO {c.Tools.OpenImageIoVersion} (oiiotool, DDS reader)",
            ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]),
            ["result"] = "exact",
            ["maxAbsoluteError"] = 0,
            ["meanAbsoluteError"] = 0.0,
            ["notes"] = notes,
        };
    }

    /// <summary>Compares the top levels of the faces (or elements) of an 8-bit RGBA reference with the images ImageMagick reads from the file.</summary>
    private static Obj MagickCrossCheck(Corpus<DdsTools> c, byte[] data, Surfaces reference, string notes)
    {
        var directory = c.Scratch / ("magick-check-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = directory / "input.dds";
        File.WriteAllBytes(path, data);
        List<string> command = [c.Tools.MagickPath, path.Value, "-depth", "8", "rgba:-"];
        var output = Proc.Run(command);
        var tops = reference.Texture.Surfaces.Select((surface, i) => (surface, i)).Where(item => item.surface.MipLevel == 0 && item.surface.DepthSlice == 0).ToList();
        var length = tops[0].surface.Width * tops[0].surface.Height * 4;
        Py.Assert(output.Length == length * tops.Count, string.Create(CultureInfo.InvariantCulture, $"ImageMagick gives the top level of each face: {output.Length} bytes for {tops.Count} faces"));
        for (var k = 0; k < tops.Count; k++)
        {
            var expected = reference.Pixels[tops[k].i];
            for (var j = 0; j < length; j++)
                Py.Assert(output[(k * length) + j] == Round(expected[j] * 255), "ImageMagick gives the reference of each face");
        }

        return new Obj
        {
            ["decoder"] = $"ImageMagick {c.Tools.MagickVersion}",
            ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]),
            ["result"] = "exact",
            ["maxAbsoluteError"] = 0,
            ["meanAbsoluteError"] = 0.0,
            ["notes"] = notes,
        };
    }

    private static void AddError(Corpus<DdsTools> c, string kind, string name, string function, byte[] data, Obj expectedError, string notes, Obj? decodeOptions = null, bool structure = false)
    {
        _ = structure;
        var fixtureId = kind == "limit" ? "limit/dds/" + name : "invalid/dds/" + name;
        if (kind != "limit")
        {
            // texconv rejects the file too, except the defects it ignores
            var texconvRejects = TexconvRejects(c, data);
            if (name is "pitch-wrong" or "linear-size-wrong")
            {
                Py.Assert(!texconvRejects, fixtureId + ": texconv now rejects the file");
            }
            else if (!texconvRejects)
            {
                notes += " texconv reads the file.";
            }
        }

        var provenance = c.Provenance("generated", function, null, [$"DirectXTex {c.Tools.DirectXTexVersion} (texconv, cross-check)", ToolSet.RuntimeLabel]);
        c.AddError(fixtureId, kind, fixtureId + ".dds", data, "dds", provenance, expectedError, decodeOptions: decodeOptions, notes: notes);
    }

    private static bool TexconvRejects(Corpus<DdsTools> c, byte[] data)
    {
        try
        {
            c.Tools.Texconv(c, data, ["-tu", "-f", "R8G8B8A8_UNORM"]);
            return false;
        }
        catch (ToolException)
        {
            return true;
        }
    }

    private static Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "Dds" };

    private static Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Dds", ["feature"] = feature };

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class DdsTools : ToolSet
    {
        public DdsTools(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            TexconvPath = PrebuiltTools.Texconv.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            TexassemblePath = PrebuiltTools.Texassemble.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Oiiotool = PrebuiltTools.OiioTool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            MagickPath = PrebuiltTools.Magick.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            DirectXTexVersion = PrebuiltTools.Texconv.Version;
            OpenImageIoVersion = PrebuiltTools.OiioTool.Version;
            MagickVersion = PrebuiltTools.Magick.Version;
            if ((DirectXTexVersion != PinnedDirectXTex || OpenImageIoVersion != PinnedOpenImageIo || MagickVersion != PinnedMagick) && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: DirectXTex {DirectXTexVersion} (pinned {PinnedDirectXTex}), OpenImageIO {OpenImageIoVersion} (pinned {PinnedOpenImageIo}), ImageMagick {MagickVersion} (pinned {PinnedMagick}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [TexconvPath] = "texconv", [TexassemblePath] = "texassemble", [Oiiotool] = "oiiotool", [MagickPath] = "magick" };
        }

        public string TexconvPath { get; }

        public string TexassemblePath { get; }

        public string Oiiotool { get; }

        public string MagickPath { get; }

        public string DirectXTexVersion { get; }

        public string OpenImageIoVersion { get; }

        public string MagickVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        /// <summary>Converts a DDS file with texconv (CPU encoders), in a directory of its own.</summary>
        public (byte[] Data, string Command) Texconv(Corpus<DdsTools> c, byte[] input, IEnumerable<string> options)
        {
            var directory = c.Scratch / ("texconv-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory / "out");
            var path = directory / "input.dds";
            File.WriteAllBytes(path, input);
            List<string> command = [TexconvPath, "-nologo", "-y", .. options, "-o", (directory / "out").Value, path.Value];
            RunTool(command);
            var output = File.ReadAllBytes(directory / "out" / "input.dds");
            return (output, CommonCorpus.Display(command, this, [((directory / "out").Value, "{output}"), (path.Value, "{input}.dds")]));
        }

        /// <summary>Assembles DDS files into a cube map, an array, a volume texture or an array of cube maps.</summary>
        public (byte[] Data, string Command) Texassemble(Corpus<DdsTools> c, string kind, IReadOnlyList<byte[]> inputs)
        {
            var directory = c.Scratch / ("texassemble-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var paths = inputs.Select((input, i) =>
            {
                var path = directory / string.Create(CultureInfo.InvariantCulture, $"surface{i}.dds");
                File.WriteAllBytes(path, input);
                return path.Value;
            }).ToList();
            var output = directory / "assembled.dds";
            List<string> command = [TexassemblePath, kind, "-nologo", "-y", "-dx10", "-o", output.Value, .. paths];
            RunTool(command);
            return (File.ReadAllBytes(output), CommonCorpus.Display(command, this, [(output.Value, "{output}"), .. paths.Select((p, i) => (p, string.Create(CultureInfo.InvariantCulture, $"{{surface{i}}}.dds")))]));
        }

        /// <summary>Runs a DirectXTex tool, which reports its errors on its standard output.</summary>
        private static void RunTool(List<string> command)
        {
            var result = Proc.Exec(command);
            if (result.ExitCode != 0)
                throw new ToolException($"Command failed ({result.ExitCode}): {string.Join(' ', command)}\n{Encoding.UTF8.GetString(result.Stdout)}{Encoding.UTF8.GetString(result.Stderr)}");
        }

        /// <summary>Writes 8-bit RGBA samples as a DDS file with ImageMagick.</summary>
        public (byte[] Data, string Command) Magick(Corpus<DdsTools> c, byte[] rgba, int width, int height, string compression, int mips)
        {
            var directory = c.Scratch / ("magick-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            var input = directory / "input.rgba";
            File.WriteAllBytes(input, rgba);
            var output = directory / "output.dds";
            List<string> command = [MagickPath, "-size", string.Create(CultureInfo.InvariantCulture, $"{width}x{height}"), "-depth", "8", "rgba:" + input.Value, "-define", "dds:compression=" + compression, "-define", "dds:mipmaps=" + mips.ToString(CultureInfo.InvariantCulture), output.Value];
            Proc.Run(command);
            return (File.ReadAllBytes(output), CommonCorpus.Display(command, this, [(input.Value, "{pattern}.rgba"), (output.Value, "{output}.dds")]));
        }
    }
}
