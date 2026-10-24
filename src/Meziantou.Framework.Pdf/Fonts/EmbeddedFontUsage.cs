using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// The glyphs a document shows with an embedded font, and the text of each one. When the document is saved, they
/// become the font objects: the <c>Type0</c> font, its CIDFont, its descriptor, the subset of the font program and
/// the <c>ToUnicode</c> CMap (ISO 32000-2, 9.7 and 9.10.3).
/// </summary>
internal sealed class EmbeddedFontUsage
{
    // The entries of a bfchar section of a CMap (9.10.3)
    private const int MaxEntriesPerSection = 100;

    private readonly EmbeddedPdfFont _font;
    private readonly SortedSet<int> _glyphs = [];
    private readonly Dictionary<int, string> _texts = [];
    private PdfReference? _cidFont;
    private PdfReference? _descriptor;
    private PdfReference? _fontFile;
    private PdfReference? _toUnicode;
    private int _writtenVersion = -1;
    private int _version;

    public EmbeddedFontUsage(EmbeddedPdfFont font, PdfReference reference)
    {
        _font = font;
        Reference = reference;
    }

    /// <summary>Gets the reference to the <c>Type0</c> font dictionary.</summary>
    public PdfReference Reference { get; }

    /// <summary>Records the glyphs of a shaped text.</summary>
    /// <returns><see langword="false"/> when a glyph already shows another text in the document: the text then needs a replacement text to be extracted.</returns>
    public bool Record(ShapedText text)
    {
        foreach (var glyph in text.Glyphs)
        {
            if (_glyphs.Add(glyph.Glyph))
            {
                _version++;
            }
        }

        var consistent = true;
        foreach (var (glyph, value) in text.Mappings)
        {
            if (_texts.TryGetValue(glyph, out var existing))
            {
                consistent &= string.Equals(existing, value, StringComparison.Ordinal);
            }
            else
            {
                _texts.Add(glyph, value);
                _version++;
            }
        }

        return consistent;
    }

    /// <summary>Writes the font objects, when glyphs were added since they were written.</summary>
    public void Write(PdfDocument document)
    {
        if (_writtenVersion == _version)
            return;

        _writtenVersion = _version;
        var face = _font.Face;
        var isCff = face.Outlines == FontOutlineKind.Cff;
        var baseFont = CreateSubsetTag() + "+" + _font.PostScriptName;

        var program = isCff ? FontSubsetter.SubsetCff(face, _glyphs, baseFont) : FontSubsetter.SubsetTrueType(face, _glyphs);
        var fontFile = new PdfStream();
        fontFile.SetData(program);
        if (isCff)
        {
            fontFile.Dictionary[PdfNames.Subtype] = PdfName.Create("CIDFontType0C");
        }
        else
        {
            fontFile.Dictionary[PdfNames.Length1] = PdfInteger.Get(program.Length);
        }

        _fontFile = Set(document, _fontFile, fontFile);

        // The flags (9.8.2): fixed pitch, symbolic (the glyphs are not those of the standard Latin character set), italic
        var flags = 4;
        if (face.IsMonospaced)
        {
            flags |= 1;
        }

        if (face.IsItalic || face.ItalicAngle != 0)
        {
            flags |= 64;
        }

        var descriptor = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.FontDescriptor,
            [PdfNames.FontName] = PdfName.Create(baseFont),
            [PdfNames.Flags] = PdfInteger.Get(flags),
            [PdfNames.FontBBox] = PdfArray.FromNumbers(Math.Round(_font.Scale(face.XMin), MidpointRounding.AwayFromZero), Math.Round(_font.Scale(face.YMin), MidpointRounding.AwayFromZero), Math.Round(_font.Scale(face.XMax), MidpointRounding.AwayFromZero), Math.Round(_font.Scale(face.YMax), MidpointRounding.AwayFromZero)),
            [PdfNames.ItalicAngle] = PdfObjectFactory.Number(Math.Round(face.ItalicAngle, 3, MidpointRounding.AwayFromZero)),
            [PdfNames.Ascent] = PdfObjectFactory.Number(Math.Round(_font.Ascent, MidpointRounding.AwayFromZero)),
            [PdfNames.Descent] = PdfObjectFactory.Number(Math.Round(_font.Descent, MidpointRounding.AwayFromZero)),
            [PdfNames.CapHeight] = PdfObjectFactory.Number(Math.Round(_font.CapHeight, MidpointRounding.AwayFromZero)),

            // The dominant vertical stem width is not in the font: an estimate from the weight
            [PdfNames.StemV] = PdfInteger.Get(50 + (int)Math.Round(Math.Pow(Math.Clamp(face.Weight, 1, 1000) / 65d, 2), MidpointRounding.AwayFromZero)),
            [isCff ? PdfNames.FontFile3 : PdfNames.FontFile2] = _fontFile,
        };
        _descriptor = Set(document, _descriptor, descriptor);

        var cidFont = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Font,
            [PdfNames.Subtype] = isCff ? PdfNames.CIDFontType0 : PdfNames.CIDFontType2,
            [PdfNames.BaseFont] = PdfName.Create(baseFont),
            [PdfNames.CIDSystemInfo] = new PdfDictionary
            {
                [PdfNames.Registry] = PdfString.FromText("Adobe"),
                [PdfNames.Ordering] = PdfString.FromText("Identity"),
                [PdfNames.Supplement] = PdfInteger.Get(0),
            },
            [PdfNames.FontDescriptor] = _descriptor,
            [PdfNames.W] = CreateWidths(),
        };
        if (!isCff)
        {
            cidFont[PdfNames.CIDToGIDMap] = PdfNames.Identity;
        }

        _cidFont = Set(document, _cidFont, cidFont);

        var toUnicode = new PdfStream();
        toUnicode.SetData(CreateToUnicode());
        _toUnicode = Set(document, _toUnicode, toUnicode);

        document.Objects.Set(Reference, new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Font,
            [PdfNames.Subtype] = PdfNames.Type0,
            [PdfNames.BaseFont] = PdfName.Create(baseFont),
            [PdfNames.Encoding] = PdfNames.IdentityH,
            [PdfNames.DescendantFonts] = new PdfArray(_cidFont),
            [PdfNames.ToUnicode] = _toUnicode,
        });
    }

    private static PdfReference Set(PdfDocument document, PdfReference? reference, PdfObject value)
    {
        if (reference is null)
            return document.Objects.Add(value);

        document.Objects.Set(reference, value);
        return reference;
    }

    /// <summary>Creates the tag of the name of a subset (9.6.4): six uppercase letters, from the glyphs of the subset.</summary>
    private string CreateSubsetTag()
    {
        var data = new byte[(_glyphs.Count * 4) + Encoding.UTF8.GetByteCount(_font.PostScriptName)];
        var position = 0;
        foreach (var glyph in _glyphs)
        {
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(position), glyph);
            position += 4;
        }

        Encoding.UTF8.GetBytes(_font.PostScriptName, data.AsSpan(position));
        var hash = SHA256.HashData(data);
        var tag = new char[6];
        for (var i = 0; i < tag.Length; i++)
        {
            tag[i] = (char)('A' + (hash[i] % 26));
        }

        return new string(tag);
    }

    /// <summary>Creates the widths of the glyphs (9.7.4.3): <c>[first [w1 w2 ...] ...]</c> for each range of consecutive glyphs.</summary>
    private PdfArray CreateWidths()
    {
        var result = new PdfArray();
        PdfArray? range = null;
        var previous = -2;
        foreach (var glyph in _glyphs)
        {
            if (range is null || glyph != previous + 1)
            {
                result.Add(PdfInteger.Get(glyph));
                range = new PdfArray();
                result.Add(range);
            }

            range.Add(PdfObjectFactory.Number(_font.GetWidth(glyph)));
            previous = glyph;
        }

        return result;
    }

    /// <summary>Creates the <c>ToUnicode</c> CMap of the glyphs whose text is known (9.10.3).</summary>
    private byte[] CreateToUnicode()
    {
        var builder = new StringBuilder();
        builder.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n");
        builder.Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n");
        builder.Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n");
        builder.Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
        var entries = _glyphs.Where(_texts.ContainsKey).ToArray();
        for (var start = 0; start < entries.Length; start += MaxEntriesPerSection)
        {
            var count = Math.Min(MaxEntriesPerSection, entries.Length - start);
            builder.Append(count.ToString(CultureInfo.InvariantCulture)).Append(" beginbfchar\n");
            for (var i = start; i < start + count; i++)
            {
                builder.Append('<').Append(entries[i].ToString("X4", CultureInfo.InvariantCulture)).Append("> <");
                foreach (var c in _texts[entries[i]])
                {
                    builder.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                }

                builder.Append(">\n");
            }

            builder.Append("endbfchar\n");
        }

        builder.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }
}
