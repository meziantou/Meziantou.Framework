using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Objects;

/// <summary>An object of the PDF object model (ISO 32000-2, 7.3).</summary>
/// <remarks>
/// <para>
/// Numbers, booleans, names, strings and the null object are immutable. Arrays, dictionaries and streams are mutable and
/// are not thread-safe. An object is linked to the indirect objects of a document only through the
/// <see cref="PdfReference"/> objects it contains.
/// </para>
/// <para>The class cannot be derived outside the library.</para>
/// </remarks>
public abstract class PdfObject
{
    private protected PdfObject()
    {
    }

    /// <summary>Gets the kind of the object.</summary>
    public abstract PdfObjectKind Kind { get; }

    /// <summary>Returns the PDF syntax of the object. The data of a stream is not included.</summary>
    /// <returns>The PDF syntax of the object.</returns>
    public override string ToString() => PdfObjectSerializer.ToDisplayString(this);
}
