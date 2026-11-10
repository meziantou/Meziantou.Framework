using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Conformance;

/// <summary>
/// Embeds a substitute for a standard font a document does not embed: the bundled Liberation font of its kind (Sans for
/// Helvetica, Serif for Times, Mono for Courier), which has its widths. The font dictionary becomes a symbolic TrueType
/// font (ISO 32000-2, 9.6.5.4) whose embedded program maps each code to its glyph with a (3,0) <c>cmap</c> subtable, as
/// the encoding of the font maps it, and whose <c>ToUnicode</c> CMap gives the text: the codes of the content and their
/// widths do not change, so nothing on the page moves.
/// </summary>
internal static class SubstituteFont
{
    private const long MaxFontAllocationBytes = 64L * 1024 * 1024;
    private const uint CmapTag = 0x636D6170; // cmap
    private const uint HeadTag = 0x68656164; // head

    private static readonly PdfName FirstCharName = PdfName.Create("FirstChar");
    private static readonly PdfName LastCharName = PdfName.Create("LastChar");
    private static readonly PdfName WidthsName = PdfName.Create("Widths");
    private static readonly PdfName EncodingName = PdfName.Create("Encoding");
    private static readonly PdfName DifferencesName = PdfName.Create("Differences");
    private static readonly PdfName BaseEncodingName = PdfName.Create("BaseEncoding");
    private static readonly PdfName TrueTypeName = PdfName.Create("TrueType");
    private static readonly PdfName FontDescriptorName = PdfName.Create("FontDescriptor");
    private static readonly PdfName Length1Name = PdfName.Create("Length1");

    /// <summary>Gets the name of the standard font a font dictionary designates, when the library has a substitute for it: a simple font of a Latin standard font that is not embedded.</summary>
    public static string? GetSubstitutableName(PdfDictionary font)
    {
        if (font.GetName(PdfNames.Subtype)?.Value is not ("Type1" or "TrueType" or "MMType1") || font.GetName(PdfNames.BaseFont)?.Value is not { } baseFont)
            return null;

        return StandardFontMetrics.GetStandardName(baseFont) is { } name && name is not ("Symbol" or "ZapfDingbats") ? name : null;
    }

    /// <summary>Embeds the substitute of a standard font in its dictionary.</summary>
    /// <returns><see langword="false"/> when the bundled font cannot be loaded.</returns>
    public static bool Embed(PdfDocument document, PdfDictionary font, string standardName)
    {
        var face = LoadFace(standardName);
        if (face is null)
            return false;

        var metrics = StandardFontMetrics.Get(standardName);
        var names = GetEncoding(font);

        // The glyph of each code, and its width: the widths of the dictionary, which the content was laid out with
        var glyphs = new int[256];
        var widths = new double[256];
        var firstChar = (int)font.GetInteger(FirstCharName, 0);
        var dictionaryWidths = font.Get<PdfArray>(WidthsName);
        var text = new string?[256];
        for (var code = 0; code < 256; code++)
        {
            var name = names[code];
            if (name is null)
                continue;

            text[code] = GlyphList.ToUnicode(name);
            if (text[code] is { Length: > 0 } unicode && Rune.DecodeFromUtf16(unicode, out var rune, out _) == System.Buffers.OperationStatus.Done)
            {
                glyphs[code] = face.MapCodePoint(rune.Value);
            }

            var index = code - firstChar;
            widths[code] = dictionaryWidths is not null && index >= 0 && index < dictionaryWidths.Count
                ? dictionaryWidths.GetNumber(index)
                : StandardFontMetrics.GetWidth(metrics, name) ?? 0;
        }

        var program = AddCmap(FontSubsetter.SubsetTrueType(face, glyphs.Where(glyph => glyph > 0).Append(0)), glyphs);
        var fontFile = new PdfStream(new PdfDictionary { [Length1Name] = PdfInteger.Get(program.Length) });
        fontFile.SetData(program);

        var bold = standardName.Contains("Bold", StringComparison.Ordinal);
        var italic = standardName.Contains("Oblique", StringComparison.Ordinal) || standardName.Contains("Italic", StringComparison.Ordinal);
        var fixedPitch = standardName.StartsWith("Courier", StringComparison.Ordinal);
        var serif = standardName.StartsWith("Times", StringComparison.Ordinal);

        // FixedPitch 1, Serif 2, Symbolic 4, Italic 64, ForceBold 262144 (9.8.2)
        var flags = 4 | (fixedPitch ? 1 : 0) | (serif ? 2 : 0) | (italic ? 64 : 0) | (bold ? 262144 : 0);
        var box = metrics.FontBBox ?? new AfmBox(0, StandardFontMetrics.GetDescender(metrics), 1000, StandardFontMetrics.GetAscender(metrics));
        var descriptor = new PdfDictionary
        {
            [PdfNames.Type] = FontDescriptorName,
            [PdfName.Create("FontName")] = font[PdfNames.BaseFont]!,
            [PdfName.Create("Flags")] = PdfInteger.Get(flags),
            [PdfName.Create("FontBBox")] = PdfArray.FromNumbers(box.Left, box.Bottom, box.Right, box.Top),
            [PdfName.Create("ItalicAngle")] = PdfInteger.Get(italic ? -12 : 0),
            [PdfName.Create("Ascent")] = PdfReal.Get(StandardFontMetrics.GetAscender(metrics)),
            [PdfName.Create("Descent")] = PdfReal.Get(StandardFontMetrics.GetDescender(metrics)),
            [PdfName.Create("CapHeight")] = PdfReal.Get(StandardFontMetrics.GetCapHeight(metrics)),
            [PdfName.Create("StemV")] = PdfInteger.Get(bold ? 140 : 80),
            [PdfNames.FontFile2] = document.Objects.Add(fontFile),
        };

        if (!font.ContainsKey(PdfNames.ToUnicode))
        {
            var toUnicode = new PdfStream();
            toUnicode.SetData(CreateToUnicode(text));
            font[PdfNames.ToUnicode] = document.Objects.Add(toUnicode);
        }

        // A symbolic TrueType font has no encoding: its codes select the glyphs through the (3,0) subtable (9.6.5.4)
        font[PdfNames.Subtype] = TrueTypeName;
        font.Remove(EncodingName);
        font[FirstCharName] = PdfInteger.Get(0);
        font[LastCharName] = PdfInteger.Get(255);
        font[WidthsName] = PdfArray.FromNumbers(widths);
        font[FontDescriptorName] = document.Objects.Add(descriptor);
        return true;
    }

    private static FontFace? LoadFace(string standardName)
    {
        var family = standardName.StartsWith("Courier", StringComparison.Ordinal) ? "LiberationMono" : standardName.StartsWith("Times", StringComparison.Ordinal) ? "LiberationSerif" : "LiberationSans";
        var bold = standardName.Contains("Bold", StringComparison.Ordinal);
        var italic = standardName.Contains("Oblique", StringComparison.Ordinal) || standardName.Contains("Italic", StringComparison.Ordinal);
        var style = (bold, italic) switch
        {
            (true, true) => "BoldItalic",
            (true, false) => "Bold",
            (false, true) => "Italic",
            _ => "Regular",
        };

        using var stream = typeof(SubstituteFont).Assembly.GetManifestResourceStream("Meziantou.Framework.Pdf.Rendering.Fonts." + family + "-" + style + ".woff2");
        if (stream is null)
            return null;

        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        var faces = FontLoader.Load(buffer.ToArray(), new PooledFontEnvironment(FontResourceLimits.Default, MaxFontAllocationBytes));
        return faces.Length > 0 ? faces[0] : null;
    }

    /// <summary>Gets the glyph name of each code: the encoding of the font (9.6.5), else the encoding of the standard font.</summary>
    private static string?[] GetEncoding(PdfDictionary font)
    {
        var result = new string?[256];
        string?[]? baseEncoding = null;
        PdfArray? differences = null;
        switch (font.GetResolved(EncodingName))
        {
            case PdfName name:
                baseEncoding = FontEncodings.GetByName(name.Value);
                break;
            case PdfDictionary encoding:
                baseEncoding = encoding.GetName(BaseEncodingName) is { } baseName ? FontEncodings.GetByName(baseName.Value) : null;
                differences = encoding.Get<PdfArray>(DifferencesName);
                break;
        }

        baseEncoding ??= FontEncodings.Standard;
        for (var code = 0; code < 256 && code < baseEncoding.Length; code++)
        {
            result[code] = baseEncoding[code];
        }

        if (differences is not null)
        {
            var code = 0;
            foreach (var item in differences)
            {
                switch (PdfReference.ResolveIfReference(item))
                {
                    case PdfNumber number:
                        code = (int)Math.Clamp(number.DoubleValue, -1, 256);
                        break;
                    case PdfName glyphName:
                        if (code is >= 0 and < 256)
                        {
                            result[code] = glyphName.Value;
                        }

                        code++;
                        break;
                }
            }
        }

        return result;
    }

    private static byte[] CreateToUnicode(string?[] text)
    {
        var mappings = new List<string>();
        for (var code = 0; code < text.Length; code++)
        {
            if (text[code] is { Length: > 0 } value)
            {
                mappings.Add(string.Create(CultureInfo.InvariantCulture, $"<{code:X2}> <{Convert.ToHexString(Encoding.BigEndianUnicode.GetBytes(value))}>"));
            }
        }

        var builder = new StringBuilder();
        builder.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        builder.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        builder.Append("1 begincodespacerange\n<00> <FF>\nendcodespacerange\n");
        for (var i = 0; i < mappings.Count; i += 100)
        {
            var chunk = mappings.Skip(i).Take(100).ToList();
            builder.Append(chunk.Count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            foreach (var mapping in chunk)
            {
                builder.Append(mapping).Append('\n');
            }

            builder.Append("endbfchar\n");
        }

        builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    /// <summary>
    /// Adds to an sfnt a <c>cmap</c> table with one (3,0) subtable of format 4 that maps the code <c>c</c> to its glyph
    /// at <c>0xF000 + c</c>, as a symbolic TrueType font of a PDF document reads it (9.6.5.4).
    /// </summary>
    private static byte[] AddCmap(byte[] sfnt, int[] glyphs)
    {
        var tables = new SortedDictionary<uint, byte[]>();
        var count = BinaryPrimitives.ReadUInt16BigEndian(sfnt.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = sfnt.AsSpan(12 + (i * 16), 16);
            var tag = BinaryPrimitives.ReadUInt32BigEndian(record);
            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]);
            var length = (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..]);
            tables[tag] = sfnt.AsSpan(offset, length).ToArray();
        }

        tables[CmapTag] = CreateCmap(glyphs);
        return WriteSfnt(BinaryPrimitives.ReadUInt32BigEndian(sfnt), tables);
    }

    private static byte[] CreateCmap(int[] glyphs)
    {
        // One segment per mapped code, and the last segment (0xFFFF) that the format needs
        var segments = new List<(ushort Code, ushort Delta)>();
        for (var code = 0; code < glyphs.Length; code++)
        {
            if (glyphs[code] > 0)
            {
                var character = (ushort)(0xF000 + code);
                segments.Add((character, (ushort)((glyphs[code] - character) & 0xFFFF)));
            }
        }

        segments.Add((0xFFFF, 1));
        var segmentCount = segments.Count;
        var subtableLength = 16 + (segmentCount * 8);
        var data = new byte[12 + subtableLength];
        var span = data.AsSpan();
        BinaryPrimitives.WriteUInt16BigEndian(span, 0); // version
        BinaryPrimitives.WriteUInt16BigEndian(span[2..], 1); // one subtable
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], 3); // Windows
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], 0); // Symbol
        BinaryPrimitives.WriteUInt32BigEndian(span[8..], 12);
        var table = span[12..];
        var searchRange = 2 * (1 << (int)Math.Floor(Math.Log2(segmentCount)));
        BinaryPrimitives.WriteUInt16BigEndian(table, 4);
        BinaryPrimitives.WriteUInt16BigEndian(table[2..], (ushort)subtableLength);
        BinaryPrimitives.WriteUInt16BigEndian(table[4..], 0); // language
        BinaryPrimitives.WriteUInt16BigEndian(table[6..], (ushort)(segmentCount * 2));
        BinaryPrimitives.WriteUInt16BigEndian(table[8..], (ushort)searchRange);
        BinaryPrimitives.WriteUInt16BigEndian(table[10..], (ushort)Math.Log2(searchRange / 2));
        BinaryPrimitives.WriteUInt16BigEndian(table[12..], (ushort)((segmentCount * 2) - searchRange));
        var endCodes = table[14..];
        var startCodes = table[(16 + (segmentCount * 2))..];
        var deltas = table[(16 + (segmentCount * 4))..];
        var rangeOffsets = table[(16 + (segmentCount * 6))..];
        for (var i = 0; i < segmentCount; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(endCodes[(i * 2)..], segments[i].Code);
            BinaryPrimitives.WriteUInt16BigEndian(startCodes[(i * 2)..], segments[i].Code);
            BinaryPrimitives.WriteUInt16BigEndian(deltas[(i * 2)..], segments[i].Delta);
            BinaryPrimitives.WriteUInt16BigEndian(rangeOffsets[(i * 2)..], 0);
        }

        return data;
    }

    /// <summary>Writes an sfnt from its tables, sorted by tag, with their checksums and the checksum adjustment of the <c>head</c> table.</summary>
    private static byte[] WriteSfnt(uint version, SortedDictionary<uint, byte[]> tables)
    {
        static int Pad(int length) => (length + 3) & ~3;
        static uint Checksum(ReadOnlySpan<byte> data)
        {
            uint sum = 0;
            for (var i = 0; i < data.Length; i += 4)
            {
                uint value = 0;
                for (var j = 0; j < 4; j++)
                {
                    value = (value << 8) | (i + j < data.Length ? data[i + j] : 0u);
                }

                sum = unchecked(sum + value);
            }

            return sum;
        }

        var count = tables.Count;
        var directoryLength = 12 + (count * 16);
        var output = new byte[directoryLength + tables.Values.Sum(table => Pad(table.Length))];
        var span = output.AsSpan();
        var power = 1 << (int)Math.Floor(Math.Log2(count));
        BinaryPrimitives.WriteUInt32BigEndian(span, version);
        BinaryPrimitives.WriteUInt16BigEndian(span[4..], (ushort)count);
        BinaryPrimitives.WriteUInt16BigEndian(span[6..], (ushort)(power * 16));
        BinaryPrimitives.WriteUInt16BigEndian(span[8..], (ushort)Math.Log2(power));
        BinaryPrimitives.WriteUInt16BigEndian(span[10..], (ushort)((count * 16) - (power * 16)));
        var offset = directoryLength;
        var index = 0;
        var headOffset = -1;
        foreach (var (tag, data) in tables)
        {
            if (tag == HeadTag && data.Length >= 12)
            {
                // The adjustment is computed with the field set to 0
                data.AsSpan(8, 4).Clear();
                headOffset = offset;
            }

            var record = span.Slice(12 + (index * 16), 16);
            BinaryPrimitives.WriteUInt32BigEndian(record, tag);
            BinaryPrimitives.WriteUInt32BigEndian(record[4..], Checksum(data));
            BinaryPrimitives.WriteUInt32BigEndian(record[8..], (uint)offset);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)data.Length);
            data.CopyTo(span[offset..]);
            offset += Pad(data.Length);
            index++;
        }

        if (headOffset >= 0)
        {
            BinaryPrimitives.WriteUInt32BigEndian(span[(headOffset + 8)..], unchecked(0xB1B0AFBA - Checksum(output)));
        }

        return output;
    }
}
