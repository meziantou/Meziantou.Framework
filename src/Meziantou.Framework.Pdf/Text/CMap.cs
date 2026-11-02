using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// A CMap (ISO 32000-2, 9.7.5 and 9.10.3; Adobe Technical Note #5014 and #5099): the code space ranges that split a
/// string into codes, and the mappings of the codes to CIDs (an encoding CMap) or to Unicode text (a ToUnicode CMap).
/// </summary>
/// <remarks>
/// A code is its bytes: <c>&lt;41&gt;</c> and <c>&lt;0041&gt;</c> are two codes, so the mappings are looked up with the
/// value and the length of the code. A mapping defined later replaces an earlier one, single codes and ranges alike, and
/// the mappings of the CMap replace those of the CMap it uses (<c>usecmap</c>).
/// </remarks>
internal sealed class CMap
{
    // The entries of a CMap and of the CMaps it uses, together: code space ranges, mappings of single codes, ranges and
    // the items of their arrays. The code space ranges are tried for each code: they have a smaller limit of their own.
    private const int MaxEntries = 65536;
    private const int MaxCodeSpaceRanges = 256;
    private const int MaxUseCMapDepth = 8;

    // The texts of the codes of ranges and of the character set the CMap keeps, so that a code shown many times is
    // decoded once
    private const int MaxCachedTexts = 4096;

    // The mappings of a used CMap rank below those of the CMap that uses it: their order is shifted by this
    private const long UsedCMapOrderShift = 1L << 40;

    private readonly List<(int Length, uint Low, uint High)> _codeSpaces = [];
    private readonly Mappings<string> _unicode = new();
    private readonly Mappings<int> _cids = new();
    private readonly Dictionary<ulong, string?> _texts = [];
    private int _entryCount;
    private long _order;
    private int _shortestCodeLength = 1;
    private int _codeSpaceLengths;
    private bool _usesIdentity;

    private CMap()
    {
    }

    /// <summary>Gets a value indicating whether the CMap is <c>Identity-H</c> or <c>Identity-V</c>: two-byte codes that are their own CIDs.</summary>
    public bool IsIdentity { get; private init; }

    public bool IsVertical { get; private set; }

    /// <summary>Gets the character encoding of the codes of a predefined CMap, from which their text is known without a ToUnicode CMap.</summary>
    public CMapCharset? Charset { get; private set; }

    public static CMap Identity(bool vertical)
    {
        var cmap = new CMap { IsIdentity = true, IsVertical = vertical, _codeSpaces = { (2, 0, 0xFFFF) } };
        cmap.Finish();
        return cmap;
    }

    public static CMap Parse(PdfStream stream, int depth = 0)
    {
        var cmap = new CMap();
        cmap.ParseInto(stream.GetDecodedData(), depth);
        switch (stream.Dictionary.GetResolved(PdfName.Create("UseCMap")))
        {
            case PdfStream parent when depth < MaxUseCMapDepth:
                cmap.Merge(Parse(parent, depth + 1));
                break;
            case PdfName name when TryGetPredefined(name.Value) is { } predefined:
                cmap.Merge(predefined);
                break;
        }

        if (stream.Dictionary.GetInteger(PdfName.Create("WMode"), 0) == 1)
        {
            cmap.IsVertical = true;
        }

        cmap.Finish();
        return cmap;
    }

    /// <summary>Gets a predefined CMap (9.7.5.2, Table 116): its code space ranges, its writing mode and the character encoding of its codes.</summary>
    /// <remarks>
    /// The library does not have the mappings of the codes to CIDs of the predefined CMaps other than <c>Identity-H</c> and
    /// <c>Identity-V</c> (the files of Adobe are several megabytes): the codes are split correctly, their text is decoded
    /// with the character encoding of the CMap, and their CID is unknown (the default width of the font applies).
    /// </remarks>
    public static CMap? TryGetPredefined(string name)
    {
        if (name is "Identity-H" or "Identity-V")
            return Identity(vertical: name[^1] == 'V');

        if (PredefinedCMaps.TryGet(name) is not { } definition)
            return null;

        var cmap = new CMap { IsVertical = definition.IsVertical, Charset = definition.Charset };
        cmap._codeSpaces.AddRange(definition.CodeSpaces);
        cmap.Finish();
        return cmap;
    }

    /// <summary>Reads the next code of a string: the code space range it matches gives its length; a string that matches none is read one byte at a time.</summary>
    /// <remarks>A code matches a code space range when each of its bytes is between the bytes of the bounds at the same position (9.7.6.2).</remarks>
    public (uint Code, int Length) ReadCode(ReadOnlySpan<byte> data)
    {
        uint value = 0;
        for (var length = 1; length <= Math.Min(4, data.Length); length++)
        {
            value = (value << 8) | data[length - 1];
            if ((_codeSpaceLengths & (1 << length)) == 0)
                continue;

            foreach (var (rangeLength, low, high) in _codeSpaces)
            {
                if (rangeLength == length && IsInCodeSpace(value, low, high, length))
                    return (value, length);
            }
        }

        // No code space range: the shortest code of the CMap, or one byte (9.7.6.3)
        var shortest = Math.Min(_shortestCodeLength, data.Length);
        value = 0;
        for (var i = 0; i < shortest; i++)
        {
            value = (value << 8) | data[i];
        }

        return (value, Math.Max(1, shortest));
    }

    /// <summary>Gets the text of a code of a ToUnicode CMap, or <see langword="null"/>.</summary>
    public string? GetUnicode(uint code, int length)
    {
        if (TryGetUnicode(code, length, out var text))
            return text;

        // A CMap whose codes do not have the length of the codes of the font (a ToUnicode CMap written with two-byte
        // codes for a simple font): the codes of another length that have the same value
        if (!HasCodeSpaceOfLength(length))
        {
            for (var other = 1; other <= 4; other++)
            {
                if (other != length && code <= MaxValue(other) && TryGetUnicode(code, other, out text))
                    return text;
            }
        }

        return null;
    }

    /// <summary>Gets the text of a code from the character encoding of the CMap (a predefined CMap), or <see langword="null"/>.</summary>
    public string? GetCharsetText(uint code, int length)
    {
        if (Charset is not { } charset)
            return null;

        var key = Key(code, length);
        if (_texts.TryGetValue(key, out var text))
            return text;

        text = charset.Decode(code, length);
        Cache(key, text);
        return text;
    }

    /// <summary>Gets the CID of a code (9.7.6.2), or <see langword="null"/> when the CMap does not map it.</summary>
    public int? GetCid(uint code, int length)
    {
        if (IsIdentity)
            return (int)(code & 0xFFFF);

        if (TryGetCid(code, length) is { } cid)
            return cid;

        if (!HasCodeSpaceOfLength(length))
        {
            for (var other = 1; other <= 4; other++)
            {
                if (other != length && code <= MaxValue(other) && TryGetCid(code, other) is { } otherCid)
                    return otherCid;
            }
        }

        // A CMap that uses Identity-H or Identity-V: the two-byte codes it does not map are their own CIDs
        if (_usesIdentity && length == 2)
            return (int)code;

        return null;
    }

    private int? TryGetCid(uint code, int length)
    {
        if (!_cids.TryFind(length, code, out var start, out var low))
            return null;

        return (int)Math.Min(start + (long)(code - low), int.MaxValue);
    }

    private bool TryGetUnicode(uint code, int length, out string? text)
    {
        if (!_unicode.TryFind(length, code, out var start, out var low))
        {
            text = null;
            return false;
        }

        if (code == low)
        {
            text = start;
            return true;
        }

        var key = Key(code, length);
        if (!_texts.TryGetValue(key, out text))
        {
            text = GetRangeText(start, code - low);
            Cache(key, text);
        }

        return true;
    }

    /// <summary>
    /// Gets the text of a code of a <c>bfrange</c> whose destination is a string: the destination of the first code, whose
    /// last character is incremented by the offset of the code in the range (9.10.3).
    /// </summary>
    /// <remarks>
    /// The specification increments the last byte of the destination and forbids a range that would carry into the byte
    /// before it. Files have ranges that pass that limit: the last character is incremented instead, a supplementary
    /// character (a surrogate pair) as one character, and a result that is not a Unicode scalar value has no text.
    /// </remarks>
    internal static string? GetRangeText(string start, uint offset)
    {
        if (start.Length == 0)
            return null;

        if (start.Length >= 2 && char.IsSurrogatePair(start[^2], start[^1]))
        {
            var codePoint = char.ConvertToUtf32(start[^2], start[^1]) + (long)offset;
            if (codePoint > 0x10FFFF)
                return null;

            return string.Concat(start.AsSpan(0, start.Length - 2), char.ConvertFromUtf32((int)codePoint));
        }

        var last = start[^1] + (long)offset;
        if (last > 0xFFFF || last is >= 0xD800 and <= 0xDFFF)
            return null;

        return string.Create(start.Length, (start, (char)last), static (span, state) =>
        {
            state.start.AsSpan(0, state.start.Length - 1).CopyTo(span);
            span[^1] = state.Item2;
        });
    }

    private void Cache(ulong key, string? text)
    {
        if (_texts.Count >= MaxCachedTexts)
        {
            _texts.Clear();
        }

        _texts[key] = text;
    }

    /// <summary>Indicates whether every code space range of the CMap has codes of a length.</summary>
    public bool HasOnlyCodesOfLength(int length) => _codeSpaceLengths == 1 << length;

    private bool HasCodeSpaceOfLength(int length) => (_codeSpaceLengths & (1 << length)) != 0;

    private static uint MaxValue(int length) => length >= 4 ? uint.MaxValue : (1u << (8 * length)) - 1;

    private static ulong Key(uint code, int length) => ((ulong)(uint)length << 32) | code;

    private static bool IsInCodeSpace(uint value, uint low, uint high, int length)
    {
        for (var i = 0; i < length; i++)
        {
            var shift = 8 * i;
            var b = (value >> shift) & 0xFF;
            if (b < ((low >> shift) & 0xFF) || b > ((high >> shift) & 0xFF))
                return false;
        }

        return true;
    }

    private void Finish()
    {
        _unicode.Build();
        _cids.Build();
        _shortestCodeLength = _codeSpaces.Count > 0 ? _codeSpaces.Min(c => c.Length) : 1;
        _codeSpaceLengths = 0;
        foreach (var (length, _, _) in _codeSpaces)
        {
            _codeSpaceLengths |= 1 << length;
        }
    }

    private bool TryCharge()
    {
        if (_entryCount >= MaxEntries)
            return false;

        _entryCount++;
        return true;
    }

    private void Merge(CMap parent)
    {
        foreach (var range in parent._codeSpaces)
        {
            if (_codeSpaces.Count < MaxCodeSpaceRanges && TryCharge())
            {
                _codeSpaces.Add(range);
            }
        }

        foreach (var (length, low, high, start, order) in parent._unicode.Entries)
        {
            if (TryCharge())
            {
                _unicode.Add(length, low, high, start, order - UsedCMapOrderShift);
            }
        }

        foreach (var (length, low, high, start, order) in parent._cids.Entries)
        {
            if (TryCharge())
            {
                _cids.Add(length, low, high, start, order - UsedCMapOrderShift);
            }
        }

        IsVertical |= parent.IsVertical;
        Charset ??= parent.Charset;
        _usesIdentity |= parent.IsIdentity || parent._usesIdentity;
    }

    private void ParseInto(byte[] data, int depth)
    {
        var lexer = new PdfLexer(data);
        var parser = new PdfObjectParser(lexer, objects: null, maxDepth: 32);
        var operands = new List<PdfObject>();
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
                        if (operands[i] is PdfString low && operands[i + 1] is PdfString high && low.Span.Length is >= 1 and <= 4 && _codeSpaces.Count < MaxCodeSpaceRanges && TryCharge())
                        {
                            _codeSpaces.Add((low.Span.Length, ToCode(low.Span), ToCode(high.Span)));
                        }
                    }

                    operands.Clear();
                    break;
                case "endbfchar":
                    for (var i = 0; i + 1 < operands.Count && TryCharge(); i += 2)
                    {
                        if (operands[i] is PdfString code && code.Span.Length is >= 1 and <= 4)
                        {
                            var text = operands[i + 1] switch
                            {
                                PdfString destination => DecodeUtf16(destination.Span),
                                PdfName name => Fonts.GlyphList.ToUnicode(name.Value),
                                _ => null,
                            };
                            if (text is not null)
                            {
                                var value = ToCode(code.Span);
                                _unicode.Add(code.Span.Length, value, value, text, _order++);
                            }
                        }
                    }

                    operands.Clear();
                    break;
                case "endbfrange":
                    for (var i = 0; i + 2 < operands.Count && TryCharge(); i += 3)
                    {
                        if (operands[i] is not PdfString lowString || operands[i + 1] is not PdfString highString || lowString.Span.Length is < 1 or > 4)
                            continue;

                        var length = lowString.Span.Length;
                        var low = ToCode(lowString.Span);
                        var high = ToCode(highString.Span);
                        if (high < low)
                            continue;

                        switch (operands[i + 2])
                        {
                            case PdfString start:
                                _unicode.Add(length, low, high, DecodeUtf16(start.Span), _order++);
                                break;
                            case PdfArray array:
                                for (var j = 0; j < array.Count && low + (uint)j <= high && TryCharge(); j++)
                                {
                                    if (array[j] is PdfString item)
                                    {
                                        _unicode.Add(length, low + (uint)j, low + (uint)j, DecodeUtf16(item.Span), _order++);
                                    }
                                }

                                break;
                        }
                    }

                    operands.Clear();
                    break;
                case "endcidchar":
                    for (var i = 0; i + 1 < operands.Count && TryCharge(); i += 2)
                    {
                        if (operands[i] is PdfString code && code.Span.Length is >= 1 and <= 4 && operands[i + 1] is PdfInteger cid)
                        {
                            var value = ToCode(code.Span);
                            _cids.Add(code.Span.Length, value, value, (int)Math.Clamp(cid.Value, 0, ushort.MaxValue), _order++);
                        }
                    }

                    operands.Clear();
                    break;
                case "endcidrange":
                    for (var i = 0; i + 2 < operands.Count && TryCharge(); i += 3)
                    {
                        if (operands[i] is PdfString low && low.Span.Length is >= 1 and <= 4 && operands[i + 1] is PdfString high && operands[i + 2] is PdfInteger start)
                        {
                            var lowCode = ToCode(low.Span);
                            var highCode = ToCode(high.Span);
                            if (highCode >= lowCode)
                            {
                                _cids.Add(low.Span.Length, lowCode, highCode, (int)Math.Clamp(start.Value, 0, ushort.MaxValue), _order++);
                            }
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

    /// <summary>
    /// The mappings of a CMap of one kind (to texts or to CIDs): ranges of codes of each length, a single code being a
    /// range of one code. Overlapping ranges are resolved when the CMap is built: the codes are split into disjoint
    /// segments, each of the range of the highest order that contains it, so that a lookup is a binary search.
    /// </summary>
    private sealed class Mappings<T>
    {
        private readonly List<(uint Low, uint High, T Start, long Order)>[] _ranges = [[], [], [], [], []];
        private readonly (uint Low, uint High, int Range)[][] _segments = [[], [], [], [], []];

        public IEnumerable<(int Length, uint Low, uint High, T Start, long Order)> Entries
        {
            get
            {
                for (var length = 1; length <= 4; length++)
                {
                    foreach (var (low, high, start, order) in _ranges[length])
                    {
                        yield return (length, low, high, start, order);
                    }
                }
            }
        }

        public void Add(int length, uint low, uint high, T start, long order) => _ranges[length].Add((low, high, start, order));

        public bool TryFind(int length, uint code, out T start, out uint low)
        {
            if ((uint)length < (uint)_segments.Length)
            {
                var segments = _segments[length];
                var lower = 0;
                var upper = segments.Length - 1;
                while (lower <= upper)
                {
                    var middle = lower + ((upper - lower) / 2);
                    var segment = segments[middle];
                    if (code < segment.Low)
                    {
                        upper = middle - 1;
                    }
                    else if (code > segment.High)
                    {
                        lower = middle + 1;
                    }
                    else
                    {
                        (low, _, start, _) = _ranges[length][segment.Range];
                        return true;
                    }
                }
            }

            start = default!;
            low = 0;
            return false;
        }

        /// <summary>Splits the ranges into disjoint segments: a sweep over their bounds, with the active ranges in a queue by order.</summary>
        public void Build()
        {
            for (var length = 1; length <= 4; length++)
            {
                var ranges = _ranges[length];
                if (ranges.Count == 0)
                {
                    _segments[length] = [];
                    continue;
                }

                var events = new (ulong Position, int Range, bool IsStart)[ranges.Count * 2];
                for (var i = 0; i < ranges.Count; i++)
                {
                    events[2 * i] = (ranges[i].Low, i, true);
                    events[(2 * i) + 1] = ((ulong)ranges[i].High + 1, i, false);
                }

                Array.Sort(events, static (a, b) => a.Position.CompareTo(b.Position));
                var active = new bool[ranges.Count];
                var queue = new PriorityQueue<int, long>();
                var segments = new List<(uint Low, uint High, int Range)>();
                ulong previous = 0;
                var index = 0;
                while (index < events.Length)
                {
                    var position = events[index].Position;
                    while (queue.TryPeek(out var ended, out _) && !active[ended])
                    {
                        queue.Dequeue();
                    }

                    if (position > previous && queue.TryPeek(out var top, out _))
                    {
                        var low = (uint)previous;
                        var high = (uint)(position - 1);
                        if (segments.Count > 0 && segments[^1].Range == top && (ulong)segments[^1].High + 1 == low)
                        {
                            segments[^1] = (segments[^1].Low, high, top);
                        }
                        else
                        {
                            segments.Add((low, high, top));
                        }
                    }

                    for (; index < events.Length && events[index].Position == position; index++)
                    {
                        var range = events[index].Range;
                        active[range] = events[index].IsStart;
                        if (events[index].IsStart)
                        {
                            // The queue gives the smallest priority first: the highest order first
                            queue.Enqueue(range, -ranges[range].Order);
                        }
                    }

                    previous = position;
                }

                _segments[length] = [.. segments];
            }
        }
    }
}
