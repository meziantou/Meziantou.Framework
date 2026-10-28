namespace Meziantou.Framework.Fonts;

/// <summary>Maps characters to glyphs with the <c>cmap</c> table.</summary>
/// <remarks>
/// <para>
/// OpenType specification, "cmap - Character to Glyph Index Mapping Table". One subtable is selected when the font is
/// loaded, by decreasing preference: the full Unicode repertoire (formats 12 and 13), the Basic Multilingual Plane
/// (formats 4 and 6), a Windows symbol encoding, a Macintosh Roman encoding (of which only the ASCII range is used). The
/// Unicode variation sequences come from the format 14 subtable, when there is one.
/// </para>
/// <para>
/// The structure of the selected subtables is validated when the font is loaded, so that a lookup never fails: a
/// character without glyph, or with a glyph the font does not have, maps to the glyph 0.
/// </para>
/// </remarks>
internal sealed class CmapTable
{
    private const int MaxCodePoint = 0x10FFFF;

    private readonly ReadOnlyMemory<byte> _table;
    private readonly int _glyphCount;
    private readonly int _offset;
    private readonly int _format;
    private readonly int _count;
    private readonly bool _isSymbol;
    private readonly bool _isMacintosh;
    private readonly int _variationOffset;
    private readonly int _variationCount;

    // The glyphs of the pages of 256 characters of the Basic Multilingual Plane that were looked up.
    private ushort[]?[]? _pages;

    private CmapTable(ReadOnlyMemory<byte> table, int glyphCount, int offset, int format, int count, bool isSymbol, bool isMacintosh, int variationOffset, int variationCount)
    {
        _table = table;
        _glyphCount = glyphCount;
        _offset = offset;
        _format = format;
        _count = count;
        _isSymbol = isSymbol;
        _isMacintosh = isMacintosh;
        _variationOffset = variationOffset;
        _variationCount = variationCount;
    }

    /// <summary>Gets a value indicating whether the font has Unicode variation sequences.</summary>
    public bool HasVariationSequences => _variationCount > 0;

    public static CmapTable Read(ReadOnlyMemory<byte> table, int glyphCount)
    {
        var data = table.Span;
        var subtableCount = FontBinary.UInt16(data, 2);
        var records = FontBinary.Slice(data, 4, (long)subtableCount * 8);
        var bestScore = 0;
        var bestOffset = 0;
        var bestFormat = 0;
        var bestPlatform = 0;
        var bestEncoding = 0;
        var variationOffset = -1;
        for (var i = 0; i < subtableCount; i++)
        {
            var platform = FontBinary.UInt16(records, i * 8);
            var encoding = FontBinary.UInt16(records, (i * 8) + 2);
            var offset = FontBinary.Offset32(records, (i * 8) + 4);
            var format = FontBinary.UInt16(data, offset);
            if (format == 14)
            {
                if (platform == 0 && encoding == 5)
                {
                    variationOffset = offset;
                }

                continue;
            }

            var isSupported = format is 0 or 4 or 6 or 10 or 12 or 13;
            var score = !isSupported ? 0 : (platform, encoding) switch
            {
                (3, 10) when format is 10 or 12 or 13 => 60,
                (0, 4 or 6) when format is 10 or 12 or 13 => 55,
                (0, _) when format is 10 or 12 or 13 => 50,
                (3, 1) => 40,
                (0, _) => 35,
                (3, 0) => 20,
                (1, 0) => 10,
                _ => 0,
            };
            if (score > bestScore)
            {
                bestScore = score;
                bestOffset = offset;
                bestFormat = format;
                bestPlatform = platform;
                bestEncoding = encoding;
            }
        }

        if (bestScore == 0)
            throw FontErrors.Unsupported("The font has no character map in a supported format and encoding.", "cmap without a Unicode, symbol or Macintosh Roman subtable in format 0, 4, 6, 10, 12 or 13");

        var count = Validate(data, bestOffset, bestFormat);
        var variationCount = 0;
        if (variationOffset >= 0)
        {
            variationCount = ValidateVariations(data, variationOffset);
        }

        return new CmapTable(table, glyphCount, bestOffset, bestFormat, count, isSymbol: bestPlatform == 3 && bestEncoding == 0, isMacintosh: bestPlatform == 1, variationOffset, variationCount);
    }

    /// <summary>Gets the glyph of a character.</summary>
    /// <returns>The glyph identifier, or 0 when the font has no glyph for the character.</returns>
    /// <remarks>
    /// The glyphs of the characters of the Basic Multilingual Plane are kept by pages of 256 characters, built the first
    /// time a character of the page is looked up: a text looks up the same few pages again and again. A page is
    /// complete when it is published, so concurrent readers see it whole or build their own.
    /// </remarks>
    public int Map(int codePoint)
    {
        if ((uint)codePoint <= 0xFFFF)
        {
            var pages = Volatile.Read(ref _pages);
            if (pages is null)
            {
                pages = new ushort[]?[256];
                pages = Interlocked.CompareExchange(ref _pages, pages, null) ?? pages;
            }

            var page = Volatile.Read(ref pages[codePoint >> 8]);
            if (page is null)
            {
                page = new ushort[256];
                var first = codePoint & ~0xFF;
                for (var i = 0; i < page.Length; i++)
                {
                    page[i] = (ushort)MapUncached(first + i);
                }

                Volatile.Write(ref pages[codePoint >> 8], page);
            }

            return page[codePoint & 0xFF];
        }

        return MapUncached(codePoint);
    }

    /// <summary>
    /// Gets the characters that map to a glyph other than the glyph 0, as sorted and disjoint ranges: the first and the
    /// last character of each range, one range after the other.
    /// </summary>
    /// <remarks>
    /// The characters are the ones <see cref="Map"/> gives a glyph: the ranges of the subtable are merged, then each of
    /// their characters is mapped, so that the ranges say exactly what <see cref="Map"/> says. A subtable has at most
    /// all the characters of Unicode: the work is bounded by their number, whatever the ranges of the subtable.
    /// </remarks>
    public int[] GetCoveredRanges()
    {
        var data = _table.Span[_offset..];
        var candidates = new List<(int Start, int End)>();
        switch (_format)
        {
            case 0:
                candidates.Add((0, 0xFF));
                break;

            case 4:
                {
                    var endCodes = 14;
                    var startCodes = endCodes + (_count * 2) + 2;
                    for (var i = 0; i < _count; i++)
                    {
                        AddCandidate(candidates, FontBinary.UInt16(data, startCodes + (i * 2)), FontBinary.UInt16(data, endCodes + (i * 2)));
                    }

                    break;
                }

            case 6:
                {
                    var first = FontBinary.UInt16(data, 6);
                    AddCandidate(candidates, first, (long)first + _count - 1);
                    break;
                }

            case 10:
                {
                    var first = FontBinary.UInt32(data, 12);
                    AddCandidate(candidates, first, (long)first + _count - 1);
                    break;
                }

            default:
                for (var i = 0; i < _count; i++)
                {
                    var group = 16 + (i * 12);
                    AddCandidate(candidates, FontBinary.UInt32(data, group), FontBinary.UInt32(data, group + 4));
                }

                break;
        }

        // A symbol font also maps the characters below U+0100 to the ones it has at U+F000 plus their code.
        if (_isSymbol)
        {
            candidates.Add((0, 0xFF));
        }

        candidates.Sort((x, y) => x.Start.CompareTo(y.Start));
        var ranges = new List<int>();
        var next = 0;
        foreach (var (start, end) in candidates)
        {
            var last = _isMacintosh ? Math.Min(end, 0x7F) : end;
            for (var codePoint = Math.Max(start, next); codePoint <= last; codePoint++)
            {
                if (MapUncached(codePoint) == 0)
                    continue;

                if (ranges.Count > 0 && ranges[^1] == codePoint - 1)
                {
                    ranges[^1] = codePoint;
                }
                else
                {
                    ranges.Add(codePoint);
                    ranges.Add(codePoint);
                }
            }

            next = Math.Max(next, end + 1);
        }

        return [.. ranges];

        static void AddCandidate(List<(int Start, int End)> candidates, long start, long end)
        {
            if (start > end || start > MaxCodePoint)
                return;

            candidates.Add(((int)start, (int)Math.Min(end, MaxCodePoint)));
        }
    }

    private int MapUncached(int codePoint)
    {
        if ((uint)codePoint > 0x10FFFF)
            return 0;

        if (_isMacintosh && codePoint >= 0x80)
            return 0;

        var glyph = MapCore(codePoint);
        if (glyph == 0 && _isSymbol && codePoint < 0x100)
        {
            // A symbol font maps its characters in the Private Use Area, at U+F000 plus the character code.
            glyph = MapCore(0xF000 + codePoint);
        }

        return glyph < _glyphCount ? glyph : 0;
    }

    /// <summary>Gets the glyph of a variation sequence.</summary>
    /// <param name="codePoint">The base character.</param>
    /// <param name="selector">The variation selector.</param>
    /// <param name="glyph">The glyph of the sequence, when the method returns <see cref="CmapVariation.Glyph"/>.</param>
    public CmapVariation MapVariation(int codePoint, int selector, out int glyph)
    {
        glyph = 0;
        if (_variationCount == 0 || (uint)codePoint > 0x10FFFF)
            return CmapVariation.NotFound;

        var data = _table.Span[_variationOffset..];

        // Records of 11 bytes sorted by selector: the selector, then the offsets of its two tables.
        var low = 0;
        var high = _variationCount - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var record = 10 + (middle * 11);
            var value = FontBinary.UInt24(data, record);
            if (selector < value)
            {
                high = middle - 1;
            }
            else if (selector > value)
            {
                low = middle + 1;
            }
            else
            {
                var defaultOffset = FontBinary.Offset32(data, record + 3);
                var nonDefaultOffset = FontBinary.Offset32(data, record + 7);
                if (nonDefaultOffset != 0 && TryFindVariationGlyph(data[nonDefaultOffset..], codePoint, out glyph))
                {
                    if (glyph >= _glyphCount)
                    {
                        glyph = 0;
                        return CmapVariation.NotFound;
                    }

                    return CmapVariation.Glyph;
                }

                return defaultOffset != 0 && IsDefaultVariation(data[defaultOffset..], codePoint) ? CmapVariation.UseDefault : CmapVariation.NotFound;
            }
        }

        return CmapVariation.NotFound;
    }

    private int MapCore(int codePoint)
    {
        var data = _table.Span[_offset..];
        switch (_format)
        {
            case 0:
                return codePoint < 256 ? data[6 + codePoint] : 0;

            case 4:
                return codePoint > 0xFFFF ? 0 : MapFormat4(data, codePoint);

            case 6:
                {
                    var index = codePoint - FontBinary.UInt16(data, 6);
                    return (uint)index < (uint)_count ? FontBinary.UInt16(data, 10 + (index * 2)) : 0;
                }

            // A range of characters with 32-bit codes, and one glyph for each.
            case 10:
                {
                    var index = (long)codePoint - FontBinary.UInt32(data, 12);
                    return index >= 0 && index < _count ? FontBinary.UInt16(data, 20 + ((int)index * 2)) : 0;
                }

            default:
                return MapGroups(data, codePoint);
        }
    }

    private int MapFormat4(ReadOnlySpan<byte> data, int codePoint)
    {
        // Four parallel arrays of _count segments: end codes, a reserved word, start codes, deltas, range offsets.
        var segmentCount = _count;
        var endCodes = 14;
        var startCodes = endCodes + (segmentCount * 2) + 2;
        var deltas = startCodes + (segmentCount * 2);
        var rangeOffsets = deltas + (segmentCount * 2);

        // The first segment whose end code is not below the character.
        var low = 0;
        var high = segmentCount - 1;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (FontBinary.UInt16(data, endCodes + (middle * 2)) < codePoint)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (FontBinary.UInt16(data, endCodes + (low * 2)) < codePoint)
            return 0;

        var start = FontBinary.UInt16(data, startCodes + (low * 2));
        if (start > codePoint)
            return 0;

        var delta = FontBinary.UInt16(data, deltas + (low * 2));
        var rangeOffsetPosition = rangeOffsets + (low * 2);
        var rangeOffset = FontBinary.UInt16(data, rangeOffsetPosition);
        if (rangeOffset == 0)
            return (codePoint + delta) & 0xFFFF;

        // The range offset is counted from its own position to the entry of the first character of the segment.
        var position = rangeOffsetPosition + rangeOffset + ((codePoint - start) * 2);
        if (position > data.Length - 2)
            return 0;

        var glyph = FontBinary.UInt16(data, position);
        return glyph == 0 ? 0 : (glyph + delta) & 0xFFFF;
    }

    private int MapGroups(ReadOnlySpan<byte> data, int codePoint)
    {
        // Groups of 12 bytes sorted by character: first character, last character, glyph.
        var low = 0;
        var high = _count - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var group = 16 + (middle * 12);
            var start = FontBinary.UInt32(data, group);
            if ((uint)codePoint < start)
            {
                high = middle - 1;
            }
            else if ((uint)codePoint > FontBinary.UInt32(data, group + 4))
            {
                low = middle + 1;
            }
            else
            {
                // Format 12 maps consecutive characters to consecutive glyphs; format 13 maps them to one glyph.
                var glyph = FontBinary.UInt32(data, group + 8);
                if (_format == 12)
                {
                    glyph += (uint)codePoint - start;
                }

                return glyph > int.MaxValue ? 0 : (int)glyph;
            }
        }

        return 0;
    }

    private static bool TryFindVariationGlyph(ReadOnlySpan<byte> table, int codePoint, out int glyph)
    {
        // Mappings of 5 bytes sorted by character: the character, then its glyph.
        var low = 0;
        var high = FontBinary.Offset32(table, 0) - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var mapping = 4 + (middle * 5);
            var value = FontBinary.UInt24(table, mapping);
            if (codePoint < value)
            {
                high = middle - 1;
            }
            else if (codePoint > value)
            {
                low = middle + 1;
            }
            else
            {
                glyph = FontBinary.UInt16(table, mapping + 3);
                return true;
            }
        }

        glyph = 0;
        return false;
    }

    private static bool IsDefaultVariation(ReadOnlySpan<byte> table, int codePoint)
    {
        // Ranges of 4 bytes sorted by character: the first character, then the number of characters that follow it.
        var low = 0;
        var high = FontBinary.Offset32(table, 0) - 1;
        while (low <= high)
        {
            var middle = (low + high) >>> 1;
            var range = 4 + (middle * 4);
            var start = FontBinary.UInt24(table, range);
            if (codePoint < start)
            {
                high = middle - 1;
            }
            else if (codePoint > start + FontBinary.UInt8(table, range + 3))
            {
                low = middle + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Checks that the arrays of a subtable lie inside the table, and returns its number of segments, entries or groups.</summary>
    private static int Validate(ReadOnlySpan<byte> table, int offset, int format)
    {
        var data = table[offset..];
        switch (format)
        {
            case 0:
                FontBinary.EnsureRange(data, 6, 256);
                return 256;

            case 4:
                {
                    var segmentCountTimesTwo = FontBinary.UInt16(data, 6);
                    if (segmentCountTimesTwo == 0 || (segmentCountTimesTwo & 1) != 0)
                        throw FontErrors.InvalidTable(FontTags.Cmap, "has a format 4 subtable with an invalid segment count.");

                    var segmentCount = segmentCountTimesTwo / 2;
                    FontBinary.EnsureRange(data, 14, ((long)segmentCount * 8) + 2);
                    return segmentCount;
                }

            case 6:
                {
                    var count = FontBinary.UInt16(data, 8);
                    FontBinary.EnsureRange(data, 10, (long)count * 2);
                    return count;
                }

            case 10:
                {
                    var count = FontBinary.Offset32(data, 16);
                    FontBinary.EnsureRange(data, 20, (long)count * 2);
                    return count;
                }

            default:
                {
                    var count = FontBinary.Offset32(data, 12);
                    FontBinary.EnsureRange(data, 16, (long)count * 12);
                    return count;
                }
        }
    }

    /// <summary>Checks the records of the variation subtable and the tables they point at, and returns the number of records.</summary>
    private static int ValidateVariations(ReadOnlySpan<byte> table, int offset)
    {
        var data = table[offset..];
        var count = FontBinary.Offset32(data, 6);
        FontBinary.EnsureRange(data, 10, (long)count * 11);
        for (var i = 0; i < count; i++)
        {
            var record = 10 + (i * 11);
            var defaultOffset = FontBinary.Offset32(data, record + 3);
            if (defaultOffset != 0)
            {
                FontBinary.EnsureRange(data, (long)defaultOffset + 4, (long)FontBinary.Offset32(data, defaultOffset) * 4);
            }

            var nonDefaultOffset = FontBinary.Offset32(data, record + 7);
            if (nonDefaultOffset != 0)
            {
                FontBinary.EnsureRange(data, (long)nonDefaultOffset + 4, (long)FontBinary.Offset32(data, nonDefaultOffset) * 5);
            }
        }

        return count;
    }
}
