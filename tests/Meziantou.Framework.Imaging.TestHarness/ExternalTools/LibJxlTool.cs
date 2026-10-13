using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.TestHarness.Pnm;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The libjxl reference command-line tools: <c>cjxl</c> and <c>jxl_from_tree</c> write JPEG XL files for the decoder of the
/// library, <c>djxl</c> is the reference decoder they are compared with, and <c>jxlinfo</c> describes the structure of a file.
/// </summary>
public sealed partial class LibJxlTool
{
    private LibJxlTool(string cjxl, string djxl, string jxlinfo, string jxlFromTree, string version)
    {
        CjxlPath = cjxl;
        DjxlPath = djxl;
        JxlinfoPath = jxlinfo;
        JxlFromTreePath = jxlFromTree;
        Version = version;
    }

    /// <summary>Gets the path of <c>cjxl</c>.</summary>
    public string CjxlPath { get; }

    /// <summary>Gets the path of <c>djxl</c>.</summary>
    public string DjxlPath { get; }

    /// <summary>Gets the path of <c>jxlinfo</c>.</summary>
    public string JxlinfoPath { get; }

    /// <summary>Gets the path of <c>jxl_from_tree</c>.</summary>
    public string JxlFromTreePath { get; }

    /// <summary>Gets the libjxl version <c>cjxl</c> and <c>djxl</c> report (<c>--version</c>).</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads the tools (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that <c>cjxl</c> and <c>djxl</c> report the libjxl version the package declares.
    /// <c>jxlinfo</c> and <c>jxl_from_tree</c> print no version: they are built from the same sources, and only checked to run.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool does not run or does not report the pinned version.</exception>
    public static async Task<LibJxlTool> LocateAsync(CancellationToken cancellationToken)
    {
        var cjxl = await PrebuiltTools.Cjxl.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var djxl = await PrebuiltTools.Djxl.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var jxlinfo = await PrebuiltTools.Jxlinfo.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var jxlFromTree = await PrebuiltTools.JxlFromTree.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        var encoderVersion = await ReadVersionAsync(cjxl, PrebuiltTools.Cjxl.Version, cancellationToken).ConfigureAwait(false);
        var decoderVersion = await ReadVersionAsync(djxl, PrebuiltTools.Djxl.Version, cancellationToken).ConfigureAwait(false);
        if (encoderVersion != decoderVersion)
            throw new ExternalToolUnavailableException($"cjxl reports libjxl {encoderVersion} and djxl reports libjxl {decoderVersion}.");

        // "jxlinfo -h" prints its usage and exits with 0; jxl_from_tree prints its usage and exits with 1 without arguments
        var info = await ProcessRunner.RunAsync(jxlinfo, ["-h"], cancellationToken).ConfigureAwait(false);
        if (info.ExitCode != 0 || !(info.StandardOutputText + info.StandardError).Contains("INPUT", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{jxlinfo}' does not run (exit code {info.ExitCode.ToString(CultureInfo.InvariantCulture)}): {info.StandardOutputText}{info.StandardError}");

        var tree = await ProcessRunner.RunAsync(jxlFromTree, [], cancellationToken).ConfigureAwait(false);
        if (!(tree.StandardOutputText + tree.StandardError).Contains("tree_in.txt", StringComparison.Ordinal))
            throw new ExternalToolUnavailableException($"'{jxlFromTree}' does not run (exit code {tree.ExitCode.ToString(CultureInfo.InvariantCulture)}): {tree.StandardOutputText}{tree.StandardError}");

        return new LibJxlTool(cjxl, djxl, jxlinfo, jxlFromTree, encoderVersion);
    }

    /// <summary>
    /// Runs <c>cjxl</c> without worker threads (<c>--num_threads=0</c>, so that the output does not depend on the machine).
    /// </summary>
    /// <param name="inputPath">The image to encode (PPM, PGM, PAM, PNG, APNG or JPEG).</param>
    /// <param name="outputPath">The JPEG XL file to write.</param>
    /// <param name="arguments">The options, for example <c>-d 0 -e 7</c>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">cjxl failed.</exception>
    public Task EncodeAsync(FullPath inputPath, FullPath outputPath, IEnumerable<string> arguments, CancellationToken cancellationToken)
        => RunCheckedAsync(CjxlPath, [inputPath, outputPath, .. arguments, "--quiet", "--num_threads=0"], cancellationToken);

    /// <summary>
    /// Writes a JPEG XL file with <c>jxl_from_tree</c>: the frames, their headers and the Modular tree that predicts every
    /// sample are described as text, so the pixels are defined by the description and not by an input image.
    /// </summary>
    /// <param name="tree">The description, in the syntax of jxl_from_tree.</param>
    /// <param name="outputPath">The JPEG XL file to write (a bare codestream).</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes when the file is written.</returns>
    /// <exception cref="InvalidOperationException">jxl_from_tree rejected the description.</exception>
    public async Task EncodeFromTreeAsync(string tree, FullPath outputPath, CancellationToken cancellationToken)
    {
        var treePath = outputPath + ".tree.txt";
        await File.WriteAllTextAsync(treePath, tree, cancellationToken).ConfigureAwait(false);
        File.Delete(outputPath);
        await RunCheckedAsync(JxlFromTreePath, [treePath, outputPath], cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads the description printed by <c>jxlinfo -v</c>.</summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The parsed description.</returns>
    /// <exception cref="InvalidOperationException">jxlinfo rejected the file.</exception>
    public async Task<JxlInfo> GetInfoAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var result = await RunCheckedAsync(JxlinfoPath, ["-v", inputPath], cancellationToken).ConfigureAwait(false);
        return JxlInfo.Parse(result.StandardOutputText);
    }

    /// <summary>
    /// Decodes every displayed frame with <c>djxl</c> to floating-point samples (<c>.npy</c>). The frames are coalesced: a
    /// layer is composed with the frame that follows it, and every frame has the size of the image. djxl applies the
    /// orientation of the file.
    /// </summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The frames.</returns>
    /// <exception cref="InvalidOperationException">djxl rejected the file.</exception>
    public async Task<NpyFrames> DecodeFramesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".djxl.npy";
        File.Delete(output);
        await RunCheckedAsync(DjxlPath, [inputPath, output, "--quiet", "--num_threads=0"], cancellationToken).ConfigureAwait(false);
        return NpyFrames.Parse(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Decodes a still image, or the first frame of an animation, with <c>djxl</c> to a Netpbm file (PAM) at the bit depth
    /// of the file: integer samples, with no conversion from floating point on the reading side.
    /// </summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The decoded image.</returns>
    /// <exception cref="InvalidOperationException">djxl rejected the file.</exception>
    public async Task<ReferencePnm> DecodeAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".djxl.pam";
        File.Delete(output);
        await RunCheckedAsync(DjxlPath, [inputPath, output, "--quiet", "--num_threads=0"], cancellationToken).ConfigureAwait(false);
        return ReferencePnm.Parse(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    /// <summary>
    /// Rebuilds the JPEG file a JPEG XL file was made from, with <c>djxl</c>: a JPEG output is the original file, byte for
    /// byte, when the input holds JPEG bitstream reconstruction data. Without these data djxl encodes the pixels as a new
    /// JPEG file: check <see cref="JxlInfo.HasJpegReconstruction"/> first.
    /// </summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The JPEG file.</returns>
    /// <exception cref="InvalidOperationException">djxl rejected the file.</exception>
    public async Task<byte[]> ReconstructJpegAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".djxl.jpg";
        File.Delete(output);
        await RunCheckedAsync(DjxlPath, [inputPath, output, "--quiet", "--num_threads=0"], cancellationToken).ConfigureAwait(false);
        return await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Extracts the ICC profile of the image with <c>djxl --orig_icc_out</c>: the embedded one, or the one libjxl synthesizes for an enumerated color encoding.</summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The profile.</returns>
    /// <exception cref="InvalidOperationException">djxl rejected the file.</exception>
    public async Task<byte[]> GetOriginalIccProfileAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".djxl-icc.npy";
        var profile = inputPath + ".djxl.icc";
        File.Delete(profile);
        await RunCheckedAsync(DjxlPath, [inputPath, output, "--quiet", "--num_threads=0", "--orig_icc_out=" + profile], cancellationToken).ConfigureAwait(false);
        return await File.ReadAllBytesAsync(profile, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<string> ReadVersionAsync(string tool, string expectedVersion, CancellationToken cancellationToken)
    {
        // "cjxl v0.12.0 0.12.0 [_NEON_BF16_,NEON] {AppleClang 21.0.0.21000101}"
        var result = await ProcessRunner.RunAsync(tool, ["--version"], cancellationToken).ConfigureAwait(false);
        var match = VersionRegex().Match(result.StandardOutputText + result.StandardError);
        if (result.ExitCode != 0 || !match.Success)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{tool}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        var version = match.Groups["version"].Value;
        if (version != expectedVersion)
            throw new ExternalToolUnavailableException($"'{Path.GetFileName(tool)}' reports libjxl '{version}', which does not match the version '{expectedVersion}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        return version;
    }

    private static async Task<ProcessResult> RunCheckedAsync(string tool, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var list = arguments.ToList();
        var result = await ProcessRunner.RunAsync(tool, list, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException($"'{Path.GetFileName(tool)} {string.Join(' ', list)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return result;
    }

    [GeneratedRegex(@"^[cd]jxl v(?<version>\d+\.\d+\.\d+)\b", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();
}
