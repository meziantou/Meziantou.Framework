using System.Globalization;
using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Pdf.TestHarness.ExternalTools;

/// <summary>PDFium's <c>pdfium_test</c>: it renders the pages of a file to PNG images, the reference of the rendering tests.</summary>
public sealed class PdfiumTool
{
    private PdfiumTool(string path) => PdfiumTestPath = path;

    /// <summary>Gets the path of <c>pdfium_test</c>.</summary>
    public string PdfiumTestPath { get; }

    /// <summary>Downloads the tool.</summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The tool.</returns>
    /// <exception cref="ExternalToolUnavailableException">The tool is not available.</exception>
    public static async Task<PdfiumTool> LocateAsync(CancellationToken cancellationToken)
    {
        // pdfium_test has no version option: the release of meziantou/prebuilt pins it
        var path = await PrebuiltToolLocator.LocateAsync(PrebuiltTools.PdfiumTest, versionArguments: null, cancellationToken).ConfigureAwait(false);
        return new PdfiumTool(path);
    }

    /// <summary>Renders a page to a PNG file with <c>pdfium_test --png</c>.</summary>
    /// <param name="data">The file.</param>
    /// <param name="page">The page index, from 0.</param>
    /// <param name="scale">The scale: 1 renders one pixel per point.</param>
    /// <param name="directory">The directory the file and the image are written to.</param>
    /// <param name="password">The password of an encrypted file, or <see langword="null"/>.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>The path of the PNG file, or <see langword="null"/> when PDFium does not render the page.</returns>
    public async Task<string?> RenderPageAsync(byte[] data, int page, double scale, string directory, string? password, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".pdf");
        await File.WriteAllBytesAsync(path, data, cancellationToken).ConfigureAwait(false);

        // The tool writes the image next to the file it is given
        string[] arguments = ["--png", "--scale=" + scale.ToString(CultureInfo.InvariantCulture), "--pages=" + page.ToString(CultureInfo.InvariantCulture), .. password is null ? Array.Empty<string>() : ["--password=" + password], path];
        await ProcessRunner.RunAsync(PdfiumTestPath, arguments, cancellationToken).ConfigureAwait(false);
        var output = path + "." + page.ToString(CultureInfo.InvariantCulture) + ".png";
        return File.Exists(output) ? output : null;
    }
}
