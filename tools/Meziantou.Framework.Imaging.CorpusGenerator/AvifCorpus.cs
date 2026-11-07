using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the AV1 conformance vectors of the corpus: raw AV1 OBU streams under tests/Meziantou.Framework.Imaging.Fixtures/av1
/// and the <c>av1Vectors</c> section of the manifest, which records for every output frame the SHA-256 of each decoded plane.
/// <para>
/// This generator is the reviewed, reproducible recipe of every committed vector. It is NEVER run by the tests and does not
/// use the library under test. Every tool comes from the FFmpeg build of the meziantou/prebuilt release pinned by the
/// <c>Meziantou.Prebuilt</c> package (the binaries of the interop tests):
/// - the streams are encoded from deterministic lavfi sources by libaom and SVT-AV1, single-threaded and bit-exact;
/// - the planes are decoded by two independent AV1 decoders, dav1d and libaom, to the native pixel format of the stream (no
///   swscale conversion: ffprobe must report that format for both decoders), film grain applied, one raw file per output
///   frame and no scaling, so that a frame keeps its own size when the size changes inside a stream (reference scaling); a
///   digest is recorded only when both decoders agree byte for byte on every plane of every output frame, in output order,
///   and the generation fails for a vector that either of them rejects;
/// - the feature tags are derived from the headers as parsed by a third implementation, FFmpeg's trace_headers bitstream
///   filter, never from the encoder options: an option does not guarantee that a tool is used. Each vector lists the tags
///   it exists for (<see cref="Av1Vector.Requires"/>); the generation fails when the encoder did not produce them.
/// </para>
/// <para>
/// Plane digest: SHA-256 of the samples of one plane, rows in order, exactly plane-width samples per row, 1 byte per sample
/// for 8-bit streams and 2 little-endian bytes for 10 and 12-bit streams, no padding. Planes are in bitstream order Y, U, V
/// (for an identity-matrix stream FFmpeg's planar GBR order G, B, R is that order); a monochrome stream has one plane.
/// </para>
/// <para>
/// Output frames: a decoder outputs one frame per shown frame and per <c>show_existing_frame</c>, in bitstream order; a
/// hidden frame (<c>show_frame = 0</c>) produces no output. The size of every output frame is computed from the headers
/// (<c>ResolveFrameSizes</c>) and must be the size both decoders report and produce.
/// </para>
/// <para>
/// An intra vector is made of shown key frames without intra block copy: a frame of another type or a stream that signals
/// <c>allow_intrabc</c> fails its generation unless the recipe asks for them (<see cref="Av1Vector.Inter"/>, a required
/// <c>av1.allow_intrabc</c> tag).
/// </para>
/// <para>
/// A few vectors are not what an encoder wrote alone, for the coding tools no encoder produces: the stream of another
/// recipe with the frame-level interpolation filter of its frame headers overwritten (<c>Patched</c>,
/// <c>PatchInterpolationFilter</c>), or a libaom key frame followed by inter frames written bit by bit from the
/// specification (<c>HandAuthored</c>, <see cref="Av1HandAuthoredStream"/>). Their headers, tags and digests are those of
/// the final stream, under the same two-decoder rule; they are tagged <c>av1.patched_header</c> and
/// <c>av1.hand_authored</c>, and their provenance records the source stream.
/// </para>
/// <para>
/// The vectors with several spatial layers are the payload of the coded item of a layered image written by libavif's
/// avifenc, the only pinned tool that codes them (<c>LayeredVectors</c>, AvifCorpus.Layered.cs). Both decoders then output
/// one frame per temporal unit, the shown frame of its highest spatial layer: the recorded frames are those.
/// </para>
/// <para>
/// It complements the other generators: this generator only replaces the <c>av1Vectors</c> section and the files it lists,
/// and keeps every fixture and file of the other generators unchanged (they keep this section and its files in return).
/// </para>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif            # check: regenerate in a temporary directory and diff
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif --write    # explicit regeneration (review the diff afterwards!)
/// Requirements: none besides network access to download the pinned tools once (FFmpeg, ffprobe and, for the layered
/// vectors, avifenc; override a version mismatch with --accept-tool-versions after reviewing the differences). The
/// committed vectors were generated on macOS arm64; the encoders are not guaranteed to make the same decisions on another
/// platform.
/// </summary>
internal static partial class AvifCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/AvifCorpus.cs";


    // Tool versions used to produce the committed vectors. Regenerating with other versions is allowed only explicitly
    // (--accept-tool-versions) and must be reviewed: the encoded inputs change with the encoders, and the digests must not
    // change with the decoders.
    private static readonly (string Name, string Version)[] PinnedTools =
    [
        ("ffmpeg", "9.0.2"),
        ("libaom", "3.15.1"),
        ("SVT-AV1", "4.2.0"),
        ("libdav1d", "1.5.4"),
    ];

    private const string PlaneDigestDescription =
        "SHA-256 of the samples of one plane of one output frame, film grain applied: rows in order, exactly plane-width samples per row, " +
        "1 byte per sample for 8-bit streams and 2 little-endian bytes for 10 and 12-bit streams, no padding. Planes are in bitstream order " +
        "Y, U, V (planar GBR order G, B, R for an identity-matrix stream), one plane for a monochrome stream; a chroma plane is " +
        "(width + subsampling_x) >> subsampling_x samples wide and (height + subsampling_y) >> subsampling_y rows high. " +
        "A digest is recorded only when both decoders produce the same bytes.";

    public static int Run(GeneratorOptions options)
    {
        // avifenc writes the layered streams (LayeredVectors); every other tool is the pinned FFmpeg
        var tools = new AvifFileTools(options.AcceptToolVersions);
        return CorpusDriver.Run(options, "mfi-avif-corpus-", output => Generate(output, tools),
            (vectors, files, bytes) => Inv($"{vectors} AV1 vectors; corpus: {files} files, {bytes} bytes"),
            "The committed AV1 vectors are reproducible.");
    }

    private static int Generate(FullPath outDir, AvifFileTools fileTools)
    {
        // Every fixture and file of the other generators is kept unchanged
        var tools = fileTools.Av1;
        var manifest = CorpusDriver.LoadCommittedManifest();
        CorpusDriver.CopyCommittedFiles(outDir, CorpusDriver.Fixtures(manifest));
        CorpusDriver.PreserveHevcVectors(outDir, manifest);
        CorpusDriver.PreserveColorSections(outDir, manifest);
        CorpusDriver.PreserveFonts(outDir, manifest);

        var vectors = VectorsWithPatches();
        var layered = LayeredVectors();
        var names = vectors.Select(v => v.Name).Concat(layered.Select(v => v.Name)).ToList();
        Py.Assert(names.Distinct(StringComparer.Ordinal).Count() == names.Count, "duplicate vector name");
        var entries = new Obj?[names.Count];
        var failures = new ConcurrentBag<string>();
        Directory.CreateDirectory(outDir / "av1");
        Parallel.For(0, names.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try
            {
                entries[i] = i < vectors.Count ? BuildVector(outDir, tools, vectors[i]) : BuildLayeredVector(outDir, fileTools, layered[i - vectors.Count]);
            }
            catch (ToolException error)
            {
                failures.Add($"av1/{names[i]}: {error.Message.TrimEnd()}");
            }
        });

        if (!failures.IsEmpty)
            throw new FatalException($"{failures.Count} AV1 vector(s) cannot be generated; nothing is recorded for them:\n  " + string.Join("\n  ", failures.Order(StringComparer.Ordinal)));

        var bytes = entries.Sum(entry => new FileInfo(outDir / (string)((Obj)entry!["input"]!)["path"]!).Length);
        Console.WriteLine(Inv($"av1: {entries.Length} vectors, {bytes} input bytes"));
        manifest[CorpusDriver.Av1VectorsKey] = new Obj
        {
            ["generator"] = new Obj
            {
                ["script"] = ScriptPath,
                ["command"] = "dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- avif --write",
                ["tools"] = new List<string> { tools.FfmpegLabel, tools.LibaomLabel, tools.SvtLabel, tools.Dav1dLabel, fileTools.AvifencLabel, ToolSet.RuntimeLabel },
            },
            ["headerParser"] = new Obj
            {
                ["name"] = tools.FfmpegLabel + " (trace_headers bitstream filter)",
                ["command"] = CommonCorpus.Display(TraceCommand(tools, "{input}"), tools, []),
            },
            ["decoders"] = new List<object?>
            {
                new Obj { ["name"] = tools.Dav1dLabel, ["command"] = CommonCorpus.Display(DecodeCommand(tools, Dav1d, "{input}", "{pixelFormat}", "{frames}/" + FramePattern), tools, []) },
                new Obj { ["name"] = tools.LibaomLabel, ["command"] = CommonCorpus.Display(DecodeCommand(tools, Libaom, "{input}", "{pixelFormat}", "{frames}/" + FramePattern), tools, []) },
            },
            ["planeDigest"] = PlaneDigestDescription,
            ["entries"] = entries.OrderBy(entry => (string)entry!["id"]!, StringComparer.Ordinal).Cast<object?>().ToList(),
        };
        CorpusDriver.WriteManifest(outDir, manifest);
        return entries.Length;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // One vector: encode, parse the headers, decode twice, digest
    // -----------------------------------------------------------------------------------------------------------------

    private const string Dav1d = "libdav1d";
    private const string Libaom = "libaom-av1";

    private static List<string> EncodeCommand(AvifTools tools, Av1Vector vector, string output) =>
        tools.FfmpegCmd(["-filter_threads", "1", "-f", "lavfi", "-i", vector.Source, "-frames:v", vector.EncodedFrames.ToString(CultureInfo.InvariantCulture),
            "-sws_flags", "accurate_rnd+full_chroma_int+bitexact", "-pix_fmt", vector.PixelFormat, .. vector.Encoder, "-threads", "1", "-flags", "+bitexact", "-f", "obu", output]);

    private static List<string> TraceCommand(AvifTools tools, string input) =>
        [tools.Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "info", "-f", "obu", "-i", input, "-c", "copy", "-bsf:v", "trace_headers", "-f", "null", "-"];

    /// <summary>The name of the raw file of an output frame, numbered from 1 in output order.</summary>
    private const string FramePattern = "%05d.raw";

    /// <summary>One pass, one thread, the decoder forced, the raw planes of every output frame in the native pixel format
    /// and in a file of its own, without scaling: a frame whose size differs from the first one keeps its size. Neither
    /// decoder is asked to export the film grain parameters instead of applying them: both apply the grain.</summary>
    private static List<string> DecodeCommand(ToolSet tools, string decoder, string input, string pixelFormat, string output, bool withoutFilmGrain = false, string[]? decoderOptions = null) =>
        tools.FfmpegCmd([.. (withoutFilmGrain ? ["-export_side_data", "film_grain"] : Array.Empty<string>()), "-threads", "1", "-c:v", decoder, .. decoderOptions ?? [], "-f", "obu", "-i", input,
            "-fps_mode", "passthrough", "-noautoscale", "-f", "image2", "-c:v", "rawvideo", "-pix_fmt", pixelFormat, output]);

    private static Obj BuildVector(FullPath outDir, AvifTools tools, Av1Vector vector)
    {
        var relative = $"av1/{vector.Name}.obu";
        var path = outDir / relative;
        var encode = EncodeCommand(tools, vector, path.Value);
        var encoded = Proc.Exec(encode);
        if (encoded.ExitCode != 0 || !File.Exists(path))
            throw new ToolException("the encoder failed: " + Encoding.UTF8.GetString(encoded.Stderr).Trim());

        var data = File.ReadAllBytes(path);
        if (data.Length == 0)
            throw new ToolException("the encoder wrote an empty stream.");

        // A recipe made by header patching: the stream of another recipe with the frame-level interpolation filter
        // overwritten. Everything below (headers, decoders, digests) is that of the patched stream
        Obj? patch = null;
        if (vector.InterpolationFilterPatch is { } filter)
        {
            var encodedTrace = ParseTrace(Proc.RunText(TraceCommand(tools, path.Value), stderr: true));
            var patched = PatchInterpolationFilter(data, encodedTrace, filter, out var positions);
            patch = new Obj
            {
                ["patchOf"] = "av1/" + vector.PatchOf,
                ["sourceSha256"] = Bytes.Sha256Hex(data),
                ["patch"] = Inv($"interpolation_filter = {filter} ({(filter == 3 ? "BILINEAR" : "EIGHTTAP_SMOOTH")}) written over the 2 bits of the element in every frame header that codes is_filter_switchable = 0"),
                ["patchedBitOffsets"] = positions.Cast<object?>().ToList(),
            };
            data = patched;
            File.WriteAllBytes(path, data);
        }

        // A hand-authored recipe: the key frame the encoder wrote, followed by inter frames written bit by bit
        Obj? handAuthored = null;
        if (vector.HandAuthoredFrames is { } recipe)
        {
            var stream = Av1HandAuthoredStream.Build(data, recipe);
            handAuthored = new Obj
            {
                ["handAuthoredRecipe"] = recipe.Name,
                ["description"] = recipe.Description,
                ["keyFrameSha256"] = Bytes.Sha256Hex(data),
                ["keyFrameLength"] = data.Length,
            };
            data = stream;
            File.WriteAllBytes(path, data);
        }

        var provenance = new Obj
        {
            ["origin"] = "generated",
            ["license"] = Corpus<AvifTools>.License,
            ["author"] = Corpus<AvifTools>.Author,
            ["generator"] = $"{ScriptPath} (Vectors)",
            ["tools"] = new List<string> { tools.FfmpegLabel, vector.Encoder.Contains("libsvtav1", StringComparer.Ordinal) ? tools.SvtLabel : tools.LibaomLabel },
        };
        if ((patch ?? handAuthored) is { } parameters)
            provenance["parameters"] = parameters;

        provenance["commands"] = new List<string> { CommonCorpus.Display(encode, tools, [(path.Value, "{input}")]) };
        return DescribeVector(tools, vector, path, relative, data, provenance, patch is not null ? "av1.patched_header" : handAuthored is not null ? "av1.hand_authored" : null);
    }

    /// <summary>
    /// Records a stream: its headers as trace_headers reads them, the feature tags, and the digests of the planes that both
    /// decoders agree on. <paramref name="provenanceTag"/> is a tag that does not come from the headers
    /// (<c>av1.patched_header</c>, <c>av1.hand_authored</c>), or <see langword="null"/>.
    /// </summary>
    private static Obj DescribeVector(AvifTools tools, Av1Vector vector, FullPath path, string relative, byte[] data, Obj provenance, string? provenanceTag)
    {
        // Headers, by a parser independent of the encoders and of the decoders
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools, path.Value), stderr: true));
        var color = ReadColorConfig(trace.Sequence);
        var coded = trace.Frames.Where(f => !f.ShowsExistingFrame).Select(f => f.Header).ToList();
        // Absent from a reduced still picture header: a shown key frame
        var onlyShownKeyFrames = trace.Frames.All(f => !f.ShowsExistingFrame && f.Header.Get("frame_type") == 0 && f.Header.Get("show_frame", absent: 1) == 1);
        if (vector.Inter == onlyShownKeyFrames)
        {
            throw new ToolException(vector.Inter
                ? "every frame of the stream is a shown key frame; an inter vector has other frames."
                : "the stream has a frame that is not a shown key frame; an intra vector is made of shown key frames.");
        }

        if (!vector.Inter && !vector.Requires.Contains("av1.allow_intrabc", StringComparer.Ordinal) && coded.Any(frame => frame.Get("allow_intrabc") != 0))
            throw new ToolException("the stream signals allow_intrabc = 1 (pass enable-intrabc=0 to libaom); only the intra block copy vectors require it.");

        var outputs = trace.Frames.Where(f => f.IsOutput).Select(f => f.Size).ToList();
        if (outputs.Count != vector.Frames)
            throw new ToolException(Inv($"the stream has {outputs.Count} output frames (shown frames and show_existing_frame), {vector.Frames} expected."));
        if (color.PixelFormat != vector.PixelFormat)
            throw new ToolException($"the stream is {color.PixelFormat}, not the requested {vector.PixelFormat}: the encoder converted the source.");

        var features = Features(trace, color);
        if (provenanceTag is not null)
        {
            // The only tags that do not come from the headers: they tell that an encoder did not write all of them
            features.Add(provenanceTag);
            features.Sort(StringComparer.Ordinal);
        }

        var missing = vector.Requires.Where(tag => !HasFeature(features, tag)).ToList();
        if (missing.Count > 0)
            throw new ToolException($"the stream does not signal {string.Join(", ", missing)} (it signals {string.Join(" ", features)}).");

        // Two independent decoders, native pixel format (checked with ffprobe: no conversion), every output frame at the
        // size the headers give it (no scaling), same bytes
        var expected = outputs.Select(size => (size.Width, size.Height, color.PixelFormat)).ToList();
        List<byte[]>? reference = null;
        foreach (var decoder in new[] { Dav1d, Libaom })
        {
            var probed = ProbeFrames(tools, decoder, path);
            if (!probed.SequenceEqual(expected))
                throw new ToolException($"ffprobe ({decoder}) reports the frames {Describe(probed)}; the headers describe {Describe(expected)}.");

            var decoded = Decode(tools, decoder, path, color.PixelFormat);
            if (decoded.Count != outputs.Count)
                throw new ToolException(Inv($"{decoder} produced {decoded.Count} frames, not {outputs.Count}."));

            for (var i = 0; i < decoded.Count; i++)
            {
                var frameLength = PlaneSizes(color, outputs[i]).Sum(plane => plane.Width * plane.Height * color.BytesPerSample);
                if (decoded[i].Length != frameLength)
                    throw new ToolException(Inv($"{decoder} produced {decoded[i].Length} bytes for frame {i}, not the {frameLength} bytes of a {outputs[i].Width}x{outputs[i].Height} {color.PixelFormat} frame."));
                if (reference is not null && !reference[i].AsSpan().SequenceEqual(decoded[i]))
                    throw new ToolException(Inv($"{Dav1d} and {Libaom} decode different samples (frame {i}, first difference at byte {reference[i].AsSpan().CommonPrefixLength(decoded[i])}); no digest is recorded from a single decoder."));
            }

            reference ??= decoded;
        }

        if (features.Contains("av1.film_grain", StringComparer.Ordinal))
        {
            // The digests are those of the frames with film grain: decoding without it must give other samples
            var withoutGrain = Decode(tools, Dav1d, path, color.PixelFormat, withoutFilmGrain: true);
            if (withoutGrain.Count != reference!.Count || withoutGrain.Zip(reference).All(pair => pair.First.AsSpan().SequenceEqual(pair.Second)))
                throw new ToolException("decoding without film grain does not change the samples: the digests would not cover film grain synthesis.");
        }

        var frames = new List<object?>();
        for (var i = 0; i < outputs.Count; i++)
        {
            frames.Add(new Obj { ["width"] = outputs[i].Width, ["height"] = outputs[i].Height, ["planes"] = PlaneDigests(color, outputs[i], reference![i]) });
        }

        return new Obj
        {
            ["id"] = "av1/" + vector.Name,
            ["input"] = new Obj { ["path"] = relative, ["sha256"] = Bytes.Sha256Hex(data), ["role"] = "encoded" },
            ["provenance"] = provenance,
            ["features"] = features,
            ["pixelFormat"] = color.PixelFormat,
            ["bitDepth"] = color.BitDepth,
            ["subsampling"] = color.Subsampling,
            ["frameCount"] = outputs.Count,
            ["frames"] = frames,
        };

        static string Describe(IEnumerable<(int Width, int Height, string PixelFormat)> frames) => string.Join(' ', frames.Select(frame => Inv($"{frame.Width}x{frame.Height}/{frame.PixelFormat}")));
    }

    /// <summary>The OBUs of a low overhead bitstream (section 5.2): the type and the offset of each OBU header. Every OBU of
    /// a vector has a size field.</summary>
    private static List<(int Type, int Offset)> ReadObus(byte[] stream)
    {
        var obus = new List<(int Type, int Offset)>();
        var offset = 0;
        while (offset < stream.Length)
        {
            var header = stream[offset];
            if ((header & 0x02) == 0)
                throw new ToolException("an OBU of the stream has no size field.");

            obus.Add(((header >> 3) & 15, offset));
            var position = offset + 1 + ((header & 0x04) != 0 ? 1 : 0);
            var size = 0;
            for (var shift = 0; ; shift += 7)
            {
                var value = stream[position++];
                size |= (value & 0x7F) << shift;
                if ((value & 0x80) == 0)
                    break;
            }

            offset = position + size;
        }

        if (offset != stream.Length)
            throw new ToolException("the last OBU of the stream is truncated.");
        return obus;
    }

    /// <summary>
    /// Writes <paramref name="filter"/> over the 2-bit <c>interpolation_filter</c> element of every frame header that codes
    /// <c>is_filter_switchable = 0</c> (section 5.9.10 "Interpolation filter syntax"). The position of the element in its
    /// OBU is the one trace_headers reports; the n-th frame header of the trace is the n-th OBU_FRAME_HEADER or OBU_FRAME
    /// of the stream. The bits found there must be the value trace_headers read.
    /// </summary>
    /// <param name="bitOffsets">The offset in the stream of the first bit of every patched element.</param>
    private static byte[] PatchInterpolationFilter(byte[] stream, Av1Trace trace, int filter, out List<long> bitOffsets)
    {
        const int FrameHeaderObu = 3;
        const int FrameObu = 6;
        const int RedundantFrameHeaderObu = 7;
        var obus = ReadObus(stream);
        if (obus.Any(obu => obu.Type == RedundantFrameHeaderObu))
            throw new ToolException("the stream has redundant frame headers, which the patch does not follow.");

        var headers = obus.Where(obu => obu.Type is FrameHeaderObu or FrameObu).ToList();
        if (headers.Count != trace.Frames.Count)
            throw new ToolException(Inv($"the stream has {headers.Count} frame header OBUs and trace_headers reports {trace.Frames.Count} frame headers."));

        var patched = (byte[])stream.Clone();
        bitOffsets = [];
        for (var i = 0; i < headers.Count; i++)
        {
            var header = trace.Frames[i].Header;
            if (!header.Has("is_filter_switchable") || header.Get("is_filter_switchable") != 0)
                continue;

            var (position, bitCount) = header.GetPosition("interpolation_filter");
            if (bitCount != 2)
                throw new ToolException("interpolation_filter is not coded with 2 bits.");

            var first = (headers[i].Offset * 8L) + position;
            var current = 0;
            for (var bit = 0; bit < 2; bit++)
            {
                var index = first + bit;
                current = (current << 1) | ((patched[index >> 3] >> (7 - (int)(index & 7))) & 1);
                var mask = (byte)(1 << (7 - (int)(index & 7)));
                patched[index >> 3] = (byte)(((filter >> (1 - bit)) & 1) != 0 ? patched[index >> 3] | mask : patched[index >> 3] & ~mask);
            }

            if (current != header.Get("interpolation_filter"))
                throw new ToolException(Inv($"frame header {i}: the bits at the position trace_headers gives for interpolation_filter are {current}, not the {header.Get("interpolation_filter")} it read."));
            if (current == filter)
                throw new ToolException(Inv($"frame header {i} already codes interpolation_filter = {filter}."));

            bitOffsets.Add(first);
        }

        if (bitOffsets.Count == 0)
            throw new ToolException("no frame header of the stream codes is_filter_switchable = 0: there is nothing to patch.");
        return patched;
    }

    /// <summary>Whether the tags include a tag, one of the comma-separated values of a <c>key=value</c> tag, or, for a bare
    /// key, a tag of that key whatever its value.</summary>
    private static bool HasFeature(List<string> features, string tag)
    {
        if (features.Contains(tag, StringComparer.Ordinal))
            return true;

        var separator = tag.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
            return features.Any(feature => feature.StartsWith(tag + "=", StringComparison.Ordinal));

        var prefix = tag[..(separator + 1)];
        return features.Any(feature => feature.StartsWith(prefix, StringComparison.Ordinal) && feature[prefix.Length..].Split(',').Contains(tag[prefix.Length..], StringComparer.Ordinal));
    }

    /// <summary>The dimensions of the planes of a frame: a chroma plane is (size + subsampling) >> subsampling.</summary>
    private static List<(int Width, int Height)> PlaneSizes(Av1ColorConfig color, (int Width, int Height) frame) =>
        [.. Enumerable.Range(0, color.PlaneCount).Select(plane => plane == 0 ? frame : ((frame.Width + color.SubsamplingX) >> color.SubsamplingX, (frame.Height + color.SubsamplingY) >> color.SubsamplingY))];

    /// <summary>The digest of each plane of a raw frame whose length was checked against <see cref="PlaneSizes"/>.</summary>
    private static List<string> PlaneDigests(Av1ColorConfig color, (int Width, int Height) frame, byte[] raw)
    {
        var digests = new List<string>();
        var offset = 0;
        foreach (var plane in PlaneSizes(color, frame))
        {
            var length = plane.Width * plane.Height * color.BytesPerSample;
            digests.Add(Bytes.Sha256Hex(raw.AsSpan(offset, length)));
            offset += length;
        }

        return digests;
    }

    /// <summary>The raw planes of every output frame, in output order.</summary>
    /// <param name="decoderOptions">Options of the decoder, for a check that needs another output than the default one.</param>
    private static List<byte[]> Decode(ToolSet tools, string decoder, FullPath path, string pixelFormat, bool withoutFilmGrain = false, string[]? decoderOptions = null)
    {
        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-avif-frames-").FullName);
        try
        {
            var result = Proc.Exec(DecodeCommand(tools, decoder, path.Value, pixelFormat, (scratch / FramePattern).Value, withoutFilmGrain, decoderOptions));
            if (result.ExitCode != 0 || result.Stderr.Length > 0)
                throw new ToolException($"{decoder} rejects the stream: {Encoding.UTF8.GetString(result.Stderr).Trim()}");

            // Numbered from 1 with a fixed width: the ordinal order of the names is the output order
            var files = Directory.EnumerateFiles(scratch).Select(file => FullPath.FromPath(file)).OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
            for (var i = 0; i < files.Count; i++)
            {
                if (files[i].Name != Inv($"{i + 1:D5}.raw"))
                    throw new ToolException($"{decoder} wrote the unexpected frame file {files[i].Name}.");
            }

            return [.. files.Select(file => File.ReadAllBytes(file))];
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>The size and the pixel format of every frame a decoder produces, in output order, before any conversion.</summary>
    private static List<(int Width, int Height, string PixelFormat)> ProbeFrames(AvifTools tools, string decoder, FullPath path)
    {
        var result = Proc.Exec([tools.Ffprobe, "-hide_banner", "-v", "error", "-threads", "1", "-f", "obu", "-c:v", decoder, "-select_streams", "v:0",
            "-show_entries", "frame=width,height,pix_fmt", "-of", "csv=print_section=0", path.Value]);
        if (result.ExitCode != 0 || result.Stderr.Length > 0)
            throw new ToolException($"ffprobe ({decoder}) rejects the stream: {Encoding.UTF8.GetString(result.Stderr).Trim()}");

        return [.. Encoding.UTF8.GetString(result.Stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Split(','))
            .Select(parts => (int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2]))];
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Verification (the "verify" subcommand): the committed vectors against the decoders of the pinned release
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks every committed vector: the hash of its file, then the digests of its planes re-derived with each of the two
    /// decoders the FFmpeg build has (a decoder the build lacks is reported and skipped; the hashes are always checked).
    /// </summary>
    /// <returns>The number of vectors checked, and the disagreements.</returns>
    public static (int Verified, List<string> Failures) Verify(ToolSet tools)
    {
        var failures = new List<string>();
        var manifest = CorpusDriver.LoadCommittedManifest();
        if (!manifest.TryGetValue(CorpusDriver.Av1VectorsKey, out var value) || value is not Obj section)
            return (0, failures);

        var available = Proc.RunText([tools.Ffmpeg, "-hide_banner", "-decoders"]);
        var decoders = new List<string>();
        foreach (var decoder in new[] { Dav1d, Libaom })
        {
            if (DecoderListRegex(decoder).IsMatch(available))
                decoders.Add(decoder);
            else
                Console.WriteLine($"  av1: this FFmpeg build has no {decoder} decoder; the plane digests are not re-derived with it.");
        }

        var entries = ((List<object?>)section["entries"]!).Cast<Obj>().ToList();
        var errors = new ConcurrentBag<string>();
        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, entry =>
        {
            var id = (string)entry["id"]!;
            foreach (var error in VerifyVector(tools, entry, decoders))
                errors.Add($"{id}: {error}");
        });

        failures.AddRange(errors.Order(StringComparer.Ordinal));
        return (entries.Count, failures);
    }

    private static List<string> VerifyVector(ToolSet tools, Obj entry, List<string> decoders)
    {
        var input = (Obj)entry["input"]!;
        var path = CorpusDriver.CorpusDir / (string)input["path"]!;
        if (!File.Exists(path))
            return ["the input file is missing."];
        if (Bytes.Sha256Hex(File.ReadAllBytes(path)) != (string)input["sha256"]!)
            return ["the input file does not have the recorded SHA-256."];

        var pixelFormat = (string)entry["pixelFormat"]!;
        var subsampling = (string)entry["subsampling"]!;
        var bitDepth = Convert.ToInt32(entry["bitDepth"], CultureInfo.InvariantCulture);
        var color = new Av1ColorConfig(bitDepth, Monochrome: subsampling == "4:0:0", SubsamplingX: subsampling is "4:4:4" ? 0 : 1, SubsamplingY: subsampling is "4:4:4" or "4:2:2" ? 0 : 1, IdentityMatrix: pixelFormat.StartsWith("gbrp", StringComparison.Ordinal));
        var frames = ((List<object?>)entry["frames"]!).Cast<Obj>().ToList();
        var errors = new List<string>();
        foreach (var decoder in decoders)
        {
            List<byte[]> decoded;
            try
            {
                decoded = Decode(tools, decoder, path, pixelFormat);
            }
            catch (ToolException error)
            {
                errors.Add(error.Message.Split('\n')[0]);
                continue;
            }

            if (decoded.Count != frames.Count)
            {
                errors.Add(Inv($"{decoder} decodes {decoded.Count} frames, {frames.Count} expected."));
                continue;
            }

            for (var i = 0; i < frames.Count; i++)
            {
                var size = (Width: Convert.ToInt32(frames[i]["width"], CultureInfo.InvariantCulture), Height: Convert.ToInt32(frames[i]["height"], CultureInfo.InvariantCulture));
                var frameLength = PlaneSizes(color, size).Sum(plane => plane.Width * plane.Height * color.BytesPerSample);
                if (decoded[i].Length != frameLength)
                {
                    errors.Add(Inv($"frame {i}: {decoder} produces {decoded[i].Length} bytes, not the {frameLength} bytes of a {size.Width}x{size.Height} {pixelFormat} frame."));
                    continue;
                }

                var digests = PlaneDigests(color, size, decoded[i]);
                var recorded = ((List<object?>)frames[i]["planes"]!).Cast<string>().ToList();
                for (var plane = 0; plane < recorded.Count; plane++)
                {
                    if (plane >= digests.Count || digests[plane] != recorded[plane])
                        errors.Add(Inv($"frame {i} plane {plane}: {decoder} decodes samples whose SHA-256 is not the recorded one."));
                }
            }
        }

        return errors;
    }

    /// <summary>A video decoder line of <c>ffmpeg -decoders</c>: capability flags, then the decoder name.</summary>
    private static Regex DecoderListRegex(string decoder) => new(@"^\s*V\S*\s+" + Regex.Escape(decoder) + @"\s", RegexOptions.Multiline | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The FFmpeg and ffprobe builds of the pinned meziantou/prebuilt release, and the versions of the AV1 libraries
    /// they are linked with (reported by the libraries themselves in the verbose log).</summary>
    private sealed partial class AvifTools : ToolSet
    {
        public AvifTools(bool acceptVersions)
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            Ffprobe = Download(PrebuiltTools.Ffprobe);
            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Ffprobe] = "ffprobe" };

            var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-avif-tools-").FullName);
            try
            {
                var aom = scratch / "aom.obu";
                var svt = scratch / "svt.obu";
                LibaomVersion = Version(LibaomVersionRegex(), Verbose(["-f", "lavfi", "-i", "color=size=16x16", "-frames:v", "1", "-c:v", "libaom-av1", "-threads", "1", "-f", "obu", aom.Value]), "libaom (encoder)");
                SvtVersion = Version(SvtVersionRegex(), Verbose(["-f", "lavfi", "-i", "color=size=64x64", "-frames:v", "1", "-c:v", "libsvtav1", "-threads", "1", "-f", "obu", svt.Value]), "SVT-AV1");
                Dav1dVersion = Version(Dav1dVersionRegex(), Verbose(["-c:v", Dav1d, "-f", "obu", "-i", aom.Value, "-f", "null", "-"]), "libdav1d");
                var decoderVersion = Version(LibaomVersionRegex(), Verbose(["-c:v", Libaom, "-f", "obu", "-i", svt.Value, "-f", "null", "-"]), "libaom (decoder)");
                if (decoderVersion != LibaomVersion)
                    throw new FatalException($"The libaom decoder reports version {decoderVersion}, the encoder {LibaomVersion}.");
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }

            var actual = new Dictionary<string, string>(StringComparer.Ordinal) { ["ffmpeg"] = FfmpegVersion, ["libaom"] = LibaomVersion, ["SVT-AV1"] = SvtVersion, ["libdav1d"] = Dav1dVersion };
            var mismatches = PinnedTools.Where(p => actual[p.Name] != p.Version).Select(p => $"{p.Name} {actual[p.Name]} (pinned {p.Version})").ToList();
            if (mismatches.Count > 0 && !acceptVersions)
            {
                throw new FatalException("Tool versions differ from the pinned versions: " + string.Join(", ", mismatches) +
                    ". Review the impact and rerun with --accept-tool-versions (then update PinnedTools).");
            }
        }

        public string Ffprobe { get; }

        public string LibaomVersion { get; }

        public string SvtVersion { get; }

        public string Dav1dVersion { get; }

        public string LibaomLabel => "libaom " + LibaomVersion;

        public string SvtLabel => "SVT-AV1 " + SvtVersion;

        public string Dav1dLabel => "libdav1d " + Dav1dVersion;

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();

        private string Verbose(IEnumerable<string> arguments) => Proc.RunText([Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "verbose", "-y", .. arguments], stderr: true);

        private static string Version(Regex regex, string log, string library)
        {
            var match = regex.Match(log);
            if (!match.Success)
                throw new FatalException($"Cannot read the version of {library} from the FFmpeg log.");
            return match.Groups["version"].Value;
        }

        [GeneratedRegex(@"\[libaom-av1 @ [^\]]+\] v(?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex LibaomVersionRegex();

        [GeneratedRegex(@"SVT-AV1 Encoder Lib v(?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex SvtVersionRegex();

        [GeneratedRegex(@"\[libdav1d @ [^\]]+\] libdav1d (?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex Dav1dVersionRegex();
    }
}
