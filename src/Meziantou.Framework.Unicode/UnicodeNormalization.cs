using System.Buffers;

namespace Meziantou.Framework;

/// <summary>Provides the Unicode normalization forms.</summary>
/// <remarks>
/// <para>
/// The forms are those of Unicode Standard Annex #15 for <see cref="UnicodeProperties.UnicodeVersion"/>. They do not
/// use the globalization library of the platform: the results are the same on every platform and every framework
/// version, including in globalization-invariant mode, where <see cref="string.Normalize()"/> only handles ASCII.
/// </para>
/// <para>
/// An unpaired surrogate is not a character: it is copied as is and never combines. The methods never throw for the
/// content of the text.
/// </para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr15/" />
public static class UnicodeNormalization
{
    private const int HangulBase = 0xAC00;
    private const int HangulLeadBase = 0x1100;
    private const int HangulVowelBase = 0x1161;
    private const int HangulTrailBase = 0x11A7;
    private const int HangulLeadCount = 19;
    private const int HangulVowelCount = 21;
    private const int HangulTrailCount = 28;
    private const int HangulSyllableCount = HangulLeadCount * HangulVowelCount * HangulTrailCount;

    // Flags of UnicodeNormalizationTables.NormalizationTuples.
    private const int NfdNo = 0x01;
    private const int NfkdNo = 0x02;
    private const int NfcNo = 0x04;
    private const int NfcMaybe = 0x08;
    private const int NfkcNo = 0x10;
    private const int NfkcMaybe = 0x20;

    // A run of combining marks shorter than this is reordered by insertion.
    private const int InsertionSortLimit = 24;

    /// <summary>Gets the greatest number of code points of the full canonical decomposition of one code point.</summary>
    public static int MaxCanonicalDecompositionLength => 4;

    /// <summary>Returns a text in a normalization form.</summary>
    /// <param name="value">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <returns>The normalized text; <paramref name="value"/> itself when it is already normalized.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="value"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="form"/> is not a defined value.</exception>
    public static string Normalize(string value, NormalizationForm form = NormalizationForm.FormC)
    {
        ArgumentNullException.ThrowIfNull(value);
        ValidateForm(form);
        if (QuickCheck(value, form) is QuickCheckResult.Yes)
            return value;

        var buffer = Normalize(value, form, out var count);
        try
        {
            var length = GetUtf16Length(buffer.AsSpan(0, count));
            if (length == value.Length && Matches(buffer.AsSpan(0, count), value))
                return value;

            return string.Create(length, (buffer, count), (destination, state) => Encode(state.buffer.AsSpan(0, state.count), destination));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    /// <summary>Writes a text in a normalization form to a buffer.</summary>
    /// <param name="source">The text.</param>
    /// <param name="destination">The buffer that receives the normalized text. It must not overlap <paramref name="source"/>.</param>
    /// <param name="charsWritten">The number of UTF-16 code units written, when the method returns <see langword="true"/>; otherwise 0.</param>
    /// <param name="form">The normalization form.</param>
    /// <returns><see langword="false"/> when <paramref name="destination"/> is too small; nothing meaningful is written in that case.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="form"/> is not a defined value.</exception>
    public static bool TryNormalize(ReadOnlySpan<char> source, Span<char> destination, out int charsWritten, NormalizationForm form = NormalizationForm.FormC)
    {
        ValidateForm(form);
        charsWritten = 0;
        if (QuickCheck(source, form) is QuickCheckResult.Yes)
        {
            if (!source.TryCopyTo(destination))
                return false;

            charsWritten = source.Length;
            return true;
        }

        var buffer = Normalize(source, form, out var count);
        try
        {
            var length = GetUtf16Length(buffer.AsSpan(0, count));
            if (length > destination.Length)
                return false;

            Encode(buffer.AsSpan(0, count), destination);
            charsWritten = length;
            return true;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    /// <summary>Gets the length of a text once normalized.</summary>
    /// <param name="source">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <returns>The number of UTF-16 code units <see cref="TryNormalize(ReadOnlySpan{char}, Span{char}, out int, NormalizationForm)"/> writes.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="form"/> is not a defined value.</exception>
    public static int GetNormalizedLength(ReadOnlySpan<char> source, NormalizationForm form = NormalizationForm.FormC)
    {
        ValidateForm(form);
        if (QuickCheck(source, form) is QuickCheckResult.Yes)
            return source.Length;

        var buffer = Normalize(source, form, out var count);
        try
        {
            return GetUtf16Length(buffer.AsSpan(0, count));
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    /// <summary>Determines whether a text is in a normalization form.</summary>
    /// <param name="value">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <returns><see langword="true"/> when normalizing the text would not change it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="form"/> is not a defined value.</exception>
    public static bool IsNormalized(ReadOnlySpan<char> value, NormalizationForm form = NormalizationForm.FormC)
    {
        ValidateForm(form);
        switch (QuickCheck(value, form))
        {
            case QuickCheckResult.Yes:
                return true;

            case QuickCheckResult.No:
                return false;
        }

        var buffer = Normalize(value, form, out var count);
        try
        {
            return GetUtf16Length(buffer.AsSpan(0, count)) == value.Length && Matches(buffer.AsSpan(0, count), value);
        }
        finally
        {
            ArrayPool<int>.Shared.Return(buffer);
        }
    }

    /// <summary>Gets the canonical decomposition mapping of a code point: one step of decomposition.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="first">The first code point of the mapping, when the method returns <see langword="true"/>; otherwise <paramref name="codePoint"/>.</param>
    /// <param name="second">The second code point of the mapping, or -1 when the mapping is a single code point.</param>
    /// <returns><see langword="true"/> when the code point has a canonical decomposition.</returns>
    /// <remarks>
    /// The first code point can have a decomposition of its own; the second one never has. A Hangul syllable is
    /// decomposed into its leading syllable and its trailing consonant, or into its two jamos when it has no trailing consonant.
    /// </remarks>
    public static bool TryDecomposeCanonical(int codePoint, out int first, out int second)
    {
        var index = codePoint - HangulBase;
        if ((uint)index < HangulSyllableCount)
        {
            var trail = index % HangulTrailCount;
            if (trail != 0)
            {
                first = codePoint - trail;
                second = HangulTrailBase + trail;
            }
            else
            {
                first = HangulLeadBase + (index / (HangulVowelCount * HangulTrailCount));
                second = HangulVowelBase + (index % (HangulVowelCount * HangulTrailCount) / HangulTrailCount);
            }

            return true;
        }

        if (TryFindCanonical(codePoint, out first, out second))
        {
            if (second == 0)
            {
                second = -1;
            }

            return true;
        }

        first = codePoint;
        second = -1;
        return false;
    }

    /// <summary>Gets the primary composite of two code points.</summary>
    /// <param name="first">The first code point, a starter.</param>
    /// <param name="second">The second code point.</param>
    /// <param name="composite">The composite, when the method returns <see langword="true"/>; otherwise 0.</param>
    /// <returns><see langword="true"/> when the two code points compose under the normalization form C. The pairs excluded from composition return <see langword="false"/>.</returns>
    public static bool TryComposeCanonical(int first, int second, out int composite)
    {
        // Hangul: a leading consonant and a vowel, then a syllable without trailing consonant and a trailing consonant.
        var lead = first - HangulLeadBase;
        if ((uint)lead < HangulLeadCount)
        {
            var vowel = second - HangulVowelBase;
            if ((uint)vowel < HangulVowelCount)
            {
                composite = HangulBase + (((lead * HangulVowelCount) + vowel) * HangulTrailCount);
                return true;
            }
        }

        var syllable = first - HangulBase;
        if ((uint)syllable < HangulSyllableCount && syllable % HangulTrailCount == 0)
        {
            var trail = second - HangulTrailBase;
            if (trail > 0 && trail < HangulTrailCount)
            {
                composite = first + trail;
                return true;
            }
        }

        if ((uint)first <= 0x10FFFF && (uint)second <= 0x10FFFF)
        {
            var index = UnicodeNormalizationTables.CompositionKeys.BinarySearch(((long)first << 21) | (uint)second);
            if (index >= 0)
            {
                composite = UnicodeNormalizationTables.CompositionValues[index];
                return true;
            }
        }

        composite = 0;
        return false;
    }

    /// <summary>Gets the full canonical decomposition of a code point.</summary>
    /// <param name="codePoint">The code point.</param>
    /// <param name="destination">The buffer that receives the code points. <see cref="MaxCanonicalDecompositionLength"/> elements are always enough.</param>
    /// <returns>The number of code points written; 1, the code point itself, when it has no decomposition.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public static int GetCanonicalDecomposition(int codePoint, Span<int> destination)
    {
        Span<int> buffer = stackalloc int[4];
        var count = DecomposeCanonical(codePoint, buffer);
        if (count > destination.Length)
            throw new ArgumentException("The destination is too small", nameof(destination));

        buffer[..count].CopyTo(destination);
        return count;
    }

    private static void ValidateForm(NormalizationForm form)
    {
        if (form is not (NormalizationForm.FormC or NormalizationForm.FormD or NormalizationForm.FormKC or NormalizationForm.FormKD))
            throw new ArgumentOutOfRangeException(nameof(form), form, "The value is not defined");
    }

    private static bool TryFindCanonical(int codePoint, out int first, out int second)
    {
        var index = UnicodeNormalizationTables.CanonicalKeys.BinarySearch(codePoint);
        if (index < 0)
        {
            first = codePoint;
            second = 0;
            return false;
        }

        var values = UnicodeNormalizationTables.CanonicalValues;
        first = values[index * 2];
        second = values[(index * 2) + 1];
        return true;
    }

    /// <summary>Writes the full canonical decomposition of a code point; the destination holds at least 4 elements.</summary>
    private static int DecomposeCanonical(int codePoint, Span<int> destination)
    {
        var index = codePoint - HangulBase;
        if ((uint)index < HangulSyllableCount)
        {
            destination[0] = HangulLeadBase + (index / (HangulVowelCount * HangulTrailCount));
            destination[1] = HangulVowelBase + (index % (HangulVowelCount * HangulTrailCount) / HangulTrailCount);
            var trail = index % HangulTrailCount;
            if (trail == 0)
                return 2;

            destination[2] = HangulTrailBase + trail;
            return 3;
        }

        // The second code point of a mapping has no decomposition: follow the first one, and write backwards.
        var count = 0;
        while (count < 3 && TryFindCanonical(codePoint, out var first, out var second))
        {
            if (second != 0)
            {
                destination[3 - count] = second;
                count++;
            }

            codePoint = first;
        }

        destination[0] = codePoint;
        if (count > 0)
        {
            destination.Slice(4 - count, count).CopyTo(destination[1..]);
        }

        return count + 1;
    }

    private static QuickCheckResult QuickCheck(ReadOnlySpan<char> text, NormalizationForm form)
    {
        var (no, maybe) = form switch
        {
            NormalizationForm.FormD => (NfdNo, 0),
            NormalizationForm.FormKD => (NfkdNo, 0),
            NormalizationForm.FormC => (NfcNo, NfcMaybe),
            _ => (NfkcNo, NfkcMaybe),
        };

        // The characters below this limit are starters that no form changes: U+00C0 is the first character that has a
        // canonical decomposition, and U+00A0 the first one that has a compatibility decomposition.
        var unchanged = form is NormalizationForm.FormC or NormalizationForm.FormD ? 0xC0 : 0xA0;
        var result = QuickCheckResult.Yes;
        var lastClass = 0;
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        for (var index = 0; index < text.Length;)
        {
            var c = text[index];
            if (c < unchanged)
            {
                lastClass = 0;
                index++;
                continue;
            }

            var entry = UnicodeNormalizationTables.GetNormalization(Unicode.DecodeAt(text, index, out var length)) * 2;
            index += length;
            var combiningClass = tuples[entry];
            var flags = tuples[entry + 1];
            if ((flags & no) != 0 || (combiningClass != 0 && lastClass > combiningClass))
                return QuickCheckResult.No;

            if ((flags & maybe) != 0)
            {
                result = QuickCheckResult.Maybe;
            }

            lastClass = combiningClass;
        }

        return result;
    }

    /// <summary>Normalizes a text into a rented buffer of code points, which the caller returns to the shared pool.</summary>
    private static int[] Normalize(ReadOnlySpan<char> text, NormalizationForm form, out int count)
    {
        var isCompatibility = form is NormalizationForm.FormKC or NormalizationForm.FormKD;
        var buffer = ArrayPool<int>.Shared.Rent(Math.Max(16, text.Length + (text.Length / 2)));
        count = 0;
        Span<int> canonical = stackalloc int[4];
        var runStart = 0;
        var tuples = UnicodeNormalizationTables.NormalizationTuples;

        for (var index = 0; index < text.Length;)
        {
            var codePoint = Unicode.DecodeAt(text, index, out var length);
            index += length;

            // The worst expansion of one code point is the longest compatibility decomposition, in code points.
            if (buffer.Length - count < UnicodeNormalizationTables.MaxCompatibilityDecompositionLength)
            {
                Grow(ref buffer, count);
            }

            var start = count;
            if (isCompatibility && TryFindCompatibility(codePoint, out var decomposition))
            {
                for (var i = 0; i < decomposition.Length;)
                {
                    var unit = (char)decomposition[i];
                    if (char.IsHighSurrogate(unit))
                    {
                        buffer[count++] = char.ConvertToUtf32(unit, (char)decomposition[i + 1]);
                        i += 2;
                    }
                    else
                    {
                        buffer[count++] = unit;
                        i++;
                    }
                }
            }
            else
            {
                var written = DecomposeCanonical(codePoint, canonical);
                canonical[..written].CopyTo(buffer.AsSpan(count));
                count += written;
            }

            // Canonical ordering: each run of characters whose combining class is not 0 is sorted by class, and the
            // characters of the same class keep their order.
            for (var i = start; i < count; i++)
            {
                if (tuples[UnicodeNormalizationTables.GetNormalization(buffer[i]) * 2] == 0)
                {
                    Reorder(buffer.AsSpan(runStart, i - runStart));
                    runStart = i + 1;
                }
            }
        }

        Reorder(buffer.AsSpan(runStart, count - runStart));
        if (form is NormalizationForm.FormC or NormalizationForm.FormKC)
        {
            count = Compose(buffer.AsSpan(0, count));
        }

        return buffer;
    }

    private static void Grow(ref int[] buffer, int count)
    {
        var larger = ArrayPool<int>.Shared.Rent(checked(buffer.Length * 2));
        buffer.AsSpan(0, count).CopyTo(larger);
        ArrayPool<int>.Shared.Return(buffer);
        buffer = larger;
    }

    private static bool TryFindCompatibility(int codePoint, out ReadOnlySpan<ushort> decomposition)
    {
        var index = UnicodeNormalizationTables.CompatibilityKeys.BinarySearch(codePoint);
        if (index < 0)
        {
            decomposition = default;
            return false;
        }

        var entry = UnicodeNormalizationTables.CompatibilityEntries[index];
        decomposition = UnicodeNormalizationTables.CompatibilityPool.Slice(entry >> 6, entry & 0x3F);
        return true;
    }

    /// <summary>Sorts a run of characters whose combining class is not 0 by class, keeping the order of equal classes.</summary>
    private static void Reorder(Span<int> run)
    {
        if (run.Length < 2)
            return;

        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        if (run.Length <= InsertionSortLimit)
        {
            for (var i = 1; i < run.Length; i++)
            {
                var value = run[i];
                var valueClass = tuples[UnicodeNormalizationTables.GetNormalization(value) * 2];
                var j = i;
                while (j > 0 && tuples[UnicodeNormalizationTables.GetNormalization(run[j - 1]) * 2] > valueClass)
                {
                    run[j] = run[j - 1];
                    j--;
                }

                run[j] = value;
            }

            return;
        }

        // A long run, which only a hostile text has: sort keys made of the class, the position and the code point,
        // so that the cost stays in n log n and the order of equal classes is kept.
        var keys = ArrayPool<long>.Shared.Rent(run.Length);
        try
        {
            for (var i = 0; i < run.Length; i++)
            {
                long combiningClass = tuples[UnicodeNormalizationTables.GetNormalization(run[i]) * 2];
                keys[i] = (combiningClass << 53) | ((long)i << 21) | (uint)run[i];
            }

            keys.AsSpan(0, run.Length).Sort();
            for (var i = 0; i < run.Length; i++)
            {
                run[i] = (int)(keys[i] & 0x1FFFFF);
            }
        }
        finally
        {
            ArrayPool<long>.Shared.Return(keys);
        }
    }

    /// <summary>Applies the canonical composition algorithm in place and returns the new length.</summary>
    private static int Compose(Span<int> buffer)
    {
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        var starter = -1;
        var lastClass = 0;
        var count = 0;
        foreach (var codePoint in buffer)
        {
            var combiningClass = tuples[UnicodeNormalizationTables.GetNormalization(codePoint) * 2];

            // A character combines with the last starter unless a character between them blocks it: one whose class
            // is 0 or is not lower than its own.
            if (starter >= 0 && (lastClass == 0 || lastClass < combiningClass) && TryComposeCanonical(buffer[starter], codePoint, out var composite))
            {
                buffer[starter] = composite;
                continue;
            }

            if (combiningClass == 0)
            {
                starter = count;
            }

            lastClass = combiningClass;
            buffer[count++] = codePoint;
        }

        return count;
    }

    private static int GetUtf16Length(ReadOnlySpan<int> codePoints)
    {
        var length = codePoints.Length;
        foreach (var codePoint in codePoints)
        {
            if (codePoint > 0xFFFF)
            {
                length++;
            }
        }

        return length;
    }

    private static void Encode(ReadOnlySpan<int> codePoints, Span<char> destination)
    {
        var index = 0;
        foreach (var codePoint in codePoints)
        {
            if (codePoint > 0xFFFF)
            {
                var value = codePoint - 0x10000;
                destination[index++] = (char)(0xD800 + (value >> 10));
                destination[index++] = (char)(0xDC00 + (value & 0x3FF));
            }
            else
            {
                destination[index++] = (char)codePoint;
            }
        }
    }

    private static bool Matches(ReadOnlySpan<int> codePoints, ReadOnlySpan<char> text)
    {
        var index = 0;
        foreach (var codePoint in codePoints)
        {
            if (index >= text.Length || Unicode.DecodeAt(text, index, out var length) != codePoint)
                return false;

            index += length;
        }

        return index == text.Length;
    }

    private enum QuickCheckResult
    {
        Yes,
        No,
        Maybe,
    }
}
