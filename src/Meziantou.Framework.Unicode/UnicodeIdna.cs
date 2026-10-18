using System.Diagnostics.CodeAnalysis;

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
        var labels = Process(domainName, options, out var hasErrors);
        if (hasErrors)
            return false;

        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (!Ascii.IsValid(label))
            {
                if (!TryEncodePunycode(label, out var encoded))
                    return false;

                labels[i] = AcePrefix + encoded;
            }
        }

        if (options.VerifyDnsLength)
        {
            // A4_1 and A4_2: an empty label, including the root label after a trailing dot, is an error
            var totalLength = labels.Length - 1;
            foreach (var label in labels)
            {
                if (label.Length is < 1 or > 63)
                    return false;

                totalLength += label.Length;
            }

            if (totalLength > 253)
                return false;
        }

        result = string.Join('.', labels);
        return true;
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
        var labels = Process(domainName, options ?? UnicodeIdnaOptions.Default, out hasErrors);

        // X4_2: an empty label is an error, except the root label after a trailing dot
        for (var i = 0; i < labels.Length; i++)
        {
            if (labels[i].Length == 0 && (i < labels.Length - 1 || labels.Length == 1))
            {
                hasErrors = true;
            }
        }

        return string.Join('.', labels);
    }

    /// <summary>Encodes a label with Punycode (RFC 3492), without the <c>xn--</c> prefix.</summary>
    /// <param name="value">The label, such as <c>bücher</c>. The case of the letters is kept.</param>
    /// <param name="result">The encoded label, such as <c>bcher-kva</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the label is encoded; <see langword="false"/> when the encoding overflows.</returns>
    /// <seealso href="https://www.rfc-editor.org/rfc/rfc3492" />
    public static bool TryEncodePunycode(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? result)
    {
        result = Punycode.Encode(value);
        return result is not null;
    }

    /// <summary>Decodes a label encoded with Punycode (RFC 3492), without the <c>xn--</c> prefix.</summary>
    /// <param name="value">The encoded label, such as <c>bcher-kva</c>.</param>
    /// <param name="result">The decoded label, such as <c>bücher</c>, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the label is valid Punycode.</returns>
    /// <seealso href="https://www.rfc-editor.org/rfc/rfc3492" />
    public static bool TryDecodePunycode(ReadOnlySpan<char> value, [NotNullWhen(true)] out string? result)
    {
        result = Punycode.Decode(value);
        return result is not null;
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Processing</summary>
    private static string[] Process(string domainName, UnicodeIdnaOptions options, out bool hasErrors)
    {
        hasErrors = false;

        // 1. Map
        var mapped = new StringBuilder(domainName.Length);
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
                        AppendMapping(mapped, rune.Value);
                    }

                    break;

                case IdnaStatus.Deviation when options.TransitionalProcessing:
                    AppendMapping(mapped, rune.Value);
                    break;

                default:
                    mapped.Append(rune);
                    break;
            }
        }

        // 2. Normalize
        var normalized = UnicodeNormalization.Normalize(mapped.ToString(), NormalizationForm.FormC);

        // 3. Break
        var labels = normalized.Split('.');

        // 4. Convert/Validate
        var validatedAsNontransitional = new bool[labels.Length];
        for (var i = 0; i < labels.Length; i++)
        {
            var label = labels[i];
            if (label.StartsWith(AcePrefix, StringComparison.Ordinal))
            {
                if (!Ascii.IsValid(label))
                {
                    hasErrors = true;
                    continue;
                }

                var decoded = Punycode.Decode(label.AsSpan(AcePrefix.Length));
                if (decoded is null)
                {
                    if (!options.IgnoreInvalidPunycode)
                    {
                        hasErrors = true;
                        continue;
                    }
                }
                else
                {
                    labels[i] = decoded;
                    if (decoded.Length == 0 || Ascii.IsValid(decoded))
                    {
                        hasErrors = true;
                    }
                }

                validatedAsNontransitional[i] = true;
            }
        }

        // The bidi rules apply to every label when the domain name is a Bidi domain name
        var isBidiDomainName = options.CheckBidi && IsBidiDomainName(labels);
        for (var i = 0; i < labels.Length; i++)
        {
            var transitional = options.TransitionalProcessing && !validatedAsNontransitional[i];
            if (!IsValidLabel(labels[i], options, transitional, isBidiDomainName))
            {
                hasErrors = true;
            }
        }

        return labels;
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Validity_Criteria</summary>
    private static bool IsValidLabel(string label, UnicodeIdnaOptions options, bool transitional, bool isBidiDomainName)
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

        if (label.Contains('.', StringComparison.Ordinal))
            return false;

        var codePoints = new List<int>(label.Length);
        foreach (var rune in label.EnumerateRunes())
        {
            codePoints.Add(rune.Value);
        }

        if (UnicodeProperties.GetGeneralCategory(codePoints[0]) is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark)
            return false;

        foreach (var codePoint in codePoints)
        {
            var status = GetStatus(codePoint);
            if (status != IdnaStatus.Valid && (transitional || status != IdnaStatus.Deviation))
                return false;

            if (options.UseStd3AsciiRules && codePoint < 0x80 && !(char.IsAsciiLetterLower((char)codePoint) || char.IsAsciiDigit((char)codePoint) || codePoint == '-'))
                return false;
        }

        if (options.CheckJoiners && !SatisfiesContextJ(codePoints))
            return false;

        if (isBidiDomainName && !SatisfiesBidiRule(codePoints))
            return false;

        return true;
    }

    /// <summary>ContextJ rules: RFC 5892 Appendix A.1 and A.2.</summary>
    private static bool SatisfiesContextJ(List<int> codePoints)
    {
        const int Virama = 9;
        for (var i = 0; i < codePoints.Count; i++)
        {
            var codePoint = codePoints[i];
            if (codePoint is not (0x200C or 0x200D))
                continue;

            if (i > 0 && UnicodeProperties.GetCanonicalCombiningClass(codePoints[i - 1]) == Virama)
                continue;

            if (codePoint == 0x200D)
                return false;

            // (Joining_Type:{L,D})(Joining_Type:T)*U+200C(Joining_Type:T)*(Joining_Type:{R,D})
            var before = i - 1;
            while (before >= 0 && UnicodeProperties.GetJoiningType(codePoints[before]) == UnicodeJoiningType.Transparent)
            {
                before--;
            }

            if (before < 0 || UnicodeProperties.GetJoiningType(codePoints[before]) is not (UnicodeJoiningType.LeftJoining or UnicodeJoiningType.DualJoining))
                return false;

            var after = i + 1;
            while (after < codePoints.Count && UnicodeProperties.GetJoiningType(codePoints[after]) == UnicodeJoiningType.Transparent)
            {
                after++;
            }

            if (after >= codePoints.Count || UnicodeProperties.GetJoiningType(codePoints[after]) is not (UnicodeJoiningType.RightJoining or UnicodeJoiningType.DualJoining))
                return false;
        }

        return true;
    }

    /// <summary>RFC 5893 section 1.4: a Bidi domain name contains at least one character with Bidi_Class R, AL, or AN.</summary>
    private static bool IsBidiDomainName(string[] labels)
    {
        foreach (var label in labels)
        {
            foreach (var rune in label.EnumerateRunes())
            {
                if (UnicodeProperties.GetBidiClass(rune.Value) is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic or UnicodeBidirectionalCategory.ArabicNumber)
                    return true;
            }
        }

        return false;
    }

    /// <summary>The Bidi Rule: RFC 5893 section 2.</summary>
    private static bool SatisfiesBidiRule(List<int> codePoints)
    {
        var first = UnicodeProperties.GetBidiClass(codePoints[0]);

        // 1. The first character must be a character with Bidi property L, R, or AL
        if (first is not (UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic))
            return false;

        // Last character that is not NSM
        var last = codePoints.Count - 1;
        while (last > 0 && UnicodeProperties.GetBidiClass(codePoints[last]) == UnicodeBidirectionalCategory.NonspacingMark)
        {
            last--;
        }

        var lastClass = UnicodeProperties.GetBidiClass(codePoints[last]);
        if (first is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic)
        {
            var hasEuropeanNumber = false;
            var hasArabicNumber = false;
            foreach (var codePoint in codePoints)
            {
                var bidiClass = UnicodeProperties.GetBidiClass(codePoint);

                // 2. In an RTL label, only characters with the Bidi properties R, AL, AN, EN, ES, CS, ET, ON, BN, or NSM are allowed
                if (bidiClass is not (UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic or UnicodeBidirectionalCategory.ArabicNumber
                    or UnicodeBidirectionalCategory.EuropeanNumber or UnicodeBidirectionalCategory.EuropeanSeparator or UnicodeBidirectionalCategory.CommonSeparator
                    or UnicodeBidirectionalCategory.EuropeanTerminator or UnicodeBidirectionalCategory.OtherNeutral or UnicodeBidirectionalCategory.BoundaryNeutral
                    or UnicodeBidirectionalCategory.NonspacingMark))
                {
                    return false;
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

        foreach (var codePoint in codePoints)
        {
            // 5. In an LTR label, only characters with the Bidi properties L, EN, ES, CS, ET, ON, BN, or NSM are allowed
            if (UnicodeProperties.GetBidiClass(codePoint) is not (UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.EuropeanNumber
                or UnicodeBidirectionalCategory.EuropeanSeparator or UnicodeBidirectionalCategory.CommonSeparator or UnicodeBidirectionalCategory.EuropeanTerminator
                or UnicodeBidirectionalCategory.OtherNeutral or UnicodeBidirectionalCategory.BoundaryNeutral or UnicodeBidirectionalCategory.NonspacingMark))
            {
                return false;
            }
        }

        // 6. The end of the label must be a character with Bidi property L or EN, followed by zero or more NSM
        return lastClass is UnicodeBidirectionalCategory.LeftToRight or UnicodeBidirectionalCategory.EuropeanNumber;
    }

    private static IdnaStatus GetStatus(int codePoint) => (IdnaStatus)UnicodeIdnaTables.GetIdna(codePoint);

    private static void AppendMapping(StringBuilder destination, int codePoint)
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
        for (var i = 0; i < length; i++)
        {
            var index = (start + i) * 2;
            destination.Append((char)(pool[index] | (pool[index + 1] << 8)));
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

        /// <summary>Decodes a Punycode string (without the "xn--" prefix). Returns null on failure.</summary>
        public static string? Decode(ReadOnlySpan<char> input)
        {
            var output = new List<int>(input.Length);
            var basicLength = input.LastIndexOf(Delimiter);
            if (basicLength < 0)
            {
                basicLength = 0;
            }

            for (var j = 0; j < basicLength; j++)
            {
                if (input[j] >= 0x80)
                    return null;

                output.Add(input[j]);
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
                        return null;

                    var digit = DecodeDigit(input[index++]);
                    if (digit >= Base)
                        return null;

                    if (digit > (int.MaxValue - i) / w)
                        return null;

                    i += digit * w;
                    var t = k <= bias ? TMin : k >= bias + TMax ? TMax : k - bias;
                    if (digit < t)
                        break;

                    if (w > int.MaxValue / (Base - t))
                        return null;

                    w *= Base - t;
                }

                bias = Adapt(i - oldI, output.Count + 1, oldI == 0);
                if (i / (output.Count + 1) > int.MaxValue - n)
                    return null;

                n += i / (output.Count + 1);
                i %= output.Count + 1;
                if (n > 0x10FFFF || n is >= 0xD800 and <= 0xDFFF)
                    return null;

                output.Insert(i, n);
                i++;
            }

            var builder = new StringBuilder(output.Count);
            foreach (var codePoint in output)
            {
                builder.Append(new Rune(codePoint));
            }

            return builder.ToString();
        }

        /// <summary>Encodes a string to Punycode (without the "xn--" prefix). Returns null on overflow.</summary>
        public static string? Encode(ReadOnlySpan<char> input)
        {
            var codePoints = new List<int>(input.Length);
            foreach (var rune in input.EnumerateRunes())
            {
                codePoints.Add(rune.Value);
            }

            var output = new StringBuilder(input.Length + 8);
            foreach (var codePoint in codePoints)
            {
                if (codePoint < 0x80)
                {
                    output.Append((char)codePoint);
                }
            }

            var basicLength = output.Length;
            var handled = basicLength;
            if (basicLength > 0)
            {
                output.Append(Delimiter);
            }

            var n = InitialN;
            var delta = 0;
            var bias = InitialBias;
            while (handled < codePoints.Count)
            {
                var m = int.MaxValue;
                foreach (var codePoint in codePoints)
                {
                    if (codePoint >= n && codePoint < m)
                    {
                        m = codePoint;
                    }
                }

                if (m - n > (int.MaxValue - delta) / (handled + 1))
                    return null;

                delta += (m - n) * (handled + 1);
                n = m;
                foreach (var codePoint in codePoints)
                {
                    if (codePoint < n)
                    {
                        if (++delta == int.MaxValue)
                            return null;
                    }

                    if (codePoint == n)
                    {
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
                    }
                }

                delta++;
                n++;
            }

            return output.ToString();
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
    }
}
