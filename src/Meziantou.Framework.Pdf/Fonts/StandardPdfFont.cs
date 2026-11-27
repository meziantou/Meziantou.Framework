using System.Buffers;
using System.Globalization;
using System.Text;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Fonts;

/// <summary>
/// A standard 14 font: a <c>Type1</c> font that is not embedded. The Latin fonts use <c>WinAnsiEncoding</c>; Symbol and
/// ZapfDingbats use their built-in encodings, whose glyphs are found from the characters through the Adobe Glyph List.
/// </summary>
internal sealed class StandardPdfFont : PdfFont
{
    private readonly Lazy<AfmFontMetrics> _metrics;
    private readonly Lazy<FontEncoding> _encoding;

    public StandardPdfFont(string name)
    {
        PostScriptName = name;
        _metrics = new Lazy<AfmFontMetrics>(() => StandardFontMetrics.Get(name));
        _encoding = new Lazy<FontEncoding>(CreateEncoding);
    }

    public override string PostScriptName { get; }

    public override double Ascent => StandardFontMetrics.GetAscender(_metrics.Value);

    public override double Descent => StandardFontMetrics.GetDescender(_metrics.Value);

    public override double CapHeight => StandardFontMetrics.GetCapHeight(_metrics.Value);

    /// <summary>Gets a value indicating whether the font uses its built-in encoding (Symbol, ZapfDingbats).</summary>
    public bool IsSymbolic => StandardFontMetrics.IsFontSpecific(_metrics.Value);

    public override bool CanEncode(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var encoding = _encoding.Value;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!encoding.TryGetCode(rune.Value, out _))
                return false;
        }

        return true;
    }

    internal override byte[] Encode(string text)
    {
        // One byte per character: the array is the length of the text, shortened when it has surrogate pairs
        var result = new byte[text.Length];
        var length = Encode(text, result);
        return length == result.Length ? result : result.AsSpan(0, length).ToArray();
    }

    internal override void WriteString(PdfByteWriter writer, string text)
    {
        const int MaxStackLength = 256;
        byte[]? rented = null;
        Span<byte> buffer = text.Length <= MaxStackLength ? stackalloc byte[MaxStackLength] : (rented = ArrayPool<byte>.Shared.Rent(text.Length));
        try
        {
            var length = Encode(text, buffer);
            PdfObjectSerializer.WriteString(writer, buffer[..length], hexadecimal: false);
        }
        finally
        {
            if (rented is not null)
            {
                ArrayPool<byte>.Shared.Return(rented);
            }
        }
    }

    internal override void ValidateText(string text)
    {
        var encoding = _encoding.Value;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!encoding.TryGetCode(rune.Value, out _))
                throw CreateMissingGlyphException(rune, nameof(text));
        }
    }

    /// <summary>Encodes a text into a buffer at least as long as the text; returns the count of bytes.</summary>
    private int Encode(string text, Span<byte> destination)
    {
        var encoding = _encoding.Value;
        var length = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!encoding.TryGetCode(rune.Value, out var code))
                throw CreateMissingGlyphException(rune, nameof(text));

            destination[length++] = code;
        }

        return length;
    }

    private ArgumentException CreateMissingGlyphException(Rune rune, string parameterName) => new(string.Create(CultureInfo.InvariantCulture, $"The character U+{rune.Value:X4} has no glyph in the standard font {PostScriptName}"), parameterName);

    private protected override double GetTextWidthInGlyphUnits(string text)
    {
        var encoding = _encoding.Value;
        double width = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            if (!encoding.TryGetCode(rune.Value, out var code))
                throw CreateMissingGlyphException(rune, nameof(text));

            width += encoding.Widths[code];
        }

        return width;
    }

    internal override PdfReference GetReference(PdfDocument document)
    {
        if (document.SharedResources.TryGetValue(this, out var reference))
            return reference;

        var dictionary = new PdfDictionary
        {
            [PdfNames.Type] = PdfNames.Font,
            [PdfNames.Subtype] = PdfNames.Type1,
            [PdfNames.BaseFont] = PdfName.Create(PostScriptName),
        };
        if (!IsSymbolic)
        {
            dictionary[PdfNames.Encoding] = PdfNames.WinAnsiEncoding;
        }

        reference = document.Objects.Add(dictionary);
        document.SharedResources[this] = reference;
        return reference;
    }

    private FontEncoding CreateEncoding()
    {
        var metrics = _metrics.Value;
        var names = IsSymbolic ? (PostScriptName == "Symbol" ? FontEncodings.Symbol : FontEncodings.ZapfDingbats) : FontEncodings.WinAnsi;
        var isZapfDingbats = PostScriptName == "ZapfDingbats";
        var codes = new Dictionary<int, byte>();
        var widths = new double[256];
        for (var code = 0; code < 256; code++)
        {
            var name = names[code];
            if (name is null || StandardFontMetrics.GetWidth(metrics, name) is not { } width)
                continue;

            widths[code] = width;
            var text = AdobeGlyphList.ToUnicode(name, isZapfDingbats);
            if (text is not null && text.EnumerateRunes().Count() == 1)
            {
                codes.TryAdd(text.EnumerateRunes().First().Value, (byte)code);
            }
        }

        if (!IsSymbolic)
        {
            // The codes that name the same glyphs as other codes (Annex D): a no-break space and a soft hyphen
            codes.TryAdd(0xA0, 0xA0);
            codes.TryAdd(0xAD, 0xAD);
        }

        return new FontEncoding(codes, widths);
    }

    /// <summary>The codes of the characters, and the widths of the codes.</summary>
    private sealed class FontEncoding
    {
        // The codes of the characters U+0000 to U+00FF, the characters of most texts, without a dictionary lookup: -1 when
        // the font has no glyph for the character
        private readonly short[] _latin1Codes = new short[256];
        private readonly Dictionary<int, byte> _codes;

        public FontEncoding(Dictionary<int, byte> codes, double[] widths)
        {
            _codes = codes;
            Widths = widths;
            for (var i = 0; i < _latin1Codes.Length; i++)
            {
                _latin1Codes[i] = codes.TryGetValue(i, out var code) ? code : (short)-1;
            }
        }

        public double[] Widths { get; }

        public bool TryGetCode(int character, out byte code)
        {
            if ((uint)character < (uint)_latin1Codes.Length)
            {
                var value = _latin1Codes[character];
                code = (byte)value;
                return value >= 0;
            }

            return _codes.TryGetValue(character, out code);
        }
    }
}
