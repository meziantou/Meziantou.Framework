using System.Text;

namespace Meziantou.Framework.WebPlatform.Unicode;

internal readonly record struct Uts46Options(
    bool CheckHyphens,
    bool CheckBidi,
    bool CheckJoiners,
    bool UseStd3AsciiRules,
    bool TransitionalProcessing,
    bool VerifyDnsLength,
    bool IgnoreInvalidPunycode);

/// <summary>
/// Unicode IDNA Compatibility Processing (UTS #46): https://www.unicode.org/reports/tr46/
/// </summary>
internal static class Uts46
{
    /// <summary>https://www.unicode.org/reports/tr46/#ToASCII. Returns null on failure.</summary>
    public static string? ToAscii(string domainName, Uts46Options options)
    {
        var labels = Process(domainName, options, out var hasError);
        if (hasError)
            return null;

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (!System.Text.Ascii.IsValid(label))
            {
                var encoded = Punycode.Encode(label);
                if (encoded is null)
                    return null;

                labels[i] = "xn--" + encoded;
            }
        }

        if (options.VerifyDnsLength)
        {
            var totalLength = labels.Sum(l => l.Length) + labels.Count - 1;
            var labelCount = labels.Count;
            if (labels[^1].Length == 0)
            {
                // The root label and its dot are excluded from the length check
                totalLength--;
                labelCount--;
            }

            if (totalLength is < 1 or > 253)
                return null;

            for (var i = 0; i < labelCount; i++)
            {
                if (labels[i].Length is < 1 or > 63)
                    return null;
            }
        }

        return string.Join('.', labels);
    }

    /// <summary>https://www.unicode.org/reports/tr46/#ToUnicode</summary>
    public static string ToUnicode(string domainName, Uts46Options options, out bool hasError)
    {
        var labels = Process(domainName, options, out hasError);
        return string.Join('.', labels);
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Processing</summary>
    private static List<string> Process(string domainName, Uts46Options options, out bool hasError)
    {
        hasError = false;

        // 1. Map
        var mapped = new StringBuilder(domainName.Length);
        foreach (var rune in domainName.EnumerateRunes())
        {
            var status = UnicodeProperties.GetIdnaStatus(rune.Value, out var mapping);
            switch (status)
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
                        mapped.Append(mapping);
                    }

                    break;

                case IdnaStatus.Deviation when options.TransitionalProcessing:
                    mapped.Append(mapping);
                    break;

                default:
                    mapped.Append(rune);
                    break;
            }
        }

        // 2. Normalize
        var normalized = Nfc.Normalize(mapped.ToString());

        // 3. Break
        var labels = normalized.Split('.').ToList();

        // 4. Convert/Validate
        var validatedAsNontransitional = new bool[labels.Count];
        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];
            if (label.StartsWith("xn--", StringComparison.Ordinal))
            {
                if (!System.Text.Ascii.IsValid(label))
                {
                    hasError = true;
                    continue;
                }

                var decoded = Punycode.Decode(label.AsSpan(4));
                if (decoded is null)
                {
                    if (!options.IgnoreInvalidPunycode)
                    {
                        hasError = true;
                        continue;
                    }
                }
                else
                {
                    labels[i] = decoded;
                    if (decoded.Length == 0 || System.Text.Ascii.IsValid(decoded))
                    {
                        hasError = true;
                    }
                }

                validatedAsNontransitional[i] = true;
            }
        }

        // The bidi rules apply to every label when the domain name is a Bidi domain name
        var isBidiDomainName = options.CheckBidi && IsBidiDomainName(labels);
        for (var i = 0; i < labels.Count; i++)
        {
            var transitional = options.TransitionalProcessing && !validatedAsNontransitional[i];
            if (!IsValidLabel(labels[i], options, transitional, isBidiDomainName))
            {
                hasError = true;
            }
        }

        return labels;
    }

    /// <summary>https://www.unicode.org/reports/tr46/#Validity_Criteria</summary>
    private static bool IsValidLabel(string label, Uts46Options options, bool transitional, bool isBidiDomainName)
    {
        if (label.Length == 0)
            return true;

        if (!Nfc.IsNormalized(label))
            return false;

        if (options.CheckHyphens)
        {
            if (label.Length >= 4 && label[2] == '-' && label[3] == '-')
                return false;

            if (label[0] == '-' || label[^1] == '-')
                return false;
        }
        else if (label.StartsWith("xn--", StringComparison.Ordinal))
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

        if (UnicodeProperties.IsMark(codePoints[0]))
            return false;

        foreach (var codePoint in codePoints)
        {
            var status = UnicodeProperties.GetIdnaStatus(codePoint, out _);
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
            while (before >= 0 && UnicodeProperties.GetJoiningType(codePoints[before]) == JoiningType.T)
            {
                before--;
            }

            if (before < 0 || UnicodeProperties.GetJoiningType(codePoints[before]) is not (JoiningType.L or JoiningType.D))
                return false;

            var after = i + 1;
            while (after < codePoints.Count && UnicodeProperties.GetJoiningType(codePoints[after]) == JoiningType.T)
            {
                after++;
            }

            if (after >= codePoints.Count || UnicodeProperties.GetJoiningType(codePoints[after]) is not (JoiningType.R or JoiningType.D))
                return false;
        }

        return true;
    }

    /// <summary>RFC 5893 section 1.4: a Bidi domain name contains at least one character with Bidi_Class R, AL, or AN.</summary>
    private static bool IsBidiDomainName(List<string> labels)
    {
        foreach (var label in labels)
        {
            foreach (var rune in label.EnumerateRunes())
            {
                if (UnicodeProperties.GetBidiClass(rune.Value) is BidiClass.R or BidiClass.AL or BidiClass.AN)
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
        if (first is not (BidiClass.L or BidiClass.R or BidiClass.AL))
            return false;

        // Last character that is not NSM
        var last = codePoints.Count - 1;
        while (last > 0 && UnicodeProperties.GetBidiClass(codePoints[last]) == BidiClass.NSM)
        {
            last--;
        }

        var lastClass = UnicodeProperties.GetBidiClass(codePoints[last]);
        if (first is BidiClass.R or BidiClass.AL)
        {
            var hasEN = false;
            var hasAN = false;
            foreach (var codePoint in codePoints)
            {
                var bidiClass = UnicodeProperties.GetBidiClass(codePoint);

                // 2. In an RTL label, only characters with the Bidi properties R, AL, AN, EN, ES, CS, ET, ON, BN, or NSM are allowed
                if (bidiClass is not (BidiClass.R or BidiClass.AL or BidiClass.AN or BidiClass.EN or BidiClass.ES or BidiClass.CS or BidiClass.ET or BidiClass.ON or BidiClass.BN or BidiClass.NSM))
                    return false;

                hasEN |= bidiClass == BidiClass.EN;
                hasAN |= bidiClass == BidiClass.AN;
            }

            // 3. The end of the label must be a character with Bidi property R, AL, EN, or AN, followed by zero or more NSM
            if (lastClass is not (BidiClass.R or BidiClass.AL or BidiClass.EN or BidiClass.AN))
                return false;

            // 4. If an EN is present, no AN may be present, and vice versa
            return !(hasEN && hasAN);
        }

        foreach (var codePoint in codePoints)
        {
            // 5. In an LTR label, only characters with the Bidi properties L, EN, ES, CS, ET, ON, BN, or NSM are allowed
            if (UnicodeProperties.GetBidiClass(codePoint) is not (BidiClass.L or BidiClass.EN or BidiClass.ES or BidiClass.CS or BidiClass.ET or BidiClass.ON or BidiClass.BN or BidiClass.NSM))
                return false;
        }

        // 6. The end of the label must be a character with Bidi property L or EN, followed by zero or more NSM
        return lastClass is BidiClass.L or BidiClass.EN;
    }
}
