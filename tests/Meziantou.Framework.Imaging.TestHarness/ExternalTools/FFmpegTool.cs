using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>A located, version-checked ffmpeg executable used as an independent reference decoder.</summary>
/// <remarks>
/// Follow the corpus safeguards (tests/Meziantou.Framework.Imaging.Fixtures/README.md): always request explicit raw pixel formats, disable automatic
/// rotation, use one decoding pass with <c>-fps_mode passthrough</c> for animations, verify byte counts, and never treat
/// ffmpeg timestamps as the oracle for exact frame durations or loop counts.
/// </remarks>
public sealed partial class FFmpegTool
{
    private FFmpegTool(string path, string version, string probePath)
    {
        ExecutablePath = path;
        Version = version;
        ProbeExecutablePath = probePath;
    }

    /// <summary>Gets the path of the executable.</summary>
    public string ExecutablePath { get; }

    /// <summary>Gets the path of the companion <c>ffprobe</c> executable, from the same release.</summary>
    public string ProbeExecutablePath { get; }

    /// <summary>Gets the FFmpeg version the release declares (e.g. <c>9.0.2</c>).</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads ffmpeg and ffprobe (the static build of the meziantou/prebuilt release pinned by the
    /// <c>Meziantou.Prebuilt</c> package, cached and checksum-verified) and checks that ffmpeg runs.
    /// </summary>
    /// <remarks>
    /// The package version pins the binaries. Their own <c>-version</c> output cannot confirm the FFmpeg version: the build is
    /// compiled from a GitHub tag archive, which has no <c>VERSION</c> file, so it reports the commit of the meziantou/prebuilt
    /// checkout it was built in. <see cref="Version"/> is therefore the version the package declares.
    /// </remarks>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">ffmpeg does not run.</exception>
    public static async Task<FFmpegTool> LocateAsync(CancellationToken cancellationToken)
    {
        var path = await PrebuiltTools.Ffmpeg.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var probePath = await PrebuiltTools.Ffprobe.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(path, ["-hide_banner", "-version"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !VersionRegex().IsMatch(result.StandardOutputText))
            throw new ExternalToolUnavailableException($"'{path}' does not run as ffmpeg (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new FFmpegTool(path, PrebuiltTools.Ffmpeg.Version, probePath);
    }

    /// <summary>
    /// Runs <c>ffprobe</c> on a file and returns its JSON description of the first video stream and its decoded frames
    /// (<c>-show_streams -show_frames</c>): pixel format (color type and bit depth as decoded by FFmpeg), dimensions, frame
    /// tags (PNG text chunks FFmpeg exports) and side data (ICC profiles).
    /// </summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed JSON document.</returns>
    /// <exception cref="InvalidOperationException">ffprobe failed.</exception>
    public async Task<JsonDocument> ProbeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ProbeExecutablePath, ["-hide_banner", "-v", "error", "-select_streams", "v:0", "-show_streams", "-show_frames", "-of", "json", inputPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed on '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return JsonDocument.Parse(result.StandardOutput);
    }

    /// <summary>
    /// Runs <c>ffprobe</c> on a file and returns its JSON description of every stream (<c>-show_streams</c>): for an ISO base
    /// media file, the image items and the tracks, with the side data FFmpeg reads from their boxes (mastering display,
    /// content light level, display matrix).
    /// </summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed JSON document.</returns>
    /// <exception cref="InvalidOperationException">ffprobe failed.</exception>
    public async Task<JsonDocument> ProbeStreamsAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ProbeExecutablePath, ["-hide_banner", "-v", "error", "-show_streams", "-of", "json", inputPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe failed on '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return JsonDocument.Parse(result.StandardOutput);
    }

    /// <summary>Runs ffmpeg with the specified arguments (<c>-hide_banner -nostdin -loglevel error</c> are prepended).</summary>
    /// <param name="arguments">The arguments.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The result; the exit code is not checked.</returns>
    public Task<ProcessResult> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ProcessRunner.RunAsync(ExecutablePath, ["-hide_banner", "-nostdin", "-loglevel", "error", .. arguments], cancellationToken);

    /// <summary>Gets the names of the pixel formats ffmpeg supports.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The pixel format names.</returns>
    public async Task<IReadOnlySet<string>> GetPixelFormatsAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-pix_fmts"], cancellationToken).ConfigureAwait(false);
        var formats = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.StandardOutputText.Split('\n'))
        {
            var match = PixelFormatLineRegex().Match(line);
            if (match.Success)
            {
                formats.Add(match.Groups["name"].Value);
            }
        }

        return formats;
    }

    /// <summary>Gets the names of the decoders ffmpeg supports.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoder names.</returns>
    public async Task<IReadOnlySet<string>> GetDecodersAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-decoders"], cancellationToken).ConfigureAwait(false);
        var decoders = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in result.StandardOutputText.Split('\n'))
        {
            var match = CodecLineRegex().Match(line);
            if (match.Success)
            {
                decoders.Add(match.Groups["name"].Value);
            }
        }

        return decoders;
    }

    /// <summary>Gets the names of the encoders ffmpeg supports.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The encoder names.</returns>
    public async Task<IReadOnlySet<string>> GetEncodersAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-encoders"], cancellationToken).ConfigureAwait(false);
        return ParseNames(result.StandardOutputText, CodecLineRegex());
    }

    /// <summary>Gets the names of the formats ffmpeg can write (muxers).</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The muxer names; a line that lists several names (<c>mov,mp4</c>) gives each of them.</returns>
    public async Task<IReadOnlySet<string>> GetMuxersAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-muxers"], cancellationToken).ConfigureAwait(false);
        return ParseNames(result.StandardOutputText, FormatLineRegex());
    }

    /// <summary>Gets the names of the formats ffmpeg can read (demuxers).</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The demuxer names; a line that lists several names (<c>mov,mp4</c>) gives each of them.</returns>
    public async Task<IReadOnlySet<string>> GetDemuxersAsync(CancellationToken cancellationToken)
    {
        var result = await RunAsync(["-demuxers"], cancellationToken).ConfigureAwait(false);
        return ParseNames(result.StandardOutputText, FormatLineRegex());
    }

    /// <summary>Gets the names of the bitstream filters ffmpeg supports.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The bitstream filter names.</returns>
    public async Task<IReadOnlySet<string>> GetBitstreamFiltersAsync(CancellationToken cancellationToken)
    {
        // "Bitstream filters:" then one name per line
        var result = await RunAsync(["-bsfs"], cancellationToken).ConfigureAwait(false);
        return result.StandardOutputText.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries).Skip(1).ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// Returns the headers of every video stream of a file as parsed by the <c>trace_headers</c> bitstream filter (the
    /// packets are copied, nothing is decoded): one line per syntax element, for example <c>show_existing_frame</c> or
    /// <c>show_frame</c> for AV1. FFmpeg reads an AVIF image sequence as two streams: the primary item, then the track.
    /// </summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="format">The demuxer to force (<c>obu</c> for a raw AV1 stream), or <see langword="null"/> to let ffmpeg detect it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The trace.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg failed.</exception>
    public async Task<string> TraceHeadersAsync(FullPath inputPath, string? format, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(ExecutablePath, ["-hide_banner", "-nostdin", "-loglevel", "info", .. (format is null ? [] : new[] { "-f", format }), "-i", inputPath, "-map", "0:v", "-c", "copy", "-bsf:v", "trace_headers", "-f", "null", "-"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed to trace the headers of '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return result.StandardError;
    }

    /// <summary>
    /// Decodes every frame of an encoded file to tightly packed raw pixels, in one pass, without rotation, scaling or
    /// frame-rate conversion, and verifies the byte count.
    /// </summary>
    /// <param name="inputPath">The encoded file.</param>
    /// <param name="pixelFormat">The explicit ffmpeg raw pixel format (<c>rgba</c>, <c>rgba64le</c>, <c>gray</c>, <c>gray16le</c>, <c>rgb24</c>, <c>rgb565le</c>, <c>rgb555le</c>, and the planar floats <c>gbrapf32le</c>, <c>gbrapf16le</c> and <c>grayf32le</c>).</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>One buffer per decoded frame.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg failed or produced a byte count that is not a whole number of frames.</exception>
    public Task<IReadOnlyList<byte[]>> DecodeToRawFramesAsync(FullPath inputPath, string pixelFormat, int width, int height, CancellationToken cancellationToken)
        => DecodeToRawFramesAsync(inputPath, pixelFormat, width, height, ignoreLoop: false, cancellationToken);

    /// <summary>
    /// Decodes every frame of an encoded file to tightly packed raw pixels, in one pass, without rotation, scaling or
    /// frame-rate conversion, and verifies the byte count.
    /// </summary>
    /// <param name="inputPath">The encoded file.</param>
    /// <param name="pixelFormat">The explicit ffmpeg raw pixel format (<c>rgba</c>, <c>rgba64le</c>, <c>gray</c>, <c>gray16le</c>, <c>rgb24</c>, <c>rgb565le</c>, <c>rgb555le</c>).</param>
    /// <param name="width">The expected width.</param>
    /// <param name="height">The expected height.</param>
    /// <param name="ignoreLoop">
    /// <see langword="true"/> to pass <c>-ignore_loop 1</c> to the animation demuxer (APNG, GIF), so that every frame is
    /// decoded exactly once whatever the encoded play count. Not valid for still-image inputs.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>One buffer per decoded frame.</returns>
    /// <exception cref="InvalidOperationException">ffmpeg failed or produced a byte count that is not a whole number of frames.</exception>
    public async Task<IReadOnlyList<byte[]>> DecodeToRawFramesAsync(FullPath inputPath, string pixelFormat, int width, int height, bool ignoreLoop, CancellationToken cancellationToken)
    {
        var bytesPerPixel = pixelFormat switch
        {
            "rgba" => 4,
            "rgba64le" => 8,
            "gray" => 1,
            "gray16le" => 2,
            "ya8" => 2,
            "ya16le" => 4,
            "rgb24" => 3,
            "rgb565le" => 2,
            "rgb555le" => 2,
            "rgbf32le" => 12,
            "rgbaf32le" => 16,
            "gbrapf32le" => 16,
            "gbrapf16le" => 8,
            "grayf32le" => 4,
            _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported raw pixel format."),
        };

        // A Netpbm file is a short text header followed by raw samples, and so is an uncompressed TIFF file, which the format
        // probe of ffmpeg can take for another container (a pattern with 0x47 every 188 bytes scores as MPEG-TS): the image
        // demuxer is named, and it picks the decoder from the extension
        string[] demuxer = inputPath.Extension is ".pam" or ".pgm" or ".ppm" or ".pbm" or ".pnm" or ".tif" or ".tiff" ? ["-f", "image2"] : [];
        var result = await RunAsync(
            [.. (ignoreLoop ? (string[])["-ignore_loop", "1"] : []), "-noautorotate", .. demuxer, "-i", inputPath, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"],
            cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"ffmpeg failed to decode '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        var frameLength = checked(width * height * bytesPerPixel);
        if (result.StandardOutput.Length == 0 || result.StandardOutput.Length % frameLength != 0)
            throw new InvalidOperationException($"ffmpeg produced {result.StandardOutput.Length.ToString(CultureInfo.InvariantCulture)} bytes for '{inputPath}', which is not a whole number of {width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)} {pixelFormat} frames.");

        var frames = new List<byte[]>();
        for (var offset = 0; offset < result.StandardOutput.Length; offset += frameLength)
        {
            frames.Add(result.StandardOutput.Slice(offset, frameLength).ToArray());
        }

        return frames;
    }

    [GeneratedRegex(@"^ffmpeg version (?<version>\S+)", RegexOptions.CultureInvariant | RegexOptions.Multiline, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();

    [GeneratedRegex(@"^[I.][O.][H.][P.][B.]\s+(?<name>\S+)\s+\d+\s+\d+", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex PixelFormatLineRegex();

    [GeneratedRegex(@"^\s[VAS][F.][S.][X.][B.][D.]\s+(?<name>\S+)\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex CodecLineRegex();

    [GeneratedRegex(@"^\s[D ][E ][d ]\s+(?<name>[^\s=]+)\s", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex FormatLineRegex();

    private static HashSet<string> ParseNames(string text, Regex lineRegex)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in text.Split('\n'))
        {
            var match = lineRegex.Match(line);
            if (match.Success)
            {
                names.UnionWith(match.Groups["name"].Value.Split(','));
            }
        }

        return names;
    }
}
