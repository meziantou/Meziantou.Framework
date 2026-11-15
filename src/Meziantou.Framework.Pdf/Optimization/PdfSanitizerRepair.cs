namespace Meziantou.Framework.Pdf;

/// <summary>An issue <see cref="PdfSaveSanitizer"/> repaired.</summary>
public sealed class PdfSanitizerRepair
{
    internal PdfSanitizerRepair(PdfSanitizerRepairKind kind, string description, int? pageIndex)
    {
        Kind = kind;
        Description = description;
        PageIndex = pageIndex;
    }

    /// <summary>Gets the kind of issue.</summary>
    public PdfSanitizerRepairKind Kind { get; }

    /// <summary>Gets a description of the repair.</summary>
    public string Description { get; }

    /// <summary>Gets the index of the page repaired, or <see langword="null"/> when the repair concerns the document.</summary>
    public int? PageIndex { get; }

    /// <inheritdoc/>
    public override string ToString() => Description;
}
