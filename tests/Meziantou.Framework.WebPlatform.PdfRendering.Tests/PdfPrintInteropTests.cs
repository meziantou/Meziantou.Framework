using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;
using Meziantou.Framework.WebPlatform.Imaging.Tests;

namespace Meziantou.Framework.WebPlatform.PdfRendering.Tests;

/// <summary>
/// External oracles of the printed PDF documents: qpdf checks the files, Poppler extracts their text, and veraPDF validates
/// the PDF/UA-1 and PDF/A-2a documents (the tools are downloaded by the Pdf test harness).
/// </summary>
public sealed class PdfPrintInteropTests : IDisposable
{
    private readonly TestPages _pages = new();

    public void Dispose() => _pages.Dispose();

    [Theory]
    [InlineData("ua1")]
    [InlineData("2a")]
    public async Task AccessibleDocumentConformsToItsLevel(string flavour)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var verapdf = await VeraPdfTool.LocateAsync(cancellationToken);
        var path = await WriteAsync("accessible-" + flavour, new PdfPrintOptions { PdfUAConformance = PdfUAConformance.PdfUA1, PdfAConformance = PdfAConformance.PdfA2A }, cancellationToken);

        var failures = await verapdf.ValidateAsync(path, flavour, cancellationToken);

        Assert.True(failures.Count == 0, $"veraPDF {VeraPdfTool.Version} {flavour}:\n" + string.Join('\n', failures));
    }

    [Fact]
    public async Task DocumentPassesTheCheckOfQpdf()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var qpdf = await QpdfTool.LocateAsync(cancellationToken);
        var path = await WriteAsync("qpdf", PdfPrintOptions.Default, cancellationToken);

        var result = await qpdf.CheckAsync(path, cancellationToken);

        Assert.True(result.ExitCode == 0, result.StandardOutputText + result.StandardError);
        Assert.Contains("No syntax or stream encoding errors found", result.StandardOutputText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TextIsExtractedByPoppler()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var poppler = await PopplerTool.LocateAsync(cancellationToken);
        var path = await WriteAsync("poppler", PdfPrintOptions.Default, cancellationToken);

        var text = await poppler.ExtractTextAsync(path, cancellationToken);

        Assert.Contains("An accessible document", text, StringComparison.Ordinal);
        Assert.Contains("office hours", text, StringComparison.Ordinal);
    }

    /// <summary>Prints a document with headings, a paragraph with a link, a list, a table and an image, and writes the PDF file.</summary>
    private async Task<string> WriteAsync(string name, PdfPrintOptions options, CancellationToken cancellationToken)
    {
        _pages.Resources["/noto.ttf"] = (File.ReadAllBytes(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf"), "font/ttf");
        _pages.Resources["/a.png"] = (TestPages.CreatePng(2, 1, new Rgba32(255, 0, 0, 255), new Rgba32(0, 0, 255, 128)), "image/png");
        var context = _pages.Load("""
            <html lang=en>
            <title>An accessible document</title>
            <style>
              @font-face { font-family: Noto; src: url(/noto.ttf) }
              @page { size: 400px 500px; margin: 20px }
              body { font: 12px Noto }
              th, td { border: 1px solid black }
            </style>
            <h1>An accessible document</h1>
            <p>Some text with a <a href='https://example.org/'>link</a>, and the office hours.</p>
            <ul><li>First item</li><li>Second item</li></ul>
            <table><thead><tr><th>Name</th><th>Value</th></tr></thead><tbody><tr><td>A</td><td>1</td></tr></tbody></table>
            <img src='/a.png' alt='Two dots' width=40 height=20>
            <h2 style='box-shadow: 2px 2px 4px gray'>A section</h2>
            <p style='opacity: 0.6'>Translucent text</p>
            </html>
            """);

        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "print-" + name + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, context.PrintToPdf(options: options).ToByteArray(), cancellationToken);
        return path;
    }
}
