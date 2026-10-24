using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>
/// The Poppler tools: <c>pdftotext</c> extracts the text of a file, <c>pdfinfo</c> describes it, <c>pdffonts</c> lists its
/// fonts and whether they are embedded, and <c>pdftoppm</c> renders its pages.
/// </summary>
public sealed class PopplerTool
{
    private PopplerTool(string pdftotext, string pdfinfo, string pdffonts, string pdftoppm)
    {
        PdftotextPath = pdftotext;
        PdfinfoPath = pdfinfo;
        PdffontsPath = pdffonts;
        PdftoppmPath = pdftoppm;
    }

    /// <summary>Gets the path of <c>pdftotext</c>.</summary>
    public string PdftotextPath { get; }

    /// <summary>Gets the path of <c>pdfinfo</c>.</summary>
    public string PdfinfoPath { get; }

    /// <summary>Gets the path of <c>pdffonts</c>.</summary>
    public string PdffontsPath { get; }

    /// <summary>Gets the path of <c>pdftoppm</c>.</summary>
    public string PdftoppmPath { get; }

    /// <summary>Downloads the tools and checks their versions.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tools.</returns>
    /// <exception cref="ExternalToolUnavailableException">A tool is not available.</exception>
    public static async Task<PopplerTool> LocateAsync(CancellationToken cancellationToken)
    {
        var pdftotext = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Pdftotext, ["-v"], cancellationToken).ConfigureAwait(false);
        var pdfinfo = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Pdfinfo, ["-v"], cancellationToken).ConfigureAwait(false);
        var pdffonts = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Pdffonts, ["-v"], cancellationToken).ConfigureAwait(false);
        var pdftoppm = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.Pdftoppm, ["-v"], cancellationToken).ConfigureAwait(false);
        return new PopplerTool(pdftotext, pdfinfo, pdffonts, pdftoppm);
    }

    /// <summary>Extracts the text of a file with <c>pdftotext -enc UTF-8</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The text: the pages are separated by form feeds.</returns>
    /// <exception cref="InvalidOperationException">pdftotext failed.</exception>
    public async Task<string> ExtractTextAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(PdftotextPath, ["-enc", "UTF-8", "-nopgbrk", path, "-"], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"pdftotext failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        return result.StandardOutputText;
    }

    /// <summary>Describes a file with <c>pdfinfo</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The fields of the description (<c>Pages</c>, <c>Title</c>, <c>PDF version</c>, ...).</returns>
    /// <exception cref="InvalidOperationException">pdfinfo failed.</exception>
    public async Task<IReadOnlyDictionary<string, string>> GetInfoAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(PdfinfoPath, ["-enc", "UTF-8", path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"pdfinfo failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        var fields = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in result.StandardOutputText.Split('\n'))
        {
            var separator = line.IndexOf(':', StringComparison.Ordinal);
            if (separator > 0)
            {
                fields[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }
        }

        return fields;
    }

    /// <summary>Lists the fonts of a file with <c>pdffonts</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The lines of the table, without its header.</returns>
    /// <exception cref="InvalidOperationException">pdffonts failed.</exception>
    public async Task<IReadOnlyList<string>> GetFontsAsync(string path, CancellationToken cancellationToken)
    {
        var result = await ProcessRunner.RunAsync(PdffontsPath, [path], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"pdffonts failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        return [.. result.StandardOutputText.Split('\n').Skip(2).Select(line => line.TrimEnd('\r')).Where(line => line.Length > 0)];
    }

    /// <summary>Renders a page to a PNG file with <c>pdftoppm</c>.</summary>
    /// <param name="path">The file.</param>
    /// <param name="page">The page number, from 1.</param>
    /// <param name="dpi">The resolution.</param>
    /// <param name="outputPrefix">The path of the image without extension.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the PNG file.</returns>
    /// <exception cref="InvalidOperationException">pdftoppm failed.</exception>
    public async Task<string> RenderPageAsync(string path, int page, int dpi, string outputPrefix, CancellationToken cancellationToken)
    {
        var pageText = page.ToString(CultureInfo.InvariantCulture);
        var result = await ProcessRunner.RunAsync(PdftoppmPath, ["-png", "-singlefile", "-r", dpi.ToString(CultureInfo.InvariantCulture), "-f", pageText, "-l", pageText, path, outputPrefix], cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture, $"pdftoppm failed on '{path}' (exit code {result.ExitCode}): {result.StandardError}"));

        return outputPrefix + ".png";
    }
}
