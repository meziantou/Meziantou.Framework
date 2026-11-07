namespace Meziantou.Framework.Pdf.Signing;

/// <summary>The fields a field-locking signature locks (ISO 32000-2, 12.8.2.4, the <c>/Action</c> of the FieldMDP transform).</summary>
public enum PdfFieldLockAction
{
    /// <summary>Every field of the form (<c>/All</c>).</summary>
    All,

    /// <summary>The fields of <see cref="PdfFieldLock.Fields"/> (<c>/Include</c>).</summary>
    Include,

    /// <summary>Every field but those of <see cref="PdfFieldLock.Fields"/> (<c>/Exclude</c>).</summary>
    Exclude,
}
