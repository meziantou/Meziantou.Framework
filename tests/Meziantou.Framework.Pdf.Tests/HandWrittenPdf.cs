using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Pdf.Tests;

/// <summary>
/// Writes PDF files from the text of their objects, independently of the writer of the library: the tests read these
/// files, so that the reader is never checked against the writer it is paired with.
/// </summary>
internal sealed class HandWrittenPdf
{
    private readonly List<(int Number, string Body)> _objects = [];
    private readonly HashSet<int> _compressed = [];

    public string Header { get; set; } = "%PDF-1.7\n";

    /// <summary>Gets or sets the trailer dictionary; <c>{size}</c> is replaced with the size of the table.</summary>
    public string Trailer { get; set; } = "<< /Size {size} /Root 1 0 R >>";

    /// <summary>Gets or sets a value added to every offset of the cross-reference section, to make it wrong.</summary>
    public int OffsetError { get; set; }

    public bool WriteXref { get; set; } = true;

    /// <summary>Gets or sets a value indicating whether the cross-reference information is a stream (7.5.8) instead of a table.</summary>
    public bool UseXrefStream { get; set; }

    public HandWrittenPdf Add(int number, string body)
    {
        _objects.Add((number, body));
        return this;
    }

    /// <summary>Stores an object in an object stream (7.5.7) instead of the body of the file.</summary>
    public HandWrittenPdf Compress(params int[] numbers)
    {
        foreach (var number in numbers)
        {
            _compressed.Add(number);
        }

        return this;
    }

    /// <summary>Creates a document with one page whose content stream shows a text with Helvetica.</summary>
    public static HandWrittenPdf CreateSimple(string text = "Hello")
    {
        var content = "BT /F1 12 Tf 72 720 Td (" + text + ") Tj ET";
        return new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, Stream(content));
    }

    public static string Stream(string content, string extraEntries = "") => "<< /Length " + Encoding.Latin1.GetByteCount(content).ToString(CultureInfo.InvariantCulture) + extraEntries + " >>\nstream\n" + content + "\nendstream";

    public byte[] ToArray()
    {
        var output = new MemoryStream();
        Write(output, Header);
        var offsets = new SortedDictionary<int, long>();
        var containers = new SortedDictionary<int, int>();
        var maxNumber = _objects.Count == 0 ? 0 : _objects.Max(o => o.Number);
        foreach (var (number, body) in _objects.Where(o => !_compressed.Contains(o.Number)))
        {
            offsets[number] = output.Length;
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        if (_compressed.Count > 0)
        {
            // The object stream, uncompressed: the pairs "number offset", then the objects
            var containerNumber = ++maxNumber;
            var header = new StringBuilder();
            var body = new StringBuilder();
            var index = 0;
            foreach (var (number, text) in _objects.Where(o => _compressed.Contains(o.Number)))
            {
                header.Append(CultureInfo.InvariantCulture, $"{number} {Encoding.Latin1.GetByteCount(body.ToString())} ");
                body.Append(text).Append('\n');
                containers[number] = index++;
            }

            var first = Encoding.Latin1.GetByteCount(header.ToString());
            offsets[containerNumber] = output.Length;
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{containerNumber} 0 obj\n{Stream(header.ToString() + body, $" /Type /ObjStm /N {_compressed.Count} /First {first}")}\nendobj\n"));
            foreach (var number in containers.Keys)
            {
                offsets.Remove(number);
            }

            containers = new SortedDictionary<int, int>(containers.ToDictionary(c => c.Key, c => (containerNumber << 8) | c.Value));
        }

        var xrefOffset = output.Length;
        if (UseXrefStream)
        {
            var xrefNumber = ++maxNumber;
            offsets[xrefNumber] = xrefOffset;
            var size = xrefNumber + 1;
            var data = new MemoryStream();
            for (var number = 0; number < size; number++)
            {
                if (offsets.TryGetValue(number, out var offset))
                {
                    WriteEntry(data, 1, offset + OffsetError, 0);
                }
                else if (containers.TryGetValue(number, out var location))
                {
                    WriteEntry(data, 2, location >> 8, location & 0xFF);
                }
                else
                {
                    WriteEntry(data, 0, 0, number == 0 ? 0xFFFF : 0);
                }
            }

            var trailer = Trailer.Replace("{size}", size.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal).Trim();
            var entries = trailer[2..^2] + string.Create(CultureInfo.InvariantCulture, $" /Type /XRef /W [1 4 2] /Length {data.Length}");
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{xrefNumber} 0 obj\n<<{entries}>>\nstream\n"));
            output.Write(data.ToArray());
            Write(output, "\nendstream\nendobj\n");
        }
        else if (WriteXref)
        {
            var size = maxNumber + 1;
            Write(output, string.Create(CultureInfo.InvariantCulture, $"xref\n0 {size}\n0000000000 65535 f \n"));
            for (var number = 1; number < size; number++)
            {
                Write(output, offsets.TryGetValue(number, out var offset) ? string.Create(CultureInfo.InvariantCulture, $"{offset + OffsetError:D10} 00000 n \n") : "0000000000 65535 f \n");
            }

            Write(output, "trailer\n" + Trailer.Replace("{size}", size.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal) + "\n");
        }

        Write(output, string.Create(CultureInfo.InvariantCulture, $"startxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    /// <summary>Appends an incremental update to a file: objects, a cross-reference table of these objects and a trailer with /Prev.</summary>
    public static byte[] AppendUpdate(byte[] original, IEnumerable<(int Number, string Body)> objects, string trailer)
    {
        var output = new MemoryStream();
        output.Write(original);
        var text = Encoding.Latin1.GetString(original);
        var prev = text[(text.LastIndexOf("startxref", StringComparison.Ordinal) + 9)..].Trim().Split('\n')[0].Trim();
        var offsets = new List<(int Number, long Offset)>();
        foreach (var (number, body) in objects)
        {
            offsets.Add((number, output.Length));
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n{body}\nendobj\n"));
        }

        var xrefOffset = output.Length;
        Write(output, "xref\n");
        foreach (var (number, offset) in offsets.OrderBy(o => o.Number))
        {
            Write(output, string.Create(CultureInfo.InvariantCulture, $"{number} 1\n{offset:D10} 00000 n \n"));
        }

        Write(output, "trailer\n" + trailer.Replace("{prev}", prev, StringComparison.Ordinal) + "\n");
        Write(output, string.Create(CultureInfo.InvariantCulture, $"startxref\n{xrefOffset}\n%%EOF\n"));
        return output.ToArray();
    }

    private static void WriteEntry(MemoryStream data, int type, long field2, int field3)
    {
        data.WriteByte((byte)type);
        data.WriteByte((byte)(field2 >> 24));
        data.WriteByte((byte)(field2 >> 16));
        data.WriteByte((byte)(field2 >> 8));
        data.WriteByte((byte)field2);
        data.WriteByte((byte)(field3 >> 8));
        data.WriteByte((byte)field3);
    }

    private static void Write(MemoryStream output, string text) => output.Write(Encoding.Latin1.GetBytes(text));
}
