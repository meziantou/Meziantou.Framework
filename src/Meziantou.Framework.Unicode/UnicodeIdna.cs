using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;

namespace Meziantou.Framework;

/// <summary>Converts internationalized domain names with Unicode IDNA Compatibility Processing (Unicode Technical Standard #46).</summary>
/// <remarks>
/// <para>
/// The conversion does not depend on the runtime or on ICU, unlike <see cref="System.Globalization.IdnMapping"/>: the
/// mapping table and the normalization are the ones of <see cref="UnicodeProperties.UnicodeVersion"/>, so the results
/// are the same on every platform and in globalization-invariant mode.
/// </para>
/// <para>The methods never throw for an invalid domain name: they report it.</para>
/// </remarks>
/// <seealso href="https://www.unicode.org/reports/tr46/" />
public static class UnicodeIdna
{
    private const string AcePrefix = "xn--";

    // The longest label, in code points, that can be encoded within the 63 characters of a DNS label: Punycode writes
    // at least one character per code point, after the ACE prefix.
    private const int MaxEncodableLabelLength = 63 - 4;

    // The length of the buffers on the stack. A domain name is at most 253 characters long in the DNS, and most are much
    // shorter: a longer one rents its buffers.
    private const int StackallocLength = 128;

    /// <summary>Converts a domain name to its ASCII form (ToASCII), with the default options.</summary>
    /// <param name="domainName">The domain name, such as <c>Bücher.example</c>.</param>
    /// <param name="result">The ASCII form, such as <c>xn--bcher-kva.example</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the domain name is valid.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr46/#ToASCII" />
    public static bool TryToAscii(string domainName, [NotNullWhen(true)] out string? result) => TryToAscii(domainName, options: null, out result);

    /// <summary>Converts a domain name to its ASCII form (ToASCII).</summary>
    /// <param name="domainName">The domain name, such as <c>Bücher.example</c>.</param>
    /// <param name="options">The options of the processing, or <see langword="null"/> for <see cref="UnicodeIdnaOptions.Default"/>.</param>
    /// <param name="result">The ASCII form, such as <c>xn--bcher-kva.example</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the domain name is valid.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr46/#ToASCII" />
    public static bool TryToAscii(string domainName, UnicodeIdnaOptions? options, [NotNullWhen(true)] out string? result)
    {
        ArgumentNullException.ThrowIfNull(domainName);
        options ??= UnicodeIdnaOptions.Default;

        result = null;
        if (TryProcessAscii(domainName, options, out var asciiResult, out var asciiHasErrors))
        {
            if (asciiHasErrors || (options.VerifyDnsLength && !HasValidDnsLength(asciiResult)))
                return false;

            result = asciiResult;
            return true;
        }

        // An ACE label is the Punycode encoding of its decoded form, which is unique: when it is too long for the DNS,
        // so is the label of the result, and it does not need to be decoded.
        var labels = new CharBuffer(stackalloc char[StackallocLength]);
        var output = new CharBuffer(stackalloc char[StackallocLength]);
        try
        {
            Process(domainName, options, maxAceLabelLength: options.VerifyDnsLength ? 63 : int.MaxValue, ref labels, out var hasErrors);
            if (hasErrors)
                return false;

            for (var position = 0; position >= 0;)
            {
                if (position > 0)
                {
                    output.Append('.');
                }

                var label = ReadLabel(labels.AsSpan(), ref position);
                var start = output.Length;
                if (Ascii.IsValid(label))
                {
                    output.Append(label);
                }
                else
                {
                    // Encoding is quadratic in the length of the label: do not encode a label that cannot fit anyway.
                    if (options.VerifyDnsLength && GetCodePointCount(label) > MaxEncodableLabelLength)
                        return false;

                    output.Append(AcePrefix);
                    if (!Punycode.TryEncode(label, ref output))
                        return false;
                }

                // A4_1 and A4_2: an empty label, including the root label after a trailing dot, is an error
                if (options.VerifyDnsLength && output.Length - start is < 1 or > 63)
                    return false;
            }

            if (options.VerifyDnsLength && output.Length > 253)
                return false;

            result = output.AsSpan().ToString();
            return true;
        }
        finally
        {
            labels.Dispose();
            output.Dispose();
        }
    }

    /// <summary>Converts a domain name to its Unicode form (ToUnicode), with the default options.</summary>
    /// <param name="domainName">The domain name, such as <c>xn--bcher-kva.example</c>.</param>
    /// <param name="hasErrors"><see langword="true"/> when the domain name is not valid; the result is still converted as much as possible.</param>
    /// <returns>The Unicode form, such as <c>bücher.example</c>.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr46/#ToUnicode" />
    public static string ToUnicode(string domainName, out bool hasErrors) => ToUnicode(domainName, options: null, out hasErrors);

    /// <summary>Converts a domain name to its Unicode form (ToUnicode).</summary>
    /// <param name="domainName">The domain name, such as <c>xn--bcher-kva.example</c>.</param>
    /// <param name="options">The options of the processing, or <see langword="null"/> for <see cref="UnicodeIdnaOptions.Default"/>. <see cref="UnicodeIdnaOptions.VerifyDnsLength"/> is not used.</param>
    /// <param name="hasErrors"><see langword="true"/> when the domain name is not valid; the result is still converted as much as possible.</param>
    /// <returns>The Unicode form, such as <c>bücher.example</c>.</returns>
    /// <seealso href="https://www.unicode.org/reports/tr46/#ToUnicode" />
    public static string ToUnicode(string domainName, UnicodeIdnaOptions? options, out bool hasErrors)
    {
        ArgumentNullException.ThrowIfNull(domainName);
        options ??= UnicodeIdnaOptions.Default;
        if (TryProcessAscii(domainName, options, out var asciiResult, out hasErrors))
        {
            // X4_2: an empty label is an error, except the root label after a trailing dot
            if (asciiResult.Length == 0 || asciiResult[0] == '.' || asciiResult.Contains("..", StringComparison.Ordinal))
            {
                hasErrors = true;
            }

            return asciiResult;
        }

        var labels = new CharBuffer(stackalloc char[StackallocLength]);
        try
        {
            Process(domainName, options, maxAceLabelLength: int.MaxValue, ref labels, out hasErrors);

            // X4_2: an empty label is an error, except the root label after a trailing dot
            var value = labels.AsSpan();
            if (value.IsEmpty || value[0] == '.' || value.Contains("..", StringComparison.Ordinal))
            {
                hasErrors = true;
            }

            return value.ToString();
        }
        finally
        {
            labels.Dispose();
        }
    }

    /// <summary>Encodes a label with Punycode (RFC 3492), without the <c>xn--</c> prefix.</summary>
    /// <param name="value">The label, such as <c>bücher</c>. The case of the letters is kept.</param>
    /// <param name="result">The encoded label, such as <c>bcher-kva</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the label is encoded; <see langword="false"/> when the encoding overflows, or when the label has an unpaired surrogate, which has no code point to encode.</returns>
    /// <seealso href="https://www.rfc-editor.org/rfc/rfc3492" />
    public static bool TryEncodePunycode(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? result)
    {
        var output = new CharBuffer(stackalloc char[StackallocLength]);
        try
        {
            result = Punycode.TryEncode(value, ref output) ? output.AsSpan().ToString() : null;
            return result is not null;
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>Decodes a label encoded with Punycode (RFC 3492), without the <c>xn--</c> prefix.</summary>
    /// <param name="value">The encoded label, such as <c>bcher-kva</c>.</param>
    /// <param name="result">The decoded label, such as <c>bücher</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the label is valid Punycode.</returns>
    /// <seealso href="https://www.rfc-editor.org/rfc/rfc3492" />
    public static bool TryDecodePunycode(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? result)
    {
        var output = new CharBuffer(stackalloc char[StackallocLength]);
        try
        {
            result = Punycode.TryDecode(value, ref output) ? output.AsSpan().ToString() : null;
            return result is not null;
        }
        finally
        {
            output.Dispose();
        }
    }

    /// <summary>Processes a domain name made of ASCII letters, digits, hyphens and dots only, which is the common case.</summary>
    /// <returns><see langword="false"/> when the domain name has other characters, or a label that needs the full processing.</returns>
    /// <remarks>
    /// Such a domain name maps to its lowercase form, is normalized, has no bidi character and no joiner, and every
    /// one of its characters is valid with the STD3 rules: only the hyphens of its labels remain to be checked.
    /// </remarks>
    private static bool TryProcessAscii(string domainName, UnicodeIdnaOptions options, [NotNullWhen(true)] out string? result, out bool hasErrors)
    {
        result = null;
        hasErrors = false;

        // A label with "--" can be an ACE label to decode, or is invalid because of its hyphens: leave it to the full
        // processing.
        foreach (var c in domainName)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c is not ('-' or '.'))
                return false;
        }

        if (domainName.Contains("--", StringComparison.Ordinal))
            return false;

        result = domainName.AsSpan().ContainsAnyInRange('A', 'Z')
            ? string.Create(domainName.Length, domainName, (destination, value) => Ascii.ToLower(value, destination, out _))
            : domainName;

        if (options.CheckHyphens)
        {
            foreach (var range in result.AsSpan().Split('.'))
            {
                var label = result.AsSpan(range);
                if (label.Length > 0 && (label[0] == '-' || label[^1] == '-'))
                {
                    hasErrors = true;
                }
            }
        }

        return true;
    }

    /// <summary>Checks the lengths of the labels and of an ASCII domain name (VerifyDnsLength).</summary>
    private static bool HasValidDnsLength(string domainName)
    {
        // A4_1 and A4_2: an empty label, including the root label after a trailing dot, is an error
        if (domainName.Length > 253)
            return false;

        foreach (var range in domainName.AsSpan().Split('.'))
        {
            if (range.GetOffsetAndLength(domainName.Length).Length is < 1 or > 63)
                return false;
        }

        return true;
    }

    private static int GetCodePointCount(ReadOnlySpan<char> value)
    {
        var count = value.Length;
        for (var i = 0; i < value.Length - 1; i++)
        {
            if (char.IsHighSurrogate(value[i]) && char.IsLowSurrogate(value[i + 1]))
            {
                count--;
                i++;
            }
        }

        return count;
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Processing</summary>
    /// <param name="domainName">The domain name.</param>
    /// <param name="options">The options.</param>
    /// <param name="maxAceLabelLength">The length above which an ACE label is an error without being decoded.</param>
    /// <param name="labels">Receives the labels, converted, separated by dots.</param>
    /// <param name="hasErrors">Whether an error was recorded.</param>
    private static void Process(string domainName, UnicodeIdnaOptions options, int maxAceLabelLength, ref CharBuffer labels, out bool hasErrors)
    {
        hasErrors = false;

        // 1. Map
        var mapped = new CharBuffer(stackalloc char[StackallocLength]);
        char[]? normalizedBuffer = null;
        try
        {
            foreach (var rune in domainName.EnumerateRunes())
            {
                switch (GetStatus(rune.Value))
                {
                    case IdnaStatus.Ignored:
                        break;

                    case IdnaStatus.Mapped:
                        if (options.TransitionalProcessing && rune.Value == 0x1E9E)
                        {
                            mapped.Append("ss");
                        }
                        else
                        {
                            AppendMapping(ref mapped, rune.Value);
                        }

                        break;

                    case IdnaStatus.Deviation when options.TransitionalProcessing:
                        AppendMapping(ref mapped, rune.Value);
                        break;

                    default:
                        mapped.Append(rune);
                        break;
                }
            }

            // 2. Normalize
            normalizedBuffer = UnicodeNormalization.NormalizeToRentedBuffer(mapped.AsSpan(), NormalizationForm.FormC, out var normalizedLength);
            var normalized = normalizedBuffer is null ? mapped.AsSpan() : normalizedBuffer.AsSpan(0, normalizedLength);

            // 3. Break, and 4. Convert: the labels, decoded, are written to the result
            for (var position = 0; position >= 0;)
            {
                if (position > 0)
                {
                    labels.Append('.');
                }

                var label = ReadLabel(normalized, ref position);
                if (!label.StartsWith(AcePrefix, StringComparison.Ordinal))
                {
                    labels.Append(label);
                    continue;
                }

                if (!Ascii.IsValid(label) || label.Length > maxAceLabelLength)
                {
                    hasErrors = true;
                    labels.Append(label);
                    continue;
                }

                var start = labels.Length;
                if (!Punycode.TryDecode(label[AcePrefix.Length..], ref labels))
                {
                    labels.Truncate(start);
                    labels.Append(label);
                    if (!options.IgnoreInvalidPunycode)
                    {
                        hasErrors = true;
                        continue;
                    }
                }

                // A label kept with IgnoreInvalidPunycode is ASCII, so it is an error too
                var decoded = labels.AsSpan()[start..];
                if (decoded.IsEmpty || Ascii.IsValid(decoded))
                {
                    hasErrors = true;
                }
            }

            // 4. Validate. The bidi rules apply to every label when the domain name is a Bidi domain name. An ACE label
            // is validated as nontransitional; when it is not decoded, an error is recorded already.
            var isBidiDomainName = options.CheckBidi && IsBidiDomainName(labels.AsSpan());
            var sourcePosition = 0;
            for (var position = 0; position >= 0;)
            {
                var label = ReadLabel(labels.AsSpan(), ref position);
                var transitional = options.TransitionalProcessing && !ReadLabel(normalized, ref sourcePosition).StartsWith(AcePrefix, StringComparison.Ordinal);
                if (!IsValidLabel(label, options, transitional, isBidiDomainName))
                {
                    hasErrors = true;
                }
            }
        }
        finally
        {
            mapped.Dispose();
            if (normalizedBuffer is not null)
            {
                ArrayPool<char>.Shared.Return(normalizedBuffer);
            }
        }
    }

    /// <summary>Reads the label of a domain name that starts at a position, as <c>Split('.')</c> does.</summary>
    /// <param name="domainName">The domain name.</param>
    /// <param name="position">The start of the label; on return, the start of the next label, or -1 after the last one.</param>
    /// <returns>The label.</returns>
    private static ReadOnlySpan<char> ReadLabel(ReadOnlySpan<char> domainName, ref int position)
    {
        var rest = domainName[position..];
        var length = rest.IndexOf('.');
        if (length < 0)
        {
            position = -1;
            return rest;
        }

        position += length + 1;
        return rest[..length];
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Validity_Criteria</summary>
    private static bool IsValidLabel(ReadOnlySpan<char> label, UnicodeIdnaOptions options, bool transitional, bool isBidiDomainName)
    {
        if (label.Length == 0)
            return true;

        if (!UnicodeNormalization.IsNormalized(label, NormalizationForm.FormC))
            return false;

        if (options.CheckHyphens)
        {
            if (label.Length >= 4 && label[2] == '-' && label[3] == '-')
                return false;

            if (label[0] == '-' || label[^1] == '-')
                return false;
        }
        else if (label.StartsWith(AcePrefix, StringComparison.Ordinal))
        {
            return false;
        }

        if (label.Contains('.'))
            return false;

        if (UnicodeProperties.GetGeneralCategory(GetCodePointAt(label, 0)) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return false;

        var hasJoiner = false;
        foreach (var rune in label.EnumerateRunes())
        {
            var codePoint = rune.Value;
            var status = GetStatus(codePoint);
            if (status != IdnaStatus.Valid && (transitional || status != IdnaStatus.Deviation))
                return false;

            if (options.UseStd3AsciiRules && codePoint < 0x80 && !(char.IsAsciiLetterLower((char)codePoint) || char.IsAsciiDigit((char)codePoint) || codePoint == '-'))
                return false;

            hasJoiner |= codePoint is 0x200C or 0x200D;
        }

        if (hasJoiner && options.CheckJoiners && !SatisfiesContextJ(label))
            return false;

        if (isBidiDomainName && !SatisfiesBidiRule(label))
            return false;

        return true;
    }

    /// <summary>Returns the code point that starts at <paramref name="index"/>.</summary>
    private static int GetCodePointAt(ReadOnlySpan<char> value, int index)
    {
        Rune.DecodeFromUtf16(value[index..], out var rune, out _);
        return rune.Value;
    }

    /// <summary>ContextJ rules: RFC 5892 Appendix A.1 and A.2.</summary>
    private static bool SatisfiesContextJ(ReadOnlySpan<char> label)
    {
        const int Virama = 9;
        for (var i = 0; i < label.Length; i++)
        {
            // The joiners are in the BMP: they are one UTF-16 code unit, which no surrogate pair contains
            var codePoint = label[i];
            if (codePoint is not ('\u200C' or '\u200D'))
                continue;

            if (i > 0)
            {
                Rune.DecodeLastFromUtf16(label[..i], out var previous, out _);
                if (UnicodeProperties.GetCanonicalCombiningClass(previous.Value) == Virama)
                    continue;
            }

            if (codePoint == '\u200D')
                return false;

            // (Joining_Type:{L,D})(Joining_Type:T)*U+200C(Joining_Type:T)*(Joining_Type:{R,D})
            var before = UnicodeJoiningType.NonJoining;
            for (var end = i; end > 0;)
            {
                Rune.DecodeLastFromUtf16(label[..end], out var rune, out var length);
                before = UnicodeProperties.GetJoiningType(rune.Value);
                if (before != UnicodeJoiningType.Transparent)
                    break;

                end -= length;
            }

            if (before is not (UnicodeJoiningType.LeftJoining or UnicodeJoiningType.DualJoining))
                return false;

            var after = UnicodeJoiningType.NonJoining;
            for (var start = i + 1; start < label.Length;)
            {
                Rune.DecodeFromUtf16(label[start..], out var rune, out var length);
                after = UnicodeProperties.GetJoiningType(rune.Value);
                if (after != UnicodeJoiningType.Transparent)
                    break;

                start += length;
            }

            if (after is not (UnicodeJoiningType.RightJoining or UnicodeJoiningType.DualJoining))
                return false;
        }

        return true;
    }

    /// <summary>RFC 5893 section 1.4: a Bidi domain name contains at least one character with Bidi_Class R, AL, or AN.</summary>
    /// <param name="labels">The labels, separated by dots: the class of a dot is CS, and a surrogate pair is never split by one.</param>
    private static bool IsBidiDomainName(ReadOnlySpan<char> labels)
    {
        // No code point below U+0590 is R, AL or AN, and the rest of a surrogate pair is above.
        var start = labels.IndexOfAnyInRange('\u0590', char.MaxValue);
        if (start < 0)
            return false;

        foreach (var rune in labels[start..].EnumerateRunes())
        {
            if (UnicodeProperties.GetBidiClass(rune.Value) is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic or UnicodeBidirectionalCategory.ArabicNumber)
                return true;
        }

        return false;
    }

    /// <summary>The Bidi Rule: RFC 5893 section 2.</summary>
    private static bool SatisfiesBidiRule(ReadOnlySpan<char> label)
    {
        var first = UnicodeProperties.GetBidiClass(GetCodePointAt(label, 0));

        // 1. The first character must be a character with Bidi property L, R, or AL
        if (first is not (UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic))
            return false;

        // The first character is not an NSM: it is the last one when every other one is an NSM
        var lastClass = first;
        if (first is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic)
        {
            var hasEuropeanNumber = false;
            var hasArabicNumber = false;
            foreach (var rune in label.EnumerateRunes())
            {
                var bidiClass = UnicodeProperties.GetBidiClass(rune.Value);

                // 2. In an RTL label, only characters with the Bidi properties R, AL, AN, EN, ES, CS, ET, ON, BN, or NSM are allowed
                if (bidiClass is not (UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic or UnicodeBidirectionalCategory.ArabicNumber
                    or UnicodeBidirectionalCategory.EuropeanNumber or UnicodeBidirectionalCategory.EuropeanSeparator or UnicodeBidirectionalCategory.CommonSeparator
                    or UnicodeBidirectionalCategory.EuropeanTerminator or UnicodeBidirectionalCategory.OtherNeutral or UnicodeBidirectionalCategory.BoundaryNeutral
                    or UnicodeBidirectionalCategory.NonspacingMark))
                {
                    return false;
                }

                if (bidiClass != UnicodeBidirectionalCategory.NonspacingMark)
                {
                    lastClass = bidiClass;
                }

                hasEuropeanNumber |= bidiClass == UnicodeBidirectionalCategory.EuropeanNumber;
                hasArabicNumber |= bidiClass == UnicodeBidirectionalCategory.ArabicNumber;
            }

            // 3. The end of the label must be a character with Bidi property R, AL, EN, or AN, followed by zero or more NSM
            if (lastClass is not (UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic or UnicodeBidirectionalCategory.EuropeanNumber or UnicodeBidirectionalCategory.ArabicNumber))
                return false;

            // 4. If an EN is present, no AN may be present, and vice versa
            return !(hasEuropeanNumber && hasArabicNumber);
        }

        foreach (var rune in label.EnumerateRunes())
        {
            var bidiClass = UnicodeProperties.GetBidiClass(rune.Value);

            // 5. In an LTR label, only characters with the Bidi properties L, EN, ES, CS, ET, ON, BN, or NSM are allowed
            if (bidiClass is not (UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.EuropeanNumber
                or UnicodeBidirectionalCategory.EuropeanSeparator or UnicodeBidirectionalCategory.CommonSeparator or UnicodeBidirectionalCategory.EuropeanTerminator
                or UnicodeBidirectionalCategory.OtherNeutral or UnicodeBidirectionalCategory.BoundaryNeutral or UnicodeBidirectionalCategory.NonspacingMark))
            {
                return false;
            }

            if (bidiClass != UnicodeBidirectionalCategory.NonspacingMark)
            {
                lastClass = bidiClass;
            }
        }

        // 6. The end of the label must be a character with Bidi property L or EN, followed by zero or more NSM
        return lastClass is UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.EuropeanNumber;
    }

    // The status of each ASCII character, and the character it maps to when it maps to a single ASCII character (an
    // uppercase letter): most domain names are mostly ASCII. Both are read from the tables.
    private static readonly IdnaStatus[] AsciiStatuses = CreateAsciiStatuses();
    private static readonly char[] AsciiMappings = CreateAsciiMappings();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static IdnaStatus GetStatus(int codePoint)
    {
        var statuses = AsciiStatuses;
        return (uint)codePoint < (uint)statuses.Length ? statuses[codePoint] : (IdnaStatus)UnicodeIdnaTables.GetIdna(codePoint);
    }

    private static IdnaStatus[] CreateAsciiStatuses()
    {
        var result = new IdnaStatus[128];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = (IdnaStatus)UnicodeIdnaTables.GetIdna(i);
        }

        return result;
    }

    private static char[] CreateAsciiMappings()
    {
        var result = new char[128];
        var mapping = new CharBuffer(stackalloc char[16]);
        try
        {
            for (var i = 0; i < result.Length; i++)
            {
                mapping.Truncate(0);
                AppendMappingFromTable(ref mapping, i);
                if (mapping.Length == 1 && mapping.AsSpan()[0] < 0x80)
                {
                    result[i] = mapping.AsSpan()[0];
                }
            }
        }
        finally
        {
            mapping.Dispose();
        }

        return result;
    }

    private static void AppendMapping(ref CharBuffer destination, int codePoint)
    {
        var mappings = AsciiMappings;
        if ((uint)codePoint < (uint)mappings.Length && mappings[codePoint] != 0)
        {
            destination.Append(mappings[codePoint]);
            return;
        }

        AppendMappingFromTable(ref destination, codePoint);
    }

    private static void AppendMappingFromTable(ref CharBuffer destination, int codePoint)
    {
        // 6-byte entries: the code point, then the offset and the length of its mapping in the pool. A deviation code
        // point that is not listed maps to nothing.
        var records = UnicodeIdnaTables.IdnaMappings;
        var offset = UnicodeTableReader.FindRecord(records, 6, codePoint);
        if (offset < 0)
            return;

        var start = records[offset + 3] | (records[offset + 4] << 8);
        var length = records[offset + 5];
        var pool = UnicodeIdnaTables.IdnaPool;
        var span = destination.AppendSpan(length);
        for (var i = 0; i < span.Length; i++)
        {
            var index = (start + i) * 2;
            span[i] = (char)(pool[index] | (pool[index + 1] << 8));
        }
    }

    private enum IdnaStatus : byte
    {
        Valid = 0,
        Ignored = 1,
        Mapped = 2,
        Deviation = 3,
        Disallowed = 4,
    }

    /// <summary>Punycode (RFC 3492).</summary>
    private static class Punycode
    {
        private const int Base = 36;
        private const int TMin = 1;
        private const int TMax = 26;
        private const int Skew = 38;
        private const int Damp = 700;
        private const int InitialBias = 72;
        private const int InitialN = 128;
        private const char Delimiter = '-';

        // The length of the labels whose working buffers are on the stack: a label of the DNS is at most 63 characters long.
        private const int StackallocLabelLength = 64;

        /// <summary>Decodes a Punycode string (without the "xn--" prefix).</summary>
        /// <returns><see langword="false"/> when the string is not valid Punycode; part of the output may be written then.</returns>
        /// <remarks>
        /// RFC 3492 inserts each decoded code point into the output, which is quadratic with a list. The insertions are
        /// recorded instead, then their final positions are computed in reverse order with a Fenwick tree of the free
        /// slots: the last insertion at position <c>i</c> takes the <c>i</c>-th slot, and so on.
        /// </remarks>
        public static bool TryDecode(scoped ReadOnlySpan<char> input, ref CharBuffer output)
        {
            // Every code point of the extended part takes at least one character. The buffer holds the values, the
            // positions, the decoded code points and the Fenwick tree.
            var bufferLength = (4 * input.Length) + 1;
            int[]? rented = null;
            Span<int> buffer = input.Length <= StackallocLabelLength ? stackalloc int[(4 * StackallocLabelLength) + 1] : (rented = ArrayPool<int>.Shared.Rent(bufferLength));
            try
            {
                return TryDecode(input, ref output, buffer[..bufferLength]);
            }
            finally
            {
                if (rented is not null)
                {
                    ArrayPool<int>.Shared.Return(rented);
                }
            }
        }

        private static bool TryDecode(scoped ReadOnlySpan<char> input, ref CharBuffer output, scoped Span<int> buffer)
        {
            var basicLength = Math.Max(input.LastIndexOf(Delimiter), 0);
            var values = buffer[..input.Length];
            var positions = buffer.Slice(input.Length, input.Length);
            var count = 0;
            for (var j = 0; j < basicLength; j++)
            {
                if (input[j] >= 0x80)
                    return false;

                values[count] = input[j];
                positions[count] = count;
                count++;
            }

            var n = InitialN;
            var i = 0;
            var bias = InitialBias;
            for (var index = basicLength > 0 ? basicLength + 1 : 0; index < input.Length;)
            {
                var oldI = i;
                var w = 1;
                for (var k = Base; ; k += Base)
                {
                    if (index >= input.Length)
                        return false;

                    var digit = DecodeDigit(input[index++]);
                    if (digit >= Base)
                        return false;

                    if (digit > (int.MaxValue - i) / w)
                        return false;

                    i += digit * w;
                    var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                    if (digit < t)
                        break;

                    if (w > int.MaxValue / (Base - t))
                        return false;

                    w *= Base - t;
                }

                bias = Adapt(i - oldI, count + 1, oldI == 0);
                if (i / (count + 1) > int.MaxValue - n)
                    return false;

                n += i / (count + 1);
                i %= count + 1;
                if (n > 0x10FFFF || n is >= 0xD800 and <= 0xDFFF)
                    return false;

                values[count] = n;
                positions[count] = i;
                count++;
                i++;
            }

            var freeSlots = new FenwickTree(buffer.Slice(2 * input.Length, count + 1), initialValue: 1);
            var codePoints = buffer.Slice((3 * input.Length) + 1, count);
            var length = 0;
            for (var k = count - 1; k >= 0; k--)
            {
                var slot = freeSlots.FindNth(positions[k] + 1);
                freeSlots.Add(slot, -1);
                codePoints[slot] = values[k];
                length += values[k] >= 0x10000 ? 2 : 1;
            }

            var destination = output.AppendSpan(length);
            var offset = 0;
            foreach (var codePoint in codePoints)
            {
                offset += new Rune(codePoint).EncodeToUtf16(destination[offset..]);
            }

            return true;
        }

        /// <summary>Encodes a string to Punycode (without the "xn--" prefix).</summary>
        /// <returns><see langword="false"/> on overflow or on an unpaired surrogate; part of the output may be written then.</returns>
        /// <remarks>
        /// RFC 3492 scans the whole input for each distinct code point, which is quadratic. The code points are sorted
        /// instead, and a Fenwick tree counts the code points already handled (the ones smaller than <c>n</c>) between two
        /// positions, which is what the scan adds to <c>delta</c>.
        /// </remarks>
        public static bool TryEncode(scoped ReadOnlySpan<char> input, ref CharBuffer output)
        {
            // The buffers hold the code points and the Fenwick tree, then the code points to encode.
            int[]? rentedInts = null;
            long[]? rentedLongs = null;
            var isShort = input.Length <= StackallocLabelLength;
            Span<int> ints = isShort ? stackalloc int[(2 * StackallocLabelLength) + 1] : (rentedInts = ArrayPool<int>.Shared.Rent((2 * input.Length) + 1));
            Span<long> longs = isShort ? stackalloc long[StackallocLabelLength] : (rentedLongs = ArrayPool<long>.Shared.Rent(input.Length));
            try
            {
                return TryEncode(input, ref output, ints, longs);
            }
            finally
            {
                if (rentedInts is not null)
                {
                    ArrayPool<int>.Shared.Return(rentedInts);
                }

                if (rentedLongs is not null)
                {
                    ArrayPool<long>.Shared.Return(rentedLongs);
                }
            }
        }

        private static bool TryEncode(scoped ReadOnlySpan<char> input, ref CharBuffer output, scoped Span<int> ints, scoped Span<long> longs)
        {
            var codePoints = ints[..input.Length];
            var count = 0;
            for (var index = 0; index < input.Length;)
            {
                // An unpaired surrogate has no code point: encoding U+FFFD instead would make distinct inputs collide
                if (Rune.DecodeFromUtf16(input[index..], out var rune, out var consumed) != OperationStatus.Done)
                    return false;

                codePoints[count++] = rune.Value;
                index += consumed;
            }

            codePoints = codePoints[..count];
            var handledPositions = new FenwickTree(ints.Slice(input.Length, count + 1), initialValue: 0);
            var outputStart = output.Length;
            var nonBasicCount = 0;
            for (var position = 0; position < count; position++)
            {
                var codePoint = codePoints[position];
                if (codePoint < 0x80)
                {
                    output.Append((char)codePoint);
                    handledPositions.Add(position, 1);
                }
                else
                {
                    nonBasicCount++;
                }
            }

            var basicLength = output.Length - outputStart;
            var handled = basicLength;
            if (basicLength > 0)
            {
                output.Append(Delimiter);
            }

            // The code points to encode, in the order of the encoding: by value, then by position
            var pending = longs[..nonBasicCount];
            nonBasicCount = 0;
            for (var position = 0; position < count; position++)
            {
                if (codePoints[position] >= 0x80)
                {
                    pending[nonBasicCount++] = ((long)codePoints[position] << 32) | (uint)position;
                }
            }

            pending.Sort();

            var n = InitialN;
            var delta = 0;
            var bias = InitialBias;
            var next = 0;
            while (next < pending.Length)
            {
                var m = (int)(pending[next] >> 32);
                if (m - n > (int.MaxValue - delta) / (handled + 1))
                    return false;

                delta += (m - n) * (handled + 1);
                n = m;

                var roundStart = next;
                var cursor = 0;
                for (; next < pending.Length && (int)(pending[next] >> 32) == n; next++)
                {
                    var position = (int)pending[next];
                    if (!TryAddDelta(ref delta, handledPositions.Sum(position) - handledPositions.Sum(cursor)))
                        return false;

                    var q = delta;
                    for (var k = Base; ; k += Base)
                    {
                        var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                        if (q < t)
                            break;

                        output.Append(EncodeDigit(t + ((q - t) % (Base - t))));
                        q = (q - t) / (Base - t);
                    }

                    output.Append(EncodeDigit(q));
                    bias = Adapt(delta, handled + 1, handled == basicLength);
                    delta = 0;
                    handled++;
                    cursor = position + 1;
                }

                if (!TryAddDelta(ref delta, handledPositions.Sum(count) - handledPositions.Sum(cursor)))
                    return false;

                for (var r = roundStart; r < next; r++)
                {
                    handledPositions.Add((int)pending[r], 1);
                }

                delta++;
                n++;
            }

            return true;
        }

        /// <summary>Adds the code points smaller than <c>n</c> that the scan of RFC 3492 counts one by one, and fails when <c>delta</c> reaches <see cref="int.MaxValue"/>.</summary>
        private static bool TryAddDelta(ref int delta, int count)
        {
            if (count > 0 && count >= int.MaxValue - delta)
                return false;

            delta += count;
            return true;
        }

        private static int Adapt(int delta, int numPoints, bool firstTime)
        {
            delta = firstTime ? delta / Damp : delta / 2;
            delta += delta / numPoints;
            var k = 0;
            while (delta > ((Base - TMin) * TMax) / 2)
            {
                delta /= Base - TMin;
                k += Base;
            }

            return k + (((Base - TMin + 1) * delta) / (delta + Skew));
        }

        private static int DecodeDigit(char c) => c switch
        {
            >= '0' and <= '9' => c - '0' + 26,
            >= 'A' and <= 'Z' => c - 'A',
            >= 'a' and <= 'z' => c - 'a',
            _ => Base,
        };

        private static char EncodeDigit(int digit) => (char)(digit < 26 ? 'a' + digit : '0' + digit - 26);

        /// <summary>A Fenwick tree (binary indexed tree) of counts, indexed from 0.</summary>
        private readonly ref struct FenwickTree
        {
            private readonly Span<int> _tree;

            /// <summary>Initializes a tree of <c>tree.Length - 1</c> counts, in a buffer whose content is overwritten.</summary>
            public FenwickTree(Span<int> tree, int initialValue)
            {
                _tree = tree;
                tree.Clear();
                if (initialValue != 0)
                {
                    var length = tree.Length - 1;
                    for (var i = 1; i <= length; i++)
                    {
                        _tree[i] += initialValue;
                        var parent = i + (i & -i);
                        if (parent <= length)
                        {
                            _tree[parent] += _tree[i];
                        }
                    }
                }
            }

            public void Add(int index, int value)
            {
                for (var i = index + 1; i < _tree.Length; i += i & -i)
                {
                    _tree[i] += value;
                }
            }

            /// <summary>Returns the sum of the counts of the indexes smaller than <paramref name="end"/>.</summary>
            public int Sum(int end)
            {
                var sum = 0;
                for (var i = end; i > 0; i -= i & -i)
                {
                    sum += _tree[i];
                }

                return sum;
            }

            /// <summary>Returns the smallest index whose prefix sum, itself included, is <paramref name="n"/>, for counts of 0 or 1.</summary>
            public int FindNth(int n)
            {
                var index = 0;
                for (var step = (int)BitOperations.RoundUpToPowerOf2((uint)_tree.Length) >> 1; step > 0; step >>= 1)
                {
                    var next = index + step;
                    if (next < _tree.Length && _tree[next] < n)
                    {
                        index = next;
                        n -= _tree[next];
                    }
                }

                return index;
            }
        }
    }

    /// <summary>A text built in a buffer on the stack, which grows into buffers rented from <see cref="ArrayPool{T}.Shared"/>.</summary>
    private ref struct CharBuffer
    {
        private char[]? _rented;
        private Span<char> _buffer;
        private int _length;

        public CharBuffer(Span<char> initialBuffer)
        {
            _buffer = initialBuffer;
        }

        public readonly int Length => _length;

        public readonly ReadOnlySpan<char> AsSpan() => _buffer[.._length];

        public void Append(char value)
        {
            if ((uint)_length >= (uint)_buffer.Length)
            {
                Grow(1);
            }

            _buffer[_length++] = value;
        }

        public void Append(scoped ReadOnlySpan<char> value)
        {
            value.CopyTo(AppendSpan(value.Length));
        }

        public void Append(Rune value)
        {
            var destination = AppendSpan(value.Utf16SequenceLength);
            value.EncodeToUtf16(destination);
        }

        /// <summary>Appends <paramref name="length"/> characters that the caller writes.</summary>
        public Span<char> AppendSpan(int length)
        {
            if (length > _buffer.Length - _length)
            {
                Grow(length);
            }

            var result = _buffer.Slice(_length, length);
            _length += length;
            return result;
        }

        public void Truncate(int length) => _length = length;

        public void Dispose()
        {
            if (_rented is not null)
            {
                ArrayPool<char>.Shared.Return(_rented);
                _rented = null;
            }
        }

        private void Grow(int additionalLength)
        {
            var array = ArrayPool<char>.Shared.Rent(Math.Max(_buffer.Length * 2, _length + additionalLength));
            _buffer[.._length].CopyTo(array);
            if (_rented is not null)
            {
                ArrayPool<char>.Shared.Return(_rented);
            }

            _rented = array;
            _buffer = array;
        }
    }
}
