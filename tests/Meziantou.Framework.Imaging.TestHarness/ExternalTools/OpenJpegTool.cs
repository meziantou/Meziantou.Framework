using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The OpenJPEG command-line tools: <c>opj_compress</c> writes JPEG 2000 codestreams and files for the decoder of the
/// library, <c>opj_decompress</c> is a reference decoder (it also reads the high-throughput block coder), and
/// <c>opj_dump</c> describes the headers of a codestream.
/// </summary>
/// <remarks>
/// The pinned build has no libpng, libtiff or LittleCMS: the tools read and write Netpbm, PGX and raw files, and
/// <c>opj_decompress</c> applies no ICC profile.
/// </remarks>
public sealed partial class OpenJpegTool
{
    private OpenJpegTool(string compress, string decompress, string dump, string version)
    {
        CompressPath = compress;
        DecompressPath = decompress;
        DumpPath = dump;
        Version = version;
    }

    /// <summary>Gets the path of <c>opj_compress</c>.</summary>
    public string CompressPath { get; }

    /// <summary>Gets the path of <c>opj_decompress</c>.</summary>
    public string DecompressPath { get; }

    /// <summary>Gets the path of <c>opj_dump</c>.</summary>
    public string DumpPath { get; }

    /// <summary>Gets the version of the openjp2 library the tools report in their usage text.</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that each of them reports the openjp2 version the package declares.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run or does not report the pinned version.</exception>
    public static async Task<OpenJpegTool> LocateAsync(CancellationToken cancellationToken)
    {
        var compress = await PrebuiltTools.OpjCompress.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var decompress = await PrebuiltTools.OpjDecompress.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var dump = await PrebuiltTools.OpjDump.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var version = await ReadVersionAsync(compress, PrebuiltTools.OpjCompress.Version, cancellationToken).ConfigureAwait(false);
        await ReadVersionAsync(decompress, PrebuiltTools.OpjDecompress.Version, cancellationToken).ConfigureAwait(false);
        await ReadVersionAsync(dump, PrebuiltTools.OpjDump.Version, cancellationToken).ConfigureAwait(false);
        return new OpenJpegTool(compress, decompress, dump, version);
    }

    /// <summary>Runs <c>opj_compress -i input -o output</c> with the specified options.</summary>
    /// <param name="inputPath">The image to encode: a Netpbm file (PGM, PPM, PAM), a PGX file, or raw samples described by the <c>-F</c> option.</param>
    /// <param name="outputPath">The file to write: <c>.j2k</c> for a codestream, <c>.jp2</c> for a file.</param>
    /// <param name="arguments">The options, for example <c>-n 3 -p RPCL</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">opj_compress failed.</exception>
    public async Task EncodeAsync(FullPath inputPath, FullPath outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        File.Delete(outputPath);
        var result = await TryEncodeAsync(inputPath, outputPath, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException($"opj_compress failed on '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");
    }

    /// <summary>Runs <c>opj_compress -i input -o output</c> with the specified options, without checking that it succeeds.</summary>
    /// <param name="inputPath">The image to encode.</param>
    /// <param name="outputPath">The file to write.</param>
    /// <param name="arguments">The options.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The result of the process.</returns>
    public Task<ProcessResult> TryEncodeAsync(FullPath inputPath, FullPath outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => ProcessRunner.RunAsync(CompressPath, ["-i", inputPath, "-o", outputPath, .. arguments], cancellationToken);

    /// <summary>
    /// Decodes a codestream or a file with <c>opj_decompress</c> to PGX files: one per component, with the samples of the
    /// codestream as they are (at their own size, sign and precision, without color conversion).
    /// </summary>
    /// <param name="inputPath">The JPEG 2000 codestream or file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The components, in order.</returns>
    /// <exception cref="InvalidOperationException">opj_decompress rejected the input.</exception>
    public Task<IReadOnlyList<PgxImage>> DecodeComponentsAsync(FullPath inputPath, CancellationToken cancellationToken) => DecodeComponentsAsync(inputPath, [], cancellationToken);

    /// <summary>Decodes a codestream or a file with <c>opj_decompress</c> and options to PGX files, one per component.</summary>
    /// <param name="inputPath">The JPEG 2000 codestream or file.</param>
    /// <param name="arguments">The options, for example <c>-r 2</c> (discard two resolution levels), <c>-d 0,0,64,64</c> (decode an area) or <c>-l 1</c> (decode the first layer).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The components, in order.</returns>
    /// <exception cref="InvalidOperationException">opj_decompress rejected the input.</exception>
    public async Task<IReadOnlyList<PgxImage>> DecodeComponentsAsync(FullPath inputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var components = await TryDecodeComponentsAsync(inputPath, arguments, cancellationToken).ConfigureAwait(false);
        return components.Components ?? throw new InvalidOperationException($"opj_decompress failed on '{inputPath}' (exit code {components.Result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {components.Result.StandardError}{components.Result.StandardOutputText}");
    }

    /// <summary>Decodes a codestream or a file with <c>opj_decompress</c> to PGX files, when it accepts the input.</summary>
    /// <param name="inputPath">The JPEG 2000 codestream or file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The components, or <see langword="null"/> when opj_decompress rejected the input, with the result of the process.</returns>
    public Task<(IReadOnlyList<PgxImage>? Components, ProcessResult Result)> TryDecodeComponentsAsync(FullPath inputPath, CancellationToken cancellationToken) => TryDecodeComponentsAsync(inputPath, [], cancellationToken);

    /// <summary>Decodes a codestream or a file with <c>opj_decompress</c> and options to PGX files, when it accepts the input.</summary>
    /// <param name="inputPath">The JPEG 2000 codestream or file.</param>
    /// <param name="arguments">The options of <c>opj_decompress</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The components, or <see langword="null"/> when opj_decompress rejected the input, with the result of the process.</returns>
    public async Task<(IReadOnlyList<PgxImage>? Components, ProcessResult Result)> TryDecodeComponentsAsync(FullPath inputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        // opj_decompress writes <name>_<component>.pgx. OpenJPEG 2.5.4 overflows the buffer of that name when the output
        // path has exactly 254 characters, and aborts after writing the files: such a path is made one character longer
        var outputPrefix = inputPath.Value + ".opj";
        if ((outputPrefix + ".pgx").Length == 254)
        {
            outputPrefix += "_";
        }

        for (var i = 0; File.Exists(GetComponentPath(outputPrefix, i)); i++)
        {
            File.Delete(GetComponentPath(outputPrefix, i));
        }

        var result = await ProcessRunner.RunAsync(DecompressPath, ["-i", inputPath, "-o", outputPrefix + ".pgx", .. arguments], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(GetComponentPath(outputPrefix, 0)))
            return (null, result);

        var components = new List<PgxImage>();
        for (var i = 0; File.Exists(GetComponentPath(outputPrefix, i)); i++)
        {
            components.Add(PgxImage.Parse(await File.ReadAllBytesAsync(GetComponentPath(outputPrefix, i), cancellationToken).ConfigureAwait(false)));
        }

        return (components, result);

        static string GetComponentPath(string prefix, int component) => prefix + "_" + component.ToString(CultureInfo.InvariantCulture) + ".pgx";
    }

    /// <summary>Reads the description of the headers printed by <c>opj_dump</c>.</summary>
    /// <param name="inputPath">The JPEG 2000 codestream or file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The text.</returns>
    /// <exception cref="InvalidOperationException">opj_dump rejected the input.</exception>
    public async Task<string> DumpAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(DumpPath, ["-i", inputPath], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"opj_dump failed on '{inputPath}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result.StandardOutputText;
    }

    private static async Task<string> ReadVersionAsync(string tool, string expectedVersion, CancellationToken cancellationToken)
    {
        // "It has been compiled against openjp2 library v2.5.4."
        var result = await ProcessRunner.RunAsync(tool, ["-h"], cancellationToken).ConfigureAwait(false);
        var match = VersionRegex().Match(result.StandardOutputText + result.StandardError);
        if (!match.Success)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{tool}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        var version = match.Groups["version"].Value;
        if (version != expectedVersion)
            throw new ExternalToolUnavailableException($"'{Path.GetFileName(tool)}' reports openjp2 '{version}', which does not match the version '{expectedVersion}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        return version;
    }

    [GeneratedRegex(@"openjp2 library v(?<version>\d+\.\d+\.\d+)\b", RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();
}
