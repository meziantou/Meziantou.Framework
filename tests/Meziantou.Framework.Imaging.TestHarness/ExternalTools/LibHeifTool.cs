using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The libheif reference command-line tools, built for HEVC only: <c>heif-enc</c> (x265) writes HEIC files (still images,
/// grids, image sequences) for the decoder of the library, <c>heif-dec</c> (libde265) is the independent decoder they are
/// compared with, and <c>heif-info</c> is the independent reader of the container.
/// </summary>
/// <remarks>
/// What the tools do, and what a comparison must know (libheif 1.23.6):
/// <list type="bullet">
/// <item><description>
/// <c>heif-enc</c> pads the pictures that x265 cannot code as they are (an odd dimension, a dimension below 64) and writes
/// the size of the padded picture in the <c>ispe</c> property with a clean aperture (<c>clap</c>) that restores the size of
/// the source. It takes the <c>-b</c> most significant bits of the samples of a 16-bit PNG file. It writes the alpha plane
/// of a grid (<c>--cut-tiles</c>) as an auxiliary image of each tile, and none for the grid itself. It only uses inter
/// prediction in the sequences of 8-bit pictures. The build has no <c>sharp-yuv</c> chroma downsampling.
/// </description></item>
/// <item><description>
/// x265 4.2 never returns from the encoding of pictures of some sizes (144x112, 160x96) when the deblocking filter is
/// disabled (<c>-p x265:no-deblock=1</c>, <c>-p x265:sao-non-deblock=1</c>): every tool is stopped after
/// <see cref="Timeout"/>.
/// </description></item>
/// <item><description>
/// <c>heif-dec</c> applies the transformations of the file (<c>clap</c>, <c>irot</c>, <c>imir</c>). It writes an 8-bit PNG
/// file for an 8-bit image, and a 16-bit one whose samples are the samples of the image shifted to the most significant
/// bits otherwise (<see cref="HeifDecImage.GetPixels"/> scales them). It ignores <c>chroma_sample_loc_type</c>: subsampled
/// chroma is always upsampled as samples centered between the luma samples (<c>-C bilinear</c>), or replicated
/// (<c>-C nn</c>, its default), after the planes are cropped to the clean aperture. It writes every image of a file that
/// has several, under other names: <see cref="TryDecodeAsync"/> only reads the file of a single image.
/// </description></item>
/// <item><description>
/// libheif does not read every layout: it does not recognize a file whose file type box has a 64-bit size, it reads the
/// NAL units of an item as if their length always took four bytes, and it rejects the tracks with a <c>co64</c> box or
/// an <c>hev1</c> sample entry.
/// </description></item>
/// <item><description>
/// <c>heif-dec -S</c> follows the edit list: it writes every repetition of a sequence, and never stops on a sequence that
/// repeats forever. <see cref="DecodeSequenceAsync(FullPath, bool, CancellationToken)"/> passes <c>--ignore-editlist</c>. It writes 8-bit PNG files
/// whatever the bit depth of the images (the 8 most significant bits of their samples), and always replicates subsampled
/// chroma: <c>-C bilinear</c> has no effect on the images of a sequence.
/// </description></item>
/// </list>
/// </remarks>
public sealed partial class LibHeifTool
{
    /// <summary>The time after which a tool that has not finished is stopped: none of them needs a second for the images of the tests.</summary>
    public static readonly TimeSpan Timeout = TimeSpan.FromMinutes(2);

    private static LibHeifTool? s_located;

    private LibHeifTool(string heifEnc, string heifDec, string heifInfo, string version, IReadOnlyList<string> encoders, IReadOnlyList<string> decoders)
    {
        HeifEncPath = heifEnc;
        HeifDecPath = heifDec;
        HeifInfoPath = heifInfo;
        Version = version;
        Encoders = encoders;
        Decoders = decoders;
    }

    /// <summary>Gets the path of <c>heif-enc</c>.</summary>
    public string HeifEncPath { get; }

    /// <summary>Gets the path of <c>heif-dec</c>.</summary>
    public string HeifDecPath { get; }

    /// <summary>Gets the path of <c>heif-info</c>.</summary>
    public string HeifInfoPath { get; }

    /// <summary>Gets the libheif version the three tools report (<c>--version</c>).</summary>
    public string Version { get; }

    /// <summary>
    /// Gets the encoders <c>heif-enc --list-encoders</c> prints, as <c>format: description</c> (for example
    /// <c>HEIC: x265 = x265 HEVC encoder (4.2+1-e444744) [default]</c>).
    /// </summary>
    public IReadOnlyList<string> Encoders { get; }

    /// <summary>
    /// Gets the decoders <c>heif-dec --list-decoders</c> prints, as <c>format: description</c> (for example
    /// <c>HEIC: libde265 = libde265 HEVC decoder, version 1.1.3</c>).
    /// </summary>
    public IReadOnlyList<string> Decoders { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that the three of them report the libheif version the package declares. The tools are
    /// located once per process.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run or does not report the pinned version.</exception>
    public static async Task<LibHeifTool> LocateAsync(CancellationToken cancellationToken)
    {
        if (Volatile.Read(ref s_located) is { } located)
            return located;

        var heifEnc = await PrebuiltTools.HeifEnc.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var heifDec = await PrebuiltTools.HeifDec.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var heifInfo = await PrebuiltTools.HeifInfo.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var version = await ReadVersionAsync(heifEnc, PrebuiltTools.HeifEnc.Version, cancellationToken).ConfigureAwait(false);
        var decoderVersion = await ReadVersionAsync(heifDec, PrebuiltTools.HeifDec.Version, cancellationToken).ConfigureAwait(false);
        var infoVersion = await ReadVersionAsync(heifInfo, PrebuiltTools.HeifInfo.Version, cancellationToken).ConfigureAwait(false);
        if (version != decoderVersion || version != infoVersion)
            throw new ExternalToolUnavailableException($"heif-enc reports libheif {version}, heif-dec reports libheif {decoderVersion} and heif-info reports libheif {infoVersion}.");

        var encoders = await ReadCodecsAsync(heifEnc, "--list-encoders", cancellationToken).ConfigureAwait(false);
        var decoders = await ReadCodecsAsync(heifDec, "--list-decoders", cancellationToken).ConfigureAwait(false);
        var tool = new LibHeifTool(heifEnc, heifDec, heifInfo, version, encoders, decoders);
        Volatile.Write(ref s_located, tool);
        return tool;
    }

    /// <summary>Runs <c>heif-enc</c>.</summary>
    /// <param name="arguments">The options and the input file, in order; the output is given separately.</param>
    /// <param name="outputPath">The HEIC file to write.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>What heif-enc printed.</returns>
    /// <exception cref="InvalidOperationException">heif-enc failed or wrote no file.</exception>
    public async Task<string> EncodeAsync(IEnumerable<string> arguments, FullPath outputPath, CancellationToken cancellationToken)
    {
        File.Delete(outputPath);
        var result = await RunCheckedAsync(HeifEncPath, [.. arguments, "-o", outputPath], cancellationToken).ConfigureAwait(false);
        if (!File.Exists(outputPath))
            throw new InvalidOperationException($"heif-enc wrote no file: {result.StandardError}{result.StandardOutputText}");

        return result.StandardOutputText + result.StandardError;
    }

    /// <summary>Reads the description printed by <c>heif-info</c>.</summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed description.</returns>
    /// <exception cref="InvalidOperationException">heif-info rejected the file.</exception>
    public async Task<HeifInfo> GetInfoAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(HeifInfoPath, [inputPath], cancellationToken).ConfigureAwait(false);
        return HeifInfo.Parse(result.StandardOutputText);
    }

    /// <summary>Reads the dump of the boxes of a file printed by <c>heif-info -d</c>.</summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The dump.</returns>
    /// <exception cref="InvalidOperationException">heif-info rejected the file.</exception>
    public async Task<string> DumpBoxesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(HeifInfoPath, ["-d", inputPath], cancellationToken).ConfigureAwait(false);
        return result.StandardOutputText;
    }

    /// <summary>
    /// Decodes the primary image of a file with <c>heif-dec</c> to a PNG file. heif-dec applies the transformations of the
    /// file (<c>clap</c>, <c>irot</c>, <c>imir</c>).
    /// </summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="bilinearChroma">Whether subsampled chroma is interpolated (<c>-C bilinear</c>) instead of replicated (<c>-C nn</c>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidOperationException">heif-dec rejected the file.</exception>
    public async Task<HeifDecImage> DecodeAsync(FullPath inputPath, bool bilinearChroma, CancellationToken cancellationToken)
    {
        var result = await TryDecodeAsync(inputPath, bilinearChroma, cancellationToken).ConfigureAwait(false);
        return result.Image ?? throw new InvalidOperationException($"heif-dec failed on '{inputPath}': {result.Error}");
    }

    /// <summary>Decodes the primary image of a file with <c>heif-dec</c>, or reports why heif-dec does not decode it.</summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="bilinearChroma">Whether subsampled chroma is interpolated (<c>-C bilinear</c>) instead of replicated (<c>-C nn</c>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image, or what heif-dec printed when it failed.</returns>
    public async Task<(HeifDecImage? Image, string? Error)> TryDecodeAsync(FullPath inputPath, bool bilinearChroma, CancellationToken cancellationToken)
    {
        var output = FullPath.FromPath(inputPath + (bilinearChroma ? ".heif-dec-bilinear.png" : ".heif-dec-nn.png"));
        File.Delete(output);
        var result = await RunAsync(HeifDecPath, ["--quiet", "-C", bilinearChroma ? "bilinear" : "nn", inputPath, output], cancellationToken).ConfigureAwait(false);

        // heif-dec reports some failures with the exit code 0 and no output file
        if (result.ExitCode != 0 || !File.Exists(output))
            return (null, string.Create(CultureInfo.InvariantCulture, $"exit code {result.ExitCode}: {result.StandardError}{result.StandardOutputText}").Trim());

        return (HeifDecImage.ReadPng(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false)), null);
    }

    /// <summary>
    /// Decodes the auxiliary images of the primary image of a file (depth images, mattes, gain maps; not the alpha plane)
    /// with <c>heif-dec --with-aux</c>, which writes each of them to a PNG file named after its auxiliary type.
    /// </summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The images, by the name heif-dec gives their file (the primary image is not included).</returns>
    /// <exception cref="InvalidOperationException">heif-dec rejected the file.</exception>
    public async Task<IReadOnlyDictionary<string, HeifDecImage>> DecodeAuxiliaryImagesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var directory = FullPath.FromPath(inputPath + ".heif-dec-aux");
        if (Directory.Exists(directory))
            Directory.Delete(directory, recursive: true);

        Directory.CreateDirectory(directory);
        await RunCheckedAsync(HeifDecPath, ["--quiet", "--with-aux", "--no-colons", inputPath, directory / "image.png"], cancellationToken).ConfigureAwait(false);
        var result = new Dictionary<string, HeifDecImage>(StringComparer.Ordinal);
        foreach (var file in Directory.EnumerateFiles(directory, "*.png").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(file);
            if (name is not "image.png")
                result.Add(name, HeifDecImage.ReadPng(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false)));
        }

        return result;
    }

    /// <summary>
    /// Decodes the primary image of a file with <c>heif-dec</c> to a YUV4MPEG2 file, which holds planes instead of pixels.
    /// heif-dec always writes 8-bit 4:2:0 planes: they are the decoded planes of an 8-bit 4:2:0 image, after its
    /// transformations, and a conversion of the image otherwise.
    /// </summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The planes.</returns>
    /// <exception cref="InvalidOperationException">heif-dec rejected the file.</exception>
    public async Task<HeifDecPlanes> DecodePlanesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = FullPath.FromPath(inputPath + ".heif-dec.y4m");
        File.Delete(output);
        var result = await RunCheckedAsync(HeifDecPath, ["--quiet", inputPath, output], cancellationToken).ConfigureAwait(false);
        if (!File.Exists(output))
            throw new InvalidOperationException($"heif-dec wrote no file for '{inputPath}': {result.StandardError}{result.StandardOutputText}");

        return HeifDecPlanes.ReadY4m(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Decodes every image of an image sequence, once, in presentation order, with <c>heif-dec -S --ignore-editlist</c>.
    /// </summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="bilinearChroma">Whether subsampled chroma is interpolated (<c>-C bilinear</c>) instead of replicated (<c>-C nn</c>).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded images with the duration heif-dec prints for each of them, in units of the time scale of the track.</returns>
    /// <exception cref="InvalidOperationException">heif-dec rejected the file.</exception>
    public Task<IReadOnlyList<(HeifDecImage Image, long Duration)>> DecodeSequenceAsync(FullPath inputPath, bool bilinearChroma, CancellationToken cancellationToken)
        => DecodeSequenceAsync(inputPath, bilinearChroma, followEditList: false, cancellationToken);

    /// <summary>
    /// Decodes the images of an image sequence in presentation order with <c>heif-dec -S</c>: once
    /// (<c>--ignore-editlist</c>), or as many times as the edit list of the track plays them.
    /// </summary>
    /// <param name="inputPath">The HEIF file.</param>
    /// <param name="bilinearChroma">Whether subsampled chroma is interpolated (<c>-C bilinear</c>) instead of replicated (<c>-C nn</c>).</param>
    /// <param name="followEditList">
    /// Whether heif-dec follows the edit list: it then writes every repetition of the sequence. Never for a sequence that
    /// repeats forever, which heif-dec writes until it is stopped (<see cref="Timeout"/>).
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded images with the duration heif-dec prints for each of them, in units of the time scale of the track.</returns>
    /// <exception cref="InvalidOperationException">heif-dec rejected the file.</exception>
    public async Task<IReadOnlyList<(HeifDecImage Image, long Duration)>> DecodeSequenceAsync(FullPath inputPath, bool bilinearChroma, bool followEditList, CancellationToken cancellationToken)
    {
        // heif-dec appends "-<index from 1>" to the name of the output, before its extension
        var folder = FullPath.FromPath(inputPath + (followEditList ? ".heif-dec-sequence-edits" : ".heif-dec-sequence"));
        if (Directory.Exists(folder))
        {
            Directory.Delete(folder, recursive: true);
        }

        Directory.CreateDirectory(folder);
        string[] editList = followEditList ? [] : ["--ignore-editlist"];
        var result = await RunCheckedAsync(HeifDecPath, ["-S", .. editList, "-C", bilinearChroma ? "bilinear" : "nn", inputPath, folder / "frame.png"], cancellationToken).ConfigureAwait(false);

        // "sample duration 40" before each "Written to ...frame-1.png"
        var durations = SampleDurationRegex().Matches(result.StandardOutputText + result.StandardError).Select(match => long.Parse(match.Groups["duration"].Value, CultureInfo.InvariantCulture)).ToList();
        var count = Directory.GetFiles(folder, "frame-*.png").Length;
        if (count == 0 || durations.Count != count)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"heif-dec wrote {count} image(s) and printed {durations.Count} duration(s) for '{inputPath}': {result.StandardError}{result.StandardOutputText}"));

        var frames = new List<(HeifDecImage Image, long Duration)>();
        for (var i = 0; i < count; i++)
        {
            var file = folder / string.Create(CultureInfo.InvariantCulture, $"frame-{i + 1}.png");
            if (!File.Exists(file))
                throw new InvalidOperationException($"heif-dec did not write '{file}'.");

            frames.Add((HeifDecImage.ReadPng(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false)), durations[i]));
        }

        return frames;
    }

    private static async Task<string> ReadVersionAsync(string tool, string expectedVersion, CancellationToken cancellationToken)
    {
        // "1.23.6", then "libheif: 1.23.6" and the path of the plugins
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(tool, ["--version"], cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalToolUnavailableException($"'{tool}' cannot be started: {ex.Message}", ex);
        }

        var match = VersionRegex().Match(result.StandardOutputText);
        if (result.ExitCode != 0 || !match.Success)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{tool}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        var version = match.Groups["version"].Value;
        if (version != expectedVersion)
            throw new ExternalToolUnavailableException($"'{Path.GetFileName(tool)}' reports libheif '{version}', which does not match the version '{expectedVersion}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        return version;
    }

    private static async Task<IReadOnlyList<string>> ReadCodecsAsync(string tool, string option, CancellationToken cancellationToken)
    {
        // "HEIC encoders:" then "- x265 = x265 HEVC encoder (4.2+1-e444744) [default]"; a format without codec has no line
        var result = await RunCheckedAsync(tool, [option], cancellationToken).ConfigureAwait(false);
        var codecs = new List<string>();
        var format = "";
        foreach (var line in result.StandardOutputText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("- ", StringComparison.Ordinal))
            {
                codecs.Add($"{format}: {line[2..]}");
            }
            else if (line.EndsWith(':', StringComparison.Ordinal))
            {
                // "HEIC encoders:", "HEIC decoders:" or, for the uncompressed decoder, "uncompressed:"
                format = line[..^1].Replace(" encoders", "", StringComparison.Ordinal).Replace(" decoders", "", StringComparison.Ordinal);
            }
        }

        return codecs;
    }

    private static async Task<ProcessResult> RunCheckedAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var list = arguments.ToList();
        var result = await RunAsync(tool, list, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', list)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result;
    }

    // A tool that does not finish must not block a test until the test host is stopped
    private static async Task<ProcessResult> RunAsync(string tool, IReadOnlyList<string> arguments, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            return await ProcessRunner.RunAsync(tool, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
        {
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', arguments)}' did not finish in {Timeout.TotalSeconds.ToString(CultureInfo.InvariantCulture)} seconds.", exception);
        }
    }

    [GeneratedRegex(@"^libheif: (?<version>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"sample duration (?<duration>\d+)", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex SampleDurationRegex();
}
