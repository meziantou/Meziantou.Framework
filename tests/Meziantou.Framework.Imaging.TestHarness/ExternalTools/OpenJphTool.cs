using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The OpenJPH command-line tools: <c>ojph_compress</c> writes JPEG 2000 codestreams that use the high-throughput block
/// coder (Rec. ITU-T T.814 | ISO/IEC 15444-15), which neither OpenJPEG nor FFmpeg writes, and <c>ojph_expand</c> decodes them.
/// </summary>
/// <remarks>
/// <c>ojph_compress</c> writes one coding pass per code-block, the cleanup pass: a codestream with the refinement passes of
/// the high-throughput block coder cannot come from it. The pinned build has no libtiff: the tools read and write
/// Netpbm, yuv and raw files. They print no version.
/// </remarks>
public sealed class OpenJphTool
{
    private OpenJphTool(string compress, string expand)
    {
        CompressPath = compress;
        ExpandPath = expand;
    }

    /// <summary>Gets the path of <c>ojph_compress</c>.</summary>
    public string CompressPath { get; }

    /// <summary>Gets the path of <c>ojph_expand</c>.</summary>
    public string ExpandPath { get; }

    /// <summary>Gets the OpenJPH version the package declares: the tools print none.</summary>
    public static string Version => PrebuiltTools.OjphCompress.Version;

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that they run.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run.</exception>
    public static async Task<OpenJphTool> LocateAsync(CancellationToken cancellationToken)
    {
        var compress = await PrebuiltTools.OjphCompress.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var expand = await PrebuiltTools.OjphExpand.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // Without arguments, each tool prints its usage, which names its options
        foreach (var (tool, option) in new[] { (compress, "-num_decomps"), (expand, "-skip_res") })
        {
            var result = await ProcessRunner.RunAsync(tool, [], cancellationToken).ConfigureAwait(false);
            if (!(result.StandardOutputText + result.StandardError).Contains(option, StringComparison.Ordinal))
                throw new ExternalToolUnavailableException($"'{tool}' does not run (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");
        }

        return new OpenJphTool(compress, expand);
    }

    /// <summary>Runs <c>ojph_compress -i input -o output</c> with the specified options.</summary>
    /// <param name="inputPath">The image to encode: a PGM or PPM file, or yuv or raw samples described by the options.</param>
    /// <param name="outputPath">The codestream to write.</param>
    /// <param name="arguments">The options, for example <c>-reversible true -num_decomps 2</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the codestream is written.</returns>
    /// <exception cref="InvalidOperationException">ojph_compress failed.</exception>
    public async Task EncodeAsync(FullPath inputPath, FullPath outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        File.Delete(outputPath);
        string[] all = ["-i", inputPath, "-o", outputPath, .. arguments];
        var result = await ProcessRunner.RunAsync(CompressPath, all, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"'ojph_compress {string.Join(' ', all)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");
    }

    /// <summary>Decodes a codestream with <c>ojph_expand</c> to a Netpbm file: a PGM file for one component, a PPM file for three.</summary>
    /// <param name="inputPath">The codestream.</param>
    /// <param name="componentCount">The number of components of the codestream: 1 or 3.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidOperationException">ojph_expand rejected the codestream.</exception>
    public async Task<ReferencePnm> DecodeAsync(FullPath inputPath, int componentCount, CancellationToken cancellationToken)
    {
        var output = inputPath + (componentCount == 1 ? ".ojph.pgm" : ".ojph.ppm");
        File.Delete(output);
        var result = await ProcessRunner.RunAsync(ExpandPath, ["-i", inputPath, "-o", output], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"ojph_expand failed on '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return ReferencePnm.Parse(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }
}
