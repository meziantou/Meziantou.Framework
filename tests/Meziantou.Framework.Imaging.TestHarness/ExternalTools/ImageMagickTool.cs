using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The <c>magick</c> command of ImageMagick (Q16 HDRI, with its own TIFF reader on top of libtiff), which writes the samples
/// of an image as raw bytes (<c>rgba:-</c>, <c>cmyk:-</c>, ...).
/// </summary>
/// <remarks>
/// ImageMagick 7.1.2-35 misreads two kinds of pages of separate planes (<c>PlanarConfiguration = 2</c>): a gray page with
/// alpha gives the alpha plane as its gray samples and no alpha, and a page of one sample ignores <c>WhiteIsZero</c> and the
/// color map (it reads them right when the same page is chunky).
/// </remarks>
public sealed class ImageMagickTool
{
    private ImageMagickTool(string path) => MagickPath = path;

    /// <summary>Gets the ImageMagick version the package declares.</summary>
    public static string Version => PrebuiltTools.Magick.Version;

    public string MagickPath { get; }

    /// <summary>
    /// Downloads <c>magick</c> (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it runs.
    /// </summary>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run.</exception>
    public static async Task<ImageMagickTool> LocateAsync(CancellationToken cancellationToken)
    {
        var magick = await PrebuiltTools.Magick.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(magick, ["-version"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !result.StandardOutputText.Contains("ImageMagick", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{magick}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new ImageMagickTool(magick);
    }

    /// <summary>
    /// Decodes an image and writes its samples as raw little-endian bytes, the rows from the top: <c>magick file -depth
    /// N -endian LSB map:-</c>.
    /// </summary>
    /// <param name="path">The image.</param>
    /// <param name="map">The raw format of the samples: <c>gray</c>, <c>graya</c>, <c>rgb</c>, <c>rgba</c> or <c>cmyk</c>.</param>
    /// <param name="depth">The bits of a sample: 8, 16, or 32 for floating-point samples.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The samples.</returns>
    /// <exception cref="InvalidOperationException">ImageMagick rejected the file.</exception>
    public async Task<byte[]> ExportSamplesAsync(FullPath path, string map, int depth, CancellationToken cancellationToken)
    {
        List<string> arguments = [path];
        if (depth == 32)
        {
            arguments.AddRange(["-define", "quantum:format=floating-point"]);
        }

        arguments.AddRange(["-depth", depth.ToString(CultureInfo.InvariantCulture), "-endian", "LSB", map + ":-"]);
        var result = await ProcessRunner.RunAsync(MagickPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"magick failed on '{path}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return result.StandardOutput.ToArray();
    }
}
