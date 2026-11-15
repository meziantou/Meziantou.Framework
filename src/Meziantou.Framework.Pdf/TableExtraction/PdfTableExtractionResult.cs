namespace Meziantou.Framework.Pdf;

/// <summary>The result of <see cref="PdfTableExtractor.Extract(PdfPage, PdfRectangle, PdfTableExtractionOptions?)"/>: a table, or the reason why there is none.</summary>
public sealed class PdfTableExtractionResult
{
    internal PdfTableExtractionResult(PdfExtractedTable? table, PdfTableExtractionFailureKind? failure, string? message)
    {
        Table = table;
        Failure = failure;
        Message = message;
    }

    /// <summary>Gets a value indicating whether a table was rebuilt.</summary>
    [System.Diagnostics.CodeAnalysis.MemberNotNullWhen(true, nameof(Table))]
    public bool Succeeded => Table is not null;

    /// <summary>Gets the table, or <see langword="null"/> when it could not be rebuilt.</summary>
    public PdfExtractedTable? Table { get; }

    /// <summary>Gets why the table could not be rebuilt, or <see langword="null"/> when it was.</summary>
    public PdfTableExtractionFailureKind? Failure { get; }

    /// <summary>Gets a description of the failure, or <see langword="null"/> when the table was rebuilt.</summary>
    public string? Message { get; }
}
