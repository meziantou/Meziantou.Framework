using System.Buffers.Binary;
using System.Globalization;
using System.Text.Json;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the OpenEXR fixtures of the golden corpus (tests/Meziantou.Framework.Imaging.Fixtures/exr, invalid/exr and
/// limit/exr entries). This generator is the reviewed, reproducible recipe of every committed OpenEXR fixture. It is NEVER
/// run by the tests and does not use the library under test.
/// The files are written by exr-from-raw, a program of the meziantou/prebuilt release built with OpenEXR, from raw
/// samples and a description of the header; a few are the sample images of the OpenEXR project (openexr-images,
/// BSD-3-Clause), at the pinned commit below. The reference is what OpenEXR decodes (exr-to-raw, bit for bit, lossy
/// compressions included), cross-checked with the EXR decoder of FFmpeg, which shares no code with OpenEXR, wherever it
/// reads the file as it is (one type of samples, R, G and B with or without A, or Y, the data window equal to the display
/// window). The expected pixels follow the rules of the library, applied here to the samples of the reference: the display
/// window is the image (transparent black where the data window does not cover it), a missing color is 0 and a missing
/// alpha 1, Y gives the three colors, UINT samples are their float value, and the colors are divided by alpha (in double
/// precision, rounded to a float) unless alpha is 0.
/// Malformed inputs are byte edits of valid files that OpenEXR also rejects (exr-to-raw fails on them).
/// It only replaces the OpenEXR entries of tests/Meziantou.Framework.Imaging.Fixtures/manifest.json (format "exr") and
/// their files, and keeps every other entry and file unchanged.
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- exr --openexr-images &lt;dir&gt;
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- exr --openexr-images &lt;dir&gt; --write
/// Requirements: the tools are downloaded from the meziantou/prebuilt release pinned by the Meziantou.Prebuilt package;
/// the sample images are a checkout of https://github.com/AcademySoftwareFoundation/openexr-images at OpenExrImagesCommit
/// (the generator never downloads them, and checks the SHA-256 of each one). Any OS.
/// </summary>
internal static partial class ExrCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/ExrCorpus.cs";

    // Tool versions used to produce the committed OpenEXR fixtures. Regenerating with other versions is allowed only
    // explicitly (--accept-tool-versions) and must be reviewed: the lossy compressions may decode other samples.
    private const string PinnedOpenExr = "3.5.2";

    private const string OpenExrImagesCommit = "e38ffb0790f62f05a6f083a6fa4cac150b3b7452";
    private const string OpenExrImagesUrl = "https://github.com/AcademySoftwareFoundation/openexr-images/blob/" + OpenExrImagesCommit + "/";

    // The sample images of the OpenEXR project the corpus holds, and their SHA-256
    private static readonly (string Path, string Sha256)[] ExternalImages =
    [
        ("TestImages/stripes.exr", "a4c11b79d256fb6945087dc9b244e814264a128aeb2fefacc303c32d08e426a1"),
        ("TestImages/AllHalfValues.exr", "eede573a0b59b79f21de15ee9d3b7649d58d8f2a8e7787ea34f192db3b3c84a4"),
        ("MultiResolution/ColorCodedLevels.exr", "ba836f7d37ea268ef9e96d7f0e28d035f4f197be12356133676282c578ada643"),
        ("DisplayWindow/t01.exr", "adc51278b0ceaa3475f58a9abc5cb5dec79c4590b9db773009e6ffcfdfd81643"),
        ("DisplayWindow/t02.exr", "0ef9bf41902af9ae8cc981068cd5948cb9629bc65c4943ae5b6daec4fcdcd0e1"),
        ("DisplayWindow/t09.exr", "4b57630afbe015efd01998857f033dd26236be60a042827faba7afa2c853390c"),
        ("DisplayWindow/t13.exr", "e779baaed0563955213f99daf07f903727f9100c3b6ea342302d0f0a32607d2a"),
    ];

    private static readonly string[] Compressions = ["NONE", "RLE", "ZIPS", "ZIP", "PIZ", "PXR24", "B44", "B44A"];

    private static bool IsExrFixture(Obj fixture) => (string?)fixture["format"] == "exr";

    public static int Run(GeneratorOptions options)
    {
        if (options.OpenExrImages is null)
            throw new FatalException("The exr generator needs --openexr-images: a checkout of AcademySoftwareFoundation/openexr-images at " + OpenExrImagesCommit + ".");

        var tools = new ExrTools(options.AcceptToolVersions);
        var images = FullPath.FromPath(options.OpenExrImages);
        return CorpusDriver.Run(options, "mfi-exr-corpus-", output => Generate(output, tools, images),
            (fixtures, files, bytes) => $"{fixtures} OpenEXR fixtures; corpus: {files} files, {bytes} bytes",
            "The committed OpenEXR fixtures are reproducible.");
    }

    private static int Generate(FullPath outDir, ExrTools tools, FullPath images)
    {
        List<Obj> fixtures;
        using (var corpus = new Corpus<ExrTools>(outDir, tools, ScriptPath))
        {
            BuildCompressions(corpus);
            BuildChannels(corpus);
            BuildTiles(corpus);
            BuildLayout(corpus);
            BuildColor(corpus);
            BuildExternal(corpus, images);
            BuildInvalid(corpus);
            BuildUnsupported(corpus, images);
            BuildLimits(corpus);
            fixtures = corpus.Fixtures;
        }

        foreach (var fixture in fixtures)
            Py.Assert(IsExrFixture(fixture), (string?)fixture["id"]);

        CorpusDriver.MergeIntoCommittedManifest(outDir, fixtures, IsExrFixture);
        return fixtures.Count;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Descriptions of the files exr-from-raw writes
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A channel: its pixel type (HALF, FLOAT, UINT) and the value of each sample of each level.</summary>
    private sealed record Channel(string Name, string PixelType, Func<int, int, int, double> Value, bool PLinear = false, int XSampling = 1, int YSampling = 1);

    /// <summary>The description of a file: the data window from (X0, Y0), the members of a part and other attributes.</summary>
    private sealed class Description
    {
        public required int Width { get; init; }

        public required int Height { get; init; }

        public int X0 { get; init; }

        public int Y0 { get; init; }

        public string Compression { get; init; } = "ZIP";

        public int[]? DisplayWindow { get; init; }

        public string? LineOrder { get; init; }

        public (int Width, int Height, string LevelMode, string RoundingMode)? Tiles { get; init; }

        public required Channel[] Channels { get; init; }

        public List<(string Name, string Type, object Value)> Attributes { get; init; } = [];

        public string? Type { get; init; }
    }

    /// <summary>The sizes of the levels of a description, in the order exr-from-raw reads their samples.</summary>
    private static List<(int Lx, int Ly, int Width, int Height)> Levels(Description d)
    {
        if (d.Tiles is not { } tiles || tiles.LevelMode == "ONE_LEVEL")
            return [(0, 0, d.Width, d.Height)];

        var roundUp = tiles.RoundingMode == "ROUND_UP";
        int Count(int size) => (roundUp ? (int)Math.Ceiling(Math.Log2(size)) : (int)Math.Floor(Math.Log2(size))) + 1;
        int Size(int size, int level) => Math.Max(roundUp ? (size + (1 << level) - 1) >> level : size >> level, 1);
        var result = new List<(int, int, int, int)>();
        if (tiles.LevelMode == "MIPMAP_LEVELS")
        {
            for (var level = 0; level < Count(Math.Max(d.Width, d.Height)); level++)
                result.Add((level, level, Size(d.Width, level), Size(d.Height, level)));
        }
        else
        {
            for (var ly = 0; ly < Count(d.Height); ly++)
            {
                for (var lx = 0; lx < Count(d.Width); lx++)
                    result.Add((lx, ly, Size(d.Width, lx), Size(d.Height, ly)));
            }
        }

        return result;
    }

    /// <summary>The samples of a channel, little-endian in its pixel type, for every level.</summary>
    private static byte[] Samples(Description d, Channel channel)
    {
        var builder = new List<byte>();
        Span<byte> sample = stackalloc byte[4];
        var level = 0;
        foreach (var (_, _, width, height) in Levels(d))
        {
            for (var y = 0; y < height; y++)
            {
                if ((d.Y0 + y) % channel.YSampling != 0)
                    continue;

                for (var x = 0; x < width; x += channel.XSampling)
                {
                    var value = channel.Value(level, x, y);
                    switch (channel.PixelType)
                    {
                        case "HALF":
                            BinaryPrimitives.WriteHalfLittleEndian(sample, (Half)value);
                            builder.AddRange(sample[..2].ToArray());
                            break;
                        case "FLOAT":
                            BinaryPrimitives.WriteSingleLittleEndian(sample, (float)value);
                            builder.AddRange(sample.ToArray());
                            break;
                        default:
                            BinaryPrimitives.WriteUInt32LittleEndian(sample, (uint)value);
                            builder.AddRange(sample.ToArray());
                            break;
                    }
                }
            }

            level++;
        }

        return [.. builder];
    }

    /// <summary>Writes a file with exr-from-raw.</summary>
    private static (byte[] Data, string Command) Write(Corpus<ExrTools> c, Description d)
    {
        var directory = c.Scratch / "write";
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
        using (var stream = File.Create(directory / "description.json"))
        using (var json = new Utf8JsonWriter(stream))
        {
            json.WriteStartObject();
            if (d.Type is not null)
                json.WriteString("type", d.Type);

            json.WritePropertyName("dataWindow");
            WriteInts(json, d.X0, d.Y0, d.X0 + d.Width - 1, d.Y0 + d.Height - 1);
            if (d.DisplayWindow is { } display)
            {
                json.WritePropertyName("displayWindow");
                WriteInts(json, display);
            }

            json.WriteString("compression", d.Compression);
            if (d.LineOrder is not null)
                json.WriteString("lineOrder", d.LineOrder);

            if (d.Tiles is { } tiles)
            {
                json.WriteStartObject("tiles");
                json.WriteNumber("xSize", tiles.Width);
                json.WriteNumber("ySize", tiles.Height);
                json.WriteString("levelMode", tiles.LevelMode);
                json.WriteString("roundingMode", tiles.RoundingMode);
                json.WriteEndObject();
            }

            json.WriteStartArray("channels");
            for (var i = 0; i < d.Channels.Length; i++)
            {
                var channel = d.Channels[i];
                var file = $"channel{i.ToString(CultureInfo.InvariantCulture)}.raw";
                var isDeep = d.Type?.StartsWith("deep", StringComparison.Ordinal) == true;
                if (!isDeep)
                    File.WriteAllBytes(directory / file, Samples(d, channel));

                json.WriteStartObject();
                json.WriteString("name", channel.Name);
                json.WriteString("pixelType", channel.PixelType);
                json.WriteNumber("xSampling", channel.XSampling);
                json.WriteNumber("ySampling", channel.YSampling);
                json.WriteBoolean("pLinear", channel.PLinear);
                if (!isDeep)
                    json.WriteString("file", file);

                json.WriteEndObject();
            }

            json.WriteEndArray();
            if (d.Attributes.Count > 0)
            {
                json.WriteStartArray("attributes");
                foreach (var (name, type, value) in d.Attributes)
                {
                    json.WriteStartObject();
                    json.WriteString("name", name);
                    json.WriteString("type", type);
                    json.WritePropertyName(value is byte[] ? "data" : "value");
                    switch (value)
                    {
                        case byte[] bytes:
                            json.WriteStringValue(Convert.ToHexStringLower(bytes));
                            break;
                        case string text:
                            json.WriteStringValue(text);
                            break;
                        case float[] numbers:
                            json.WriteStartArray();
                            foreach (var number in numbers)
                                json.WriteNumberValue(number);
                            json.WriteEndArray();
                            break;
                        default:
                            json.WriteNumberValue(Convert.ToDouble(value, CultureInfo.InvariantCulture));
                            break;
                    }

                    json.WriteEndObject();
                }

                json.WriteEndArray();
            }

            json.WriteEndObject();
        }

        var output = directory / "output.exr";
        List<string> command = [c.Tools.ExrFromRaw, (directory / "description.json").Value, output.Value];
        Proc.Run(command);
        return (File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [((directory / "description.json").Value, "{description}.json"), (output.Value, "{output}.exr")]));

        static void WriteInts(Utf8JsonWriter json, params int[] values)
        {
            json.WriteStartArray();
            foreach (var value in values)
                json.WriteNumberValue(value);
            json.WriteEndArray();
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // The reference: what OpenEXR decodes
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>A channel decoded by exr-to-raw: its samples of the full-resolution level, as their bits.</summary>
    private sealed record DecodedChannel(string Name, string PixelType, uint[] Bits, bool PLinear)
    {
        public float this[int index] => PixelType switch
        {
            "HALF" => (float)BitConverter.UInt16BitsToHalf((ushort)Bits[index]),
            "FLOAT" => BitConverter.UInt32BitsToSingle(Bits[index]),
            _ => Bits[index],
        };
    }

    /// <summary>The header and the samples exr-to-raw gives for a file.</summary>
    private sealed record Dump(JsonElement Part, int[] DataWindow, int[] DisplayWindow, List<DecodedChannel> Channels, string Command);

    /// <summary>Decodes a file with exr-to-raw; <see langword="null"/> when OpenEXR refuses it.</summary>
    private static Dump? Decode(Corpus<ExrTools> c, byte[] data)
    {
        var directory = c.Scratch / "dump";
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        var input = c.Scratch / "input.exr";
        File.WriteAllBytes(input, data);
        List<string> command = [c.Tools.ExrToRaw, input.Value, directory.Value];
        var result = Proc.Exec(command);
        if (result.ExitCode != 0)
            return null;

        using var header = JsonDocument.Parse(File.ReadAllBytes(directory / "header.json"));
        var part = header.RootElement.GetProperty("parts")[0].Clone();
        var dataWindow = part.GetProperty("dataWindow").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var displayWindow = part.GetProperty("displayWindow").EnumerateArray().Select(value => value.GetInt32()).ToArray();
        var width = dataWindow[2] - dataWindow[0] + 1;
        var height = dataWindow[3] - dataWindow[1] + 1;
        var channels = new List<DecodedChannel>();
        foreach (var channel in part.GetProperty("channels").EnumerateArray())
        {
            var type = channel.GetProperty("pixelType").GetString()!;
            var bytes = File.ReadAllBytes(directory / channel.GetProperty("file").GetString()!);
            var size = type == "HALF" ? 2 : 4;
            var bits = new uint[width * height];
            for (var i = 0; i < bits.Length; i++)
                bits[i] = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * i));

            channels.Add(new DecodedChannel(channel.GetProperty("name").GetString()!, type, bits, channel.GetProperty("pLinear").GetBoolean()));
        }

        return new Dump(part, dataWindow, displayWindow, channels, CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (directory.Value, "{output}")]));
    }

    /// <summary>The expected pixels: RGBA floats of the display window, by the rules of the library.</summary>
    private static float[] Present(Dump dump, out bool halfPixels, out string colorModel, out int bits)
    {
        var (data, display) = (dump.DataWindow, dump.DisplayWindow);
        var displayWidth = display[2] - display[0] + 1;
        var displayHeight = display[3] - display[1] + 1;
        var dataWidth = data[2] - data[0] + 1;
        var result = new float[displayWidth * displayHeight * 4];
        var hasAlpha = dump.Channels.Any(channel => channel.Name == "A");
        var isLuminance = dump.Channels.Any(channel => channel.Name == "Y");
        var halfOnly = dump.Channels.All(channel => channel.PixelType == "HALF");
        halfPixels = halfOnly && !hasAlpha;
        colorModel = isLuminance ? hasAlpha ? "GrayscaleAlpha" : "Grayscale" : hasAlpha ? "Rgba" : "Rgb";
        bits = halfOnly ? 16 : 32;
        for (var y = data[1]; y <= data[3]; y++)
        {
            for (var x = data[0]; x <= data[2]; x++)
            {
                var source = ((y - data[1]) * dataWidth) + (x - data[0]);
                var target = (((y - display[1]) * displayWidth) + (x - display[0])) * 4;
                result[target + 3] = 1f;
                foreach (var channel in dump.Channels)
                {
                    var value = channel[source];
                    switch (channel.Name)
                    {
                        case "R": result[target] = value; break;
                        case "G": result[target + 1] = value; break;
                        case "B": result[target + 2] = value; break;
                        case "A": result[target + 3] = value; break;
                        case "Y": result[target] = result[target + 1] = result[target + 2] = value; break;
                        default: throw new InvalidOperationException("Channel " + channel.Name);
                    }
                }

                var alpha = result[target + 3];
                if (hasAlpha && alpha != 0)
                {
                    for (var i = 0; i < 3; i++)
                        result[target + i] = (float)(result[target + i] / (double)alpha);
                }
            }
        }

        return result;
    }

    /// <summary>Decodes a file with FFmpeg and compares the samples it stores with those of OpenEXR.</summary>
    private static Obj? FfmpegCrossCheck(Corpus<ExrTools> c, byte[] data, Dump dump)
    {
        var names = string.Concat(dump.Channels.Select(channel => channel.Name));
        var types = dump.Channels.Select(channel => channel.PixelType).Distinct(StringComparer.Ordinal).ToList();
        if (names is not ("BGR" or "ABGR" or "Y") || types.Count != 1 || types[0] == "UINT" || !dump.DataWindow.SequenceEqual(dump.DisplayWindow))
            return null;

        var half = types[0] == "HALF";
        var pixelFormat = (names, half) switch
        {
            ("Y", true) => "grayf16le",
            ("Y", false) => "grayf32le",
            ("BGR", true) => "gbrpf16le",
            ("BGR", false) => "gbrpf32le",
            ("ABGR", true) => "gbrapf16le",
            _ => "gbrapf32le",
        };

        // The planes of FFmpeg: green, blue, red, alpha
        var planeOrder = names == "Y" ? ["Y"] : names == "BGR" ? new[] { "G", "B", "R" } : ["G", "B", "R", "A"];
        var width = dump.DataWindow[2] - dump.DataWindow[0] + 1;
        var height = dump.DataWindow[3] - dump.DataWindow[1] + 1;
        var size = half ? 2 : 4;
        var path = c.Scratch / "ffmpeg.exr";
        File.WriteAllBytes(path, data);
        var check = new Obj { ["decoder"] = $"{c.Tools.FfmpegLabel} (libavcodec exr decoder)" };
        List<byte[]> frames;
        try
        {
            (frames, var command) = CommonCorpus.FfmpegDecode(c.Tools, path, pixelFormat, width, height, size * planeOrder.Length, animated: false);
            check["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, "{input}")]);
        }
        catch (ToolException error)
        {
            check["result"] = "differs";
            check["notes"] = "FFmpeg failed to decode the input: " + error.Message.Split('\n')[0];
            return check;
        }

        var frame = frames[0];
        var plane = width * height;
        var differing = 0;
        for (var p = 0; p < planeOrder.Length; p++)
        {
            var channel = dump.Channels.First(item => item.Name == planeOrder[p]);
            for (var i = 0; i < plane; i++)
            {
                var offset = ((p * plane) + i) * size;
                var bits = half ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(offset));
                if (bits != channel.Bits[i])
                    differing++;
            }
        }

        check["maxAbsoluteError"] = 0;
        check["meanAbsoluteError"] = 0.0;
        if (differing == 0)
        {
            check["result"] = "exact";
            check["notes"] = "The stored samples (FFmpeg does not divide the colors by alpha), bit for bit.";
        }
        else
        {
            check.Remove("maxAbsoluteError");
            check.Remove("meanAbsoluteError");
            check["result"] = "differs";
            check["excludedFromTolerance"] = true;
            check["notes"] = dump.Channels.Any(channel => channel.PLinear)
                ? string.Create(CultureInfo.InvariantCulture, $"{differing} stored samples differ from OpenEXR: FFmpeg does not apply the conversion of the pLinear channels of B44, which the library and OpenEXR apply.")
                : string.Create(CultureInfo.InvariantCulture, $"{differing} stored samples differ from OpenEXR.");
        }

        return check;
    }

    /// <summary>
    /// Compares the samples OpenEXR decodes with the pattern given to exr-from-raw: the reference of a lossless compression,
    /// and of the channels a lossy one keeps (UINT and HALF samples of PXR24, FLOAT and UINT samples of B44).
    /// </summary>
    private static Obj? PatternCrossCheck(Description d, Dump dump)
    {
        var lossyTypes = d.Compression switch
        {
            "PXR24" => new[] { "FLOAT" },
            "B44" or "B44A" => ["HALF"],
            _ => [],
        };
        var channels = d.Channels.Where(channel => !lossyTypes.Contains(channel.PixelType, StringComparer.Ordinal)).ToList();
        if (channels.Count == 0)
            return null;

        var differing = 0;
        foreach (var channel in channels)
        {
            var decoded = dump.Channels.Single(item => item.Name == channel.Name);
            var samples = Samples(new Description { Width = d.Width, Height = d.Height, X0 = d.X0, Y0 = d.Y0, Compression = d.Compression, Channels = [channel] }, channel);
            var size = channel.PixelType == "HALF" ? 2 : 4;
            for (var i = 0; i < d.Width * d.Height; i++)
            {
                var bits = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(samples.AsSpan(2 * i)) : BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(4 * i));
                if (bits != decoded.Bits[i])
                    differing++;
            }
        }

        var names = string.Join(", ", channels.Select(channel => channel.Name));
        var check = new Obj
        {
            ["decoder"] = "the pattern given to exr-from-raw",
            ["result"] = differing == 0 ? "exact" : "differs",
            ["notes"] = differing == 0
                ? $"The samples of {names}, bit for bit: the compression keeps them."
                : string.Create(CultureInfo.InvariantCulture, $"{differing} samples of {names} differ from the pattern."),
        };
        if (differing == 0)
        {
            check["maxAbsoluteError"] = 0;
            check["meanAbsoluteError"] = 0.0;
        }

        return check;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Manifest entries
    // -----------------------------------------------------------------------------------------------------------------

    private static List<string> Features(Dump dump, IEnumerable<string>? extra = null)
    {
        var part = dump.Part;
        var features = new List<string>
        {
            "exr.compression=" + part.GetProperty("compression").GetString()!.ToLowerInvariant(),
            "exr.storage=" + (part.GetProperty("type").GetString() == "tiledimage" ? "tiled" : "scanline"),
            "exr.channels=" + string.Concat(dump.Channels.Select(channel => channel.Name).Order(StringComparer.Ordinal)).ToLowerInvariant(),
        };
        foreach (var type in dump.Channels.Select(channel => channel.PixelType.ToLowerInvariant()).Distinct(StringComparer.Ordinal))
            features.Add("exr.type=" + type);

        var lineOrder = part.GetProperty("lineOrder").GetString();
        if (lineOrder != "INCREASING_Y")
            features.Add("exr.lineOrder=" + lineOrder!.ToLowerInvariant().Replace('_', '-'));

        if (part.TryGetProperty("tiles", out var tiles))
            features.Add("exr.levels=" + tiles.GetProperty("levelMode").GetString()!.ToLowerInvariant().Replace("_levels", "", StringComparison.Ordinal).Replace('_', '-'));

        if (!dump.DataWindow.SequenceEqual(dump.DisplayWindow))
            features.Add("exr.dataWindow=inside");

        if (dump.DataWindow[0] < 0 || dump.DataWindow[1] < 0)
            features.Add("exr.negativeCoordinates");

        if (part.TryGetProperty("attributes", out var attributes) && attributes.GetArrayLength() > 0)
            features.Add("exr.otherAttributes");

        features.AddRange(extra ?? []);
        features.Sort(StringComparer.Ordinal);
        return [.. features.Distinct(StringComparer.Ordinal)];
    }

    private static Obj FloatBuffer(Corpus<ExrTools> c, string stem, int width, int height, float[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 * i), pixels[i]);

        var entry = c.Write(stem + ".frame-0.rgbaf32le.raw", bytes);
        entry["layout"] = "rgbaf32le";
        entry["width"] = width;
        entry["height"] = height;
        entry["rowBytes"] = width * 16;
        entry["byteLength"] = bytes.Length;
        entry["alpha"] = "straight";
        entry["rowOrder"] = "top-down";
        entry["compression"] = "none";
        return entry;
    }

    /// <summary>Records a valid fixture: OpenEXR decodes it, and the expected pixels follow from its samples.</summary>
    private static void AddValid(Corpus<ExrTools> c, string name, string function, byte[] data, List<string> commands, string description, string? notes = null,
        IEnumerable<string>? features = null, Obj? cicp = null, Obj? parameters = null, string origin = "generated", string? source = null, string license = Corpus<ExrTools>.License,
        Description? pattern = null)
    {
        var fixtureId = "exr/" + name;
        var dump = Decode(c, data) ?? throw new FatalException(fixtureId + ": OpenEXR does not read the file.");
        var pixels = Present(dump, out var halfPixels, out var colorModel, out var bits);
        var width = dump.DisplayWindow[2] - dump.DisplayWindow[0] + 1;
        var height = dump.DisplayWindow[3] - dump.DisplayWindow[1] + 1;
        var expected = new Obj
        {
            ["width"] = width,
            ["height"] = height,
            ["pixelFormat"] = halfPixels ? "RgbaHalf" : "RgbaFloat",
            ["colorModel"] = colorModel,
            ["bitsPerComponent"] = bits,
            ["orientation"] = 1,
            ["iccProfile"] = "none",
        };
        if (cicp is null)
        {
            expected["transferFunction"] = "linear";
        }
        else
        {
            expected["cicp"] = cicp;
        }

        expected["animation"] = null;
        expected["resolution"] = null;
        expected["profiles"] = new Obj { ["icc"] = null, ["exif"] = null, ["xmp"] = null };
        expected["text"] = new List<object?>();
        expected["frameCount"] = 1;
        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = new List<object?> { FloatBuffer(c, fixtureId, width, height, pixels) } } };
        expected["poster"] = null;

        var checks = new List<object?>();
        if (FfmpegCrossCheck(c, data, dump) is { } check)
            checks.Add(check);

        if (pattern is not null && PatternCrossCheck(pattern, dump) is { } patternCheck)
            checks.Add(patternCheck);

        Py.Assert(checks.Count > 0, fixtureId + ": no independent cross-check");

        var input = c.Write(fixtureId + ".exr", data);
        input["role"] = "encoded";
        var provenance = c.Provenance(origin, function, parameters, [$"OpenEXR {c.Tools.OpenExrVersion} (exr-from-raw, exr-to-raw)", $"{c.Tools.FfmpegLabel} (cross-check)", ToolSet.RuntimeLabel], commands);
        provenance["license"] = license;
        if (source is not null)
            provenance["source"] = source;

        var fixture = new Obj
        {
            ["id"] = fixtureId,
            ["kind"] = "valid",
            ["format"] = "exr",
            ["input"] = input,
            ["provenance"] = provenance,
            ["features"] = Features(dump, features),
            ["reference"] = new Obj
            {
                ["method"] = "decoded",
                ["description"] = description + " The samples OpenEXR decodes, presented by the rules of the library: the display window is the image, the colors are divided by alpha.",
                ["decoder"] = $"OpenEXR {c.Tools.OpenExrVersion} (exr-to-raw)",
                ["backend"] = "exr-to-raw of the meziantou/prebuilt release: the decoded samples of every channel, in their own type, bit for bit",
                ["command"] = dump.Command,
                ["crossChecks"] = checks,
            },
            ["expected"] = expected,
            ["comparison"] = new Obj { ["mode"] = "exact" },
        };
        if (!string.IsNullOrEmpty(notes))
            fixture["notes"] = notes;

        c.Fixtures.Add(fixture);
    }

    private static void AddGenerated(Corpus<ExrTools> c, string name, string function, Description d, string description, string? notes = null, IEnumerable<string>? features = null, Obj? cicp = null)
    {
        var (data, command) = Write(c, d);
        AddValid(c, name, function, data, [command], description, notes, features, cicp, new Obj { ["width"] = d.Width, ["height"] = d.Height }, pattern: d);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Patterns: deterministic values above 1 and below 0 included
    // -----------------------------------------------------------------------------------------------------------------

    private static Func<int, int, int, double> Wave(int seed, double scale = 4) => (level, x, y) =>
        ((((x * 37) + (y * 101) + (seed * 53) + (level * 29)) % 97) / 97.0 * scale) - (scale / 4) + (((x ^ y) & 3) * 0.001);

    private static Func<int, int, int, double> Smooth(int seed) => (level, x, y) =>
        (Math.Sin((x + seed) / 3.0) * Math.Cos((y - seed) / 5.0) * 3) + 1 + level;

    private static Func<int, int, int, double> Alpha => (level, x, y) => ((x + (2 * y)) % 5) switch { 0 => 0, 1 => 0.25, 2 => 0.5, 3 => 1, _ => 0.8 };

    private static Func<int, int, int, double> Flat(double value) => (level, x, y) => value;

    private static Channel[] Rgba(string type, int seed) => [new("A", type, Alpha), new("B", type, Wave(seed)), new("G", type, Smooth(seed)), new("R", type, Wave(seed + 1, 8))];

    private static Channel[] Rgb(string type, int seed) => [new("B", type, Wave(seed)), new("G", type, Smooth(seed)), new("R", type, Wave(seed + 1, 8))];

    // -----------------------------------------------------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------------------------------------------------

    private static void BuildCompressions(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildCompressions);
        foreach (var compression in Compressions)
        {
            var name = compression.ToLowerInvariant();
            AddGenerated(c, name + "-half-rgba", Function, new Description { Width = 23, Height = 37, Compression = compression, Channels = Rgba("HALF", 1) },
                "HALF R, G, B and A: waves and alphas of 0, 1/4, 1/2, 4/5 and 1.");
            AddGenerated(c, name + "-float-rgb", Function, new Description { Width = 19, Height = 21, Compression = compression, Channels = Rgb("FLOAT", 2) },
                "FLOAT R, G and B: waves.");
        }

        // Flat blocks: the 3-byte form of B44A; a constant image: runs of RLE and PIZ
        AddGenerated(c, "b44a-flat-blocks", Function, new Description
        {
            Width = 16,
            Height = 9,
            Compression = "B44A",
            Channels = [new("B", "HALF", Flat(0.25)), new("G", "HALF", (level, x, y) => x < 8 ? 0.5 : x), new("R", "HALF", Flat(-2))],
        }, "HALF B, G and R whose left half, B and R channels are flat.", "The flat blocks are stored in 3 bytes.");
        AddGenerated(c, "piz-constant", Function, new Description { Width = 70, Height = 40, Compression = "PIZ", Channels = [new("Y", "HALF", Flat(0.75))] },
            "A constant HALF Y channel.", "The Huffman data are runs of the run symbol.");
        AddGenerated(c, "piz-sixteen-bit-wavelet", Function, new Description
        {
            Width = 40,
            Height = 33,
            Compression = "PIZ",
            Channels = [new("Y", "FLOAT", (level, x, y) => BitConverter.Int32BitsToSingle((int)(((uint)(x * 7919) * 2654435761u) ^ ((uint)y * 40503u)) & 0x3FFFFFFF))],
        }, "FLOAT Y samples of random bits.", "More than 16384 distinct 16-bit values: the 16-bit wavelet.");
        AddGenerated(c, "pxr24-float-rounding", Function, new Description
        {
            Width = 9,
            Height = 5,
            Compression = "PXR24",
            Channels = [new("Y", "FLOAT", (level, x, y) => BitConverter.Int32BitsToSingle(0x3F800000 + (x * 0x7F) + (y * 0x81)))],
        }, "FLOAT samples whose low 8 bits are near 0x80.", "PXR24 rounds the samples to 24 bits, ties up.");
        AddGenerated(c, "b44-plinear", Function, new Description
        {
            Width = 13,
            Height = 10,
            Compression = "B44",
            Channels = [new("Y", "HALF", (level, x, y) => (x - 4) * (y + 1) * 0.37, PLinear: true)],
        }, "A pLinear HALF Y channel, negative values included.", "B44 converts the samples of a pLinear channel with an exponential before packing them and a logarithm after.");
    }

    private static void BuildChannels(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildChannels);
        AddGenerated(c, "zip-half-luminance", Function, new Description { Width = 31, Height = 18, Compression = "ZIP", Channels = [new("Y", "HALF", Smooth(3))] },
            "A HALF Y channel: the three colors.");
        AddGenerated(c, "zip-half-luminance-alpha", Function, new Description { Width = 15, Height = 11, Compression = "ZIP", Channels = [new("A", "HALF", Alpha), new("Y", "HALF", Smooth(4))] },
            "HALF Y and A channels.");
        AddGenerated(c, "rle-half-red", Function, new Description { Width = 12, Height = 7, Compression = "RLE", Channels = [new("R", "HALF", Wave(5))] },
            "A HALF R channel only: green and blue are 0, alpha is 1.");
        AddGenerated(c, "piz-mixed-types", Function, new Description
        {
            Width = 17,
            Height = 34,
            Compression = "PIZ",
            Channels = [new("A", "HALF", Alpha), new("B", "UINT", (level, x, y) => (x * 1000) + y), new("G", "FLOAT", Smooth(6)), new("R", "HALF", Wave(6))],
        }, "HALF A and R, FLOAT G, UINT B: RgbaFloat pixels; a UINT sample is its float value.");
        AddGenerated(c, "zip-uint-luminance", Function, new Description { Width = 10, Height = 10, Compression = "ZIP", Channels = [new("Y", "UINT", (level, x, y) => (x * 1_000_003) + (y * 77))] },
            "A UINT Y channel whose values hold in a float exactly.");
        AddGenerated(c, "none-one-pixel", Function, new Description { Width = 1, Height = 1, Compression = "NONE", Channels = Rgba("FLOAT", 7) },
            "One FLOAT pixel.");
        AddGenerated(c, "piz-float-specials", Function, new Description
        {
            Width = 6,
            Height = 3,
            Compression = "PIZ",
            Channels =
            [
                new("B", "FLOAT", (level, x, y) => (x + (y * 6)) switch { 0 => double.PositiveInfinity, 1 => double.NegativeInfinity, 2 => -0.0, 3 => float.MaxValue, 4 => -float.MaxValue, 5 => float.Epsilon, _ => (x - 3) * 1e30 }),
                new("G", "FLOAT", (level, x, y) => (x * 1e-38) - (y * 3e37)),
                new("R", "FLOAT", (level, x, y) => double.NaN),
            ],
        }, "FLOAT infinities, not-a-number, negative zero, the largest and smallest floats.");
    }

    private static void BuildTiles(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildTiles);
        AddGenerated(c, "tiles-zip-one-level", Function, new Description { Width = 37, Height = 21, Compression = "ZIP", Tiles = (16, 8, "ONE_LEVEL", "ROUND_DOWN"), Channels = Rgba("HALF", 8) },
            "Tiles of 16x8 pixels: the last column and row of tiles are smaller.");
        AddGenerated(c, "tiles-piz-mipmap-round-up", Function, new Description { Width = 29, Height = 19, Compression = "PIZ", Tiles = (8, 8, "MIPMAP_LEVELS", "ROUND_UP"), Channels = Rgb("HALF", 9) },
            "Mipmap levels rounded up: the full-resolution level is the image.", "The other levels are skipped.");
        AddGenerated(c, "tiles-rle-ripmap", Function, new Description { Width = 13, Height = 9, Compression = "RLE", Tiles = (4, 4, "RIPMAP_LEVELS", "ROUND_DOWN"), Channels = Rgb("FLOAT", 10) },
            "Ripmap levels.", "The offset table lists the levels by rows of levels.");
        AddGenerated(c, "tiles-none-random-order", Function, new Description { Width = 20, Height = 20, Compression = "NONE", LineOrder = "RANDOM_Y", Tiles = (6, 7, "ONE_LEVEL", "ROUND_DOWN"), Channels = Rgba("HALF", 11) },
            "Tiles stored in a shuffled order.", "The chunks are read in the order of their offsets.");
        AddGenerated(c, "tiles-b44-odd", Function, new Description { Width = 11, Height = 10, Compression = "B44", Tiles = (5, 6, "ONE_LEVEL", "ROUND_DOWN"), Channels = Rgb("HALF", 12) },
            "B44 tiles whose sizes are not multiples of 4.");
    }

    private static void BuildLayout(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildLayout);
        AddGenerated(c, "zip-decreasing-y", Function, new Description { Width = 14, Height = 40, Compression = "ZIP", LineOrder = "DECREASING_Y", Channels = Rgba("HALF", 13) },
            "Chunks stored from the bottom of the image.");
        AddGenerated(c, "piz-data-window-inside", Function, new Description
        {
            Width = 9,
            Height = 7,
            X0 = -3,
            Y0 = 2,
            DisplayWindow = [-5, -1, 8, 11],
            Compression = "PIZ",
            Channels = Rgb("HALF", 14),
        }, "A data window inside the display window, from negative coordinates.", "The pixels the data window does not cover are transparent black.");
        AddGenerated(c, "zips-other-attributes", Function, new Description
        {
            Width = 8,
            Height = 6,
            Compression = "ZIPS",
            Channels = Rgb("HALF", 15),
            Attributes =
            [
                ("comments", "string", "OpenEXR fixture of Meziantou.Framework.Imaging"),
                ("owner", "string", "CC0-1.0"),
                ("this_attribute_has_a_name_longer_than_31_bytes", "float", 1.5f),
            ],
        }, "Attributes the library skips, one with a long name.", "The long name sets the long-name flag of the version field.", ["exr.longNames"]);
    }

    private static void BuildColor(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildColor);
        foreach (var (name, primaries, chromaticities) in new[]
        {
            ("bt709", 1, new[] { 0.64f, 0.33f, 0.30f, 0.60f, 0.15f, 0.06f, 0.3127f, 0.3290f }),
            ("bt2020", 9, new[] { 0.708f, 0.292f, 0.170f, 0.797f, 0.131f, 0.046f, 0.3127f, 0.3290f }),
            ("display-p3", 12, new[] { 0.680f, 0.320f, 0.265f, 0.690f, 0.150f, 0.060f, 0.3127f, 0.3290f }),
        })
        {
            var cicp = primaries == 1 ? null : new Obj { ["colorPrimaries"] = primaries, ["transferCharacteristics"] = 8, ["matrixCoefficients"] = 0, ["fullRange"] = true };
            AddGenerated(c, "chromaticities-" + name, Function, new Description
            {
                Width = 7,
                Height = 5,
                Compression = "ZIP",
                Channels = Rgb("HALF", 16),
                Attributes = [("chromaticities", "chromaticities", chromaticities)],
            }, $"The chromaticities of {name}.", primaries == 1 ? "Linear BT.709: the transfer function label Linear." : "A CICP description with the linear transfer characteristics.", ["exr.chromaticities=" + name], cicp);
        }
    }

    private static void BuildExternal(Corpus<ExrTools> c, FullPath images)
    {
        const string Function = nameof(BuildExternal);
        foreach (var (path, notes) in new[]
        {
            ("TestImages/stripes.exr", "HALF R, G, B and A, PIZ."),
            ("TestImages/AllHalfValues.exr", "Every value of a half, infinities and not-a-number included, PIZ."),
            ("MultiResolution/ColorCodedLevels.exr", "Mipmap levels of 64x64 tiles, PXR24, and other attributes."),
            ("DisplayWindow/t01.exr", "The display window and the data window are the same, PIZ."),
        })
        {
            var data = ReadExternal(images, path);
            var name = "openexr-images-" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
            AddValid(c, name, Function, data, [], "A sample image of the OpenEXR project (" + path + ").", notes, ["exr.external"], origin: "external", source: OpenExrImagesUrl + path, license: "BSD-3-Clause");
        }
    }

    private static byte[] ReadExternal(FullPath images, string path)
    {
        var expected = ExternalImages.Single(image => image.Path == path).Sha256;
        var data = File.ReadAllBytes(images / path);
        if (Bytes.Sha256Hex(data) != expected)
            throw new FatalException($"{path} is not the file of openexr-images at {OpenExrImagesCommit}.");

        return data;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Errors
    // -----------------------------------------------------------------------------------------------------------------

    private static void AddError(Corpus<ExrTools> c, string kind, string name, string function, byte[] data, Obj expectedError, string notes, List<string>? commands = null,
        Obj? decodeOptions = null, string origin = "generated", string? source = null, string license = Corpus<ExrTools>.License, bool requireOpenExrRejection = true)
    {
        var fixtureId = kind == "limit" ? "limit/exr/" + name : "invalid/exr/" + name;
        if (requireOpenExrRejection)
            Py.Assert(Decode(c, data) is null, fixtureId + ": OpenEXR reads the file");

        var provenance = c.Provenance(origin, function, null, [$"OpenEXR {c.Tools.OpenExrVersion} (exr-from-raw, exr-to-raw)", ToolSet.RuntimeLabel], commands);
        provenance["license"] = license;
        if (source is not null)
            provenance["source"] = source;

        c.AddError(fixtureId, kind, fixtureId + ".exr", data, "exr", provenance, expectedError, decodeOptions: decodeOptions, notes: notes);
    }

    private static Obj Invalid() => new() { ["exception"] = "InvalidImageContentException", ["format"] = "OpenExr" };

    private static Obj Unsupported(string feature) => new() { ["exception"] = "UnsupportedImageFeatureException", ["format"] = "OpenExr", ["feature"] = feature };

    /// <summary>The offset of the offset table and the offsets of the chunks of a file, as exr-to-raw reads them.</summary>
    private static (int TableOffset, long[] Chunks) Chunks(Corpus<ExrTools> c, byte[] data)
    {
        var dump = Decode(c, data) ?? throw new FatalException("OpenEXR does not read the file.");
        return (dump.Part.GetProperty("chunkTableOffset").GetInt32(), [.. dump.Part.GetProperty("chunkTable").EnumerateArray().Select(value => value.GetInt64())]);
    }

    private static void BuildInvalid(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildInvalid);
        var (zip, zipCommand) = Write(c, new Description { Width = 12, Height = 40, Compression = "ZIP", Channels = Rgb("HALF", 20) });
        var (table, chunks) = Chunks(c, zip);

        AddError(c, "invalid", "truncated", Function, zip[..^9], Invalid(), "The last chunk is cut.", [zipCommand]);

        var edited = zip.ToArray();
        BinaryPrimitives.WriteInt64LittleEndian(edited.AsSpan(table + 8), zip.Length + 100);
        AddError(c, "invalid", "offset-past-end", Function, edited, Invalid(), "The offset of the second chunk is after the end of the file.", [zipCommand]);

        edited = zip.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(edited.AsSpan((int)chunks[1]), 0);
        AddError(c, "invalid", "chunk-coordinates", Function, edited, Invalid(), "The second chunk says it starts at the line 0.", [zipCommand]);

        edited = zip.ToArray();
        for (var i = 10; i < 20; i++)
            edited[chunks[0] + 8 + i] ^= 0x5A;
        AddError(c, "invalid", "zip-corrupt", Function, edited, Invalid(), "Bytes of the zlib data of the first chunk are changed.", [zipCommand]);

        // The number of bits of the Huffman data (after the bitmap and the length of the stream) is larger than the data
        var (piz, pizCommand) = Write(c, new Description { Width = 40, Height = 32, Compression = "PIZ", Channels = [new("Y", "HALF", (level, x, y) => ((x / 4) + (y / 4)) * 0.125)] });
        var (_, pizChunks) = Chunks(c, piz);
        Py.Assert(pizChunks.Length == 1 && BinaryPrimitives.ReadInt32LittleEndian(piz.AsSpan((int)pizChunks[0] + 4)) < 40 * 32 * 2, "one compressed PIZ chunk");
        edited = piz.ToArray();
        var data = (int)pizChunks[0] + 8;
        var first = BinaryPrimitives.ReadUInt16LittleEndian(edited.AsSpan(data));
        var last = BinaryPrimitives.ReadUInt16LittleEndian(edited.AsSpan(data + 2));
        var stream = data + 4 + (first <= last ? last - first + 1 : 0) + 4;
        BinaryPrimitives.WriteInt32LittleEndian(edited.AsSpan(stream + 12), 0x00FFFFFF);
        AddError(c, "invalid", "piz-huffman-bit-count", Function, edited, Invalid(), "The Huffman data of the chunk declare more bits than they have.", [pizCommand]);

        var (rle, rleCommand) = Write(c, new Description { Width = 30, Height = 2, Compression = "RLE", Channels = [new("Y", "HALF", Flat(1))] });
        var (_, rleChunks) = Chunks(c, rle);
        edited = rle.ToArray();
        edited[rleChunks[0] + 8] = 0x7F;
        AddError(c, "invalid", "rle-overrun", Function, edited, Invalid(), "The first run of the RLE data repeats a byte 128 times, more than the chunk holds.", [rleCommand]);

        edited = zip.ToArray();
        var at = Bytes.Find(edited, "channels\0"u8);
        edited[at + 7] = (byte)'z';
        AddError(c, "invalid", "missing-channels", Function, edited, Invalid(), "The channels attribute is renamed: the header has no channel list.", [zipCommand]);

    }

    private static void BuildUnsupported(Corpus<ExrTools> c, FullPath images)
    {
        const string Function = nameof(BuildUnsupported);
        foreach (var compression in new[] { "DWAA", "DWAB", "ZSTD", "HTJ2K32" })
        {
            var (data, command) = Write(c, new Description { Width = 16, Height = 16, Compression = compression, Channels = Rgb("HALF", 30) });
            AddError(c, "unsupported", "compression-" + compression.ToLowerInvariant(), Function, data, Unsupported("Compression: " + compression), $"The {compression} compression.", [command], requireOpenExrRejection: false);
        }

        void AddUnsupported(string name, Description d, string feature, string notes)
        {
            var (data, command) = Write(c, d);
            AddError(c, "unsupported", name, Function, data, Unsupported(feature), notes, [command], requireOpenExrRejection: false);
        }

        AddUnsupported("extra-channel", new Description { Width = 4, Height = 4, Compression = "ZIP", Channels = [.. Rgb("HALF", 31), new("Z", "FLOAT", Wave(31))] }, "Extra channels",
            "A Z channel next to the colors: it would be lost.");
        AddUnsupported("layer-channels", new Description { Width = 4, Height = 4, Compression = "ZIP", Channels = [new("diffuse.B", "HALF", Wave(32)), new("diffuse.G", "HALF", Wave(33)), new("diffuse.R", "HALF", Wave(34))] }, "Extra channels",
            "The channels of a layer.");
        AddUnsupported("luminance-chroma", new Description
        {
            Width = 8,
            Height = 6,
            Compression = "PIZ",
            Channels = [new("BY", "HALF", Wave(35), PLinear: true, XSampling: 2, YSampling: 2), new("RY", "HALF", Wave(36), PLinear: true, XSampling: 2, YSampling: 2), new("Y", "HALF", Smooth(37))],
        }, "Extra channels", "A luminance and two sub-sampled chroma channels.");
        AddUnsupported("data-window-outside", new Description { Width = 10, Height = 10, DisplayWindow = [2, 2, 7, 7], Compression = "ZIP", Channels = Rgb("HALF", 38) }, "Pixels outside the display window",
            "The data window is larger than the display window: its pixels outside would be lost.");
        AddUnsupported("deep-scanline", new Description { Width = 4, Height = 3, Compression = "ZIPS", Type = "deepscanline", Channels = [new("Z", "FLOAT", Flat(1))] }, "Deep data",
            "Deep data: a list of samples per pixel.");

        foreach (var (path, notes) in new[]
        {
            ("DisplayWindow/t02.exr", "The display window and the data window overlap, but are not the same."),
            ("DisplayWindow/t09.exr", "The display window and the data window do not overlap."),
            ("DisplayWindow/t13.exr", "The display window and the data window have only one pixel in common."),
        })
        {
            AddError(c, "unsupported", "openexr-images-" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), nameof(BuildUnsupported), ReadExternal(images, path), Unsupported("Pixels outside the display window"),
                notes + " A sample image of the OpenEXR project.", origin: "external", source: OpenExrImagesUrl + path, license: "BSD-3-Clause", requireOpenExrRejection: false);
        }
    }

    private static void BuildLimits(Corpus<ExrTools> c)
    {
        const string Function = nameof(BuildLimits);
        var (data, command) = Write(c, new Description { Width = 13, Height = 9, Compression = "PIZ", Channels = Rgba("HALF", 40) });
        AddError(c, "limit", "frame-pixels-over-limit", Function, data, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "FramePixels" },
            "The image has 117 pixels.", [command], new Obj { ["limits"] = new Obj { ["MaxFramePixels"] = 116 } }, requireOpenExrRejection: false);

        (data, command) = Write(c, new Description { Width = 4, Height = 4, Compression = "ZIP", Channels = Rgb("HALF", 41), Attributes = [("comments", "string", new string('x', 2000))] });
        AddError(c, "limit", "metadata-bytes-over-limit", Function, data, new Obj { ["exception"] = "ImageResourceLimitException", ["limitKind"] = "MetadataBytes" },
            "The attributes of the header are more than 1,024 bytes.", [command], new Obj { ["limits"] = new Obj { ["MaxMetadataBytes"] = 1024 } }, requireOpenExrRejection: false);
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    private sealed class ExrTools : ToolSet
    {
        public ExrTools(bool acceptVersions)
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            ExrFromRaw = Download(PrebuiltTools.ExrFromRaw);
            ExrToRaw = Download(PrebuiltTools.ExrToRaw);
            OpenExrVersion = PrebuiltTools.ExrToRaw.Version;
            Py.Assert(PrebuiltTools.ExrFromRaw.Version == OpenExrVersion, "one OpenEXR version");
            if (OpenExrVersion != PinnedOpenExr && !acceptVersions)
                throw new FatalException($"Tool versions differ from the pinned versions: OpenEXR {OpenExrVersion} (pinned {PinnedOpenExr}). Review the impact and rerun with --accept-tool-versions (then update the pinned version).");

            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Ffmpeg] = "ffmpeg",
                [ExrFromRaw] = "exr-from-raw",
                [ExrToRaw] = "exr-to-raw",
            };
        }

        public string ExrFromRaw { get; }

        public string ExrToRaw { get; }

        public string OpenExrVersion { get; }

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();
    }
}
