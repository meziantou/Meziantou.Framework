using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>MuPDF's <c>mutool</c>: <c>mutool draw</c> renders the pages of a file.</summary>
public sealed class MutoolTool
{
    private MutoolTool(string path) => MutoolPath = path;

    /// <summary>Gets the path of <c>mutool</c>.</summary>
    public string MutoolPath { get; }

    /// <summary>Downloads the tool and checks its version.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<MutoolTool> LocateAsync(CancellationToken cancellationToken)
    {
        var path = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Mutool, ["-v"], cancellationToken).ConfigureAwait(false);
        return new MutoolTool(path);
    }

    /// <summary>Renders a page to a PNG file with <c>mutool draw</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="page">The page number, from 1.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="outputPath">The path of the PNG file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the PNG file.</returns>
    /// <exception cref="InvalidOperationException">mutool failed.</exception>
    public async Task<string> RenderPageAsync(string path, int page, int dpi, string outputPath, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(MutoolPath, ["draw", "-q", "-r", dpi.ToString(CultureInfo.InvariantCulture), "-o", outputPath, path, page.ToString(CultureInfo.InvariantCulture)], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0 || !File.Exists(outputPath))
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"mutool draw failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        return outputPath;
    }
}
