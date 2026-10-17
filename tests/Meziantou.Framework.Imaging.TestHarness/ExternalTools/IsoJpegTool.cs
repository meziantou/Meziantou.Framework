using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The <c>jpeg</c> tool of the ISO JPEG reference codec (thorfdbg/libjpeg): a JPEG encoder and decoder that shares no code
/// with libjpeg-turbo, with arithmetic coding, 12-bit samples, and the progressive and hierarchical processes. It is the
/// only arithmetic decoder independent of libjpeg-turbo, and an independent arithmetic encoder.
/// </summary>
/// <remarks>
/// The tool exits with 0 when a conversion fails, and leaves an existing output file as it is: every conversion deletes
/// its output first and checks afterwards that it exists and is not empty. It reads and writes PGM and PPM files (a
/// maximum value of 4095 selects 12-bit samples) and prints no version.
/// </remarks>
public sealed class IsoJpegTool
{
    private IsoJpegTool(string path) => ToolPath = path;

    /// <summary>Gets the path of the tool.</summary>
    public string ToolPath { get; }

    /// <summary>Gets the version the package declares: the tool prints none.</summary>
    public static string Version => PrebuiltTools.IsoJpeg.Version;

    /// <summary>
    /// Downloads the tool (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it runs.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run.</exception>
    public static async Task<IsoJpegTool> LocateAsync(CancellationToken cancellationToken)
    {
        var path = await PrebuiltTools.IsoJpeg.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // Without arguments, the tool prints its usage, which names its options
        var result = await ProcessRunner.RunAsync(path, [], cancellationToken).ConfigureAwait(false);
        if (!(result.StandardOutputText + result.StandardError).Contains("use arithmetic coding", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{path}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        return new IsoJpegTool(path);
    }

    /// <summary>Encodes a PGM or PPM file with the specified options.</summary>
    /// <param name="inputPath">The image to encode: a PGM or PPM file, with 12-bit samples when its maximum value is 4095.</param>
    /// <param name="outputPath">The JPEG file to write.</param>
    /// <param name="arguments">The options, for example <c>-q 85 -a -v</c>: <c>-q</c> selects the encoding.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">The tool wrote no file.</exception>
    public Task EncodeAsync(FullPath inputPath, FullPath outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ConvertAsync([.. arguments, inputPath, outputPath], outputPath, cancellationToken);

    /// <summary>Decodes a JPEG file to a Netpbm file: a PGM file for one component, a PPM file for three.</summary>
    /// <param name="inputPath">The JPEG file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image, at the precision of the file (a maximum value of 255 or 4095).</returns>
    /// <exception cref="InvalidOperationException">The tool rejected the file.</exception>
    public async Task<ReferencePnm> DecodeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".isojpeg.pnm";
        await ConvertAsync([inputPath, output], output, cancellationToken).ConfigureAwait(false);
        return ReferencePnm.Parse(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    private async Task ConvertAsync(string[] arguments, FullPath outputPath, CancellationToken cancellationToken)
    {
        File.Delete(outputPath);
        var result = await ProcessRunner.RunAsync(ToolPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPath) || new FileInfo(outputPath).Length == 0)
            throw new InvalidOperationException($"'jpeg {string.Join(' ', arguments)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");
    }
}
