using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Psd;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the PSD fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/psd, invalid/psd and
/// limit/psd entries). This generator is the reviewed, reproducible recipe of every committed PSD fixture. It is NEVER run
/// by the tests and does not use the library under test.
/// <para>
/// The files come from two independent writers of the pinned meziantou/prebuilt release, given hand-defined patterns:
/// <c>psd-tools write</c> (every color mode, depth and compression, merged alpha, layers in the layer info or in the
/// <c>Lr16</c>/<c>Lr32</c> blocks, PSB) and ImageMagick (<c>magick</c>: RGB, grayscale, bitmap; a layer, flat files, PSB),
/// and from <see cref="PsdFileBuilder"/> (test harness: the image resources, the malformed files). The reference is the
/// pattern, hand-computed into RGBA: a bitmap sample of 1 is black, an index is its color, gray is repeated, and the colors
/// of a composite with a transparency channel, stored matted over white as Photoshop stores them, are un-matted:
/// <c>round((s × max − max × (max − α)) / α)</c>, the stored samples where α is 0 (floats: <c>(s − (1 − α)) / α</c>).
/// </para>
/// <para>
/// Every file is read by <see cref="ReferencePsd"/>, an independent strict reader, which must give the pattern back; then
/// cross-checked with <c>psd-tools dump</c> (the stored samples, which must be the ones the file was written from), with
/// ImageMagick (which un-mattes the colors the same way, in floating point) and with the PSD decoder of FFmpeg. Their
/// disagreements are recorded with their measured errors.
/// </para>
/// It only replaces the PSD entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "psd") and their
/// files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- psd
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- psd --write
/// Requirements: FFmpeg, ImageMagick and psd-tools from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt
/// package. Any OS.
/// </summary>
internal static class PsdCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/PsdCorpus.cs";

    private const string PinnedFfmpeg = "9.0.2";
    private const string PinnedMagick = "7.1.2-35";
    private const string PinnedPsdTools = "1.26.1";

    private const int Rgb = 3;
    private const int Gray = 1;
    private const int Indexed = 2;
    private const int Bitmap = 0;
    private const int Cmyk = 4;
    private const int Duotone = 8;
    private const int Lab = 9;
    private const int Multichannel = 7;

    // What ImageMagick reads otherwise than the specification, measured on the fixtures: the generator fails when a declared
    // difference disappears or another one appears
    private static readonly Dictionary<string, string> MagickDifferences = new(StringComparer.Ordinal)
    {
        ["graya8-matted"] = "ImageMagick does not un-matte the colors of a grayscale composite with a transparency channel: it gives the stored samples, matted over white.",
        ["indexed-transparent"] = "ImageMagick does not read the transparency index resource (1047): every pixel is opaque.",
        ["rgb8-extra-channels"] = "ImageMagick takes the first extra channel for the transparency of the composite although the layer count is not negative (the file has two extra channels and no layer).",
        ["rgba16-matted-lr16"] = "ImageMagick un-mattes 16-bit colors in floating point and rounds them to its own 16-bit quantum: some samples differ by one unit.",
        ["rgba32-lr32"] = "ImageMagick un-mattes the floats in its own precision and clips them, where the reference does not.",
    };

    // What FFmpeg 9.0.2 does not read, measured on the fixtures
    private static readonly Dictionary<string, string> FfmpegDifferences = new(StringComparer.Ordinal)
    {
        ["gray32"] = "FFmpeg clips the floating-point samples to [0, 1].",
        ["graya8-matted"] = "FFmpeg does not un-matte the colors of a composite with a transparency channel.",
        ["indexed-transparent"] = "FFmpeg does not read the transparency index resource (1047).",
        ["rgba16-matted-lr16"] = "FFmpeg does not un-matte the colors of a composite with a transparency channel.",
        ["rgba8-matted"] = "FFmpeg does not un-matte the colors of a composite with a transparency channel.",
        ["rgba8-spot-channel"] = "FFmpeg does not un-matte the colors of a composite with a transparency channel.",
        ["magick-bitmap"] = "FFmpeg rejects the PackBits rows ImageMagick writes.",
        ["magick-gray16-rle"] = "FFmpeg rejects the PackBits rows ImageMagick writes.",
        ["magick-rgb8-rle"] = "FFmpeg rejects the PackBits rows ImageMagick writes.",
        ["magick-psb-gray8"] = "FFmpeg does not read PSB files.",
        ["psb-rgb8-rle"] = "FFmpeg does not read PSB files.",
        ["psb-rgba16-lr16"] = "FFmpeg does not read PSB files.",
        ["rgb32"] = "FFmpeg does not read 32-bit RGB documents.",
        ["rgba32-lr32"] = "FFmpeg does not read 32-bit RGB documents.",
    };

    private static bool IsPsdFixture(Obj fixture) => (string?)fixture["format"] == "psd";

    public static int Run(GeneratorOptions options)
    {
        var tools = new PsdToolSet(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-psd-corpus-", output => Generate(output, tools),
            (fixtures, files, bytes) => string.Create(CultureInfo.InvariantCulture, $"{fixtures} PSD fixtures; corpus: {files} files, {bytes} bytes"),
            "The committed PSD fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, PsdToolSet tools)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<PsdToolSet>(outDir, tools, ScriptPath))
        {
            BuildModes(corpus);
            BuildFloatingPoint(corpus);
            BuildTransparency(corpus);
            BuildLayers(corpus);
            BuildMagick(corpus);
            BuildLargeDocuments(corpus);
            BuildResources(corpus);
            BuildUnsupported(corpus);
            BuildInvalid(corpus);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsPsdFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsPsdFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Patterns: interleaved samples, every channel different, with the extremes, rows of equal samples (runs) and noise
    // -----------------------------------------------------------------------------------------------------------------

    private static int[] Samples(int width, int height, int channels, int max, int seed)
    {
        var samples = new int[width * height * channels];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                for (var c = 0; c < channels; c++)
                {
                    var i = (((y * width) + x) * channels) + c;
                    long value = y % 3 == 2
                        ? (c * 97 + y * 31 + seed * 7) % 256 * (max / 255)
                        : ((((long)x * 73) + (y * 151) + (c * 211) + (seed * 17)) * 2654435761L) >> 7;
                    samples[i] = (x + y + c) switch
                    {
                        0 => 0,
                        1 => max,
                        _ => (int)(Math.Abs(value) % (max + 1)),
                    };
                }
            }
        }

        return samples;
    }

    /// <summary>Alpha values over a pattern: 0, the maximum, and values in between.</summary>
    private static int[] Alphas(int width, int height, int max, int seed)
    {
        var alphas = new int[width * height];
        for (var i = 0; i < alphas.Length; i++)
        {
            alphas[i] = ((i + seed) % 7) switch
            {
                0 => 0,
                1 or 2 => max,
                _ => (int)((((long)i * 40503) + (seed * 977)) % (max + 1)),
            };
        }

        return alphas;
    }

    /// <summary>Blends colors over white as Photoshop stores the composite: <c>round((c × α + max × (max − α)) / max)</c>.</summary>
    private static int Matte(int color, int alpha, int max) => (int)((((long)color * alpha) + ((long)max * (max - alpha)) + (max / 2)) / max);

    private static float[] Floats(int width, int height, int channels, int seed)
    {
        var floats = new float[width * height * channels];
        for (var i = 0; i < floats.Length; i++)
        {
            var value = ((((long)i * 2654435761L) + (seed * 97)) >> 9) % 10_000 / 2_000.0f;
            floats[i] = (i % 9) switch
            {
                0 => 0f,
                1 => 1f,
                2 => 0.5f,
                3 => 4.25f,
                _ => value,
            };
        }

        return floats;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Writers
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A layer of a psd-tools description: its rectangle, its channels and its interleaved samples.</summary>
    private sealed record Layer(string Name, int Left, int Top, int Width, int Height, int[] Channels, byte[] Raw, string Compression);

    /// <summary>
    /// Writes a file with <c>psd-tools write</c> from a description (header.json, see the bundle): the composite is the
    /// interleaved samples in the raw layout of psd-tools (u8, u16 little-endian, f32 little-endian, a byte per bitmap sample).
    /// </summary>
    private static (byte[] Data, string Command) PsdToolsWrite(Corpus<PsdToolSet> c, string name, int width, int height, int depth, string mode, int channels, string compression,
        byte[]? composite, bool? hasComposite = true, bool mergedAlpha = false, string? layerBlock = null, IReadOnlyList<Layer>? layers = null,
        int[][]? palette = null, int? transparentIndex = null, int version = 1, byte[]? icc = null)
    {
        var folder = c.Scratch / ("psd-tools-" + name);
        Directory.CreateDirectory(folder);
        var description = new Obj
        {
            ["version"] = version,
            ["width"] = width,
            ["height"] = height,
            ["depth"] = depth,
            ["colorMode"] = mode,
            ["channels"] = channels,
            ["compression"] = compression,
            ["composite"] = composite is null ? null : "composite.raw",
            ["hasComposite"] = hasComposite,
            ["iccProfile"] = icc is null ? null : "icc.icc",
            ["palette"] = palette?.Select(entry => (object?)entry.Select(v => (object?)v).ToList()).ToList(),
            ["transparentIndex"] = transparentIndex,
            ["mergedAlpha"] = mergedAlpha,
            ["layerBlock"] = layers is { Count: > 0 } ? layerBlock ?? "layerInfo" : null,
            ["layers"] = (layers ?? []).Select((layer, i) => (object?)new Obj
            {
                ["name"] = layer.Name,
                ["top"] = layer.Top,
                ["left"] = layer.Left,
                ["bottom"] = layer.Top + layer.Height,
                ["right"] = layer.Left + layer.Width,
                ["opacity"] = 255,
                ["visible"] = true,
                ["blendMode"] = "norm",
                ["clipping"] = 0,
                ["channels"] = layer.Channels.Select(v => (object?)v).ToList(),
                ["compression"] = layer.Compression,
                ["file"] = $"layer-{i}.raw",
                ["mask"] = null,
            }).ToList(),
        };
        if (composite is not null)
            File.WriteAllBytes(folder / "composite.raw", composite);
        if (icc is not null)
            File.WriteAllBytes(folder / "icc.icc", icc);
        for (var i = 0; i < (layers?.Count ?? 0); i++)
            File.WriteAllBytes(folder / $"layer-{i}.raw", layers![i].Raw);

        File.WriteAllText(folder / "header.json", PyJson.Dumps(description));
        var output = c.Scratch / (name + (version == 2 ? ".psb" : ".psd"));
        List<string> command = [c.Tools.PsdTools, "write", (folder / "header.json").Value, output.Value];
        Proc.Run(command);

        // psd-tools gives the samples it was given back
        if (composite is not null && compression is "RAW" or "RLE")
        {
            var dump = c.Scratch / ("psd-tools-" + name + "-dump");
            if (Directory.Exists(dump))
                Directory.Delete(dump, recursive: true);

            Proc.Run([c.Tools.PsdTools, "dump", "--no-layers", output.Value, dump.Value]);
            Py.Assert(File.ReadAllBytes(dump / "composite.raw").AsSpan().SequenceEqual(composite), name + ": psd-tools dumps the samples it was given");
        }

        return (File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [((folder / "header.json").Value, "{description}.json"), (output.Value, "{output}")]));
    }

    /// <summary>Writes a file with ImageMagick from raw samples (<c>-endian MSB</c> after the inputs: see the verify script of the prebuilt release).</summary>
    private static (byte[] Data, string Command) MagickWrite(Corpus<PsdToolSet> c, string name, string input, byte[] raw, IEnumerable<string> before, IEnumerable<string> after, string extension = ".psd")
    {
        var source = c.Scratch / (name + ".src.raw");
        File.WriteAllBytes(source, raw);
        var output = c.Scratch / (name + extension);
        List<string> command = [c.Tools.Magick, .. before, input + ":" + source.Value, "-endian", "MSB", .. after, (extension == ".psb" ? "PSB:" : "") + output.Value];
        Proc.Run(command);
        return (File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [(source.Value, "{source}.raw"), (output.Value, "{output}" + extension)]));
    }

    private static byte[] RawBytes(int[] samples, int depth)
    {
        var raw = new byte[samples.Length * (depth == 16 ? 2 : 1)];
        for (var i = 0; i < samples.Length; i++)
        {
            if (depth == 16)
                BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(i * 2), (ushort)samples[i]);
            else
                raw[i] = (byte)samples[i];
        }

        return raw;
    }

    private static byte[] RawFloats(float[] samples)
    {
        var raw = new byte[samples.Length * 4];
        for (var i = 0; i < samples.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(raw.AsSpan(i * 4), samples[i]);
        return raw;
    }

    /// <summary>The samples of a pattern as psd-tools stores them: big-endian planes.</summary>
    private static byte[][] Planes(int[] samples, int channels, int depth)
    {
        if (depth == 16)
            return PsdFileBuilder.Planes16([.. samples.Select(v => (ushort)v)], channels);

        return PsdFileBuilder.Planes8([.. samples.Select(v => (byte)v)], channels);
    }

    private static int[][] Palette(int seed) => [.. Enumerable.Range(0, 256).Select(i => new[] { (i * 7 + seed) % 256, (i * 13 + 50) % 256, (255 - i + seed) % 256 })];

    // -----------------------------------------------------------------------------------------------------------------
    // Valid fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildModes(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildModes);
        foreach (var compression in new[] { "RAW", "RLE" })
        {
            var suffix = compression == "RAW" ? "raw" : "rle";
            var rgb = Samples(13, 7, 3, 255, 1);
            var (data, command) = PsdToolsWrite(c, "rgb8-" + suffix, 13, 7, 8, "RGB", 3, compression, RawBytes(rgb, 8));
            AddValid(c, "rgb8-" + suffix, Function, data, command, $"A 13x7 RGB pattern of 8-bit samples, {Describe(compression)} (psd-tools).");

            var gray = Samples(11, 6, 1, 255, 2);
            (data, command) = PsdToolsWrite(c, "gray8-" + suffix, 11, 6, 8, "GRAYSCALE", 1, compression, RawBytes(gray, 8));
            AddValid(c, "gray8-" + suffix, Function, data, command, $"An 11x6 grayscale pattern of 8-bit samples, {Describe(compression)} (psd-tools).");

            var rgb16 = Samples(9, 5, 3, 65535, 3);
            (data, command) = PsdToolsWrite(c, "rgb16-" + suffix, 9, 5, 16, "RGB", 3, compression, RawBytes(rgb16, 16));
            AddValid(c, "rgb16-" + suffix, Function, data, command, $"A 9x5 RGB pattern of 16-bit samples, {Describe(compression)} (psd-tools).");
        }

        var gray16 = Samples(10, 4, 1, 65535, 4);
        var (gray16Data, gray16Command) = PsdToolsWrite(c, "gray16-rle", 10, 4, 16, "GRAYSCALE", 1, "RLE", RawBytes(gray16, 16));
        AddValid(c, "gray16-rle", Function, gray16Data, gray16Command, "A 10x4 grayscale pattern of 16-bit samples, PackBits rows (psd-tools).");

        // Bitmap rows of 13 pixels: two bytes, the second one padded with 0 bits (psd-tools)
        foreach (var compression in new[] { "RAW", "RLE" })
        {
            var bits = Enumerable.Range(0, 13 * 5).Select(i => (byte)((i * 7 / 3 + i / 13) % 2)).ToArray();
            var (data, command) = PsdToolsWrite(c, "bitmap-" + compression.ToLowerInvariant(), 13, 5, 1, "BITMAP", 1, compression, bits);
            AddValid(c, "bitmap-" + compression.ToLowerInvariant(), Function, data, command, $"A 13x5 bitmap pattern (1 is black), {Describe(compression)}, rows padded with 0 bits (psd-tools).");
        }

        // Indexed: the indexes of a pattern and a table of 256 colors, with and without a transparent index
        var indexes = Samples(12, 6, 1, 255, 5);
        var (indexedData, indexedCommand) = PsdToolsWrite(c, "indexed-rle", 12, 6, 8, "INDEXED", 1, "RLE", RawBytes(indexes, 8), palette: Palette(1));
        AddValid(c, "indexed-rle", Function, indexedData, indexedCommand, "A 12x6 pattern of indexes into a table of 256 colors, PackBits rows (psd-tools).");
        (indexedData, indexedCommand) = PsdToolsWrite(c, "indexed-transparent", 12, 6, 8, "INDEXED", 1, "RAW", RawBytes(indexes, 8), palette: Palette(2), transparentIndex: indexes[5]);
        AddValid(c, "indexed-transparent", Function, indexedData, indexedCommand, "The indexes of psd/indexed-rle, raw, with a transparency index resource (1047): the pixels of that index are transparent (psd-tools).");

        // Duotone: read as its grayscale samples (psd-tools writes an empty duotone specification)
        var duotone = Samples(8, 5, 1, 255, 6);
        var (duotoneData, duotoneCommand) = PsdToolsWrite(c, "duotone8", 8, 5, 8, "DUOTONE", 1, "RLE", RawBytes(duotone, 8));
        AddValid(c, "duotone8", Function, duotoneData, duotoneCommand, "An 8x5 duotone document, read as its grayscale samples (the duotone specification of the color mode data is not applied) (psd-tools).");
    }

    private static void BuildFloatingPoint(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildFloatingPoint);
        var rgb = Floats(7, 4, 3, 1);
        var (data, command) = PsdToolsWrite(c, "rgb32", 7, 4, 32, "RGB", 3, "RLE", RawFloats(rgb));
        AddValid(c, "rgb32", Function, data, command, "A 7x4 RGB pattern of 32-bit floating-point samples (values from 0 to 4.25, linear light), PackBits rows of their bytes (psd-tools).");

        var gray = Floats(6, 3, 1, 2);
        (data, command) = PsdToolsWrite(c, "gray32", 6, 3, 32, "GRAYSCALE", 1, "RAW", RawFloats(gray));
        AddValid(c, "gray32", Function, data, command, "A 6x3 grayscale pattern of 32-bit floating-point samples, raw (psd-tools).");

        // RGBA in the Lr32 block, as Photoshop writes 32-bit layers; alpha in [0, 1], colors matted over white
        var colors = Floats(5, 4, 3, 3).Select(v => Math.Min(v, 1f)).ToArray();
        var alphas = new[] { 0f, 1f, 0.5f, 0.25f, 0.75f, 1f, 0.125f, 1f, 0f, 0.625f, 1f, 0.5f, 0.375f, 1f, 1f, 0.875f, 0.5f, 0.0625f, 1f, 0.25f };
        var rgba = new float[5 * 4 * 4];
        for (var i = 0; i < 20; i++)
        {
            for (var k = 0; k < 3; k++)
                rgba[(i * 4) + k] = (colors[(i * 3) + k] * alphas[i]) + (1 - alphas[i]);
            rgba[(i * 4) + 3] = alphas[i];
        }

        var layer = new Layer("Layer 1", 0, 0, 5, 4, [0, 1, 2, -1], RawFloats(rgba), "RLE");
        (data, command) = PsdToolsWrite(c, "rgba32-lr32", 5, 4, 32, "RGB", 4, "RLE", RawFloats(rgba), mergedAlpha: true, layerBlock: "Lr32", layers: [layer]);
        AddValid(c, "rgba32-lr32", Function, data, command, "A 5x4 RGB pattern of 32-bit floats with a transparency channel, the layer count in the Lr32 block; the colors are matted over white (psd-tools).", ["psd.alpha=merged"]);
    }

    private static void BuildTransparency(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildTransparency);
        foreach (var (depth, block) in new[] { (8, "layerInfo"), (16, "Lr16") })
        {
            var max = depth == 16 ? 65535 : 255;
            var colors = Samples(9, 6, 3, max, 7 + depth);
            var alphas = Alphas(9, 6, max, depth);
            var stored = new int[9 * 6 * 4];
            for (var i = 0; i < alphas.Length; i++)
            {
                for (var k = 0; k < 3; k++)
                    stored[(i * 4) + k] = Matte(colors[(i * 3) + k], alphas[i], max);
                stored[(i * 4) + 3] = alphas[i];
            }

            var raw = RawBytes(stored, depth);
            var layer = new Layer("Layer 1", 0, 0, 9, 6, [0, 1, 2, -1], raw, "RLE");
            var name = depth == 8 ? "rgba8-matted" : "rgba16-matted-lr16";
            var (data, command) = PsdToolsWrite(c, name, 9, 6, depth, "RGB", 4, "RLE", raw, mergedAlpha: true, layerBlock: block, layers: [layer]);
            AddValid(c, name, Function, data, command,
                string.Create(CultureInfo.InvariantCulture, $"A 9x6 RGB pattern of {depth}-bit samples with a transparency channel (a negative layer count in the {(block == "Lr16" ? "Lr16 block" : "layer info")}), its colors matted over white as Photoshop stores the composite (psd-tools); alpha values of 0, the maximum and in between."),
                ["psd.alpha=merged"]);
        }

        // Gray with alpha, 8 bits
        {
            var gray = Samples(7, 5, 1, 255, 9);
            var alphas = Alphas(7, 5, 255, 3);
            var stored = new int[7 * 5 * 2];
            for (var i = 0; i < alphas.Length; i++)
            {
                stored[i * 2] = Matte(gray[i], alphas[i], 255);
                stored[(i * 2) + 1] = alphas[i];
            }

            var raw = RawBytes(stored, 8);
            var layer = new Layer("Layer 1", 0, 0, 7, 5, [0, -1], raw, "RAW");
            var (data, command) = PsdToolsWrite(c, "graya8-matted", 7, 5, 8, "GRAYSCALE", 2, "RAW", raw, mergedAlpha: true, layers: [layer]);
            AddValid(c, "graya8-matted", Function, data, command, "A 7x5 grayscale pattern with a transparency channel, matted over white, raw (psd-tools).", ["psd.alpha=merged"]);
        }

        // Extra channels without the transparency flag: saved selections, not part of the composite
        {
            var samples = Samples(8, 4, 5, 255, 11);
            var (data, command) = PsdToolsWrite(c, "rgb8-extra-channels", 8, 4, 8, "RGB", 5, "RLE", RawBytes(samples, 8));
            AddValid(c, "rgb8-extra-channels", Function, data, command, "An 8x4 RGB pattern with two more channels and a layer count that is not negative: the extra channels (saved selections) are not part of the composite, which is opaque (psd-tools).", ["psd.extraChannels"]);
        }

        // The transparency channel followed by a spot channel
        {
            var colors = Samples(6, 4, 3, 255, 12);
            var alphas = Alphas(6, 4, 255, 5);
            var spot = Samples(6, 4, 1, 255, 13);
            var stored = new int[6 * 4 * 5];
            for (var i = 0; i < alphas.Length; i++)
            {
                for (var k = 0; k < 3; k++)
                    stored[(i * 5) + k] = Matte(colors[(i * 3) + k], alphas[i], 255);
                stored[(i * 5) + 3] = alphas[i];
                stored[(i * 5) + 4] = spot[i];
            }

            var layerRaw = new byte[6 * 4 * 4];
            for (var i = 0; i < 24; i++)
            {
                for (var k = 0; k < 4; k++)
                    layerRaw[(i * 4) + k] = (byte)stored[(i * 5) + k];
            }

            var layer = new Layer("Layer 1", 0, 0, 6, 4, [0, 1, 2, -1], layerRaw, "RLE");
            var (data, command) = PsdToolsWrite(c, "rgba8-spot-channel", 6, 4, 8, "RGB", 5, "RLE", RawBytes(stored, 8), mergedAlpha: true, layers: [layer]);
            AddValid(c, "rgba8-spot-channel", Function, data, command, "A 6x4 RGB pattern with a transparency channel followed by another channel: the first extra channel is the transparency, the next one is skipped (psd-tools).", ["psd.alpha=merged", "psd.extraChannels"]);
        }
    }

    private static void BuildLayers(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildLayers);

        // Two layers, ZIP-compressed (ZIP is only read in the layers, which are skipped), and a raw composite
        var composite = Samples(10, 6, 3, 255, 21);
        var top = Samples(4, 3, 4, 255, 22);
        var bottom = Samples(10, 6, 3, 255, 23);
        Layer[] layers =
        [
            new("Background", 0, 0, 10, 6, [0, 1, 2], RawBytes(bottom, 8), "ZIP"),
            new("Top", 3, 2, 4, 3, [-1, 0, 1, 2], RawBytes(top, 8), "ZIP_WITH_PREDICTION"),
        ];
        var (data, command) = PsdToolsWrite(c, "layers-zip", 10, 6, 8, "RGB", 3, "RAW", RawBytes(composite, 8), layers: layers);
        AddValid(c, "layers-zip", Function, data, command, "A 10x6 RGB composite after two layers (a background and a smaller layer with transparency) whose channels are ZIP and ZIP-with-prediction compressed: the layers are skipped, the composite is read (psd-tools).", ["psd.layers=layerInfo"]);

        // 16-bit layers in the Lr16 block, after an empty layer info and global layer mask
        var composite16 = Samples(6, 5, 3, 65535, 24);
        Layer[] layers16 = [new("Layer 1", 1, 1, 3, 2, [0, 1, 2], RawBytes(Samples(3, 2, 3, 65535, 25), 16), "RLE")];
        (data, command) = PsdToolsWrite(c, "rgb16-layers-lr16", 6, 5, 16, "RGB", 3, "RLE", RawBytes(composite16, 16), layerBlock: "Lr16", layers: layers16);
        AddValid(c, "rgb16-layers-lr16", Function, data, command, "A 6x5 RGB composite of 16-bit samples after a layer in the Lr16 block, as Photoshop writes 16-bit documents (psd-tools).", ["psd.layers=lr16"]);
    }

    private static void BuildMagick(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildMagick);
        var rgb = Samples(14, 6, 3, 255, 31);
        var (data, command) = MagickWrite(c, "magick-rgb8-rle", "rgb", RawBytes(rgb, 8), ["-size", "14x6", "-depth", "8"], ["-compress", "RLE"]);
        AddValid(c, "magick-rgb8-rle", Function, data, command, "A 14x6 RGB pattern written by ImageMagick as the composite and as one layer, PackBits rows.", ["psd.layers=layerInfo"], writer: "magick");

        (data, command) = MagickWrite(c, "magick-rgb8-flat", "rgb", RawBytes(rgb, 8), ["-size", "14x6", "-depth", "8"], ["-define", "psd:write-layers=false"]);
        AddValid(c, "magick-rgb8-flat", Function, data, command, "The pattern of psd/magick-rgb8-rle written by ImageMagick without layers (an empty layer and mask information section), raw.", writer: "magick");

        var gray16 = Samples(9, 7, 1, 65535, 32);
        (data, command) = MagickWrite(c, "magick-gray16-rle", "gray", RawBytes(gray16, 16), ["-size", "9x7", "-depth", "16", "-endian", "LSB"], ["-compress", "RLE"]);
        AddValid(c, "magick-gray16-rle", Function, data, command, "A 9x7 grayscale pattern of 16-bit samples written by ImageMagick, PackBits rows.", ["psd.layers=layerInfo"], writer: "magick");

        // Bitmap rows padded with 1 bits (ImageMagick), where psd-tools pads with 0 bits
        var bits = Enumerable.Range(0, 11 * 4).Select(i => (byte)((i * 5 / 2 + i / 11) % 2 == 0 ? 255 : 0)).ToArray();
        (data, command) = MagickWrite(c, "magick-bitmap", "gray", bits, ["-size", "11x4", "-depth", "8"], ["-type", "Bilevel", "-depth", "1", "-define", "psd:write-layers=false", "-compress", "RLE"]);
        AddValid(c, "magick-bitmap", Function, data, command, "An 11x4 bilevel pattern written by ImageMagick as a bitmap document: rows padded with 1 bits, PackBits rows.", writer: "magick");
    }

    private static void BuildLargeDocuments(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildLargeDocuments);
        var rgb = Samples(12, 5, 3, 255, 41);
        var (data, command) = PsdToolsWrite(c, "psb-rgb8-rle", 12, 5, 8, "RGB", 3, "RLE", RawBytes(rgb, 8), version: 2);
        AddValid(c, "psb-rgb8-rle", Function, data, command, "A 12x5 RGB pattern in a large document (PSB, version 2): 8-byte section lengths and 4-byte row counts (psd-tools).");

        var rgba16 = Samples(5, 4, 4, 65535, 42);
        for (var i = 0; i < 20; i++)
            rgba16[(i * 4) + 3] = 65535;
        Layer[] layers = [new("Layer 1", 0, 0, 5, 4, [0, 1, 2, -1], RawBytes(rgba16, 16), "RLE")];
        (data, command) = PsdToolsWrite(c, "psb-rgba16-lr16", 5, 4, 16, "RGB", 4, "RLE", RawBytes(rgba16, 16), mergedAlpha: true, layerBlock: "Lr16", layers: layers, version: 2);
        AddValid(c, "psb-rgba16-lr16", Function, data, command, "A 5x4 opaque RGBA pattern of 16-bit samples in a PSB file, the layer count in an Lr16 block of 8-byte length (psd-tools).", ["psd.alpha=merged", "psd.layers=lr16"]);

        var gray = Samples(9, 4, 1, 255, 43);
        (data, command) = MagickWrite(c, "magick-psb-gray8", "gray", RawBytes(gray, 8), ["-size", "9x4", "-depth", "8"], ["-compress", "RLE"], ".psb");
        AddValid(c, "magick-psb-gray8", Function, data, command, "A 9x4 grayscale pattern written by ImageMagick as a PSB file, with a layer, PackBits rows.", ["psd.layers=layerInfo"], writer: "magick");
    }

    private static void BuildResources(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildResources);
        var rgb = Samples(6, 4, 3, 255, 51);
        var xmp = Bytes.Utf8("<?xpacket begin=\"\uFEFF\" id=\"W5M0MpCehiHzreSzNTczkc9d\"?><x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF xmlns:rdf=\"http://www.w3.org/1999/02/22-rdf-syntax-ns#\"><rdf:Description rdf:about=\"\" xmlns:xmp=\"http://ns.adobe.com/xap/1.0/\" xmp:CreatorTool=\"Meziantou.Framework.Imaging corpus\"/></rdf:RDF></x:xmpmeta><?xpacket end=\"r\"?>");
        var builder = new PsdFileBuilder(6, 4, 8, Rgb, Planes(rgb, 3, 8)) { Compression = 1 };
        builder.AddResolution(300 << 16, 150 << 16)
            .AddResource(1039, CommonCorpus.IccProfile("RGB "))
            .AddResource(1058, CommonCorpus.ExifTiff(littleEndian: false, orientation: 1, software: "corpus"))
            .AddResource(1060, xmp)
            .AddResource(1036, new byte[33], "thumbnail")
            .AddResource(4000, [1, 2, 3])
            .AddVersionInfo(hasRealMergedData: true);
        AddValid(c, "resources", Function, builder.Build(), null, "A 6x4 RGB pattern after image resources: the resolution (300 by 150 pixels per inch), an ICC profile, EXIF data, XMP metadata, a thumbnail resource (skipped), a plug-in resource of odd length and the version information (assembled from the specification).",
            ["psd.resources"], writer: "builder", iccLabel: "rgb");

        var gray = Samples(5, 3, 1, 255, 52);
        var grayBuilder = new PsdFileBuilder(5, 3, 8, Gray, Planes(gray, 1, 8));
        grayBuilder.AddResource(1039, CommonCorpus.IccProfile("GRAY"), "ICC Profile")
            .AddResource(1005, [0, 72, 0, 0, 0, 1, 0, 1, 0, 72, 0, 0, 0, 1, 0, 1], signature: "MeSa")
            .AddResolution(72 << 16, 72 << 16, unit: 2);
        AddValid(c, "resources-gray-icc", Function, grayBuilder.Build(), null, "A 5x3 grayscale pattern with a grayscale ICC profile (a resource with a name), a resource of another signature (MeSa, not read) and the resolution in a display unit of centimeters (stored in pixels per inch) (assembled from the specification).",
            ["psd.resources"], writer: "builder", iccLabel: "gray");
    }

    private static string Describe(string compression) => compression == "RAW" ? "raw" : "PackBits rows";

    private static void AddValid(Corpus<PsdToolSet> c, string name, string function, byte[] data, string? command, string description, IList<string>? extraFeatures = null, string writer = "psd-tools", string iccLabel = "none")
    {
        var fixtureId = "psd/" + name;
        PsdPicture picture;
        try
        {
            picture = ReferencePsd.Read(data);
        }
        catch (InvalidDataException error)
        {
            throw new FatalException(fixtureId + ": the reference reader rejects the file: " + error.Message);
        }

        var path = c.Scratch / (name + (picture.Version == 2 ? ".psb" : ".psd"));
        File.WriteAllBytes(path, data);
        var checks = new List<object?> { PsdToolsDumpCheck(c, path, picture) };

        var hasAlpha = picture.HasMergedAlpha || picture.TransparentIndex is not null;
        var isGray = picture.Mode is Bitmap or Gray or Duotone;
        var features = new List<string>
        {
            "psd.mode=" + ModeName(picture.Mode),
            "psd.depth=" + picture.Depth.ToString(CultureInfo.InvariantCulture),
            "psd.compression=" + (picture.Compression == 0 ? "raw" : "rle"),
            "psd.writer=" + writer,
        };
        if (picture.Version == 2)
            features.Add("psd.version=psb");
        features.AddRange(extraFeatures ?? []);
        features = [.. features.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        var tools = new List<string>();
        if (writer == "psd-tools")
            tools.Add($"psd-tools {c.Tools.PsdToolsVersion} (writer)");
        else
            tools.Add($"psd-tools {c.Tools.PsdToolsVersion} (dump, cross-check)");
        tools.Add(writer == "magick" ? $"ImageMagick {c.Tools.MagickVersion} (writer, cross-check)" : $"ImageMagick {c.Tools.MagickVersion} (cross-check)");
        tools.Add($"{c.Tools.FfmpegLabel} (cross-check)");
        tools.Add(ToolSet.RuntimeLabel);
        var origin = writer == "builder" ? "hand-authored" : "generated";
        var provenance = c.Provenance(origin, function, new Obj { ["width"] = picture.Width, ["height"] = picture.Height }, tools, command is null ? null : [command]);
        var referenceDescription = description + " The reference is the pattern: a bitmap sample of 1 is black, an index is its color, gray is repeated, and colors stored matted over white are un-matted, round((s × max − max × (max − α)) / α) (floats: (s − (1 − α)) / α), the stored samples where α is 0.";

        if (picture.Depth == 32)
        {
            var floats = picture.GetRgbaFloat();
            checks.Add(Declare(name, MagickFloatCheck(c, path, picture, floats), MagickDifferences));
            checks.Add(Declare(name, FfmpegCheck(c, path, picture, null, floats), FfmpegDifferences));
            AddFloatFixture(c, fixtureId, data, picture, floats, provenance, features, referenceDescription, checks, hasAlpha);
            return;
        }

        var rgba = picture.GetRgba();
        checks.Add(Declare(name, MagickCheck(c, path, picture, rgba), MagickDifferences));
        checks.Add(Declare(name, FfmpegCheck(c, path, picture, rgba, null), FfmpegDifferences));
        var depth = picture.Depth == 16 ? 16 : 8;
        var model = (isGray, hasAlpha) switch
        {
            (true, false) => "gray",
            (true, true) => "graya",
            (false, false) => "rgb",
            (false, true) => "rgba",
        };
        var pixels = new List<Px>();
        for (var i = 0; i < picture.Width * picture.Height; i++)
        {
            var p = rgba.AsSpan(i * 4, 4);
            pixels.Add(model switch
            {
                "gray" => new Px(p[0]),
                "graya" => new Px(p[0], p[3]),
                "rgb" => new Px(p[0], p[1], p[2]),
                _ => new Px(p[0], p[1], p[2], p[3]),
            });
        }

        var image = new Img(picture.Width, picture.Height, model, depth, pixels);
        var pixelFormat = (isGray, hasAlpha, depth) switch
        {
            (true, false, 8) => "Gray8",
            (true, false, _) => "Gray16",
            (true, true, 8) => "La16",
            (true, true, _) => "La32",
            (false, false, 8) => "Rgb24",
            (false, false, _) => "Rgb48",
            (false, true, 8) => "Rgba32",
            _ => "Rgba64",
        };
        var colorModel = picture.Mode == Indexed ? "Indexed" : (isGray, hasAlpha) switch
        {
            (true, false) => "Grayscale",
            (true, true) => "GrayscaleAlpha",
            (false, false) => "Rgb",
            _ => "Rgba",
        };
        var expected = CommonCorpus.StillExpected("psd", image, pixelFormat, colorModel, picture.Depth);
        expected["iccProfile"] = iccLabel;
        var native = (model, depth) switch
        {
            ("gray", 8) => "gray8",
            ("gray", 16) => "gray16le",
            ("rgb", 8) => "rgb8",
            _ => null,
        };
        List<string> layouts = native is null ? [CommonCorpus.CanonicalLayout(image)] : [CommonCorpus.CanonicalLayout(image), native];
        c.AddValid(fixtureId, fixtureId + (picture.Version == 2 ? ".psb" : ".psd"), data, [image], expected, provenance,
            new Obj { ["method"] = "hand-computed", ["description"] = referenceDescription, ["crossChecks"] = checks }, features, layouts: layouts);
    }

    private static void AddFloatFixture(Corpus<PsdToolSet> c, string fixtureId, byte[] data, PsdPicture picture, float[] floats, Obj provenance, List<string> features, string description, List<object?> checks, bool hasAlpha)
    {
        var expected = new Obj
        {
            ["width"] = picture.Width,
            ["height"] = picture.Height,
            ["pixelFormat"] = picture.ColorChannelCount == 1 ? (hasAlpha ? "LaFloat" : "GrayFloat") : "RgbaFloat",
            ["colorModel"] = picture.ColorChannelCount == 1 ? (hasAlpha ? "GrayscaleAlpha" : "Grayscale") : (hasAlpha ? "Rgba" : "Rgb"),
            ["bitsPerComponent"] = 32,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
            ["transferFunction"] = "linear",
            ["animation"] = null,
        };
        foreach (var (key, value) in (IEnumerable<KeyValuePair<string, object?>>)CommonCorpus.MetadataExpectation("psd", data, 1, "none"))
            expected[key] = value;

        var bytes = new byte[floats.Length * 4];
        for (var i = 0; i < floats.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * 4), floats[i]);
        var buffer = c.Write(fixtureId + ".frame-0.rgbaf32le.raw", bytes);
        buffer["layout"] = "rgbaf32le";
        buffer["width"] = picture.Width;
        buffer["height"] = picture.Height;
        buffer["rowBytes"] = picture.Width * 16;
        buffer["byteLength"] = bytes.Length;
        buffer["alpha"] = "straight";
        buffer["rowOrder"] = "top-down";
        buffer["compression"] = "none";
        expected["frameCount"] = 1;
        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = new List<object?> { buffer } } };
        expected["poster"] = null;

        var input = c.Write(fixtureId + ".psd", data);
        input["role"] = "encoded";
        c.Fixtures.Add(new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "psd",
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = features,
            ["reference"] = new Obj { ["method"] = "hand-computed", ["description"] = description, ["crossChecks"] = checks },
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        });
    }

    private static string ModeName(int mode) => mode switch
    {
        Bitmap => "bitmap",
        Gray => "grayscale",
        Indexed => "indexed",
        Rgb => "rgb",
        Cmyk => "cmyk",
        Multichannel => "multichannel",
        Duotone => "duotone",
        Lab => "lab",
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };

    // -----------------------------------------------------------------------------------------------------------------
    // Cross-checks
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The stored samples <c>psd-tools dump</c> gives must be those <see cref="ReferencePsd"/> reads.</summary>
    private static Obj PsdToolsDumpCheck(Corpus<PsdToolSet> c, FullPath path, PsdPicture picture)
    {
        var folder = c.Scratch / ("dump-" + path.NameWithoutExtension);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);

        List<string> command = [c.Tools.PsdTools, "dump", "--no-layers", path.Value, folder.Value];
        Proc.Run(command);
        var composite = File.ReadAllBytes(folder / "composite.raw");
        var count = picture.Width * picture.Height;
        var differing = 0;
        for (var i = 0; i < count; i++)
        {
            for (var channel = 0; channel < picture.ChannelCount; channel++)
            {
                int stored;
                int dumped;
                switch (picture.Depth)
                {
                    case 1:
                        stored = (picture.Channels[channel][((i / picture.Width) * picture.RowLength) + ((i % picture.Width) / 8)] >> (7 - (i % picture.Width % 8))) & 1;
                        dumped = composite[(i * picture.ChannelCount) + channel];
                        break;
                    case 8:
                        stored = picture.Channels[channel][i];
                        dumped = composite[(i * picture.ChannelCount) + channel];
                        break;
                    case 16:
                        stored = BinaryPrimitives.ReadUInt16BigEndian(picture.Channels[channel].AsSpan(i * 2));
                        dumped = BinaryPrimitives.ReadUInt16LittleEndian(composite.AsSpan(((i * picture.ChannelCount) + channel) * 2));
                        break;
                    default:
                        stored = BinaryPrimitives.ReadInt32BigEndian(picture.Channels[channel].AsSpan(i * 4));
                        dumped = BinaryPrimitives.ReadInt32LittleEndian(composite.AsSpan(((i * picture.ChannelCount) + channel) * 4));
                        break;
                }

                if (stored != dumped)
                    differing++;
            }
        }

        Py.Assert(differing == 0, string.Create(CultureInfo.InvariantCulture, $"{path}: psd-tools dumps other stored samples ({differing} differ)"));
        return new Obj
        {
            ["decoder"] = $"psd-tools {c.Tools.PsdToolsVersion} (dump: the stored samples of the composite)",
            ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}"), (folder.Value, "{output}")]),
            ["result"] = "exact",
            ["maxAbsoluteError"] = 0,
            ["meanAbsoluteError"] = 0.0,
            ["excludedFromTolerance"] = true,
            ["notes"] = "psd-tools gives the stored samples of every channel, before the conversion to RGBA: they are the ones the reference reader reads, bit for bit.",
        };
    }

    /// <summary>Decodes the composite with ImageMagick (<c>[0]</c>), which un-mattes the colors of a transparency channel in floating point.</summary>
    private static Obj MagickCheck(Corpus<PsdToolSet> c, FullPath path, PsdPicture picture, int[] rgba)
    {
        var depth = picture.Depth == 16 ? 16 : 8;
        List<string> command = [c.Tools.Magick, path.Value + "[0]", "-depth", depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", "rgba:-"];
        var output = Proc.Run(command);
        Py.Assert(output.Length == rgba.Length * (depth / 8), $"{path}: magick gives every sample");
        var (max, sum) = Compare(rgba, i => depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(output.AsSpan(i * 2)) : output[i]);
        return Check($"ImageMagick {c.Tools.MagickVersion} (PSD coder)", CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]), max, sum / rgba.Length, depth);
    }

    private static Obj MagickFloatCheck(Corpus<PsdToolSet> c, FullPath path, PsdPicture picture, float[] floats)
    {
        List<string> command = [c.Tools.Magick, path.Value + "[0]", "-depth", "32", "-define", "quantum:format=floating-point", "-endian", "LSB", "rgba:-"];
        var output = Proc.Run(command);
        Py.Assert(output.Length == floats.Length * 4, $"{path}: magick gives every sample");
        double max = 0;
        double sum = 0;
        for (var i = 0; i < floats.Length; i++)
        {
            var error = Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(output.AsSpan(i * 4)) - floats[i]);
            max = Math.Max(max, error);
            sum += error;
        }

        _ = picture;
        return new Obj
        {
            ["decoder"] = $"ImageMagick {c.Tools.MagickVersion} (PSD coder, HDRI)",
            ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]),
            ["result"] = max == 0 ? "exact" : "differs",
            ["maxAbsoluteError"] = 0,
            ["meanAbsoluteError"] = 0.0,
            ["excludedFromTolerance"] = true,
            ["notes"] = max == 0
                ? "ImageMagick gives the same floats, bit for bit."
                : string.Create(CultureInfo.InvariantCulture, $"The floats differ by at most {max:G6} (mean {sum / floats.Length:G6})."),
        };
    }

    /// <summary>Decodes the composite with the PSD decoder of FFmpeg.</summary>
    private static Obj FfmpegCheck(Corpus<PsdToolSet> c, FullPath path, PsdPicture picture, int[]? rgba, float[]? floats)
    {
        var description = $"{c.Tools.FfmpegLabel} (libavcodec psd decoder)";
        try
        {
            if (floats is not null)
            {
                var (frames, command) = CommonCorpus.FfmpegDecode(c.Tools, path, "gbrapf32le", picture.Width, picture.Height, 16, animated: false);
                var plane = picture.Width * picture.Height;
                double max = 0;
                for (var i = 0; i < plane; i++)
                {
                    foreach (var (component, index) in new[] { (0, 2), (1, 0), (2, 1), (3, 3) })
                        max = Math.Max(max, Math.Abs(BinaryPrimitives.ReadSingleLittleEndian(frames[0].AsSpan(((index * plane) + i) * 4)) - floats[(i * 4) + component]));
                }

                return new Obj
                {
                    ["decoder"] = description,
                    ["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]),
                    ["result"] = max == 0 ? "exact" : "differs",
                    ["maxAbsoluteError"] = 0,
                    ["meanAbsoluteError"] = 0.0,
                    ["excludedFromTolerance"] = true,
                    ["notes"] = max == 0 ? "FFmpeg gives the same floats, bit for bit." : string.Create(CultureInfo.InvariantCulture, $"The floats differ by at most {max:G6}."),
                };
            }

            var depth = picture.Depth == 16 ? 16 : 8;
            var (decoded, decodeCommand) = CommonCorpus.FfmpegDecode(c.Tools, path, depth == 16 ? "rgba64le" : "rgba", picture.Width, picture.Height, depth == 16 ? 8 : 4, animated: false);
            var (maxError, sum) = Compare(rgba!, i => depth == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(decoded[0].AsSpan(i * 2)) : decoded[0][i]);
            return Check(description, CommonCorpus.Display(decodeCommand, c.Tools, [(path.Value, "{input}")]), maxError, sum / rgba!.Length, depth);
        }
        catch (ToolException error)
        {
            // The message of the decoder, after the prefixes of the log lines
            var reason = error.Message.Split('\n').Where(line => line.Contains("psd @", StringComparison.Ordinal) || line.Contains("dec:psd", StringComparison.Ordinal))
                .Select(line => line[(line.LastIndexOf("] ", StringComparison.Ordinal) + 2)..].Trim()).FirstOrDefault() ?? "an error";
            return new Obj
            {
                ["decoder"] = description,
                ["result"] = "differs",
                ["excludedFromTolerance"] = true,
                ["notes"] = "FFmpeg rejects the file (\"" + reason + "\").",
            };
        }
    }

    /// <summary>Requires that a cross-check differs exactly where a difference is declared, and records its explanation.</summary>
    private static Obj Declare(string name, Obj check, Dictionary<string, string> differences)
    {
        var differs = (string?)check["result"] != "exact";
        if (differences.TryGetValue(name, out var explanation))
        {
            Py.Assert(differs, $"psd/{name}: {check["decoder"]} now reads the file as the reference; remove the declared difference");
            check["notes"] = check.TryGetValue("notes", out var notes) && notes is string measured && measured.Length > 0 ? explanation + " " + measured : explanation;
        }
        else
        {
            Py.Assert(!differs, $"psd/{name}: {check["decoder"]} differs from the reference: {check.GetValueOrDefault("notes")}");
        }

        return check;
    }

    private static (int Max, double Sum) Compare(int[] expected, Func<int, int> actual)
    {
        var max = 0;
        double sum = 0;
        for (var i = 0; i < expected.Length; i++)
        {
            var error = Math.Abs(actual(i) - expected[i]);
            max = Math.Max(max, error);
            sum += error;
        }

        return (max, sum);
    }

    private static Obj Check(string decoder, string command, int max, double mean, int depth)
    {
        var check = new Obj
        {
            ["decoder"] = decoder,
            ["command"] = command,
            ["result"] = max == 0 ? "exact" : "differs",
            ["maxAbsoluteError"] = max,
            ["meanAbsoluteError"] = Math.Round(mean, 6, MidpointRounding.ToEven),
        };
        if (max != 0)
        {
            check["excludedFromTolerance"] = true;
            check["notes"] = string.Create(CultureInfo.InvariantCulture, $"At most {max} units of a {depth}-bit sample differ.");
        }

        return check;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Error fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildUnsupported(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildUnsupported);
        var cmyk = Samples(7, 4, 4, 255, 61);
        var (data, _) = PsdToolsWrite(c, "cmyk8", 7, 4, 8, "CMYK", 4, "RLE", RawBytes(cmyk, 8));
        AddUnsupported(c, "cmyk8", Function, data, "CMYK samples without color conversion", "8-bit CMYK samples (stored inverted, psd-tools): identified, rejected by an untyped load; they load as Cmyk32 pixels or with CmykConversionOptions (PsdDecoderConformanceTests).", ["psd.mode=cmyk"]);

        var cmyk16 = Samples(5, 3, 4, 65535, 62);
        (data, _) = PsdToolsWrite(c, "cmyk16", 5, 3, 16, "CMYK", 4, "RAW", RawBytes(cmyk16, 16));
        AddUnsupported(c, "cmyk16", Function, data, "CMYK samples without color conversion", "16-bit CMYK samples (psd-tools): identified, rejected by an untyped load; they load as Cmyk64 pixels.", ["psd.mode=cmyk"]);

        var lab = Samples(6, 3, 3, 255, 63);
        (data, _) = PsdToolsWrite(c, "lab8", 6, 3, 8, "LAB", 3, "RLE", RawBytes(lab, 8));
        AddUnsupported(c, "lab8", Function, data, "Lab samples without color conversion", "8-bit Lab samples (psd-tools): identified, only loaded with CieLabConversionOptions.", ["psd.mode=lab"]);

        // "Maximize Compatibility" off: the version information says there is no real composite, whose data is white
        var rgb = Samples(6, 4, 3, 255, 64);
        Layer[] layers = [new("Layer 1", 0, 0, 6, 4, [0, 1, 2], RawBytes(rgb, 8), "RLE")];
        (data, _) = PsdToolsWrite(c, "no-composite", 6, 4, 8, "RGB", 3, "RLE", composite: null, hasComposite: false, layers: layers);
        AddUnsupported(c, "no-composite", Function, data, "No composite image", "A file saved with \"Maximize Compatibility\" off (psd-tools): the version information resource (1057) says there is no real composite, and the image data is white. The layers are not rendered.", ["psd.layers=layerInfo"]);

        foreach (var compression in new[] { "ZIP", "ZIP_WITH_PREDICTION" })
        {
            var name = compression == "ZIP" ? "zip" : "zip-prediction";
            (data, _) = PsdToolsWrite(c, name, 6, 4, 8, "RGB", 3, compression, RawBytes(rgb, 8));
            AddUnsupported(c, name, Function, data, "ZIP-compressed image data", $"A composite of {(compression == "ZIP" ? "ZIP" : "ZIP with prediction")} compressed channels (psd-tools), which Photoshop only writes in layers: identified, rejected by a load.", []);
        }

        // Header-level rejections, assembled from the specification
        AddUnsupported(c, "multichannel", Function, new PsdFileBuilder(4, 2, 8, Multichannel, Planes(Samples(4, 2, 2, 255, 65), 2, 8)).Build(), "Multichannel color mode", "A multichannel document: its channels have no color interpretation.", ["psd.mode=multichannel"]);
        AddUnsupported(c, "cmyk32", Function, new PsdFileBuilder(2, 2, 32, Cmyk, PsdFileBuilder.Planes32(new float[16], 4)).Build(), "32-bit CMYK samples", "32-bit CMYK samples, which no CMYK pixel format or conversion reads.", ["psd.mode=cmyk"]);
        AddUnsupported(c, "lab16", Function, new PsdFileBuilder(2, 2, 16, Lab, PsdFileBuilder.Planes16(new ushort[12], 3)).Build(), "16-bit Lab samples", "16-bit Lab samples: only 8-bit Lab documents are read.", ["psd.mode=lab"]);
    }

    private static void BuildInvalid(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildInvalid);
        var samples = Samples(5, 3, 3, 255, 71);
        PsdFileBuilder Valid(int compression = 1) => new(5, 3, 8, Rgb, Planes(samples, 3, 8)) { Compression = compression };

        static byte[] Patch(byte[] data, int offset, params byte[] bytes)
        {
            var copy = (byte[])data.Clone();
            bytes.CopyTo(copy, offset);
            return copy;
        }

        var valid = Valid().Build();
        AddInvalid(c, "version-3", Function, Patch(valid, 4, 0, 3), "The version is 3: only 1 (PSD) and 2 (PSB) are defined.");
        AddInvalid(c, "reserved-bytes", Function, Patch(valid, 6, 0, 0, 1), "A reserved byte of the file header is not zero.");
        AddInvalid(c, "zero-channels", Function, Patch(valid, 12, 0, 0), "The file header declares 0 channels.");
        AddInvalid(c, "57-channels", Function, Patch(valid, 12, 0, 57), "The file header declares 57 channels; the specification allows 1 to 56.");
        AddInvalid(c, "zero-width", Function, Patch(valid, 18, 0, 0, 0, 0), "The width is 0.");
        AddInvalid(c, "psd-width-30001", Function, Patch(valid, 18, 0, 0, 0x75, 0x31), "A PSD file 30,001 pixels wide: only PSB files are wider than 30,000 pixels.");
        AddInvalid(c, "depth-4", Function, Patch(valid, 22, 0, 4), "The depth is 4 bits: only 1, 8, 16 and 32 are defined.");
        AddInvalid(c, "mode-5", Function, Patch(valid, 24, 0, 5), "The color mode is 5, which is not defined.");
        AddInvalid(c, "bitmap-depth-8", Function, Patch(valid, 24, 0, 0), "A bitmap document of 8-bit samples: bitmap samples have 1 bit.");
        AddInvalid(c, "fewer-channels-than-colors", Function, new PsdFileBuilder(5, 3, 8, Rgb, Planes(samples, 3, 8)[..2]) { Compression = 1 }.Build(), "An RGB document of two channels.");

        var indexed = new PsdFileBuilder(5, 3, 8, Indexed, Planes(Samples(5, 3, 1, 255, 72), 1, 8)) { ColorModeData = new byte[767] }.Build();
        AddInvalid(c, "indexed-color-table-length", Function, indexed, "An indexed document whose color table has 767 bytes instead of 768.");

        AddInvalid(c, "resource-signature", Function, Valid().AddResource(1005, new byte[16], signature: "8BIX").Build(), "An image resource whose signature is neither 8BIM nor one of the other signatures Photoshop writes.");
        var resource = Valid().AddResource(4000, new byte[8]).Build();
        AddInvalid(c, "resource-past-section", Function, Patch(resource, 26 + 4 + 4 + 8, 0, 0, 0, 9), "An image resource of 9 bytes in a section that holds 8.");

        var layered = Valid();
        layered.LayerCount = 1;
        var layeredData = layered.Build();
        var layerSection = 26 + 4 + 4;
        AddInvalid(c, "layer-info-past-section", Function, Patch(layeredData, layerSection + 4, 0, 0, 0, 9), "A layer info of 9 bytes in a layer and mask information section of 10 bytes.");
        var tagged = Valid();
        tagged.LayerCount = 1;
        tagged.LayerBlockKey = "Lr16";
        var taggedData = tagged.Build();
        AddInvalid(c, "tagged-block-signature", Function, Patch(taggedData, layerSection + 4 + 4 + 4, (byte)'8', (byte)'B', (byte)'I', (byte)'X'), "A tagged block of the layer and mask information whose signature is neither 8BIM nor 8B64.");

        AddInvalid(c, "compression-4", Function, new PsdFileBuilder(5, 3, 8, Rgb, []) { ChannelCount = 3, Compression = 4, CompressedData = new byte[45] }.Build(), "The compression of the image data is 4, which is not defined.");

        var rowsTooLong = Valid();
        rowsTooLong.EditPackedRow = (channel, row, packed) => channel == 1 && row == 1 ? [4, 1, 2, 3, 4, 5, 6] : packed;
        AddInvalid(c, "rle-row-too-long", Function, rowsTooLong.Build(), "A PackBits row of the second channel gives 5 bytes after its first literal and goes on: it decompresses to more than the 5 bytes of a row.", pixelDefect: true);
        var rowsTooShort = Valid();
        rowsTooShort.EditPackedRow = (channel, row, packed) => channel == 2 && row == 2 ? [2, 1, 2, 3] : packed;
        AddInvalid(c, "rle-row-too-short", Function, rowsTooShort.Build(), "A PackBits row of the third channel decompresses to 3 bytes; a row has 5.", pixelDefect: true);
        var literalPastLength = Valid();
        literalPastLength.EditPackedRow = (channel, row, packed) => channel == 0 && row == 0 ? [4, 1, 2, 3] : packed;
        AddInvalid(c, "rle-literal-past-length", Function, literalPastLength.Build(), "A PackBits literal of 5 bytes in a row whose stored length leaves 3.", pixelDefect: true);
        var runWithoutByte = Valid();
        runWithoutByte.EditPackedRow = (channel, row, packed) => channel == 0 && row == 2 ? [4, 1, 2, 3, 4, 5, 0xFE] : packed;
        AddInvalid(c, "rle-run-without-byte", Function, runWithoutByte.Build(), "A PackBits row that ends with the header of a run, without the byte to repeat.", pixelDefect: true);

        AddInvalid(c, "truncated-header", Function, valid[..20], "A file cut in its header.");
        AddInvalid(c, "truncated-resources", Function, Valid().AddResource(4000, new byte[40]).Build()[..60], "A file cut in its image resources.");
        AddInvalid(c, "truncated-counts", Function, valid[..(26 + 4 + 4 + 4 + 2 + 10)], "A file cut in the table of the lengths of the PackBits rows.");
        var raw = Valid(compression: 0).Build();
        AddInvalid(c, "truncated-image-data", Function, raw[..^4], "A file of raw image data cut in its last channel.");
    }

    private static void BuildLimits(Corpus<PsdToolSet> c)
    {
        const string Function = nameof(BuildLimits);
        var source = c.Fixtures.First(f => (string)f["id"]! == "psd/rgb8-rle");
        var input = (Obj)source["input"]!;
        foreach (var (name, limits, kind, notes) in new[]
        {
            ("width-over-limit", new Obj { ["MaxWidth"] = 12 }, "Width", "Reuses psd/rgb8-rle (13x7) with MaxWidth = 12."),
            ("height-over-limit", new Obj { ["MaxHeight"] = 6 }, "Height", "Reuses psd/rgb8-rle (13x7) with MaxHeight = 6."),
            ("frame-pixels-over-limit", new Obj { ["MaxFramePixels"] = 90 }, "FramePixels", "Reuses psd/rgb8-rle (13x7, 91 pixels) with MaxFramePixels = 90."),
        })
        {
            c.AddError("limit/psd/" + name, "limit", null, null, "psd", c.Provenance("generated", Function, null, [ToolSet.RuntimeLabel]),
                new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = kind }, features: (IList<string>?)source["features"], decodeOptions: new Obj { ["limits"] = limits },
                existingInput: new Obj { ["path"] = input["path"], ["sha256"] = input["sha256"] }, notes: notes);
        }

        var resources = c.Fixtures.First(f => (string)f["id"]! == "psd/resources");
        var resourcesInput = (Obj)resources["input"]!;
        c.AddError("limit/psd/metadata-bytes-over-limit", "limit", null, null, "psd", c.Provenance("generated", Function, null, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "MetadataBytes" }, features: (IList<string>?)resources["features"],
            decodeOptions: new Obj { ["limits"] = new Obj { ["MaxMetadataBytes"] = 200 } },
            existingInput: new Obj { ["path"] = resourcesInput["path"], ["sha256"] = resourcesInput["sha256"] },
            notes: "Reuses psd/resources with MaxMetadataBytes = 200: the bytes of the resolution and of the ICC profile, charged before they are read, exceed it.");
    }

    private static void AddUnsupported(Corpus<PsdToolSet> c, string name, string function, byte[] data, string feature, string notes, IList<string> features)
    {
        var fixtureId = "invalid/psd/" + name;
        var picture = ReferencePsd.Read(data);
        Py.Assert(picture.Width > 0, fixtureId);
        c.AddError(fixtureId, "unsupported", fixtureId + (picture.Version == 2 ? ".psb" : ".psd"), data, "psd", c.Provenance("generated", function, null, [$"psd-tools {c.Tools.PsdToolsVersion} (writer)", ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "Psd", ["feature"] = feature }, features: features, notes: notes);
    }

    private static void AddInvalid(Corpus<PsdToolSet> c, string name, string function, byte[] data, string notes, bool pixelDefect = false)
    {
        var fixtureId = "invalid/psd/" + name;

        // The defect is one the independent reader finds too
        bool rejected;
        try
        {
            var picture = ReferencePsd.Read(data);
            rejected = picture.ChannelCount < picture.ColorChannelCount;
        }
        catch (InvalidDataException)
        {
            rejected = true;
        }

        Py.Assert(rejected, fixtureId + ": the reference reader reads the file");
        _ = pixelDefect;
        c.AddError(fixtureId, "invalid", fixtureId + ".psd", data, "psd", c.Provenance("hand-authored", function, null, [ToolSet.RuntimeLabel]),
            new Obj { ["exception"] = "InvalidImageContentException", ["format"] = "Psd" }, notes: notes);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class PsdToolSet : ToolSet
    {
        public PsdToolSet(bool acceptVersions)
            : base(PrebuiltTools.Ffmpeg.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult())
        {
            Magick = PrebuiltTools.Magick.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            PsdTools = PrebuiltTools.PsdTools.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
            MagickVersion = PrebuiltTools.Magick.Version;
            PsdToolsVersion = PrebuiltTools.PsdTools.Version;
            if ((FfmpegVersion != PinnedFfmpeg || MagickVersion != PinnedMagick || PsdToolsVersion != PinnedPsdTools) && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: FFmpeg {FfmpegVersion} (pinned {PinnedFfmpeg}), ImageMagick {MagickVersion} (pinned {PinnedMagick}), psd-tools {PsdToolsVersion} (pinned {PinnedPsdTools}). Review the impact and rerun with --accept-tool-versions (then update the pinned versions).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Magick] = "magick", [PsdTools] = "psd-tools" };
        }

        public string Magick { get; }

        public string PsdTools { get; }

        public string MagickVersion { get; }

        public string PsdToolsVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }
    }
}
