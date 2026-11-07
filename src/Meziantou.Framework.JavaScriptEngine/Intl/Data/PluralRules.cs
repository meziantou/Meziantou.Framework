using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.JavaScriptEngine.Intl.Data;

internal enum PluralRuleType
{
    Cardinal,
    Ordinal,
}

/// <summary>
/// The plural operands of UTS 35 (section 5.1, "Plural Operand Meanings") of a decimal number. The integer operands
/// keep their last 18 digits (enough for the moduli of the rules, which are powers of ten) and whether they are larger.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PluralOperands
{
    /// <summary>Gets the integer digits of n (i), modulo 10^18.</summary>
    public ulong I { get; init; }

    /// <summary>Gets whether i is 10^18 or more.</summary>
    public bool IsLargeInteger { get; init; }

    /// <summary>Gets the number of visible fraction digits, with trailing zeros (v).</summary>
    public int V { get; init; }

    /// <summary>Gets the number of visible fraction digits, without trailing zeros (w).</summary>
    public int W { get; init; }

    /// <summary>Gets the visible fraction digits, with trailing zeros (f), modulo 10^18.</summary>
    public ulong F { get; init; }

    /// <summary>Gets whether f is 10^18 or more.</summary>
    public bool IsLargeF { get; init; }

    /// <summary>Gets the visible fraction digits, without trailing zeros (t), modulo 10^18.</summary>
    public ulong T { get; init; }

    /// <summary>Gets whether t is 10^18 or more.</summary>
    public bool IsLargeT { get; init; }

    /// <summary>Gets the exponent of the compact decimal or scientific notation (c and e, which are synonyms).</summary>
    public int E { get; init; }

    /// <summary>
    /// Parses a decimal number: an optional '-', digits, an optional '.' followed by digits, and an optional exponent
    /// ('c' or 'e' followed by digits, as in the samples of the rules: "1.2c3" is 1200 with c = 3). The trailing zeros
    /// of the fraction are significant ("1.50" has v = 2). With an exponent, the operands are the ones of the number
    /// shifted by the exponent (UTS 35: n, i, v, w, f and t of "1.2c3" are the ones of 1200).
    /// </summary>
    public static bool TryParse(ReadOnlySpan<char> value, out PluralOperands result)
    {
        result = default;
        if (value.StartsWith('-'))
        {
            value = value[1..];
        }

        var exponent = 0;
        var exponentIndex = value.IndexOfAny('c', 'e');
        if (exponentIndex >= 0)
        {
            var exponentText = value[(exponentIndex + 1)..];
            if (exponentText.IsEmpty || exponentText.Length > 4)
                return false;

            foreach (var c in exponentText)
            {
                if (!char.IsAsciiDigit(c))
                    return false;

                exponent = (exponent * 10) + (c - '0');
            }

            value = value[..exponentIndex];
        }

        var dot = value.IndexOf('.');
        var integerPart = dot < 0 ? value : value[..dot];
        var fractionPart = dot < 0 ? [] : value[(dot + 1)..];
        if (integerPart.IsEmpty || (dot >= 0 && fractionPart.IsEmpty))
            return false;

        foreach (var c in integerPart)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        foreach (var c in fractionPart)
        {
            if (!char.IsAsciiDigit(c))
                return false;
        }

        // Shift the decimal point by the exponent
        string? shifted;
        if (exponent > 0)
        {
            var digits = string.Concat(integerPart, fractionPart);
            var pointPosition = integerPart.Length + exponent;
            if (pointPosition >= digits.Length)
            {
                shifted = digits + new string('0', pointPosition - digits.Length);
                integerPart = shifted;
                fractionPart = [];
            }
            else
            {
                shifted = digits;
                integerPart = shifted.AsSpan(0, pointPosition);
                fractionPart = shifted.AsSpan(pointPosition);
            }
        }

        var (i, isLargeInteger) = ToInteger(integerPart);
        var trimmedFraction = fractionPart.TrimEnd('0');
        var (f, isLargeFraction) = ToInteger(fractionPart);
        var (t, isLargeTrimmedFraction) = ToInteger(trimmedFraction);
        result = new PluralOperands
        {
            I = i,
            IsLargeInteger = isLargeInteger,
            V = fractionPart.Length,
            W = trimmedFraction.Length,
            F = f,
            IsLargeF = isLargeFraction,
            T = t,
            IsLargeT = isLargeTrimmedFraction,
            E = exponent,
        };
        return true;

        static (ulong Value, bool IsLarge) ToInteger(ReadOnlySpan<char> digits)
        {
            var isLarge = digits.TrimStart('0').Length > 18;
            if (digits.Length > 18)
            {
                digits = digits[^18..];
            }

            ulong value = 0;
            foreach (var c in digits)
            {
                value = (value * 10) + (ulong)(c - '0');
            }

            return (value, isLarge);
        }
    }

    public static PluralOperands Parse(string value) => TryParse(value, out var result) ? result : throw new FormatException("Invalid decimal number: " + value);
}

/// <summary>
/// The plural rules of a locale (supplemental plurals.json and ordinals.json). The rules are parsed on first use from
/// the CLDR syntax (UTS 35 section 5.1, the samples after '@' are ignored).
/// </summary>
internal sealed class PluralRules
{
    /// <summary>The categories in the order of ECMA-402 (resolvedOptions().pluralCategories).</summary>
    private static readonly string[] CategoryOrder = ["zero", "one", "two", "few", "many", "other"];

    private readonly (string Category, PluralCondition Condition)[] _rules;

    private PluralRules(string locale, PluralRuleType type, (string Category, PluralCondition Condition)[] rules, string[] samples)
    {
        Locale = locale;
        Type = type;
        _rules = rules;
        Samples = samples;
        Categories = [.. rules.Select(rule => rule.Category)];
    }

    /// <summary>Gets the key of the rules in CLDR (a language, e.g. "fr", or a locale, e.g. "pt-PT").</summary>
    public string Locale { get; }

    public PluralRuleType Type { get; }

    /// <summary>Gets the categories, in the order of ECMA-402 ("zero", "one", "two", "few", "many", "other").</summary>
    public IReadOnlyList<string> Categories { get; }

    /// <summary>Gets the samples of each category ("@integer 0, 2~16, ... @decimal 0.0~1.5, ..."), in the order of <see cref="Categories"/>.</summary>
    internal IReadOnlyList<string> Samples { get; }

    /// <summary>Gets the category of a number (PluralRuleSelect).</summary>
    public string Select(in PluralOperands operands)
    {
        foreach (var (category, condition) in _rules)
        {
            if (condition.Matches(operands))
                return category;
        }

        return "other";
    }

    /// <summary>Creates the rules of the root locale: every number is "other".</summary>
    internal static PluralRules CreateOtherOnly(PluralRuleType type) => new("root", type, [("other", PluralCondition.Parse(""))], [""]);

    internal static PluralRules Create(string locale, PluralRuleType type, CldrTable table)
    {
        var rules = new List<(string Category, PluralCondition Condition, string Samples)>();
        for (var i = 0; i < table.Count; i++)
        {
            const string Prefix = "pluralRule-count-";
            var key = table.GetKey(i);
            if (!key.StartsWith(Prefix, StringComparison.Ordinal))
                continue;

            var text = table.GetValue(i).GetString();
            var at = text.IndexOf('@', StringComparison.Ordinal);
            var condition = at < 0 ? text : text[..at];
            rules.Add((key[Prefix.Length..], PluralCondition.Parse(condition), at < 0 ? "" : text[at..].Trim()));
        }

        rules.Sort((a, b) => Array.IndexOf(CategoryOrder, a.Category).CompareTo(Array.IndexOf(CategoryOrder, b.Category)));
        return new PluralRules(locale, type, [.. rules.Select(rule => (rule.Category, rule.Condition))], [.. rules.Select(rule => rule.Samples)]);
    }
}

/// <summary>A condition of a plural rule: a disjunction of conjunctions of relations.</summary>
internal sealed class PluralCondition
{
    private readonly PluralRelation[][] _orConditions;

    private PluralCondition(PluralRelation[][] orConditions) => _orConditions = orConditions;

    /// <summary>Gets whether the condition is empty (the "other" category, which matches every number).</summary>
    public bool IsEmpty => _orConditions.Length == 0;

    public bool Matches(in PluralOperands operands)
    {
        if (IsEmpty)
            return true;

        foreach (var andConditions in _orConditions)
        {
            var result = true;
            foreach (var relation in andConditions)
            {
                if (!relation.Matches(operands))
                {
                    result = false;
                    break;
                }
            }

            if (result)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Parses a condition: <c>and_condition ('or' and_condition)*</c>, <c>and_condition = relation ('and' relation)*</c>,
    /// <c>relation = operand ('%' value)? ('=' | '!=') (value | value '..' value) (',' ...)*</c>.
    /// </summary>
    public static PluralCondition Parse(string text)
    {
        var tokens = text.Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        var position = 0;
        var orConditions = new List<PluralRelation[]>();
        if (tokens.Length == 0)
            return new PluralCondition([]);

        while (true)
        {
            var andConditions = new List<PluralRelation>();
            while (true)
            {
                andConditions.Add(ParseRelation());
                if (position < tokens.Length && tokens[position] is "and")
                {
                    position++;
                    continue;
                }

                break;
            }

            orConditions.Add([.. andConditions]);
            if (position < tokens.Length && tokens[position] is "or")
            {
                position++;
                continue;
            }

            break;
        }

        if (position != tokens.Length)
            throw new FormatException("Invalid plural rule: " + text);

        return new PluralCondition([.. orConditions]);

        PluralRelation ParseRelation()
        {
            var operand = Next();
            if (operand is not ("n" or "i" or "v" or "w" or "f" or "t" or "c" or "e"))
                throw new FormatException("Invalid plural operand in: " + text);

            ulong modulus = 0;
            var op = Next();
            if (op is "%")
            {
                modulus = ulong.Parse(Next(), NumberStyles.None, CultureInfo.InvariantCulture);
                op = Next();
            }

            var negated = op switch
            {
                "=" => false,
                "!=" => true,
                _ => throw new FormatException("Invalid plural relation in: " + text),
            };

            var ranges = new List<(ulong Start, ulong End)>();
            foreach (var item in Next().Split(','))
            {
                var separator = item.IndexOf("..", StringComparison.Ordinal);
                if (separator < 0)
                {
                    var value = ulong.Parse(item, NumberStyles.None, CultureInfo.InvariantCulture);
                    ranges.Add((value, value));
                }
                else
                {
                    ranges.Add((ulong.Parse(item.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture), ulong.Parse(item.AsSpan(separator + 2), NumberStyles.None, CultureInfo.InvariantCulture)));
                }
            }

            return new PluralRelation(operand[0], modulus, negated, [.. ranges]);
        }

        string Next() => position < tokens.Length ? tokens[position++] : throw new FormatException("Invalid plural rule: " + text);
    }
}

/// <summary>A relation of a plural rule, e.g. <c>n % 100 != 11..19</c>.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct PluralRelation(char Operand, ulong Modulus, bool Negated, (ulong Start, ulong End)[] Ranges)
{
    public bool Matches(in PluralOperands operands)
    {
        var (value, isLarge, isInteger) = Operand switch
        {
            'n' => (operands.I, operands.IsLargeInteger, operands.T == 0 && !operands.IsLargeT),
            'i' => (operands.I, operands.IsLargeInteger, true),
            'v' => ((ulong)operands.V, false, true),
            'w' => ((ulong)operands.W, false, true),
            'f' => (operands.F, operands.IsLargeF, true),
            't' => (operands.T, operands.IsLargeT, true),
            _ => ((ulong)operands.E, false, true),
        };

        if (Modulus != 0)
        {
            value %= Modulus;
            isLarge = false;
        }

        var result = false;
        if (isInteger && !isLarge)
        {
            foreach (var (start, end) in Ranges)
            {
                if (value >= start && value <= end)
                {
                    result = true;
                    break;
                }
            }
        }

        return result != Negated;
    }
}
