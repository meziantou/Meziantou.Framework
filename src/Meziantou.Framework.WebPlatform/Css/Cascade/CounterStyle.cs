using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Cascade;

/// <summary>The algorithm of a counter style (https://drafts.csswg.org/css-counter-styles-3/#counter-style-system).</summary>
internal enum CounterStyleSystem : byte
{
    Cyclic,
    Numeric,
    Alphabetic,
    Symbolic,
    Additive,
    Fixed,

    /// <summary>
    /// The longhand East Asian styles (https://drafts.csswg.org/css-counter-styles-3/#extended-range-optional): the symbols are
    /// the ten digits, the markers of the tens, hundreds and thousands, then the markers of the groups of four digits.
    /// </summary>
    CjkLonghand,

    /// <summary>https://drafts.csswg.org/css-counter-styles-3/#ethiopic-numeric-counter-style</summary>
    EthiopicNumeric,
}

/// <summary>The disclosure styles, whose symbol depends on the writing mode and on the direction (https://drafts.csswg.org/css-counter-styles-3/#disclosure-open).</summary>
internal enum CounterStyleDisclosure : byte
{
    None,
    Open,
    Closed,
}

/// <summary>The variants of the algorithm of the longhand East Asian styles (https://drafts.csswg.org/css-counter-styles-3/#extended-range-optional).</summary>
internal enum CjkLonghandKind : byte
{
    ChineseInformal,
    ChineseFormal,
    JapaneseInformal,
    JapaneseFormal,
    KoreanInformal,
    KoreanFormal,
}

/// <summary>A reference to a counter style by name: the tree the name is looked up from (https://drafts.csswg.org/css-scoping/#css-tree-scoped-reference), or the predefined counter styles only.</summary>
internal readonly record struct CounterStyleReference(string Name, Node? Scope, bool Predefined);

/// <summary>
/// A counter style (https://drafts.csswg.org/css-counter-styles-3/#counter-styles): an algorithm that converts a counter value
/// to a string, with its negative sign, prefix, suffix, range, padding and fallback style. The counter styles are defined by
/// the @counter-style rules, by the symbols() function, or are predefined (<see cref="Predefined"/>).
/// </summary>
internal sealed class CounterStyle
{
    /// <summary>
    /// The longest representation, in UTF-16 code units, an algorithm that repeats symbols generates: the fallback style is used
    /// for the longer ones ("implementations may impose limits on the size of the representation", at least 60 code points;
    /// the limit of Chromium).
    /// </summary>
    internal const int MaxRepresentationLength = 120;

    /// <summary>The names that the @counter-style rules cannot define (https://drafts.csswg.org/css-counter-styles-3/#typedef-counter-style-name, https://drafts.csswg.org/css-counter-styles-3/#the-counter-style-rule).</summary>
    private static readonly FrozenSet<string> NonOverridableNames = FrozenSet.Create(StringComparer.Ordinal, "decimal", "disc", "square", "circle", "disclosure-open", "disclosure-closed");

    private static readonly Lazy<FrozenDictionary<string, CounterStyle>> PredefinedStyles = new(CreatePredefinedStyles);

    private static readonly (long Lower, long Upper)[] InfiniteRange = [(long.MinValue, long.MaxValue)];
    private static readonly (long Lower, long Upper)[] PositiveRange = [(1, long.MaxValue)];
    private static readonly (long Lower, long Upper)[] NonNegativeRange = [(0, long.MaxValue)];
    private static readonly (long Lower, long Upper)[] CjkLonghandRange = [(-9999_9999_9999_9999, 9999_9999_9999_9999)];

    /// <summary>
    /// The math functions of the integers of the descriptors are computed without an element: the font-relative lengths use the
    /// initial font size (https://drafts.csswg.org/css-values-4/#font-relative-lengths).
    /// </summary>
    private static readonly CssCalcContext CalcContext = new()
    {
        ResolveDimension = static (value, unit) => unit switch
        {
            "em" or "rem" or "ic" or "ric" or "lh" or "rlh" => (value * 16, "px"),
            "ex" or "rex" or "ch" or "rch" or "cap" or "rcap" => (value * 8, "px"),
            _ => null,
        },
    };

    private CounterStyle(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public CounterStyleSystem System { get; private init; }

    /// <summary>The value of the first symbol of the fixed system.</summary>
    public long FirstSymbolValue { get; private init; } = 1;

    public string[] Symbols { get; private init; } = [];

    /// <summary>The additive tuples, by decreasing weight.</summary>
    public (long Weight, string Symbol)[] AdditiveSymbols { get; private init; } = [];

    public CounterStyleDisclosure Disclosure { get; private init; }

    public CjkLonghandKind CjkLonghandKind { get; private init; }

    public string NegativePrefix { get; private init; } = "-";

    public string NegativeSuffix { get; private init; } = "";

    public string Prefix { get; private init; } = "";

    public string Suffix { get; private init; } = ". ";

    /// <summary>The ranges of the range descriptor (inclusive bounds), or null when it is auto: the range depends on the system.</summary>
    public (long Lower, long Upper)[]? Ranges { get; private init; }

    public long PadLength { get; private init; }

    public string PadSymbol { get; private init; } = "";

    /// <summary>The style used for the values the style cannot represent.</summary>
    public CounterStyleReference Fallback { get; private init; } = new("decimal", Scope: null, Predefined: true);

    /// <summary>The predefined counter styles (https://drafts.csswg.org/css-counter-styles-3/#predefined-counters), by name.</summary>
    public static FrozenDictionary<string, CounterStyle> Predefined => PredefinedStyles.Value;

    /// <summary>The decimal style: the style of the unknown names and the end of the fallback chains.</summary>
    public static CounterStyle Decimal => Predefined["decimal"];

    /// <summary>
    /// The predefined names are ASCII case-insensitive and "ASCII lowercased on parse wherever they are used as counter styles";
    /// the other names are case-sensitive (https://drafts.csswg.org/css-counter-styles-3/#typedef-counter-style-name).
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (Predefined.ContainsKey(name))
            return name;

        var lower = InfraAscii.ToAsciiLowercase(name);
        return Predefined.ContainsKey(lower) ? lower : name;
    }

    /// <summary>Whether a @counter-style rule cannot have the name (none and the CSS-wide keywords are not counter style names at all).</summary>
    public static bool IsNonOverridableName(string name) => NonOverridableNames.Contains(InfraAscii.ToAsciiLowercase(name));

    /// <summary>Whether the counter value is in the range of the style (https://drafts.csswg.org/css-counter-styles-3/#counter-style-range).</summary>
    public bool IsInRange(long value)
    {
        var ranges = Ranges ?? System switch
        {
            CounterStyleSystem.Cyclic or CounterStyleSystem.Numeric or CounterStyleSystem.Fixed => InfiniteRange,
            CounterStyleSystem.Alphabetic or CounterStyleSystem.Symbolic or CounterStyleSystem.EthiopicNumeric => PositiveRange,
            CounterStyleSystem.Additive => NonNegativeRange,
            _ => CjkLonghandRange,
        };

        foreach (var (lower, upper) in ranges)
        {
            if (value >= lower && value <= upper)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Generates the representation of a counter value (https://drafts.csswg.org/css-counter-styles-3/#generate-a-counter), without
    /// the prefix and the suffix of the style. Returns null when the style cannot represent the value: the fallback style is used.
    /// </summary>
    public string? Generate(long value, WritingMode writingMode, Direction direction)
    {
        if (!IsInRange(value))
            return null;

        // "If the counter value is negative and the counter style uses a negative sign, instead generate an initial
        // representation using the absolute value of the counter value"
        var negative = value < 0 && System is not (CounterStyleSystem.Cyclic or CounterStyleSystem.Fixed);
        var initial = GenerateInitialRepresentation(negative ? -value : value, writingMode, direction);
        if (initial is null)
            return null;

        // https://drafts.csswg.org/css-counter-styles-3/#counter-style-pad: the length is a number of grapheme clusters, which
        // includes the ones of the negative sign
        var difference = PadLength - GraphemeClusters.Count(initial);
        if (negative)
        {
            difference -= GraphemeClusters.Count(NegativePrefix) + GraphemeClusters.Count(NegativeSuffix);
        }

        if (!negative && difference <= 0)
            return initial;

        var builder = new StringBuilder();
        if (negative)
        {
            builder.Append(NegativePrefix);
        }

        if (difference > 0 && PadSymbol.Length > 0)
        {
            if (difference * PadSymbol.Length > MaxRepresentationLength)
                return null;

            builder.Insert(builder.Length, PadSymbol, (int)difference);
        }

        builder.Append(initial);
        if (negative)
        {
            builder.Append(NegativeSuffix);
        }

        return builder.ToString();
    }

    private string? GenerateInitialRepresentation(long value, WritingMode writingMode, Direction direction)
    {
        var symbols = Symbols;
        switch (System)
        {
            // https://drafts.csswg.org/css-counter-styles-3/#cyclic-system
            case CounterStyleSystem.Cyclic:
            {
                if (Disclosure != CounterStyleDisclosure.None)
                    return GetDisclosureSymbol(Disclosure, writingMode, direction);

                var index = (value - 1) % symbols.Length;
                return symbols[index < 0 ? index + symbols.Length : index];
            }

            // https://drafts.csswg.org/css-counter-styles-3/#fixed-system
            case CounterStyleSystem.Fixed:
                return value >= FirstSymbolValue && value - FirstSymbolValue < symbols.Length ? symbols[value - FirstSymbolValue] : null;

            // https://drafts.csswg.org/css-counter-styles-3/#symbolic-system: the symbol is doubled, tripled... on each pass
            case CounterStyleSystem.Symbolic:
            {
                if (value < 1)
                    return null;

                var symbol = symbols[(value - 1) % symbols.Length];
                var count = ((value - 1) / symbols.Length) + 1;
                if (count * symbol.Length > MaxRepresentationLength)
                    return null;

                return count == 1 ? symbol : new StringBuilder().Insert(0, symbol, (int)count).ToString();
            }

            // https://drafts.csswg.org/css-counter-styles-3/#alphabetic-system: a bijective numeration
            case CounterStyleSystem.Alphabetic:
            {
                if (value < 1)
                    return null;

                var parts = new List<string>();
                while (value != 0)
                {
                    value--;
                    parts.Add(symbols[value % symbols.Length]);
                    value /= symbols.Length;
                }

                parts.Reverse();
                return string.Concat(parts);
            }

            // https://drafts.csswg.org/css-counter-styles-3/#numeric-system: a positional numeration
            case CounterStyleSystem.Numeric:
            {
                if (value == 0)
                    return symbols[0];

                var parts = new List<string>();
                while (value != 0)
                {
                    parts.Add(symbols[value % symbols.Length]);
                    value /= symbols.Length;
                }

                parts.Reverse();
                return string.Concat(parts);
            }

            // https://drafts.csswg.org/css-counter-styles-3/#additive-system
            case CounterStyleSystem.Additive:
            {
                if (value == 0)
                    return AdditiveSymbols is [.., (0, var zero)] ? zero : null;

                var builder = new StringBuilder();
                foreach (var (weight, symbol) in AdditiveSymbols)
                {
                    if (weight == 0 || weight > value)
                        continue;

                    var count = value / weight;
                    if (builder.Length + (count * symbol.Length) > MaxRepresentationLength)
                        return null;

                    builder.Insert(builder.Length, symbol, (int)count);
                    value -= weight * count;
                    if (value == 0)
                        return builder.ToString();
                }

                return null;
            }

            case CounterStyleSystem.CjkLonghand:
                return GenerateCjkLonghand(value);

            case CounterStyleSystem.EthiopicNumeric:
                return GenerateEthiopic(value);

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/css-counter-styles-3/#disclosure-closed: the closed triangle points toward the inline-end
    /// direction, the open one toward the block-end direction (the small triangles U+25B8, U+25C2, U+25BE and U+25B4).
    /// </summary>
    private static string GetDisclosureSymbol(CounterStyleDisclosure disclosure, WritingMode writingMode, Direction direction)
    {
        const string Right = "\U000025B8";
        const string Left = "\U000025C2";
        const string Down = "\U000025BE";
        const string Up = "\U000025B4";
        if (disclosure == CounterStyleDisclosure.Open)
        {
            return writingMode switch
            {
                WritingMode.HorizontalTb => Down,
                WritingMode.VerticalRl or WritingMode.SidewaysRl => Left,
                _ => Right,
            };
        }

        // The inline base direction of sideways-lr is from the bottom to the top
        var forward = (direction == Direction.Ltr) != (writingMode == WritingMode.SidewaysLr);
        if (writingMode == WritingMode.HorizontalTb)
            return forward ? Right : Left;

        return forward ? Down : Up;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-counter-styles-3/#extended-range-optional: the number is split into groups of four digits;
    /// the digits are followed by the marker of their rank, and the groups by their marker. The informal styles omit some of
    /// the digits 1, the Chinese styles keep one zero for each run of zeros that is not at the end of a group, and the Korean
    /// styles separate the groups with a space.
    /// </summary>
    private string? GenerateCjkLonghand(long value)
    {
        if (value > 9999_9999_9999_9999)
            return null;

        var symbols = Symbols;
        if (value == 0)
            return symbols[0];

        var kind = CjkLonghandKind;
        var chinese = kind is CjkLonghandKind.ChineseInformal or CjkLonghandKind.ChineseFormal;
        var korean = kind is CjkLonghandKind.KoreanInformal or CjkLonghandKind.KoreanFormal;
        var builder = new StringBuilder();
        var pendingZero = false;
        for (long groupIndex = 3, groupDivisor = 1_0000_0000_0000; groupIndex >= 0; groupIndex--, groupDivisor /= 10000)
        {
            var group = value / groupDivisor % 10000;
            if (group == 0)
            {
                // The zeros between two digits are a single zero (the Chinese styles)
                pendingZero = builder.Length > 0;
                continue;
            }

            if (korean && builder.Length > 0)
            {
                builder.Append(' ');
            }

            for (long rank = 3, divisor = 1000; rank >= 0; rank--, divisor /= 10)
            {
                var digit = group / divisor % 10;
                if (digit == 0)
                {
                    pendingZero = builder.Length > 0;
                    continue;
                }

                if (pendingZero && chinese)
                {
                    builder.Append(symbols[0]);
                }

                pendingZero = false;
                var dropped = digit == 1 && kind switch
                {
                    // "For any group with a value between ten and nineteen, remove the tens digit"
                    CjkLonghandKind.ChineseInformal => rank == 1 && group is >= 10 and <= 19,

                    // The digit 1 before a digit marker
                    CjkLonghandKind.JapaneseInformal => rank > 0,

                    // "If the value of the ten-thousands group is 1, drop the digit"
                    CjkLonghandKind.KoreanInformal => rank > 0 || (groupIndex == 1 && group == 1),
                    _ => false,
                };
                if (!dropped)
                {
                    builder.Append(symbols[digit]);
                }

                if (rank > 0)
                {
                    builder.Append(symbols[9 + rank]);
                }
            }

            if (groupIndex > 0)
            {
                builder.Append(symbols[12 + groupIndex]);
            }

            // The trailing zeros of a group are dropped
            pendingZero = false;
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/css-counter-styles-3/#ethiopic-numeric-counter-style</summary>
    private static string? GenerateEthiopic(long value)
    {
        if (value < 1)
            return null;

        // The units are U+1369..U+1371, the tens U+1372..U+137A, the hundred U+137B and the ten thousand U+137C
        if (value == 1)
            return "\U00001369";

        var groups = new List<int>();
        for (; value > 0; value /= 100)
        {
            groups.Add((int)(value % 100));
        }

        var builder = new StringBuilder();
        for (var index = groups.Count - 1; index >= 0; index--)
        {
            var group = groups[index];
            var removed = group == 0 || (group == 1 && (index == groups.Count - 1 || index % 2 == 1));
            if (!removed)
            {
                if (group / 10 > 0)
                {
                    builder.Append((char)(0x1372 + (group / 10) - 1));
                }

                if (group % 10 > 0)
                {
                    builder.Append((char)(0x1369 + (group % 10) - 1));
                }
            }

            if (index % 2 == 1)
            {
                if (group != 0)
                {
                    builder.Append('\U0000137B');
                }
            }
            else if (index != 0)
            {
                builder.Append('\U0000137C');
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Creates the counter style of a @counter-style rule (https://drafts.csswg.org/css-counter-styles-3/#the-counter-style-rule),
    /// or returns null when the rule does not define a counter style: its system needs symbols that it does not have. The
    /// descriptors that are not specified have their initial value, or the value of the extended style.
    /// </summary>
    /// <param name="rule">The rule.</param>
    /// <param name="scope">The tree of the rule, the names of its extends system and of its fallback are looked up from.</param>
    /// <param name="predefined">The rule is a predefined counter style: its names reference predefined counter styles.</param>
    /// <param name="resolveExtended">Returns the counter style that the rule extends (the decimal style when it is unknown or when the styles extend each other in a cycle).</param>
    public static CounterStyle? Create(CssCounterStyleRule rule, Node? scope, bool predefined, Func<CounterStyleReference, CounterStyle> resolveExtended)
    {
        // The names of the predefined counter styles are in lowercase (they are being created: they cannot be looked up)
        var name = predefined ? rule.Name : NormalizeName(rule.Name);
        var symbols = ParseSymbols(rule.GetDescriptorValue("symbols"));
        var additiveSymbols = ParseAdditiveSymbols(rule.GetDescriptorValue("additive-symbols"));
        if (symbols is null || additiveSymbols is null)
            return null;

        // https://drafts.csswg.org/css-counter-styles-3/#counter-style-system: symbolic when the descriptor is omitted
        var system = rule.GetDescriptorValue("system") ?? [];
        var keyword = system is [CssPreservedToken { Type: CssTokenType.Ident } ident, ..] ? InfraAscii.ToAsciiLowercase(ident.Token.Value) : "symbolic";
        CounterStyle? extended = null;
        long firstSymbolValue = 1;
        CounterStyleSystem algorithm;
        switch (keyword)
        {
            // "If a @counter-style uses the extends system, it must not contain a symbols or additive-symbols descriptor"
            case "extends" when system is [_, CssPreservedToken { Type: CssTokenType.Ident } extendedName] && symbols.Length == 0 && additiveSymbols.Length == 0:
                extended = resolveExtended(new CounterStyleReference(predefined ? extendedName.Token.Value : NormalizeName(extendedName.Token.Value), scope, predefined));
                algorithm = extended.System;
                break;

            case "cyclic" or "symbolic" when symbols.Length >= 1:
                algorithm = keyword == "cyclic" ? CounterStyleSystem.Cyclic : CounterStyleSystem.Symbolic;
                break;

            case "fixed" when symbols.Length >= 1:
                algorithm = CounterStyleSystem.Fixed;
                if (system.Count > 1 && !TryGetInteger(system[1], out firstSymbolValue))
                    return null;

                break;

            case "numeric" or "alphabetic" when symbols.Length >= 2:
                algorithm = keyword == "numeric" ? CounterStyleSystem.Numeric : CounterStyleSystem.Alphabetic;
                break;

            case "additive" when additiveSymbols.Length >= 1:
                algorithm = CounterStyleSystem.Additive;
                break;

            default:
                return null;
        }

        var negative = ParseSymbols(rule.GetDescriptorValue("negative"));
        var pad = ParsePad(rule.GetDescriptorValue("pad"));
        var range = rule.GetDescriptorValue("range");
        var fallback = rule.GetDescriptorValue("fallback") is [CssPreservedToken { Type: CssTokenType.Ident } fallbackName]
            ? new CounterStyleReference(predefined ? fallbackName.Token.Value : NormalizeName(fallbackName.Token.Value), scope, predefined)
            : (CounterStyleReference?)null;

        return new CounterStyle(name)
        {
            System = algorithm,
            FirstSymbolValue = extended?.FirstSymbolValue ?? firstSymbolValue,
            Symbols = extended?.Symbols ?? symbols,
            AdditiveSymbols = extended?.AdditiveSymbols ?? additiveSymbols,
            Disclosure = extended?.Disclosure ?? CounterStyleDisclosure.None,
            CjkLonghandKind = extended?.CjkLonghandKind ?? default,
            NegativePrefix = negative is [var negativePrefix, ..] ? negativePrefix : extended?.NegativePrefix ?? "-",
            NegativeSuffix = negative is [_, var negativeSuffix] ? negativeSuffix : negative is [_] ? "" : extended?.NegativeSuffix ?? "",
            Prefix = ParseSymbols(rule.GetDescriptorValue("prefix")) is [var prefix] ? prefix : extended?.Prefix ?? "",
            Suffix = ParseSymbols(rule.GetDescriptorValue("suffix")) is [var suffix] ? suffix : extended?.Suffix ?? ". ",
            Ranges = range is not null ? ParseRanges(range) : extended?.Ranges,
            PadLength = pad?.Length ?? extended?.PadLength ?? 0,
            PadSymbol = pad?.Symbol ?? extended?.PadSymbol ?? "",
            Fallback = fallback ?? extended?.Fallback ?? new CounterStyleReference("decimal", Scope: null, Predefined: true),
        };
    }

    /// <summary>
    /// Creates the anonymous counter style of a symbols() function (https://drafts.csswg.org/css-counter-styles-3/#symbols-function):
    /// no prefix, a space as suffix, and the decimal style as fallback. Returns null when the function is invalid.
    /// </summary>
    public static CounterStyle? Create(CssFunction function)
    {
        var arguments = ValueMatcher.RemoveWhitespace(function.Value);
        var system = CounterStyleSystem.Symbolic;
        if (arguments is [CssPreservedToken { Type: CssTokenType.Ident } type, ..])
        {
            switch (InfraAscii.ToAsciiLowercase(type.Token.Value))
            {
                case "cyclic":
                    system = CounterStyleSystem.Cyclic;
                    break;
                case "numeric":
                    system = CounterStyleSystem.Numeric;
                    break;
                case "alphabetic":
                    system = CounterStyleSystem.Alphabetic;
                    break;
                case "symbolic":
                    system = CounterStyleSystem.Symbolic;
                    break;
                case "fixed":
                    system = CounterStyleSystem.Fixed;
                    break;
                default:
                    return null;
            }

            arguments.RemoveAt(0);
        }

        var symbols = new string[arguments.Count];
        for (var i = 0; i < symbols.Length; i++)
        {
            if (arguments[i] is not CssPreservedToken { Type: CssTokenType.String } symbol)
                return null;

            symbols[i] = symbol.Token.Value;
        }

        if (symbols.Length < (system is CounterStyleSystem.Numeric or CounterStyleSystem.Alphabetic ? 2 : 1))
            return null;

        return new CounterStyle("") { System = system, Symbols = symbols, Suffix = " " };
    }

    /// <summary>The symbols of a descriptor (strings and identifiers); null when one of them is an image, which is not supported (like the browsers).</summary>
    private static string[]? ParseSymbols(List<CssComponentValue>? values)
    {
        if (values is null)
            return [];

        var symbols = new string[values.Count];
        for (var i = 0; i < symbols.Length; i++)
        {
            if (GetSymbol(values[i]) is not { } symbol)
                return null;

            symbols[i] = symbol;
        }

        return symbols;
    }

    private static string? GetSymbol(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.String or CssTokenType.Ident } token ? token.Token.Value : null;

    private static (long Weight, string Symbol)[]? ParseAdditiveSymbols(List<CssComponentValue>? value)
    {
        if (value is null)
            return [];

        var tuples = ValueMatcher.SplitAtCommas(value);
        var result = new (long Weight, string Symbol)[tuples.Count];
        for (var i = 0; i < result.Length; i++)
        {
            // A weight and a symbol, in any order; the weights are in strictly decreasing order (the math functions are
            // only known now)
            if (tuples[i] is not [var first, var second])
                return null;

            var (weight, symbol) = GetSymbol(first) is null ? (first, second) : (second, first);
            if (!TryGetInteger(weight, out var weightValue) || GetSymbol(symbol) is not { } symbolValue)
                return null;

            // The result of a math function is clamped to the range of the weights (https://drafts.csswg.org/css-values-4/#calc-range)
            weightValue = Math.Max(weightValue, 0);
            if (i > 0 && result[i - 1].Weight <= weightValue)
                return null;

            result[i] = (weightValue, symbolValue);
        }

        return result;
    }

    /// <summary>The ranges of the range descriptor; null for auto, and for the values that are only known to be invalid once their math functions are computed.</summary>
    private static (long Lower, long Upper)[]? ParseRanges(List<CssComponentValue> value)
    {
        var ranges = ValueMatcher.SplitAtCommas(value);
        var result = new (long Lower, long Upper)[ranges.Count];
        for (var i = 0; i < result.Length; i++)
        {
            if (ranges[i] is not [var lower, var upper])
                return null;

            if (!TryGetBound(lower, long.MinValue, out var lowerValue) || !TryGetBound(upper, long.MaxValue, out var upperValue) || lowerValue > upperValue)
                return null;

            result[i] = (lowerValue, upperValue);
        }

        return result;

        static bool TryGetBound(CssComponentValue value, long infinite, out long result)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Ident })
            {
                result = infinite;
                return true;
            }

            return TryGetInteger(value, out result);
        }
    }

    private static (long Length, string Symbol)? ParsePad(List<CssComponentValue>? value)
    {
        // A length and a symbol, in any order
        if (value is not [var first, var second])
            return null;

        var (length, symbol) = GetSymbol(first) is null ? (first, second) : (second, first);
        return TryGetInteger(length, out var lengthValue) && GetSymbol(symbol) is { } symbolValue ? (Math.Max(lengthValue, 0), symbolValue) : null;
    }

    /// <summary>An &lt;integer&gt;: a number, or a math function, whose result is rounded to the nearest integer (https://drafts.csswg.org/css-values-4/#calc-range).</summary>
    private static bool TryGetInteger(CssComponentValue value, out long result)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Number } number:
                result = (long)number.Token.NumericValue;
                return true;

            case CssFunction function when CssCalc.Evaluate(function, CalcContext) is { IsResolved: true, Unit: "" } calc && !double.IsNaN(calc.Value):
                result = (long)Math.Clamp(Math.Round(calc.Value, MidpointRounding.ToPositiveInfinity), int.MinValue, int.MaxValue);
                return true;

            default:
                result = 0;
                return false;
        }
    }

    private static FrozenDictionary<string, CounterStyle> CreatePredefinedStyles()
    {
        var rules = new Dictionary<string, CssCounterStyleRule>(StringComparer.Ordinal);
        foreach (var rule in new CssRuleParser(sheet: null) { AllowsReservedCounterStyleNames = true }.ParseStylesheet(PredefinedCounterStyles.StyleSheet))
        {
            if (rule is CssCounterStyleRule counterStyleRule)
            {
                rules[counterStyleRule.Name] = counterStyleRule;
            }
        }

        var styles = new Dictionary<string, CounterStyle>(StringComparer.Ordinal);
        foreach (var name in rules.Keys)
        {
            Resolve(name);
        }

        // https://drafts.csswg.org/css-counter-styles-3/#complex-predefined-counters: the styles that the descriptors cannot
        // define, with the cjk-decimal style as fallback. The symbols are the code points of the digits 0 to 9, of the markers
        // of the second, third and fourth digits, and of the markers of the second, third and fourth groups
        var cjkDecimal = new CounterStyleReference("cjk-decimal", Scope: null, Predefined: true);
        const string ChineseSuffix = "\U00003001";
        const string KoreanSuffix = ", ";
        const string SimplifiedChineseNegative = "\U00008D1F";
        const string TraditionalChineseNegative = "\U00008CA0";
        const string JapaneseNegative = "\U000030DE\U000030A4\U000030CA\U000030B9";
        const string KoreanNegative = "\U0000B9C8\U0000C774\U0000B108\U0000C2A4 ";
        const string SimplifiedChineseFourthGroup = "\U00004E07\U00004EBF";
        AddCjkLonghand("simp-chinese-informal", CjkLonghandKind.ChineseInformal, [0x96F6, 0x4E00, 0x4E8C, 0x4E09, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x5341, 0x767E, 0x5343, 0x4E07, 0x4EBF], SimplifiedChineseFourthGroup, SimplifiedChineseNegative, ChineseSuffix);
        AddCjkLonghand("simp-chinese-formal", CjkLonghandKind.ChineseFormal, [0x96F6, 0x58F9, 0x8D30, 0x53C1, 0x8086, 0x4F0D, 0x9646, 0x67D2, 0x634C, 0x7396, 0x62FE, 0x4F70, 0x4EDF, 0x4E07, 0x4EBF], SimplifiedChineseFourthGroup, SimplifiedChineseNegative, ChineseSuffix);
        AddCjkLonghand("trad-chinese-informal", CjkLonghandKind.ChineseInformal, [0x96F6, 0x4E00, 0x4E8C, 0x4E09, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x5341, 0x767E, 0x5343, 0x842C, 0x5104, 0x5146], fourthGroup: null, TraditionalChineseNegative, ChineseSuffix);
        AddCjkLonghand("trad-chinese-formal", CjkLonghandKind.ChineseFormal, [0x96F6, 0x58F9, 0x8CB3, 0x53C3, 0x8086, 0x4F0D, 0x9678, 0x67D2, 0x634C, 0x7396, 0x62FE, 0x4F70, 0x4EDF, 0x842C, 0x5104, 0x5146], fourthGroup: null, TraditionalChineseNegative, ChineseSuffix);
        AddCjkLonghand("japanese-informal", CjkLonghandKind.JapaneseInformal, [0x3007, 0x4E00, 0x4E8C, 0x4E09, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x5341, 0x767E, 0x5343, 0x4E07, 0x5104, 0x5146], fourthGroup: null, JapaneseNegative, ChineseSuffix);
        AddCjkLonghand("japanese-formal", CjkLonghandKind.JapaneseFormal, [0x96F6, 0x58F1, 0x5F10, 0x53C2, 0x56DB, 0x4F0D, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x62FE, 0x767E, 0x9621, 0x842C, 0x5104, 0x5146], fourthGroup: null, JapaneseNegative, ChineseSuffix);
        AddCjkLonghand("korean-hangul-formal", CjkLonghandKind.KoreanFormal, [0xC601, 0xC77C, 0xC774, 0xC0BC, 0xC0AC, 0xC624, 0xC721, 0xCE60, 0xD314, 0xAD6C, 0xC2ED, 0xBC31, 0xCC9C, 0xB9CC, 0xC5B5, 0xC870], fourthGroup: null, KoreanNegative, KoreanSuffix);
        AddCjkLonghand("korean-hanja-informal", CjkLonghandKind.KoreanInformal, [0x96F6, 0x4E00, 0x4E8C, 0x4E09, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x5341, 0x767E, 0x5343, 0x842C, 0x5104, 0x5146], fourthGroup: null, KoreanNegative, KoreanSuffix);
        AddCjkLonghand("korean-hanja-formal", CjkLonghandKind.KoreanFormal, [0x96F6, 0x58F9, 0x8CB3, 0x53C3, 0x56DB, 0x4E94, 0x516D, 0x4E03, 0x516B, 0x4E5D, 0x62FE, 0x767E, 0x4EDF, 0x842C, 0x5104, 0x5146], fourthGroup: null, KoreanNegative, KoreanSuffix);

        // "This counter style is identical to trad-chinese-informal. (It exists for legacy reasons.)"
        styles["cjk-ideographic"] = styles["trad-chinese-informal"];
        styles["ethiopic-numeric"] = new CounterStyle("ethiopic-numeric") { System = CounterStyleSystem.EthiopicNumeric, Suffix = "/ " };
        return styles.ToFrozenDictionary(StringComparer.Ordinal);

        CounterStyle Resolve(string name)
        {
            if (styles.TryGetValue(name, out var style))
                return style;

            style = Create(rules[name], scope: null, predefined: true, reference => Resolve(reference.Name)) ?? throw new InvalidOperationException("The predefined counter style '" + name + "' is invalid");
            if (name is "disclosure-open" or "disclosure-closed")
            {
                style = new CounterStyle(name) { System = style.System, Symbols = style.Symbols, Suffix = style.Suffix, Disclosure = name == "disclosure-open" ? CounterStyleDisclosure.Open : CounterStyleDisclosure.Closed };
            }

            styles[name] = style;
            return style;
        }

        void AddCjkLonghand(string name, CjkLonghandKind kind, int[] symbols, string? fourthGroup, string negative, string suffix)
        {
            string[] symbolStrings = [.. symbols.Select(static symbol => ((char)symbol).ToString())];
            styles[name] = new CounterStyle(name)
            {
                System = CounterStyleSystem.CjkLonghand,
                CjkLonghandKind = kind,
                Symbols = fourthGroup is null ? symbolStrings : [.. symbolStrings, fourthGroup],
                NegativePrefix = negative,
                Suffix = suffix,
                Fallback = cjkDecimal,
            };
        }
    }

    public override string ToString() => Name.Length > 0 ? Name : "symbols(" + System.ToString().ToLower(CultureInfo.InvariantCulture) + ")";
}
