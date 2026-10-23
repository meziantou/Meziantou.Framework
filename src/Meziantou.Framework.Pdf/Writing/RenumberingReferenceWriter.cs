using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes the references with the numbers a full save gives the objects; a reference to an object that does not exist is written as <c>null</c>.</summary>
internal sealed class RenumberingReferenceWriter(Dictionary<IndirectObjectEntry, int> numbers, PdfIndirectObjectCollection objects) : ReferenceWriter
{
    public override void Write(PdfByteWriter writer, PdfReference reference)
    {
        if (ReferenceEquals(reference.Owner, objects) && objects.GetEntry(reference) is { } entry && numbers.TryGetValue(entry, out var number))
        {
            writer.WriteInteger(number);
            writer.WriteAscii(" 0 R");
            return;
        }

        writer.WriteAscii("null");
    }
}
