using System.Buffers.Binary;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Rendering.Fonts;

/// <summary>
/// Makes the render font of a font dictionary (ISO 32000-2, 9.6 to 9.9): its embedded program (TrueType, CFF, Type 1)
/// and how a code selects a glyph of it, the glyph procedures of a Type 3 font, or a font that replaces a font the
/// document does not embed.
/// </summary>
internal static class RenderFontFactory
{
    // The flags of a font descriptor (9.8.2)
    private const int FixedPitch = 1;
    private const int Serif = 2;
    private const int Symbolic = 4;
    private const int Italic = 64;
    private const int ForceBold = 1 << 18;

    public static RenderFont? Create(PdfDictionary font, RenderCache cache)
    {
        var decoder = PdfFontDecoder.Create(font);
        var subtype = font.GetName(PdfNames.Subtype)?.Value;
        return subtype switch
        {
            "Type3" when decoder is SimpleFontDecoder simple => new Type3RenderFont(decoder, new Type3Glyphs(font, simple)),
            "Type0" when decoder is CompositeFontDecoder composite => CreateComposite(font, composite, cache),
            _ when decoder is SimpleFontDecoder simple => CreateSimple(font, simple, cache),
            _ => null,
        };
    }

    private static RenderFont? CreateComposite(PdfDictionary font, CompositeFontDecoder decoder, RenderCache cache)
    {
        if (font.Get<PdfArray>(PdfNames.DescendantFonts) is not { } descendants || !descendants.TryGetResolved<PdfDictionary>(0, out var descendant))
            return null;

        var descriptor = descendant.Get<PdfDictionary>(PdfNames.FontDescriptor);
        var program = LoadProgram(descriptor, cache);
        switch (program)
        {
            case TrueTypeFontProgram trueType:
                {
                    // CIDFontType2: the CID selects a glyph through /CIDToGIDMap, the identity by default (9.7.4.2)
                    var map = descendant.GetResolved(PdfNames.CIDToGIDMap) is PdfStream mapStream ? mapStream.GetDecodedData() : null;
                    return new RenderFont(decoder, trueType.FontMatrix, code =>
                    {
                        var cid = decoder.GetCid(code);
                        var glyph = map is null ? cid : (2 * cid) + 1 < map.Length ? BinaryPrimitives.ReadUInt16BigEndian(map.AsSpan(2 * cid)) : 0;
                        return trueType.GetPath(glyph);
                    });
                }

            case CffFontProgram cff:
                return new RenderFont(decoder, cff.FontMatrix, code => cff.GetPath(cff.GetGlyphByCid(decoder.GetCid(code))));
            case Type1FontProgram:
                return null;
        }

        // Not embedded: the text of the codes in a font of the system
        var face = FontSubstitution.Resolve(CreateRequest(font, descriptor), cache);
        if (face is null)
            return null;

        return CreateSubstitute(decoder, face, code => decoder.GetText(code), cache);
    }

    private static RenderFont? CreateSimple(PdfDictionary font, SimpleFontDecoder decoder, RenderCache cache)
    {
        var descriptor = font.Get<PdfDictionary>(PdfNames.FontDescriptor);
        var flags = (int)(descriptor?.GetInteger(PdfNames.Flags, 0) ?? 0);
        var program = LoadProgram(descriptor, cache);
        var names = GetGlyphNames(font, decoder, program);
        switch (program)
        {
            case Type1FontProgram type1:
                return new RenderFont(decoder, type1.FontMatrix, code => names(code) is { } name ? type1.GetPath(name, cache) ?? type1.GetPath(".notdef", cache) : null);

            case CffFontProgram cff:
                return new RenderFont(decoder, cff.FontMatrix, code =>
                {
                    var glyph = names(code) is { } name ? cff.GetGlyphByName(name) : 0;
                    if (glyph == 0 && code < 256)
                    {
                        glyph = cff.GetGlyphByBuiltinCode((int)code);
                    }

                    return cff.GetPath(glyph);
                });

            case TrueTypeFontProgram trueType:
                return new RenderFont(decoder, trueType.FontMatrix, code => trueType.GetPath(GetTrueTypeGlyph(trueType, code, names(code), font, flags)));
        }

        // Not embedded: a font of the system, the glyphs found by the characters of their names
        var request = CreateRequest(font, descriptor);
        var face = FontSubstitution.Resolve(request, cache);
        if (face is null)
            return null;

        var isZapfDingbats = request.StandardName == "ZapfDingbats";
        return CreateSubstitute(decoder, face, code => names(code) is { } name ? GlyphList.ToUnicode(name, isZapfDingbats) : decoder.GetText(code) ?? (code < 256 ? ((char)code).ToString() : null), cache);
    }

    /// <summary>Selects the glyph of a code of a simple TrueType font (9.6.5.4): through the (3,1) subtable by the character of the glyph name, or through the (3,0) and (1,0) subtables by the code.</summary>
    private static int GetTrueTypeGlyph(TrueTypeFontProgram program, uint code, string? name, PdfDictionary font, int flags)
    {
        var hasEncoding = font.GetResolved(PdfNames.Encoding) is not null;
        var isSymbolic = (flags & Symbolic) != 0;
        if (name is not null && (hasEncoding || !isSymbolic))
        {
            if (program.HasCmap(3, 1) && GlyphList.ToUnicode(name) is { Length: > 0 } text)
            {
                var glyph = program.MapCharacter(3, 1, char.ConvertToUtf32(text, 0));
                if (glyph != 0)
                    return glyph;
            }

            if (program.HasCmap(1, 0) && FontEncodings.MacRoman.AsSpan().IndexOf(name) is >= 0 and var macCode)
            {
                var glyph = program.MapCharacter(1, 0, macCode);
                if (glyph != 0)
                    return glyph;
            }

            if (program.GetGlyphByPostName(name) is not 0 and var postGlyph)
                return postGlyph;
        }

        // A symbolic font: the code in the (3,0) subtable, which may hold it at 0xF000 + code, or in the (1,0) subtable
        foreach (var character in new[] { (int)code, 0xF000 + (int)code, 0xF100 + (int)code, 0xF200 + (int)code })
        {
            var glyph = program.MapCharacter(3, 0, character);
            if (glyph != 0)
                return glyph;
        }

        var macGlyph = program.MapCharacter(1, 0, (int)code);
        if (macGlyph != 0)
            return macGlyph;

        return program.MapCharacter(3, 1, (int)code) is not 0 and var unicodeGlyph ? unicodeGlyph : (int)code;
    }

    /// <summary>Gets the glyph names of the codes: the encoding of the font, or the built-in encoding of the program when the font has no base encoding, with the differences (9.6.5).</summary>
    private static Func<uint, string?> GetGlyphNames(PdfDictionary font, SimpleFontDecoder decoder, object? program)
    {
        var encoding = font.GetResolved(PdfNames.Encoding);
        var hasBaseEncoding = encoding is PdfName || (encoding is PdfDictionary dictionary && dictionary.GetResolved(PdfNames.BaseEncoding) is not null);
        if (hasBaseEncoding || program is not (Type1FontProgram or CffFontProgram))
            return decoder.GetGlyphName;

        var differences = new string?[256];
        if (encoding is PdfDictionary encodingDictionary && encodingDictionary.Get<PdfArray>(PdfNames.Differences) is { } array)
        {
            var code = 0;
            foreach (var item in array)
            {
                switch (PdfReference.ResolveIfReference(item))
                {
                    case PdfNumber number:
                        code = (int)number.DoubleValue;
                        break;
                    case PdfName name:
                        if (code is >= 0 and < 256)
                        {
                            differences[code] = name.Value;
                        }

                        code++;
                        break;
                }
            }
        }

        var builtin = program is Type1FontProgram { BuiltinEncoding: { } names } ? names : null;
        return code =>
        {
            if (code >= 256)
                return null;

            if (differences[code] is { } difference)
                return difference;

            if (program is CffFontProgram)
                return null;

            return builtin is not null ? builtin[code] : FontEncodings.Standard[code];
        };
    }

    /// <summary>Loads the embedded font program of a font descriptor (9.9); <see langword="null"/> when there is none or it cannot be read.</summary>
    private static object? LoadProgram(PdfDictionary? descriptor, RenderCache cache)
    {
        if (descriptor is null)
            return null;

        try
        {
            if (descriptor.Get<PdfStream>(PdfNames.FontFile2) is { } fontFile2)
                return TrueTypeFontProgram.Read(fontFile2.GetDecodedData(), cache);

            if (descriptor.Get<PdfStream>(PdfNames.FontFile3) is { } fontFile3)
            {
                var data = fontFile3.GetDecodedData();
                if (fontFile3.Dictionary.GetName(PdfNames.Subtype)?.Value == "OpenType" || (data.Length >= 4 && BinaryPrimitives.ReadUInt32BigEndian(data) is 0x4F54544F or 0x00010000 or 0x74727565))
                {
                    // An OpenType font: its CFF table, or its TrueType outlines
                    var cff = FindTable(data, 0x43464620);
                    return cff is not null ? new CffFontProgram(cff, cache) : TrueTypeFontProgram.Read(data, cache);
                }

                return new CffFontProgram(data, cache);
            }

            if (descriptor.Get<PdfStream>(PdfNames.FontFile) is { } fontFile)
                return Type1FontProgram.Read(fontFile.GetDecodedData(), fontFile.Dictionary.GetInteger(PdfNames.Length2, 0));
        }
        catch (Exception ex) when (RenderCache.IsContentError(ex) || ex is ArgumentException)
        {
            // A program that cannot be read: the font is replaced as if it were not embedded
        }

        return null;
    }

    private static byte[]? FindTable(byte[] data, uint tag)
    {
        if (data.Length < 12)
            return null;

        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var record = 12 + (i * 16);
            if (record + 16 > data.Length)
                break;

            if (BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record)) == tag)
            {
                var offset = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 8));
                var length = BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(record + 12));
                if (offset + (long)length <= data.Length)
                    return data.AsSpan((int)offset, (int)length).ToArray();
            }
        }

        return null;
    }

    private static PdfFontRequest CreateRequest(PdfDictionary font, PdfDictionary? descriptor)
    {
        var baseFont = font.GetName(PdfNames.BaseFont)?.Value ?? "";
        var name = baseFont.Length > 7 && baseFont[6] == '+' ? baseFont[7..] : baseFont;
        var standardName = StandardFontMetrics.GetStandardName(baseFont);
        var flags = descriptor?.GetInteger(PdfNames.Flags, 0) ?? 0;
        var weight = descriptor?.GetNumber(PdfName.Create("FontWeight"), 0) ?? 0;
        var isBold = weight >= 600 || (flags & ForceBold) != 0 || name.Contains("Bold", StringComparison.OrdinalIgnoreCase) || name.Contains("Black", StringComparison.OrdinalIgnoreCase) || name.Contains("Heavy", StringComparison.OrdinalIgnoreCase);
        var isItalic = (flags & Italic) != 0 || name.Contains("Italic", StringComparison.OrdinalIgnoreCase) || name.Contains("Oblique", StringComparison.OrdinalIgnoreCase);
        var isFixedPitch = (flags & FixedPitch) != 0 || standardName?.StartsWith("Courier", StringComparison.Ordinal) == true;
        var isSerif = (flags & Serif) != 0 || standardName?.StartsWith("Times", StringComparison.Ordinal) == true;
        var isSymbolic = standardName is "Symbol" or "ZapfDingbats";
        return new PdfFontRequest(name, standardName, isBold, isItalic, isFixedPitch, isSerif, isSymbolic);
    }

    /// <summary>A font that draws the characters of the codes with the glyphs of a font of the system, through its character map.</summary>
    private static RenderFont CreateSubstitute(PdfFontDecoder decoder, FontFace face, Func<uint, string?> text, RenderCache cache)
    {
        var scale = 1.0 / face.UnitsPerEm;
        var scratch = cache.CreateGlyphScratch(face.Data.Environment, "PDF substituted font");
        return new RenderFont(decoder, new PdfMatrix(scale, 0, 0, scale, 0, 0), code =>
        {
            if (text(code) is not { Length: > 0 } characters)
                return null;

            var glyph = face.MapCodePoint(char.ConvertToUtf32(characters, 0));
            if (glyph == 0)
                return null;

            var sink = new GlyphPathSink();
            var budget = new GlyphBudget(face.Data.Environment.Limits);
            face.DecodeOutline(glyph, sink, scratch, ref budget);
            return sink.ToPath();
        });
    }
}
