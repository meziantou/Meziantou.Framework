using System.Text;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Framework.Imaging.TestHarness.Avif;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

// Spatial layers (AV1 specification, sections 6.2.3 "OBU extension semantics", 6.4.1 "General sequence header OBU
// semantics" and 7.18.1 "Output process"): the AV1 vectors with several spatial layers, and the helpers the AVIF container
// generator shares with them.
//
// No encoder option of the pinned FFmpeg codes spatial layers. libavif does, with libaom: "avifenc --layered" codes one
// input per layer and "avifenc --progressive" two layers of one input, as one AV1 temporal unit that is the data of the
// coded item. A layered vector is that payload (a temporal delimiter, a sequence header that declares one operating point
// per number of layers, one frame per layer), extracted from the file by ReferenceAvif. It is recorded like any vector:
// headers and tags by trace_headers, digests only when dav1d and libaom agree.
//
// What the two decoders output for such a stream, through FFmpeg and by default: ONE frame per temporal unit, the shown
// frame of the highest spatial layer the unit holds (dav1d: "all layers" off; libaom: "output all layers" off). The
// recorded frames are those. The shown frames of the lower layers have no digest of their own in a layered vector; they
// have one in the vectors that are the same stream without the units of its higher layers (KeptLayers), which both
// decoders decode like any stream: the frame of the highest layer that is left.
//
// Traps:
// - avifenc accepts 2 to 4 inputs with --layered (AV1 has 4 spatial layers) and refuses --layered and --progressive with
//   a grid ("Layered grid image unimplemented in avifenc"): the layered grid of the container fixtures is assembled;
// - --progressive is two layers of one input: the first one at half the size and at quality 10 (--scaling-mode 1/2);
// - a lower layer that is smaller is coded with frame_size_override_flag and a render size of the full size, and the
//   next layer predicts from it with reference scaling;
// - avifenc writes one extent per layer and an a1lx property (the sizes of the layers); it writes neither a1op nor lsel;
// - the first layer is a shown key frame and the next ones are shown inter frames (frame_type 1) of the same temporal
//   unit, which refresh the slot the next layer predicts from.
internal static partial class AvifCorpus
{
    /// <summary>One temporal unit of a layered vector: an avifenc command, whose tokens <c>@source0.png</c>,
    /// <c>@source1.png</c>... name the patterns it encodes.</summary>
    private sealed record LayeredUnit(string Arguments, params Img[] Sources);

    /// <param name="Name">The file name without extension; the vector id is <c>av1/Name</c>.</param>
    /// <param name="PixelFormat">The FFmpeg pixel format of the planes of the stream.</param>
    /// <param name="Units">The temporal units of the stream, each one the payload of the coded item of an avifenc file.</param>
    /// <param name="Requires">The feature tags the stream must signal.</param>
    private sealed record LayeredVector(string Name, string PixelFormat, LayeredUnit[] Units, params string[] Requires)
    {
        /// <summary>
        /// The number of spatial layers kept, from the first one: the units of the higher layers are removed from the
        /// payload (section 5.3.1 "General OBU syntax": a decoder drops the units outside its operating point, and nothing
        /// in a layer refers to a higher one). <see langword="null"/>: the whole payload.
        /// </summary>
        public int? KeptLayers { get; init; }

        /// <summary>The name of the vector that holds every layer of the same payload, when <see cref="KeptLayers"/> is set.</summary>
        public string? TruncationOf { get; init; }
    }

    /// <summary>The first layers of a layered vector: the same avifenc file, without the units of the higher layers.</summary>
    private static LayeredVector FirstLayers(LayeredVector vector, int count, params string[] requires) =>
        vector with
        {
            Name = vector.Name + (count == 1 ? "-first-layer" : Inv($"-first-{count}-layers")),
            KeptLayers = count,
            TruncationOf = vector.Name,
            Requires = [.. requires],
        };

    /// <summary>"--layered": one input per layer, each at its own quality; every layer has the size of the image.</summary>
    private static string Qualities(string format, params int[] qualities) =>
        "--layered " + format + string.Concat(qualities.Select((quality, layer) => Inv($" {(layer == 0 ? "-q" : "-q:u")} {quality} @source0.png")));

    /// <summary>"--layered" with a scale per layer (the last one is 1/1) and a quality per layer.</summary>
    private static string Scaled(string format, params (string Scale, int Quality)[] layers) =>
        "--layered " + format + string.Concat(layers.Select((layer, index) => Inv($" {(index == 0 ? "--scaling-mode" : "--scaling-mode:u")} {layer.Scale} {(index == 0 ? "-q" : "-q:u")} {layer.Quality} @source0.png")));

    private static List<LayeredVector> LayeredVectors()
    {
        const string Layers2 = "av1.spatial_layers=2";
        const string Layers3 = "av1.spatial_layers=3";
        const string Layers4 = "av1.spatial_layers=4";
        var detail = CommonCorpus.PatternDetailColor(32, 32, 5);
        var other = CommonCorpus.PatternDetailColor(32, 32, 9);
        var deep10 = DeepColor(32, 32, 10);
        var deep12 = DeepColor(32, 32, 12);

        // Quality layers: every layer has the size of the image. 2, 3 and 4 layers, 8 to 12 bits, every subsampling
        var quality2 = new LayeredVector("layered-2-quality-420-8bit-32x32", "yuv420p", [new(Qualities("-y 420 --cicp 1/13/6", 20, 80), detail)], Layers2, "av1.operating_points=2", "av1.operating_point_idc=0x301", "av1.bitDepth=8", "av1.subsampling=4:2:0");
        var quality3 = new LayeredVector("layered-3-quality-444-10bit-32x32", "yuv444p10le", [new(Qualities("-y 444 -d 10 --cicp 1/13/1", 15, 50, 85), deep10)], Layers3, "av1.operating_points=3", "av1.operating_point_idc=0x701", "av1.bitDepth=10", "av1.subsampling=4:4:4");
        var quality4 = new LayeredVector("layered-4-quality-420-12bit-32x32", "yuv420p12le", [new(Qualities("-y 420 -d 12 --cicp 1/13/6", 10, 40, 70, 95), deep12)], Layers4, "av1.operating_points=4", "av1.operating_point_idc=0xF01", "av1.bitDepth=12", "av1.subsampling=4:2:0");

        // Layers of growing size: the first one at half the size ("--progressive", and "--layered" with a scale), and three
        // layers at a quarter, half and the whole size
        var progressive = new LayeredVector("layered-progressive-420-8bit-64x64", "yuv420p", [new("--progressive -y 420 --cicp 1/13/6 @source0.png", CommonCorpus.PatternDetailColor(64, 64, 7))], Layers2, "av1.spatial_layer_sizes=32x32,64x64", "av1.frame_size_override_flag", "av1.render_and_frame_size_different");
        var scaled3 = new LayeredVector("layered-3-scaled-444-10bit-32x32", "yuv444p10le", [new(Scaled("-y 444 -d 10 --cicp 1/13/1", ("1/4", 30), ("1/2", 55), ("1/1", 80)), deep10)], Layers3, "av1.spatial_layer_sizes=8x8,16x16,32x32", "av1.bitDepth=10");

        return
        [
            quality2,
            quality3,
            quality4,
            new("layered-4-quality-444-8bit-16x16", "yuv444p", [new(Qualities("-y 444 --cicp 1/13/6", 10, 40, 70, 95), CommonCorpus.PatternDetailColor(16, 16, 3))], Layers4, "av1.bitDepth=8", "av1.subsampling=4:4:4"),
            new("layered-2-quality-422-12bit-16x16", "yuv422p12le", [new(Qualities("-y 422 -d 12 --cicp 1/13/1", 25, 75), DeepColor(16, 16, 12))], Layers2, "av1.bitDepth=12", "av1.subsampling=4:2:2"),
            new("layered-2-quality-400-8bit-32x32", "gray", [new(Qualities("", 20, 80), CommonCorpus.PatternDetailGray(32, 32, 4))], Layers2, "av1.bitDepth=8", "av1.subsampling=4:0:0"),
            new("layered-2-quality-gbr-8bit-16x16", "gbrp", [new(Qualities("-y 444 --cicp 1/13/0", 30, 90), CommonCorpus.PatternDetailColor(16, 16, 6))], Layers2, "av1.matrix=identity"),

            // One input per layer: the second layer is another picture, which the first one predicts badly
            new("layered-2-contents-420-8bit-32x32", "yuv420p", [new("--layered -y 420 --cicp 1/13/6 -q 60 @source0.png -q:u 60 @source1.png", detail, other)], Layers2, "av1.bitDepth=8"),

            progressive,
            new("layered-progressive-400-10bit-32x32", "gray10le", [new("--progressive -d 10 @source0.png", DeepGray(32, 32, 10))], Layers2, "av1.spatial_layer_sizes=16x16,32x32", "av1.subsampling=4:0:0", "av1.bitDepth=10"),
            new("layered-2-scaled-422-8bit-32x32", "yuv422p", [new(Scaled("-y 422 --cicp 1/13/6", ("1/2", 30), ("1/1", 80)), detail)], Layers2, "av1.spatial_layer_sizes=16x16,32x32", "av1.subsampling=4:2:2"),
            scaled3,

            // Two temporal units, each one a layered picture that starts with a key frame: one output frame per unit
            new("layered-2-temporal-units-420-8bit-32x32", "yuv420p", [new(Qualities("-y 420 --cicp 1/13/6", 20, 80), detail), new(Qualities("-y 420 --cicp 1/13/6", 30, 70), other)], Layers2, "av1.frames=2"),

            // The same payloads without the units of their higher layers: the stream still declares every layer, and a
            // decoder outputs the frame of the highest layer it finds in the temporal unit. These are the only digests of
            // the lower layers that two decoders produced
            FirstLayers(quality2, 1, Layers2, "av1.spatial_layers.present=1"),
            FirstLayers(quality3, 1, Layers3, "av1.spatial_layers.present=1"),
            FirstLayers(quality3, 2, Layers3, "av1.spatial_layers.present=2"),
            FirstLayers(quality4, 1, Layers4, "av1.spatial_layers.present=1"),
            FirstLayers(quality4, 2, Layers4, "av1.spatial_layers.present=2"),
            FirstLayers(quality4, 3, Layers4, "av1.spatial_layers.present=3"),
            FirstLayers(progressive, 1, Layers2, "av1.spatial_layers.present=1"),
            FirstLayers(scaled3, 1, Layers3, "av1.spatial_layers.present=1"),
            FirstLayers(scaled3, 2, Layers3, "av1.spatial_layers.present=2", "av1.spatial_layer_sizes=8x8,16x16"),
        ];
    }

    /// <summary>Runs avifenc in a scratch directory. A token that starts with <c>@</c> names an input file; the output is
    /// the last argument.</summary>
    private static (byte[] Data, string Command) RunAvifenc(AvifFileTools tools, FullPath scratch, string name, IReadOnlyDictionary<string, byte[]> files, string arguments)
    {
        foreach (var (file, content) in files)
            File.WriteAllBytes(scratch / file, content);

        var output = scratch / (name + ".avif");
        var tokens = (StandardEncoder + " " + arguments).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        List<string> command = [tools.Avifenc, .. tokens.Select(token => token[0] == '@' ? (scratch / token[1..]).Value : token), output.Value];
        var result = Proc.Exec(command);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new ToolException($"avifenc failed: {Encoding.UTF8.GetString(result.Stderr).Trim()} {Encoding.UTF8.GetString(result.Stdout).Trim()}");

        return (File.ReadAllBytes(output), CommonCorpus.Display(command, tools, [(scratch.Value + Path.DirectorySeparatorChar, ""), (name + ".avif", "{output}.avif")]));
    }

    private static Obj BuildLayeredVector(FullPath outDir, AvifFileTools tools, LayeredVector vector)
    {
        var relative = $"av1/{vector.Name}.obu";
        var path = outDir / relative;
        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-avif-layered-").FullName);
        try
        {
            // The payload of the coded item of each file: one temporal unit, which libavif starts with a temporal delimiter
            var stream = new List<byte>();
            var whole = new List<byte>();
            var commands = new List<string>();
            var files = new List<object?>();
            for (var i = 0; i < vector.Units.Length; i++)
            {
                var unit = vector.Units[i];
                var directory = scratch / Inv($"{i}");
                Directory.CreateDirectory(directory);
                var sources = unit.Sources.Select((source, index) => (Name: Inv($"source{index}.png"), Data: Png(source))).ToDictionary(source => source.Name, source => source.Data, StringComparer.Ordinal);
                var (file, command) = RunAvifenc(tools, directory, vector.TruncationOf ?? vector.Name, sources, unit.Arguments);
                var avif = ReferenceAvif.Parse(file);
                var item = avif.PrimaryItem ?? throw new ToolException("the file avifenc wrote has no primary item.");
                if (item.Type != "av01" || item.LayerSelector is not null || item.OperatingPoint is not null)
                    throw new ToolException("the primary item of the file avifenc wrote is not a plain coded item.");

                var payload = ToTemporalUnit(avif.ReadItemData(item));
                var layers = ReadObuUnits(payload).Where(obu => obu.Type is ObuFrame or ObuFrameHeader).Select(obu => obu.SpatialId).Distinct().Count();
                files.Add(new Obj { ["sha256"] = Bytes.Sha256Hex(file), ["length"] = file.Length, ["payloadLength"] = payload.Length, ["spatialLayers"] = layers });
                commands.Add(command);
                whole.AddRange(payload);
                if (vector.KeptLayers is { } kept)
                {
                    if (kept >= layers)
                        throw new ToolException(Inv($"the payload has {layers} spatial layers: {kept} cannot be kept."));

                    payload = SelectObus(payload, operatingPointIdc: 0, maxSpatialLayer: kept - 1);
                }

                stream.AddRange(payload);
            }

            var data = stream.ToArray();
            File.WriteAllBytes(path, data);
            var parameters = new Obj
            {
                ["payloadOf"] = "The coded item (the primary item) of the file each command writes: one temporal unit per file, in order.",
                ["files"] = files,
            };
            if (vector.KeptLayers is { } keptLayers)
            {
                parameters["truncationOf"] = "av1/" + vector.TruncationOf;
                parameters["keptSpatialLayers"] = keptLayers;
            }

            var provenance = new Obj
            {
                ["origin"] = "generated",
                ["license"] = Corpus<AvifTools>.License,
                ["author"] = Corpus<AvifTools>.Author,
                ["generator"] = $"{ScriptPath} (LayeredVectors)",
                ["tools"] = new List<string> { tools.AvifencLabel, tools.FfmpegLabel },
                ["parameters"] = parameters,
                ["commands"] = commands,
            };

            // A stream that only holds its first layer is one shown key frame per temporal unit: an intra vector
            var expectation = new Av1Vector(vector.Name, "", vector.PixelFormat, [], vector.Units.Length) { Inter = vector.KeptLayers != 1, Requires = vector.Requires };
            var entry = DescribeVector(tools.Av1, expectation, path, relative, data, provenance, provenanceTag: null);
            CheckEveryLayer(tools.Av1, path, (string)entry["pixelFormat"]!, entry);
            if (vector.KeptLayers is { } layer)
            {
                // The frame both decoders output for the truncated stream is the frame of that layer of the whole stream
                var wholePath = scratch / "whole.obu";
                File.WriteAllBytes(wholePath, [.. whole]);
                CheckLayerOfWholeStream(tools.Av1, wholePath, (string)entry["pixelFormat"]!, entry, layer - 1);
            }

            return entry;
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>
    /// What the tests rely on when they ask the library for every layer: dav1d, asked for every layer ("-alllayers 1"),
    /// outputs one frame per shown frame, in bitstream order, and the last frame of each temporal unit is the frame that
    /// both decoders output by default.
    /// </summary>
    private static void CheckEveryLayer(AvifTools tools, FullPath path, string pixelFormat, Obj entry)
    {
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools, path.Value), stderr: true));
        var shown = trace.Frames.Where(frame => frame.IsShown).ToList();
        var everyLayer = Decode(tools, Dav1d, path, pixelFormat, decoderOptions: ["-alllayers", "1"]);
        if (everyLayer.Count != shown.Count)
            throw new ToolException(Inv($"{Dav1d} outputs {everyLayer.Count} frames for every layer; the stream shows {shown.Count} frames."));

        var color = ReadColorConfig(trace.Sequence);
        var frames = ((List<object?>)entry["frames"]!).Cast<Obj>().ToList();
        var output = 0;
        for (var i = 0; i < shown.Count; i++)
        {
            if (!shown[i].IsOutput)
                continue;

            var digests = PlaneDigests(color, shown[i].Size, everyLayer[i]);
            if (!digests.SequenceEqual((List<string>)frames[output]["planes"]!, StringComparer.Ordinal))
                throw new ToolException(Inv($"frame {i} of every layer ({Dav1d}) is not output frame {output}."));

            output++;
        }
    }

    /// <summary>
    /// What makes the digests of a truncated vector those of a lower layer: dav1d, asked for every layer of the whole
    /// stream ("-alllayers 1"), outputs for that layer the planes that both decoders output for the stream without the
    /// units of the higher layers.
    /// </summary>
    private static void CheckLayerOfWholeStream(AvifTools tools, FullPath wholePath, string pixelFormat, Obj entry, int layer)
    {
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools, wholePath.Value), stderr: true));
        var shown = trace.Frames.Where(frame => frame.IsShown).ToList();
        var everyLayer = Decode(tools, Dav1d, wholePath, pixelFormat, decoderOptions: ["-alllayers", "1"]);
        if (everyLayer.Count != shown.Count)
            throw new ToolException(Inv($"{Dav1d} outputs {everyLayer.Count} frames for every layer of the whole stream, which shows {shown.Count} frames."));

        var color = ReadColorConfig(trace.Sequence);
        var frames = ((List<object?>)entry["frames"]!).Cast<Obj>().ToList();
        var ofLayer = Enumerable.Range(0, shown.Count).Where(index => shown[index].SpatialId == layer).ToList();
        if (ofLayer.Count != frames.Count)
            throw new ToolException(Inv($"the whole stream shows {ofLayer.Count} frames of layer {layer}; the truncated stream outputs {frames.Count} frames."));

        for (var i = 0; i < ofLayer.Count; i++)
        {
            var digests = PlaneDigests(color, shown[ofLayer[i]].Size, everyLayer[ofLayer[i]]);
            if (!digests.SequenceEqual((List<string>)frames[i]["planes"]!, StringComparer.Ordinal))
                throw new ToolException(Inv($"the frame of layer {layer} of the whole stream ({Dav1d}, every layer) is not output frame {i} of the truncated stream."));
        }
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Units of a low overhead bitstream
    // -----------------------------------------------------------------------------------------------------------------

    private const int ObuSequenceHeader = 1;
    private const int ObuTemporalDelimiter = 2;
    private const int ObuFrameHeader = 3;
    private const int ObuFrame = 6;

    /// <summary>An OBU of a low overhead bitstream (section 5.3): its type, its bytes in the stream, and the layers of its
    /// extension (0 without extension).</summary>
    private readonly record struct ObuUnit(int Type, int Offset, int Length, bool HasExtension, int TemporalId, int SpatialId);

    private static List<ObuUnit> ReadObuUnits(byte[] stream)
    {
        var units = new List<ObuUnit>();
        var offset = 0;
        while (offset < stream.Length)
        {
            var header = stream[offset];
            if ((header & 0x02) == 0)
                throw new ToolException("an OBU of the stream has no size field.");

            var hasExtension = (header & 0x04) != 0;
            var position = offset + 1;
            var (temporalId, spatialId) = (0, 0);
            if (hasExtension)
            {
                (temporalId, spatialId) = (stream[position] >> 5, (stream[position] >> 3) & 3);
                position++;
            }

            var size = 0;
            for (var shift = 0; ; shift += 7)
            {
                var value = stream[position++];
                size |= (value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
            }

            if (position + size > stream.Length)
                throw new ToolException("the last OBU of the stream is truncated.");

            units.Add(new ObuUnit((header >> 3) & 15, offset, position + size - offset, hasExtension, temporalId, spatialId));
            offset = position + size;
        }

        return units;
    }

    /// <summary>
    /// The units a decoder keeps (section 5.3.1 "General OBU syntax"): a unit with an extension, other than a sequence
    /// header or a temporal delimiter, is dropped when its layers are not in the operating point
    /// (<paramref name="operatingPointIdc"/>; 0 keeps every layer), and here also when its spatial layer is above
    /// <paramref name="maxSpatialLayer"/>. The result is a stream both decoders decode like any other.
    /// </summary>
    private static byte[] SelectObus(byte[] stream, int operatingPointIdc, int maxSpatialLayer)
    {
        var kept = new List<byte>();
        foreach (var unit in ReadObuUnits(stream))
        {
            if (unit.Type is not (ObuSequenceHeader or ObuTemporalDelimiter) && unit.HasExtension)
            {
                var inOperatingPoint = operatingPointIdc == 0 || (((operatingPointIdc >> unit.TemporalId) & 1) != 0 && ((operatingPointIdc >> (unit.SpatialId + 8)) & 1) != 0);
                if (!inOperatingPoint || unit.SpatialId > maxSpatialLayer)
                    continue;
            }

            kept.AddRange(stream.AsSpan(unit.Offset, unit.Length));
        }

        return [.. kept];
    }
}
