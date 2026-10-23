using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.Signing;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

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

    // The pages are compared with PDFium at half the resolution of a point: one pixel per two points
    private const double RenderedDpi = 36;

    // A page matches PDFium when at most this part of its pixels differ. The update mode records the pages above it; a
    // page that is not recorded fails above twice that, so that a change of antialiasing does not change the outcome.
    private const double MaxDifferentPixels = 0.05;
    private const int MaxChannelDifference = 48;

    private static readonly PdfReadOptions Options = new()
    {
        MaxDecodedStreamLength = 64 * 1024 * 1024,
    };

    public static TheoryData<string> PdfJs => GetFiles("pdfjs");

    public static TheoryData<string> Pdfium => GetFiles("pdfium");

    public static TheoryData<string> Qpdf => GetFiles("qpdf");

    public static TheoryData<string> Pdf20Examples => GetFiles("pdf20examples");

    public static TheoryData<string> PdfBox => GetFiles("pdfbox");

    public static TheoryData<string> VeraPdf => GetFiles("verapdf");

    /// <summary>Gets the pages of the reference tests of pdf.js (the <c>eq</c> tests of its manifest): one page per file, the first one it tests, with the password of the test.</summary>
    public static TheoryData<string, int, string?> PdfJsReferencePages
    {
        get
        {
            var data = new TheoryData<string, int, string?>();
            var manifest = RepositoryPaths.External / "pdfjs" / "test" / "test_manifest.json";
            if (!File.Exists(manifest))
                return data;

            var files = CorpusFiles.GetFiles("pdfjs").ToHashSet(StringComparer.Ordinal);
            var added = new HashSet<string>(StringComparer.Ordinal);
            using var document = JsonDocument.Parse(File.ReadAllText(manifest));
            foreach (var test in document.RootElement.EnumerateArray())
            {
                // The linked tests name a file that the test suite of pdf.js downloads
                if (test.GetProperty("type").GetString() != "eq" || test.TryGetProperty("link", out _) || test.GetProperty("file").GetString() is not { } file)
                    continue;

                var id = "pdfjs/test/" + file;
                if (files.Contains(id) && added.Add(id))
                {
                    data.Add(id, test.TryGetProperty("firstPage", out var firstPage) ? firstPage.GetInt32() - 1 : 0, test.TryGetProperty("password", out var password) ? password.GetString() : null);
                }
            }

            return data;
        }
    }

    public static TheoryData<string> SignedFiles => new(CorpusFiles.Suites.SelectMany(CorpusFiles.GetFiles).Where(id => CorpusFiles.Read(id).AsSpan().IndexOf("/ByteRange"u8) >= 0));

    /// <summary>Gets the documents the signature tests of Apache PDFBox sign, with the keystore of the tests.</summary>
    public static TheoryData<string> PdfBoxSignatureSamples => new(CorpusFiles.GetFiles("pdfbox").Where(id => id.StartsWith(PdfBoxSignatureDirectory + "sign_me", StringComparison.Ordinal)));

    private static string PdfBoxSignatureDirectory => "pdfbox/examples/src/test/resources/org/apache/pdfbox/examples/signature/";

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

    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(VeraPdf), DisableDiscoveryEnumeration = true)]
    public void VeraPdfFiles(string id) => Run(id);

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
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException)
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
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException)
            {
                outcome = ex.GetType().Name;
                details = ex.ToString();
            }

            CorpusExpectations.Check("bilevel-images/" + id + "#" + reference.ObjectNumber.ToString(System.Globalization.CultureInfo.InvariantCulture), outcome, details);
        }
    }

    /// <summary>
    /// The pages of the reference tests of pdf.js are rendered without exception, and as PDFium renders them: the pages
    /// that differ are recorded in <c>expectations/rendering.txt</c>. The pixels are compared on Windows, where the
    /// expectations are recorded: the fonts that replace the fonts that are not embedded are those of the system.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PdfJsReferencePages), DisableDiscoveryEnumeration = true)]
    public async Task PdfJsRenderedPages(string id, int page, string? password)
    {
        var data = CorpusFiles.Read(id);
        var expectationId = "rendering/" + id + "#" + page.ToString(CultureInfo.InvariantCulture);
        var stopwatch = Stopwatch.StartNew();
        Image<Rgba32>? image = null;
        string? outcome = null;
        string? details = null;
        try
        {
            // The pages of a file whose page tree cannot be read are missing
            var document = PdfDocument.Open(data, new PdfReadOptions { MaxDecodedStreamLength = Options.MaxDecodedStreamLength, Password = password });
            if (page < document.Pages.Count)
            {
                image = PdfRenderer.RenderPage(document.Pages[page], new PdfRenderOptions { Dpi = RenderedDpi });
            }
            else
            {
                outcome = "MissingPage";
            }
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException)
        {
            outcome = ex.GetType().Name;
            details = ex.ToString();
        }

        Assert.True(stopwatch.Elapsed < MaxDuration, $"Rendering the page took {stopwatch.Elapsed}");
        using (image)
        {
            var difference = image is not null && OperatingSystem.IsWindows() ? await CompareWithPdfiumAsync(data, page, password, image) : null;
            var isRecordedAsDifferent = CorpusExpectations.GetExpectedOutcome(expectationId) == "Different";
            if (CorpusExpectations.UpdateMode || outcome is not null)
            {
                CorpusExpectations.Check(expectationId, outcome ?? (difference > MaxDifferentPixels || (difference is null && isRecordedAsDifferent) ? "Different" : null), details);
            }
            else if (difference is { } ratio)
            {
                if (isRecordedAsDifferent)
                {
                    Assert.True(ratio > MaxDifferentPixels / 5, $"UNEXPECTED SUCCESS: {ratio:P2} of the pixels differ from PDFium: remove '{expectationId}' from expectations/rendering.txt");
                }
                else
                {
                    Assert.True(ratio <= MaxDifferentPixels * 2, $"REGRESSION: {ratio:P2} of the pixels differ from PDFium");
                }
            }
            else if (!isRecordedAsDifferent)
            {
                // Where the pixels are not compared, the page is rendered without exception
                CorpusExpectations.Check(expectationId, outcome, details);
            }
        }
    }

    /// <summary>
    /// The signatures of the signed files of the corpora are checked: the outcome of each one (valid, or what fails) is
    /// recorded in <c>expectations/signatures.txt</c>.
    /// </summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(SignedFiles), DisableDiscoveryEnumeration = true)]
    public void Signatures(string id)
    {
        IReadOnlyList<PdfSignatureInfo> signatures;
        try
        {
            signatures = PdfSignatureVerifier.Verify(CorpusFiles.Read(id), Options);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException)
        {
            CorpusExpectations.Check("signatures/" + id, ex.GetType().Name, ex.ToString());
            return;
        }

        CorpusExpectations.Check("signatures/" + id, null, details: null);
        for (var i = 0; i < signatures.Count; i++)
        {
            var signature = signatures[i];
            var outcome = signature.Error is not null ? "Error"
                : signature.IsValid ? (signature.CoversWholeDocument ? "Valid" : "ValidRevision")
                : $"Invalid(intact={signature.IsDocumentIntact},signature={signature.IsSignatureValid},timestamp={signature.IsTimestampValid?.ToString() ?? "none"})";
            CorpusExpectations.Check("signatures/" + id + "#" + i.ToString(CultureInfo.InvariantCulture), outcome == "Valid" ? null : outcome, signature.Error);
        }
    }

    /// <summary>The documents of the signature tests of Apache PDFBox are signed with the keystore of these tests, and the signatures are checked.</summary>
    [Theory(SkipTestWithoutData = true)]
    [MemberData(nameof(PdfBoxSignatureSamples), DisableDiscoveryEnumeration = true)]
    public void PdfBoxSamplesAreSigned(string id)
    {
        using var certificate = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadPkcs12FromFile(RepositoryPaths.External / PdfBoxSignatureDirectory / "keystore.p12", "123456");
        // The protected document has the password of the PDFBox test, a space
        var options = new PdfReadOptions { MaxDecodedStreamLength = Options.MaxDecodedStreamLength, Password = id.EndsWith("_protected.pdf", StringComparison.Ordinal) ? " " : null };
        var document = PdfDocument.Open(CorpusFiles.Read(id), options);

        var signed = PdfSigner.Sign(document, new PdfSignatureOptions(certificate) { Reason = "Corpus" });

        var signatures = PdfSignatureVerifier.Verify(signed, options);
        var signature = signatures[^1];
        Assert.True(signature.IsValid, signature.Error);
        Assert.True(signature.CoversWholeDocument);
        Assert.Equal(certificate.Thumbprint, signature.Certificate!.Thumbprint);
    }

    [Fact]
    public void EverySuiteHasATest()
    {
        Assert.Equal(["pdfjs", "pdfium", "qpdf", "pdf20examples", "pdfbox", "verapdf"], CorpusFiles.Suites);
    }

    private static TheoryData<string> GetFiles(string suite) => new(CorpusFiles.GetFiles(suite));

    /// <summary>Gets the part of the pixels of a page that differ from the image PDFium renders; <see langword="null"/> when PDFium does not render it.</summary>
    private static async Task<double?> CompareWithPdfiumAsync(byte[] data, int page, string? password, Image<Rgba32> image)
    {
        var pdfium = await PdfiumTool.LocateAsync(XunitCancellationToken);
        var directory = Path.Combine(Path.GetTempPath(), "pdf-corpus-rendering", Guid.NewGuid().ToString("N"));
        try
        {
            var path = await pdfium.RenderPageAsync(data, page, RenderedDpi / 72, directory, password, XunitCancellationToken);
            if (path is null)
                return null;

            using var reference = Image.Load<Rgba32>(path);
            if (Math.Abs(reference.Width - image.Width) > 1 || Math.Abs(reference.Height - image.Height) > 1)
                return 1;

            var width = Math.Min(reference.Width, image.Width);
            var height = Math.Min(reference.Height, image.Height);
            var ours = image.Frames[0];
            var theirs = reference.Frames[0];
            long different = 0;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // PDFium leaves the background of a page with transparency transparent: the paper is white
                    var expected = theirs[x, y];
                    var actual = ours[x, y];
                    if (IsDifferent(actual.R, OverWhite(expected.R, expected.A)) || IsDifferent(actual.G, OverWhite(expected.G, expected.A)) || IsDifferent(actual.B, OverWhite(expected.B, expected.A)))
                    {
                        different++;
                    }
                }
            }

            return (double)different / ((long)width * height);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }

        static int OverWhite(byte value, byte alpha) => ((value * alpha) + (255 * (255 - alpha)) + 127) / 255;

        static bool IsDifferent(int actual, int expected) => Math.Abs(actual - expected) > MaxChannelDifference;
    }

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
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException)
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
