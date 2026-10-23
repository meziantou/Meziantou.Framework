using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// A CMap (ISO 32000-2, 9.7.5 and 9.10.3; Adobe Technical Note #5014 and #5099): the code space ranges that split a
/// string into codes, and the mappings of the codes to CIDs (an encoding CMap) or to Unicode text (a ToUnicode CMap).
/// </summary>
internal sealed class CMap
{
    private const int MaxRangeEntries = 65536;
    private const int MaxUseCMapDepth = 8;

    private readonly List<(int Length, uint Low, uint High)> _codeSpaces = [];
    private readonly Dictionary<uint, string> _unicode = [];
    private readonly List<(uint Low, uint High, string Start)> _unicodeRanges = [];
    private readonly Dictionary<uint, int> _cids = [];
    private readonly List<(uint Low, uint High, int Start)> _cidRanges = [];

    private CMap()
    {
    }

    /// <summary>Gets a value indicating whether the CMap is <c>Identity-H</c> or <c>Identity-V</c>: two-byte codes that are their own CIDs.</summary>
    public bool IsIdentity { get; private init; }

    public bool IsVertical { get; private set; }

    public static CMap Identity(bool vertical) => new() { IsIdentity = true, IsVertical = vertical, _codeSpaces = { (2, 0, 0xFFFF) } };

    public static CMap Parse(PdfStream stream, int depth = 0)
    {
        var cmap = new CMap();
        cmap.ParseInto(stream.GetDecodedData(), depth);
        if (stream.Dictionary.GetResolved(PdfName.Create("UseCMap")) is PdfStream parent && depth < MaxUseCMapDepth)
        {
            cmap.Merge(Parse(parent, depth + 1));
        }

        if (stream.Dictionary.GetInteger(PdfName.Create("WMode"), 0) == 1)
        {
            cmap.IsVertical = true;
        }

        return cmap;
    }

    /// <summary>Reads the next code of a string: the code space range it matches gives its length; a string that matches none is read one byte at a time.</summary>
    public (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data)
    {
        uint value = 0;
        for (var length = 1; length <= Math.Min(4, data.Length); length++)
        {
            value = (value << 8) | data[length - 1];
            foreach (var (rangeLength, low, high) in _codeSpaces)
            {
                if (rangeLength == length && value >= low && value <= high)
                    return (value, length);
            }
        }

        // No code space range: the shortest code of the CMap, or one byte (9.7.6.3)
        var shortest = _codeSpaces.Count > 0 ? Math.Min(_codeSpaces.Min(c => c.Length), data.Length) : 1;
        value = 0;
        for (var i = 0; i < shortest; i++)
        {
            value = (value << 8) | data[i];
        }

        return (value, Math.Max(1, shortest));
    }

    public string? GetUnicode(uint code)
    {
        if (_unicode.TryGetValue(code, out var text))
            return text;

        foreach (var (low, high, start) in _unicodeRanges)
        {
            if (code >= low && code <= high)
            {
                // The last character of the destination string is incremented (9.10.3)
                var offset = (int)(code - low);
                if (start.Length == 0)
                    return null;

                var last = start[^1] + offset;
                if (last > 0xFFFF)
                    return null;

                return string.Concat(start.AsSpan(0, start.Length - 1), ((char)last).ToString());
            }
        }

        return null;
    }

    public int GetCid(uint code)
    {
        if (IsIdentity)
            return (int)code;

        if (_cids.TryGetValue(code, out var cid))
            return cid;

        foreach (var (low, high, start) in _cidRanges)
        {
            if (code >= low && code <= high)
                return start + (int)(code - low);
        }

        return 0;
    }

    private void Merge(CMap parent)
    {
        _codeSpaces.AddRange(parent._codeSpaces);
        foreach (var (key, value) in parent._unicode)
        {
            _unicode.TryAdd(key, value);
        }

        _unicodeRanges.AddRange(parent._unicodeRanges);
        foreach (var (key, value) in parent._cids)
        {
            _cids.TryAdd(key, value);
        }

        _cidRanges.AddRange(parent._cidRanges);
        IsVertical |= parent.IsVertical;
    }

    private void ParseInto(byte[] data, int depth)
    {
        var lexer = new PdfLexer(data);
        var parser = new PdfObjectParser(lexer, objects: null, maxDepth: 32);
        var operands = new List<PdfObject>();
        var entries = 0;
        while (true)
        {
            var token = lexer.Next();
            if (token.Kind == PdfTokenKind.EndOfInput)
                break;

            if (token.Kind != PdfTokenKind.Keyword)
            {
                try
                {
                    if (parser.ParseValue(token, depth: 0) is { } value)
                    {
                        operands.Add(value);
                    }
                }
                catch (InvalidPdfContentException)
                {
                    break;
                }

                continue;
            }

            var keyword = Encoding.ASCII.GetString(lexer.GetBytes(token));
            switch (keyword)
            {
                case "begincodespacerange":
                case "beginbfchar":
                case "beginbfrange":
                case "begincidchar":
                case "begincidrange":
                    operands.Clear();
                    break;
                case "endcodespacerange":
                    for (var i = 0; i + 1 < operands.Count; i += 2)
                    {
                        if (operands[i] is PdfString low && operands[i + 1] is PdfString high && low.Span.Length is >= 1 and <= 4)
                        {
                            _codeSpaces.Add((low.Span.Length, ToCode(low.Span), ToCode(high.Span)));
                        }
                    }

                    operands.Clear();
                    break;
                case "endbfchar":
                    for (var i = 0; i + 1 < operands.Count && entries < MaxRangeEntries; i += 2, entries++)
                    {
                        if (operands[i] is PdfString code)
                        {
                            var text = operands[i + 1] switch
                            {
                                PdfString destination => DecodeUtf16(destination.Span),
                                PdfName name => Fonts.GlyphList.ToUnicode(name.Value),
                                _ => null,
                            };
                            if (text is not null)
                            {
                                _unicode[ToCode(code.Span)] = text;
                            }
                        }
                    }

                    operands.Clear();
                    break;
                case "endbfrange":
                    for (var i = 0; i + 2 < operands.Count && entries < MaxRangeEntries; i += 3, entries++)
                    {
                        if (operands[i] is not PdfString lowString || operands[i + 1] is not PdfString highString)
                            continue;

                        var low = ToCode(lowString.Span);
                        var high = ToCode(highString.Span);
                        if (high < low)
                            continue;

                        switch (operands[i + 2])
                        {
                            case PdfString start:
                                _unicodeRanges.Add((low, high, DecodeUtf16(start.Span)));
                                break;
                            case PdfArray array:
                                for (var j = 0; j < array.Count && low + (uint)j <= high; j++)
                                {
                                    if (array[j] is PdfString item)
                                    {
                                        _unicode[low + (uint)j] = DecodeUtf16(item.Span);
                                    }
                                }

                                break;
                        }
                    }

                    operands.Clear();
                    break;
                case "endcidchar":
                    for (var i = 0; i + 1 < operands.Count && entries < MaxRangeEntries; i += 2, entries++)
                    {
                        if (operands[i] is PdfString code && operands[i + 1] is PdfInteger cid)
                        {
                            _cids[ToCode(code.Span)] = (int)Math.Clamp(cid.Value, 0, ushort.MaxValue);
                        }
                    }

                    operands.Clear();
                    break;
                case "endcidrange":
                    for (var i = 0; i + 2 < operands.Count && entries < MaxRangeEntries; i += 3, entries++)
                    {
                        if (operands[i] is PdfString low && operands[i + 1] is PdfString high && operands[i + 2] is PdfInteger start)
                        {
                            _cidRanges.Add((ToCode(low.Span), ToCode(high.Span), (int)Math.Clamp(start.Value, 0, ushort.MaxValue)));
                        }
                    }

                    operands.Clear();
                    break;
                case "usecmap":
                    if (operands.Count > 0 && operands[^1] is PdfName parentName && depth < MaxUseCMapDepth && TryGetPredefined(parentName.Value) is { } predefined)
                    {
                        Merge(predefined);
                    }

                    operands.Clear();
                    break;
                default:
                    operands.Clear();
                    break;
            }
        }
    }

    /// <summary>Gets the predefined CMaps the library knows: the identity CMaps (9.7.5.2).</summary>
    public static CMap? TryGetPredefined(string name)
    {
        return name switch
        {
            "Identity-H" => Identity(vertical: false),
            "Identity-V" => Identity(vertical: true),
            _ => null,
        };
    }

    private static uint ToCode(ReadOnlySpan<byte> bytes)
    {
        uint value = 0;
        foreach (var b in bytes[..Math.Min(4, bytes.Length)])
        {
            value = (value << 8) | b;
        }

        return value;
    }

    private static string DecodeUtf16(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 1)
            return ((char)bytes[0]).ToString();

        return Encoding.BigEndianUnicode.GetString(bytes[..(bytes.Length & ~1)]);
    }
}
