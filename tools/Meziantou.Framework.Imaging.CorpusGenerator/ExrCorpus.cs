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

    private static readonly string[] Compressions = ["NONE", "RLE", "ZIPS", "ZIP", "PIZ", "PXR24", "B44", "B44A", "DWAA", "DWAB", "ZSTD"];

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

        /// <summary>Gets the name of the part (required for the parts of a multi-part file).</summary>
        public string? Name { get; init; }

        /// <summary>
        /// Gets the number of samples of each pixel (x, y) of a deep part, whose channels give the value of the sample s of a
        /// pixel as their value at the level s.
        /// </summary>
        public Func<int, int, int>? DeepCounts { get; init; }

        public bool IsDeep => Type?.StartsWith("deep", StringComparison.Ordinal) == true;
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

    /// <summary>The number of samples of each pixel of a deep part, as exr-from-raw reads them.</summary>
    private static byte[] DeepCounts(Description d)
    {
        var bytes = new byte[d.Width * d.Height * 4];
        for (var y = 0; y < d.Height; y++)
        {
            for (var x = 0; x < d.Width; x++)
                BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(((y * d.Width) + x) * 4), d.DeepCounts!(x, y));
        }

        return bytes;
    }

    /// <summary>The samples of a channel, little-endian in its pixel type, for every level (for every sample of each pixel of a deep part).</summary>
    private static byte[] Samples(Description d, Channel channel)
    {
        var builder = new List<byte>();
        var level = 0;
        foreach (var (_, _, width, height) in Levels(d))
        {
            for (var y = 0; y < height; y++)
            {
                if ((d.Y0 + y) % channel.YSampling != 0)
                    continue;

                for (var x = 0; x < width; x += channel.XSampling)
                {
                    if (d.DeepCounts is null)
                    {
                        Append(channel.Value(level, x, y));
                        continue;
                    }

                    for (var sample = 0; sample < d.DeepCounts(x, y); sample++)
                        Append(channel.Value(sample, x, y));
                }
            }

            level++;
        }

        return [.. builder];

        void Append(double value)
        {
            Span<byte> sample = stackalloc byte[4];
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

    /// <summary>Writes a single-part file with exr-from-raw.</summary>
    private static (byte[] Data, string Command) Write(Corpus<ExrTools> c, Description d) => Write(c, [d], multiPart: false);

    /// <summary>Writes a file with exr-from-raw: a multi-part file when <paramref name="multiPart"/> is set.</summary>
    private static (byte[] Data, string Command) Write(Corpus<ExrTools> c, Description[] parts, bool multiPart)
    {
        Py.Assert(multiPart || parts.Length == 1, "one part");
        var directory = c.Scratch / "write";
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
        using (var stream = File.Create(directory / "description.json"))
        using (var json = new Utf8JsonWriter(stream))
        {
            if (multiPart)
            {
                json.WriteStartObject();
                json.WriteStartArray("parts");
            }

            for (var p = 0; p < parts.Length; p++)
                WritePart(json, directory, parts[p], multiPart ? string.Create(CultureInfo.InvariantCulture, $"part{p}.") : "");

            if (multiPart)
            {
                json.WriteEndArray();
                json.WriteEndObject();
            }
        }

        var output = directory / "output.exr";
        List<string> command = [c.Tools.ExrFromRaw, (directory / "description.json").Value, output.Value];
        Proc.Run(command);
        return (File.ReadAllBytes(output), CommonCorpus.Display(command, c.Tools, [((directory / "description.json").Value, "{description}.json"), (output.Value, "{output}.exr")]));
    }

    /// <summary>Writes the description of a part, and the files of its samples.</summary>
    private static void WritePart(Utf8JsonWriter json, FullPath directory, Description d, string prefix)
    {
        json.WriteStartObject();
        if (d.Type is not null)
            json.WriteString("type", d.Type);

        if (d.Name is not null)
            json.WriteString("name", d.Name);

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

        if (d.IsDeep && d.DeepCounts is not null)
        {
            var counts = prefix + "sampleCounts.raw";
            File.WriteAllBytes(directory / counts, DeepCounts(d));
            json.WriteString("sampleCounts", counts);
        }

        json.WriteStartArray("channels");
        for (var i = 0; i < d.Channels.Length; i++)
        {
            var channel = d.Channels[i];
            var file = prefix + $"channel{i.ToString(CultureInfo.InvariantCulture)}.raw";
            var hasSamples = !d.IsDeep || d.DeepCounts is not null;
            if (hasSamples)
                File.WriteAllBytes(directory / file, Samples(d, channel));

            json.WriteStartObject();
            json.WriteString("name", channel.Name);
            json.WriteString("pixelType", channel.PixelType);
            json.WriteNumber("xSampling", channel.XSampling);
            json.WriteNumber("ySampling", channel.YSampling);
            json.WriteBoolean("pLinear", channel.PLinear);
            if (hasSamples)
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

    /// <summary>
    /// The header and the samples exr-to-raw gives for a part of a file; for deep data, the number of samples of each pixel
    /// (<see cref="SampleCounts"/>), the samples of the channels being those of each pixel one after the other.
    /// </summary>
    private sealed record Dump(JsonElement Part, int[] DataWindow, int[] DisplayWindow, List<DecodedChannel> Channels, string Command, uint[]? SampleCounts = null);

    /// <summary>Decodes a part of a file with exr-to-raw; <see langword="null"/> when OpenEXR refuses it.</summary>
    private static Dump? Decode(Corpus<ExrTools> c, byte[] data, int partIndex = 0)
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
        var part = header.RootElement.GetProperty("parts")[partIndex].Clone();
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

            // A sub-sampled channel has the samples of the pixels whose coordinates are multiples of its sampling only
            var bits = new uint[bytes.Length / size];
            for (var i = 0; i < bits.Length; i++)
                bits[i] = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2 * i)) : BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * i));

            channels.Add(new DecodedChannel(channel.GetProperty("name").GetString()!, type, bits, channel.GetProperty("pLinear").GetBoolean()));
        }

        uint[]? counts = null;
        if (part.TryGetProperty("sampleCounts", out var countsFile))
        {
            var bytes = File.ReadAllBytes(directory / countsFile.GetString()!);
            counts = new uint[bytes.Length / 4];
            for (var i = 0; i < counts.Length; i++)
                counts[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4 * i));
        }

        return new Dump(part, dataWindow, displayWindow, channels, CommonCorpus.Display(command, c.Tools, [(input.Value, "{input}"), (directory.Value, "{output}")]), counts);
    }

    /// <summary>The expected pixels: RGBA floats of the display window, by the rules of the library.</summary>
    private static float[] Present(Dump dump, out bool halfPixels, out string colorModel, out int bits)
        => Present(dump, layer: null, "DisplayWindow", out halfPixels, out colorModel, out bits, out _, out _);

    /// <summary>
    /// The expected pixels of a decoding with options: RGBA floats of the display window (cropped) or of the data window,
    /// from the channels of a layer (<see langword="null"/>: every channel), by the rules of the library.
    /// </summary>
    private static float[] Present(Dump dump, string? layer, string windowMode, out bool halfPixels, out string colorModel, out int bits, out int width, out int height)
    {
        var data = dump.DataWindow;
        var image = windowMode == "DataWindow" ? data : dump.DisplayWindow;
        width = image[2] - image[0] + 1;
        height = image[3] - image[1] + 1;
        var dataWidth = data[2] - data[0] + 1;
        var result = new float[width * height * 4];

        // The channels of the layer, by their name without the layer
        var channels = layer is null
            ? dump.Channels.Select(channel => (Name: channel.Name, Channel: channel)).ToList()
            : [.. dump.Channels.Select(channel => (Name: LayerChannel(channel.Name, layer), Channel: channel)).Where(item => item.Name is "R" or "G" or "B" or "A" or "Y")];
        Py.Assert(channels.Count > 0, "the layer has channels");
        var hasAlpha = channels.Any(channel => channel.Name == "A");
        var isLuminance = channels.Any(channel => channel.Name == "Y");
        var halfOnly = channels.All(channel => channel.Channel.PixelType == "HALF");
        halfPixels = halfOnly && !hasAlpha;
        colorModel = isLuminance ? hasAlpha ? "GrayscaleAlpha" : "Grayscale" : hasAlpha ? "Rgba" : "Rgb";
        bits = halfOnly ? 16 : 32;
        for (var y = Math.Max(data[1], image[1]); y <= Math.Min(data[3], image[3]); y++)
        {
            for (var x = Math.Max(data[0], image[0]); x <= Math.Min(data[2], image[2]); x++)
            {
                var source = ((y - data[1]) * dataWidth) + (x - data[0]);
                var target = (((y - image[1]) * width) + (x - image[0])) * 4;
                result[target + 3] = 1f;
                foreach (var (name, channel) in channels)
                {
                    var value = channel[source];
                    switch (name)
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
        var largest = 0.0;
        for (var p = 0; p < planeOrder.Length; p++)
        {
            var channel = dump.Channels.First(item => item.Name == planeOrder[p]);
            for (var i = 0; i < plane; i++)
            {
                var offset = ((p * plane) + i) * size;
                var bits = half ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(offset));
                if (bits != channel.Bits[i])
                {
                    differing++;
                    var value = half ? (double)BitConverter.UInt16BitsToHalf((ushort)bits) : BitConverter.UInt32BitsToSingle(bits);
                    largest = Math.Max(largest, Math.Abs(value - channel[i]));
                }
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
                : dump.Part.GetProperty("compression").GetString() is "DWAA" or "DWAB"
                    ? string.Create(CultureInfo.InvariantCulture, $"{differing} stored samples differ from OpenEXR, by at most {largest:G4}: FFmpeg has its own DWA decoder, which does not reproduce the arithmetic of OpenEXR.")
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
        if (dump.SampleCounts is not null)
            return DeepPatternCrossCheck(d, dump);

        var channels = d.Channels.Where(channel => !IsLossy(d.Compression, channel) && channel.XSampling == 1 && channel.YSampling == 1).ToList();
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

    /// <summary>
    /// Determines whether a compression loses the samples of a channel: FLOAT samples of PXR24, HALF samples of B44, and the
    /// colors (R, G, B, Y, RY, BY of type HALF or FLOAT) that DWA codes with its DCT (compression notes, section 8.3).
    /// </summary>
    private static bool IsLossy(string compression, Channel channel) => compression switch
    {
        "PXR24" => channel.PixelType == "FLOAT",
        "B44" or "B44A" => channel.PixelType == "HALF",
        "DWAA" or "DWAB" => channel.PixelType != "UINT" && channel.Name[(channel.Name.LastIndexOf('.', StringComparison.Ordinal) + 1)..] is "R" or "G" or "B" or "Y" or "RY" or "BY",
        _ => false,
    };

    /// <summary>Compares the deep samples and the sample counts OpenEXR decodes with the pattern given to exr-from-raw.</summary>
    private static Obj DeepPatternCrossCheck(Description d, Dump dump)
    {
        var differing = 0;
        var counts = DeepCounts(d);
        for (var i = 0; i < dump.SampleCounts!.Length; i++)
        {
            if (dump.SampleCounts[i] != BinaryPrimitives.ReadUInt32LittleEndian(counts.AsSpan(4 * i)))
                differing++;
        }

        foreach (var channel in d.Channels)
        {
            var decoded = dump.Channels.Single(item => item.Name == channel.Name);
            var samples = Samples(d, channel);
            var size = channel.PixelType == "HALF" ? 2 : 4;
            Py.Assert(samples.Length / size == decoded.Bits.Length, "the number of deep samples");
            for (var i = 0; i < decoded.Bits.Length; i++)
            {
                var bits = size == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(samples.AsSpan(2 * i)) : BinaryPrimitives.ReadUInt32LittleEndian(samples.AsSpan(4 * i));
                if (bits != decoded.Bits[i])
                    differing++;
            }
        }

        var check = new Obj
        {
            ["decoder"] = "the pattern given to exr-from-raw",
            ["result"] = differing == 0 ? "exact" : "differs",
            ["notes"] = differing == 0
                ? "The number of samples of every pixel and the samples of every channel, bit for bit: the compression keeps them. The expected pixels flatten them as OpenEXR does (compression notes, section 1.7)."
                : string.Create(CultureInfo.InvariantCulture, $"{differing} sample counts or samples differ from the pattern."),
        };
        if (differing == 0)
        {
            check["maxAbsoluteError"] = 0;
            check["meanAbsoluteError"] = 0.0;
        }

        return check;
    }

    /// <summary>
    /// Flattens a deep dump as OpenEXR flattens the samples of one source (compression notes, section 1.7): the samples of a
    /// pixel in their stored order, each adding (1 - alpha) times its values, in single precision, until alpha reaches 1.
    /// </summary>
    /// <returns>A dump of FLOAT channels of one sample per pixel: the composited channels, without Z and ZBack.</returns>
    private static Dump Flatten(Dump dump)
    {
        var counts = dump.SampleCounts!;
        var alpha = dump.Channels.Single(channel => channel.Name == "A");
        var channels = dump.Channels.Where(channel => channel.Name is not ("Z" or "ZBack")).ToList();
        var outputs = channels.Select(_ => new uint[counts.Length]).ToList();
        var values = new float[channels.Count];
        var alphaIndex = channels.IndexOf(alpha);
        var first = 0;
        for (var pixel = 0; pixel < counts.Length; pixel++)
        {
            Array.Clear(values);
            for (var s = first; s < first + counts[pixel]; s++)
            {
                var accumulated = values[alphaIndex];
                if (accumulated >= 1f)
                    break;

                var weight = 1f - accumulated;
                for (var c = 0; c < channels.Count; c++)
                {
                    var product = weight * channels[c][s];
                    values[c] += product;
                }
            }

            for (var c = 0; c < channels.Count; c++)
                outputs[c][pixel] = BitConverter.SingleToUInt32Bits(values[c]);

            first += (int)counts[pixel];
        }

        return dump with
        {
            Channels = [.. channels.Select((channel, c) => new DecodedChannel(channel.Name, "FLOAT", outputs[c], channel.PLinear))],
            SampleCounts = null,
        };
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

    private static Obj FloatBuffer(Corpus<ExrTools> c, string path, int width, int height, float[] pixels)
    {
        var bytes = new byte[pixels.Length * 4];
        for (var i = 0; i < pixels.Length; i++)
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(4 * i), pixels[i]);

        var entry = c.Write(path, bytes);
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
        expected["frames"] = new List<object?> { new Obj { ["duration"] = "0/1", ["buffers"] = new List<object?> { FloatBuffer(c, fixtureId + ".frame-0.rgbaf32le.raw", width, height, pixels) } } };
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
        // DWA: tiles (deflate AC), a colour set of mixed types, a luminance; ZSTD: tiles, two segments of 2-byte and 4-byte samples
        AddGenerated(c, "dwaa-tiles", Function, new Description { Width = 29, Height = 21, Compression = "DWAA", Tiles = (16, 16, "ONE_LEVEL", "ROUND_DOWN"), Channels = Rgba("HALF", 70) },
            "Tiles of 16x16 pixels of HALF R, G, B and A, DWAA.", "OpenEXR compresses the AC coefficients of DWAA tiles with zlib, and those of scan lines with Huffman codes.");
        AddGenerated(c, "dwab-mixed-types", Function, new Description { Width = 27, Height = 13, Compression = "DWAB", Channels = [new("A", "FLOAT", Alpha), new("B", "HALF", Wave(71)), new("G", "FLOAT", Smooth(72)), new("R", "HALF", Wave(73, 8))] },
            "HALF R and B, FLOAT G and A, DWAB.", "R, G and B of different types are one colour set, coded through the luma-chroma transform; A is run-length coded.");
        AddGenerated(c, "dwaa-luminance-alpha", Function, new Description { Width = 19, Height = 35, Compression = "DWAA", Channels = [new("A", "HALF", Alpha), new("Y", "HALF", Smooth(74))] },
            "HALF Y and A, DWAA.", "Y is coded alone with the DCT, A with run-length coding.");
        AddGenerated(c, "zstd-tiles", Function, new Description { Width = 21, Height = 17, Compression = "ZSTD", Tiles = (8, 8, "ONE_LEVEL", "ROUND_DOWN"), Channels = Rgba("HALF", 75) },
            "Tiles of 8x8 pixels of HALF R, G, B and A, ZSTD.");
        AddGenerated(c, "zstd-mixed-types", Function, new Description { Width = 15, Height = 9, Compression = "ZSTD", Channels = [new("A", "HALF", Alpha), new("B", "FLOAT", Wave(76)), new("G", "HALF", Smooth(77)), new("R", "FLOAT", Wave(78, 8))] },
            "HALF A and G, FLOAT B and R, ZSTD.", "The 2-byte samples and the 4-byte samples are two segments of the Zstandard data.");
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
        Obj? decodeOptions = null, string origin = "generated", string? source = null, string license = Corpus<ExrTools>.License, bool requireOpenExrRejection = true,
        ExrDecoding[]? decodings = null, Description? pattern = null)
    {
        var fixtureId = kind == "limit" ? "limit/exr/" + name : "invalid/exr/" + name;
        if (requireOpenExrRejection)
            Py.Assert(Decode(c, data) is null, fixtureId + ": OpenEXR reads the file");

        var provenance = c.Provenance(origin, function, null, [$"OpenEXR {c.Tools.OpenExrVersion} (exr-from-raw, exr-to-raw)", ToolSet.RuntimeLabel], commands);
        provenance["license"] = license;
        if (source is not null)
            provenance["source"] = source;

        var entries = decodings is null ? null : Decodings(c, fixtureId, data, decodings, pattern);
        c.AddError(fixtureId, kind, fixtureId + ".exr", data, "exr", provenance, expectedError, decodeOptions: decodeOptions, notes: notes, exrDecodings: entries);
    }

    /// <summary>A decoding with options of an OpenEXR file the default options reject: its pixels, or the error it still gives.</summary>
    private sealed record ExrDecoding(string? Layer, string WindowMode, string? Feature = null)
    {
        /// <summary>Gets the name of the part, and its index in the file.</summary>
        public (string Name, int Index)? Part { get; init; }

        /// <summary>Gets a value indicating whether deep data are flattened.</summary>
        public bool Flatten { get; init; }

        /// <summary>Gets the pattern the part was written from, when it is not the one of the fixture.</summary>
        public Description? Pattern { get; init; }
    }

    private static ExrDecoding Layer(string layer, string windowMode = "DisplayWindow") => new(layer, windowMode);

    private static ExrDecoding Window(string windowMode, string? layer = null) => new(layer, windowMode);

    private static ExrDecoding Rejected(string? layer, string feature, string windowMode = "DisplayWindow") => new(layer, windowMode, feature);

    /// <summary>The manifest entries of the decodings of a file: what OpenEXR decodes, presented by the rules of the library.</summary>
    private static List<object?> Decodings(Corpus<ExrTools> c, string fixtureId, byte[] data, ExrDecoding[] decodings, Description? pattern)
    {
        var result = new List<object?>();
        for (var i = 0; i < decodings.Length; i++)
        {
            var decoding = decodings[i];
            var entry = new Obj();
            if (decoding.Part is { } selected)
                entry["part"] = selected.Name;

            if (decoding.Flatten)
                entry["deepData"] = "Flatten";

            entry["layer"] = decoding.Layer;
            entry["windowMode"] = decoding.WindowMode;
            if (decoding.Feature is { } feature)
            {
                entry["expectedError"] = Unsupported(feature);
                result.Add(entry);
                continue;
            }

            var dump = Decode(c, data, decoding.Part?.Index ?? 0) ?? throw new FatalException(fixtureId + ": OpenEXR does not read the file.");
            var checks = new List<object?>();
            var decodingPattern = decoding.Pattern ?? pattern;
            if (decodingPattern is not null && PatternCrossCheck(decodingPattern, dump) is { } patternCheck)
                checks.Add(patternCheck);

            var halfOnly = true;
            if (dump.SampleCounts is not null)
            {
                Py.Assert(decoding.Flatten, fixtureId + ": deep data are flattened");
                halfOnly = dump.Channels.Where(channel => channel.Name is not ("Z" or "ZBack") && (decoding.Layer is null || LayerChannel(channel.Name, decoding.Layer) is not null)).All(channel => channel.PixelType == "HALF");
                dump = Flatten(dump);
            }
            else if (decoding.Part is null && FfmpegDecodingCrossCheck(c, data, dump, decoding) is { } ffmpegCheck)
            {
                checks.Add(ffmpegCheck);
            }

            var pixels = Present(dump, decoding.Layer, decoding.WindowMode, out var halfPixels, out var colorModel, out var bits, out var width, out var height);
            if (decoding.Flatten)
            {
                halfPixels = false;
                bits = halfOnly ? 16 : 32;
            }


            Py.Assert(checks.Count > 0, fixtureId + ": no independent cross-check");
            var options = decoding.Layer switch
            {
                null => "every channel",
                "" => "the default layer",
                var layer => $"the layer '{layer}'",
            };
            if (decoding.Part is { } part)
                options = $"the part '{part.Name}', " + options;

            if (decoding.Flatten)
                options = "the deep samples of each pixel flattened in their stored order (compression notes, section 1.7), " + options;
            entry["width"] = width;
            entry["height"] = height;
            entry["pixelFormat"] = halfPixels ? "RgbaHalf" : "RgbaFloat";
            entry["colorModel"] = colorModel;
            entry["bitsPerComponent"] = bits;
            entry["buffer"] = FloatBuffer(c, fixtureId + string.Create(CultureInfo.InvariantCulture, $".decoding-{i}.rgbaf32le.raw"), width, height, pixels);
            entry["reference"] = new Obj
            {
                ["method"] = "decoded",
                ["description"] = $"The samples OpenEXR decodes, presented by the rules of the library: {options}, the {(decoding.WindowMode == "DataWindow" ? "data window" : "display window")} is the image{(decoding.WindowMode == "CropToDisplayWindow" ? " (the pixels of the data window outside it are cropped)" : "")}, the other channels are ignored, the colors are divided by alpha.",
                ["decoder"] = $"OpenEXR {c.Tools.OpenExrVersion} (exr-to-raw)",
                ["backend"] = "exr-to-raw of the meziantou/prebuilt release: the decoded samples of every channel, in their own type, bit for bit",
                ["command"] = dump.Command,
                ["crossChecks"] = checks,
            };
            entry["comparison"] = new Obj { ["mode"] = "exact" };
            result.Add(entry);
        }

        return result;
    }

    /// <summary>
    /// Decodes a file with FFmpeg, which selects a layer (its <c>layer</c> option), and compares the samples it stores with
    /// those of OpenEXR where the data window covers the image. FFmpeg 9.0.2 gives an image of the size of the display
    /// window, but places the data window at its coordinates from (0, 0) whatever the origin of the display window: when the
    /// data window has no negative coordinate, FFmpeg reads a copy of the file whose display window is (0, 0) to the
    /// bottom-right corner of the data window, which shows every sample at its coordinates; otherwise the file itself, when
    /// its display window is the image and starts at (0, 0). FFmpeg reads the channels R, G and B with or without A, or Y, of
    /// one type, and no sub-sampled channel.
    /// </summary>
    private static Obj? FfmpegDecodingCrossCheck(Corpus<ExrTools> c, byte[] data, Dump dump, ExrDecoding decoding)
    {
        var layer = decoding.Layer ?? "";
        var channels = dump.Channels.Select(channel => (Name: LayerChannel(channel.Name, layer), Channel: channel)).Where(item => item.Name is "R" or "G" or "B" or "A" or "Y").ToList();
        var names = string.Concat(channels.Select(channel => channel.Name).Order(StringComparer.Ordinal));
        var types = channels.Select(channel => channel.Channel.PixelType).Distinct(StringComparer.Ordinal).ToList();
        if (names is not ("BGR" or "ABGR" or "Y") || types.Count != 1 || types[0] == "UINT" || dump.Part.GetProperty("channels").EnumerateArray().Any(channel => channel.GetProperty("xSampling").GetInt32() != 1 || channel.GetProperty("ySampling").GetInt32() != 1))
            return null;

        // The frame of FFmpeg: the pixels (0, 0) to (window[2], window[3]) of the file
        var input = data;
        int[] window;
        var image = decoding.WindowMode == "DataWindow" ? dump.DataWindow : dump.DisplayWindow;
        if (dump.DataWindow[0] >= 0 && dump.DataWindow[1] >= 0)
        {
            // The display window of the copy: its box2i value follows the name, the type and the size of the attribute
            window = [0, 0, dump.DataWindow[2], dump.DataWindow[3]];
            input = data.ToArray();
            var at = Bytes.Find(input, "displayWindow\0box2i\0"u8) + "displayWindow\0box2i\0"u8.Length + 4;
            for (var i = 0; i < 4; i++)
                BinaryPrimitives.WriteInt32LittleEndian(input.AsSpan(at + (4 * i)), window[i]);
        }
        else if (decoding.WindowMode != "DataWindow" && dump.DisplayWindow[0] == 0 && dump.DisplayWindow[1] == 0)
        {
            window = dump.DisplayWindow;
        }
        else
        {
            return null;
        }

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
        var planeOrder = names == "Y" ? ["Y"] : names == "BGR" ? new[] { "G", "B", "R" } : ["G", "B", "R", "A"];
        var width = window[2] - window[0] + 1;
        var height = window[3] - window[1] + 1;
        var size = half ? 2 : 4;
        var path = c.Scratch / "ffmpeg-decoding.exr";
        File.WriteAllBytes(path, input);
        var check = new Obj { ["decoder"] = $"{c.Tools.FfmpegLabel} (libavcodec exr decoder)" };
        byte[] frame;
        try
        {
            List<string> command = c.Tools.FfmpegCmd([.. (layer.Length > 0 ? ["-layer", layer] : Array.Empty<string>()), "-noautorotate", "-i", path.Value, .. CommonCorpus.FfmpegDecodeFlags, "-pix_fmt", pixelFormat, "-"]);
            frame = Proc.Run(command);
            check["command"] = CommonCorpus.Display(command, c.Tools, [(path.Value, ReferenceEquals(input, data) ? "{input}" : "{input with the display window (0, 0) - (dataWindow.max)}")]);
            if (frame.Length != width * height * size * planeOrder.Length)
                throw new ToolException(string.Create(CultureInfo.InvariantCulture, $"ffmpeg produced {frame.Length} bytes, not a {width}x{height} {pixelFormat} frame"));
        }
        catch (ToolException error)
        {
            // The first line of the error output, without the paths of the command
            var lines = error.Message.Split('\n');
            var message = lines.Length > 1 ? System.Text.RegularExpressions.Regex.Replace(lines[1], @"^(\[[^\]]*\]\s*)+", "", System.Text.RegularExpressions.RegexOptions.ExplicitCapture, TimeSpan.FromSeconds(1)) : "no error output";
            check["result"] = "differs";
            check["excludedFromTolerance"] = true;
            check["notes"] = "FFmpeg failed to decode the input: " + message;
            return check;
        }

        var (data0, plane) = (dump.DataWindow, width * height);
        var dataWidth = data0[2] - data0[0] + 1;
        var (compared, differing) = (0, 0);
        for (var p = 0; p < planeOrder.Length; p++)
        {
            var channel = channels.First(item => item.Name == planeOrder[p]).Channel;
            for (var y = Math.Max(data0[1], image[1]); y <= Math.Min(data0[3], image[3]); y++)
            {
                for (var x = Math.Max(data0[0], image[0]); x <= Math.Min(data0[2], image[2]); x++)
                {
                    var offset = ((p * plane) + ((y - window[1]) * width) + (x - window[0])) * size;
                    var bits = half ? BinaryPrimitives.ReadUInt16LittleEndian(frame.AsSpan(offset)) : BinaryPrimitives.ReadUInt32LittleEndian(frame.AsSpan(offset));
                    compared++;
                    if (bits != channel.Bits[((y - data0[1]) * dataWidth) + (x - data0[0])])
                        differing++;
                }
            }
        }

        if (differing == 0)
        {
            check["result"] = "exact";
            check["maxAbsoluteError"] = 0;
            check["meanAbsoluteError"] = 0.0;
            check["notes"] = string.Create(CultureInfo.InvariantCulture, $"The {compared} stored samples of the selected channels that the data window gives to the image (FFmpeg does not divide the colors by alpha), bit for bit.");
        }
        else
        {
            check["result"] = "differs";
            check["excludedFromTolerance"] = true;
            check["notes"] = string.Create(CultureInfo.InvariantCulture, $"{differing} of the {compared} stored samples of the selected channels differ from OpenEXR.");
        }

        return check;
    }

    /// <summary>The name of a channel without its layer, or <see langword="null"/> when it is not a channel of the layer.</summary>
    private static string? LayerChannel(string name, string layer)
    {
        var dot = name.LastIndexOf('.', StringComparison.Ordinal);
        var channelLayer = dot < 0 ? "" : name[..dot];
        return channelLayer == layer ? name[(dot + 1)..] : null;
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
        foreach (var compression in new[] { "HTJ2K256", "HTJ2K32" })
        {
            var (data, command) = Write(c, new Description { Width = 16, Height = 16, Compression = compression, Channels = Rgb("HALF", 30) });
            AddError(c, "unsupported", "compression-" + compression.ToLowerInvariant(), Function, data, Unsupported("Compression: " + compression), $"The {compression} compression.", [command], requireOpenExrRejection: false);
        }

        void AddUnsupported(string name, Description d, string feature, string notes, params ExrDecoding[] decodings)
        {
            var (data, command) = Write(c, d);
            AddError(c, "unsupported", name, Function, data, Unsupported(feature), notes, [command], requireOpenExrRejection: false, decodings: decodings.Length == 0 ? null : decodings, pattern: d);
        }

        AddUnsupported("extra-channel", new Description { Width = 4, Height = 4, Compression = "ZIP", Channels = [.. Rgb("HALF", 31), new("Z", "FLOAT", Wave(31))] }, "Extra channels",
            "A Z channel next to the colors: it would be lost. The default layer is decoded without it.", Layer(""));
        AddUnsupported("layer-channels", new Description { Width = 4, Height = 4, Compression = "ZIP", Channels = [new("diffuse.B", "HALF", Wave(32)), new("diffuse.G", "HALF", Wave(33)), new("diffuse.R", "HALF", Wave(34))] }, "Extra channels",
            "The channels of a layer. The layer is decoded when it is selected; the default layer has no channel.", Layer("diffuse"), Rejected("", "Missing layer"), Rejected("diffuse.R", "Missing layer"));
        AddUnsupported("luminance-chroma", new Description
        {
            Width = 8,
            Height = 6,
            Compression = "PIZ",
            Channels = [new("BY", "HALF", Wave(35), PLinear: true, XSampling: 2, YSampling: 2), new("RY", "HALF", Wave(36), PLinear: true, XSampling: 2, YSampling: 2), new("Y", "HALF", Smooth(37))],
        }, "Extra channels", "A luminance and two sub-sampled chroma channels: the chroma channels are not decoded, also in a layer.", Rejected("", "Luminance-chroma channels"));
        AddUnsupported("data-window-outside", new Description { Width = 10, Height = 10, DisplayWindow = [2, 2, 7, 7], Compression = "ZIP", Channels = Rgb("HALF", 38) }, "Pixels outside the display window",
            "The data window is larger than the display window: its pixels outside would be lost. They are cropped, or the data window is the image.", Window("CropToDisplayWindow"), Window("DataWindow"));

        // Renderer outputs: colors next to depth, a layer of other outputs, an overscan, in every compression the decoder reads
        foreach (var compression in Compressions)
        {
            // The colors are of a type the compression keeps exactly, so that the pattern checks them: B44 packs HALF samples
            var type = compression is "B44" or "B44A" ? "FLOAT" : "HALF";
            AddUnsupported("overscan-layers-" + compression.ToLowerInvariant(), new Description
            {
                Width = 13,
                Height = 11,
                X0 = -2,
                Y0 = -3,
                DisplayWindow = [0, 0, 8, 5],
                Compression = compression,
                Channels = [.. Rgba(type, 50), new("Z", "FLOAT", Smooth(51)), new("N.X", type, Wave(52)), new("N.Y", type, Wave(53)), new("N.Z", type, Wave(54))],
            }, "Extra channels", $"RGBA, a depth and the normals of a renderer (N.X, N.Y, N.Z), {compression}, with an overscan of 2 to 4 pixels around the display window. The layer N is a vector: its N.Y is not a luminance.",
                Layer("", "CropToDisplayWindow"), Layer("", "DataWindow"), Rejected("N", "Ambiguous layer", "CropToDisplayWindow"), Rejected("", "Pixels outside the display window"), Rejected("P", "Missing layer", "CropToDisplayWindow"));
        }

        AddUnsupported("tiles-layers-overscan", new Description
        {
            Width = 37,
            Height = 21,
            X0 = -5,
            Y0 = -2,
            DisplayWindow = [0, 0, 25, 14],
            Compression = "PIZ",
            Tiles = (8, 8, "ONE_LEVEL", "ROUND_DOWN"),
            Channels = [.. Rgb("HALF", 55), new("Z", "FLOAT", Smooth(56)), new("diffuse.A", "HALF", Alpha), new("diffuse.B", "FLOAT", Wave(57)), new("diffuse.G", "FLOAT", Wave(58)), new("diffuse.R", "FLOAT", Wave(59)), new("shadow.Y", "HALF", Smooth(60))],
        }, "Extra channels", "Tiles of 8x8 pixels, PIZ: the default layer (HALF RGB), a depth, a diffuse layer (FLOAT RGB and HALF alpha) and a shadow layer (HALF luminance), with an overscan.",
            Layer("", "CropToDisplayWindow"), Layer("diffuse", "CropToDisplayWindow"), Layer("shadow", "DataWindow"), Layer("diffuse", "DataWindow"));
        AddUnsupported("tiles-mipmap-layers", new Description
        {
            Width = 19,
            Height = 13,
            Compression = "ZIP",
            Tiles = (8, 4, "MIPMAP_LEVELS", "ROUND_DOWN"),
            Channels = [new("beauty.B", "HALF", Wave(61)), new("beauty.G", "HALF", Wave(62)), new("beauty.R", "HALF", Wave(63)), new("depth.Z", "FLOAT", Smooth(64))],
        }, "Extra channels", "Mipmap levels of ZIP tiles holding a beauty layer and a depth layer, and no default layer: the full-resolution level of the layer is decoded.",
            Layer("beauty"), Rejected("", "Missing layer"), Rejected("depth", "Missing layer"));
        AddUnsupported("subsampled-extra-channel", new Description
        {
            Width = 12,
            Height = 8,
            Compression = "RLE",
            Channels = [.. Rgb("HALF", 65), new("C", "HALF", Wave(66), XSampling: 2, YSampling: 2)],
        }, "Extra channels", "A channel sub-sampled by 2 along x and y next to the colors: the lines of the chunks do not all have its samples.", Layer(""));
        // Multi-part files: the parts are decoded one by one
        void AddMultipart(string name, Description[] parts, string notes, params ExrDecoding[] decodings)
        {
            var (data, command) = Write(c, parts, multiPart: true);
            decodings = [.. decodings.Select(decoding => decoding.Part is { Index: >= 0 } part ? decoding with { Pattern = parts[part.Index] } : decoding)];
            AddError(c, "unsupported", name, Function, data, Unsupported("Multi-part file"), notes, [command], requireOpenExrRejection: false, decodings: decodings);
        }

        ExrDecoding Part(string name, int index, string? layer = null) => new(layer, "DisplayWindow") { Part = (name, index) };

        AddMultipart("multipart-stereo",
            [
                new Description { Name = "left", Width = 17, Height = 11, Compression = "PIZ", Channels = Rgba("HALF", 80), Attributes = [("view", "string", "left")] },
                new Description { Name = "right", Width = 17, Height = 11, Compression = "ZSTD", Channels = Rgb("FLOAT", 81), Attributes = [("view", "string", "right")] },
            ],
            "The two views of a stereo pair, PIZ and ZSTD: each part is decoded when it is selected.",
            Part("left", 0), Part("right", 1), new ExrDecoding(null, "DisplayWindow", "Missing part") { Part = ("center", -1) });
        AddMultipart("multipart-render-passes",
            [
                new Description { Name = "beauty", Width = 14, Height = 10, Compression = "ZIP", Channels = Rgba("HALF", 82) },
                new Description { Name = "depth", Width = 14, Height = 10, Compression = "ZIPS", Channels = [new("Z", "FLOAT", Smooth(83))] },
                new Description { Name = "normals", Width = 14, Height = 10, Compression = "RLE", Tiles = (8, 8, "ONE_LEVEL", "ROUND_DOWN"), Channels = [new("N.X", "HALF", Wave(84)), new("N.Y", "HALF", Wave(85)), new("N.Z", "HALF", Wave(86)), new("diffuse.B", "HALF", Wave(87)), new("diffuse.G", "HALF", Wave(88)), new("diffuse.R", "HALF", Wave(89))] },
            ],
            "The outputs of a renderer in three parts (scan lines and tiles): the beauty pass, a depth, normals and a diffuse layer.",
            Part("beauty", 0), Part("normals", 2, "diffuse"), new ExrDecoding(null, "DisplayWindow", "Extra channels") { Part = ("depth", 1) },
            new ExrDecoding("N", "DisplayWindow", "Ambiguous layer") { Part = ("normals", 2) });

        // Deep data: flattened when it is selected, in every compression deep data allow, scan lines and clipped tiles
        static int Counts(int x, int y) => ((x * 7) + (y * 3)) % 5 == 4 ? 0 : (((x * 7) + (y * 3)) % 5) + 1;
        static double DeepAlpha(int s, int x, int y) => ((s + x + y) % 4) switch { 0 => 0.25, 1 => 0.5, 2 => 1, _ => 0.75 };
        Channel[] DeepRgba(int seed) => [new("A", "HALF", DeepAlpha), new("B", "HALF", Wave(seed)), new("G", "FLOAT", Smooth(seed)), new("R", "HALF", Wave(seed + 1, 8)), new("Z", "FLOAT", (s, x, y) => 1 + s + (((x + y) % 3) * 0.25))];
        var flatten = new ExrDecoding(null, "DisplayWindow") { Flatten = true };
        foreach (var compression in new[] { "NONE", "RLE", "ZIPS", "ZSTD" })
        {
            AddUnsupported("deep-scanline-" + compression.ToLowerInvariant(), new Description { Width = 13, Height = 7, Compression = compression, Type = "deepscanline", DeepCounts = Counts, Channels = DeepRgba(90) }, "Deep data",
                $"Deep scan lines of 0 to 5 samples per pixel, {compression}: HALF A, B and R, FLOAT G and Z; samples whose accumulated alpha reaches 1 hide those behind them.", flatten);
        }

        foreach (var compression in new[] { "RLE", "ZSTD" })
        {
            AddUnsupported("deep-tiles-" + compression.ToLowerInvariant(), new Description { Width = 13, Height = 7, Compression = compression, Type = "deeptile", Tiles = (5, 4, "ONE_LEVEL", "ROUND_DOWN"), DeepCounts = Counts, Channels = DeepRgba(91) }, "Deep data",
                $"Deep tiles of 5x4 pixels clipped at the right and bottom edges, {compression}.", flatten);
        }

        AddUnsupported("deep-layers-zips", new Description
        {
            Width = 9,
            Height = 6,
            Compression = "ZIPS",
            Type = "deepscanline",
            DeepCounts = Counts,
            Channels = [.. DeepRgba(92), new("ZBack", "FLOAT", (s, x, y) => 1.5 + s), new("spec.Y", "HALF", Wave(93))],
        }, "Deep data", "Deep scan lines with ZBack and another layer: flattening needs the layer option to ignore it.", flatten with { Feature = "Extra channels" }, flatten with { Layer = "" });
        AddUnsupported("deep-without-alpha", new Description { Width = 5, Height = 4, Compression = "NONE", Type = "deepscanline", DeepCounts = Counts, Channels = [new("R", "HALF", Wave(94)), new("Z", "FLOAT", (s, x, y) => s)] }, "Deep data",
            "Deep data without alpha: OpenEXR does not flatten them either.", flatten with { Feature = "Deep data without depth or alpha" });

        foreach (var (path, notes) in new[]
        {
            ("DisplayWindow/t02.exr", "The display window and the data window overlap, but are not the same."),
            ("DisplayWindow/t09.exr", "The display window and the data window do not overlap."),
            ("DisplayWindow/t13.exr", "The display window and the data window have only one pixel in common."),
        })
        {
            AddError(c, "unsupported", "openexr-images-" + Path.GetFileNameWithoutExtension(path).ToLowerInvariant(), nameof(BuildUnsupported), ReadExternal(images, path), Unsupported("Pixels outside the display window"),
                notes + " A sample image of the OpenEXR project.", origin: "external", source: OpenExrImagesUrl + path, license: "BSD-3-Clause", requireOpenExrRejection: false,
                decodings: [Window("CropToDisplayWindow"), Window("DataWindow")]);
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
