using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.CorpusGenerator.Common;
using Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.CorpusGenerator;

/// <summary>
/// Generates the HEVC conformance vectors of the corpus: raw HEVC byte streams (H.265 Annex B) under
/// tests/Meziantou.Framework.Imaging.Fixtures/hevc and the <c>hevcVectors</c> section of the manifest, which records for
/// every output picture the SHA-256 of each decoded plane.
/// <para>
/// This generator is the reviewed, reproducible recipe of every committed vector. It is NEVER run by the tests and does not
/// use the library under test. Every tool comes from the meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c>
/// package (the binaries of the interop tests):
/// - the streams are encoded from deterministic lavfi sources by x265 and kvazaar (through FFmpeg) and by HM
///   (<c>TAppEncoder</c>, the only producer of PCM and of the range extensions coding tools), single-threaded;
/// - the planes are decoded by three independent decoders, FFmpeg's native hevc decoder, libde265 (<c>dec265</c>) and HM
///   (<c>TAppDecoder</c>, the reference decoder), cropped to the conformance window, at the bit depth of the stream; a
///   digest is recorded only when at least two of them produce the same bytes for every plane of every output picture,
///   in output order. The entry records which decoders agree and, for the other one, that it is excluded and why: each
///   recipe declares the disagreements it expects (<see cref="HevcVector.Excluded"/>), and any other one fails the generation;
/// - every stream carries a decoded picture hash SEI message for each picture (clause D.3.19: MD5, CRC or checksum). The
///   decoders that can check it do (FFmpeg the MD5 and TAppDecoder all three, for every picture; dec265 for the last
///   picture of the stream only, and not the checksum above 8 bits), and decoders that agree but are contradicted by
///   the hash fail the generation;
/// - the feature tags are derived from the headers as parsed by a fourth implementation, FFmpeg's trace_headers bitstream
///   filter, never from the encoder options: an option does not guarantee that a tool is used. Each vector lists the tags
///   it exists for (<see cref="HevcVector.Requires"/>); the generation fails when the encoder did not produce them.
/// </para>
/// <para>
/// Plane digest: SHA-256 of the samples of one plane of one output picture, cropped to the conformance window, rows in
/// order, 1 byte per sample up to 8 bits and 2 little-endian bytes above, no padding. Planes are in bitstream order Y, Cb,
/// Cr (G, B, R for an identity-matrix stream); a monochrome stream has one plane.
/// </para>
/// <para>
/// Output pictures: the pictures a decoder outputs, in output order (increasing picture order count inside a coded video
/// sequence, sequence after sequence), which is not the decoding order as soon as a stream has B pictures. A RASL picture
/// whose IRAP picture starts a sequence is not output. The generator derives the pictures, their order, their sizes and
/// their picture order counts from the headers, and checks them against what the decoders report.
/// </para>
/// <para>
/// A few vectors are derived from the stream of another recipe, for what no encoder writes
/// (<see cref="HevcDerivation"/>, HevcCorpus.Stream.cs): a conformance window rewritten by FFmpeg's hevc_metadata, NAL
/// units removed, inserted or retyped. They are recorded like any vector, from the final stream, and tagged
/// <c>hevc.derived</c>.
/// </para>
/// <para>
/// It complements the other generators: this generator only replaces the <c>hevcVectors</c> section and the files it
/// lists, and keeps every fixture and file of the other generators unchanged (they keep this section and its files in return).
/// </para>
/// Usage (from the repository root):
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hevc            # check: regenerate in a temporary directory and diff
///     dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hevc --write    # explicit regeneration (review the diff afterwards!)
/// Requirements: none besides network access to download the pinned tools once (override a version mismatch with
/// --accept-tool-versions after reviewing the differences). The committed vectors were generated on macOS arm64; the
/// encoders are not guaranteed to make the same decisions on another platform.
/// </summary>
internal static partial class HevcCorpus
{
    public const string ScriptPath = "tools/Meziantou.Framework.Imaging.CorpusGenerator/HevcCorpus.cs";

    // Tool versions used to produce the committed vectors. Regenerating with other versions is allowed only explicitly
    // (--accept-tool-versions) and must be reviewed: the encoded inputs change with the encoders, and the digests must not
    // change with the decoders.
    private static readonly (string Name, string Version)[] PinnedTools =
    [
        ("ffmpeg", "9.0.2"),
        ("x265", "4.2"),
        ("kvazaar", "2.3.2"),
        ("libde265", "1.1.3"),
        ("HM", "18.0"),
    ];

    private const string PlaneDigestDescription =
        "SHA-256 of the samples of one plane of one output picture, cropped to the conformance window: rows in order, exactly plane-width samples per row, " +
        "1 byte per sample up to 8 bits and 2 little-endian bytes above, no padding. Planes are in bitstream order Y, Cb, Cr (planar GBR order G, B, R for an " +
        "identity-matrix stream), one plane for a monochrome stream; a chroma plane is width / SubWidthC samples wide and height / SubHeightC rows high. " +
        "A digest is recorded only when at least two independent decoders produce the same bytes, and no decoded picture hash of the stream contradicts them.";

    private const string PictureHashDescription =
        "The decoded picture hash SEI message of each picture (H.265, clause D.3.19), read from the stream by the header parser: one lowercase hexadecimal value per " +
        "colour component (16 bytes for md5, 2 for crc, 4 for checksum). It covers the whole decoded picture, before the conformance window is applied, " +
        "where the plane digests cover the cropped picture.";

    private const string DecoderFfmpeg = "ffmpeg";
    private const string DecoderLibde265 = "libde265";
    private const string DecoderHm = "hm";

    private static readonly string[] DecoderIds = [DecoderFfmpeg, DecoderLibde265, DecoderHm];

    public static int Run(GeneratorOptions options)
    {
        var tools = new HevcTools(options.AcceptToolVersions, encoders: true);
        return CorpusDriver.Run(options, "mfi-hevc-corpus-", output => Generate(output, tools),
            (vectors, files, bytes) => Inv($"{vectors} HEVC vectors; corpus: {files} files, {bytes} bytes"),
            "The committed HEVC vectors are reproducible.");
    }

    private static int Generate(FullPath outDir, HevcTools tools)
    {
        // Every fixture and file of the other generators is kept unchanged
        var manifest = CorpusDriver.LoadCommittedManifest();
        CorpusDriver.CopyCommittedFiles(outDir, CorpusDriver.Fixtures(manifest));
        CorpusDriver.CopyCommittedAv1Vectors(outDir, manifest);
        CorpusDriver.PreserveColorSections(outDir, manifest);
        CorpusDriver.PreserveFonts(outDir, manifest);

        var vectors = Vectors();
        Py.Assert(vectors.Select(v => v.Name).Distinct(StringComparer.Ordinal).Count() == vectors.Count, "duplicate vector name");
        var entries = new Obj?[vectors.Count];
        var failures = new ConcurrentBag<string>();
        Directory.CreateDirectory(outDir / "hevc");
        Parallel.For(0, vectors.Count, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, i =>
        {
            try
            {
                entries[i] = BuildVector(outDir, tools, vectors, vectors[i]);
            }
            catch (ToolException error)
            {
                failures.Add($"hevc/{vectors[i].Name}: {error.Message.TrimEnd()}");
            }
        });

        if (!failures.IsEmpty)
            throw new FatalException($"{failures.Count} HEVC vector(s) cannot be generated; nothing is recorded for them:\n  " + string.Join("\n  ", failures.Order(StringComparer.Ordinal)));

        var bytes = entries.Sum(entry => new FileInfo(outDir / (string)((Obj)entry!["input"]!)["path"]!).Length);
        Console.WriteLine(Inv($"hevc: {entries.Length} vectors, {bytes} input bytes"));
        manifest[CorpusDriver.HevcVectorsKey] = new Obj
        {
            ["generator"] = new Obj
            {
                ["script"] = ScriptPath,
                ["command"] = "dotnet run --project tools/Meziantou.Framework.Imaging.CorpusGenerator -- hevc --write",
                ["tools"] = new List<string> { tools.FfmpegLabel, tools.X265Label, tools.KvazaarLabel, tools.Libde265Label, tools.HmLabel, ToolSet.RuntimeLabel },
            },
            ["headerParser"] = new Obj
            {
                ["name"] = tools.FfmpegLabel + " (trace_headers bitstream filter)",
                ["command"] = CommonCorpus.Display(TraceCommand(tools, "{input}"), tools, []),
            },
            ["decoders"] = new List<object?>
            {
                new Obj { ["id"] = DecoderFfmpeg, ["name"] = tools.FfmpegLabel + " (hevc)", ["command"] = CommonCorpus.Display(FfmpegDecodeCommand(tools, "{input}", "{pixelFormat}", "{frames}/" + FramePattern), tools, []) },
                new Obj { ["id"] = DecoderLibde265, ["name"] = tools.Libde265Label + " (dec265)", ["command"] = CommonCorpus.Display(Dec265Command(tools, "{input}", "{frames}.yuv"), tools, []) },
                new Obj { ["id"] = DecoderHm, ["name"] = tools.HmLabel + " (TAppDecoder)", ["command"] = CommonCorpus.Display(HmDecodeCommand(tools, "{input}", "{frames}.yuv"), tools, []) },
            },
            ["planeDigest"] = PlaneDigestDescription,
            ["pictureHash"] = PictureHashDescription,
            ["entries"] = entries.OrderBy(entry => (string)entry!["id"]!, StringComparer.Ordinal).Cast<object?>().ToList(),
        };
        CorpusDriver.WriteManifest(outDir, manifest);
        return entries.Length;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // One vector: encode, derive, parse the headers, decode three times, digest
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The packets go to the framecrc muxer, which accepts a stream whose dimensions FFmpeg does not know: its
    /// parser does not read the sequence parameter set of a stream above 12 bits. The trace is the log.</summary>
    private static List<string> TraceCommand(HevcTools tools, string input) =>
        [tools.Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "info", "-f", "hevc", "-i", input, "-c", "copy", "-bsf:v", "trace_headers", "-f", "framecrc", "-"];

    /// <summary>The name of the raw file of an output picture, numbered from 1 in output order.</summary>
    private const string FramePattern = "%05d.raw";

    /// <summary>The common part of the FFmpeg commands that read a deterministic lavfi source.</summary>
    private static List<string> SourceCommand(HevcTools tools, HevcVector vector) =>
        tools.FfmpegCmd(["-filter_threads", "1", "-f", "lavfi", "-i", vector.Source, "-frames:v", vector.EncodedFrames.ToString(CultureInfo.InvariantCulture),
            "-sws_flags", "accurate_rnd+full_chroma_int+bitexact", "-pix_fmt", vector.SourcePixelFormat]);

    /// <summary>Encodes the stream of a recipe in a scratch directory.</summary>
    /// <returns>The stream, the commands that wrote it (symbolic file names) and the tools they ran.</returns>
    private static (byte[] Data, List<string> Commands, List<string> Tools) Encode(HevcTools tools, HevcVector vector, FullPath scratch)
    {
        var output = scratch / "stream.hevc";
        var substitutions = new List<(string Path, string Symbol)> { (output.Value, "{input}") };
        var arguments = vector.Arguments;
        if (vector.File is { } file)
        {
            var path = scratch / file.Name;
            File.WriteAllText(path, file.Content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            arguments = [.. arguments.Select(argument => argument.Replace("{file}", path.Value, StringComparison.Ordinal))];
            substitutions.Add((path.Value, "{" + file.Name + "}"));
        }

        var commands = new List<List<string>>();
        List<string> labels;
        switch (vector.Encoder)
        {
            case EncoderX265:
                commands.Add([.. SourceCommand(tools, vector), "-c:v", "libx265", "-x265-params", arguments[0], "-threads", "1", "-flags", "+bitexact", "-f", "hevc", output.Value]);
                labels = [tools.FfmpegLabel, tools.X265Label];
                break;
            case EncoderKvazaar:
                commands.Add([.. SourceCommand(tools, vector), "-c:v", "libkvazaar", "-kvazaar-params", arguments[0], "-threads", "1", "-flags", "+bitexact", "-f", "hevc", output.Value]);
                labels = [tools.FfmpegLabel, tools.KvazaarLabel];
                break;
            case EncoderHm:
                var source = scratch / "source.yuv";
                substitutions.Add((source.Value, "{source}.yuv"));
                var (width, height) = vector.SourceSize;
                commands.Add([.. SourceCommand(tools, vector), "-f", "rawvideo", source.Value]);
                commands.Add([tools.HmEncoder, "-i", source.Value, "-b", output.Value, Inv($"--SourceWidth={width}"), Inv($"--SourceHeight={height}"), Inv($"--FramesToBeEncoded={vector.EncodedFrames}"), .. arguments]);
                labels = [tools.FfmpegLabel, tools.HmLabel];
                break;
            default:
                throw new ToolException("Unknown encoder " + vector.Encoder);
        }

        foreach (var command in commands)
        {
            var result = Proc.Exec(command);
            if (result.ExitCode != 0)
                throw new ToolException($"the encoder failed: {LastLines(Encoding.UTF8.GetString(result.Stderr) + Encoding.UTF8.GetString(result.Stdout))}");
        }

        if (!File.Exists(output))
            throw new ToolException("the encoder wrote no stream.");

        var data = File.ReadAllBytes(output);
        if (data.Length == 0)
            throw new ToolException("the encoder wrote an empty stream.");

        return (data, [.. commands.Select(command => CommonCorpus.Display(command, tools, substitutions))], labels);
    }

    private static string LastLines(string text)
    {
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(" | ", lines.TakeLast(4));
    }

    private static Obj BuildVector(FullPath outDir, HevcTools tools, IReadOnlyList<HevcVector> vectors, HevcVector vector)
    {
        var relative = $"hevc/{vector.Name}.hevc";
        var path = outDir / relative;
        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-hevc-vector-").FullName);
        try
        {
            var (data, commands, labels) = Encode(tools, vector, scratch);
            Obj? parameters = null;
            if (vector.File is { } file)
            {
                parameters = new Obj
                {
                    ["files"] = new List<object?> { new Obj { ["name"] = file.Name, ["sha256"] = Bytes.Sha256Hex(Encoding.UTF8.GetBytes(file.Content)), ["description"] = file.Description } },
                };
            }

            if (vector.Derivation is { } derivation)
            {
                // A derived recipe: everything below (headers, decoders, digests) is that of the derived stream
                var sourceTrace = ParseTrace(Trace(tools, data, scratch), SplitAnnexB(data));
                byte[]? second = null;
                if (derivation.Append is { } appended)
                {
                    var other = vectors.Single(candidate => candidate.Name == appended);
                    var otherScratch = scratch / "appended";
                    Directory.CreateDirectory(otherScratch);
                    (second, var otherCommands, var otherLabels) = Encode(tools, other, otherScratch);
                    commands.AddRange(otherCommands);
                    labels = [.. labels.Union(otherLabels, StringComparer.Ordinal)];
                }

                var derived = Derive(tools, derivation, data, sourceTrace, second, scratch, out var description, out var derivationCommand);
                parameters ??= [];
                parameters["derivedFrom"] = "hevc/" + vector.DerivedFrom;
                parameters["sourceSha256"] = Bytes.Sha256Hex(data);
                parameters["derivation"] = derivation.Kind;
                parameters["description"] = description;
                if (second is not null)
                {
                    parameters["appended"] = "hevc/" + derivation.Append;
                    parameters["appendedSha256"] = Bytes.Sha256Hex(second);
                }

                if (derivationCommand is not null)
                    commands.Add(derivationCommand);

                data = derived;
            }

            File.WriteAllBytes(path, data);
            var provenance = new Obj
            {
                ["origin"] = "generated",
                ["license"] = Corpus<HevcTools>.License,
                ["author"] = Corpus<HevcTools>.Author,
                ["generator"] = $"{ScriptPath} (Vectors)",
                ["tools"] = labels,
            };
            if (parameters is not null)
                provenance["parameters"] = parameters;

            provenance["commands"] = commands;
            return DescribeVector(tools, vector, path, relative, data, provenance, scratch);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    private static string Trace(HevcTools tools, byte[] stream, FullPath scratch)
    {
        var path = scratch / Inv($"trace-{Guid.NewGuid():N}.hevc");
        File.WriteAllBytes(path, stream);
        try
        {
            return Proc.RunText(TraceCommand(tools, path.Value), stderr: true);
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Records a stream: its headers as trace_headers reads them, the feature tags, the output pictures, and the digests
    /// of the planes that at least two decoders agree on.
    /// </summary>
    private static Obj DescribeVector(HevcTools tools, HevcVector vector, FullPath path, string relative, byte[] data, Obj provenance, FullPath scratch)
    {
        // Headers, by a parser independent of the encoders and of the decoders
        var trace = ParseTrace(Proc.RunText(TraceCommand(tools, path.Value), stderr: true), SplitAnnexB(data));

        var formats = trace.Pictures.Select(picture => picture.Format with { FullRange = false }).Distinct().ToList();
        if (formats.Count != 1)
            throw new ToolException("the sample format changes inside the stream: the planes of its pictures have no common layout.");

        var format = formats[0] with { FullRange = trace.Pictures.All(picture => picture.Format.FullRange) };
        if (trace.Pictures.Any(picture => picture.Format.FullRange != format.FullRange))
            throw new ToolException("the range of the samples changes inside the stream.");
        if (format.PixelFormat != vector.PixelFormat)
            throw new ToolException($"the stream is {format.PixelFormat}, not the expected {vector.PixelFormat}.");

        var outputs = trace.Outputs;
        if (outputs.Count != vector.Frames)
            throw new ToolException(Inv($"the stream has {outputs.Count} output pictures, {vector.Frames} expected ({trace.Pictures.Count} coded pictures, {trace.Pictures.Count(picture => picture.IsSkipped)} skipped)."));

        // The pictures have the size of the source: a source filter that rounds its size, or an encoder that crops or
        // scales, would otherwise go unnoticed
        if (vector.Derivation is null)
        {
            var (width, height) = vector.SourceSize;
            var expectedSize = vector.OutputSize ?? Inv($"{width}x{height}");
            var different = outputs.Select(picture => Inv($"{picture.Size.Width}x{picture.Size.Height}")).Where(size => size != expectedSize).Distinct(StringComparer.Ordinal).ToList();
            if (different.Count > 0)
                throw new ToolException($"the output pictures are {string.Join(", ", different)}, {expectedSize} expected.");
        }

        var features = Features(trace);
        features.Add("hevc.encoder=" + vector.Encoder);
        if (vector.Derivation is { } derivation)
            features.Add("hevc.derived=" + derivation.Kind);

        if (vector.Inter != features.Contains("hevc.frameTypes=inter"))
            throw new ToolException(vector.Inter ? "every slice of the stream is an I slice; an inter vector has P or B slices." : "the stream has P or B slices; an intra vector is made of I slices.");

        // Every picture carries its hash, and a stream has one type of hash
        var hashes = trace.Pictures.Select(ReadPictureHash).ToList();
        var hashTypes = hashes.Select(hash => hash.Type).Distinct(StringComparer.Ordinal).ToList();
        if (hashTypes.Count != 1)
            throw new ToolException("the pictures of the stream do not all have the same type of hash: " + string.Join(", ", hashTypes));

        var hashType = hashTypes[0];
        var sizes = outputs.Select(picture => picture.Size).ToList();
        var results = new List<DecoderResult>
        {
            DecodeFfmpeg(tools, path, format, sizes, outputs.Select(picture => picture.FirstSliceType).ToList(), hashType),
            DecodeLibde265(tools, path, format, sizes, hashType, trace.Pictures.Count, scratch),
            DecodeHm(tools, path, format, sizes, [.. trace.Pictures.Where(picture => !picture.IsSkipped).Select(picture => picture.Poc)], scratch),
        };

        // The decoders that produce the same bytes for every plane of every output picture. Agreeing decoders that the
        // picture hash contradicts are not a reference
        var groups = results.Where(result => result.Frames is not null).GroupBy(result => result, DecoderResultComparer.Instance).Select(group => group.ToList()).OrderByDescending(group => group.Count).ToList();
        var agreed = groups.FirstOrDefault(group => group.Count >= 2 && group.All(result => result.Hash != HashCheck.Mismatch));
        List<DecoderResult> verified;
        if (agreed is not null)
        {
            verified = [.. agreed.Where(result => result.Hash == HashCheck.Verified)];
            if (verified.Count == 0)
                throw new ToolException($"{Describe(results)}: none of the agreeing decoders can check the {hashType} hash of the stream; use another type of hash.");
            if (vector.WithoutDigests)
                throw new ToolException($"{Describe(results)}: the recipe expects no agreement, but {string.Join(" and ", agreed.Select(result => result.Decoder))} agree.");
        }
        else
        {
            // No digest: only for a stream the library rejects, and that the reference decoder decodes with the right hashes
            verified = [.. results.Where(result => result.Decoder == DecoderHm && result.Frames is not null && result.Hash == HashCheck.Verified)];
            if (groups.Count > 0 && groups[0].Count >= 2 && verified.Count == 0)
                throw new ToolException($"{string.Join(" and ", groups[0].Select(result => result.Decoder))} agree, but not with the picture hashes of the stream: the encoder did not write the hashes of the pictures it coded ({Describe(results)}).");
            if (!vector.WithoutDigests || verified.Count == 0)
                throw new ToolException($"no two decoders agree without being contradicted by the picture hashes ({Describe(results)}); no digest is recorded from a single decoder.");
        }

        // The decoders that are left out, which the recipe must expect one by one
        var consistent = agreed ?? verified;
        var excluded = results.Where(result => !consistent.Contains(result)).ToList();
        var unexpected = excluded.Select(result => result.Decoder).Except(vector.Excluded.Select(exclusion => exclusion.Decoder), StringComparer.Ordinal).ToList();
        var missing = vector.Excluded.Select(exclusion => exclusion.Decoder).Except(excluded.Select(result => result.Decoder), StringComparer.Ordinal).ToList();
        if (unexpected.Count > 0 || missing.Count > 0)
        {
            throw new ToolException($"{Describe(results)}: " + (unexpected.Count > 0
                ? $"the recipe does not expect {string.Join(" and ", unexpected)} to be left out."
                : $"the recipe expects {string.Join(" and ", missing)} to be left out, and it agrees with the others."));
        }

        var reference = agreed?[0].Frames;
        if (reference is not null && features.Contains("hevc.transquantBypass") && vector.Derivation is null && IsLossless(tools, vector, format, sizes, reference))
        {
            // The only tag that is measured: the decoded pictures are the source pictures
            features.Add("hevc.lossless");
        }

        var absent = vector.Requires.Where(tag => !HasFeature(features, tag)).ToList();
        if (absent.Count > 0)
            throw new ToolException($"the stream does not signal {string.Join(", ", absent)} (it signals {string.Join(" ", features)}).");
        if (features.Any(feature => feature.StartsWith("hevc.unsupported=", StringComparison.Ordinal)) != vector.Requires.Any(tag => tag.StartsWith("hevc.unsupported", StringComparison.Ordinal)))
            throw new ToolException("the stream uses something the library does not decode (hevc.unsupported), or the recipe requires it and the stream does not: " + string.Join(" ", features.Where(feature => feature.StartsWith("hevc.unsupported", StringComparison.Ordinal))));

        var frames = new List<object?>();
        for (var i = 0; i < outputs.Count; i++)
        {
            frames.Add(new Obj
            {
                ["width"] = sizes[i].Width,
                ["height"] = sizes[i].Height,
                ["poc"] = outputs[i].Poc,
                ["decodeOrder"] = outputs[i].DecodeIndex,
                ["planes"] = reference is null ? [] : reference[i].Select(plane => Bytes.Sha256Hex(plane)).ToList(),
                ["hash"] = hashes[outputs[i].DecodeIndex].Values,
            });
        }

        var entry = new Obj
        {
            ["id"] = "hevc/" + vector.Name,
            ["input"] = new Obj { ["path"] = relative, ["sha256"] = Bytes.Sha256Hex(data), ["role"] = "encoded" },
            ["provenance"] = provenance,
            ["features"] = features.ToList(),
            ["pixelFormat"] = format.PixelFormat,
            ["bitDepth"] = format.BitDepthLuma,
        };
        if (format.ChromaFormatIdc != 0 && format.BitDepthChroma != format.BitDepthLuma)
            entry["bitDepthChroma"] = format.BitDepthChroma;

        entry["chromaFormat"] = format.ChromaFormat;
        entry["pictureCount"] = trace.Pictures.Count;
        entry["frameCount"] = outputs.Count;
        entry["outputOrder"] = features.Contains("hevc.outputOrder=reordered") ? "reordered" : "decoding";
        entry["decoders"] = new Obj
        {
            ["agree"] = DecoderIds.Where(id => consistent.Any(result => result.Decoder == id)).ToList(),
            ["excluded"] = DecoderIds.Where(id => excluded.Any(result => result.Decoder == id)).Select(id =>
            {
                var result = excluded.Single(candidate => candidate.Decoder == id);
                return (object?)new Obj
                {
                    ["decoder"] = id,
                    ["outcome"] = result.Decodes ? "differs" : "rejects",
                    ["reason"] = vector.Excluded.Single(exclusion => exclusion.Decoder == id).Reason,
                    ["observed"] = result.Frames is null ? result.Failure : Difference(result, consistent[0]),
                };
            }).ToList(),
        };
        entry["pictureHash"] = new Obj { ["type"] = hashType, ["verifiedBy"] = DecoderIds.Where(id => verified.Any(result => result.Decoder == id)).ToList() };
        entry["frames"] = frames;
        return entry;

        // What a decoder that is left out outputs, compared with the decoders that agree
        static string Difference(DecoderResult result, DecoderResult reference)
        {
            var hash = result.Hash switch
            {
                HashCheck.Verified => "; its own hash check passes: it decodes the same pictures and writes them otherwise",
                HashCheck.Mismatch => "; its own check does not find the picture hashes of the stream",
                _ => "",
            };
            if (reference.Frames is null || reference.Frames.Count != result.Frames!.Count)
                return "outputs other pictures" + hash;

            var frames = Enumerable.Range(0, result.Frames.Count).Where(i => !result.Frames[i].SequenceEqual(reference.Frames[i], ByteArrayComparer.Instance)).ToList();
            var planes = frames.SelectMany(i => Enumerable.Range(0, result.Frames[i].Length).Where(plane => !result.Frames[i][plane].AsSpan().SequenceEqual(reference.Frames[i][plane]))).Distinct().Order().ToList();
            return Inv($"outputs other samples in {frames.Count} of the {result.Frames.Count} pictures (plane {string.Join(", ", planes)}; first in picture {frames[0]} of the output order)") + hash;
        }

        static string Describe(IEnumerable<DecoderResult> results) => string.Join("; ", results.Select(result =>
            result.Frames is null ? $"{result.Decoder} {(result.Decodes ? "" : "rejects the stream: ")}{result.Failure}" : Inv($"{result.Decoder} decodes {Bytes.Sha256Hex(Bytes.Concat(result.Frames.SelectMany(frame => frame)))[..12]}, hash {result.Hash}")));
    }

    /// <summary>Whether the decoded pictures are the pictures given to the encoder: the source, read again in the format of
    /// the stream.</summary>
    private static bool IsLossless(HevcTools tools, HevcVector vector, HevcFormat format, List<(int Width, int Height)> sizes, List<byte[][]> decoded)
    {
        if (vector.SourcePixelFormat != format.PixelFormat || vector.EncodedFrames != sizes.Count)
            return false;

        var source = Proc.Run([.. SourceCommand(tools, vector), "-f", "rawvideo", "-"]);
        var offset = 0;
        for (var i = 0; i < sizes.Count; i++)
        {
            var length = format.FrameLength(sizes[i]);
            if (offset + length > source.Length || !source.AsSpan(offset, length).SequenceEqual(Bytes.Concat(decoded[i])))
                return false;

            offset += length;
        }

        return offset == source.Length;
    }

    /// <summary>Whether the tags include a tag, one of the comma-separated values of a <c>key=value</c> tag, or, for a bare
    /// key, a tag of that key whatever its value.</summary>
    private static bool HasFeature(IReadOnlyCollection<string> features, string tag)
    {
        if (features.Contains(tag, StringComparer.Ordinal))
            return true;

        var separator = tag.IndexOf('=', StringComparison.Ordinal);
        if (separator < 0)
            return features.Any(feature => feature.StartsWith(tag + "=", StringComparison.Ordinal));

        var prefix = tag[..(separator + 1)];
        return features.Any(feature => feature.StartsWith(prefix, StringComparison.Ordinal) && feature[prefix.Length..].Split(',').Contains(tag[prefix.Length..], StringComparer.Ordinal));
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Decoders
    // -----------------------------------------------------------------------------------------------------------------

    private enum HashCheck
    {
        /// <summary>The decoder does not check this type of hash, or not for every picture.</summary>
        NotChecked,

        /// <summary>The decoder computed the hash of every picture of the stream and found the values of the stream.</summary>
        Verified,

        /// <summary>The decoder computed other values than those of the stream.</summary>
        Mismatch,
    }

    /// <param name="Frames">The planes of every output picture, in output order and in the digest layout, or
    /// <see langword="null"/> when the decoder rejects the stream or does not output the expected pictures.</param>
    /// <param name="Failure">Why there are no frames.</param>
    /// <param name="Decodes">Whether the decoder decodes the stream: without frames, it then outputs other pictures than
    /// the expected ones (another number, other sizes).</param>
    private sealed record DecoderResult(string Decoder, List<byte[][]>? Frames, HashCheck Hash, string? Failure, bool Decodes = true)
    {
        public static DecoderResult Rejected(string decoder, string failure) => new(decoder, Frames: null, HashCheck.NotChecked, failure, Decodes: false);

        public static DecoderResult OtherPictures(string decoder, string failure) => new(decoder, Frames: null, HashCheck.NotChecked, failure);
    }

    private sealed class ByteArrayComparer : IEqualityComparer<byte[]>
    {
        public static ByteArrayComparer Instance { get; } = new();

        public bool Equals(byte[]? x, byte[]? y) => x is not null && y is not null && x.AsSpan().SequenceEqual(y);

        public int GetHashCode(byte[] obj) => obj.Length;
    }

    /// <summary>Two results are equal when the decoders produced the same bytes.</summary>
    private sealed class DecoderResultComparer : IEqualityComparer<DecoderResult>
    {
        public static DecoderResultComparer Instance { get; } = new();

        public bool Equals(DecoderResult? x, DecoderResult? y)
        {
            if (x?.Frames is null || y?.Frames is null || x.Frames.Count != y.Frames.Count)
                return false;

            for (var i = 0; i < x.Frames.Count; i++)
            {
                if (x.Frames[i].Length != y.Frames[i].Length)
                    return false;

                for (var plane = 0; plane < x.Frames[i].Length; plane++)
                {
                    if (!x.Frames[i][plane].AsSpan().SequenceEqual(y.Frames[i][plane]))
                        return false;
                }
            }

            return true;
        }

        // Equal results have the same number of pictures: enough to group three decoders
        public int GetHashCode(DecoderResult obj) => obj.Frames?.Count ?? -1;
    }

    /// <summary>One pass, one thread, the raw planes of every output picture in the native pixel format and in a file of
    /// its own, without scaling. The MD5 of each picture is checked (<c>crccheck</c>): a mismatch is logged and the
    /// decoding goes on. <c>unaligned</c>: the conformance window is applied exactly, whatever its left offset.</summary>
    private static List<string> FfmpegDecodeCommand(HevcDecoders tools, string input, string pixelFormat, string output) =>
        [tools.Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "error", "-y", "-threads", "1", "-err_detect", "crccheck", "-flags", "unaligned", "-c:v", "hevc", "-f", "hevc", "-i", input,
            "-fps_mode", "passthrough", "-noautoscale", "-f", "image2", "-c:v", "rawvideo", "-pix_fmt", pixelFormat, output];

    /// <summary>One thread, the planes of every output picture appended to one file, each plane at its own bit depth.</summary>
    private static List<string> Dec265Command(HevcDecoders tools, string input, string output) => [tools.Dec265, "-q", "-t", "0", "-o", output, input];

    /// <summary>The same decoding with the hash check of dec265 (the last picture of the stream only), and no output.</summary>
    private static List<string> Dec265CheckCommand(HevcDecoders tools, string input) => [tools.Dec265, "-q", "-t", "0", "-c", input];

    /// <summary>The planes of every output picture appended to one file, at the bit depth of the stream (<c>-d 0</c>).
    /// TAppDecoder checks the hash of every picture by default, and reports each one.</summary>
    private static List<string> HmDecodeCommand(HevcDecoders tools, string input, string output) => [tools.HmDecoder, "-b", input, "-o", output, "-d", "0"];

    /// <summary>FFmpeg's native hevc decoder. ffprobe must report the expected size and the native pixel format for every
    /// frame (no conversion), and the picture types in output order must be those of the headers.</summary>
    /// <param name="sliceTypes">The slice type of the first slice of each output picture (0 is B, 1 is P, 2 is I), or
    /// <see langword="null"/> when the headers were not parsed (verification).</param>
    /// <param name="hashType">The type of the hashes of the stream, or <see langword="null"/> when they are not checked.</param>
    private static DecoderResult DecodeFfmpeg(HevcDecoders tools, FullPath path, HevcFormat format, List<(int Width, int Height)> sizes, List<int>? sliceTypes, string? hashType)
    {
        if (format.BitDepthLuma > 12 || format.BitDepthChroma > 12 || (format.ChromaFormatIdc != 0 && format.BitDepthLuma != format.BitDepthChroma))
        {
            // No FFmpeg pixel format holds these samples: the decoder is expected to refuse the stream
            var attempt = Proc.Exec([tools.Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "error", "-threads", "1", "-c:v", "hevc", "-f", "hevc", "-i", path.Value, "-f", "null", "-"]);
            return DecoderResult.Rejected(DecoderFfmpeg, attempt.ExitCode != 0 ? FirstLine(attempt.Stderr) : "decodes the stream to a pixel format that does not hold its samples");
        }

        var probe = Proc.Exec([tools.Ffprobe, "-hide_banner", "-v", "error", "-threads", "1", "-flags", "unaligned", "-f", "hevc", "-c:v", "hevc", "-select_streams", "v:0",
            "-show_entries", "frame=width,height,pix_fmt,pict_type", "-of", "csv=print_section=0", path.Value]);
        if (probe.ExitCode != 0 || Encoding.UTF8.GetString(probe.Stderr).Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line => !line.Contains("missing picture in access unit", StringComparison.Ordinal)))
            return DecoderResult.Rejected(DecoderFfmpeg, FirstLine(probe.Stderr));

        var probed = Encoding.UTF8.GetString(probe.Stdout).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(line => line.Split(',')).ToList();
        var expected = sizes.Select((size, index) => Inv($"{size.Width}x{size.Height}/{format.FfmpegPixelFormat}") + (sliceTypes is null ? "" : "/" + "BPI"[sliceTypes[index]])).ToList();
        var actual = probed.Select(parts => $"{parts[0]}x{parts[1]}/{parts[2]}" + (sliceTypes is null ? "" : "/" + parts[3])).ToList();
        if (!actual.SequenceEqual(expected, StringComparer.Ordinal))
            return DecoderResult.OtherPictures(DecoderFfmpeg, $"outputs the frames {string.Join(' ', actual)} (ffprobe); the headers describe {string.Join(' ', expected)}");

        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-hevc-frames-").FullName);
        try
        {
            var result = Proc.Exec(FfmpegDecodeCommand(tools, path.Value, format.FfmpegPixelFormat, (scratch / FramePattern).Value));
            var log = Encoding.UTF8.GetString(result.Stderr);
            var lines = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            // The only accepted messages: the MD5 of a picture is not the one of the stream; the parser found NAL units
            // without a picture after the last picture (an end of sequence, an end of bitstream), which it reports and
            // the decoder ignores
            var mismatch = lines.Any(line => line.Contains("mismatching checksum", StringComparison.Ordinal));
            var unexpected = lines.Where(line => !line.Contains("mismatching checksum", StringComparison.Ordinal) && !line.Contains("missing picture in access unit", StringComparison.Ordinal)).ToList();
            if (result.ExitCode != 0 || unexpected.Count > 0)
                return DecoderResult.Rejected(DecoderFfmpeg, unexpected.Count > 0 ? AddressRegex().Replace(unexpected[0], "") : Inv($"exit code {result.ExitCode}"));

            // Numbered from 1 with a fixed width: the ordinal order of the names is the output order
            var files = Directory.EnumerateFiles(scratch).Select(file => FullPath.FromPath(file)).OrderBy(file => file.Name, StringComparer.Ordinal).ToList();
            if (files.Count != sizes.Count || files.Where((file, index) => file.Name != Inv($"{index + 1:D5}.raw")).Any())
                return DecoderResult.Rejected(DecoderFfmpeg, Inv($"writes {files.Count} frames, not {sizes.Count}"));

            var frames = new List<byte[][]>();
            for (var i = 0; i < files.Count; i++)
            {
                var raw = File.ReadAllBytes(files[i]);
                if (raw.Length != format.FrameLength(sizes[i]) || SplitPlanes(raw, 0, format, sizes[i], wideSamples: null) is not { } planes)
                    return DecoderResult.Rejected(DecoderFfmpeg, Inv($"frame {i} does not have the bytes of a {sizes[i].Width}x{sizes[i].Height} {format.PixelFormat} frame"));

                frames.Add(planes);
            }

            // FFmpeg only checks the MD5 (the other hashes are read and ignored), and only reports a mismatch
            return new DecoderResult(DecoderFfmpeg, frames, mismatch ? HashCheck.Mismatch : hashType == "md5" ? HashCheck.Verified : HashCheck.NotChecked, Failure: null);
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }
    }

    /// <summary>libde265, through dec265. Its hash check stops at a mismatch without writing a picture, so it is a second
    /// run. It is not run for the checksum of a picture above 8 bits, which this version computes as for 8 bits. And it
    /// only compares the hash of the last picture of the stream (a wrong hash of any other picture goes unnoticed): a
    /// mismatch rules the decoder out, but the check only verifies the hashes of a stream of one picture.</summary>
    /// <param name="hashType">The type of the hashes of the stream, or <see langword="null"/> not to check them.</param>
    /// <param name="pictureCount">The number of coded pictures of the stream.</param>
    private static DecoderResult DecodeLibde265(HevcDecoders tools, FullPath path, HevcFormat format, List<(int Width, int Height)> sizes, string? hashType, int pictureCount, FullPath scratch)
    {
        var output = scratch / Inv($"dec265-{Guid.NewGuid():N}.yuv");
        var result = Proc.Exec(Dec265Command(tools, path.Value, output.Value));
        var log = (Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr)).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var unexpected = log.Where(line => !line.StartsWith("nFrames decoded:", StringComparison.Ordinal)).ToList();
        if (result.ExitCode != 0 || unexpected.Count > 0 || !File.Exists(output))
            return DecoderResult.Rejected(DecoderLibde265, unexpected.Count > 0 ? unexpected[0] : Inv($"exit code {result.ExitCode}"));

        // dec265 writes each plane at its own bit depth: the digest layout
        if (SplitFrames(File.ReadAllBytes(output), format, sizes, wideSamples: null) is not { } frames)
            return DecoderResult.OtherPictures(DecoderLibde265, Inv($"outputs {new FileInfo(output).Length} bytes, not the {sizes.Sum(format.FrameLength)} bytes of the {sizes.Count} output pictures"));

        var hash = HashCheck.NotChecked;
        if (hashType is not null && (hashType != "checksum" || (format.BitDepthLuma == 8 && format.BitDepthChroma == 8)))
        {
            var check = Proc.Exec(Dec265CheckCommand(tools, path.Value));
            var checkLog = Encoding.UTF8.GetString(check.Stdout) + Encoding.UTF8.GetString(check.Stderr);
            if (check.ExitCode == 0)
                hash = pictureCount == 1 ? HashCheck.Verified : HashCheck.NotChecked;
            else if (checkLog.Contains("checksum mismatch", StringComparison.Ordinal))
                hash = HashCheck.Mismatch;
            else
                return DecoderResult.Rejected(DecoderLibde265, "the hash check fails: " + checkLog.Split('\n')[0].Trim());
        }

        return new DecoderResult(DecoderLibde265, frames, hash, Failure: null);
    }

    /// <summary>HM, through TAppDecoder: the reference decoder. Its log has one line per decoded picture, in decoding
    /// order, with the picture order count and the result of the hash check.</summary>
    /// <param name="pocs">The picture order counts of the decoded pictures, in decoding order, as derived from the
    /// headers, or <see langword="null"/> when the headers were not parsed (verification).</param>
    private static DecoderResult DecodeHm(HevcDecoders tools, FullPath path, HevcFormat format, List<(int Width, int Height)> sizes, List<int>? pocs, FullPath scratch)
    {
        var output = scratch / Inv($"hm-{Guid.NewGuid():N}.yuv");
        var result = Proc.Exec(HmDecodeCommand(tools, path.Value, output.Value));
        var log = Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr);
        var mismatch = log.Contains("A decoding mismatch occured", StringComparison.Ordinal);
        if ((result.ExitCode != 0 && !mismatch) || !File.Exists(output))
        {
            var error = log.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault(line => line.Contains("Assertion failed", StringComparison.Ordinal) || line.Contains("rror", StringComparison.Ordinal));
            return DecoderResult.Rejected(DecoderHm, error ?? Inv($"exit code {result.ExitCode}"));
        }

        var pictures = HmPictureRegex().Matches(log);
        if (pocs is not null && !pictures.Select(match => int.Parse(match.Groups["poc"].Value, CultureInfo.InvariantCulture)).SequenceEqual(pocs))
        {
            return DecoderResult.Rejected(DecoderHm, $"decodes the pictures of order count {string.Join(' ', pictures.Select(match => match.Groups["poc"].Value))}; the headers give {string.Join(' ', pocs)}");
        }

        // TAppDecoder writes every plane with 2 bytes per sample as soon as one of them has more than 8 bits
        if (SplitFrames(File.ReadAllBytes(output), format, sizes, wideSamples: format.BitDepthLuma > 8 || format.BitDepthChroma > 8) is not { } frames)
            return DecoderResult.OtherPictures(DecoderHm, Inv($"outputs {new FileInfo(output).Length} bytes, not those of the {sizes.Count} output pictures"));

        var statuses = pictures.Select(match => match.Groups["rest"].Value).ToList();
        var hash = mismatch || statuses.Any(status => status.Contains("(***ERROR***)", StringComparison.Ordinal))
            ? HashCheck.Mismatch
            : statuses.Count > 0 && statuses.All(status => status.Contains(",(OK)]", StringComparison.Ordinal)) ? HashCheck.Verified : HashCheck.NotChecked;
        return new DecoderResult(DecoderHm, frames, hash, Failure: null);
    }

    private static string FirstLine(byte[] log)
    {
        var lines = Encoding.UTF8.GetString(log).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // An address of the process is not part of the message
        return lines.Length == 0 ? "no message" : AddressRegex().Replace(lines[0], "");
    }

    /// <summary>The pictures of a file that holds them one after the other, or <see langword="null"/> when the file does
    /// not have exactly their bytes.</summary>
    private static List<byte[][]>? SplitFrames(byte[] file, HevcFormat format, List<(int Width, int Height)> sizes, bool? wideSamples)
    {
        var frames = new List<byte[][]>();
        var offset = 0;
        foreach (var size in sizes)
        {
            if (SplitPlanes(file, offset, format, size, wideSamples) is not { } planes)
                return null;

            frames.Add(planes);
            offset += format.PlaneSizes(size).Select((plane, index) => plane.Width * plane.Height * ((wideSamples ?? format.BitDepth(index) > 8) ? 2 : 1)).Sum();
        }

        return offset == file.Length ? frames : null;
    }

    /// <summary>The planes of one picture, in the digest layout: 1 byte per sample up to 8 bits, 2 above.</summary>
    /// <param name="wideSamples">Whether the file has 2 bytes for every sample (<see langword="true"/>), 1
    /// (<see langword="false"/>), or what the digest layout has for the plane (<see langword="null"/>).</param>
    private static byte[][]? SplitPlanes(byte[] file, int offset, HevcFormat format, (int Width, int Height) size, bool? wideSamples)
    {
        var planeSizes = format.PlaneSizes(size);
        var planes = new byte[planeSizes.Count][];
        for (var i = 0; i < planes.Length; i++)
        {
            var samples = planeSizes[i].Width * planeSizes[i].Height;
            var digestBytes = format.BytesPerSample(i);
            var fileBytes = wideSamples is { } wide ? (wide ? 2 : 1) : digestBytes;
            if (offset + (samples * fileBytes) > file.Length)
                return null;

            if (fileBytes == digestBytes)
            {
                planes[i] = file.AsSpan(offset, samples * fileBytes).ToArray();
            }
            else if (fileBytes == 2)
            {
                // A plane of at most 8 bits in a file of 16-bit samples: the high byte is zero
                planes[i] = new byte[samples];
                for (var sample = 0; sample < samples; sample++)
                {
                    if (file[offset + (2 * sample) + 1] != 0)
                        return null;

                    planes[i][sample] = file[offset + (2 * sample)];
                }
            }
            else
            {
                return null;
            }

            offset += samples * fileBytes;
        }

        return planes;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Verification (the "verify" subcommand): the committed vectors against the decoders of the pinned release
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Checks every committed vector: the hash of its file, then the digests of its planes re-derived with each of the
    /// decoders that agreed when it was generated.
    /// </summary>
    /// <returns>The number of vectors checked, and the disagreements.</returns>
    public static (int Verified, List<string> Failures) Verify()
    {
        var failures = new List<string>();
        var manifest = CorpusDriver.LoadCommittedManifest();
        if (!manifest.TryGetValue(CorpusDriver.HevcVectorsKey, out var value) || value is not Obj section)
            return (0, failures);

        // Whatever the versions of the tools: the point is to measure the impact of a version change
        var tools = new HevcTools(acceptVersions: true, encoders: false);
        Console.WriteLine($"  hevc: {tools.FfmpegLabel} (hevc), {tools.Libde265Label} (dec265), {tools.HmLabel} (TAppDecoder)");
        var entries = ((List<object?>)section["entries"]!).Cast<Obj>().ToList();
        var errors = new ConcurrentBag<string>();
        Parallel.ForEach(entries, new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount / 2) }, entry =>
        {
            var id = (string)entry["id"]!;
            foreach (var error in VerifyVector(tools, entry))
                errors.Add($"{id}: {error}");
        });

        failures.AddRange(errors.Order(StringComparer.Ordinal));
        return (entries.Count, failures);
    }

    private static List<string> VerifyVector(HevcTools tools, Obj entry)
    {
        var input = (Obj)entry["input"]!;
        var path = CorpusDriver.CorpusDir / (string)input["path"]!;
        if (!File.Exists(path))
            return ["the input file is missing."];
        if (Bytes.Sha256Hex(File.ReadAllBytes(path)) != (string)input["sha256"]!)
            return ["the input file does not have the recorded SHA-256."];

        var pixelFormat = (string)entry["pixelFormat"]!;
        var bitDepth = Convert.ToInt32(entry["bitDepth"], CultureInfo.InvariantCulture);
        var format = new HevcFormat(
            (string)entry["chromaFormat"]! switch { "4:0:0" => 0, "4:2:0" => 1, "4:2:2" => 2, _ => 3 },
            bitDepth,
            entry.TryGetValue("bitDepthChroma", out var chromaDepth) ? Convert.ToInt32(chromaDepth, CultureInfo.InvariantCulture) : bitDepth,
            pixelFormat.StartsWith("gbrp", StringComparison.Ordinal),
            ((List<object?>)entry["features"]!).Contains("hevc.fullRange"));
        var frames = ((List<object?>)entry["frames"]!).Cast<Obj>().ToList();
        var sizes = frames.Select(frame => (Convert.ToInt32(frame["width"], CultureInfo.InvariantCulture), Convert.ToInt32(frame["height"], CultureInfo.InvariantCulture))).ToList();
        var errors = new List<string>();
        var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-hevc-verify-").FullName);
        try
        {
            foreach (var decoder in ((List<object?>)((Obj)entry["decoders"]!)["agree"]!).Cast<string>())
            {
                var result = decoder switch
                {
                    DecoderFfmpeg => DecodeFfmpeg(tools, path, format, sizes, sliceTypes: null, hashType: null),
                    DecoderLibde265 => DecodeLibde265(tools, path, format, sizes, hashType: null, pictureCount: 0, scratch),
                    DecoderHm => DecodeHm(tools, path, format, sizes, pocs: null, scratch),
                    _ => DecoderResult.Rejected(decoder, "unknown decoder"),
                };
                if (result.Frames is null)
                {
                    errors.Add($"{decoder}: {result.Failure}");
                    continue;
                }

                for (var i = 0; i < frames.Count; i++)
                {
                    var recorded = ((List<object?>)frames[i]["planes"]!).Cast<string>().ToList();
                    for (var plane = 0; plane < recorded.Count; plane++)
                    {
                        if (plane >= result.Frames[i].Length || Bytes.Sha256Hex(result.Frames[i][plane]) != recorded[plane])
                            errors.Add(Inv($"frame {i} plane {plane}: {decoder} decodes samples whose SHA-256 is not the recorded one."));
                    }
                }
            }
        }
        finally
        {
            Directory.Delete(scratch, recursive: true);
        }

        return errors;
    }

    // -----------------------------------------------------------------------------------------------------------------
    // Tools
    // -----------------------------------------------------------------------------------------------------------------

    /// <summary>The three decoders, and ffprobe.</summary>
    private abstract class HevcDecoders(string ffmpeg) : ToolSet(ffmpeg)
    {
        public string Ffprobe { get; protected init; } = "";

        public string Dec265 { get; protected init; } = "";

        public string HmDecoder { get; protected init; } = "";
    }

    /// <summary>The tools of the pinned meziantou/prebuilt release, and their versions as they report them.</summary>
    private sealed partial class HevcTools : HevcDecoders
    {
        /// <param name="encoders">Whether the encoders are needed: their versions are then read and compared with the pinned ones.</param>
        public HevcTools(bool acceptVersions, bool encoders)
            : base(Download(PrebuiltTools.Ffmpeg))
        {
            Ffprobe = Download(PrebuiltTools.Ffprobe);
            Dec265 = Download(PrebuiltTools.Dec265);
            HmDecoder = Download(PrebuiltTools.HmDecoder);
            HmEncoder = encoders ? Download(PrebuiltTools.HmEncoder) : "";
            DisplayNames = new Dictionary<string, string>(StringComparer.Ordinal) { [Ffmpeg] = "ffmpeg", [Ffprobe] = "ffprobe", [Dec265] = "dec265", [HmDecoder] = "TAppDecoder", [HmEncoder] = "TAppEncoder" };

            // dec265 and the HM tools have no version option: they print it with their usage
            Libde265Version = Version(Libde265VersionRegex(), Output([Dec265, "-h"]), "libde265");
            HmVersion = Version(HmVersionRegex("Decoder"), Output([HmDecoder]), "HM (TAppDecoder)");
            foreach (var capability in new[] { ("-decoders", "hevc"), ("-bsfs", "trace_headers"), ("-bsfs", "hevc_metadata") })
            {
                if (!Regex.IsMatch(Proc.RunText([Ffmpeg, "-hide_banner", capability.Item1]), @"(^|\s)" + Regex.Escape(capability.Item2) + @"(\s|$)", RegexOptions.Multiline, TimeSpan.FromSeconds(10)))
                    throw new FatalException($"The FFmpeg build has no {capability.Item2} ({capability.Item1}).");
            }

            if (!encoders)
                return;

            if (Version(HmVersionRegex("Encoder"), Output([HmEncoder]), "HM (TAppEncoder)") != HmVersion)
                throw new FatalException("TAppEncoder and TAppDecoder report different versions.");

            var scratch = FullPath.FromPath(Directory.CreateTempSubdirectory("mfi-hevc-tools-").FullName);
            try
            {
                // x265 prints its version in the log; kvazaar writes it in the SEI message that describes the encoder
                var x265 = scratch / "x265.hevc";
                var kvazaar = scratch / "kvazaar.hevc";
                X265Version = Version(X265VersionRegex(), Verbose(["-f", "lavfi", "-i", "color=size=64x64", "-frames:v", "1", "-c:v", "libx265", "-threads", "1", "-f", "hevc", x265.Value]), "x265");
                Verbose(["-f", "lavfi", "-i", "color=size=64x64", "-frames:v", "1", "-c:v", "libkvazaar", "-threads", "1", "-f", "hevc", kvazaar.Value]);
                KvazaarVersion = Version(KvazaarVersionRegex(), Encoding.Latin1.GetString(File.ReadAllBytes(kvazaar)), "kvazaar");

                // 10 and 12-bit x265, which a build may lack
                foreach (var pixelFormat in new[] { "yuv420p10le", "yuv444p12le" })
                    Verbose(["-f", "lavfi", "-i", "color=size=64x64", "-frames:v", "1", "-pix_fmt", pixelFormat, "-c:v", "libx265", "-threads", "1", "-f", "hevc", x265.Value]);
            }
            catch (ToolException error)
            {
                throw new FatalException("The FFmpeg build cannot encode with libx265 (8, 10 and 12 bits) or libkvazaar: " + error.Message);
            }
            finally
            {
                Directory.Delete(scratch, recursive: true);
            }

            var actual = new Dictionary<string, string>(StringComparer.Ordinal) { ["ffmpeg"] = FfmpegVersion, ["x265"] = X265Version, ["kvazaar"] = KvazaarVersion, ["libde265"] = Libde265Version, ["HM"] = HmVersion };
            var mismatches = PinnedTools.Where(p => actual[p.Name] != p.Version).Select(p => $"{p.Name} {actual[p.Name]} (pinned {p.Version})").ToList();
            if (mismatches.Count > 0 && !acceptVersions)
            {
                throw new FatalException("Tool versions differ from the pinned versions: " + string.Join(", ", mismatches) +
                    ". Review the impact and rerun with --accept-tool-versions (then update PinnedTools).");
            }
        }

        public string HmEncoder { get; }

        public string X265Version { get; } = "";

        public string KvazaarVersion { get; } = "";

        public string Libde265Version { get; }

        public string HmVersion { get; }

        public string X265Label => "x265 " + X265Version;

        public string KvazaarLabel => "kvazaar " + KvazaarVersion;

        public string Libde265Label => "libde265 " + Libde265Version;

        public string HmLabel => "HM " + HmVersion;

        public override IReadOnlyDictionary<string, string> DisplayNames { get; }

        private static string Download(PrebuiltTool tool) => tool.GetOrDownloadAsync(CancellationToken.None).GetAwaiter().GetResult();

        /// <summary>Everything a tool prints, whatever its exit code: the tools without a version option fail after their usage.</summary>
        private static string Output(IReadOnlyList<string> command)
        {
            var result = Proc.Exec(command);
            return Encoding.UTF8.GetString(result.Stdout) + Encoding.UTF8.GetString(result.Stderr);
        }

        private string Verbose(IEnumerable<string> arguments) => Proc.RunText([Ffmpeg, "-hide_banner", "-nostdin", "-loglevel", "verbose", "-y", .. arguments], stderr: true);

        private static string Version(Regex regex, string log, string tool)
        {
            var match = regex.Match(log);
            if (!match.Success)
                throw new FatalException($"Cannot read the version of {tool}.");
            return match.Groups["version"].Value;
        }

        [GeneratedRegex(@"HEVC encoder version (?<version>\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex X265VersionRegex();

        [GeneratedRegex(@"Kvazaar HEVC Encoder v\. (?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex KvazaarVersionRegex();

        [GeneratedRegex(@"dec265\s+v(?<version>\d+\.\d+\.\d+)", RegexOptions.ExplicitCapture)]
        private static partial Regex Libde265VersionRegex();

        private static Regex HmVersionRegex(string tool) => new(@"HM software: " + tool + @" Version \[(?<version>[0-9.]+)\]", RegexOptions.ExplicitCapture | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(10));
    }

    // "POC    4 TId: 0 ( P-SLICE, QP 30 ) ... [MD5:...,(OK)]": one line per decoded picture, in decoding order
    [GeneratedRegex(@"^POC\s+(?<poc>-?\d+)\s+TId:(?<rest>[^\n]*)$", RegexOptions.ExplicitCapture | RegexOptions.Multiline)]
    private static partial Regex HmPictureRegex();

    [GeneratedRegex(@" @ 0x[0-9a-f]+")]
    private static partial Regex AddressRegex();
}
