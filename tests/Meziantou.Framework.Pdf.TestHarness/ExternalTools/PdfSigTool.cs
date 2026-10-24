using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>Poppler's <c>pdfsig</c>: it checks the signatures of a file. The release of meziantou/prebuilt has no build of it for Windows.</summary>
public sealed class PdfSigTool
{
    private PdfSigTool(string path) => Path = path;

    /// <summary>Gets the path of the executable.</summary>
    public string Path { get; }

    /// <summary>Gets a value indicating whether the tool is built for the current machine.</summary>
    public static bool IsSupported => PrebuiltTools.Pdfsig.IsSupported(PrebuiltTools.CurrentRuntimeIdentifier);

    /// <summary>Downloads the tool and checks its version.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<PdfSigTool> LocateAsync(CancellationToken cancellationToken) => new(await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Pdfsig, ["-v"], cancellationToken).ConfigureAwait(false));

    /// <summary>Checks the signatures of a file.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The report of <c>pdfsig</c>: one block per signature, with its validation.</returns>
    /// <exception cref="InvalidOperationException">pdfsig failed.</exception>
    public async Task<string> VerifyAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(Path, [path], cancellationToken).ConfigureAwait(false);
        var output = result.StandardOutputText;
        if (!output.Contains("Signature #1", StringComparison.Ordinal))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"pdfsig failed on '{path}' (exit code {result.ExitCode}): {output}{result.StandardError}"));

        return output;
    }
}
