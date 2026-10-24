using System.Diagnostics;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>
/// What every input must satisfy: reading it (pages, content, text, annotations, outline) and writing it in full only
/// throws the documented exceptions, within the time limit and the memory limits of the options, and a document that is
/// written can be read back without repair.
/// </summary>
internal static class PdfFuzzOracle
{
    public static readonly PdfReadOptions Options = new()
    {
        MaxDecodedStreamLength = 16 * 1024 * 1024,
        MaxNestingDepth = 64,
    };

    public static void Run(byte[] data, string description)
    {
        var stopwatch = Stopwatch.StartNew();
        try
        {
            Read(data);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
        {
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{description}: undocumented exception {ex.GetType().Name}: {ex.Message}\nInput (base64): {Convert.ToBase64String(data)}", ex);
        }

        if (stopwatch.Elapsed > PdfFuzzSettings.CaseTimeout)
            throw new InvalidOperationException($"{description}: the input took {stopwatch.Elapsed}\nInput (base64): {Convert.ToBase64String(data)}");
    }

    private static void Read(byte[] data)
    {
        var document = PdfDocument.Open(data, Options);
        var pageCount = document.Pages.Count;
        foreach (var page in document.Pages.Take(10))
        {
            _ = page.MediaBox;
            _ = page.Rotation;
            try
            {
                _ = page.GetContentData();
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
                // A damaged content stream: the page is still read
            }

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
        _ = document.Information.ModificationDate;
        foreach (var item in document.Outlines)
        {
            _ = item.Title;
            _ = item.Destination;
        }

        var saved = PdfDocument.Open(document.ToByteArray(), Options);
        if (saved.IsRepaired)
            throw new InvalidOperationException("A document written by the library is repaired when it is read back");

        if (saved.Pages.Count != pageCount)
            throw new InvalidOperationException($"The written document has {saved.Pages.Count} pages instead of {pageCount}");
    }
}
