using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the KTX 2.0 conformance files of tests/Meziantou.Framework.Imaging.Ktx2.Tests/Files and their references
/// (files.json). This generator is the reviewed, reproducible recipe of every committed KTX 2.0 file. It is NEVER run by the
/// tests and does not use the library under test.
/// <para>
/// KTX 2.0 is a random-access container whose subresources are reached through <c>ImageCollection</c>, like TIFF, so it has
/// no fixture in the golden corpus (whose suites read every fixture sequentially): the conformance tests decode each
/// file listed here and compare the SHA-256 of every reference image with the decoded one.
/// </para>
/// <para>
/// The files are written by KTX-Software (<c>ktx create</c>), astcenc and basisu from patterns, or by <c>ktx create --raw</c>
/// from block data the generator chooses. Each reference comes from the reference tools only, and is cross-checked:
/// </para>
/// <list type="bullet">
/// <item><description>uncompressed texels: the pattern given to <c>ktx create --raw</c>, converted by the rules of the format (the channels a format lacks are 0, alpha 1); <c>ktx extract --raw</c> must give the stored texels back;</description></item>
/// <item><description>BCn: the blocks of the DDS fixtures of the golden corpus, written by <c>ktx create --raw</c>; the reference is the one of the DDS fixture (texconv of DirectXTex and the exact S3TC reference of the DDS generator, cross-checked there), read by the rules of KTX (the channels a format lacks are 0, alpha 1; BC1 without alpha is opaque); <c>ktx extract --raw</c> must give the blocks back;</description></item>
/// <item><description>ETC2 and EAC (random blocks): Apple ImageIO (<c>sips</c>, macOS); it reads the punchthrough and signed formats wrongly, which the unit tests cover;</description></item>
/// <item><description>ASTC: <c>ktx extract</c> (sRGB, decode_unorm8) and astcenc (<c>-ds</c>, <c>-dh</c>: the halves of decode_float16), which must agree;</description></item>
/// <item><description>ETC1S and UASTC: <c>ktx extract --transcode rgba8</c>, cross-checked with <c>basisu -unpack</c>; for the sRGB transfer function, the UASTC blocks transcoded losslessly to ASTC by basisu and decoded as ASTC sRGB by <c>ktx extract</c> (both tools decode UASTC with the linear expansion whatever the transfer function, which differs by 1 at most).</description></item>
/// </list>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- ktx2
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- ktx2 --write
/// Requirements: macOS (Apple ImageIO), and FFmpeg, KTX-Software, astcenc and basisu from the meziantou/prebuilt release
/// pinned by the Meziantou.Prebuilt package.
/// </summary>
internal static class Ktx2Corpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/Ktx2Corpus.cs";

    private const string PinnedKtx = "4.4.2";
    private const string PinnedAstcenc = "5.7.1";
    private const string PinnedBasisu = "2.50";

    private static FullPath OutputDir => CorpusDriver.RepoRoot / "tests" / "Meziantou.Framework.Imaging.Ktx2.Tests" / "Files";

    public static int Run(GeneratorOptions options)
    {
        if (!OperatingSystem.IsMacOS())
            throw new FatalException("The ktx2 generator needs Apple ImageIO (sips): run it on macOS.");

        var tools = new Ktx2Tools(options.AcceptToolVersions);
        var temp = FullPath.FromPath(Path.Combine(Path.GetTempPath(), "mfi-ktx2-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(temp);
        try
        {
            var output = temp / "Files";
            Directory.CreateDirectory(output);
            var scratch = temp / "scratch";
            Directory.CreateDirectory(scratch);
            var generator = new Generator(tools, output, scratch);
            generator.Build();
            var json = new JsonObject
            {
                ["generator"] = ScriptPath,
                ["tools"] = new JsonArray($"KTX-Software {tools.KtxVersion}", $"astcenc {tools.AstcencVersion}", $"basisu {tools.BasisuVersion}", tools.FfmpegLabel, "Apple ImageIO (sips)", ToolSet.RuntimeLabel),
                ["files"] = generator.Files,
            };
            File.WriteAllText(output / "files.json", json.ToJsonString(new JsonSerializerOptions { WriteIndented = true, RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true }) + "\n", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            var differences = Compare(output, OutputDir);
            if (options.Write)
            {
                if (Directory.Exists(OutputDir))
                {
                    Directory.Delete(OutputDir, recursive: true);
                }

                CopyDirectory(output, OutputDir);
                Console.WriteLine($"Wrote {generator.Files.Count} KTX 2.0 files and files.json to {OutputDir}.");
                return 0;
            }

            foreach (var difference in differences)
            {
                Console.WriteLine(difference);
            }

            Console.WriteLine(differences.Count == 0 ? "The committed KTX 2.0 files are reproducible." : $"{differences.Count} files differ; rerun with --write to replace them.");
            return differences.Count == 0 ? 0 : 1;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static List<string> Compare(FullPath generated, FullPath committed)
    {
        var differences = new List<string>();
        var names = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(generated))
        {
            names.Add(Path.GetFileName(file));
        }

        if (Directory.Exists(committed))
        {
            foreach (var file in Directory.EnumerateFiles(committed))
            {
                names.Add(Path.GetFileName(file));
            }
        }

        foreach (var name in names)
        {
            var a = generated / name;
            var b = committed / name;
            if (!File.Exists(a) || !File.Exists(b) || !File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b)))
            {
                differences.Add("differs: " + name);
            }
        }

        return differences;
    }

    private static void CopyDirectory(FullPath source, FullPath destination)
    {
        Directory.CreateDirectory(destination);
        foreach (var file in Directory.EnumerateFiles(source))
        {
            File.Copy(file, destination / Path.GetFileName(file));
        }
    }

    /// <summary>The reference image of a subresource (or of a frame of the animation).</summary>
    private sealed record Reference(int Index, int Level, int Layer, int Face, int Slice, int Width, int Height, string Layout, byte[] Pixels, string Method, JsonArray CrossChecks);

    private sealed class Generator(Ktx2Tools tools, FullPath output, FullPath scratch)
    {
        public JsonArray Files { get; } = [];

        public void Build()
        {
            BuildUncompressed();
            BuildEtc2();
            BuildBcn();
            BuildAstc();
            BuildBasis();
        }

        // -------------------------------------------------------------------------------------------------------------
        // Patterns
        // -------------------------------------------------------------------------------------------------------------

        /// <summary>An 8-bit RGBA pattern: sines, gradients, saturated squares and translucent rows.</summary>
        private static byte[] Pattern8(int width, int height, int seed)
        {
            var state = (uint)seed + 1;
            var data = new byte[width * height * 4];
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    var r = (int)(127 + (120 * Math.Sin((x / 5.0) + (y / 9.0) + seed)));
                    var g = width == 1 ? 128 : x * 255 / (width - 1);
                    var b = (int)(127 + (127 * Math.Cos(x * y / 40.0)));
                    var a = (x + y + seed) % 7 != 0 ? 255 : height == 1 ? 64 : y * 255 / (height - 1);
                    if (x > width / 4 && x < width / 2 && y > height / 4 && y < 3 * height / 4)
                    {
                        (r, g, b) = (x + y) % 2 == 0 ? (250, 10, 10) : (10, 10, 250);
                    }

                    var offset = ((y * width) + x) * 4;
                    data[offset] = (byte)Math.Clamp(r + (int)(Next(ref state) % 13) - 6, 0, 255);
                    data[offset + 1] = (byte)g;
                    data[offset + 2] = (byte)Math.Clamp(b, 0, 255);
                    data[offset + 3] = (byte)a;
                }
            }

            return data;
        }

        private static byte[] RandomBytes(int length, int seed)
        {
            var data = new byte[length];
            var state = (uint)seed + 1;
            for (var i = 0; i < data.Length; i++)
            {
                data[i] = (byte)(Next(ref state) >> 24);
            }

            return data;
        }

        /// <summary>A deterministic xorshift generator: the patterns do not depend on the runtime.</summary>
        private static uint Next(ref uint state)
        {
            state ^= state << 13;
            state ^= state >> 17;
            state ^= state << 5;
            return state;
        }

        private static byte[] Pam(int width, int height, byte[] rgba)
            => [.. Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"P7\nWIDTH {width}\nHEIGHT {height}\nDEPTH 4\nMAXVAL 255\nTUPLTYPE RGB_ALPHA\nENDHDR\n")), .. rgba];

        /// <summary>A flat Radiance HDR picture of high dynamic range, the input of the HDR ASTC encoder.</summary>
        private static byte[] RadiancePicture(int width, int height)
        {
            var data = new List<byte>(Encoding.ASCII.GetBytes(string.Create(CultureInfo.InvariantCulture, $"#?RADIANCE\nFORMAT=32-bit_rle_rgbe\n\n-Y {height} +X {width}\n")));
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    double[] color = [Math.Exp((x - (width / 2.0)) / 4.0), 0.01 + (Math.Abs(Math.Sin(x * y / 50.0)) * 20), (x + y) % 5 == 0 ? 0 : Math.Pow(y / (double)height, 3) * 100];
                    var max = color.Max();
                    var exponent = Math.ILogB(max) + 1;
                    var scale = Math.ScaleB(256.0, -exponent);
                    data.AddRange([(byte)(color[0] * scale), (byte)(color[1] * scale), (byte)(color[2] * scale), (byte)(exponent + 128)]);
                }
            }

            return [.. data];
        }

        // -------------------------------------------------------------------------------------------------------------
        // Files
        // -------------------------------------------------------------------------------------------------------------

        private void BuildUncompressed()
        {
            // Three levels of sRGB RGBA: the base level and two more, each its own pattern
            {
                (int Width, int Height)[] sizes = [(13, 9), (6, 4), (3, 2)];
                var levels = sizes.Select((size, i) => Pattern8(size.Width, size.Height, i)).ToArray();
                var file = KtxRaw("rgba8-srgb-levels", "R8G8B8A8_SRGB", 13, 9, levels, ["--levels", "3"]);
                AddUncompressed(file, "Three levels of 8-bit sRGB RGBA, each a pattern.", levels.Select((level, i) => (i, i, 0, 0, 0, sizes[i].Width, sizes[i].Height, "rgba8", level)).ToArray(), ["--level", "{level}"]);
            }

            // 16-bit RGBA, supercompressed with Zstandard
            {
                var samples = RandomBytes(11 * 7 * 8, 10);
                var file = KtxRaw("rgba16-unorm-zstd", "R16G16B16A16_UNORM", 11, 7, [samples], ["--zstd", "12"]);
                AddUncompressed(file, "16-bit RGBA supercompressed with Zstandard (random samples).", [(0, 0, 0, 0, 0, 11, 7, "rgba16le", samples)], []);
            }

            // 8-bit RGB with ZLIB: alpha is 1
            {
                var rgb = RandomBytes(9 * 5 * 3, 11);
                var file = KtxRaw("rgb8-unorm-zlib", "R8G8B8_UNORM", 9, 5, [rgb], ["--zlib", "9"]);
                AddUncompressed(file, "8-bit RGB supercompressed with ZLIB: alpha is 1.", [(0, 0, 0, 0, 0, 9, 5, "rgba8", Expand(rgb, 3, [0, 1, 2]))], []);
            }

            // A cube map of 32-bit floats, values outside [0, 1] included
            {
                var faces = new byte[6][];
                for (var face = 0; face < 6; face++)
                {
                    faces[face] = new byte[4 * 4 * 16];
                    for (var i = 0; i < 4 * 4 * 4; i++)
                    {
                        BinaryPrimitives.WriteSingleLittleEndian(faces[face].AsSpan(i * 4), ((i * 7) - 20 + (face * 3)) / 8f);
                    }
                }

                var file = KtxRaw("rgba32f-cube", "R32G32B32A32_SFLOAT", 4, 4, faces, ["--cubemap"]);
                AddUncompressed(file, "A cube map of 32-bit float RGBA: every face is an entry, from +X to -Z.", faces.Select((data, face) => (face, 0, 0, face, 0, 4, 4, "rgbaf32le", data)).ToArray(), ["--face", "{face}"]);
            }

            // An array of three layers of halves
            {
                var layers = new byte[3][];
                for (var layer = 0; layer < 3; layer++)
                {
                    layers[layer] = new byte[5 * 3 * 8];
                    for (var i = 0; i < 5 * 3 * 4; i++)
                    {
                        BinaryPrimitives.WriteHalfLittleEndian(layers[layer].AsSpan(i * 2), (Half)(((i * 5) - 40 + layer) / 16f));
                    }
                }

                var file = KtxRaw("rgba16f-array", "R16G16B16A16_SFLOAT", 5, 3, layers, ["--layers", "3"]);
                AddUncompressed(file, "An array of three layers of half RGBA.", layers.Select((data, layer) => (layer, 0, layer, 0, 0, 5, 3, "rgbaf16le", data)).ToArray(), ["--layer", "{layer}"]);
            }

            // Luminance: R8 with the swizzle rrr1, and R8G8 without swizzle (blue 0, alpha 1)
            {
                var gray = RandomBytes(7 * 3, 12);
                var file = KtxRaw("r8-luminance", "R8_UNORM", 7, 3, [gray], ["--swizzle", "rrr1"]);
                AddUncompressed(file, "R8 with the swizzle rrr1 (Luminance8, section 5.4.1): gray.", [(0, 0, 0, 0, 0, 7, 3, "rgba8", Expand(gray, 1, [0, 0, 0]))], []);
                var rg = RandomBytes(7 * 3 * 2, 13);
                var rgFile = KtxRaw("rg8-unorm", "R8G8_UNORM", 7, 3, [rg], []);
                AddUncompressed(rgFile, "R8G8: blue is 0 and alpha 1, as a GPU samples it.", [(0, 0, 0, 0, 0, 7, 3, "rgba8", Expand(rg, 2, [0, 1, -1]))], []);
            }

            // A three-dimensional texture of 4 slices
            {
                var slices = RandomBytes(3 * 2 * 4 * 4, 14);
                var file = KtxRaw("rgba8-3d", "R8G8B8A8_UNORM", 3, 2, [.. Enumerable.Range(0, 4).Select(slice => slices.AsSpan(slice * 24, 24).ToArray())], ["--depth", "4"]);
                AddUncompressed(file, "A 3x2x4 texture: each depth slice is an entry.", Enumerable.Range(0, 4).Select(slice => (slice, 0, 0, 0, slice, 3, 2, "rgba8", slices.AsSpan(slice * 24, 24).ToArray())).ToArray(), ["--depth", "{slice}"]);
            }
        }

        /// <summary>Expands texels of 1 to 3 components to RGBA8: each output color is a component (or 0 for -1), and alpha 255.</summary>
        private static byte[] Expand(byte[] texels, int components, int[] colors)
        {
            var count = texels.Length / components;
            var result = new byte[count * 4];
            for (var i = 0; i < count; i++)
            {
                for (var c = 0; c < 3; c++)
                {
                    result[(i * 4) + c] = colors[c] < 0 ? (byte)0 : texels[(i * components) + colors[c]];
                }

                result[(i * 4) + 3] = 255;
            }

            return result;
        }

        private void BuildEtc2()
        {
            foreach (var (name, format, layout, blockBytes) in new[]
            {
                ("etc2-rgb8-srgb", "ETC2_R8G8B8_SRGB_BLOCK", "rgba8", 8),
                ("etc2-rgba8-unorm", "ETC2_R8G8B8A8_UNORM_BLOCK", "rgba8", 16),
                ("eac-r11-unorm", "EAC_R11_UNORM_BLOCK", "rgba16le", 8),
                ("eac-rg11-unorm", "EAC_R11G11_UNORM_BLOCK", "rgba16le", 16),
            })
            {
                // Random blocks reach every mode: individual, differential, T, H and planar
                var blocks = RandomBytes(4 * 3 * blockBytes, name.Length * 31);
                var file = KtxRaw(name, format, 16, 12, [blocks], []);
                var reference = ImageIoDecode(file, 16, 12);
                if (layout == "rgba8")
                {
                    var bytes = new byte[reference.Length / 2];
                    for (var i = 0; i < bytes.Length; i++)
                    {
                        var value = BinaryPrimitives.ReadUInt16LittleEndian(reference.AsSpan(i * 2));
                        Py.Assert(value % 257 == 0, name + ": Apple ImageIO gives 16-bit samples of 8-bit values");
                        bytes[i] = (byte)(value / 257);
                    }

                    reference = bytes;
                }

                AddFile(file, $"{format}: 16x12 texels of random blocks.", [new Reference(0, 0, 0, 0, 0, 16, 12, layout, reference, "Apple ImageIO (sips, a 16-bit PNG file)", [])]);
            }
        }

        private void BuildBcn()
        {
            var fixtures = CorpusDriver.RepoRoot / "tests" / "Meziantou.Framework.Imaging.Fixtures" / "dds";
            foreach (var (name, fixture, format, blockBytes, reference) in new[]
            {
                ("bc1-rgb-unorm", "random-bc1-unorm", "BC1_RGB_UNORM_BLOCK", 8, "rgba8"),
                ("bc1-rgba-unorm-odd-size", "odd-size-bc1", "BC1_RGBA_UNORM_BLOCK", 8, "rgba8"),
                ("bc1-rgba-srgb", "random-bc1-unorm-srgb", "BC1_RGBA_SRGB_BLOCK", 8, "rgba8"),
                ("bc2-unorm", "random-bc2-unorm", "BC2_UNORM_BLOCK", 16, "rgba8"),
                ("bc3-srgb", "random-bc3-unorm-srgb", "BC3_SRGB_BLOCK", 16, "rgba8"),
                ("bc4-unorm", "random-bc4-unorm", "BC4_UNORM_BLOCK", 8, "gray8"),
                ("bc4-snorm", "random-bc4-snorm", "BC4_SNORM_BLOCK", 8, "rgbaf32le"),
                ("bc5-unorm", "random-bc5-unorm", "BC5_UNORM_BLOCK", 16, "rgba8"),
                ("bc5-snorm", "random-bc5-snorm", "BC5_SNORM_BLOCK", 16, "rgbaf32le"),
                ("bc6h-ufloat", "random-bc6h-uf16", "BC6H_UFLOAT_BLOCK", 16, "rgbaf32le"),
                ("bc6h-sfloat", "random-bc6h-sf16", "BC6H_SFLOAT_BLOCK", 16, "rgbaf32le"),
                ("bc7-unorm-odd-size", "odd-size-bc7", "BC7_UNORM_BLOCK", 16, "rgba8"),
                ("bc7-srgb", "random-bc7-unorm-srgb", "BC7_SRGB_BLOCK", 16, "rgba8"),
            })
            {
                // The DDS fixture has a DX10 header (148 bytes) and one surface: its blocks, row by row
                var dds = File.ReadAllBytes(fixtures / (fixture + ".dds"));
                Py.Assert(dds.AsSpan(84, 4).SequenceEqual("DX10"u8), fixture + ": the DDS fixture has a DX10 header");
                var height = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(12));
                var width = BinaryPrimitives.ReadInt32LittleEndian(dds.AsSpan(16));
                var blocks = dds.AsSpan(148, ((width + 3) / 4) * ((height + 3) / 4) * blockBytes).ToArray();
                var file = KtxRaw("bcn-" + name, format, width, height, [blocks], []);
                var raw = scratch / "extract.raw";
                Run(tools.Ktx, "extract", "--raw", file, raw);
                Py.Assert(File.ReadAllBytes(raw).AsSpan().SequenceEqual(blocks), name + ": ktx extract --raw gives the blocks back");
                File.Delete(raw);

                var source = File.ReadAllBytes(fixtures / string.Create(CultureInfo.InvariantCulture, $"{fixture}.frame-0.{reference}.raw"));
                var (layout, pixels) = ToKtxTexels(format, reference, source, width * height);
                AddFile(file, $"{format}: the {width}x{height} blocks of the DDS fixture dds/{fixture}.", [new Reference(0, 0, 0, 0, 0, width, height, layout, pixels,
                    $"The reference of the DDS fixture dds/{fixture} (golden corpus), read by the rules of KTX: the channels the format lacks are 0, alpha 1",
                    [Check("ktx extract --raw", "exact", "The stored blocks are those of the DDS fixture.")])]);
            }
        }

        /// <summary>
        /// Converts the reference of a DDS fixture to the texels of the KTX 2.0 decoder: BC1 without alpha is opaque, BC4 is
        /// red and BC5 red and green (blue 0, alpha 1), and BC6H is stored as halves (the floats of the DDS reference are
        /// halves converted exactly).
        /// </summary>
        private static (string Layout, byte[] Pixels) ToKtxTexels(string format, string reference, byte[] source, int count)
        {
            if (reference == "gray8")
            {
                return ("rgba8", Expand(source, 1, [0, -1, -1]));
            }

            if (reference == "rgba8")
            {
                var pixels = source.ToArray();
                for (var i = 0; i < count; i++)
                {
                    if (format.StartsWith("BC1_RGB_", StringComparison.Ordinal))
                    {
                        pixels[(i * 4) + 3] = 255;
                    }
                    else if (format.StartsWith("BC5_", StringComparison.Ordinal))
                    {
                        Py.Assert(pixels[(i * 4) + 2] == 0 && pixels[(i * 4) + 3] == 255, format + ": the DDS reference of BC5 has blue 0 and alpha 1");
                    }
                }

                return ("rgba8", pixels);
            }

            var floats = new float[count * 4];
            for (var i = 0; i < floats.Length; i++)
            {
                floats[i] = BinaryPrimitives.ReadSingleLittleEndian(source.AsSpan(i * 4));
            }

            if (format.StartsWith("BC6H_", StringComparison.Ordinal))
            {
                var halves = new byte[count * 8];
                for (var i = 0; i < floats.Length; i++)
                {
                    var half = (Half)floats[i];
                    Py.Assert((float)half == floats[i] || (float.IsNaN(floats[i]) && Half.IsNaN(half)), format + ": the DDS reference holds halves");
                    BinaryPrimitives.WriteHalfLittleEndian(halves.AsSpan(i * 2), half);
                }

                return ("rgbaf16le", halves);
            }

            // Signed BC4 and BC5: red (and green), blue 0, alpha 1
            var channels = format.StartsWith("BC5_", StringComparison.Ordinal) ? 2 : 1;
            var texels = new byte[count * 16];
            for (var i = 0; i < count; i++)
            {
                for (var c = 0; c < 4; c++)
                {
                    var value = c < channels ? floats[(i * 4) + c] : c == 3 ? 1f : 0f;
                    BinaryPrimitives.WriteSingleLittleEndian(texels.AsSpan((i * 16) + (c * 4)), value);
                }
            }

            return ("rgbaf32le", texels);
        }

        private void BuildAstc()
        {
            var pattern = Pattern8(37, 29, 3);
            var pam = scratch / "astc.pam";
            File.WriteAllBytes(pam, Pam(37, 29, pattern));
            foreach (var size in new[] { "4x4", "8x6", "12x12" })
            {
                var name = "astc-" + size + "-srgb";
                var file = output / (name + ".ktx2");
                Run(tools.Ktx, "create", "--testrun", "--format", $"ASTC_{size}_SRGB_BLOCK", "--astc-quality", "exhaustive", pam, file);
                var extracted = ExtractPng(file, [], 37, 29, sixteen: false);
                var astcenc = AstcencDecode(file, size, "-ds", 37, 29);
                Py.Assert(extracted.AsSpan().SequenceEqual(astcenc), name + ": ktx extract and astcenc -ds agree");
                AddFile(file, $"ASTC {size} sRGB from a pattern (ktx create, exhaustive quality).", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", extracted, "ktx extract (PNG)", [Check("astcenc -ds (KTX file of 8-bit samples)", "exact", "The same texels.")])]);
            }

            foreach (var size in new[] { "5x5", "10x8" })
            {
                var name = "astc-" + size + "-unorm";
                var file = output / (name + ".ktx2");
                Run(tools.Ktx, "create", "--testrun", "--format", $"ASTC_{size}_UNORM_BLOCK", "--astc-quality", "thorough", pam, file);
                var halves = AstcencDecode(file, size, "-dh", 37, 29);
                var extracted = ExtractPng(file, [], 37, 29, sixteen: false);
                for (var i = 0; i < extracted.Length; i++)
                {
                    // decode_unorm8 keeps the top 8 bits of the UNORM16 value whose half decode_float16 truncates
                    var value = (float)BinaryPrimitives.ReadHalfLittleEndian(halves.AsSpan(i * 2));
                    Py.Assert((int)(value * 256) == extracted[i] || (value == 1 && extracted[i] == 255), name + ": the halves of astcenc and the 8-bit samples of ktx extract agree");
                }

                AddFile(file, $"ASTC {size} UNORM from a pattern: decode_float16 halves (KTX section 5.7).", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgbaf16le", halves, "astcenc -dh (KTX file of halves)", [Check("ktx extract (PNG, decode_unorm8)", "exact", "The top 8 bits of each UNORM16 value, which the half keeps.")])]);
            }

            var hdr = scratch / "astc.hdr";
            File.WriteAllBytes(hdr, RadiancePicture(19, 13));
            foreach (var size in new[] { "4x4", "8x8" })
            {
                var name = "astc-" + size + "-hdr";
                var astc = scratch / (name + ".astc");
                Run(tools.Astcenc, "-ch", hdr, astc, size, "-thorough", "-silent");
                var blocks = File.ReadAllBytes(astc)[16..];
                var file = KtxRaw(name, $"ASTC_{size}_SFLOAT_BLOCK", 19, 13, [blocks], []);
                var halves = AstcencDecode(file, size, "-dh", 19, 13);
                AddFile(file, $"ASTC {size} HDR (SFLOAT) blocks written by astcenc -ch from a Radiance HDR picture.", [new Reference(0, 0, 0, 0, 0, 19, 13, "rgbaf16le", halves, "astcenc -dh (KTX file of halves)", [])]);
            }
        }

        private void BuildBasis()
        {
            var pattern = Pattern8(37, 29, 4);
            var pam = scratch / "basis.pam";
            File.WriteAllBytes(pam, Pam(37, 29, pattern));

            // ETC1S with alpha and levels
            {
                var file = output / "etc1s-rgba-srgb-levels.ktx2";
                Run(tools.Ktx, "create", "--testrun", "--format", "R8G8B8A8_SRGB", "--encode", "basis-lz", "--generate-mipmap", pam, file);
                var references = new List<Reference>();
                (int Width, int Height)[] sizes = [(37, 29), (18, 14), (9, 7), (4, 3), (2, 1), (1, 1)];
                for (var level = 0; level < sizes.Length; level++)
                {
                    var (width, height) = sizes[level];
                    var transcoded = ExtractPng(file, ["--transcode", "rgba8", "--level", level.ToString(CultureInfo.InvariantCulture)], width, height, sixteen: false);
                    var checks = new JsonArray();
                    if (level == 0)
                    {
                        Py.Assert(transcoded.AsSpan().SequenceEqual(BasisuUnpack(file, "rgba_RGBA32_level_0_face_0_layer0000", width, height)), "basisu agrees on the ETC1S level 0");
                        checks.Add(Check("basisu -unpack (RGBA32)", "exact", "The same texels."));
                    }

                    references.Add(new Reference(level, level, 0, 0, 0, width, height, "rgba8", transcoded, "ktx extract --transcode rgba8", checks));
                }

                AddFile(file, "ETC1S (BasisLZ) with an alpha slice, sRGB, and the generated levels (ktx create --encode basis-lz --generate-mipmap).", [.. references]);
            }

            // ETC1S of one and two components
            foreach (var (name, format, components) in new[] { ("etc1s-r", "R8_UNORM", 1), ("etc1s-rg", "R8G8_UNORM", 2) })
            {
                var texels = new byte[37 * 29 * components];
                for (var i = 0; i < 37 * 29; i++)
                {
                    for (var c = 0; c < components; c++)
                    {
                        texels[(i * components) + c] = pattern[(i * 4) + c];
                    }
                }

                var file = KtxRaw(name, format, 37, 29, [texels], ["--encode", "basis-lz"]);
                var transcoded = ExtractPng(file, ["--transcode", "rgba8"], 37, 29, sixteen: false);
                AddFile(file, $"ETC1S of the channels {(components == 1 ? "RRR (red)" : "RRR and GGG (red and green)")}.", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", transcoded, "ktx extract --transcode rgba8", [])]);
            }

            // An ETC1S video: P-frames refer to the previous frame
            {
                for (var frame = 0; frame < 4; frame++)
                {
                    File.WriteAllBytes(scratch / string.Create(CultureInfo.InvariantCulture, $"frame{frame}.tga"), Tga(32, 16, Pattern8(32, 16, 10 + (frame / 2))));
                }

                var file = output / "etc1s-video.ktx2";
                Run(tools.Basisu, "-ktx2", "-tex_type", "video", "-multifile_printf", (scratch / "frame%u.tga").Value, "-multifile_first", "0", "-multifile_num", "4", "-output_file", file);
                var references = new List<Reference>();
                for (var layer = 0; layer < 4; layer++)
                {
                    var transcoded = ExtractPng(file, ["--transcode", "rgba8", "--layer", layer.ToString(CultureInfo.InvariantCulture)], 32, 16, sixteen: false);
                    references.Add(new Reference(layer, 0, layer, 0, 0, 32, 16, "rgba8", transcoded, "ktx extract --transcode rgba8", []));
                }

                AddFile(file, "An ETC1S video of 4 frames (basisu -tex_type video): KTXanimData, and P-frames that reuse blocks of the previous frame.", [.. references]);
            }

            // UASTC: linear with Zstandard, sRGB, and red-green
            {
                var file = output / "uastc-rgba-unorm-zstd.ktx2";
                Run(tools.Ktx, "create", "--testrun", "--format", "R8G8B8A8_UNORM", "--encode", "uastc", "--uastc-quality", "3", "--zstd", "9", pam, file);
                var transcoded = ExtractPng(file, ["--transcode", "rgba8"], 37, 29, sixteen: false);
                Py.Assert(transcoded.AsSpan().SequenceEqual(BasisuUnpack(file, "rgba_RGBA32_level_0_face_0_layer0000", 37, 29)), "basisu agrees on the UASTC file");
                AddFile(file, "UASTC with Zstandard, linear transfer function (ktx create --encode uastc --zstd).", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", transcoded, "ktx extract --transcode rgba8", [Check("basisu -unpack (RGBA32)", "exact", "The same texels.")])]);
            }

            {
                var file = output / "uastc-rgba-srgb.ktx2";
                Run(tools.Ktx, "create", "--testrun", "--format", "R8G8B8A8_SRGB", "--encode", "uastc", "--uastc-quality", "2", pam, file);
                var (reference, check) = SrgbUastcReference(file, 37, 29);
                AddFile(file, "UASTC with the sRGB transfer function: the endpoints are expanded with 0x80 (section 25.7.2.8).", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", reference, SrgbUastcMethod, [check])]);
            }

            {
                var rg = new byte[37 * 29 * 2];
                for (var i = 0; i < 37 * 29; i++)
                {
                    rg[i * 2] = pattern[i * 4];
                    rg[(i * 2) + 1] = pattern[(i * 4) + 3];
                }

                var file = KtxRaw("uastc-rg", "R8G8_UNORM", 37, 29, [rg], ["--encode", "uastc"]);
                var transcoded = ExtractPng(file, ["--transcode", "rgba8"], 37, 29, sixteen: false);
                AddFile(file, "UASTC of an R8G8 texture.", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", transcoded, "ktx extract --transcode rgba8", [])]);
            }

            {
                var tga = scratch / "basisu.tga";
                File.WriteAllBytes(tga, Tga(37, 29, pattern));
                var file = output / "basisu-uastc.ktx2";
                Run(tools.Basisu, "-ktx2", "-uastc", "-mipmap", tga, "-output_file", file);
                var (reference, check) = SrgbUastcReference(file, 37, 29);
                AddFile(file, "UASTC with levels and the sRGB transfer function, written by basisu (-ktx2 -uastc -mipmap) from an RGB picture.", [new Reference(0, 0, 0, 0, 0, 37, 29, "rgba8", reference, SrgbUastcMethod, [check])]);
            }
        }

        private const string SrgbUastcMethod = "basisu -unpack (the lossless transcoding to ASTC 4x4), decoded as ASTC 4x4 sRGB by ktx extract";

        /// <summary>
        /// The reference of the base level of a UASTC file with the sRGB transfer function: its blocks transcoded losslessly
        /// to ASTC 4x4 by basisu, decoded as ASTC sRGB (endpoints expanded with 0x80) by ktx extract; and the check of the
        /// linear decoding of KTX-Software, which differs by 1 at most.
        /// </summary>
        private (byte[] Reference, JsonObject Check) SrgbUastcReference(FullPath file, int width, int height)
        {
            var astc = BasisuUnpackKtx1(file, "transcoded_ASTC_LDR_4X4_RGBA_*");
            var wrapped = scratch / "uastc-as-astc.ktx2";
            File.Delete(wrapped);
            Run(tools.Ktx, "create", "--testrun", "--raw", "--format", "ASTC_4x4_SRGB_BLOCK", "--width", width.ToString(CultureInfo.InvariantCulture), "--height", height.ToString(CultureInfo.InvariantCulture), WriteScratch("uastc-as-astc.bin", astc), wrapped);
            var reference = ExtractPng(wrapped, [], width, height, sixteen: false);
            var linear = ExtractPng(file, ["--transcode", "rgba8"], width, height, sixteen: false);
            var differing = 0;
            var max = 0;
            for (var i = 0; i < reference.Length; i++)
            {
                var error = Math.Abs(reference[i] - linear[i]);
                differing += error == 0 ? 0 : 1;
                max = Math.Max(max, error);
            }

            Py.Assert(max <= 1 && differing > 0, file.Name + ": the linear decoding of KTX-Software differs by 1 at most");
            return (reference, Check("ktx extract --transcode rgba8", "differs", string.Create(CultureInfo.InvariantCulture, $"KTX-Software and basisu decode UASTC with the linear expansion of the endpoints whatever the transfer function: {differing} samples differ by 1.")));
        }

        // -------------------------------------------------------------------------------------------------------------
        // Tools
        // -------------------------------------------------------------------------------------------------------------

        private static byte[] Tga(int width, int height, byte[] rgba)
        {
            // An uncompressed 24-bit TGA image, top-down
            var data = new byte[18 + (width * height * 3)];
            data[2] = 2;
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(12), (ushort)width);
            BinaryPrimitives.WriteUInt16LittleEndian(data.AsSpan(14), (ushort)height);
            data[16] = 24;
            data[17] = 0x20;
            for (var i = 0; i < width * height; i++)
            {
                data[18 + (i * 3)] = rgba[(i * 4) + 2];
                data[18 + (i * 3) + 1] = rgba[(i * 4) + 1];
                data[18 + (i * 3) + 2] = rgba[i * 4];
            }

            return data;
        }

        private FullPath WriteScratch(string name, byte[] data)
        {
            var path = scratch / name;
            File.WriteAllBytes(path, data);
            return path;
        }

        /// <summary>Writes a file with <c>ktx create --raw</c>, one input per image (level, layer, face).</summary>
        private FullPath KtxRaw(string name, string format, int width, int height, byte[][] images, string[] options)
        {
            var file = output / (name + ".ktx2");
            var inputs = images.Select((image, i) => WriteScratch(string.Create(CultureInfo.InvariantCulture, $"{name}-{i}.raw"), image).Value).ToArray();
            Run([tools.Ktx, "create", "--testrun", "--raw", "--format", format, "--width", width.ToString(CultureInfo.InvariantCulture), "--height", height.ToString(CultureInfo.InvariantCulture), .. options, .. inputs, file.Value]);
            return file;
        }

        private void AddUncompressed(FullPath file, string description, (int Index, int Level, int Layer, int Face, int Slice, int Width, int Height, string Layout, byte[] Pixels)[] images, string[] selector)
        {
            var references = new List<Reference>();
            foreach (var (index, level, layer, face, slice, width, height, layout, pixels) in images)
            {
                // ktx extract --raw gives the stored texels back: the texels the reference is converted from
                var options = selector.Select(option => option.Replace("{level}", level.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{face}", face.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{layer}", layer.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Replace("{slice}", slice.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)).ToArray();
                var raw = scratch / "extract.raw";
                Run([tools.Ktx, "extract", "--raw", .. options, file.Value, raw.Value]);
                Py.Assert(File.Exists(raw), file.Name + ": ktx extract --raw writes the texels");
                references.Add(new Reference(index, level, layer, face, slice, width, height, layout, pixels, "The pattern given to ktx create --raw, converted by the rules of the format (the channels it lacks are 0, alpha 1)",
                    [Check("ktx extract --raw", "exact", "The stored texels the pattern was written from.")]));
                File.Delete(raw);
            }

            AddFile(file, description, [.. references]);
        }

        private byte[] ExtractPng(FullPath file, string[] options, int width, int height, bool sixteen)
        {
            var png = scratch / "extract.png";
            File.Delete(png);
            Run([tools.Ktx, "extract", .. options, file.Value, png.Value]);
            return DecodePng(png, width, height, sixteen);
        }

        private byte[] DecodePng(FullPath png, int width, int height, bool sixteen)
        {
            var bytes = Proc.Run(tools.FfmpegCmd(["-i", png.Value, "-f", "rawvideo", "-pix_fmt", sixteen ? "rgba64le" : "rgba", "-"]));
            Py.Assert(bytes.Length == width * height * (sixteen ? 8 : 4), png.Name + ": FFmpeg decodes the PNG file");
            return bytes;
        }

        private byte[] ImageIoDecode(FullPath file, int width, int height)
        {
            var png = scratch / "imageio.png";
            File.Delete(png);
            Run("sips", "-s", "format", "png", file, "--out", png);
            return DecodePng(png, width, height, sixteen: true);
        }

        /// <summary>Decodes the ASTC blocks of a file with astcenc: 8-bit samples for <c>-ds</c>, halves for <c>-dh</c>.</summary>
        private byte[] AstcencDecode(FullPath file, string size, string mode, int width, int height)
        {
            var blocks = scratch / "blocks.raw";
            File.Delete(blocks);
            Run(tools.Ktx, "extract", "--raw", file, blocks);
            var dimensions = size.Split('x').Select(value => byte.Parse(value, CultureInfo.InvariantCulture)).ToArray();
            byte[] header = [0x13, 0xAB, 0xA1, 0x5C, dimensions[0], dimensions[1], 1, (byte)width, (byte)(width >> 8), 0, (byte)height, (byte)(height >> 8), 0, 1, 0, 0];
            var astc = WriteScratch("decode.astc", [.. header, .. File.ReadAllBytes(blocks)]);
            var ktx = scratch / "decode.ktx";
            File.Delete(ktx);
            Run(tools.Astcenc, mode, astc, ktx, "-silent");
            return ReadKtx1(File.ReadAllBytes(ktx));
        }

        private byte[] BasisuUnpack(FullPath file, string suffix, int width, int height)
        {
            var directory = UnpackDirectory(file);
            return DecodePng(FullPath.FromPath(Directory.GetFiles(directory, "*_unpacked_" + suffix + ".png").Single()), width, height, sixteen: false);
        }

        private byte[] BasisuUnpackKtx1(FullPath file, string suffix)
        {
            var directory = UnpackDirectory(file);
            return ReadKtx1(File.ReadAllBytes(Directory.GetFiles(directory, "*_" + suffix + ".ktx").Order(StringComparer.Ordinal).First()));
        }

        private FullPath UnpackDirectory(FullPath file)
        {
            var directory = scratch / ("unpack-" + file.NameWithoutExtension);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
                File.Copy(file, directory / file.Name);
                Run(tools.Basisu, "-unpack", directory / file.Name, "-output_path", directory);
            }

            return directory;
        }

        /// <summary>
        /// The image of a KTX (version 1) file: the 64-byte header, the key/value data, then the length of the image and its
        /// bytes. An RGB image (GL_RGB, which astcenc writes when every alpha is 1) is expanded to RGBA with an alpha of 1; a
        /// compressed image is returned as it is.
        /// </summary>
        private static byte[] ReadKtx1(byte[] data)
        {
            var glType = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(16));
            var glFormat = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(24));
            var keyValueLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(60));
            var offset = 64 + keyValueLength;
            var length = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(offset));
            var image = data.AsSpan(offset + 4, length).ToArray();
            // A compressed image (glFormat 0) or RGBA samples are returned as they are
            if (glFormat != 0x1907)
            {
                Py.Assert(glFormat is 0 or 0x1908, "the KTX file is compressed, RGB or RGBA");
                return image;
            }

            // GL_HALF_FLOAT (0x140B) or GL_UNSIGNED_BYTE samples
            var sample = glType == 0x140B ? 2 : 1;
            byte[] one = sample == 2 ? [0x00, 0x3C] : [0xFF];
            var count = image.Length / (3 * sample);
            var rgba = new byte[count * 4 * sample];
            for (var i = 0; i < count; i++)
            {
                image.AsSpan(i * 3 * sample, 3 * sample).CopyTo(rgba.AsSpan(i * 4 * sample));
                one.CopyTo(rgba.AsSpan(((i * 4) + 3) * sample));
            }

            return rgba;
        }

        private static JsonObject Check(string decoder, string result, string notes) => new() { ["decoder"] = decoder, ["result"] = result, ["notes"] = notes };

        private static void Run(params object[] command) => Proc.Run([.. command.Select(part => part is FullPath path ? path.Value : (string)part)]);

        private void AddFile(FullPath file, string description, Reference[] references)
        {
            var entries = new JsonArray();
            foreach (var reference in references)
            {
                entries.Add(new JsonObject
                {
                    ["index"] = reference.Index,
                    ["level"] = reference.Level,
                    ["layer"] = reference.Layer,
                    ["face"] = reference.Face,
                    ["slice"] = reference.Slice,
                    ["width"] = reference.Width,
                    ["height"] = reference.Height,
                    ["layout"] = reference.Layout,
                    ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(reference.Pixels)),
                    ["reference"] = reference.Method,
                    ["crossChecks"] = reference.CrossChecks,
                });
            }

            Files.Add(new JsonObject
            {
                ["file"] = file.Name,
                ["sha256"] = Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(file))),
                ["description"] = description,
                ["entries"] = entries,
            });
        }
    }

    private sealed class Ktx2Tools : ToolSet
    {
        public Ktx2Tools(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Ktx = PrebuiltTools.Ktx.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Astcenc = PrebuiltTools.Astcenc.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            Basisu = PrebuiltTools.Basisu.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            KtxVersion = PrebuiltTools.Ktx.Version;
            AstcencVersion = PrebuiltTools.Astcenc.Version;
            BasisuVersion = PrebuiltTools.Basisu.Version;
            if ((KtxVersion != PinnedKtx || AstcencVersion != PinnedAstcenc || BasisuVersion != PinnedBasisu) && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: KTX-Software {KtxVersion} (pinned {PinnedKtx}), astcenc {AstcencVersion} (pinned {PinnedAstcenc}), basisu {BasisuVersion} (pinned {PinnedBasisu}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Ktx] = "ktx", [Astcenc] = "astcenc", [Basisu] = "basisu" };
        }

        public string Ktx { get; }

        public string Astcenc { get; }

        public string Basisu { get; }

        public string KtxVersion { get; }

        public string AstcencVersion { get; }

        public string BasisuVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }
    }
}
