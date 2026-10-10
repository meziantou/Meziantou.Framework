using System.Globalization;
using System.Text;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The libwebp reference command-line tools used as independent WebP decoders and container readers: <c>dwebp</c> (still
/// images: straight RGBA, raw YUV planes), <c>webpmux</c> (container: frame parameters, metadata chunks, frame extraction)
/// and <c>anim_dump</c> (animations composed by <c>WebPAnimDecoder</c>).
/// </summary>
public sealed class LibWebPTool
{
    private LibWebPTool(string dwebp, string webpmux, string animDump, string version)
    {
        DWebPPath = dwebp;
        WebPMuxPath = webpmux;
        AnimDumpPath = animDump;
        Version = version;
    }

    /// <summary>Gets the path of <c>dwebp</c>.</summary>
    public string DWebPPath { get; }

    /// <summary>Gets the path of <c>webpmux</c>.</summary>
    public string WebPMuxPath { get; }

    /// <summary>Gets the path of <c>anim_dump</c>.</summary>
    public string AnimDumpPath { get; }

    /// <summary>Gets the libwebp version reported by <c>dwebp -version</c>.</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that <c>dwebp</c> reports the libwebp version the package declares.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">The version of the tools does not match.</exception>
    public static async Task<LibWebPTool> LocateAsync(CancellationToken cancellationToken)
    {
        var dwebp = await PrebuiltTools.Dwebp.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var webpmux = await PrebuiltTools.Webpmux.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var animDump = await PrebuiltTools.WebpAnimDump.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(dwebp, ["-version"], cancellationToken).ConfigureAwait(false);
        var version = result.StandardOutputText.Split('\n')[0].Trim();
        if (result.ExitCode != 0 || version.Length == 0)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{dwebp}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}).");

        var expected = PrebuiltTools.Dwebp.Version;
        if (!version.StartsWith(expected, StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"libwebp version '{version}' does not match the version '{expected}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        return new LibWebPTool(dwebp, webpmux, animDump, version);
    }

    /// <summary>Decodes a still WebP image with <c>dwebp -pam</c> (straight RGBA, default fancy chroma upsampling).</summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The size and the RGBA pixels.</returns>
    public async Task<(int Width, int Height, byte[] Rgba)> DecodeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".dwebp.pam";
        await RunCheckedAsync(DWebPPath, ["-quiet", "-pam", inputPath, "-o", output], cancellationToken).ConfigureAwait(false);
        return ReadPam(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>Decodes a still lossy WebP image to its raw 4:2:0 planes with <c>dwebp -yuv</c> (no color conversion).</summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The Y, U and V planes, concatenated (luma <c>width x height</c>, then each chroma plane <c>ceil(width/2) x ceil(height/2)</c>).</returns>
    public async Task<byte[]> DecodeYuvAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".dwebp.yuv";
        await RunCheckedAsync(DWebPPath, ["-quiet", "-yuv", inputPath, "-o", output], cancellationToken).ConfigureAwait(false);
        return await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the container description printed by <c>webpmux -info</c>.</summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed description.</returns>
    public async Task<WebPMuxInfo> GetInfoAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(WebPMuxPath, ["-info", inputPath], cancellationToken).ConfigureAwait(false);
        return WebPMuxInfo.Parse(result.StandardOutputText);
    }

    /// <summary>Extracts a metadata chunk payload with <c>webpmux -get icc|exif|xmp</c>, or <see langword="null"/> when absent.</summary>
    /// <param name="inputPath">The file.</param>
    /// <param name="kind"><c>icc</c>, <c>exif</c> or <c>xmp</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The payload.</returns>
    public async Task<byte[]?> GetMetadataAsync(FullPath inputPath, string kind, CancellationToken cancellationToken)
    {
        var output = inputPath + "." + kind;
        var result = await ProcessRunner.RunAsync(WebPMuxPath, ["-get", kind, inputPath, "-o", output], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0 && File.Exists(output) ? await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false) : null;
    }

    /// <summary>Extracts frame <paramref name="index"/> (1-based, as <c>webpmux</c> numbers them) as a still WebP file.</summary>
    /// <param name="inputPath">The animation.</param>
    /// <param name="index">The 1-based frame number.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the extracted still image.</returns>
    public async Task<FullPath> ExtractFrameAsync(FullPath inputPath, int index, CancellationToken cancellationToken)
    {
        var output = inputPath + string.Create(CultureInfo.InvariantCulture, $".frame-{index}.webp");
        await RunCheckedAsync(WebPMuxPath, ["-get", "frame", index.ToString(CultureInfo.InvariantCulture), inputPath, "-o", output], cancellationToken).ConfigureAwait(false);
        return output;
    }

    /// <summary>Composes every displayed frame of an animation with <c>anim_dump -pam</c> (one full canvas per frame, straight RGBA).</summary>
    /// <param name="inputPath">The animation.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The canvases in display order.</returns>
    public async Task<IReadOnlyList<(int Width, int Height, byte[] Rgba)>> DumpAnimationAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var folder = inputPath + ".anim_dump";
        Directory.CreateDirectory(folder);
        await RunCheckedAsync(AnimDumpPath, ["-folder", folder, "-prefix", "frame_", "-pam", inputPath], cancellationToken).ConfigureAwait(false);
        var frames = new List<(int, int, byte[])>();
        foreach (var file in Directory.GetFiles(folder, "frame_*.pam").Order(StringComparer.Ordinal))
        {
            frames.Add(ReadPam(await File.ReadAllBytesAsync(file, cancellationToken).ConfigureAwait(false)));
        }

        return frames;
    }

    private static async Task<ProcessResult> RunCheckedAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var list = arguments.ToList();
        var result = await ProcessRunner.RunAsync(tool, list, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', list)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result;
    }

    /// <summary>Parses a P7 PAM file with the RGB_ALPHA tuple type (8-bit samples).</summary>
    private static (int Width, int Height, byte[] Rgba) ReadPam(byte[] data)
    {
        var end = data.AsSpan().IndexOf("ENDHDR\n"u8);
        if (end < 0)
            throw new InvalidDataException("The PAM header has no ENDHDR line.");

        var header = Encoding.ASCII.GetString(data, 0, end).Split('\n').Skip(1).Select(line => line.Split(' ', 2)).Where(parts => parts.Length == 2).ToDictionary(parts => parts[0], parts => parts[1], StringComparer.Ordinal);
        var width = int.Parse(header["WIDTH"], CultureInfo.InvariantCulture);
        var height = int.Parse(header["HEIGHT"], CultureInfo.InvariantCulture);
        if (header["DEPTH"] != "4" || header["MAXVAL"] != "255")
            throw new InvalidDataException("Expected an 8-bit RGB_ALPHA PAM.");

        var pixels = data.AsSpan(end + 7).ToArray();
        if (pixels.Length != width * height * 4)
            throw new InvalidDataException("The PAM pixel data has the wrong length.");

        return (width, height, pixels);
    }
}
