using System.Collections.Concurrent;

namespace Meziantou.Framework.JavaScriptEngine.Intl;

internal enum AffixKind : byte
{
    Literal,
    Currency,
    Percent,
    PerMille,
    MinusSign,
    PlusSign,
}

/// <summary>A token of the prefix or suffix of a number pattern.</summary>
internal readonly record struct AffixToken(AffixKind Kind, string Text);

/// <summary>
/// A number pattern of CLDR (UTS 35 part 3, "Number Format Patterns"), e.g. "\u00A4#,##0.00;(\u00A4#,##0.00)": the affixes of the
/// positive and the negative subpatterns and the grouping sizes. The digits of the pattern are not used: ECMA-402 gives
/// the number of digits.
/// </summary>
internal sealed class NumberPattern
{
    private static readonly ConcurrentDictionary<string, NumberPattern> Cache = new(StringComparer.Ordinal);

    private NumberPattern(AffixToken[] positivePrefix, AffixToken[] positiveSuffix, AffixToken[]? negativePrefix, AffixToken[]? negativeSuffix, int primaryGrouping, int secondaryGrouping)
    {
        PositivePrefix = positivePrefix;
        PositiveSuffix = positiveSuffix;
        NegativePrefix = negativePrefix;
        NegativeSuffix = negativeSuffix;
        PrimaryGrouping = primaryGrouping;
        SecondaryGrouping = secondaryGrouping;
    }

    public AffixToken[] PositivePrefix { get; }

    public AffixToken[] PositiveSuffix { get; }

    /// <summary>Gets the prefix of the explicit negative subpattern, or null when the pattern has none.</summary>
    public AffixToken[]? NegativePrefix { get; }

    public AffixToken[]? NegativeSuffix { get; }

    /// <summary>Gets the size of the group of the lowest digits, or 0 when the pattern has no grouping.</summary>
    public int PrimaryGrouping { get; }

    /// <summary>Gets the size of the other groups.</summary>
    public int SecondaryGrouping { get; }

    public bool HasCurrency => Contains(PositivePrefix, AffixKind.Currency) || Contains(PositiveSuffix, AffixKind.Currency);

    private static bool Contains(AffixToken[] tokens, AffixKind kind)
    {
        foreach (var token in tokens)
        {
            if (token.Kind == kind)
                return true;
        }

        return false;
    }

    public static NumberPattern Parse(string pattern) => Cache.GetOrAdd(pattern, static pattern => ParseCore(pattern));

    private static NumberPattern ParseCore(string pattern)
    {
        var separator = FindNegativeSeparator(pattern);
        var positive = separator < 0 ? pattern : pattern[..separator];
        ParseSubpattern(positive, out var prefix, out var suffix, out var primary, out var secondary);
        AffixToken[]? negativePrefix = null;
        AffixToken[]? negativeSuffix = null;
        if (separator >= 0)
        {
            ParseSubpattern(pattern[(separator + 1)..], out negativePrefix, out negativeSuffix, out _, out _);
        }

        return new NumberPattern(prefix, suffix, negativePrefix, negativeSuffix, primary, secondary);
    }

    private static int FindNegativeSeparator(string pattern)
    {
        var quoted = false;
        for (var i = 0; i < pattern.Length; i++)
        {
            if (pattern[i] == '\'')
            {
                quoted = !quoted;
            }
            else if (!quoted && pattern[i] == ';')
            {
                return i;
            }
        }

        return -1;
    }

    private static bool IsNumberChar(char c) => c is '#' or ',' or '.' or '@' or (>= '0' and <= '9');

    private static void ParseSubpattern(string pattern, out AffixToken[] prefix, out AffixToken[] suffix, out int primaryGrouping, out int secondaryGrouping)
    {
        var index = 0;
        prefix = ParseAffix(pattern, ref index, untilNumber: true);
        var numberStart = index;
        while (index < pattern.Length && IsNumberChar(pattern[index]))
        {
            index++;
        }

        // The scientific patterns ("#E0") are not used: the exponent is formatted from the symbols
        var number = pattern[numberStart..index];
        suffix = ParseAffix(pattern, ref index, untilNumber: false);

        var dot = number.IndexOf('.', StringComparison.Ordinal);
        var integer = dot < 0 ? number : number[..dot];
        var lastComma = integer.AsSpan().LastIndexOf(',');
        if (lastComma < 0)
        {
            primaryGrouping = 0;
            secondaryGrouping = 0;
            return;
        }

        primaryGrouping = integer.Length - lastComma - 1;
        var previousComma = lastComma > 0 ? integer.AsSpan(0, lastComma).LastIndexOf(',') : -1;
        secondaryGrouping = previousComma < 0 ? primaryGrouping : lastComma - previousComma - 1;
    }

    private static AffixToken[] ParseAffix(string pattern, ref int index, bool untilNumber)
    {
        var tokens = new List<AffixToken>();
        var literal = new StringBuilder();
        while (index < pattern.Length)
        {
            var c = pattern[index];
            if (untilNumber && IsNumberChar(c))
                break;

            if (c == '\'')
            {
                var end = pattern.AsSpan(index + 1).IndexOf('\'');
                end = end < 0 ? pattern.Length : end + index + 1;

                if (end == index + 1)
                {
                    literal.Append('\'');
                }
                else
                {
                    literal.Append(pattern, index + 1, end - index - 1);
                }

                index = end + 1;
                continue;
            }

            var kind = c switch
            {
                '\u00A4' => AffixKind.Currency,
                '%' => AffixKind.Percent,
                '\u2030' => AffixKind.PerMille,
                '-' => AffixKind.MinusSign,
                '+' => AffixKind.PlusSign,
                _ => AffixKind.Literal,
            };

            if (kind is AffixKind.Literal)
            {
                literal.Append(c);
                index++;
                continue;
            }

            if (literal.Length > 0)
            {
                tokens.Add(new AffixToken(AffixKind.Literal, literal.ToString()));
                literal.Clear();
            }

            var start = index;
            index++;
            while (kind is AffixKind.Currency && index < pattern.Length && pattern[index] == '\u00A4')
            {
                index++;
            }

            tokens.Add(new AffixToken(kind, pattern[start..index]));
        }

        if (literal.Length > 0)
        {
            tokens.Add(new AffixToken(AffixKind.Literal, literal.ToString()));
        }

        return [.. tokens];
    }
}
