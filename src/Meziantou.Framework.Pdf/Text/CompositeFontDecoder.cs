using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>A composite font (Type0, ISO 32000-2, 9.7): an encoding CMap maps the codes to CIDs, the descendant CIDFont gives their widths (9.7.4.3).</summary>
internal sealed class CompositeFontDecoder : PdfFontDecoder
{
    private const int MaxWidthEntries = 65536;

    private readonly CMap _encoding;
    private readonly Dictionary<int, double> _widths = [];
    private readonly double _defaultWidth = 1;

    public CompositeFontDecoder(PdfDictionary font, CMap? toUnicode)
        : base(toUnicode)
    {
        _encoding = font.GetResolved(PdfNames.Encoding) switch
        {
            PdfName name => CMap.TryGetPredefined(name.Value) ?? CMap.Identity(vertical: name.Value.EndsWith("-V", StringComparison.Ordinal)),
            PdfStream stream => TryParse(stream),
            _ => CMap.Identity(vertical: false),
        };

        if (font.Get<PdfArray>(PdfNames.DescendantFonts) is { Count: > 0 } descendants && descendants.GetResolved(0) is PdfDictionary cidFont)
        {
            _defaultWidth = cidFont.GetNumber(PdfNames.DW, 1000) / 1000;
            if (cidFont.Get<PdfArray>(PdfNames.W) is { } widths)
            {
                ReadWidths(widths);
            }
        }
    }

    public override bool IsVertical => _encoding.IsVertical;

    public override (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data) => _encoding.ReadCode(data);

    public override double GetWidth(uint code) => _widths.TryGetValue(_encoding.GetCid(code), out var width) ? width : _defaultWidth;

    public override string? GetText(uint code) => ToUnicode?.GetUnicode(code);

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
}
