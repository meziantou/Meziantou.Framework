using System.Globalization;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The <c>oiiotool</c> command of OpenImageIO (its TIFF reader is libtiff), which prints the samples of the pixels of an
/// image (<c>--info --dumpdata</c>).
/// </summary>
/// <remarks>
/// The samples are read as stored: <c>oiio:UnassociatedAlpha</c> keeps an unassociated alpha as it is (OpenImageIO
/// premultiplies the colors otherwise) and <c>oiio:RawColor</c> keeps CMYK inks (OpenImageIO converts them to RGB
/// otherwise). Integer samples are printed as integers (a bilevel sample as 0 or 255, a palette page as its 8-bit colors),
/// and floating-point samples with nine decimals.
/// </remarks>
public sealed class OpenImageIOTool
{
    private OpenImageIOTool(string path) => OiioToolPath = path;

    /// <summary>Gets the OpenImageIO version the package declares.</summary>
    public static string Version => PrebuiltTools.OiioTool.Version;

    public string OiioToolPath { get; }

    /// <summary>
    /// Downloads <c>oiiotool</c> (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it runs.
    /// </summary>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run.</exception>
    public static async Task<OpenImageIOTool> LocateAsync(CancellationToken cancellationToken)
    {
        var oiiotool = await PrebuiltTools.OiioTool.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var result = await ProcessRunner.RunAsync(oiiotool, ["--version"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new ExternalToolUnavailableException($"'{oiiotool}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        return new OpenImageIOTool(oiiotool);
    }

    /// <summary>Decodes an image: the size, the number of channels, the type of a sample and every sample, the rows from the top.</summary>
    /// <param name="path">The image.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The image.</returns>
    /// <exception cref="InvalidOperationException">OpenImageIO rejected the file.</exception>
    public async Task<OpenImageIOPixels> DumpAsync(FullPath path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(OiioToolPath, ["--iconfig", "oiio:UnassociatedAlpha", "1", "--iconfig", "oiio:RawColor", "1", "--info", "--dumpdata", path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"oiiotool failed on '{path}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}");

        // "<path> : <width> x <height>, <channels> channel, <type> tiff", then "Pixel (x, y): v v v (normalized values)"
        var lines = result.StandardOutputText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var header = lines[0][(lines[0].IndexOf(" : ", StringComparison.Ordinal) + 3)..].Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var width = int.Parse(header[0], CultureInfo.InvariantCulture);
        var height = int.Parse(header[2], CultureInfo.InvariantCulture);
        var channels = int.Parse(header[3], CultureInfo.InvariantCulture);
        var type = header[5];
        var samples = new List<double>(width * height * channels);
        foreach (var line in lines.Skip(1))
        {
            if (!line.StartsWith("Pixel (", StringComparison.Ordinal))
                continue;

            var values = line[(line.IndexOf("):", StringComparison.Ordinal) + 2)..];
            var normalized = values.IndexOf('(', StringComparison.Ordinal);
            foreach (var value in (normalized < 0 ? values : values[..normalized]).Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                samples.Add(double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture));
            }
        }

        if (samples.Count != width * height * channels)
            throw new InvalidOperationException($"oiiotool printed {samples.Count.ToString(CultureInfo.InvariantCulture)} samples for an image of {width.ToString(CultureInfo.InvariantCulture)}x{height.ToString(CultureInfo.InvariantCulture)} pixels of {channels.ToString(CultureInfo.InvariantCulture)} channels.");

        return new OpenImageIOPixels(width, height, channels, type, [.. samples]);
    }
}
