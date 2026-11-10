using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes the references of an incremental update with their own numbers; a reference to an object of another document is an error.</summary>
internal sealed class IncrementalReferenceWriter(PdfIndirectObjectCollection objects) : ReferenceWriter
{
    public override void Write(PdfByteWriter writer, PdfReference reference)
    {
        if (!ReferenceEquals(reference.Owner, objects))
            throw ForeignReference(reference);

        // A reference of the file to an object it does not have, whose number an added object took, is the null object
        if (reference.Generation == PdfIndirectObjectCollection.DanglingGeneration)
        {
            writer.WriteAscii("null");
            return;
        }

        base.Write(writer, reference);
    }
}
