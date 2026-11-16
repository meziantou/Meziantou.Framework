namespace Meziantou.Framework.Pdf;

/// <summary>An accessibility issue of a document (<see cref="PdfAccessibilityInspector"/>).</summary>
public sealed class PdfAccessibilityIssue
{
    internal PdfAccessibilityIssue(PdfAccessibilityIssueKind kind, string message, int? pageIndex = null, string? structureType = null)
    {
        Kind = kind;
        Message = message;
        PageIndex = pageIndex;
        StructureType = structureType;
    }

    /// <summary>Gets the kind of issue.</summary>
    public PdfAccessibilityIssueKind Kind { get; }

    /// <summary>Gets a description of the issue.</summary>
    public string Message { get; }

    /// <summary>Gets the index of the page of the issue, or <see langword="null"/> when it concerns the document.</summary>
    public int? PageIndex { get; }

    /// <summary>Gets the type of the structure element of the issue, such as <c>Figure</c>, or <see langword="null"/>.</summary>
    public string? StructureType { get; }

    /// <inheritdoc/>
    public override string ToString() => PageIndex is { } page ? $"{Kind} (page {page + 1}): {Message}" : $"{Kind}: {Message}";
}
