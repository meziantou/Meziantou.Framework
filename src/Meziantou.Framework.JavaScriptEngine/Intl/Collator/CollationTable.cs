namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>
/// A collation table: the root collation with the mappings of a tailoring (which override the root mappings), and the
/// computation of the collation elements of a string in NFD (UCA S2: prefix mappings, the longest contiguous and
/// discontiguous contractions, the implicit weights, and the numeric collation of the digits).
/// </summary>
internal sealed class CollationTable
{
    public CollationTable(CollationMap? tailored, HashSet<int>? suppressedContractions)
    {
        Root = RootCollation.Instance;
        Tailored = tailored;
        SuppressedContractions = suppressedContractions is { Count: > 0 } ? suppressedContractions : null;
    }

    public RootCollation Root { get; }

    /// <summary>Gets the mappings of the tailoring, or null for the root collation.</summary>
    public CollationMap? Tailored { get; }

    /// <summary>Gets the code points whose root contractions are removed (<c>[suppressContractions]</c>).</summary>
    public HashSet<int>? SuppressedContractions { get; }

    /// <summary>
    /// Appends the collation elements of code points in NFD to a buffer (grown when needed) and returns the new count.
    /// The code points are modified when a discontiguous contraction consumes a combining mark.
    /// </summary>
    public int GetElements(Span<int> codePoints, ref ulong[] output, int outputCount, bool numeric)
    {
        var rootMap = Root.Map;
        var tailored = Tailored;
        var length = codePoints.Length;
        var i = 0;
        while (i < length)
        {
            var c = codePoints[i];
            if (outputCount + CollationMap.CountMask + 2 > output.Length)
            {
                Array.Resize(ref output, Math.Max(output.Length * 2, outputCount + 64));
            }

            var tailoredEntry = tailored?.GetEntry(c) ?? 0;
            var rootEntry = rootMap.GetEntry(c);
            if (numeric)
            {
                // As ICU, the digits are numbers even when the tailoring maps them
                var digit = Root.GetDigitValue(c);
                if (digit >= 0)
                {
                    i = AppendNumber(codePoints[..length], i, ref output, ref outputCount);
                    continue;
                }
            }

            if ((tailoredEntry & CollationMap.PrefixFlag) != 0 && TryMatchPrefix(tailored!, codePoints[..length], i, out var prefixElements, out var prefixMatchLength))
            {
                outputCount = Append(prefixElements, output, outputCount);
                i += prefixMatchLength;
                continue;
            }

            var hasTailoredContractions = (tailoredEntry & CollationMap.ContractionFlag) != 0;
            var hasRootContractions = (rootEntry & CollationMap.ContractionFlag) != 0 && (SuppressedContractions is null || !SuppressedContractions.Contains(c));
            if (hasTailoredContractions || hasRootContractions)
            {
                if (TryMatchContraction(codePoints, ref length, i, hasTailoredContractions, hasRootContractions, out var matchLength, out var contraction))
                {
                    outputCount = Append(contraction, output, outputCount);
                    i += matchLength;
                    continue;
                }
            }

            if ((tailoredEntry & CollationMap.CountMask) != 0)
            {
                outputCount = Append(tailored!.GetElements(tailoredEntry), output, outputCount);
            }
            else if ((rootEntry & CollationMap.CountMask) != 0)
            {
                outputCount = Append(rootMap.GetElements(rootEntry), output, outputCount);
            }
            else
            {
                output[outputCount++] = Root.GetImplicitCE(c);
            }

            i++;
        }

        return outputCount;
    }

    private static int Append(ReadOnlySpan<ulong> elements, ulong[] output, int outputCount)
    {
        elements.CopyTo(output.AsSpan(outputCount));
        return outputCount + elements.Length;
    }

    private static bool TryMatchPrefix(CollationMap map, ReadOnlySpan<int> codePoints, int index, out ReadOnlySpan<ulong> elements, out int matchLength)
    {
        foreach (var mapping in map.GetPrefixMappings(codePoints[index]) ?? [])
        {
            var prefix = mapping.Prefix;
            var suffix = mapping.Suffix;
            if (prefix.Length <= index && codePoints.Slice(index - prefix.Length, prefix.Length).SequenceEqual(prefix) &&
                index + 1 + suffix.Length <= codePoints.Length && codePoints.Slice(index + 1, suffix.Length).SequenceEqual(suffix))
            {
                elements = map.GetElements(mapping.Offset, mapping.Count);
                matchLength = 1 + suffix.Length;
                return true;
            }
        }

        elements = default;
        matchLength = 0;
        return false;
    }

    private bool TryGetContraction(scoped ReadOnlySpan<int> key, bool tailored, bool root, out ReadOnlySpan<ulong> elements)
    {
        if (tailored && Tailored!.TryGetContraction(key, out elements))
            return true;

        if (root && Root.Map.TryGetContraction(key, out elements))
            return true;

        elements = default;
        return false;
    }

    private bool TryMatchContraction(Span<int> codePoints, ref int length, int index, bool tailored, bool root, out int matchLength, out ReadOnlySpan<ulong> elements)
    {
        matchLength = 1;
        elements = default;

        // Quick check: the next code point, or one of the following non-starters, must continue a contraction
        var continues = false;
        for (var next = index + 1; next < length; next++)
        {
            var codePoint = codePoints[next];
            if ((tailored && Tailored!.IsContinuation(codePoint)) || (root && Root.Map.IsContinuation(codePoint)))
            {
                continues = true;
                break;
            }

            if (codePoint < 0x300 || UnicodeProperties.GetCanonicalCombiningClass(codePoint) == 0)
                break;
        }

        if (!continues)
            return false;

        var maxLength = Math.Max(tailored ? Tailored!.MaxContractionLength : 1, root ? Root.Map.MaxContractionLength : 1);
        Span<int> key = stackalloc int[maxLength + 8];
        var found = false;

        // S2.1: the longest contiguous match
        for (var candidate = Math.Min(maxLength, length - index); candidate >= 2; candidate--)
        {
            if (TryGetContraction(codePoints.Slice(index, candidate), tailored, root, out var contraction))
            {
                elements = contraction;
                matchLength = candidate;
                found = true;
                break;
            }
        }

        // S2.1.1-S2.1.3: the unblocked non-starters that extend the match (discontiguous contractions)
        var keyLength = matchLength;
        codePoints.Slice(index, matchLength).CopyTo(key);
        var position = index + matchLength;
        var lastSkippedCombiningClass = -1;
        while (position < length && keyLength < key.Length)
        {
            var next = codePoints[position];
            var combiningClass = next < 0x300 ? 0 : UnicodeProperties.GetCanonicalCombiningClass(next);
            if (combiningClass == 0)
                break;

            if (combiningClass > lastSkippedCombiningClass)
            {
                key[keyLength] = next;
                if (TryGetContraction(key[..(keyLength + 1)], tailored, root, out var contraction))
                {
                    // The code point is consumed by the contraction
                    elements = contraction;
                    found = true;
                    keyLength++;
                    codePoints[(position + 1)..length].CopyTo(codePoints[position..]);
                    length--;
                    continue;
                }
            }

            lastSkippedCombiningClass = combiningClass;
            position++;
        }

        return found;
    }

    // Numeric collation: a sequence of decimal digits is a number. The first CE has the primary of the digit zero plus
    // the number of significant digits; each significant digit adds a primary-only CE.
    private int AppendNumber(ReadOnlySpan<int> codePoints, int index, ref ulong[] output, ref int outputCount)
    {
        var end = index;
        var start = -1;
        while (end < codePoints.Length && Root.GetDigitValue(codePoints[end]) is var digit && digit >= 0)
        {
            if (start < 0 && digit != 0)
            {
                start = end;
            }

            end++;
        }

        if (start < 0)
        {
            start = end - 1;
        }

        var count = Math.Min(end - start, 0x9FFFE);
        if (outputCount + count + 2 > output.Length)
        {
            Array.Resize(ref output, Math.Max(output.Length * 2, outputCount + count + 64));
        }

        var zero = Root.DigitZeroPrimary;
        output[outputCount++] = CollationWeights.MakeCE(zero + (uint)count);
        for (var i = start; i < start + count; i++)
        {
            output[outputCount++] = (ulong)(zero + 1 + (uint)Root.GetDigitValue(codePoints[i])) << 32;
        }

        return end;
    }
}
