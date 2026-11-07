using System.Collections.Concurrent;
using System.Globalization;
using Meziantou.Framework.Imaging.Internals;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;

namespace Meziantou.Framework.Imaging.InteropTests;

// Differential test of the AV1 decoder against dav1d and libaom (both through the pinned FFmpeg build).
//
// By default, 48 parameter tuples are derived from a fixed seed, so that a run is reproducible. Environment variables turn the
// test into a longer campaign:
// - MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_SEED: the seed of the tuples (an integer; default 20261010). A tuple only depends
//   on the seed and on its index, so "seed S, case N" identifies a stream whatever the number of cases.
// - MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_CASES: the number of tuples (default 48). At least 5 tuples out of 6 must be
//   effective (encoded, and decoded identically by dav1d and libaom).
// - MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_STREAMS: a directory of local raw AV1 streams (*.obu, searched recursively), in
//   the low overhead bitstream format or in the length-delimited format of Annex B, which is converted. When it is set,
//   these streams are compared instead of generated ones; the seed and the number of cases are ignored, and at least one
//   stream must be effective. This is how third-party conformance material is run: it has no license that allows
//   committing it, so it is downloaded for the campaign and never added to the fixtures. The Argon suite
//   (https://streams.videolan.org/argon/argon.tar.zst, 2.6 GB) is the reference campaign: its 2,763 streams are Annex B
//   streams, except those of the "not_annexb" directories. Such streams have frames beyond the default limits: a local
//   stream is decoded with a live allocation limit of 8 GiB, and one that the library refuses as unsupported is reported
//   and does not count. For a stream with several spatial layers, the three decoders output the frame of the highest
//   layer of each temporal unit; dav1d and libaom do not always agree on it (dav1d waits for the highest layer of the
//   operating point), and such a stream does not count. The reference digests of the Argon suite hold every shown frame
//   of every layer instead (Av1Decoder.OutputAllLayers, "dav1d --alllayers 1").
//
// A failure writes the stream and a description of the tuple (with the FFmpeg command that encoded it) to the directory of
// MEZIANTOU_FRAMEWORK_IMAGING_INTEROP_ARTIFACTS (Av1DifferentialInteropTests), and the message names both files.

/// <summary>
/// The AV1 decoder against two independent decoders on streams that are generated for the run: libaom and SVT-AV1 encode
/// deterministic sources with parameters derived from a seed (size, sample format, frame count, quality, speed and a set of
/// encoder options), dav1d and libaom decode every output frame to raw planes, and, when they agree byte for byte, the planes
/// of the library (film grain applied) must be the same bytes.
/// </summary>
/// <remarks>
/// AV1 decoding is normative: there is no tolerance. A tuple that an encoder refuses, that a reference decoder rejects or on
/// which the two reference decoders disagree proves nothing about the library: it is reported in the test output and does
/// not count, and the test fails when too few tuples are left.
/// </remarks>
public sealed class Av1DifferentialInteropTests
{
    private const string SeedVariable = "MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_SEED";
    private const string CasesVariable = "MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_CASES";
    private const string StreamsVariable = "MEZIANTOU_FRAMEWORK_IMAGING_AV1_DIFF_STREAMS";
    private const int DefaultSeed = 20261010;
    private const int DefaultCases = 48;

    private const int LibaomOptionSets = 14;
    private const int SvtOptionSets = 10;

    private const long LocalStreamAllocationLimit = 8L << 30;

    private static readonly TimeSpan EncodeTimeout = TimeSpan.FromSeconds(30);
    private static readonly string[] PlaneNames = ["Y", "U", "V"];

    // libaom encodes every sample format of the three AV1 profiles; 4:2:0 is the most common, so it is listed more often
    private static readonly string[] LibaomPixelFormats =
    [
        "yuv420p", "yuv420p", "yuv420p", "yuv420p10le", "yuv420p10le", "yuv420p12le",
        "yuv422p", "yuv422p10le", "yuv422p12le",
        "yuv444p", "yuv444p10le", "yuv444p12le",
        "gbrp", "gbrp10le", "gbrp12le",
        "gray", "gray10le", "gray12le",
    ];

    // SVT-AV1 only encodes 4:2:0
    private static readonly string[] SvtPixelFormats = ["yuv420p", "yuv420p", "yuv420p10le"];

    private enum Outcome
    {
        Effective,
        NotCounted,
        Failed,
    }

    /// <param name="Name">The name of the stream in the report and in the artifacts.</param>
    /// <param name="Description">The parameters of the tuple, or the path of a local stream.</param>
    /// <param name="EncodeArguments">The FFmpeg arguments that encode the stream, without the output; <see langword="null"/> for a local stream.</param>
    /// <param name="LocalPath">The path of a local stream; <see langword="null"/> for a generated one.</param>
    private sealed record StreamTuple(string Name, string Description, string[]? EncodeArguments, FullPath? LocalPath);

    /// <param name="Outcome">Whether the tuple counts, and whether the library decoded it as the references.</param>
    /// <param name="Message">The reason of a tuple that does not count, or the failure.</param>
    /// <param name="Tools">The coding tools the library met in the stream.</param>
    /// <param name="Features">Properties of the stream read from its headers by the library (a diagnostic for the report).</param>
    private sealed record TupleResult(StreamTuple Tuple, Outcome Outcome, string? Message, Av1Tools Tools, IReadOnlyList<string> Features);

    [Fact]
    public async Task StreamsDecodeAsDav1dAndLibaom()
    {
        var ffmpeg = await RequiredTools.FFmpegAsync();
        var output = TestContext.Current.TestOutputHelper;
        var tuples = CreateTuples(out var minimumEffective, out var campaign);
        output?.WriteLine(campaign);

        var artifacts = InteropSettings.GetArtifactsDirectory(nameof(Av1DifferentialInteropTests));
        var results = new ConcurrentBag<TupleResult>();
        var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount / 2, 1, 8), CancellationToken = XunitCancellationToken };
        await Parallel.ForEachAsync(tuples, options, async (tuple, cancellationToken) => results.Add(await RunAsync(ffmpeg, tuple, artifacts, cancellationToken)));

        var ordered = results.OrderBy(result => result.Tuple.Name, StringComparer.Ordinal).ToList();
        var effective = ordered.Where(result => result.Outcome is Outcome.Effective).ToList();
        var failures = ordered.Where(result => result.Outcome is Outcome.Failed).ToList();
        foreach (var result in ordered)
        {
            var status = result.Outcome switch
            {
                Outcome.Effective => "identical",
                Outcome.NotCounted => "NOT COUNTED",
                _ => "FAILED",
            };
            output?.WriteLine($"{result.Tuple.Name}: {status}: {result.Tuple.Description}{(result.Message is null ? "" : " -- " + result.Message)}{(result.Features.Count == 0 ? "" : " [" + string.Join(' ', result.Features) + "]")}");
        }

        // What the effective streams reached: the coding tools of their blocks and the properties of their headers
        output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{effective.Count} effective tuple(s) out of {ordered.Count} ({failures.Count} failed, {ordered.Count - effective.Count - failures.Count} not counted); {minimumEffective} required."));
        foreach (var tool in Enum.GetValues<Av1Tools>().Where(tool => tool is not Av1Tools.None))
        {
            output?.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  tool {tool}: {effective.Count(result => result.Tools.HasFlag(tool))} stream(s)"));
        }

        foreach (var feature in effective.SelectMany(result => result.Features).Select(feature => feature.Split('=')[0]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var values = effective.SelectMany(result => result.Features).Where(item => item.Split('=')[0] == feature).GroupBy(item => item, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal);
            output?.WriteLine("  " + string.Join(", ", values.Select(group => string.Create(CultureInfo.InvariantCulture, $"{group.Key}: {group.Count()}"))));
        }

        if (failures.Count > 0)
            Assert.Fail($"{failures.Count} stream(s) are not decoded as dav1d and libaom decode them:\n" + string.Join("\n", failures.Select(failure => $"- {failure.Tuple.Name}: {failure.Message}")));

        Assert.HasCountGreaterThanOrEqual(minimumEffective, effective, $"Only {effective.Count} tuple(s) out of {ordered.Count} were encoded and decoded identically by dav1d and libaom; at least {minimumEffective} are required. See the test output for the tuples that do not count.");
    }

    private static List<StreamTuple> CreateTuples(out int minimumEffective, out string campaign)
    {
        if (Environment.GetEnvironmentVariable(StreamsVariable) is { Length: > 0 } directory)
        {
            if (!Directory.Exists(directory))
                Assert.Fail($"{StreamsVariable} is set to '{directory}', which is not a directory.");

            var files = Directory.GetFiles(directory, "*.obu", SearchOption.AllDirectories).Order(StringComparer.Ordinal).ToList();
            if (files.Count == 0)
                Assert.Fail($"{StreamsVariable} is set to '{directory}', which has no .obu file.");

            minimumEffective = 1;
            campaign = $"{files.Count} local stream(s) of '{directory}' ({StreamsVariable}).";
            return [.. files.Select((file, index) => new StreamTuple(string.Create(CultureInfo.InvariantCulture, $"local-{index:D4}-{Path.GetFileNameWithoutExtension(file)}"), file, EncodeArguments: null, FullPath.FromPath(file)))];
        }

        var seed = ReadInteger(SeedVariable, DefaultSeed, minimum: int.MinValue);
        var cases = ReadInteger(CasesVariable, DefaultCases, minimum: 1);
        minimumEffective = ((cases * 5) + 5) / 6;
        campaign = string.Create(CultureInfo.InvariantCulture, $"Seed {seed} ({SeedVariable}), {cases} tuple(s) ({CasesVariable}).");
        return [.. Enumerable.Range(0, cases).Select(index => CreateTuple(seed, index))];
    }

    private static int ReadInteger(string variable, int defaultValue, int minimum)
    {
        var text = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(text))
            return defaultValue;

        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < minimum)
            Assert.Fail($"{variable} is set to '{text}', which is not an integer of at least {minimum}.");

        return value;
    }

    /// <summary>
    /// Derives a tuple from the seed and its index only: encoder, size, sample format, number of frames, source, quality,
    /// speed and one set of encoder options. Three tuples out of eight are encoded by SVT-AV1; the option sets of an encoder
    /// are taken in turn, so that a run of a few dozen tuples has each of them, and everything else is drawn at random.
    /// </summary>
    /// <remarks>
    /// Known limits of the pinned encoders that the tuples respect: SVT-AV1 writes an empty stream for a multi-frame encode
    /// that is given <c>lp=</c>; its super-resolution and reference scaling code frames larger than the sequence maximum
    /// when the size is not a multiple of 8, which libaom rejects, and never finish for a frame whose width is between 64
    /// and 128 pixels (observed from a height of 96 pixels); this libaom build rejects <c>superres-mode</c>, <c>resize-mode</c>,
    /// <c>sframe-dist</c> and <c>enable-fwd-kf</c> as <c>-aom-params</c>, and only uses intra block copy for an all-intra
    /// encode of screen content that is large enough.
    /// </remarks>
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    private static StreamTuple CreateTuple(int seed, int index)
    {
        // A generator per tuple: the tuple does not depend on the tuples before it
        var random = new Random(Mix(seed, index));
        var position = index % 8;
        var useSvt = position is 2 or 5 or 7;
        var ordinal = useSvt
            ? ((index / 8) * 3) + (position switch { 2 => 0, 5 => 1, _ => 2 })
            : ((index / 8) * 5) + (position switch { 0 => 0, 1 => 1, 3 => 2, 4 => 3, _ => 4 });
        var frames = random.Next(1, 11);
        var pixelFormat = useSvt ? Pick(random, SvtPixelFormats) : Pick(random, LibaomPixelFormats);
        var quality = random.Next(8, 61);
        int width;
        int height;
        string content;
        string options;
        List<string> encoder;
        if (useSvt)
        {
            width = random.Next(16, 177);
            height = random.Next(16, 145);
            var preset = random.Next(4, 11);
            var set = (ordinal + (Mix(seed, -1) % SvtOptionSets)) % SvtOptionSets;
            if (set is 5 or 6)
            {
                // Super-resolution and reference scaling: multiples of 8, a width on which the encoder finishes, and enough
                // frames for the size to change
                width = random.Next(8) == 0 ? 64 : 128 + (8 * random.Next(0, 7));
                height = 64 + (8 * random.Next(0, 11));
                frames = Math.Max(frames, 4);
            }
            else if (set is 3)
            {
                (width, height) = (Math.Max(width, 136), Math.Max(height, 136));
            }

            var parameters = set switch
            {
                0 => null, // random access: hidden frames, frames shown again
                1 => "pred-struct=1", // low delay
                2 => string.Create(CultureInfo.InvariantCulture, $"film-grain={random.Next(4, 41)}:film-grain-denoise=0"),
                3 => "tile-columns=1:tile-rows=1",
                4 => "scm=1", // screen content
                5 => random.Next(3) == 0
                    ? "superres-mode=2" // a random denominator per frame
                    : string.Create(CultureInfo.InvariantCulture, $"superres-mode=1:superres-denom={random.Next(9, 17)}:superres-kf-denom={random.Next(9, 17)}"),
                6 => random.Next(3) == 0
                    ? "resize-mode=2" // a random denominator per frame
                    : string.Create(CultureInfo.InvariantCulture, $"resize-mode=1:resize-denom={random.Next(9, 17)}:resize-kf-denom={random.Next(9, 17)}"),
                7 => string.Create(CultureInfo.InvariantCulture, $"aq-mode={random.Next(1, 3)}:enable-overlays=1"),
                8 => "enable-restoration=1:enable-cdef=1:enable-tf=0",
                _ => "fast-decode=1",
            };

            content = set is 4 ? "screen" : Pick(random, ["moving", "rotating", "noise", "mandelbrot"]);
            options = parameters ?? "default";
            encoder = ["-c:v", "libsvtav1", "-preset", preset.ToString(CultureInfo.InvariantCulture), "-crf", quality.ToString(CultureInfo.InvariantCulture)];
            if (parameters is not null)
            {
                encoder.AddRange(["-svtav1-params", parameters]);
            }
        }
        else
        {
            // Half of the sizes are not multiples of 8; a size can be odd
            width = random.Next(2) == 0 ? 16 + (8 * random.Next(0, 21)) : random.Next(16, 177);
            height = random.Next(2) == 0 ? 16 + (8 * random.Next(0, 17)) : random.Next(16, 145);
            var speed = random.Next(2, 9);
            var set = (ordinal + (Mix(seed, -2) % LibaomOptionSets)) % LibaomOptionSets;
            switch (set)
            {
                case 3:
                    // Two tile columns and two tile rows of 64-sample superblocks
                    (width, height) = (Math.Max(width, 136), Math.Max(height, 136));
                    break;

                case 12:
                    // The slowest speeds search every compound type; a small frame keeps the encode short
                    (width, height, frames, speed) = (Math.Min(width, 96), Math.Min(height, 64), random.Next(4, 7), random.Next(0, 2));
                    break;

                case 13:
                    (width, height, frames) = (176, 144, random.Next(1, 4));
                    break;
            }

            content = set switch
            {
                2 or 13 => "screen",
                12 => "rotating",
                _ => Pick(random, ["moving", "rotating", "noise", "mandelbrot"]),
            };
            List<string> extra = set switch
            {
                0 => ["-usage", "good"],
                1 => ["-usage", "allintra", .. (frames == 1 ? ["-still-picture", "1"] : Array.Empty<string>())],
                2 => ["-usage", "good", "-aom-params", "tune-content=screen"],
                3 => ["-usage", "good", "-tile-columns", "1", "-tile-rows", "1", "-aom-params", "sb-size=64"],
                4 => ["-usage", "good", "-aom-params", string.Create(CultureInfo.InvariantCulture, $"film-grain-test={random.Next(1, 17)}")],
                5 => ["-usage", "good", "-lag-in-frames", "0"],
                6 => ["-usage", "good", "-error-resilience", "default"],
                7 => ["-usage", "realtime", "-lag-in-frames", "0"],
                8 => ["-usage", "good", "-aom-params", "lossless=1"],
                9 => ["-usage", "good", "-aq-mode", random.Next(1, 4).ToString(CultureInfo.InvariantCulture), "-aom-params", "enable-qm=1"],
                10 => ["-usage", "good", "-aom-params", "sb-size=128:deltaq-mode=2:delta-lf-mode=1"],
                11 => ["-usage", "good", "-aom-params", "enable-tx64=0:reduced-tx-type-set=1:enable-rect-partitions=0"],
                12 => ["-usage", "good"],
                _ => ["-usage", "allintra", "-aom-params", "tune-content=screen"], // intra block copy
            };

            options = string.Join(' ', ["-cpu-used", speed.ToString(CultureInfo.InvariantCulture), .. extra]);
            encoder = ["-c:v", "libaom-av1", "-cpu-used", speed.ToString(CultureInfo.InvariantCulture), "-crf", (set is 8 ? 0 : quality).ToString(CultureInfo.InvariantCulture), .. extra, "-row-mt", "0"];
        }

        // "testsrc2" only has even sizes: an odd size is cropped from the next even one, in a format that is not subsampled
        var size = string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");
        var (evenWidth, evenHeight) = ((width + 1) & ~1, (height + 1) & ~1);
        var evenSize = string.Create(CultureInfo.InvariantCulture, $"{evenWidth}x{evenHeight}");
        var crop = (width, height) == (evenWidth, evenHeight) ? "" : string.Create(CultureInfo.InvariantCulture, $",format=gbrp,crop={width}:{height}:0:0");
        var noiseSeed = random.Next(1, 1000);
        var source = content switch
        {
            "moving" => $"testsrc2=size={evenSize}:rate=10{crop}",
            "rotating" => string.Create(CultureInfo.InvariantCulture, $"testsrc2=size={evenSize}:rate=10,rotate=t/3:ow={evenWidth}:oh={evenHeight}{crop}"),
            "noise" => string.Create(CultureInfo.InvariantCulture, $"testsrc2=size={evenSize}:rate=10,noise=alls=20:allf=t:all_seed={noiseSeed}{crop}"),
            "mandelbrot" => $"mandelbrot=size={size}:rate=10",
            _ => $"testsrc=size={size}:rate=10",
        };

        string[] arguments =
        [
            "-y", "-filter_threads", "1", "-f", "lavfi", "-i", source, "-frames:v", frames.ToString(CultureInfo.InvariantCulture),
            "-sws_flags", "accurate_rnd+full_chroma_int+bitexact", "-pix_fmt", pixelFormat, .. encoder, "-threads", "1", "-flags", "+bitexact", "-f", "obu",
        ];
        var name = string.Create(CultureInfo.InvariantCulture, $"seed{seed}-case{index:D4}");
        var description = string.Create(CultureInfo.InvariantCulture, $"{(useSvt ? "libsvtav1" : "libaom-av1")} {size} {pixelFormat} {frames} frame(s) {content} [{options}]");
        return new StreamTuple(name, description, arguments, LocalPath: null);

        static string Pick(Random random, string[] values) => values[random.Next(values.Length)];
    }

    /// <summary>
    /// A well-mixed non-negative number from a seed and an index (the finalizer of SplitMix64): <see cref="Random"/>
    /// instances created from consecutive seeds give correlated first values.
    /// </summary>
    private static int Mix(int seed, int index)
    {
        unchecked
        {
            var z = (((ulong)(uint)seed << 32) | (uint)index) + 0x9E3779B97F4A7C15UL;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            z ^= z >> 31;
            return (int)(z & 0x7FFFFFFF);
        }
    }

    private static async Task<TupleResult> RunAsync(FFmpegTool ffmpeg, StreamTuple tuple, FullPath artifacts, CancellationToken cancellationToken)
    {
        var work = artifacts / "work" / tuple.Name;
        Directory.CreateDirectory(work);
        try
        {
            var stream = tuple.LocalPath ?? work / "stream.obu";
            if (tuple.LocalPath is not null && TryConvertAnnexB(await File.ReadAllBytesAsync(stream, cancellationToken)) is { } converted)
            {
                // The reference decoders and the library all read the converted stream
                stream = work / "stream.obu";
                await File.WriteAllBytesAsync(stream, converted, cancellationToken);
            }

            if (tuple.EncodeArguments is not null)
            {
                // An encoder that does not finish refuses the tuple, like one that fails
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(EncodeTimeout);
                ProcessResult encoded;
                try
                {
                    encoded = await ffmpeg.RunAsync([.. tuple.EncodeArguments, stream], timeout.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    return NotCounted(tuple, $"the encoder does not finish in {EncodeTimeout.TotalSeconds} s.");
                }

                if (encoded.ExitCode != 0 || !File.Exists(stream) || new FileInfo(stream).Length == 0)
                    return NotCounted(tuple, $"the encoder refuses the tuple (exit code {encoded.ExitCode}): {LastLine(encoded.StandardError)}");
            }

            // The references: every output frame as decoded by dav1d and by libaom, in the native format of the stream
            var dav1d = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Dav1d);
            var libaom = new Av1ReferenceDecoder(ffmpeg, Av1ReferenceDecoder.Libaom);
            IReadOnlyList<(int Width, int Height, string PixelFormat, string Color)> frames;
            IReadOnlyList<byte[]> reference;
            IReadOnlyList<byte[]> second;
            try
            {
                frames = await dav1d.ProbeAsync(stream, cancellationToken);
                if (frames.Count == 0)
                    return NotCounted(tuple, "dav1d outputs no frame.");

                if (frames.Any(frame => frame.PixelFormat != frames[0].PixelFormat))
                    return NotCounted(tuple, "the sample format changes inside the stream.");

                // FFmpeg converts the frames to the range and the matrix of the first one: they are no longer the samples of
                // the stream
                if (frames.Any(frame => frame.Color != frames[0].Color))
                    return NotCounted(tuple, "the color description changes inside the stream.");

                reference = await dav1d.DecodeAsync(stream, frames[0].PixelFormat, work / "dav1d", cancellationToken);
                second = await libaom.DecodeAsync(stream, frames[0].PixelFormat, work / "libaom", cancellationToken);
            }
            catch (InvalidOperationException exception)
            {
                return NotCounted(tuple, "a reference decoder rejects the stream: " + LastLine(exception.Message));
            }

            if (reference.Count != frames.Count || second.Count != frames.Count)
                return NotCounted(tuple, $"dav1d outputs {reference.Count} frame(s) and libaom {second.Count}; ffprobe (dav1d) reports {frames.Count}.");

            var format = SampleFormat.Parse(frames[0].PixelFormat);
            if (format is null)
                return NotCounted(tuple, $"the references decode to the pixel format '{frames[0].PixelFormat}', which is not a planar AV1 sample format.");

            for (var i = 0; i < frames.Count; i++)
            {
                var length = format.GetFrameLength(frames[i].Width, frames[i].Height);
                if (reference[i].Length != length || second[i].Length != length)
                    return NotCounted(tuple, $"frame {i} ({frames[i].Width}x{frames[i].Height} {frames[i].PixelFormat}, {length} bytes): dav1d wrote {reference[i].Length} bytes and libaom {second[i].Length}.");

                if (!reference[i].AsSpan().SequenceEqual(second[i]))
                    return NotCounted(tuple, $"dav1d and libaom disagree on frame {i} (first difference at byte {reference[i].AsSpan().CommonPrefixLength(second[i])}).");
            }

            // The library
            var data = await File.ReadAllBytesAsync(stream, cancellationToken);
            var (failure, tools, features, unsupported) = DecodeAndCompare(tuple.Name, data, frames, reference, format, isLocal: tuple.LocalPath is not null, cancellationToken);
            if (failure is null)
                return new TupleResult(tuple, Outcome.Effective, Message: null, tools, features);

            if (unsupported)
                return NotCounted(tuple, failure);

            // The stream and its tuple are kept for the diagnosis
            var streamCopy = artifacts / (tuple.Name + ".obu");
            var descriptionPath = artifacts / (tuple.Name + ".txt");
            File.Copy(stream, streamCopy, overwrite: true);
            var command = tuple.EncodeArguments is null ? "(local stream)" : "ffmpeg -hide_banner -nostdin -loglevel error " + string.Join(' ', tuple.EncodeArguments.Select(Quote)) + " " + tuple.Name + ".obu";
            await File.WriteAllTextAsync(descriptionPath, $"{tuple.Name}\n{tuple.Description}\n{command}\nreference: {frames.Count} frame(s) {frames[0].PixelFormat}, first {frames[0].Width}x{frames[0].Height}\n{failure}\n", cancellationToken);
            return new TupleResult(tuple, Outcome.Failed, $"{failure} Stream: '{streamCopy}', tuple: '{descriptionPath}'.", tools, features);
        }
        finally
        {
            Directory.Delete(work, recursive: true);
        }

        static TupleResult NotCounted(StreamTuple tuple, string reason) => new(tuple, Outcome.NotCounted, reason, Av1Tools.None, []);

        static string LastLine(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault() ?? "";

        // Enough for a POSIX shell: the arguments have no single quote
        static string Quote(string argument) => argument.AsSpan().IndexOfAny(" ;&|<>()$*?[\"") >= 0 ? "'" + argument + "'" : argument;
    }

    /// <summary>Decodes a stream with the library and compares every plane of every output frame with the reference bytes.</summary>
    /// <returns>
    /// The first difference, or <see langword="null"/>; the coding tools and the header properties the stream reached;
    /// whether the failure is a local stream that the library refuses as unsupported, which proves nothing.
    /// </returns>
    private static (string? Failure, Av1Tools Tools, IReadOnlyList<string> Features, bool Unsupported) DecodeAndCompare(string name, byte[] data, IReadOnlyList<(int Width, int Height, string PixelFormat, string Color)> frames, IReadOnlyList<byte[]> reference, SampleFormat format, bool isLocal, CancellationToken cancellationToken)
    {
        var features = new SortedSet<string>(StringComparer.Ordinal);

        // Conformance streams have frames of up to 65536 samples in a direction, with eight references
        var configuration = isLocal ? new ImageConfiguration { Limits = new ImageResourceLimits { MaxLiveAllocationBytes = LocalStreamAllocationLimit } } : ImageConfiguration.Default;
        var scope = AllocationScope.Create(configuration, name);
        using var decoder = new Av1Decoder(scope);
        string? failure = null;
        var unsupported = false;
        var frameIndex = 0;
        try
        {
            decoder.SetInput(data, 0, data.Length);
            while (failure is null && decoder.DecodeNextOutputFrame(cancellationToken) is { } frame)
            {
                try
                {
                    failure = frameIndex < frames.Count
                        ? CompareFrame(scope, frame, frameIndex, frames[frameIndex], reference[frameIndex], format, features)
                        : $"The library outputs more than the {frames.Count} frame(s) of dav1d and libaom.";
                }
                finally
                {
                    frame.Release();
                }

                frameIndex++;
            }
        }
        catch (UnsupportedImageFeatureException exception) when (isLocal)
        {
            failure = $"the library does not support the stream: {exception.Message}";
            unsupported = true;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            failure = $"The library throws {exception.GetType().Name} while decoding output frame {frameIndex}: {exception.Message}";
        }

        if (failure is null && frameIndex != frames.Count)
        {
            failure = $"The library outputs {frameIndex} frame(s); dav1d and libaom output {frames.Count}.";
        }

        return (failure, decoder.Tools, [.. features], unsupported);
    }

    /// <summary>
    /// Converts a stream from the length-delimited format of Annex B of the AV1 specification (a size before every
    /// temporal unit, frame unit and OBU) to the low overhead format, where every OBU carries its own size.
    /// </summary>
    /// <returns>
    /// The converted stream, or <see langword="null"/> when the data are not an Annex B stream: the nested sizes must
    /// cover the file exactly, and the first OBU must be a temporal delimiter.
    /// </returns>
    private static byte[]? TryConvertAnnexB(ReadOnlySpan<byte> data)
    {
        var output = new List<byte>(data.Length + 64);
        var position = 0;
        var first = true;
        while (position < data.Length)
        {
            if (!TryReadLeb128(data, ref position, out var temporalUnitSize) || temporalUnitSize > data.Length - position)
                return null;

            var temporalUnitEnd = position + temporalUnitSize;
            while (position < temporalUnitEnd)
            {
                if (!TryReadLeb128(data, ref position, out var frameUnitSize) || frameUnitSize > temporalUnitEnd - position)
                    return null;

                var frameUnitEnd = position + frameUnitSize;
                while (position < frameUnitEnd)
                {
                    if (!TryReadLeb128(data, ref position, out var obuLength) || obuLength == 0 || obuLength > frameUnitEnd - position)
                        return null;

                    var obu = data.Slice(position, obuLength);
                    position += obuLength;
                    var header = obu[0];
                    var headerLength = (header & 0x04) != 0 ? 2 : 1;
                    if ((header & 0x80) != 0 || obu.Length < headerLength || (first && (header & 0x78) != (2 << 3)))
                        return null;

                    first = false;
                    var payload = obu[headerLength..];
                    if ((header & 0x02) != 0)
                    {
                        var payloadStart = 0;
                        if (!TryReadLeb128(payload, ref payloadStart, out var size) || size > payload.Length - payloadStart)
                            return null;

                        payload = payload.Slice(payloadStart, size);
                    }

                    output.Add((byte)(header | 0x02));
                    if (headerLength == 2)
                        output.Add(obu[1]);

                    var remaining = payload.Length;
                    do
                    {
                        var value = remaining & 0x7F;
                        remaining >>= 7;
                        output.Add((byte)(remaining != 0 ? value | 0x80 : value));
                    }
                    while (remaining != 0);

                    output.AddRange(payload);
                }
            }
        }

        return first ? null : [.. output];
    }

    private static bool TryReadLeb128(ReadOnlySpan<byte> data, ref int position, out int value)
    {
        value = 0;
        for (var i = 0; i < 8; i++)
        {
            if (position >= data.Length)
                return false;

            var current = data[position++];
            var bits = (long)(current & 0x7F) << (7 * i);
            if (bits > int.MaxValue - value)
                return false;

            value |= (int)bits;
            if ((current & 0x80) == 0)
                return true;
        }

        return false;
    }

    private static string? CompareFrame(AllocationScope scope, Av1FrameBuffer frame, int frameIndex, (int Width, int Height, string PixelFormat, string Color) expected, byte[] reference, SampleFormat format, SortedSet<string> features)
    {
        var sequence = frame.Sequence;
        var header = frame.Header;
        features.Add(string.Create(CultureInfo.InvariantCulture, $"format={expected.PixelFormat}"));
        features.Add(header.FilmGrain is null ? "film-grain=no" : "film-grain=yes");
        features.Add(header.UseSuperres ? "superres=yes" : "superres=no");
        features.Add(header.AllowIntrabc ? "intrabc=yes" : "intrabc=no");
        features.Add(string.Create(CultureInfo.InvariantCulture, $"cdef-bits={header.CdefBits}"));
        features.Add(header.FrameIsIntra ? "frames=intra" : "frames=inter");
        features.Add(string.Create(CultureInfo.InvariantCulture, $"tiles={header.TileCols}x{header.TileRows}"));
        features.Add(sequence.Use128x128Superblock ? "superblock=128" : "superblock=64");
        if (header.AllLossless)
        {
            features.Add("lossless=yes");
        }

        if (header.AllowScreenContentTools)
        {
            features.Add("screen-content-tools=yes");
        }

        // Every shown key frame is error resilient: only the other frames tell something
        if (header.ErrorResilientMode && !header.FrameIsIntra)
        {
            features.Add("error-resilient=yes");
        }

        if (header.SegmentationEnabled)
        {
            features.Add("segmentation=yes");
        }

        if (header.UsingQmatrix)
        {
            features.Add("qmatrix=yes");
        }

        if (header.DeltaQPresent)
        {
            features.Add("delta-q=yes");
        }

        if (header.UsesLr)
        {
            features.Add("loop-restoration=yes");
        }

        if (sequence.StillPicture)
        {
            features.Add("still-picture=yes");
        }

        if ((expected.Width, expected.Height) != (sequence.MaxFrameWidth, sequence.MaxFrameHeight))
        {
            // Reference scaling: an output frame that is smaller than the sequence
            features.Add("frame-size=scaled");
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"Output frame {frameIndex}");
        if (sequence.BitDepth != format.BitDepth || frame.PlaneCount != format.PlaneCount || (frame.PlaneCount > 1 && (sequence.SubsamplingX != format.SubsamplingX || sequence.SubsamplingY != format.SubsamplingY)))
            return $"{name}: the library decodes {sequence.BitDepth}-bit samples in {frame.PlaneCount} plane(s) (subsampling {sequence.SubsamplingX}, {sequence.SubsamplingY}); dav1d and libaom decode {expected.PixelFormat}.";

        var planes = CopyOutputPlanes(scope, frame);
        var offset = 0;
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var (width, height) = format.GetPlaneSize(plane, expected.Width, expected.Height);
            var decoded = frame.GetPlane(plane);
            if ((decoded.Width, decoded.Height) != (width, height))
                return $"{name} plane {PlaneNames[plane]}: the library decodes {decoded.Width}x{decoded.Height} samples; dav1d and libaom decode {width}x{height} (frame {expected.Width}x{expected.Height} {expected.PixelFormat}).";

            for (var i = 0; i < planes[plane].Length; i++)
            {
                var actual = planes[plane][i];
                var sample = format.BytesPerSample == 1 ? reference[offset + i] : reference[offset + (2 * i)] | (reference[offset + (2 * i) + 1] << 8);
                if (actual != sample)
                    return string.Create(CultureInfo.InvariantCulture, $"{name} ({expected.Width}x{expected.Height} {expected.PixelFormat}) plane {PlaneNames[plane]}: the sample at ({i % width}, {i / width}) is {actual}; dav1d and libaom decode {sample}. It is the first of {CountDifferences(planes[plane], reference, offset, format.BytesPerSample)} differing sample(s) of the plane.");
            }

            offset += width * height * format.BytesPerSample;
        }

        return null;
    }

    private static int CountDifferences(ushort[] plane, byte[] reference, int offset, int bytesPerSample)
    {
        var count = 0;
        for (var i = 0; i < plane.Length; i++)
        {
            var sample = bytesPerSample == 1 ? reference[offset + i] : reference[offset + (2 * i)] | (reference[offset + (2 * i) + 1] << 8);
            if (plane[i] != sample)
            {
                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// The planes of an output frame, tightly packed (plane width samples per row), with the film grain of the frame header
    /// applied by stripes of 32 luma rows, the way the AVIF decoder outputs a frame. Same logic as
    /// <c>Av1DecoderConformanceTests.CopyOutputPlanes</c> of the conformance tests.
    /// </summary>
    private static ushort[][] CopyOutputPlanes(AllocationScope scope, Av1FrameBuffer frame)
    {
        var planes = new ushort[frame.PlaneCount][];
        for (var plane = 0; plane < frame.PlaneCount; plane++)
        {
            var source = frame.GetPlane(plane);
            planes[plane] = new ushort[source.Width * source.Height];
            for (var y = 0; y < source.Height; y++)
            {
                source.GetRow(y).CopyTo(planes[plane].AsSpan(y * source.Width, source.Width));
            }
        }

        if (frame.Header.FilmGrain is not { } parameters)
            return planes;

        var luma = frame.GetPlane(0);
        using var grain = new Av1FilmGrain(scope, frame.Sequence, parameters, luma.Width, luma.Height);
        var chromaWidth = frame.PlaneCount > 1 ? frame.GetPlane(1).Width : 0;
        var chromaRowsPerStripe = Av1FilmGrain.StripeHeight >> frame.Sequence.SubsamplingY;
        for (var stripe = 0; stripe < grain.StripeCount; stripe++)
        {
            var lumaRows = planes[0].AsSpan(stripe * Av1FilmGrain.StripeHeight * luma.Width);
            Span<ushort> uRows = default;
            Span<ushort> vRows = default;
            if (frame.PlaneCount > 1)
            {
                uRows = planes[1].AsSpan(stripe * chromaRowsPerStripe * chromaWidth);
                vRows = planes[2].AsSpan(stripe * chromaRowsPerStripe * chromaWidth);
            }

            grain.ApplyStripe(frame, stripe, lumaRows, luma.Width, uRows, vRows, chromaWidth);
        }

        return planes;
    }

    /// <summary>A planar FFmpeg pixel format that holds AV1 planes without conversion.</summary>
    /// <param name="BitDepth">The bit depth of the samples.</param>
    /// <param name="PlaneCount">1 for a monochrome stream, 3 otherwise (Y, U, V; G, B, R for planar GBR, which is the bitstream order of an identity-matrix stream).</param>
    /// <param name="SubsamplingX">The horizontal subsampling of the chroma planes.</param>
    /// <param name="SubsamplingY">The vertical subsampling of the chroma planes.</param>
    private sealed record SampleFormat(int BitDepth, int PlaneCount, int SubsamplingX, int SubsamplingY)
    {
        /// <summary>1 byte per sample for 8-bit streams, 2 little-endian bytes otherwise.</summary>
        public int BytesPerSample => BitDepth == 8 ? 1 : 2;

        public static SampleFormat? Parse(string pixelFormat)
        {
            var (layout, depth) = pixelFormat switch
            {
                _ when pixelFormat.EndsWith("10le", StringComparison.Ordinal) => (pixelFormat[..^4], 10),
                _ when pixelFormat.EndsWith("12le", StringComparison.Ordinal) => (pixelFormat[..^4], 12),
                _ => (pixelFormat, 8),
            };

            return layout switch
            {
                "gray" => new SampleFormat(depth, 1, 0, 0),
                "yuv420p" => new SampleFormat(depth, 3, 1, 1),
                "yuv422p" => new SampleFormat(depth, 3, 1, 0),
                "yuv444p" or "gbrp" => new SampleFormat(depth, 3, 0, 0),
                _ => null,
            };
        }

        /// <summary>The size of a plane: a chroma plane is (size + subsampling) >> subsampling.</summary>
        public (int Width, int Height) GetPlaneSize(int plane, int width, int height)
            => plane == 0 ? (width, height) : ((width + SubsamplingX) >> SubsamplingX, (height + SubsamplingY) >> SubsamplingY);

        public int GetFrameLength(int width, int height)
        {
            var length = 0;
            for (var plane = 0; plane < PlaneCount; plane++)
            {
                var size = GetPlaneSize(plane, width, height);
                length += size.Width * size.Height * BytesPerSample;
            }

            return length;
        }
    }
}
