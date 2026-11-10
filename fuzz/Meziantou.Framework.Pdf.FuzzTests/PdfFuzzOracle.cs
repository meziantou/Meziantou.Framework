using System.Diagnostics;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.Signing;

namespace Meziantou.Framework.Pdf.FuzzTests;

/// <summary>
/// What every input must satisfy: reading it (pages, content, text, annotations, outline, metadata, embedded files,
/// structure tree, signatures), rendering it and writing it in full only throws the documented exceptions, within the
/// time limit and the memory limits of the options; reading it from a stream gives the same document, and a document
/// that is written can be read back without repair.
/// </summary>
internal static class PdfFuzzOracle
{
    private const int MaxPages = 10;
    private const int MaxStructureElements = 1000;

    public static readonly PdfReadOptions Options = new()
    {
        MaxDecodedStreamLength = 16 * 1024 * 1024,
        MaxNestingDepth = 64,
    };

    /// <summary>A low resolution and a bounded amount of work: the cost of a page stays small whatever its content.</summary>
    private static readonly PdfRenderOptions RenderOptions = new()
    {
        Dpi = 12,
        MaxOperations = 2_000_000,
        UseSystemFonts = false,
    };

    /// <summary>Checks an input.</summary>
    /// <param name="data">The input.</param>
    /// <param name="description">The name of the input in the failures.</param>
    /// <param name="password">The password to open the input with, or <see langword="null"/> for none.</param>
    /// <param name="render"><see langword="true"/> to render the first page too.</param>
    public static async Task RunAsync(byte[] data, string description, string? password = null, bool render = true)
    {
        var options = password is null ? Options : new PdfReadOptions
        {
            MaxDecodedStreamLength = Options.MaxDecodedStreamLength,
            MaxNestingDepth = Options.MaxNestingDepth,
            Password = password,
        };

        var stopwatch = Stopwatch.StartNew();
        await CheckAsync(data, description, () => ReadAsync(data, options, render)).ConfigureAwait(false);
        await CheckAsync(data, description, () =>
        {
            PdfSignatureVerifier.Verify(data, options);
            return Task.CompletedTask;
        }).ConfigureAwait(false);
        if (stopwatch.Elapsed > PdfFuzzSettings.CaseTimeout)
            throw new InvalidOperationException($"{description}: the input took {stopwatch.Elapsed}\nInput (base64): {Convert.ToBase64String(data)}");
    }

    private static async Task CheckAsync(byte[] data, string description, Func<Task> action)
    {
        try
        {
            await action().ConfigureAwait(false);
        }
        catch (Exception ex) when (IsDocumented(ex))
        {
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"{description}: undocumented exception {ex.GetType().Name}: {ex.Message}\nInput (base64): {Convert.ToBase64String(data)}", ex);
        }
    }

    private static bool IsDocumented(Exception ex) => ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or PdfPasswordException;

    /// <summary>Runs an independent part of the checks: a documented exception there does not stop the others.</summary>
    private static void Tolerate(Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException)
        {
        }
    }

    private static async Task ReadAsync(byte[] data, PdfReadOptions options, bool render)
    {
        var document = PdfDocument.Open(data, options);
        var pageCount = document.Pages.Count;
        var texts = new List<string>();
        foreach (var page in document.Pages.Take(MaxPages))
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

            texts.Add(page.ExtractText());
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

        if (render && pageCount > 0)
        {
            Tolerate(() =>
            {
                using var image = PdfRenderer.RenderPage(document.Pages[0], RenderOptions);
            });
        }

        _ = document.Information.Title;
        _ = document.Information.ModificationDate;
        foreach (var item in document.Outlines)
        {
            _ = item.Title;
            _ = item.Destination;
        }

        Tolerate(() => document.GetXmpMetadata());
        Tolerate(() =>
        {
            foreach (var file in document.EmbeddedFiles.Take(16))
            {
                _ = file.FileName;
                Tolerate(() => file.GetData());
            }
        });
        Tolerate(() => ReadStructureTree(document.StructureTree));

        // Reading from a stream reads the same document
        using (var stream = new MemoryStream(data, writable: false))
        {
            CheckSameDocument(PdfDocument.Open(stream, options), pageCount, texts, "Open(Stream)");
        }

        using (var stream = new MemoryStream(data, writable: false))
        {
            CheckSameDocument(await PdfDocument.OpenAsync(stream, options).ConfigureAwait(false), pageCount, texts, "OpenAsync(Stream)");
        }

        CheckReadBack(document.ToByteArray(), options, pageCount, "default options");
        CheckReadBack(document.ToByteArray(new PdfSaveOptions { CompressStreams = false, UseObjectStreams = false }), options, pageCount, "uncompressed streams, no object streams");
        if (pageCount > 0)
        {
            CheckRedaction(PdfDocument.Open(data, options), options, pageCount);
            CheckWatermark(PdfDocument.Open(data, options), options, pageCount);
            CheckPdfAConversion(PdfDocument.Open(data, options), options, pageCount);
        }
    }

    /// <summary>
    /// Redacts the left half of the first page: no glyph is left inside it, and the document is written and read back. A
    /// glyph whose box is inside the area touches it, so the redaction removed it; the boxes of the replacement texts are
    /// the union of the boxes of their glyphs, which is inside the area only when all of them are.
    /// </summary>
    private static void CheckRedaction(PdfDocument document, PdfReadOptions options, int pageCount)
    {
        var page = document.Pages[0];
        var box = page.MediaBox;
        var area = new PdfRectangle(box.Left, box.Bottom, box.Left + (box.Width / 2), box.Top);
        try
        {
            page.Redact([area], new PdfRedactionOptions { FillColor = null });
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or InvalidOperationException)
        {
            // InvalidOperationException: the file uses the highest object numbers, and no object can be added
            return;
        }

        Tolerate(() =>
        {
            foreach (var glyph in page.ExtractTextLayout().Lines.SelectMany(line => line.Glyphs))
            {
                var bounds = glyph.Bounds;
                if (bounds.Width > 0 && bounds.Height > 0 && bounds.Left > area.Left && bounds.Right < area.Right && bounds.Bottom > area.Bottom && bounds.Top < area.Top)
                    throw new InvalidOperationException($"The glyph '{glyph.Text}' at {bounds} is left in the redacted area {area}");
            }
        });

        CheckReadBack(document.ToByteArray(), options, pageCount, "after a redaction");
    }

    /// <summary>Adds a watermark under the first page and over every page, from the boxes and the rotation of the file: the document is written and read back.</summary>
    private static void CheckWatermark(PdfDocument document, PdfReadOptions options, int pageCount)
    {
        try
        {
            document.Pages[0].AddWatermark("DRAFT", new PdfWatermarkOptions { Placement = PdfContentPlacement.Under });
            document.AddWatermark("CONFIDENTIAL");
        }
        catch (InvalidOperationException)
        {
            // The file uses the highest object numbers: no object can be added
            return;
        }

        CheckReadBack(document.ToByteArray(), options, pageCount, "after a watermark");
    }

    /// <summary>Converts the document to PDF/A-3b: when the conversion succeeds, the save, which checks the same requirements, writes it, and it is read back.</summary>
    private static void CheckPdfAConversion(PdfDocument document, PdfReadOptions options, int pageCount)
    {
        PdfAConversionResult result;
        try
        {
            result = document.ConvertToPdfA(PdfAConformance.PdfA3B);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException or PdfResourceLimitException or InvalidOperationException)
        {
            // InvalidOperationException: the file uses the highest object numbers, and no object can be added
            return;
        }

        if (!result.Succeeded)
            return;

        byte[] written;
        try
        {
            written = document.ToByteArray();
        }
        catch (InvalidOperationException ex) when (ex.Message.StartsWith("No object number", StringComparison.Ordinal))
        {
            return;
        }

        CheckReadBack(written, options, pageCount, "after a conversion to PDF/A");
    }

    private static void ReadStructureTree(PdfStructureTree tree)
    {
        _ = tree.RoleMap;
        var pending = new Stack<(PdfStructureElement Element, int Depth)>(tree.Elements.Select(element => (element, 0)));
        for (var count = 0; count < MaxStructureElements && pending.TryPop(out var item); count++)
        {
            _ = item.Element.Type;
            _ = item.Element.AlternateDescription;
            _ = item.Element.ActualText;
            _ = item.Element.Language;
            _ = item.Element.Title;
            if (item.Depth < Options.MaxNestingDepth)
            {
                foreach (var child in item.Element.Children)
                {
                    pending.Push((child, item.Depth + 1));
                }
            }
        }
    }

    private static void CheckSameDocument(PdfDocument document, int pageCount, List<string> texts, string method)
    {
        if (document.Pages.Count != pageCount)
            throw new InvalidOperationException($"{method} reads {document.Pages.Count} pages instead of {pageCount}");

        var index = 0;
        foreach (var page in document.Pages.Take(MaxPages))
        {
            if (!string.Equals(page.ExtractText(), texts[index], StringComparison.Ordinal))
                throw new InvalidOperationException($"{method} extracts another text from the page {index}");

            index++;
        }
    }

    private static void CheckReadBack(byte[] written, PdfReadOptions options, int pageCount, string saveOptions)
    {
        PdfDocument saved;
        try
        {
            // The document is written with its encryption: it opens with the password that opened it
            saved = PdfDocument.Open(written, options);
        }
        catch (PdfException ex)
        {
            throw new InvalidOperationException($"A document written by the library ({saveOptions}) cannot be read back: {ex.Message}", ex);
        }

        if (saved.IsRepaired)
            throw new InvalidOperationException($"A document written by the library ({saveOptions}) is repaired when it is read back");

        if (saved.Pages.Count != pageCount)
            throw new InvalidOperationException($"The written document ({saveOptions}) has {saved.Pages.Count} pages instead of {pageCount}");
    }
}
