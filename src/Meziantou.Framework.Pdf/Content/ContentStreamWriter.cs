using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Content;

/// <summary>Writes the operations <see cref="ContentStreamReader"/> reads: operands followed by an operator, and the inline images.</summary>
internal static class ContentStreamWriter
{
    /// <summary>Writes an operation. An inline image is the operator <c>BI</c> with its dictionary as operand and its data.</summary>
    public static void Write(PdfByteWriter writer, string name, ReadOnlySpan<PdfObject> operands, ReadOnlySpan<byte> inlineData = default)
    {
        if (name == "BI")
        {
            writer.WriteAscii("BI ");
            if (operands.Length > 0 && operands[0] is PdfDictionary dictionary)
            {
                foreach (var (key, value) in dictionary)
                {
                    PdfObjectSerializer.WriteName(writer, key);
                    writer.WriteSpace();
                    PdfObjectSerializer.Write(writer, value, ReferenceWriter.Identity);
                    writer.WriteSpace();
                }
            }

            writer.WriteAscii("ID ");
            writer.Write(inlineData);
            writer.WriteAscii("\nEI\n");
            return;
        }

        foreach (var operand in operands)
        {
            PdfObjectSerializer.Write(writer, operand, ReferenceWriter.Identity);
            writer.WriteSpace();
        }

        writer.WriteAscii(name);
        writer.WriteNewLine();
    }
}
