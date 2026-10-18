using System.Text;

namespace Meziantou.Framework.WebPlatform.Unicode;

/// <summary>
/// Unicode Normalization Form C (UAX #15) implemented from the generated Unicode Character Database tables.
/// <see cref="string.Normalize()"/> is not used because its result depends on the OS/ICU and it is unavailable with invariant globalization.
/// </summary>
internal static class Nfc
{
    private const int SBase = 0xAC00;
    private const int LBase = 0x1100;
    private const int VBase = 0x1161;
    private const int TBase = 0x11A7;
    private const int LCount = 19;
    private const int VCount = 21;
    private const int TCount = 28;
    private const int NCount = VCount * TCount;
    private const int SCount = LCount * NCount;

    /// <summary>Code points below U+0300 are never affected by normalization.</summary>
    private const int FirstAffectedCodePoint = 0x300;

    public static bool IsNormalized(ReadOnlySpan<char> value)
    {
        if (value.IndexOfAnyExceptInRange((char)0, (char)(FirstAffectedCodePoint - 1)) < 0)
            return true;

        return value.SequenceEqual(Normalize(value.ToString()));
    }

    public static string Normalize(string value)
    {
        if (value.AsSpan().IndexOfAnyExceptInRange((char)0, (char)(FirstAffectedCodePoint - 1)) < 0)
            return value;

        var codePoints = new List<int>(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            Decompose(rune.Value, codePoints);
        }

        ReorderCanonically(codePoints);
        Compose(codePoints);

        var builder = new StringBuilder(codePoints.Count);
        foreach (var codePoint in codePoints)
        {
            builder.Append(new Rune(codePoint));
        }

        return builder.ToString();
    }

    /// <summary>Writes the full canonical decomposition of a code point (itself when it has none) and returns its length; the Hangul syllables are not decomposed.</summary>
    public static int Decompose(int codePoint, Span<int> destination)
    {
        if (codePoint >= FirstAffectedCodePoint || codePoint is >= 0xC0 and <= 0x2FF)
        {
            var index = NormalizationData.DecompositionKeys.BinarySearch(codePoint);
            if (index >= 0)
            {
                var offsets = NormalizationData.DecompositionOffsets;
                var parts = NormalizationData.DecompositionData[offsets[index]..offsets[index + 1]];
                if (parts.Length <= destination.Length)
                {
                    for (var i = 0; i < parts.Length; i++)
                    {
                        destination[i] = parts[i];
                    }

                    return parts.Length;
                }
            }
        }

        destination[0] = codePoint;
        return 1;
    }

    private static void Decompose(int codePoint, List<int> output)
    {
        if (codePoint is >= SBase and < SBase + SCount)
        {
            var sIndex = codePoint - SBase;
            output.Add(LBase + (sIndex / NCount));
            output.Add(VBase + (sIndex % NCount / TCount));
            var t = TBase + (sIndex % TCount);
            if (t != TBase)
            {
                output.Add(t);
            }

            return;
        }

        if (codePoint >= FirstAffectedCodePoint || codePoint is >= 0xC0 and <= 0x2FF)
        {
            var index = NormalizationData.DecompositionKeys.BinarySearch(codePoint);
            if (index >= 0)
            {
                var offsets = NormalizationData.DecompositionOffsets;
                foreach (var part in NormalizationData.DecompositionData[offsets[index]..offsets[index + 1]])
                {
                    output.Add(part);
                }

                return;
            }
        }

        output.Add(codePoint);
    }

    private static void ReorderCanonically(List<int> codePoints)
    {
        // Stable sort of each run of non-starters by canonical combining class (insertion sort; runs are short).
        for (var i = 1; i < codePoints.Count; i++)
        {
            var ccc = UnicodeProperties.GetCanonicalCombiningClass(codePoints[i]);
            if (ccc == 0)
                continue;

            var j = i;
            while (j > 0)
            {
                var previousCcc = UnicodeProperties.GetCanonicalCombiningClass(codePoints[j - 1]);
                if (previousCcc <= ccc)
                    break;

                (codePoints[j], codePoints[j - 1]) = (codePoints[j - 1], codePoints[j]);
                j--;
            }
        }
    }

    private static void Compose(List<int> codePoints)
    {
        if (codePoints.Count == 0)
            return;

        var starterPosition = 0;
        var starter = codePoints[0];
        var lastClass = UnicodeProperties.GetCanonicalCombiningClass(starter);
        if (lastClass != 0)
        {
            // The string starts with a non-starter: nothing can compose with it.
            lastClass = 256;
        }

        var writePosition = 1;
        for (var readPosition = 1; readPosition < codePoints.Count; readPosition++)
        {
            var codePoint = codePoints[readPosition];
            var ccc = UnicodeProperties.GetCanonicalCombiningClass(codePoint);
            var composite = ComposePair(starter, codePoint);
            if (composite >= 0 && (lastClass < ccc || lastClass == 0))
            {
                codePoints[starterPosition] = composite;
                starter = composite;
                continue;
            }

            if (ccc == 0)
            {
                starterPosition = writePosition;
                starter = codePoint;
            }

            lastClass = ccc;
            codePoints[writePosition++] = codePoint;
        }

        codePoints.RemoveRange(writePosition, codePoints.Count - writePosition);
    }

    /// <summary>The primary composite of two code points (Unicode Standard, section 3.11), or -1 when they do not compose.</summary>
    public static int ComposePair(int first, int second)
    {
        if (first is >= LBase and < LBase + LCount && second is >= VBase and < VBase + VCount)
            return SBase + (((first - LBase) * VCount) + (second - VBase)) * TCount;

        if (first is >= SBase and < SBase + SCount && (first - SBase) % TCount == 0 && second is > TBase and < TBase + TCount)
            return first + (second - TBase);

        var index = NormalizationData.CompositionKeys.BinarySearch(((long)first << 21) | (uint)second);
        return index >= 0 ? NormalizationData.CompositionValues[index] : -1;
    }
}
