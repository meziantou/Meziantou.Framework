using System.Globalization;

namespace Meziantou.Framework.Pdf.Signing;

/// <summary>An object of a document that a revision appended after a signature added or changed.</summary>
public sealed class PdfModification
{
    internal PdfModification(int objectNumber, PdfModificationKind kind, string? fieldName, bool isNewObject)
    {
        ObjectNumber = objectNumber;
        Kind = kind;
        FieldName = fieldName;
        IsNewObject = isNewObject;
    }

    /// <summary>Gets the number of the object.</summary>
    public int ObjectNumber { get; }

    /// <summary>Gets what the change is.</summary>
    public PdfModificationKind Kind { get; }

    /// <summary>Gets the fully qualified name of the field the object belongs to, or <see langword="null"/>.</summary>
    public string? FieldName { get; }

    /// <summary>Gets a value indicating whether the object did not exist in the signed revision.</summary>
    public bool IsNewObject { get; }

    /// <inheritdoc />
    public override string ToString() => FieldName is null ? string.Create(CultureInfo.InvariantCulture, $"{ObjectNumber}: {Kind}") : string.Create(CultureInfo.InvariantCulture, $"{ObjectNumber}: {Kind} ({FieldName})");
}
