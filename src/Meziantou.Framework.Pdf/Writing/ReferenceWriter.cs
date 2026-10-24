using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes references: with their own numbers, or with the numbers a full save gives the objects.</summary>
internal class ReferenceWriter
{
    public static ReferenceWriter Identity { get; } = new();

    public virtual void Write(PdfByteWriter writer, PdfReference reference)
    {
        writer.WriteInteger(reference.ObjectNumber);
        writer.WriteSpace();
        writer.WriteInteger(reference.Generation);
        writer.WriteAscii(" R");
    }
}
