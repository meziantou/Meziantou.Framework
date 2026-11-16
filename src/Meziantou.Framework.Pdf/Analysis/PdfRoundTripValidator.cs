namespace Meziantou.Framework.Pdf;

/// <summary>
/// Checks that the writer is deterministic and keeps the document: the document is written, the output is read back
/// and written again, and the two outputs are compared, byte by byte and by their structure.
/// </summary>
/// <remarks>
/// <para>
/// The first output must keep the pages of the document (count, boxes, rotation, annotations, decoded content); the second
/// output must have the same object graph as the first (<see cref="PdfDocumentSummary.Digest"/>) and the same bytes. The
/// bytes of an encrypted document are not compared: each save encrypts with new random initialization vectors.
/// </para>
/// <para>The validator saves the document as <see cref="PdfDocument.ToByteArray(PdfSaveOptions?)"/> does: the save writes what the document model keeps apart from the objects (outline, font subsets, conformance entries).</para>
/// </remarks>
public static class PdfRoundTripValidator
{
    /// <summary>The writer writes the numbers with at most six decimals: the boxes of the document validated may differ from those of the first output by that much.</summary>
    private const double NumberTolerance = 1e-5;

    /// <summary>Validates the round trip of a document.</summary>
    /// <param name="document">The document.</param>
    /// <param name="saveOptions">The options of both saves, or <see langword="null"/> for the default options.</param>
    /// <param name="readOptions">The options to read the first output with, or <see langword="null"/> for the default options. An encrypted document needs its password.</param>
    /// <returns>The result of the validation.</returns>
    /// <exception cref="PdfException">The document cannot be read or written.</exception>
    /// <exception cref="InvalidOperationException">The document cannot be written.</exception>
    public static PdfRoundTripResult Validate(PdfDocument document, PdfSaveOptions? saveOptions = null, PdfReadOptions? readOptions = null)
    {
        ArgumentNullException.ThrowIfNull(document);

        var isEncrypted = document.IsEncrypted || document.Encryption is not null;
        var first = document.ToByteArray(saveOptions);
        var original = PdfDocumentSummary.Create(document);

        PdfDocument firstDocument;
        try
        {
            firstDocument = PdfDocument.Open(first, readOptions);
        }
        catch (PdfException ex)
        {
            return Fail(PdfRoundTripFailureKind.ReadBackFailed, $"The first output cannot be read: {ex.Message}", first, second: null, original, firstSummary: null, secondSummary: null);
        }

        if (firstDocument.IsRepaired)
            return Fail(PdfRoundTripFailureKind.ReadBackRepaired, "The first output is repaired when it is read", first, second: null, original, firstSummary: null, secondSummary: null);

        var firstSummary = PdfDocumentSummary.Create(firstDocument);
        if (ComparePages(original, firstSummary, tolerance: NumberTolerance) is { } pageFailure)
            return new PdfRoundTripResult(pageFailure, first, secondOutput: null, original, firstSummary, secondSummary: null, comparesBytes: !isEncrypted);

        var second = firstDocument.ToByteArray(saveOptions);
        PdfDocument secondDocument;
        try
        {
            secondDocument = PdfDocument.Open(second, readOptions);
        }
        catch (PdfException ex)
        {
            return Fail(PdfRoundTripFailureKind.ReadBackFailed, $"The second output cannot be read: {ex.Message}", first, second, original, firstSummary, secondSummary: null);
        }

        if (secondDocument.IsRepaired)
            return Fail(PdfRoundTripFailureKind.ReadBackRepaired, "The second output is repaired when it is read", first, second, original, firstSummary, secondSummary: null);

        var secondSummary = PdfDocumentSummary.Create(secondDocument);
        if (ComparePages(firstSummary, secondSummary, tolerance: 0) is { } secondPageFailure)
            return new PdfRoundTripResult(secondPageFailure, first, second, original, firstSummary, secondSummary, comparesBytes: !isEncrypted);

        if (!string.Equals(firstSummary.Digest, secondSummary.Digest, StringComparison.Ordinal))
        {
            var message = $"The object graphs of the two outputs differ ({firstSummary.ObjectCount} and {secondSummary.ObjectCount} objects)";
            return Fail(PdfRoundTripFailureKind.ObjectGraphChanged, message, first, second, original, firstSummary, secondSummary);
        }

        if (!isEncrypted)
        {
            var offset = first.AsSpan().CommonPrefixLength(second);
            if (offset != first.Length || first.Length != second.Length)
                return Fail(PdfRoundTripFailureKind.OutputsDiffer, $"The two outputs differ from the byte {offset} ({first.Length} and {second.Length} bytes)", first, second, original, firstSummary, secondSummary);
        }

        return new PdfRoundTripResult(failure: null, first, second, original, firstSummary, secondSummary, comparesBytes: !isEncrypted);

        PdfRoundTripResult Fail(PdfRoundTripFailureKind kind, string message, byte[] first, byte[]? second, PdfDocumentSummary original, PdfDocumentSummary? firstSummary, PdfDocumentSummary? secondSummary)
            => new(new PdfRoundTripFailure(kind, message, pageIndex: null), first, second, original, firstSummary, secondSummary, comparesBytes: !isEncrypted);
    }

    /// <summary>Validates the round trip of a file.</summary>
    /// <param name="data">The file.</param>
    /// <param name="readOptions">The options to read the file and the outputs with, or <see langword="null"/> for the default options.</param>
    /// <param name="saveOptions">The options of both saves, or <see langword="null"/> for the default options.</param>
    /// <returns>The result of the validation.</returns>
    /// <exception cref="PdfException">The file cannot be read, or the document cannot be written.</exception>
    /// <exception cref="InvalidOperationException">The document cannot be written.</exception>
    public static PdfRoundTripResult Validate(ReadOnlyMemory<byte> data, PdfReadOptions? readOptions = null, PdfSaveOptions? saveOptions = null)
    {
        var document = PdfDocument.Open(data, readOptions);
        return Validate(document, saveOptions, readOptions);
    }

    private static PdfRoundTripFailure? ComparePages(PdfDocumentSummary expected, PdfDocumentSummary actual, double tolerance)
    {
        if (expected.Pages.Count != actual.Pages.Count)
            return new PdfRoundTripFailure(PdfRoundTripFailureKind.PageCountChanged, $"The output has {actual.Pages.Count} pages instead of {expected.Pages.Count}", pageIndex: null);

        for (var i = 0; i < expected.Pages.Count; i++)
        {
            var before = expected.Pages[i];
            var after = actual.Pages[i];
            string? what = null;
            if (!AreClose(before.MediaBox, after.MediaBox, tolerance))
            {
                what = "media box";
            }
            else if (!AreClose(before.CropBox, after.CropBox, tolerance))
            {
                what = "crop box";
            }
            else if (before.Rotation != after.Rotation)
            {
                what = "rotation";
            }
            else if (before.AnnotationCount != after.AnnotationCount)
            {
                what = "number of annotations";
            }
            else if (!string.Equals(before.ContentDigest, after.ContentDigest, StringComparison.Ordinal))
            {
                what = "content";
            }

            if (what is not null)
                return new PdfRoundTripFailure(PdfRoundTripFailureKind.PageChanged, $"The {what} of the page {i} changed", i);
        }

        return null;
    }

    private static bool AreClose(PdfRectangle left, PdfRectangle right, double tolerance)
    {
        return Math.Abs(left.Left - right.Left) <= tolerance
            && Math.Abs(left.Bottom - right.Bottom) <= tolerance
            && Math.Abs(left.Right - right.Right) <= tolerance
            && Math.Abs(left.Top - right.Top) <= tolerance;
    }
}
