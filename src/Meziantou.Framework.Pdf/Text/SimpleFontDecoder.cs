using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>A simple font (Type1, MMType1, TrueType, Type3, ISO 32000-2, 9.6): one byte per code, an encoding of glyph names (9.6.5).</summary>
internal sealed class SimpleFontDecoder : PdfFontDecoder
{
    private readonly string?[] _glyphNames = new string?[256];
    private readonly double[] _widths = new double[256];
    private readonly bool _isZapfDingbats;
    private readonly bool _isSymbolicWithoutEncoding;

    public SimpleFontDecoder(PdfDictionary font, CMap? toUnicode)
        : base(toUnicode)
    {
        var subtype = font.GetName(PdfNames.Subtype);
        var baseFont = font.GetName(PdfNames.BaseFont)?.Value ?? "";
        var standardName = StandardFontMetrics.GetStandardName(baseFont);
        _isZapfDingbats = standardName == "ZapfDingbats";
        var descriptor = font.Get<PdfDictionary>(PdfNames.FontDescriptor);
        var flags = descriptor?.GetInteger(PdfNames.Flags, 0) ?? 0;
        var isSymbolic = (flags & 4) != 0 && (flags & 32) == 0;

        // The base encoding: the /Encoding name, the /BaseEncoding of an encoding dictionary, else the built-in encoding of the font
        string?[] baseEncoding;
        var encoding = font.GetResolved(PdfNames.Encoding);
        var encodingName = encoding switch
        {
            PdfName name => name.Value,
            PdfDictionary dictionary => dictionary.GetName(PdfNames.BaseEncoding)?.Value,
            _ => null,
        };
        if (encodingName is not null && FontEncodings.GetByName(encodingName) is { } named)
        {
            baseEncoding = named;
        }
        else if (standardName == "Symbol")
        {
            baseEncoding = FontEncodings.Symbol;
        }
        else if (_isZapfDingbats)
        {
            baseEncoding = FontEncodings.ZapfDingbats;
        }
        else if (subtype == PdfNames.TrueType && isSymbolic)
        {
            // A symbolic TrueType font without encoding: the codes are the characters of its (3,0) or (1,0) cmap
            baseEncoding = new string?[256];
            _isSymbolicWithoutEncoding = encoding is null;
        }
        else
        {
            baseEncoding = FontEncodings.Standard;
        }

        baseEncoding.CopyTo(_glyphNames, 0);
        if (encoding is PdfDictionary encodingDictionary && encodingDictionary.Get<PdfArray>(PdfNames.Differences) is { } differences)
        {
            var code = 0;
            foreach (var item in differences)
            {
                switch (PdfReference.ResolveIfReference(item))
                {
                    case PdfNumber number:
                        code = (int)number.DoubleValue;
                        break;
                    case PdfName name:
                        if (code is >= 0 and < 256)
                        {
                            _glyphNames[code] = name.Value;
                        }

                        code++;
                        break;
                }
            }
        }

        // Widths: /Widths, else /MissingWidth, else the metrics of the standard font
        var scale = 0.001;
        if (subtype == PdfNames.Type3 && font.Get<PdfArray>(PdfNames.FontMatrix) is { Count: >= 1 } matrix)
        {
            scale = matrix.GetNumber(0, 0.001);
        }

        AfmFontMetrics? metrics = standardName is not null ? StandardFontMetrics.Get(standardName) : null;
        var firstChar = (int)font.GetInteger(PdfNames.FirstChar, 0);
        var widths = font.Get<PdfArray>(PdfNames.Widths);
        var missingWidth = descriptor?.GetNumber(PdfNames.MissingWidth, 0) ?? 0;
        for (var code = 0; code < 256; code++)
        {
            double width;
            var index = code - firstChar;
            if (widths is not null && index >= 0 && index < widths.Count && widths.TryGetResolved<PdfNumber>(index, out var value))
            {
                width = value.DoubleValue;
            }
            else if (metrics is not null && _glyphNames[code] is { } glyphName && metrics.Glyphs.TryGetValue(glyphName, out var glyph))
            {
                width = glyph.Width;
            }
            else
            {
                width = missingWidth;
            }

            _widths[code] = width * scale;
        }
    }

    public override (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data) => (data[0], 1);

    public override double GetWidth(uint code) => code < 256 ? _widths[code] : 0;

    public override string? GetText(uint code)
    {
        if (ToUnicode?.GetUnicode(code) is { } text)
            return text;

        if (code >= 256)
            return null;

        if (_glyphNames[code] is { } name)
        {
            if (GlyphList.ToUnicode(name, _isZapfDingbats) is { } fromName)
                return fromName;

            // Glyph names such as "g42" or "cid123" say nothing about the character
            return null;
        }

        if (_isSymbolicWithoutEncoding)
            return ((char)code).ToString();

        return null;
    }
}
