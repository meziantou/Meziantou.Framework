using System.Globalization;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// An independent AV1 decoder of the pinned FFmpeg build (<c>libdav1d</c> or <c>libaom-av1</c>), used as the reference for
/// the planes of raw AV1 streams (<c>.obu</c> files: low overhead bitstream format).
/// </summary>
/// <remarks>
/// AV1 decoding is normative and bit-exact, film grain synthesis included: two decoders that disagree on a stream cannot
/// both be right, so a reference is only meaningful when both give the same bytes.
/// </remarks>
public sealed class Av1ReferenceDecoder
{
    /// <summary>The name of the dav1d decoder of FFmpeg.</summary>
    public const string Dav1d = "libdav1d";

    /// <summary>The name of the libaom decoder of FFmpeg.</summary>
    public const string Libaom = "libaom-av1";

    private readonly FFmpegTool _ffmpeg;

    /// <summary>Creates a reference decoder.</summary>
    /// <param name="ffmpeg">The pinned FFmpeg build.</param>
    /// <param name="name">The FFmpeg decoder: <see cref="Dav1d"/> or <see cref="Libaom"/>.</param>
    public Av1ReferenceDecoder(FFmpegTool ffmpeg, string name)
    {
        ArgumentNullException.ThrowIfNull(ffmpeg);
        ArgumentException.ThrowIfNullOrEmpty(name);
        _ffmpeg = ffmpeg;
        Name = name;
    }

    /// <summary>Gets the name of the FFmpeg decoder.</summary>
    public string Name { get; }

    /// <summary>
    /// Reads the size, the pixel format and the color description of every output frame (shown frames and frames shown again,
    /// in bitstream order), as the decoder reports them before any conversion.
    /// </summary>
    /// <remarks>
    /// The color description is the range, the matrix, the primaries, the transfer function and the chroma location, as
    /// FFmpeg names them, separated by commas. <see cref="DecodeAsync"/> only returns the samples of the stream when the pixel
    /// format and the color description are those of the first frame: FFmpeg converts the frames that come after a change
    /// of the range or of the matrix to those of the first frame.
    /// </remarks>
    /// <param name="inputPath">The raw AV1 stream.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The frames, in output order.</returns>
    /// <exception cref="InvalidOperationException">The decoder rejected the stream.</exception>
    public async Task<IReadOnlyList<(int Width, int Height, string PixelFormat, string Color)>> ProbeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(_ffmpeg.ProbeExecutablePath, ["-hide_banner", "-v", "error", "-threads", "1", "-f", "obu", "-c:v", Name, "-select_streams", "v:0", "-show_entries", "frame=width,height,pix_fmt,color_range,color_space,color_primaries,color_transfer,chroma_location", "-of", "csv=print_section=0", inputPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.StandardError.Length > 0)
            throw new InvalidOperationException($"ffprobe ({Name}) rejects '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError.Trim()}");

        var frames = new List<(int, int, string, string)>();
        foreach (var line in result.StandardOutputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var parts = line.Split(',');
            frames.Add((int.Parse(parts[0], CultureInfo.InvariantCulture), int.Parse(parts[1], CultureInfo.InvariantCulture), parts[2], string.Join(',', parts[3..8])));
        }

        return frames;
    }

    /// <summary>
    /// Decodes every output frame in one pass on one thread, film grain applied, to raw planes in <paramref name="pixelFormat"/>,
    /// one file per frame and without scaling: a frame whose size differs from the first one keeps its size.
    /// </summary>
    /// <param name="inputPath">The raw AV1 stream.</param>
    /// <param name="pixelFormat">The pixel format of the stream, as <see cref="ProbeAsync"/> reports it: any other format makes FFmpeg convert the samples.</param>
    /// <param name="scratchDirectory">A directory for the frame files; it is emptied first and left with the frame files.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The raw planes of every output frame, in output order.</returns>
    /// <exception cref="InvalidOperationException">The decoder rejected the stream.</exception>
    public async Task<IReadOnlyList<byte[]>> DecodeAsync(FullPath inputPath, string pixelFormat, FullPath scratchDirectory, CancellationToken cancellationToken)
    {
        if (Directory.Exists(scratchDirectory))
        {
            Directory.Delete(scratchDirectory, recursive: true);
        }

        Directory.CreateDirectory(scratchDirectory);
        var result = await _ffmpeg.RunAsync(["-threads", "1", "-c:v", Name, "-f", "obu", "-i", inputPath, "-fps_mode", "passthrough", "-noautoscale", "-f", "image2", "-c:v", "rawvideo", "-pix_fmt", pixelFormat, scratchDirectory / "%05d.raw"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || result.StandardError.Length > 0)
            throw new InvalidOperationException($"{Name} rejects '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError.Trim()}");

        // Numbered from 1 with a fixed width: the ordinal order of the names is the output order
        var files = Directory.GetFiles(scratchDirectory, "*.raw").Order(StringComparer.Ordinal).ToList();
        var frames = new List<byte[]>();
        for (var i = 0; i < files.Count; i++)
        {
            var expectedName = string.Create(CultureInfo.InvariantCulture, $"{i + 1:D5}.raw");
            if (Path.GetFileName(files[i]) != expectedName)
                throw new InvalidOperationException($"{Name} wrote the unexpected frame file '{Path.GetFileName(files[i])}' ('{expectedName}' expected).");

            frames.Add(await File.ReadAllBytesAsync(files[i], cancellationToken).ConfigureAwait(false));
        }

        return frames;
    }
}
