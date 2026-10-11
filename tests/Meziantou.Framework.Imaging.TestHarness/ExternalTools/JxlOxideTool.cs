using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.TestHarness.ExternalTools;

/// <summary>
/// The <c>jxl-oxide</c> command-line tool: a JPEG XL decoder that shares no code with libjxl. A decoding of the library is
/// only compared with what libjxl and jxl-oxide both return.
/// </summary>
public sealed partial class JxlOxideTool
{
    private JxlOxideTool(string path, string version)
    {
        Path = path;
        Version = version;
    }

    /// <summary>Gets the path of <c>jxl-oxide</c>.</summary>
    public string Path { get; }

    /// <summary>Gets the version the tool reports (<c>--version</c>).</summary>
    public string Version { get; }

    /// <summary>
    /// Downloads the tool (meziantou/prebuilt release pinned by the <c>Meziantou.Prebuilt</c> package, cached and
    /// checksum-verified) and checks that it reports the version the package declares.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool does not run or does not report the pinned version.</exception>
    public static async Task<JxlOxideTool> LocateAsync(CancellationToken cancellationToken)
    {
        var path = await PrebuiltTools.JxlOxide.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);

        // "jxl-oxide-cli 0.12.6"
        var result = await ProcessRunner.RunAsync(path, ["--version"], cancellationToken).ConfigureAwait(false);
        var match = VersionRegex().Match(result.StandardOutputText);
        if (result.ExitCode != 0 || !match.Success)
            throw new ExternalToolUnavailableException($"Cannot determine the version of '{path}' (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardOutputText}{result.StandardError}");

        var version = match.Groups["version"].Value;
        if (version != PrebuiltTools.JxlOxide.Version)
            throw new ExternalToolUnavailableException($"jxl-oxide reports the version '{version}', which does not match the version '{PrebuiltTools.JxlOxide.Version}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion}.");

        return new JxlOxideTool(path, version);
    }

    /// <summary>
    /// Decodes every displayed frame to floating-point samples (<c>jxl-oxide decode -f npy</c>, one worker thread). The
    /// frames are coalesced, like those of <see cref="LibJxlTool.DecodeFramesAsync"/>.
    /// </summary>
    /// <param name="inputPath">The JPEG XL file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The frames.</returns>
    /// <exception cref="InvalidOperationException">jxl-oxide rejected the file.</exception>
    public async Task<NpyFrames> DecodeFramesAsync(FullPath inputPath, CancellationToken cancellationToken)
    {
        var output = inputPath + ".jxl-oxide.npy";
        File.Delete(output);
        string[] arguments = ["decode", inputPath, "-o", output, "-f", "npy", "-j", "1"];
        var result = await ProcessRunner.RunAsync(Path, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(output))
            throw new InvalidOperationException($"'jxl-oxide {string.Join(' ', arguments)}' failed (exit code {result.ExitCode.ToString(CultureInfo.InvariantCulture)}): {result.StandardError}{result.StandardOutputText}");

        return NpyFrames.Parse(await File.ReadAllBytesAsync(output, cancellationToken).ConfigureAwait(false));
    }

    [GeneratedRegex(@"^jxl-oxide-cli (?<version>\S+)", RegexOptions.Multiline | RegexOptions.CultureInvariant | RegexOptions.ExplicitCapture, matchTimeoutMilliseconds: 1000)]
    private static partial Regex VersionRegex();
}
