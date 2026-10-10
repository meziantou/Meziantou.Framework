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

    /// <summary>
    /// Decodes every frame of an encoded file to tightly packed raw pixels, in one pass, without rotation, scaling or
    /// frame-rate conversion, and verifies the byte count.
    /// </summary>
    /// <param name="inputPath">The encoded file.</param>
    /// <param name="pixelFormat">The explicit ffmpeg raw pixel format (<c>rgba</c>, <c>rgba64le</c>, <c>gray</c>, <c>gray16le</c>).</param>
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
    /// <param name="pixelFormat">The explicit ffmpeg raw pixel format (<c>rgba</c>, <c>rgba64le</c>, <c>gray</c>, <c>gray16le</c>).</param>
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
            "rgb24" => 3,
            _ => throw new ArgumentOutOfRangeException(nameof(pixelFormat), pixelFormat, "Unsupported raw pixel format."),
        };

        var result = await RunAsync(
            [.. (ignoreLoop ? (string[])["-ignore_loop", "1"] : []), "-noautorotate", "-i", inputPath, "-fps_mode", "passthrough", "-f", "rawvideo", "-pix_fmt", pixelFormat, "-"],
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
}
