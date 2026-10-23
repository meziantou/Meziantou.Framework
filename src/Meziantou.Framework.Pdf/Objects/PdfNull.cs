namespace Meziantou.Framework.Pdf.Objects;

/// <summary>The null object (ISO 32000-2, 7.3.9). A dictionary entry whose value is null is equivalent to an absent entry.</summary>
public sealed class PdfNull : PdfObject
{
    private PdfNull()
    {
    }

    /// <summary>Gets the null object.</summary>
    public static PdfNull Instance { get; } = new();

    /// <inheritdoc/>
    public override PdfObjectKind Kind => PdfObjectKind.Null;
}
