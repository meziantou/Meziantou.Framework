using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>
/// Writes the references with the numbers a full save gives the objects; a reference to an object of the document that
/// does not exist is written as <c>null</c> (7.3.10). The references of <paramref name="writerObjects"/>, the objects the
/// writer adds (the encryption dictionary), keep their numbers. A reference to an object of another document is an error.
/// </summary>
internal sealed class RenumberingReferenceWriter(Dictionary<IndirectObjectEntry, int> numbers, PdfIndirectObjectCollection objects, PdfIndirectObjectCollection? writerObjects = null) : ReferenceWriter
{
    public override void Write(PdfByteWriter writer, PdfReference reference)
    {
        if (ReferenceEquals(reference.Owner, objects))
        {
            if (objects.GetEntry(reference) is { } entry && numbers.TryGetValue(entry, out var number))
            {
                writer.WriteInteger(number);
                writer.WriteAscii(" 0 R");
            }
            else
            {
                writer.WriteAscii("null");
            }

            return;
        }

        if (writerObjects is not null && ReferenceEquals(reference.Owner, writerObjects))
        {
            base.Write(writer, reference);
            return;
        }

        throw ForeignReference(reference);
    }
}
