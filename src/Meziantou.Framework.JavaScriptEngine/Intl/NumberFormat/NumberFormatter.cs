using System.Globalization;
using Meziantou.Framework.JavaScriptEngine.Intl.Data;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

/// <summary>A part of a formatted number (formatToParts): its type ("integer", "group", "currency"...) and its value.</summary>
internal readonly record struct NumberPart(string Type, string Value);

/// <summary>
/// The parts of a formatted number, grouped as the modifiers of ICU's range formatting: the outer affixes (the unit or
/// currency name pattern), the middle affixes (sign, currency symbol, percent sign and literals of the number
/// pattern), the inner affixes (compact symbol, exponent) and the digits.
/// </summary>
internal sealed class FormattedNumberParts
{
    public List<NumberPart> OuterPrefix { get; } = [];

    public List<NumberPart> MiddlePrefix { get; } = [];

    public List<NumberPart> InnerPrefix { get; } = [];

    public List<NumberPart> Number { get; } = [];

    public List<NumberPart> InnerSuffix { get; } = [];

    public List<NumberPart> MiddleSuffix { get; } = [];

    public List<NumberPart> OuterSuffix { get; } = [];

    /// <summary>Gets or sets whether the outer pattern has no number: only the outer parts are displayed.</summary>
    public bool OmitNumber { get; set; }

    /// <summary>Gets or sets the plural category of the displayed number (for the unit, currency name and compact patterns).</summary>
    public string PluralCategory { get; set; } = "other";

    /// <summary>Gets or sets the index in <see cref="MiddlePrefix"/> where the sign is, or would be.</summary>
    public int SignIndex { get; set; }

    public List<NumberPart> ToList()
    {
        var result = new List<NumberPart>(OuterPrefix.Count + MiddlePrefix.Count + InnerPrefix.Count + Number.Count + InnerSuffix.Count + MiddleSuffix.Count + OuterSuffix.Count);
        AddMerged(result, OuterPrefix);
        if (!OmitNumber)
        {
            AddMerged(result, MiddlePrefix);
            AddMerged(result, InnerPrefix);
            AddMerged(result, Number);
            AddMerged(result, InnerSuffix);
            AddMerged(result, MiddleSuffix);
        }

        AddMerged(result, OuterSuffix);
        return result;
    }

    /// <summary>Appends parts, merging a literal with a previous literal.</summary>
    public static void AddMerged(List<NumberPart> target, List<NumberPart> parts)
    {
        foreach (var part in parts)
        {
            if (part.Type is "literal" && target.Count > 0 && target[^1].Type is "literal")
            {
                target[^1] = new NumberPart("literal", target[^1].Value + part.Value);
            }
            else
            {
                target.Add(part);
            }
        }
    }

    public override string ToString() => Concat(ToList());

    public static string Concat(List<NumberPart> parts)
    {
        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.Value);
        }

        return builder.ToString();
    }

    public static string Concat(List<NumberPart> a, List<NumberPart> b) => Concat(a) + "\u0000" + Concat(b);
}

/// <summary>The formatting of Intl.NumberFormat (PartitionNumberPattern, PartitionNumberRangePattern, FormatApproximately).</summary>
internal static class NumberFormatter
{
    private enum SignKind
    {
        None,
        Minus,
        Plus,
        Approximately,
    }

    public static string Format(JsNumberFormatObject nf, IntlDecimal x) => PartitionNumberPattern(nf, x).ToString();

    /// <summary>PartitionNumberPattern(numberFormat, x)</summary>
    public static FormattedNumberParts PartitionNumberPattern(JsNumberFormatObject nf, IntlDecimal x, bool approximately = false)
    {
        var symbols = nf.Symbols;
        var locale = nf.DataLocale;
        var parts = new FormattedNumberParts();
        var rounded = x;
        string? compactPattern = null;
        var isCurrency = nf.Style is "currency";
        var hasCurrencySymbol = isCurrency && nf.CurrencyDisplay is not "name";
        var stylePattern = nf.StylePattern ??= GetStylePattern(nf);
        if (x.IsNaN)
        {
            parts.Number.Add(new NumberPart("nan", symbols.NaN));
        }
        else if (x.IsInfinity)
        {
            parts.Number.Add(new NumberPart("infinity", symbols.Infinity));
        }
        else
        {
            if (nf.Style is "percent")
            {
                x = x.ScaleByPowerOfTen(2);
            }

            var (exponent, magnitude) = Notation.ComputeExponent(nf.Digits, nf.Notation, locale, nf.NumberingSystem, nf.CompactDisplay, hasCurrencySymbol, x);
            var formatted = nf.Digits.FormatNumericToString(x.ScaleByPowerOfTen(-exponent));
            rounded = formatted.RoundedNumber;
            var n = formatted.FormattedString;

            // As ICU, the plural category of the patterns is the one of the displayed digits (without the exponent of
            // the compact notation: "1.2 million", not "1.2 millions")
            if (nf.Style is "unit" || nf.Notation is "compact" || (isCurrency && nf.CurrencyDisplay is "name"))
            {
                parts.PluralCategory = PluralRulesBuiltins.SelectCategory(nf.PluralRules, n, 0);
            }

            var groupingPattern = nf.Notation is "compact" || stylePattern.PrimaryGrouping == 0 ? nf.DecimalPattern ??= GetDecimalPattern(nf) : stylePattern;
            AppendDigits(nf, parts.Number, n, groupingPattern, isCurrency);

            if (nf.Notation is "scientific" or "engineering")
            {
                parts.InnerSuffix.Add(new NumberPart("exponentSeparator", symbols.Exponential));
                var exponentValue = exponent;
                if (exponentValue < 0)
                {
                    AddSymbol(parts.InnerSuffix, "exponentMinusSign", symbols.MinusSign);
                    exponentValue = -exponentValue;
                }

                parts.InnerSuffix.Add(new NumberPart("exponentInteger", MapDigits(nf, exponentValue.ToString(CultureInfo.InvariantCulture))));
            }
            else if (nf.Notation is "compact" && exponent != 0)
            {
                // An explicit pattern for 1 (e.g. "mille" in French), else the one of the plural category
                if (n is "1" or "0")
                {
                    compactPattern = Notation.GetCompactPattern(locale, nf.NumberingSystem, nf.CompactDisplay, hasCurrencySymbol, magnitude, n, fallbackToOther: false).Pattern;
                }

                compactPattern ??= Notation.GetCompactPattern(locale, nf.NumberingSystem, nf.CompactDisplay, hasCurrencySymbol, magnitude, parts.PluralCategory).Pattern;
                if (compactPattern is "0")
                {
                    compactPattern = null;
                }
                else if (compactPattern is not null && !NumberPattern.Parse(compactPattern).HasDigits)
                {
                    // The pattern replaces the number
                    parts.Number.Clear();
                }
            }
        }

        // The sign (GetNumberFormatPattern)
        var isNegative = !rounded.IsNaN && rounded.IsNegative;
        var isZero = rounded.IsNaN || rounded.IsZero;
        var sign = nf.SignDisplay switch
        {
            "never" => SignKind.None,
            "always" => isNegative ? SignKind.Minus : SignKind.Plus,
            "exceptZero" => isZero ? SignKind.None : isNegative ? SignKind.Minus : SignKind.Plus,
            "negative" => isNegative && !isZero ? SignKind.Minus : SignKind.None,
            _ => isNegative ? SignKind.Minus : SignKind.None,
        };

        // FormatApproximately: the approximately sign takes the place of the plus sign (ICU), or goes before the sign
        var approximatelyBeforeSign = approximately && sign is not SignKind.None;
        if (approximately && sign is SignKind.None)
        {
            sign = SignKind.Approximately;
        }

        // The affixes of the pattern: the compact pattern replaces the one of the style
        var pattern = stylePattern;
        if (hasCurrencySymbol && rounded.IsFinite && compactPattern is null && nf.CurrencyAlphaNextToNumberPattern is { } alphaNextToNumber)
        {
            // The currency spacing applies next to a digit only
            pattern = alphaNextToNumber;
        }

        var affixesFromCompact = false;
        if (compactPattern is not null)
        {
            var compact = NumberPattern.Parse(compactPattern);
            if (nf.Style is "percent" || nf.IsPercentUnit)
            {
                // The compact symbol goes before the affixes of the percent pattern
                AddCompactLiterals(parts.InnerPrefix, compact.PositivePrefix);
                AddCompactLiterals(parts.InnerSuffix, compact.PositiveSuffix);
            }
            else
            {
                pattern = compact;
                affixesFromCompact = true;
            }
        }

        GetAffixes(pattern, sign, out var prefix, out var suffix);
        var currencyText = hasCurrencySymbol ? nf.CurrencyText ??= GetCurrencyText(nf, "other") : "";

        // Prefix
        var innerStart = affixesFromCompact ? FindFirstLiteral(prefix) : prefix.Length;
        for (var i = 0; i < prefix.Length; i++)
        {
            var token = prefix[i];
            if (token.Kind is AffixKind.MinusSign or AffixKind.PlusSign)
            {
                parts.SignIndex = parts.MiddlePrefix.Count;
                if (approximatelyBeforeSign)
                {
                    AddSymbol(parts.MiddlePrefix, "approximatelySign", symbols.ApproximatelySign);
                }
                else if (sign is SignKind.Approximately)
                {
                    AddSymbol(parts.MiddlePrefix, "approximatelySign", symbols.ApproximatelySign);
                    continue;
                }
            }

            if (i >= innerStart && token.Kind is AffixKind.Literal)
            {
                AddCompactLiteral(parts.InnerPrefix, token.Text);
            }
            else
            {
                AddToken(i >= innerStart ? parts.InnerPrefix : parts.MiddlePrefix, token, nf, currencyText);
            }
        }

        // Suffix
        var innerEnd = affixesFromCompact ? FindLastLiteral(suffix) : -1;
        for (var i = 0; i < suffix.Length; i++)
        {
            var token = suffix[i];
            if (sign is SignKind.Approximately && token.Kind is AffixKind.PlusSign)
            {
                AddSymbol(i <= innerEnd ? parts.InnerSuffix : parts.MiddleSuffix, "approximatelySign", symbols.ApproximatelySign);
                continue;
            }

            if (approximatelyBeforeSign && token.Kind is AffixKind.MinusSign or AffixKind.PlusSign)
            {
                AddSymbol(i <= innerEnd ? parts.InnerSuffix : parts.MiddleSuffix, "approximatelySign", symbols.ApproximatelySign);
            }

            if (i <= innerEnd && token.Kind is AffixKind.Literal)
            {
                AddCompactLiteral(parts.InnerSuffix, token.Text);
            }
            else if (i <= innerEnd)
            {
                AddToken(parts.InnerSuffix, token, nf, currencyText);
            }
            else
            {
                AddToken(parts.MiddleSuffix, token, nf, currencyText);
            }
        }

        if (hasCurrencySymbol && rounded.IsFinite && !ReferenceEquals(pattern, nf.CurrencyAlphaNextToNumberPattern))
        {
            ApplyCurrencySpacing(nf, parts);
        }

        AddOuterParts(nf, parts.PluralCategory, parts.OuterPrefix, parts.OuterSuffix);
        if (parts.OuterPrefix.Count > 0 && parts.OuterPrefix[^1].Type is "omitNumber")
        {
            // A unit pattern without the number (e.g. "{0} \u0644\u062A\u0631" for "other" but "\u0644\u062A\u0631" for "one" in Arabic)
            parts.OuterPrefix.RemoveAt(parts.OuterPrefix.Count - 1);
            parts.OmitNumber = true;
        }

        return parts;
    }

    /// <summary>Adds the parts of the outer pattern: the unit pattern, or the currency name pattern.</summary>
    internal static void AddOuterParts(JsNumberFormatObject nf, string pluralCategory, List<NumberPart> prefix, List<NumberPart> suffix)
    {
        if (nf.Style is "unit" && !nf.IsPercentUnit)
        {
            ApplyOuterPattern(prefix, suffix, GetUnitPattern(nf, pluralCategory), "unit", "");
        }
        else if (nf.Style is "currency" && nf.CurrencyDisplay is "name")
        {
            var locale = nf.DataLocale;
            var unitPattern = locale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", "unitPattern-count-" + pluralCategory)
                ?? locale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", "unitPattern-count-other")
                ?? "{0} {1}";
            ApplyOuterPattern(prefix, suffix, unitPattern, "currency", GetCurrencyText(nf, pluralCategory));
        }
    }

    // UTS 35 currencySpacing: a currency whose character next to the digits is not a symbol is separated from them
    private static void ApplyCurrencySpacing(JsNumberFormatObject nf, FormattedNumberParts parts)
    {
        if (parts.Number.Count == 0)
            return;

        var before = parts.InnerPrefix.Count > 0 ? parts.InnerPrefix : parts.MiddlePrefix;
        if (before.Count > 0 && before[^1].Type is "currency" && !IsSymbolOrSpace(before[^1].Value[^1]))
        {
            before.Add(new NumberPart("literal", nf.DataLocale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", "currencySpacing/afterCurrency/insertBetween") ?? "\u00A0"));
        }

        var after = parts.InnerSuffix.Count > 0 ? parts.InnerSuffix : parts.MiddleSuffix;
        if (after.Count > 0 && after[0].Type is "currency" && !IsSymbolOrSpace(after[0].Value[0]))
        {
            after.Insert(0, new NumberPart("literal", nf.DataLocale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", "currencySpacing/beforeCurrency/insertBetween") ?? "\u00A0"));
        }

        static bool IsSymbolOrSpace(char c) => char.GetUnicodeCategory(c) is UnicodeCategory.MathSymbol or UnicodeCategory.CurrencySymbol or UnicodeCategory.ModifierSymbol or UnicodeCategory.OtherSymbol
            or UnicodeCategory.SpaceSeparator or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator;
    }

    private static int FindFirstLiteral(AffixToken[] tokens)
    {
        for (var i = 0; i < tokens.Length; i++)
        {
            if (tokens[i].Kind is AffixKind.Literal && !IsSpaces(tokens[i].Text))
                return i;
        }

        return tokens.Length;
    }

    private static int FindLastLiteral(AffixToken[] tokens)
    {
        for (var i = tokens.Length - 1; i >= 0; i--)
        {
            if (tokens[i].Kind is AffixKind.Literal && !IsSpaces(tokens[i].Text))
                return i;
        }

        return -1;
    }

    private static void AddCompactLiterals(List<NumberPart> target, AffixToken[] tokens)
    {
        foreach (var token in tokens)
        {
            if (token.Kind is AffixKind.Literal)
            {
                AddCompactLiteral(target, token.Text);
            }
        }
    }

    // The whitespace around a compact symbol is a literal
    private static void AddCompactLiteral(List<NumberPart> target, string text) => AddSplitText(target, text, "compact");

    /// <summary>Adds a text whose leading and trailing whitespace are literals and whose middle has the given type.</summary>
    private static void AddSplitText(List<NumberPart> target, string text, string type)
    {
        var start = 0;
        while (start < text.Length && IsSpace(text[start]))
        {
            start++;
        }

        var end = text.Length;
        while (end > start && IsSpace(text[end - 1]))
        {
            end--;
        }

        if (start > 0)
        {
            target.Add(new NumberPart("literal", text[..start]));
        }

        if (end > start)
        {
            target.Add(new NumberPart(type, text[start..end]));
        }

        if (end < text.Length)
        {
            target.Add(new NumberPart("literal", text[end..]));
        }
    }

    private static bool IsSpaces(string text)
    {
        foreach (var c in text)
        {
            if (!IsSpace(c))
                return false;
        }

        return true;
    }

    private static bool IsSpace(char c) => c is ' ' or '\u00A0' or '\u202F' or '\u2009' or '\u200F' or '\u200E' or '\u061C';

    private static void AddToken(List<NumberPart> target, AffixToken token, JsNumberFormatObject nf, string currencyText)
    {
        var symbols = nf.Symbols;
        switch (token.Kind)
        {
            case AffixKind.MinusSign:
                AddSymbol(target, "minusSign", symbols.MinusSign);
                break;
            case AffixKind.PlusSign:
                AddSymbol(target, "plusSign", symbols.PlusSign);
                break;
            case AffixKind.Percent:
                AddSymbol(target, nf.IsPercentUnit ? "unit" : "percentSign", symbols.PercentSign);
                break;
            case AffixKind.PerMille:
                AddSymbol(target, "percentSign", symbols.PerMille);
                break;
            case AffixKind.Currency:
                if (currencyText.Length > 0)
                {
                    target.Add(new NumberPart("currency", currencyText));
                }

                break;
            default:
                target.Add(new NumberPart("literal", token.Text));
                break;
        }
    }

    // The bidi marks around a symbol (e.g. U+061C before the minus sign of Arabic) are literals
    private static void AddSymbol(List<NumberPart> target, string type, string symbol)
    {
        var start = 0;
        while (start < symbol.Length && IsBidiMark(symbol[start]))
        {
            start++;
        }

        var end = symbol.Length;
        while (end > start && IsBidiMark(symbol[end - 1]))
        {
            end--;
        }

        if (start > 0)
        {
            target.Add(new NumberPart("literal", symbol[..start]));
        }

        target.Add(new NumberPart(type, symbol[start..end]));
        if (end < symbol.Length)
        {
            target.Add(new NumberPart("literal", symbol[end..]));
        }

        static bool IsBidiMark(char c) => c is (char)0x200E or (char)0x200F or (char)0x061C;
    }

    // The affixes for a sign: the explicit negative subpattern, or the positive one with the sign before it
    private static void GetAffixes(NumberPattern pattern, SignKind sign, out AffixToken[] prefix, out AffixToken[] suffix)
    {
        switch (sign)
        {
            case SignKind.Minus when pattern.NegativePrefix is not null:
                prefix = pattern.NegativePrefix;
                suffix = pattern.NegativeSuffix!;
                return;

            case SignKind.Plus or SignKind.Approximately when pattern.NegativePrefix is not null && (ContainsKind(pattern.NegativePrefix, AffixKind.MinusSign) || ContainsKind(pattern.NegativeSuffix!, AffixKind.MinusSign)):
                prefix = ReplaceMinus(pattern.NegativePrefix);
                suffix = ReplaceMinus(pattern.NegativeSuffix!);
                return;

            case SignKind.Minus:
                prefix = [new AffixToken(AffixKind.MinusSign, "-"), .. RemoveSigns(pattern.PositivePrefix)];
                suffix = RemoveSigns(pattern.PositiveSuffix);
                return;

            case SignKind.Plus or SignKind.Approximately:
                prefix = [new AffixToken(AffixKind.PlusSign, "+"), .. RemoveSigns(pattern.PositivePrefix)];
                suffix = RemoveSigns(pattern.PositiveSuffix);
                return;

            default:
                prefix = RemoveSigns(pattern.PositivePrefix);
                suffix = RemoveSigns(pattern.PositiveSuffix);
                return;
        }

        static AffixToken[] ReplaceMinus(AffixToken[] tokens)
        {
            var result = new AffixToken[tokens.Length];
            for (var i = 0; i < tokens.Length; i++)
            {
                result[i] = tokens[i].Kind is AffixKind.MinusSign ? new AffixToken(AffixKind.PlusSign, "+") : tokens[i];
            }

            return result;
        }

        static AffixToken[] RemoveSigns(AffixToken[] tokens)
        {
            if (!ContainsKind(tokens, AffixKind.MinusSign) && !ContainsKind(tokens, AffixKind.PlusSign))
                return tokens;

            var result = new List<AffixToken>(tokens.Length);
            foreach (var token in tokens)
            {
                if (token.Kind is not (AffixKind.MinusSign or AffixKind.PlusSign))
                {
                    result.Add(token);
                }
            }

            return [.. result];
        }
    }

    private static bool ContainsKind(AffixToken[] tokens, AffixKind kind)
    {
        foreach (var token in tokens)
        {
            if (token.Kind == kind)
                return true;
        }

        return false;
    }

    private static NumberPattern GetDecimalPattern(JsNumberFormatObject nf)
    {
        return NumberPattern.Parse(nf.DataLocale.GetNumberPattern(nf.NumberingSystem, "decimalFormats", "standard") ?? "#,##0.###");
    }

    /// <summary>Gets the number pattern of the style (decimal, percent, currency or accounting).</summary>
    private static NumberPattern GetStylePattern(JsNumberFormatObject nf)
    {
        var locale = nf.DataLocale;
        string? pattern = null;
        switch (nf.Style)
        {
            case "percent":
            case "unit" when nf.IsPercentUnit:
                pattern = locale.GetNumberPattern(nf.NumberingSystem, "percentFormats", "standard");
                break;
            case "currency" when nf.CurrencyDisplay is not "name":
                pattern = locale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", nf.CurrencySign is "accounting" ? "accounting" : "standard");
                if (pattern is not null)
                {
                    // A currency symbol that ends (or starts) with a letter next to the number uses the alphaNextToNumber pattern
                    var parsed = NumberPattern.Parse(pattern);
                    nf.CurrencyAlphaNextToNumberPattern = GetAlphaNextToNumberPattern(nf, parsed.PositivePrefix, parsed.PositiveSuffix, nf.CurrencyText ??= GetCurrencyText(nf, "other"));
                    return parsed;
                }

                break;
        }

        return pattern is null ? GetDecimalPattern(nf) : NumberPattern.Parse(pattern);
    }

    private static NumberPattern? GetAlphaNextToNumberPattern(JsNumberFormatObject nf, AffixToken[] prefix, AffixToken[] suffix, string currencyText)
    {
        if (currencyText.Length == 0)
            return null;

        var adjacent = false;
        if (prefix.Length > 0 && prefix[^1].Kind is AffixKind.Currency && char.IsLetter(currencyText[^1]))
        {
            adjacent = true;
        }

        if (suffix.Length > 0 && suffix[0].Kind is AffixKind.Currency && char.IsLetter(currencyText[0]))
        {
            adjacent = true;
        }

        if (!adjacent)
            return null;

        var key = (nf.CurrencySign is "accounting" ? "accounting" : "standard") + "-alphaNextToNumber";
        var pattern = nf.DataLocale.GetNumberPattern(nf.NumberingSystem, "currencyFormats", key);
        return pattern is null ? null : NumberPattern.Parse(pattern);
    }

    /// <summary>Gets the text of the currency for [[CurrencyDisplay]].</summary>
    private static string GetCurrencyText(JsNumberFormatObject nf, string pluralCategory)
    {
        var currency = nf.Currency!;
        var locale = nf.DataLocale;
        return nf.CurrencyDisplay switch
        {
            "code" => currency,
            "narrowSymbol" => locale.GetString(CldrCategory.Currencies, currency + "/symbol-alt-narrow") ?? locale.GetString(CldrCategory.Currencies, currency + "/symbol") ?? currency,
            "name" => locale.GetString(CldrCategory.Currencies, currency + "/displayName-count-" + pluralCategory)
                ?? locale.GetString(CldrCategory.Currencies, currency + "/displayName-count-other")
                ?? locale.GetString(CldrCategory.Currencies, currency + "/displayName")
                ?? currency,
            _ => locale.GetString(CldrCategory.Currencies, currency + "/symbol") ?? currency,
        };
    }

    /// <summary>Gets the pattern of a unit ("{0} km"), or of a compound unit ("{0} km/h").</summary>
    private static string GetUnitPattern(JsNumberFormatObject nf, string pluralCategory)
    {
        var unit = nf.Unit!;
        var display = nf.UnitDisplay;
        if (GetSimpleUnitPattern(nf.DataLocale, display, unit, pluralCategory) is { } simple)
            return simple;

        var per = unit.IndexOf("-per-", StringComparison.Ordinal);
        if (per > 0)
        {
            var numerator = unit[..per];
            var denominator = unit[(per + 5)..];
            var numeratorPattern = GetSimpleUnitPattern(nf.DataLocale, display, numerator, pluralCategory) ?? "{0} " + numerator;
            if (GetUnitString(nf.DataLocale, display, denominator + "/perUnitPattern") is { } perUnitPattern)
                return perUnitPattern.Replace("{0}", numeratorPattern, StringComparison.Ordinal);

            var compound = GetUnitString(nf.DataLocale, display, "per/compoundUnitPattern") ?? "{0}/{1}";
            var denominatorName = (GetSimpleUnitPattern(nf.DataLocale, display, denominator, "one") ?? denominator).Replace("{0}", "", StringComparison.Ordinal).Trim();
            return compound.Replace("{1}", denominatorName, StringComparison.Ordinal).Replace("{0}", numeratorPattern, StringComparison.Ordinal);
        }

        return "{0} " + unit;
    }

    private static string? GetSimpleUnitPattern(CldrLocale locale, string display, string unit, string pluralCategory)
    {
        return GetUnitString(locale, display, unit + "/unitPattern-count-" + pluralCategory)
            ?? GetUnitString(locale, display, unit + "/unitPattern-count-other");
    }

    private static string? GetUnitString(CldrLocale locale, string display, string path)
    {
        return locale.GetString(CldrCategory.Units, display + "/" + path)
            ?? (display is "narrow" ? locale.GetString(CldrCategory.Units, "short/" + path) : null);
    }

    // Wraps the parts in a pattern with "{0}" (the number) and optionally "{1}" (the unit or currency name)
    private static void ApplyOuterPattern(List<NumberPart> prefix, List<NumberPart> suffix, string pattern, string type, string name)
    {
        var index = pattern.IndexOf("{0}", StringComparison.Ordinal);
        if (index < 0)
        {
            AddOuterText(prefix, pattern, type, name);
            prefix.Add(new NumberPart("omitNumber", ""));
            return;
        }

        AddOuterText(prefix, pattern[..index], type, name);
        AddOuterText(suffix, pattern[(index + 3)..], type, name);
    }

    private static void AddOuterText(List<NumberPart> target, string text, string type, string name)
    {
        if (text.Length == 0)
            return;

        AddSplitText(target, text.Replace("{1}", name, StringComparison.Ordinal), type);
    }

    /// <summary>Appends the integer, group, decimal and fraction parts of the digits of a formatted number ("1234.5").</summary>
    private static void AppendDigits(JsNumberFormatObject nf, List<NumberPart> target, string n, NumberPattern pattern, bool isCurrency)
    {
        var symbols = nf.Symbols;
        var decimalSymbol = isCurrency ? nf.CurrencyDecimal : symbols.Decimal;
        var groupSymbol = isCurrency ? nf.CurrencyGroup : symbols.Group;
        var dot = n.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? n : n[..dot];
        var fraction = dot < 0 ? null : n[(dot + 1)..];

        var primary = pattern.PrimaryGrouping;
        var secondary = pattern.SecondaryGrouping;
        var minimumGrouping = nf.UseGrouping switch
        {
            "always" => 1,
            "min2" => 2,
            "auto" => nf.MinimumGroupingDigits ??= GetMinimumGroupingDigits(nf.DataLocale),
            _ => int.MaxValue,
        };

        if (primary <= 0 || nf.UseGrouping is null || integer.Length < primary + minimumGrouping)
        {
            target.Add(new NumberPart("integer", MapDigits(nf, integer)));
        }
        else
        {
            var groups = new List<string>();
            var end = integer.Length;
            groups.Add(integer[(end - primary)..end]);
            end -= primary;
            var size = secondary > 0 ? secondary : primary;
            while (end > 0)
            {
                var start = Math.Max(0, end - size);
                groups.Add(integer[start..end]);
                end = start;
            }

            for (var i = groups.Count - 1; i >= 0; i--)
            {
                target.Add(new NumberPart("integer", MapDigits(nf, groups[i])));
                if (i > 0)
                {
                    target.Add(new NumberPart("group", groupSymbol));
                }
            }
        }

        if (fraction is not null)
        {
            target.Add(new NumberPart("decimal", decimalSymbol));
            target.Add(new NumberPart("fraction", MapDigits(nf, fraction)));
        }
    }

    private static int GetMinimumGroupingDigits(CldrLocale locale)
    {
        return locale.GetString(CldrCategory.Numbers, "minimumGroupingDigits") is { } value && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var result) ? result : 1;
    }

    /// <summary>Replaces the ASCII digits by the digits of the numbering system.</summary>
    private static string MapDigits(JsNumberFormatObject nf, string value)
    {
        var digits = nf.NumberingSystemDigits;
        if (string.IsNullOrEmpty(digits))
            return value;

        var codePoints = GetDigitStrings(digits);
        var builder = new StringBuilder(value.Length * 2);
        foreach (var c in value)
        {
            if (char.IsAsciiDigit(c))
            {
                builder.Append(codePoints[c - '0']);
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string[] GetDigitStrings(string digits)
    {
        var result = new string[10];
        var index = 0;
        for (var i = 0; i < digits.Length && index < 10; i++)
        {
            if (char.IsHighSurrogate(digits[i]) && i + 1 < digits.Length)
            {
                result[index++] = digits.Substring(i, 2);
                i++;
            }
            else
            {
                result[index++] = digits[i].ToString();
            }
        }

        return result;
    }

    /// <summary>FormatApproximately(numberFormat, x): the parts of x with the approximately sign where the sign is, or would be.</summary>
    public static List<NumberPart> FormatApproximately(JsNumberFormatObject nf, IntlDecimal x) => PartitionNumberPattern(nf, x, approximately: true).ToList();

    /// <summary>PartitionNumberRangePattern(numberFormat, x, y): the parts with their source ("startRange", "endRange" or "shared").</summary>
    public static List<(NumberPart Part, string Source)> PartitionNumberRangePattern(JsNumberFormatObject nf, IntlDecimal x, IntlDecimal y)
    {
        if (x.IsNaN || y.IsNaN)
            ThrowHelper.ThrowRangeError("Invalid number range");

        var xResult = PartitionNumberPattern(nf, x);
        var yResult = PartitionNumberPattern(nf, y);
        var result = new List<(NumberPart, string)>();
        if (xResult.ToString() == yResult.ToString())
        {
            foreach (var part in FormatApproximately(nf, x))
            {
                result.Add((part, "shared"));
            }

            return result;
        }

        // CollapseNumberRange, as ICU does (UNUM_RANGE_COLLAPSE_AUTO): the outer affixes are shared when they are equal,
        // the middle ones too when they are also equal and longer than one code point
        // The outer affixes (unit and currency names) of a formatter are the same unit: the shared one uses the plural
        // category of the range
        var collapseOuter = true;
        if (xResult.OuterPrefix.Count + xResult.OuterSuffix.Count > 0)
        {
            var category = PluralRulesBuiltins.SelectRange(nf.PluralRules, xResult.PluralCategory, yResult.PluralCategory);
            xResult.OuterPrefix.Clear();
            xResult.OuterSuffix.Clear();
            AddOuterParts(nf, category, xResult.OuterPrefix, xResult.OuterSuffix);
            xResult.OuterPrefix.RemoveAll(static part => part.Type is "omitNumber");
            xResult.OmitNumber = false;
            yResult.OmitNumber = false;
        }

        var middleX = FormattedNumberParts.Concat(xResult.MiddlePrefix, xResult.MiddleSuffix);
        var collapseMiddle = collapseOuter && middleX == FormattedNumberParts.Concat(yResult.MiddlePrefix, yResult.MiddleSuffix) && CodePointCount(middleX) > 2;
        var repeatOuter = !collapseOuter && xResult.OuterPrefix.Count + xResult.OuterSuffix.Count > 0;
        var repeatMiddle = !collapseMiddle && xResult.MiddlePrefix.Count + xResult.MiddleSuffix.Count > 0;
        var repeatInner = xResult.InnerPrefix.Count + xResult.InnerSuffix.Count > 0;

        if (collapseOuter)
        {
            AddRange(result, xResult.OuterPrefix, "shared");
        }

        if (collapseMiddle)
        {
            AddRange(result, xResult.MiddlePrefix, "shared");
        }

        AddSide(result, xResult, collapseOuter, collapseMiddle, "startRange");

        var rangePattern = nf.DataLocale.GetNumberPattern(nf.NumberingSystem, "miscPatterns", "range") ?? "{0}\u2013{1}";
        var start = rangePattern.IndexOf("{0}", StringComparison.Ordinal);
        var end = rangePattern.IndexOf("{1}", StringComparison.Ordinal);
        var separator = start >= 0 && end > start ? rangePattern[(start + 3)..end] : "\u2013";
        if ((repeatOuter || repeatMiddle || repeatInner) && separator.Length > 0)
        {
            if (!IsSpace(separator[0]))
            {
                separator = " " + separator;
            }

            if (!IsSpace(separator[^1]))
            {
                separator += " ";
            }
        }

        result.Add((new NumberPart("literal", separator), "shared"));
        AddSide(result, yResult, collapseOuter, collapseMiddle, "endRange");

        if (collapseMiddle)
        {
            AddRange(result, xResult.MiddleSuffix, "shared");
        }

        if (collapseOuter)
        {
            AddRange(result, xResult.OuterSuffix, "shared");
        }

        return result;

        static void AddSide(List<(NumberPart, string)> result, FormattedNumberParts parts, bool collapseOuter, bool collapseMiddle, string source)
        {
            if (!collapseOuter)
            {
                AddRange(result, parts.OuterPrefix, source);
            }

            if (!collapseMiddle)
            {
                AddRange(result, parts.MiddlePrefix, source);
            }

            AddRange(result, parts.InnerPrefix, source);
            AddRange(result, parts.Number, source);
            AddRange(result, parts.InnerSuffix, source);
            if (!collapseMiddle)
            {
                AddRange(result, parts.MiddleSuffix, source);
            }

            if (!collapseOuter)
            {
                AddRange(result, parts.OuterSuffix, source);
            }
        }

        static void AddRange(List<(NumberPart, string)> result, List<NumberPart> parts, string source)
        {
            foreach (var part in parts)
            {
                if (part.Type is "literal" && result.Count > 0 && result[^1].Item1.Type is "literal" && result[^1].Item2 == source)
                {
                    result[^1] = (new NumberPart("literal", result[^1].Item1.Value + part.Value), source);
                }
                else
                {
                    result.Add((part, source));
                }
            }
        }

        // The separator between the two concatenated affixes does not count
        static int CodePointCount(string value)
        {
            var count = 0;
            for (var i = 0; i < value.Length; i++)
            {
                if (!char.IsLowSurrogate(value[i]))
                {
                    count++;
                }
            }

            return count;
        }
    }
}
