using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Writes objects in the PDF syntax (ISO 32000-2, 7.3).</summary>
internal static class PdfObjectSerializer
{
    private const int MaxDepth = 1000;

    /// <summary>Gets the text of an object for <see cref="object.ToString"/>: a stream is its dictionary followed by <c>stream</c>.</summary>
    public static string ToDisplayString(PdfObject value)
    {
        var writer = new PdfByteWriter(64);
        try
        {
            Write(writer, value, ReferenceWriter.Identity);
        }
        catch (InvalidOperationException)
        {
            return "<object too deep>";
        }

        return Encoding.Latin1.GetString(writer.WrittenSpan);
    }

    /// <summary>Writes an object. A stream is written as its dictionary followed by the <c>stream</c> keyword, without its data (the file writer writes the data).</summary>
    public static void Write(PdfByteWriter writer, PdfObject value, ReferenceWriter references) => Write(writer, value, references, depth: 0);

    public static void WriteName(PdfByteWriter writer, PdfName name)
    {
        writer.WriteByte((byte)'/');
        foreach (var c in name.Key)
        {
            var b = (byte)c;
            if (b is < 0x21 or > 0x7E or (byte)'#' || IsDelimiter(b))
            {
                writer.WriteByte((byte)'#');
                writer.WriteByte(HexDigit(b >> 4));
                writer.WriteByte(HexDigit(b & 0xF));
            }
            else
            {
                writer.WriteByte(b);
            }
        }
    }

    public static void WriteString(PdfByteWriter writer, ReadOnlySpan<byte> bytes, bool hexadecimal)
    {
        if (!hexadecimal)
        {
            var nonPrintable = 0;
            foreach (var b in bytes)
            {
                if (b is (< 0x20 and not ((byte)'\n' or (byte)'\r' or (byte)'\t')) or > 0x7E)
                {
                    nonPrintable++;
                }
            }

            // Text strings in UTF-16 are mostly bytes that are not printable: the hexadecimal form is shorter
            hexadecimal = nonPrintable * 4 > bytes.Length;
        }

        if (hexadecimal)
        {
            writer.WriteByte((byte)'<');
            foreach (var b in bytes)
            {
                writer.WriteByte(HexDigit(b >> 4));
                writer.WriteByte(HexDigit(b & 0xF));
            }

            writer.WriteByte((byte)'>');
            return;
        }

        writer.WriteByte((byte)'(');
        foreach (var b in bytes)
        {
            switch (b)
            {
                case (byte)'(':
                case (byte)')':
                case (byte)'\\':
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte(b);
                    break;
                case (byte)'\n':
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte((byte)'n');
                    break;
                case (byte)'\r':
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte((byte)'r');
                    break;
                case (byte)'\t':
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte((byte)'t');
                    break;
                case < 0x20 or > 0x7E:
                    writer.WriteByte((byte)'\\');
                    writer.WriteByte((byte)('0' + ((b >> 6) & 7)));
                    writer.WriteByte((byte)('0' + ((b >> 3) & 7)));
                    writer.WriteByte((byte)('0' + (b & 7)));
                    break;
                default:
                    writer.WriteByte(b);
                    break;
            }
        }

        writer.WriteByte((byte)')');
    }

    private static void Write(PdfByteWriter writer, PdfObject value, ReferenceWriter references, int depth)
    {
        if (depth > MaxDepth)
            throw new InvalidOperationException("The object is nested too deeply to be written");

        switch (value)
        {
            case PdfNull:
                writer.WriteAscii("null");
                break;
            case PdfBoolean boolean:
                writer.WriteAscii(boolean.Value ? "true" : "false");
                break;
            case PdfInteger integer:
                writer.WriteInteger(integer.Value);
                break;
            case PdfReal real:
                writer.WriteNumber(real.Value);
                break;
            case PdfName name:
                WriteName(writer, name);
                break;
            case PdfString text:
                WriteString(writer, text.Span, text.IsHexadecimal);
                break;
            case PdfReference reference:
                references.Write(writer, reference);
                break;
            case PdfArray array:
                writer.WriteByte((byte)'[');
                for (var i = 0; i < array.Count; i++)
                {
                    if (i > 0)
                    {
                        writer.WriteSpace();
                    }

                    Write(writer, array[i], references, depth + 1);
                }

                writer.WriteByte((byte)']');
                break;
            case PdfDictionary dictionary:
                WriteDictionary(writer, dictionary, references, depth);
                break;
            case PdfStream stream:
                WriteDictionary(writer, stream.Dictionary, references, depth);
                writer.WriteAscii(" stream");
                break;
            default:
                throw new InvalidOperationException("Unknown object type " + value.GetType());
        }
    }

    private static void WriteDictionary(PdfByteWriter writer, PdfDictionary dictionary, ReferenceWriter references, int depth)
    {
        writer.WriteAscii("<<");
        var first = true;
        foreach (var entry in dictionary)
        {
            if (!first)
            {
                writer.WriteSpace();
            }

            first = false;
            WriteName(writer, entry.Key);
            writer.WriteSpace();
            Write(writer, entry.Value, references, depth + 1);
        }

        writer.WriteAscii(">>");
    }

    private static bool IsDelimiter(byte b) => b is (byte)'(' or (byte)')' or (byte)'<' or (byte)'>' or (byte)'[' or (byte)']' or (byte)'{' or (byte)'}' or (byte)'/' or (byte)'%';

    private static byte HexDigit(int value) => (byte)(value < 10 ? '0' + value : 'A' + value - 10);
}
