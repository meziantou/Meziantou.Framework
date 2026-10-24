using System.Globalization;
using System.Text.RegularExpressions;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>Downloads a tool of the release pinned by the <c>Meziantou.Prebuilt</c> package and checks the version it reports.</summary>
internal static partial class PrebuiltToolLocator
{
    /// <summary>Downloads a tool (cached and checksum-verified) and checks that its version output contains the version the package declares.</summary>
    /// <param name="tool">The tool.</param>
    /// <param name="versionArguments">The arguments that print the version, or <see langword="null"/> to skip the check.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the executable.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool cannot be downloaded, does not run, or reports another version.</exception>
    public static async Task<string> LocateAsync(PrebuiltTool tool, string[]? versionArguments, CancellationToken cancellationToken)
    {
        string path;
        try
        {
            path = await tool.GetOrDownloadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalToolUnavailableException($"'{tool.Name}' cannot be downloaded from meziantou/prebuilt {PrebuiltTools.ReleaseVersion}: {ex.Message}", ex);
        }

        if (versionArguments is null)
            return path;

        ProcessResult result;
        try
        {
            result = await ProcessRunner.RunAsync(path, versionArguments, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new ExternalToolUnavailableException($"'{tool.Name}' does not run: {ex.Message}", ex);
        }

        var output = result.StandardOutputText + result.StandardError;
        var expected = tool.Version.Split('+')[0];
        if (!output.Contains(expected, StringComparison.Ordinal))
            throw new ExternalToolUnavailableException(string.Create(CultureInfo.InvariantCulture, $"'{tool.Name}' does not report the version '{expected}' declared by meziantou/prebuilt {PrebuiltTools.ReleaseVersion} (exit code {result.ExitCode}): {FirstLine(output)}"));

        return path;
    }

    private static string FirstLine(string text) => NewLineRegex().Split(text.Trim(), 2)[0];

    [GeneratedRegex(@"\r?\n", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 1000)]
    private static partial Regex NewLineRegex();
}
