namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfRoundTripValidator.Validate(PdfDocument, PdfSaveOptions?, PdfReadOptions?)"/>.</summary>
public sealed class PdfRoundTripResult
{
    internal PdfRoundTripResult(PdfRoundTripFailure? failure, byte[] firstOutput, byte[]? secondOutput, PdfDocumentSummary originalSummary, PdfDocumentSummary? firstSummary, PdfDocumentSummary? secondSummary, bool comparesBytes)
    {
        Failure = failure;
        FirstOutput = firstOutput;
        SecondOutput = secondOutput is null ? default(ReadOnlyMemory<byte>?) : secondOutput.AsMemory();
        OriginalSummary = originalSummary;
        FirstSummary = firstSummary;
        SecondSummary = secondSummary;
        ComparesBytes = comparesBytes;
    }

    /// <summary>Gets a value indicating whether the round trip succeeded: the outputs keep the pages, have the same structure and, unless the document is encrypted, the same bytes.</summary>
    public bool Succeeded => Failure is null;

    /// <summary>Gets the first failed check, or <see langword="null"/> when the round trip succeeded.</summary>
    public PdfRoundTripFailure? Failure { get; }

    /// <summary>Gets a value indicating whether the bytes of the outputs are compared: they are not for an encrypted document.</summary>
    public bool ComparesBytes { get; }

    /// <summary>Gets a value indicating whether the two outputs are byte-identical.</summary>
    public bool AreOutputsIdentical => SecondOutput is { } second && FirstOutput.Span.SequenceEqual(second.Span);

    /// <summary>Gets a value indicating whether the two outputs have the same pages and the same object graph.</summary>
    public bool AreOutputsStructurallyEquivalent => FirstSummary is not null && SecondSummary is not null
        && string.Equals(FirstSummary.Digest, SecondSummary.Digest, StringComparison.Ordinal)
        && FirstSummary.Pages.SequenceEqual(SecondSummary.Pages);

    /// <summary>Gets the first output: the document written.</summary>
    public ReadOnlyMemory<byte> FirstOutput { get; }

    /// <summary>Gets the second output: the first output read and written again, or <see langword="null"/> when the validation stopped before.</summary>
    public ReadOnlyMemory<byte>? SecondOutput { get; }

    /// <summary>Gets the summary of the document validated.</summary>
    public PdfDocumentSummary OriginalSummary { get; }

    /// <summary>Gets the summary of the first output, or <see langword="null"/> when it cannot be read.</summary>
    public PdfDocumentSummary? FirstSummary { get; }

    /// <summary>Gets the summary of the second output, or <see langword="null"/> when it was not written or cannot be read.</summary>
    public PdfDocumentSummary? SecondSummary { get; }
}
