using System.Diagnostics;
using System.Security.Cryptography;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Corpus;

/// <summary>
/// Every file of the external corpora is read: its pages, their content and text, its annotations and outline, then it
/// is written and read back. A file may only throw the documented exceptions, within bounded time and memory; the
/// exceptions are recorded in <c>expectations/</c>, so that a change of behavior fails the test.
/// </summary>
public sealed class PdfCorpusTests
{
    private const int MaxPages = 50;
    private const int MaxRoundTripLength = 8 * 1024 * 1024;
    private static readonly TimeSpan MaxDuration = TimeSpan.FromSeconds(60);

    private static readonly PdfReadOptions Options = new()
    {
        MaxDecodedStreamLength = 64 * 1024 * 1024,
    };

    public static TheoryData<string> PdfJs => GetFiles("pdfjs");

    public static TheoryData<string> Pdfium => GetFiles("pdfium");

    public static TheoryData<string> Qpdf => GetFiles("qpdf");

    public static TheoryData<string> Pdf20Examples => GetFiles("pdf20examples");

    public static TheoryData<string> PdfBox => GetFiles("pdfbox");

    public static TheoryData<string> BilevelImageFiles => new(CorpusFiles.Suites.SelectMany(CorpusFiles.GetFiles).Where(id => HasBilevelFilter(CorpusFiles.Read(id))));

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PdfJs), DisableDiscoveryEnumeration = true)]
    public void PdfJsFiles(string id) => Run(id);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Pdfium), DisableDiscoveryEnumeration = true)]
    public void PdfiumFiles(string id) => Run(id);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Qpdf), DisableDiscoveryEnumeration = true)]
    public void QpdfFiles(string id) => Run(id);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(Pdf20Examples), DisableDiscoveryEnumeration = true)]
    public void Pdf20ExampleFiles(string id) => Run(id);

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PdfBox), DisableDiscoveryEnumeration = true)]
    public void PdfBoxFiles(string id) => Run(id);

    /// <summary>
    /// The images of the <c>CCITTFaxDecode</c> and <c>JBIG2Decode</c> filters decode to the data recorded in
    /// <c>expectations/bilevel-images.txt</c> (a digest, or the exception): data that was checked pixel by pixel against
    /// the images MuPDF extracts and the pages Poppler and PDFium render.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(BilevelImageFiles), DisableDiscoveryEnumeration = true)]
    public void BilevelImages(string id)
    {
        PdfDocument document;
        try
        {
            document = PdfDocument.Open(CorpusFiles.Read(id), Options);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
        {
            // The files that cannot be opened are checked by the other tests
            return;
        }

        foreach (var reference in document.Objects.GetReferences().ToList())
        {
            if (reference.Resolve() is not PdfStream stream || !stream.Filters.Any(filter => filter.Value is "CCITTFaxDecode" or "JBIG2Decode"))
                continue;

            string outcome;
            string? details = null;
            try
            {
                outcome = Convert.ToHexStringLower(SHA256.HashData(stream.GetDecodedData()).AsSpan(0, 16));
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
            {
                outcome = ex.GetType().Name;
                details = ex.ToString();
            }

            CorpusExpectations.Check("bilevel-images/" + id + "#" + reference.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome, details);
        }
    }

    [Fact]
    public void EverySuiteHasATest()
    {
        Assert.Equal(["pdfjs", "pdfium", "qpdf", "pdf20examples", "pdfbox"], CorpusFiles.Suites);
    }

    private static TheoryData<string> GetFiles(string suite) => new(CorpusFiles.GetFiles(suite));

    private static bool HasBilevelFilter(byte[] data)
    {
        var span = data.AsSpan();
        return span.IndexOf("JBIG2Decode"u8) >= 0 || span.IndexOf("CCITTFaxDecode"u8) >= 0 || span.IndexOf("/CCF"u8) >= 0;
    }

    private static void Run(string id)
    {
        var data = CorpusFiles.Read(id);
        var stopwatch = Stopwatch.StartNew();
        string? outcome = null;
        string? details = null;
        try
        {
            Read(data);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
        {
            outcome = ex.GetType().Name;
            details = ex.ToString();
        }

        Assert.True(stopwatch.Elapsed < MaxDuration, $"Reading the file took {stopwatch.Elapsed}");
        CorpusExpectations.Check(id, outcome, details);
    }

    private static void Read(byte[] data)
    {
        var document = PdfDocument.Open(data, Options);
        var pageCount = document.Pages.Count;
        foreach (var page in document.Pages.Take(MaxPages))
        {
            _ = page.MediaBox;
            _ = page.CropBox;
            _ = page.Rotation;
            _ = page.GetContentData();
            _ = page.ExtractText();
            foreach (var annotation in page.Annotations)
            {
                _ = annotation.Rectangle;
                if (annotation is PdfLinkAnnotation link)
                {
                    _ = link.Uri;
                    _ = link.Destination;
                }
            }
        }

        _ = document.Information.Title;
        _ = document.Information.CreationDate;
        CountOutline(document.Outlines);

        if (data.Length <= MaxRoundTripLength)
        {
            var saved = PdfDocument.Open(document.ToByteArray(), Options);
            Assert.False(saved.IsRepaired);
            Assert.Equal(pageCount, saved.Pages.Count);
        }
    }

    private static int CountOutline(PdfOutlineCollection items)
    {
        var count = items.Count;
        foreach (var item in items)
        {
            _ = item.Destination;
            count += CountOutline(item.Children);
        }

        return count;
    }
}
