using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// An independent HEVC encoder of the pinned tools, which writes raw HEVC streams (byte stream format of Rec. ITU-T H.265,
/// Annex B) for the decoder of the library: <c>x265</c>, <c>kvazaar</c>, or <c>TAppEncoder</c> of HM, the reference
/// software of the specification.
/// </summary>
/// <remarks>
/// The options of an encoder are requests, not guarantees: what a stream has is read from the stream. An encoder also
/// writes streams that do not conform to the specification for some combinations of options (see <see cref="X265"/> and
/// <see cref="Kvazaar"/>).
/// </remarks>
public sealed partial class HevcReferenceEncoder
{
    /// <summary>
    /// <c>x265</c>: every chroma format at 8, 10 and 12 bits. With several slices and a picture of few rows of coding tree
    /// blocks, it writes every row in the first slice and leaves the other slices empty; the CRC of its decoded picture
    /// hash is not the one of the specification for the chroma planes.
    /// </summary>
    public const string X265 = "x265";

    /// <summary>
    /// <c>kvazaar</c>: tiles, wavefront and dependent slice segments, at 8 bits in 4:2:0 and 4:0:0. A slice segment header
    /// has the entry points of the rows that follow the slice segment; with a slice per tile and a slice segment per row,
    /// the addresses of the slice segments are wrong when a tile is narrower than the picture.
    /// </summary>
    public const string Kvazaar = "kvazaar";

    /// <summary><c>TAppEncoder</c> of HM: PCM, the tools of the range extensions, and every structure of slices and tiles.</summary>
    public const string Hm = "TAppEncoder";

    private readonly string _executablePath;

    private HevcReferenceEncoder(string name, string executablePath, string version)
    {
        Name = name;
        _executablePath = executablePath;
        Version = version;
    }

    /// <summary>Gets the name of the encoder: <see cref="X265"/>, <see cref="Kvazaar"/> or <see cref="Hm"/>.</summary>
    public string Name { get; }

    /// <summary>Gets the version that the encoder reports.</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads an encoder (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it reports the version the package declares.
    /// </summary>
    /// <param name="name">The encoder: <see cref="X265"/>, <see cref="Kvazaar"/> or <see cref="Hm"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The encoder.</returns>
    /// <exception cref="ExternalToolUnavailableException">The encoder does not run or does not report the pinned version.</exception>
    public static async Task<HevcReferenceEncoder> LocateAsync(string name, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case X265:
                var x265 = await PrebuiltTools.X265.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
                return new HevcReferenceEncoder(X265, x265, await ReadVersionAsync(x265, ["--version"], X265VersionRegex(), PrebuiltTools.X265.Version, cancellationToken).ConfigureAwait(false));

            case Kvazaar:
                var kvazaar = await PrebuiltTools.Kvazaar.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
                return new HevcReferenceEncoder(Kvazaar, kvazaar, await ReadVersionAsync(kvazaar, ["--version"], KvazaarVersionRegex(), PrebuiltTools.Kvazaar.Version, cancellationToken).ConfigureAwait(false));

            case Hm:
                var hm = await PrebuiltTools.HmEncoder.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
                return new HevcReferenceEncoder(Hm, hm, await ReadVersionAsync(hm, ["--help"], HmVersionRegex(), PrebuiltTools.HmEncoder.Version, cancellationToken).ConfigureAwait(false));

            default:
                throw new ArgumentException($"Unknown HEVC encoder '{name}'.", nameof(name));
        }
    }

    /// <summary>
    /// Gets the arguments that encode raw planar pictures: the input, its description, the output, the settings that make
    /// the encoder use one thread and write intra pictures only with an MD5 decoded picture hash, then
    /// <paramref name="options"/>, which may change these settings.
    /// </summary>
    /// <param name="inputPath">The raw pictures: the planes of each picture one after the other, one byte per sample of 8 bits and two bytes otherwise (least significant byte first).</param>
    /// <param name="outputPath">The raw HEVC stream to write.</param>
    /// <param name="width">The width of the pictures, in luma samples.</param>
    /// <param name="height">The height of the pictures, in luma samples.</param>
    /// <param name="chromaFormatIdc">The chroma format: 0 for monochrome, 1 for 4:2:0, 2 for 4:2:2, 3 for 4:4:4.</param>
    /// <param name="bitDepth">The bit depth of the samples: 8, 10 or 12.</param>
    /// <param name="frameCount">The number of pictures.</param>
    /// <param name="options">The options of the encoder, as its command line takes them.</param>
    /// <returns>The arguments.</returns>
    public IReadOnlyList<string> GetArguments(FullPath inputPath, FullPath outputPath, int width, int height, int chromaFormatIdc, int bitDepth, int frameCount, IEnumerable<string> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var size = string.Create(CultureInfo.InvariantCulture, $"{width}x{height}");
        var depth = bitDepth.ToString(CultureInfo.InvariantCulture);
        var frames = frameCount.ToString(CultureInfo.InvariantCulture);
        var chroma = chromaFormatIdc switch
        {
            0 => "400",
            1 => "420",
            2 => "422",
            _ => "444",
        };
        if (Name is X265)
        {
            return
            [
                "--input", inputPath, "--input-res", size, "--fps", "1", "--input-depth", depth, "--output-depth", depth, "--input-csp", "i" + chroma, "--frames", frames,
                "--output", outputPath, "--log-level", "error", "--no-progress", "--no-info", "--pools", "none", "--frame-threads", "1", "--no-wpp", "--keyint", "1", "--hash", "1",
                .. options,
            ];
        }

        if (Name is Kvazaar)
        {
            if (bitDepth is not 8 || chromaFormatIdc > 1)
                throw new ArgumentException("kvazaar encodes 8-bit 4:2:0 and 4:0:0 pictures.", nameof(chromaFormatIdc));

            return
            [
                "--input", inputPath, "--input-res", size, "--input-format", "P" + chroma, "--frames", frames, "--output", outputPath,
                "--period", "1", "--threads", "0", "--owf", "0", "--no-wpp", "--no-psnr", "--no-info", "--hash", "md5",
                .. options,
            ];
        }

        // The 4:4:4 16 intra profile allows every tool of the range extensions, whatever the format of the sequence
        return
        [
            "--InputFile=" + inputPath, "--BitstreamFile=" + outputPath, "--SourceWidth=" + width.ToString(CultureInfo.InvariantCulture), "--SourceHeight=" + height.ToString(CultureInfo.InvariantCulture),
            "--FrameRate=1", "--FramesToBeEncoded=" + frames, "--InputBitDepth=" + depth, "--InternalBitDepth=" + depth, "--InputChromaFormat=" + chroma, "--ChromaFormatIDC=" + chroma,
            "--Profile=main-RExt", "--MaxBitDepthConstraint=16", "--MaxChromaFormatConstraint=444", "--IntraConstraintFlag=1", "--Level=6.2",
            "--IntraPeriod=1", "--GOPSize=1", "--DecodingRefreshType=0", "--MaxCUWidth=64", "--MaxCUHeight=64", "--MaxPartitionDepth=4",
            "--QuadtreeTULog2MaxSize=5", "--QuadtreeTULog2MinSize=2", "--QuadtreeTUMaxDepthIntra=3", "--QuadtreeTUMaxDepthInter=3",
            "--QP=30", "--SEIDecodedPictureHash=1", "--ConformanceWindowMode=1", "--SAO=1", "--TransformSkip=1", "--TransformSkipFast=1", "--SignHideFlag=1", "--RDOQ=1", "--RDOQTS=1",
            .. options,
        ];
    }

    /// <summary>Runs the encoder.</summary>
    /// <param name="arguments">The arguments of <see cref="GetArguments"/>.</param>
    /// <param name="timeout">The time after which the encoder is stopped.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The reason why the encoder wrote no stream, or <see langword="null"/>.</returns>
    public async Task<string?> EncodeAsync(IReadOnlyList<string> arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            var result = await ProcessRunner.RunAsync(_executablePath, arguments, timeoutSource.Token).ConfigureAwait(false);
            if (result.ExitCode != 0)
            {
                var message = (result.StandardError + "\n" + result.StandardOutputText).Trim();
                var lines = message.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(line => line.Contains("rror", StringComparison.Ordinal)).Take(2).ToList();
                return string.Create(CultureInfo.InvariantCulture, $"{Name} failed (exit code {result.ExitCode}): {(lines.Count > 0 ? string.Join(" | ", lines) : message.Length <= 200 ? message : message[^200..])}");
            }

            return null;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{Name} did not finish in {timeout.TotalSeconds} s.");
        }
    }

    private static async Task<string> ReadVersionAsync(string executablePath, string[] arguments, Regex pattern, string expectedVersion, CancellationToken cancellationToken)
    {
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(executablePath, arguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalToolUnavailableException($"'{executablePath}' cannot be started: {ex.Message}", ex);
        }

        var match = pattern.Match(result.StandardOutputText + "\n" + result.StandardError);
        if (!match.Success)
            throw new ExternalToolUnavailableException($"'{executablePath}' does not report its version.");

        var version = match.Groups["version"].Value;
        if (version != expectedVersion)
            throw new ExternalToolUnavailableException($"'{executablePath}' reports the version {version}; the pinned version is {expectedVersion}.");

        return version;
    }

    [GeneratedRegex(@"HEVC encoder version (?<version>[0-9.]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex X265VersionRegex();

    [GeneratedRegex(@"kvazaar (?<version>[0-9.]+)", RegexOptions.IgnoreCase | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex KvazaarVersionRegex();

    [GeneratedRegex(@"Encoder Version \[(?<version>[0-9.]+)\]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HmVersionRegex();
}
