using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>A composite font (Type0, ISO 32000-2, 9.7): an encoding CMap maps the codes to CIDs, the descendant CIDFont gives their widths (9.7.4.3).</summary>
/// <remarks>
/// A code is identified by its value and its length (<c>&lt;41&gt;</c> and <c>&lt;0041&gt;</c> are two codes): the code
/// <see cref="ReadCode"/> returns adds to the value the count of the shorter codes (0, 0x100, 0x10100, 0x1010100 for one
/// to four bytes), so that the codes of each length have their own values. A CMap whose codes all have four bytes (UTF-32)
/// keeps the values. A CMap that mixes four-byte codes from 0xFEFEFF00 with shorter codes is the only one whose codes can
/// collide.
/// </remarks>
internal sealed class CompositeFontDecoder : PdfFontDecoder
{
    private const int MaxWidthEntries = 65536;

    private static readonly uint[] LengthOffsets = [0, 0, 0x100, 0x10100, 0x1010100];

    private readonly CMap _encoding;
    private readonly bool _keepsValues;
    private readonly Dictionary<int, double> _widths = [];
    private readonly double _defaultWidth = 1;
    private readonly Dictionary<int, (double Advance, double OriginX, double OriginY)> _verticalMetrics = [];
    private readonly double _defaultVerticalOrigin = 0.88;
    private readonly double _defaultVerticalAdvance = -1;

    public CompositeFontDecoder(PdfDictionary font, CMap? toUnicode)
        : this(font, GetDescendant(font), toUnicode)
    {
    }

    private CompositeFontDecoder(PdfDictionary font, PdfDictionary? cidFont, CMap? toUnicode)
        : base(font, cidFont?.Get<PdfDictionary>(PdfNames.FontDescriptor), toUnicode)
    {
        _encoding = font.GetResolved(PdfNames.Encoding) switch
        {
            PdfName name => CMap.TryGetPredefined(name.Value) ?? CMap.Identity(vertical: name.Value.EndsWith("-V", StringComparison.Ordinal)),
            PdfStream stream => TryParse(stream),
            _ => CMap.Identity(vertical: false),
        };
        _keepsValues = _encoding.HasOnlyCodesOfLength(4);

        if (cidFont is not null)
        {
            _defaultWidth = cidFont.GetNumber(PdfNames.DW, 1000) / 1000;
            if (cidFont.Get<PdfArray>(PdfNames.W) is { } widths)
            {
                ReadWidths(widths);
            }

            // The default vertical metrics: the position vector's vertical component and the displacement (9.7.4.3)
            if (cidFont.Get<PdfArray>(PdfNames.DW2) is { Count: >= 2 } defaultVertical)
            {
                _defaultVerticalOrigin = defaultVertical.GetNumber(0, 880) / 1000;
                _defaultVerticalAdvance = defaultVertical.GetNumber(1, -1000) / 1000;
            }

            if (cidFont.Get<PdfArray>(PdfNames.W2) is { } verticalMetrics)
            {
                ReadVerticalMetrics(verticalMetrics);
            }
        }
    }

    public override bool IsVertical => _encoding.IsVertical;

    public override (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data)
    {
        var (value, length) = _encoding.ReadCode(data);
        return (_keepsValues ? value : unchecked(value + LengthOffsets[length]), length);
    }

    /// <summary>Gets the CID of a code (9.7.6); 0 when the encoding does not map it.</summary>
    public int GetCid(uint code)
    {
        var (value, length) = Split(code);
        return _encoding.GetCid(value, length) ?? 0;
    }

    public override double GetWidth(uint code)
    {
        var (value, length) = Split(code);
        return _encoding.GetCid(value, length) is { } cid && _widths.TryGetValue(cid, out var width) ? width : _defaultWidth;
    }

    public override (double Advance, double OriginX, double OriginY) GetVerticalMetrics(uint code)
    {
        var (value, length) = Split(code);
        var cid = _encoding.GetCid(value, length);
        if (cid is not null && _verticalMetrics.TryGetValue(cid.Value, out var metrics))
            return metrics;

        // The horizontal component of the default position vector is half the width of the glyph
        var width = cid is not null && _widths.TryGetValue(cid.Value, out var w) ? w : _defaultWidth;
        return (_defaultVerticalAdvance, width / 2, _defaultVerticalOrigin);
    }

    /// <summary>Gets the text of a code: its ToUnicode mapping, else the character encoding of a predefined CMap (9.10.2).</summary>
    public override string? GetText(uint code)
    {
        var (value, length) = Split(code);
        return ToUnicode?.GetUnicode(value, length) ?? _encoding.GetCharsetText(value, length);
    }

    private (uint Value, int Length) Split(uint code)
    {
        if (_keepsValues)
            return (code, 4);

        return code switch
        {
            < 0x100 => (code, 1),
            < 0x10100 => (code - 0x100, 2),
            < 0x1010100 => (code - 0x10100, 3),
            _ => (code - 0x1010100, 4),
        };
    }

    private static PdfDictionary? GetDescendant(PdfDictionary font)
    {
        return font.Get<PdfArray>(PdfNames.DescendantFonts) is { Count: > 0 } descendants && descendants.GetResolved(0) is PdfDictionary cidFont ? cidFont : null;
    }

    private static CMap TryParse(PdfStream stream)
    {
        try
        {
            return CMap.Parse(stream);
        }
        catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
        {
            return CMap.Identity(vertical: false);
        }
    }

    /// <summary>Reads the /W array: <c>c [w1 w2 ...]</c> or <c>cFirst cLast w</c> (9.7.4.3).</summary>
    private void ReadWidths(PdfArray widths)
    {
        // A budget of assignments: repeated ranges would otherwise cost the square of the size of the array
        var budget = MaxWidthEntries * 4;
        var i = 0;
        while (i < widths.Count && _widths.Count < MaxWidthEntries && budget > 0)
        {
            if (!widths.TryGetResolved<PdfNumber>(i, out var first))
                break;

            if (widths.TryGetResolved<PdfArray>(i + 1, out var list))
            {
                for (var j = 0; j < list.Count && _widths.Count < MaxWidthEntries && budget-- > 0; j++)
                {
                    _widths[(int)Math.Clamp(first.DoubleValue + j, 0, int.MaxValue)] = list.GetNumber(j) / 1000;
                }

                i += 2;
            }
            else if (widths.TryGetResolved<PdfNumber>(i + 1, out var last) && widths.TryGetResolved<PdfNumber>(i + 2, out var width))
            {
                var start = (int)Math.Clamp(first.DoubleValue, 0, int.MaxValue - MaxWidthEntries);
                var end = (int)Math.Clamp(last.DoubleValue, 0, start + MaxWidthEntries);
                for (var cid = start; cid <= end && _widths.Count < MaxWidthEntries && budget-- > 0; cid++)
                {
                    _widths[cid] = width.DoubleValue / 1000;
                }

                i += 3;
            }
            else
            {
                break;
            }
        }
    }

    /// <summary>Reads the /W2 array: <c>c [w1y vx vy w1y vx vy ...]</c> or <c>cFirst cLast w1y vx vy</c> (9.7.4.3).</summary>
    private void ReadVerticalMetrics(PdfArray metrics)
    {
        var budget = MaxWidthEntries * 4;
        var i = 0;
        while (i < metrics.Count && _verticalMetrics.Count < MaxWidthEntries && budget > 0)
        {
            if (!metrics.TryGetResolved<PdfNumber>(i, out var first))
                break;

            if (metrics.TryGetResolved<PdfArray>(i + 1, out var list))
            {
                for (var j = 0; j + 2 < list.Count && _verticalMetrics.Count < MaxWidthEntries && budget-- > 0; j += 3)
                {
                    var cid = (int)Math.Clamp(first.DoubleValue + (j / 3), 0, int.MaxValue);
                    _verticalMetrics[cid] = (list.GetNumber(j) / 1000, list.GetNumber(j + 1) / 1000, list.GetNumber(j + 2) / 1000);
                }

                i += 2;
            }
            else if (metrics.TryGetResolved<PdfNumber>(i + 1, out var last) && metrics.TryGetResolved<PdfNumber>(i + 2, out var advance)
                && metrics.TryGetResolved<PdfNumber>(i + 3, out var originX) && metrics.TryGetResolved<PdfNumber>(i + 4, out var originY))
            {
                var start = (int)Math.Clamp(first.DoubleValue, 0, int.MaxValue - MaxWidthEntries);
                var end = (int)Math.Clamp(last.DoubleValue, 0, start + MaxWidthEntries);
                var value = (advance.DoubleValue / 1000, originX.DoubleValue / 1000, originY.DoubleValue / 1000);
                for (var cid = start; cid <= end && _verticalMetrics.Count < MaxWidthEntries && budget-- > 0; cid++)
                {
                    _verticalMetrics[cid] = value;
                }

                i += 5;
            }
            else
            {
                break;
            }
        }
    }
}
