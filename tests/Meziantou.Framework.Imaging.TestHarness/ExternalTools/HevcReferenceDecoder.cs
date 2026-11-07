using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// An independent HEVC decoder of the pinned tools, used as a reference for the planes of raw HEVC streams (<c>.hevc</c>
/// files: byte stream format of Rec. ITU-T H.265, Annex B): the native decoder of FFmpeg, <c>dec265</c> (libde265), or
/// <c>TAppDecoder</c> of HM, the reference software of the specification.
/// </summary>
/// <remarks>
/// <para>
/// HEVC decoding is normative and bit-exact: two decoders that disagree on a stream cannot both be right. None of the three
/// is right on every stream (see <see cref="FFmpeg"/>, <see cref="Dec265"/> and <see cref="Hm"/>): a reference is the
/// planes that two of them give, byte for byte.
/// </para>
/// <para>Every decoder writes the pictures cropped to the conformance window, in output order.</para>
/// </remarks>
public sealed partial class HevcReferenceDecoder
{
    /// <summary>
    /// The native <c>hevc</c> decoder of FFmpeg. It does not implement the implicit residual DPCM of the range extensions,
    /// and its intra prediction with <c>constrained_intra_pred_flag</c> and its chroma deblocking with picture-level chroma
    /// quantization parameter offsets differ from those of the two other decoders on some streams.
    /// </summary>
    public const string FFmpeg = "ffmpeg";

    /// <summary>
    /// <c>dec265</c>, the decoder of libde265. It applies the sample adaptive offset across the slices that disable the
    /// in-loop filters across their boundaries, and aborts on some streams.
    /// </summary>
    public const string Dec265 = "dec265";

    /// <summary>
    /// <c>TAppDecoder</c>, the decoder of HM. It checks the constraints of the profiles: it aborts on the streams that do
    /// not conform to the profile they announce.
    /// </summary>
    public const string Hm = "TAppDecoder";

    private readonly string _executablePath;
    private readonly string? _probeExecutablePath;

    private HevcReferenceDecoder(string name, string executablePath, string version, string? probeExecutablePath = null)
    {
        Name = name;
        _executablePath = executablePath;
        _probeExecutablePath = probeExecutablePath;
        Version = version;
    }

    /// <summary>Gets the name of the decoder: <see cref="FFmpeg"/>, <see cref="Dec265"/> or <see cref="Hm"/>.</summary>
    public string Name { get; }

    /// <summary>Gets the version that the decoder reports.</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads the three decoders (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that each reports the version the package declares.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoders: FFmpeg, dec265, then HM.</returns>
    /// <exception cref="ExternalToolUnavailableException">A decoder does not run or does not report the pinned version.</exception>
    public static async Task<IReadOnlyList<HevcReferenceDecoder>> LocateAllAsync(CancellationToken cancellationToken)
    {
        return
        [
            await LocateAsync(FFmpeg, cancellationToken).ConfigureAwait(false),
            await LocateAsync(Dec265, cancellationToken).ConfigureAwait(false),
            await LocateAsync(Hm, cancellationToken).ConfigureAwait(false),
        ];
    }

    /// <summary>Downloads a decoder and checks that it reports the version the <c>Meziantou.Prebuilt</c> package declares.</summary>
    /// <param name="name">The decoder: <see cref="FFmpeg"/>, <see cref="Dec265"/> or <see cref="Hm"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoder.</returns>
    /// <exception cref="ExternalToolUnavailableException">The decoder does not run or does not report the pinned version.</exception>
    public static async Task<HevcReferenceDecoder> LocateAsync(string name, CancellationToken cancellationToken)
    {
        switch (name)
        {
            case FFmpeg:
                var ffmpeg = await FFmpegTool.LocateAsync(cancellationToken).ConfigureAwait(false);
                var decoders = await ffmpeg.GetDecodersAsync(cancellationToken).ConfigureAwait(false);
                if (!decoders.Contains("hevc"))
                    throw new ExternalToolUnavailableException("The pinned FFmpeg build has no 'hevc' decoder.");

                return new HevcReferenceDecoder(FFmpeg, ffmpeg.ExecutablePath, ffmpeg.Version, ffmpeg.ProbeExecutablePath);

            case Dec265:
                var dec265 = await PrebuiltTools.Dec265.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

                // dec265 has no version option: without input it prints its banner and its usage
                var dec265Version = await ReadVersionAsync(dec265, [], Dec265VersionRegex(), PrebuiltTools.Dec265.Version, cancellationToken).ConfigureAwait(false);
                return new HevcReferenceDecoder(Dec265, dec265, dec265Version);

            case Hm:
                var hm = await PrebuiltTools.HmDecoder.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
                var hmVersion = await ReadVersionAsync(hm, ["--help"], HmVersionRegex(), PrebuiltTools.HmDecoder.Version, cancellationToken).ConfigureAwait(false);
                return new HevcReferenceDecoder(Hm, hm, hmVersion);

            default:
                throw new ArgumentException($"Unknown HEVC reference decoder '{name}'.", nameof(name));
        }
    }

    /// <summary>
    /// Decodes every picture of a stream on one thread to raw planes: the samples of the stream, one byte per sample of 8
    /// bits and two bytes otherwise (least significant byte first), the planes of a picture one after the other, the
    /// pictures in output order.
    /// </summary>
    /// <param name="inputPath">The raw HEVC stream (Annex B).</param>
    /// <param name="scratchDirectory">A directory for the output file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// The planes, or the reason why the decoder gives none (it rejected the stream, crashed or did not finish), with what
    /// the decoder printed on its standard output.
    /// </returns>
    public async Task<HevcReferenceDecoderResult> DecodeAsync(FullPath inputPath, FullPath scratchDirectory, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(scratchDirectory);
        var output = scratchDirectory / (Name + ".yuv");
        File.Delete(output);
        string[] arguments;
        if (Name is FFmpeg)
        {
            // The pixel format of the stream, as the decoder reports it: any other format makes FFmpeg convert the samples
            var probe = await ProcessRunner.RunAsync(_probeExecutablePath!, ["-hide_banner", "-v", "error", "-threads", "1", "-c:v", "hevc", "-select_streams", "v:0", "-show_entries", "stream=pix_fmt", "-of", "csv=print_section=0", inputPath], cancellationToken).ConfigureAwait(false);
            var pixelFormat = probe.StandardOutputText.Trim();
            if (probe.ExitCode != 0 || pixelFormat.Length is 0 || pixelFormat.Contains('\n', StringComparison.Ordinal))
                return HevcReferenceDecoderResult.Failure($"ffprobe does not report the pixel format of the stream: {Truncate(probe.StandardError)}");

            arguments = ["-hide_banner", "-v", "error", "-y", "-threads", "1", "-c:v", "hevc", "-i", inputPath, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, output];
        }
        else if (Name is Dec265)
        {
            arguments = ["-q", "-t", "0", "-o", output, inputPath];
        }
        else
        {
            arguments = ["-b", inputPath, "-o", output, "-d", "0"];
        }

        // A decoder that loops on a stream it cannot decode must not block the test
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(_executablePath, arguments, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return HevcReferenceDecoderResult.Failure($"{Name} did not finish in 2 minutes.");
        }

        var standardOutput = result.StandardOutputText;
        if (result.ExitCode != 0)
            return HevcReferenceDecoderResult.Failure(string.Create(CultureInfo.InvariantCulture, $"{Name} failed (exit code {result.ExitCode}): {Truncate(result.StandardError)}"), standardOutput);

        // FFmpeg reports the errors of a stream it conceals on its standard error, and still exits with 0
        if (Name is FFmpeg && result.StandardError.Length > 0)
            return HevcReferenceDecoderResult.Failure($"ffmpeg reports errors: {Truncate(result.StandardError)}", standardOutput);

        if (!File.Exists(output))
            return HevcReferenceDecoderResult.Failure($"{Name} wrote no output.", standardOutput);

        var planes = await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
        File.Delete(output);
        return planes.Length is 0 ? HevcReferenceDecoderResult.Failure($"{Name} wrote an empty output.", standardOutput) : new HevcReferenceDecoderResult(planes, Error: null) { StandardOutput = standardOutput };
    }

    private static string Truncate(string text)
    {
        text = text.Trim();
        return text.Length <= 300 ? text : text[..300] + "...";
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

    [GeneratedRegex(@"dec265\s+v(?<version>[0-9.]+)", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex Dec265VersionRegex();

    [GeneratedRegex(@"Decoder Version \[(?<version>[0-9.]+)\]", RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex HmVersionRegex();
}
