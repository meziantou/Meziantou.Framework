using System.Buffers;

// FormKC and FormKD are marked unsupported in the browser because string.Normalize does not implement them there; this
// class implements every form itself, so they are supported on every platform.
#pragma warning disable CA1416

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
        if (QuickCheck(value, form, out var prefix) is QuickCheckResult.Yes)
            return value;

        var buffer = Normalize(value, form, prefix, out var length);
        try
        {
            var result = buffer.AsSpan(0, length);
            return result.SequenceEqual(value) ? value : result.ToString();
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
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
        if (QuickCheck(source, form, out var prefix) is QuickCheckResult.Yes)
        {
            if (!source.TryCopyTo(destination))
                return false;

            charsWritten = source.Length;
            return true;
        }

        var buffer = Normalize(source, form, prefix, out var length);
        try
        {
            if (!buffer.AsSpan(0, length).TryCopyTo(destination))
                return false;

            charsWritten = length;
            return true;
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
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
        if (QuickCheck(source, form, out var prefix) is QuickCheckResult.Yes)
            return source.Length;

        var buffer = Normalize(source, form, prefix, out var length);
        ArrayPool<char>.Shared.Return(buffer);
        return length;
    }

    /// <summary>Normalizes a text into a rented buffer, unless it is normalized already.</summary>
    /// <param name="text">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <param name="length">The length of the normalized text in the buffer.</param>
    /// <returns>A buffer of <see cref="ArrayPool{T}.Shared"/> that the caller returns, or <see langword="null"/> when <paramref name="text"/> is normalized already.</returns>
    internal static char[]? NormalizeToRentedBuffer(ReadOnlySpan<char> text, NormalizationForm form, out int length)
    {
        if (QuickCheck(text, form, out var prefix) is QuickCheckResult.Yes)
        {
            length = text.Length;
            return null;
        }

        return Normalize(text, form, prefix, out length);
    }

    /// <summary>Determines whether a text is in a normalization form.</summary>
    /// <param name="value">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <returns><see langword="true"/> when normalizing the text would not change it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="form"/> is not a defined value.</exception>
    public static bool IsNormalized(ReadOnlySpan<char> value, NormalizationForm form = NormalizationForm.FormC)
    {
        ValidateForm(form);
        switch (QuickCheck(value, form, out var prefix))
        {
            case QuickCheckResult.Yes:
                return true;

            case QuickCheckResult.No:
                return false;
        }

        var buffer = Normalize(value, form, prefix, out var length);
        try
        {
            return buffer.AsSpan(0, length).SequenceEqual(value);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(buffer);
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
            var compositions = UnicodeNormalizationTables.Compositions;
            var offset = UnicodeTableReader.FindRecord(compositions, 9, first, second);
            if (offset >= 0)
            {
                composite = UnicodeTableReader.ReadUInt24(compositions, offset + 6);
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
        var mappings = UnicodeNormalizationTables.CanonicalMappings;
        var offset = UnicodeTableReader.FindRecord(mappings, 9, codePoint);
        if (offset < 0)
        {
            first = codePoint;
            second = 0;
            return false;
        }

        first = UnicodeTableReader.ReadUInt24(mappings, offset + 3);
        second = UnicodeTableReader.ReadUInt24(mappings, offset + 6);
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

    /// <summary>Applies the quick check algorithm of UAX #15.</summary>
    /// <param name="text">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <param name="prefix">
    /// The length of the longest prefix that is normalized and that the rest of the text cannot change: it ends before
    /// a starter that the quick check accepts and that is before the first character it does not accept. The length
    /// of the text when the result is <see cref="QuickCheckResult.Yes"/>.
    /// </param>
    private static QuickCheckResult QuickCheck(ReadOnlySpan<char> text, NormalizationForm form, out int prefix)
    {
        var (no, maybe, unchanged) = GetQuickCheckParameters(form);

        // Skip the characters below the limit at the start of the text with a vectorized search: much text is ASCII.
        var start = text.IndexOfAnyInRange((char)unchanged, char.MaxValue);
        if (start < 0)
        {
            prefix = text.Length;
            return QuickCheckResult.Yes;
        }

        var result = QuickCheck(text, start, unchanged, no, maybe, out var firstRejected);
        prefix = result is QuickCheckResult.Yes ? text.Length : GetStablePrefixLength(text, firstRejected);
        return result;
    }

    /// <summary>Gets the flags of the quick check of a form, and the first character the form can change.</summary>
    private static (int No, int Maybe, int Unchanged) GetQuickCheckParameters(NormalizationForm form)
    {
        // The characters below the limit are starters that no form changes: U+00C0 is the first character that has a
        // canonical decomposition, and U+00A0 the first one that has a compatibility decomposition.
        return form switch
        {
            NormalizationForm.FormD => (NfdNo, 0, 0xC0),
            NormalizationForm.FormKD => (NfkdNo, 0, 0xA0),
            NormalizationForm.FormC => (NfcNo, NfcMaybe, 0xC0),
            _ => (NfkcNo, NfkcMaybe, 0xA0),
        };
    }

    /// <summary>Applies the quick check from an index.</summary>
    /// <param name="firstRejected">The index of the first character whose quick check value is not Yes, or that is not in canonical order.</param>
    private static QuickCheckResult QuickCheck(ReadOnlySpan<char> text, int index, int unchanged, int no, int maybe, out int firstRejected)
    {
        var result = QuickCheckResult.Yes;
        var lastClass = 0;
        firstRejected = -1;
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        while (index < text.Length)
        {
            if (text[index] < unchanged)
            {
                lastClass = 0;
                index = SkipUnchanged(text, index, unchanged);
                continue;
            }

            var entry = UnicodeNormalizationTables.GetNormalization(Unicode.DecodeAt(text, index, out var length)) * 2;
            index += length;
            var combiningClass = tuples[entry];
            var flags = tuples[entry + 1];
            if ((flags & no) != 0 || (combiningClass != 0 && lastClass > combiningClass))
            {
                if (result is QuickCheckResult.Yes)
                {
                    firstRejected = index - length;
                }

                return QuickCheckResult.No;
            }

            if ((flags & maybe) != 0 && result is QuickCheckResult.Yes)
            {
                firstRejected = index - length;
                result = QuickCheckResult.Maybe;
            }

            lastClass = combiningClass;
        }

        return result;
    }

    /// <summary>Skips the characters that no form changes from an index: they are starters, and are not looked up.</summary>
    /// <returns>The index of the first character at or after <paramref name="index"/> that is not below <paramref name="unchanged"/>, or the length of the text.</returns>
    /// <remarks>A loop of its own keeps the code of the loop compact, whatever the layout of the code of its caller.</remarks>
    private static int SkipUnchanged(ReadOnlySpan<char> text, int index, int unchanged)
    {
        // Four ASCII characters at a time, as most of a text written with the Latin alphabet is ASCII: the runs are
        // words, too short for a vectorized search.
        while (index <= text.Length - 4)
        {
            var four = text.Slice(index, 4);
            if ((four[0] | four[1] | four[2] | four[3]) >= 0x80)
                break;

            index += 4;
        }

        while ((uint)index < (uint)text.Length && text[index] < unchanged)
        {
            index++;
        }

        return index;
    }

    /// <summary>Gets the length of the text before the last starter that is before an index.</summary>
    /// <remarks>
    /// The quick check accepts every character before the index: the text before such a starter is normalized, and
    /// the rest of the text cannot change it, since nothing reorders or composes across a starter that the quick check
    /// accepts.
    /// </remarks>
    private static int GetStablePrefixLength(ReadOnlySpan<char> text, int index, int start = 0)
    {
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        while (index > start)
        {
            var codePoint = Unicode.DecodeBefore(text, index, out var length);
            index -= length;
            if (tuples[UnicodeNormalizationTables.GetNormalization(codePoint) * 2] == 0)
                return index;
        }

        return start;
    }

    /// <summary>Normalizes a text into a rented buffer, which the caller returns to the shared pool.</summary>
    /// <param name="text">The text.</param>
    /// <param name="form">The normalization form.</param>
    /// <param name="prefix">The length of the start of the text that is normalized already, as <see cref="QuickCheck(ReadOnlySpan{char}, NormalizationForm, out int)"/> returns it.</param>
    /// <param name="length">The length of the normalized text.</param>
    /// <remarks>
    /// Only the segments around the characters that the quick check rejects are normalized: a segment starts at the
    /// last starter before such a character, and ends before the next starter that the quick check accepts. Nothing
    /// reorders or composes across such a starter, so the text between two segments is copied as it is.
    /// </remarks>
    private static char[] Normalize(ReadOnlySpan<char> text, NormalizationForm form, int prefix, out int length)
    {
        var (no, maybe, unchanged) = GetQuickCheckParameters(form);
        var output = ArrayPool<char>.Shared.Rent(Math.Max(16, text.Length + (text.Length / 2)));
        var codePoints = ArrayPool<int>.Shared.Rent(64);
        try
        {
            text[..prefix].CopyTo(output);
            length = prefix;
            var index = prefix;
            while (index < text.Length)
            {
                // The text from the index is a segment that contains a character the quick check rejects.
                var end = FindSegmentEnd(text, index, unchanged, no | maybe);
                var count = NormalizeSegment(text[index..end], form, ref codePoints);
                var segmentLength = GetUtf16Length(codePoints.AsSpan(0, count));
                EnsureCapacity(ref output, length, segmentLength);
                Encode(codePoints.AsSpan(0, count), output.AsSpan(length));
                length += segmentLength;
                index = end;

                // The text the quick check accepts after the segment is normalized: copy it, up to the last starter
                // before the next character the quick check rejects.
                var rejected = FindFirstRejected(text, index, unchanged, no | maybe);
                var stable = rejected < text.Length ? GetStablePrefixLength(text, rejected, index) : text.Length;
                EnsureCapacity(ref output, length, stable - index);
                text[index..stable].CopyTo(output.AsSpan(length));
                length += stable - index;
                index = stable;
            }

            return output;
        }
        catch
        {
            ArrayPool<char>.Shared.Return(output);
            throw;
        }
        finally
        {
            ArrayPool<int>.Shared.Return(codePoints);
        }
    }

    private static void EnsureCapacity(ref char[] buffer, int length, int additional)
    {
        var required = length + additional;
        if (required <= buffer.Length)
            return;

        var larger = ArrayPool<char>.Shared.Rent(Math.Max(required, checked(buffer.Length * 2)));
        buffer.AsSpan(0, length).CopyTo(larger);
        ArrayPool<char>.Shared.Return(buffer);
        buffer = larger;
    }

    /// <summary>Gets the index of the first character the quick check rejects from an index, or the length of the text.</summary>
    /// <remarks>The index is the start of the text, or a starter the quick check accepts: the canonical order of the marks is checked from there.</remarks>
    private static int FindFirstRejected(ReadOnlySpan<char> text, int index, int unchanged, int rejectedFlags)
    {
        var lastClass = 0;
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        while (index < text.Length)
        {
            if (text[index] < unchanged)
            {
                lastClass = 0;
                index = SkipUnchanged(text, index, unchanged);
                continue;
            }

            var entry = UnicodeNormalizationTables.GetNormalization(Unicode.DecodeAt(text, index, out var length)) * 2;
            var combiningClass = tuples[entry];
            if ((tuples[entry + 1] & rejectedFlags) != 0 || (combiningClass != 0 && lastClass > combiningClass))
                return index;

            lastClass = combiningClass;
            index += length;
        }

        return index;
    }

    /// <summary>Gets the end of a segment to normalize: the index of the first starter the quick check accepts after its first character, or the length of the text.</summary>
    private static int FindSegmentEnd(ReadOnlySpan<char> text, int start, int unchanged, int rejectedFlags)
    {
        _ = Unicode.DecodeAt(text, start, out var index);
        index += start;
        var tuples = UnicodeNormalizationTables.NormalizationTuples;
        while (index < text.Length)
        {
            if (text[index] < unchanged)
                return index;

            var entry = UnicodeNormalizationTables.GetNormalization(Unicode.DecodeAt(text, index, out var length)) * 2;
            if (tuples[entry] == 0 && (tuples[entry + 1] & rejectedFlags) == 0)
                return index;

            index += length;
        }

        return index;
    }

    /// <summary>Normalizes a segment into a buffer of code points, which it grows from the shared pool when needed.</summary>
    /// <returns>The number of code points of the normalized segment.</returns>
    private static int NormalizeSegment(ReadOnlySpan<char> text, NormalizationForm form, ref int[] buffer)
    {
        var isCompatibility = form is NormalizationForm.FormKC or NormalizationForm.FormKD;
        var decomposable = isCompatibility ? NfkdNo : NfdNo;
        var count = 0;
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

            // Most code points have no decomposition: they are written as they are, without a search of the mappings.
            var entry = UnicodeNormalizationTables.GetNormalization(codePoint) * 2;
            if ((tuples[entry + 1] & decomposable) == 0)
            {
                buffer[count++] = codePoint;
                if (tuples[entry] == 0)
                {
                    Reorder(buffer.AsSpan(runStart, count - 1 - runStart));
                    runStart = count;
                }

                continue;
            }

            var start = count;
            if (isCompatibility && TryFindCompatibility(codePoint, out var decomposition))
            {
                // UTF-16 code units, two bytes each in little-endian order.
                for (var i = 0; i < decomposition.Length;)
                {
                    var unit = (char)(decomposition[i] | (decomposition[i + 1] << 8));
                    if (char.IsHighSurrogate(unit))
                    {
                        buffer[count++] = char.ConvertToUtf32(unit, (char)(decomposition[i + 2] | (decomposition[i + 3] << 8)));
                        i += 4;
                    }
                    else
                    {
                        buffer[count++] = unit;
                        i += 2;
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

        return count;
    }

    private static void Grow(ref int[] buffer, int count)
    {
        var larger = ArrayPool<int>.Shared.Rent(checked(buffer.Length * 2));
        buffer.AsSpan(0, count).CopyTo(larger);
        ArrayPool<int>.Shared.Return(buffer);
        buffer = larger;
    }

    private static bool TryFindCompatibility(int codePoint, out ReadOnlySpan<byte> decomposition)
    {
        var mappings = UnicodeNormalizationTables.CompatibilityMappings;
        var offset = UnicodeTableReader.FindRecord(mappings, 9, codePoint);
        if (offset < 0)
        {
            decomposition = default;
            return false;
        }

        // The offset and the length are in UTF-16 code units; the pool holds two bytes per code unit.
        var start = UnicodeTableReader.ReadUInt24(mappings, offset + 3);
        var length = UnicodeTableReader.ReadUInt24(mappings, offset + 6);
        decomposition = UnicodeNormalizationTables.CompatibilityPool.Slice(start * 2, length * 2);
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
            var entry = UnicodeNormalizationTables.GetNormalization(codePoint) * 2;
            var combiningClass = tuples[entry];

            // A character combines with the last starter unless a character between them blocks it: one whose class
            // is 0 or is not lower than its own. Only the characters whose NFC_Quick_Check is Maybe are the second
            // character of a composition: the others are not searched for.
            if (starter >= 0 && (tuples[entry + 1] & (NfcMaybe | NfkcMaybe)) != 0 && (lastClass == 0 || lastClass < combiningClass)
                && TryComposeCanonical(buffer[starter], codePoint, out var composite))
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

    private enum QuickCheckResult
    {
        Yes,
        No,
        Maybe,
    }
}
