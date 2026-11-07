using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes references: with their own numbers, or with the numbers a full save gives the objects.</summary>
internal class ReferenceWriter
{
    /// <summary>Gets the writer of the references with their own numbers, whatever document they belong to (the text of the objects).</summary>
    public static ReferenceWriter Identity { get; } = new();

    public virtual void Write(PdfByteWriter writer, PdfReference reference)
    {
        writer.WriteInteger(reference.ObjectNumber);
        writer.WriteSpace();
        writer.WriteInteger(reference.Generation);
        writer.WriteAscii(" R");
    }

    /// <summary>
    /// Gets the error of a reference to an object of another document: its number would point to an unrelated object of
    /// the document written. Objects are not copied between documents.
    /// </summary>
    protected static InvalidOperationException ForeignReference(PdfReference reference) => new($"The reference {reference.ObjectNumber} {reference.Generation} R belongs to another document: a document can only refer to its own objects");
}
