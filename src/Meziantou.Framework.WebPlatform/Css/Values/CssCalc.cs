using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>How the values of a math function (https://drafts.csswg.org/css-values-4/#math) are resolved.</summary>
internal sealed class CssCalcContext
{
    /// <summary>A context that resolves nothing: relative units are invalid and percentages are their own type.</summary>
    public static CssCalcContext Default { get; } = new();

    /// <summary>
    /// Converts a relative dimension (the unit is ASCII-lowercased, e.g. (2, "em"), (50, "vw"), (1, "rem"), (3, "cqw")) to a canonical value, e.g. (32, "px").
    /// Returns null when the unit cannot be resolved (the math function is then invalid).
    /// </summary>
    /// <remarks>
    /// Absolute units are converted by <see cref="CssCalc"/> itself: lengths (cm, mm, q, in, pt, pc, px) to px, angles (deg, grad, rad, turn) to deg,
    /// times (s, ms) to s, frequencies (hz, khz) to hz and resolutions (dppx, x, dpi, dpcm) to dppx. Unknown units are invalid.
    /// </remarks>
    public Func<double, string, (double Value, string Unit)?>? ResolveDimension { get; init; }

    /// <summary>
    /// When set, percentages are resolved against this value: p% is p / 100 * Value in Unit (e.g. the font-size or the line-height).
    /// When the unit is "", the percentages resolve to numbers.
    /// </summary>
    public (double Value, string Unit)? PercentageBasis { get; init; }

    /// <summary>
    /// The canonical unit percentages stand for when they are not resolved (e.g. "px" for &lt;length-percentage&gt;, "deg" for &lt;angle-percentage&gt;,
    /// "" for &lt;number-percentage&gt;), used for type checking (https://drafts.csswg.org/css-values-4/#css-type): calc(100% - 40px) is valid when this is "px".
    /// When null, a percentage is its own type (&lt;percentage&gt;): calc(50% + 10px) is invalid while calc(50% * 2) is valid.
    /// </summary>
    public string? PercentageUnit { get; init; }

    /// <summary>
    /// Resolves the tree counting functions (https://drafts.csswg.org/css-values-5/#tree-counting): sibling-index() and
    /// sibling-count() (the name is ASCII-lowercased). When null, or when it returns null, the math function is invalid.
    /// </summary>
    public Func<string, double?>? ResolveTreeCountingFunction { get; init; }

    /// <summary>
    /// Returns the random base value, in [0, 1), of a random() function (https://drafts.csswg.org/css-values-5/#random) from its
    /// &lt;random-value-sharing&gt;. When null, random() is not resolved, unless its base value is fixed.
    /// </summary>
    public Func<CssRandomSharing, double>? ResolveRandomBaseValue { get; init; }

    /// <summary>
    /// When true, the relative units are kept (e.g. calc(2em * 3) is calc(6em)): the value is a specified value
    /// (https://drafts.csswg.org/css-values-4/#calc-simplification). <see cref="ResolveDimension"/> is not used.
    /// </summary>
    public bool KeepRelativeUnits { get; init; }

    /// <summary>
    /// The channel keywords of a relative color (https://drafts.csswg.org/css-color-5/#relative-colors), e.g. r, g, b and
    /// alpha in rgb(from ...): they are numbers whose value is not known.
    /// </summary>
    public IReadOnlySet<string>? ChannelKeywords { get; init; }

    /// <summary>
    /// The used value, in px, of the <c>size</c> keyword of a <c>calc-size()</c> calculation
    /// (https://drafts.csswg.org/css-values-5/#valdef-calc-size-size): when set, the keyword is allowed in the evaluated
    /// calculation and resolves to this length (the layout resolves the calculation of a <c>calc-size()</c> with the size of its basis).
    /// </summary>
    public double? SizeKeywordValue { get; init; }

    /// <summary>
    /// The anchor functions that are allowed in the calculation as unresolved lengths (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos,
    /// https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn): <c>anchor()</c> in the inset properties, <c>anchor-size()</c> in
    /// the inset, margin and sizing properties. They are kept as written (their value is only known by the layout); a specified value
    /// (<see cref="KeepRelativeUnits"/>) allows both.
    /// </summary>
    public CssAnchorFunctions AnchorFunctions { get; init; }

    /// <summary>
    /// Resolves an <c>anchor()</c> or <c>anchor-size()</c> function to a length in px (the layout of an anchor-positioned box);
    /// null makes the calculation invalid. When set, <see cref="AnchorFunctions"/> is not used.
    /// </summary>
    public Func<CssFunction, double?>? ResolveAnchorFunction { get; init; }

    /// <summary>Serializes an unresolved anchor function (e.g. with its computed fallback value); the canonical specified form when null.</summary>
    public Func<CssFunction, string>? SerializeAnchorFunction { get; init; }
}

/// <summary>The anchor functions allowed in a value (https://drafts.csswg.org/css-anchor-position-1/).</summary>
[Flags]
internal enum CssAnchorFunctions : byte
{
    None = 0,

    /// <summary><c>anchor()</c> (the inset properties).</summary>
    Anchor = 1,

    /// <summary><c>anchor-size()</c> (the inset, margin and sizing properties).</summary>
    AnchorSize = 2,
}

/// <summary>The functions of the math function syntax (https://drafts.csswg.org/css-values-4/#math).</summary>
internal enum CssCalcFunctionKind : byte
{
    Calc,
    Min,
    Max,
    Clamp,
    Round,
    Mod,
    Rem,
    Sin,
    Cos,
    Tan,
    Asin,
    Acos,
    Atan,
    Atan2,
    Pow,
    Sqrt,
    Hypot,
    Log,
    Exp,
    Abs,
    Sign,
    Random,
    Progress,
    CalcSize,
    CalcMix,
}

/// <summary>https://drafts.csswg.org/css-values-4/#typedef-rounding-strategy</summary>
internal enum CssCalcRoundingStrategy : byte
{
    Nearest,
    Up,
    Down,
    ToZero,
}

/// <summary>
/// Evaluates math functions (https://drafts.csswg.org/css-values-4/#math): parses the calculation, checks its type, resolves and simplifies it
/// (https://drafts.csswg.org/css-values-4/#calc-simplification).
/// </summary>
/// <remarks>
/// Division follows the current draft (https://drafts.csswg.org/css-values-4/#calc-type-checking): the divisor may have a unit, and the type of the
/// division is the type of the dividend multiplied by the inverted type of the divisor, so calc(10px / 2px) is the number 5 and calc(1px / 1px * 1deg) is
/// 1deg. The result of the whole calculation must be a number or a single base type (e.g. calc(1px * 1px) is invalid).
/// Negating a sum of numeric values distributes the negation (calc(1px - (100% - 2px)) is calc(-100% + 3px)), like the multiplication of a sum by a number.
/// </remarks>
internal static class CssCalc
{
    private const int MaxDepth = 128;

    /// <summary>Returns whether the function name is a math function (https://drafts.csswg.org/css-values-4/#math-function), ASCII case-insensitively.</summary>
    public static bool IsMathFunction(string name) => GetKind(name) is not null;

    /// <summary>Parses, type-checks, resolves and simplifies a math function. Returns null when the function is invalid.</summary>
    public static CssCalcValue? Evaluate(CssFunction function, CssCalcContext context)
    {
        ArgumentNullException.ThrowIfNull(function);
        ArgumentNullException.ThrowIfNull(context);

        var kind = GetKind(function.Name);
        if (kind is null)
            return null;

        var evaluator = new Evaluator(context);
        var root = evaluator.ParseFunction(function, kind.Value, depth: 0);
        if (root is null)
            return null;

        var typeUnit = evaluator.GetUnitOfType(root.Type);
        if (typeUnit is null)
            return null;

        return new CssCalcValue(root, typeUnit, evaluator.PercentagesAreUnresolved);
    }

    /// <summary>The percentage units tried to type-check a specified value: a percentage is its own type, or stands for a length, an angle...</summary>
    private static readonly CssCalcContext[] SpecifiedValueContexts = [.. new string?[] { null, "px", "deg", "s", "hz", "" }.Select(unit => new CssCalcContext { KeepRelativeUnits = true, PercentageUnit = unit })];

    /// <summary>The contexts of a value of a property whose percentages stand for lengths: they are not resolved (min(1%, 2%) is not simplified).</summary>
    private static readonly CssCalcContext[] LengthPercentageSpecifiedValueContexts = [SpecifiedValueContexts[1], .. SpecifiedValueContexts.Where((_, index) => index != 1)];

    /// <summary>
    /// Serializes a math function as a specified value (https://drafts.csswg.org/css-values-4/#serialize-a-math-function):
    /// the calculation is simplified (the relative units are kept) and wrapped in calc() when its root is not a math function
    /// (e.g. calc(2em * 3) is calc(6em)). Returns null when the function cannot be simplified.
    /// </summary>
    /// <param name="function">The math function.</param>
    /// <param name="channelKeywords">The channel keywords of a relative color.</param>
    /// <param name="percentagesAreLengths">Whether the percentages stand for lengths that are resolved at used time (a &lt;length-percentage&gt; property).</param>
    public static string? SerializeSpecifiedValue(CssFunction function, IReadOnlySet<string>? channelKeywords = null, bool percentagesAreLengths = false)
    {
        var defaultContexts = percentagesAreLengths ? LengthPercentageSpecifiedValueContexts : SpecifiedValueContexts;
        var contexts = channelKeywords is null ? defaultContexts : [.. defaultContexts.Select(c => new CssCalcContext { KeepRelativeUnits = true, PercentageUnit = c.PercentageUnit, ChannelKeywords = channelKeywords })];
        foreach (var context in contexts)
        {
            if (Evaluate(function, context) is { } value)
            {
                var builder = new StringBuilder();

                // Like the browsers, calc() is kept around an exponential function that cannot be simplified (calc(pow(2, sign(1em - 18px))))
                var keepsCalc = GetKind(function.Name) == CssCalcFunctionKind.Calc && value.Root is CssCalcFunctionNode { Kind: CssCalcFunctionKind.Pow or CssCalcFunctionKind.Sqrt or CssCalcFunctionKind.Log or CssCalcFunctionKind.Exp };
                if (!keepsCalc && (value.Root is CssCalcFunctionNode or CssCalcKeywordNode { Name: "sibling-index()" or "sibling-count()" } || IsAnchorFunctionNode(value.Root)))
                {
                    AppendNode(builder, value.Root, omitParentheses: true);
                }
                else
                {
                    builder.Append("calc(");
                    AppendNode(builder, value.Root, omitParentheses: true);
                    builder.Append(')');
                }

                return builder.ToString();
            }
        }

        return null;
    }

    /// <summary>Whether a node is an unresolved anchor function (https://drafts.csswg.org/css-anchor-position-1/#anchor-pos): calc(anchor(--a top)) is anchor(--a top).</summary>
    internal static bool IsAnchorFunctionNode(CssCalcNode node) => node is CssCalcKeywordNode { Name: var name } && name.EndsWith(')', StringComparison.Ordinal) && (name.StartsWith("anchor(", StringComparison.Ordinal) || name.StartsWith("anchor-size(", StringComparison.Ordinal));

    /// <summary>Evaluates a component value when it is a math function; returns null otherwise or when the function is invalid.</summary>
    public static CssCalcValue? Evaluate(CssComponentValue value, CssCalcContext context) => value is CssFunction function ? Evaluate(function, context) : null;

    /// <summary>Returns the anchor function of a function name (<c>anchor</c> or <c>anchor-size</c>, ASCII case-insensitively), or null.</summary>
    internal static CssAnchorFunctions? GetAnchorFunction(string name)
    {
        if (InfraAscii.EqualsIgnoreAsciiCase(name, "anchor"))
            return CssAnchorFunctions.Anchor;

        if (InfraAscii.EqualsIgnoreAsciiCase(name, "anchor-size"))
            return CssAnchorFunctions.AnchorSize;

        return null;
    }

    /// <summary>Returns whether component values contain an anchor function (<c>anchor()</c> or <c>anchor-size()</c>), at any depth.</summary>
    internal static bool ContainsAnchorFunction(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when GetAnchorFunction(function.Name) is not null:
                    return true;
                case CssFunction function when ContainsAnchorFunction(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsAnchorFunction(block.Value):
                    return true;
            }
        }

        return false;
    }

    internal static CssCalcFunctionKind? GetKind(string name) => InfraAscii.ToAsciiLowercase(name) switch
    {
        "calc" or "-webkit-calc" => CssCalcFunctionKind.Calc,
        "min" => CssCalcFunctionKind.Min,
        "max" => CssCalcFunctionKind.Max,
        "clamp" => CssCalcFunctionKind.Clamp,
        "round" => CssCalcFunctionKind.Round,
        "mod" => CssCalcFunctionKind.Mod,
        "rem" => CssCalcFunctionKind.Rem,
        "sin" => CssCalcFunctionKind.Sin,
        "cos" => CssCalcFunctionKind.Cos,
        "tan" => CssCalcFunctionKind.Tan,
        "asin" => CssCalcFunctionKind.Asin,
        "acos" => CssCalcFunctionKind.Acos,
        "atan" => CssCalcFunctionKind.Atan,
        "atan2" => CssCalcFunctionKind.Atan2,
        "pow" => CssCalcFunctionKind.Pow,
        "sqrt" => CssCalcFunctionKind.Sqrt,
        "hypot" => CssCalcFunctionKind.Hypot,
        "log" => CssCalcFunctionKind.Log,
        "exp" => CssCalcFunctionKind.Exp,
        "abs" => CssCalcFunctionKind.Abs,
        "sign" => CssCalcFunctionKind.Sign,
        "random" => CssCalcFunctionKind.Random,
        "progress" => CssCalcFunctionKind.Progress,
        "calc-size" => CssCalcFunctionKind.CalcSize,
        "calc-mix" => CssCalcFunctionKind.CalcMix,
        _ => null,
    };

    /// <summary>Converts an absolute dimension (ASCII-lowercase unit) to its canonical unit (https://drafts.csswg.org/css-values-4/#canonical-unit).</summary>
    internal static bool TryConvertToCanonicalUnit(double value, string unit, out double result, out string canonicalUnit)
    {
        (result, canonicalUnit) = unit switch
        {
            "px" => (value, "px"),
            "cm" => (value * 96 / 2.54, "px"),
            "mm" => (value * 96 / 25.4, "px"),
            "q" => (value * 96 / 101.6, "px"),
            "in" => (value * 96, "px"),
            "pt" => (value * 4 / 3, "px"),
            "pc" => (value * 16, "px"),
            "deg" => (value, "deg"),
            "grad" => (value * 360 / 400, "deg"),
            "rad" => (value * 180 / Math.PI, "deg"),
            "turn" => (value * 360, "deg"),
            "s" => (value, "s"),
            "ms" => (value / 1000, "s"),
            "hz" => (value, "hz"),
            "khz" => (value * 1000, "hz"),
            "dppx" or "x" => (value, "dppx"),
            "dpi" => (value / 96, "dppx"),
            "dpcm" => (value * 2.54 / 96, "dppx"),
            "fr" => (value, "fr"),
            _ => (0d, ""),
        };

        return canonicalUnit.Length > 0;
    }

    /// <summary>The relative length units (https://drafts.csswg.org/css-values-4/#relative-lengths, https://drafts.csswg.org/css-conditional-5/#container-lengths).</summary>
    internal static bool IsRelativeUnit(string unit) => unit is
        "em" or "rem" or "ex" or "rex" or "cap" or "rcap" or "ch" or "rch" or "ic" or "ric" or "lh" or "rlh"
        or "vw" or "vh" or "vi" or "vb" or "vmin" or "vmax"
        or "svw" or "svh" or "svi" or "svb" or "svmin" or "svmax"
        or "lvw" or "lvh" or "lvi" or "lvb" or "lvmin" or "lvmax"
        or "dvw" or "dvh" or "dvi" or "dvb" or "dvmin" or "dvmax"
        or "cqw" or "cqh" or "cqi" or "cqb" or "cqmin" or "cqmax";

    /// <summary>Parses and simplifies a calculation tree (https://drafts.csswg.org/css-values-4/#parse-a-calculation).</summary>
    private readonly struct Evaluator
    {
        private readonly Func<double, string, (double Value, string Unit)?>? _resolveDimension;
        private readonly Func<string, double?>? _resolveTreeCountingFunction;
        private readonly Func<CssRandomSharing, double>? _resolveRandomBaseValue;
        private readonly bool _keepRelativeUnits;
        private readonly IReadOnlySet<string>? _channelKeywords;
        private readonly double? _sizeKeywordValue;
        private readonly CssAnchorFunctions _anchorFunctions;
        private readonly Func<CssFunction, double?>? _resolveAnchorFunction;
        private readonly Func<CssFunction, string>? _serializeAnchorFunction;

        // The base type percentages resolve to (CssCalcType.NoHint when a percentage is its own type)
        private readonly sbyte _percentHint;
        private readonly string _percentageTypeUnit;
        private readonly bool _hasBasis;
        private readonly double _basisValue;
        private readonly string? _basisUnit;

        public Evaluator(CssCalcContext context)
        {
            _resolveDimension = context.ResolveDimension;
            _resolveTreeCountingFunction = context.ResolveTreeCountingFunction;
            _resolveRandomBaseValue = context.ResolveRandomBaseValue;
            _keepRelativeUnits = context.KeepRelativeUnits;
            _channelKeywords = context.ChannelKeywords;
            _sizeKeywordValue = context.SizeKeywordValue;
            _anchorFunctions = context.KeepRelativeUnits ? CssAnchorFunctions.Anchor | CssAnchorFunctions.AnchorSize : context.AnchorFunctions;
            _resolveAnchorFunction = context.ResolveAnchorFunction;
            _serializeAnchorFunction = context.SerializeAnchorFunction;
            _percentHint = CssCalcType.NoHint;
            _percentageTypeUnit = "%";
            if (context.PercentageUnit is { } percentageUnit)
            {
                var unit = InfraAscii.ToAsciiLowercase(percentageUnit);
                if (unit.Length == 0)
                {
                    _percentHint = CssCalcType.NumberHint;
                    _percentageTypeUnit = "";
                }
                else if (TryConvertToCanonicalUnit(0, unit, out _, out var canonicalUnit))
                {
                    _percentHint = (sbyte)CssCalcType.GetIndex(canonicalUnit);
                    _percentageTypeUnit = canonicalUnit;
                }
            }

            if (context.PercentageBasis is { } basis)
            {
                _hasBasis = true;
                var basisUnit = InfraAscii.ToAsciiLowercase(basis.Unit);
                if (basisUnit.Length == 0)
                {
                    _basisValue = basis.Value;
                    _basisUnit = "";
                }
                else if (ResolveDimension(basis.Value, basisUnit, out var value, out var unit))
                {
                    _basisValue = value;
                    _basisUnit = unit;
                }
            }
        }

        /// <summary>Whether the size keyword of calc-size() is allowed (https://drafts.csswg.org/css-values-5/#valdef-calc-size-size).</summary>
        private bool AllowsSizeKeyword { get; init; }

        /// <summary>True when percentages stand for another type and are not resolved: they block the simplification of the functions.</summary>
        public bool PercentagesAreUnresolved => !_hasBasis && _percentHint != CssCalcType.NoHint;

        public CssCalcNode? ParseFunction(CssFunction function, CssCalcFunctionKind kind, int depth)
        {
            if (depth > MaxDepth)
                return null;

            var values = function.Value;
            if (kind == CssCalcFunctionKind.Calc)
                return ParseSum(values, 0, values.Count, depth + 1);

            if (kind == CssCalcFunctionKind.Random)
                return ParseRandom(values, depth);

            if (kind == CssCalcFunctionKind.CalcSize)
                return ParseCalcSize(values, depth);

            if (kind == CssCalcFunctionKind.CalcMix)
                return ParseCalcMix(values, depth);

            // https://drafts.csswg.org/css-values-5/#progress: progress( [ no-clamp ]? <calc-sum>, <calc-sum>, <calc-sum> )
            var noClamp = false;
            if (kind == CssCalcFunctionKind.Progress)
            {
                var first = SkipWhitespace(values, 0, values.Count);
                if (first < values.Count && values[first] is CssPreservedToken noClampKeyword && noClampKeyword.Token.IsIdent("no-clamp"))
                {
                    noClamp = true;
                    values = values[(first + 1)..];
                }
            }

            var arguments = new List<CssCalcNode?>(4);
            var strategy = CssCalcRoundingStrategy.Nearest;
            var start = 0;
            var index = 0;
            while (true)
            {
                var end = start;
                while (end < values.Count && values[end] is not CssPreservedToken { Type: CssTokenType.Comma })
                {
                    end++;
                }

                if (index == 0 && kind == CssCalcFunctionKind.Round && end < values.Count && GetRoundingStrategy(GetSingleIdent(values, start, end)) is { } parsedStrategy)
                {
                    strategy = parsedStrategy;
                }
                else if (kind == CssCalcFunctionKind.Clamp && index is 0 or 2 && GetSingleIdent(values, start, end) is { } ident && InfraAscii.EqualsIgnoreAsciiCase(ident, "none"))
                {
                    arguments.Add(null);
                }
                else
                {
                    var argument = ParseSum(values, start, end, depth + 1);
                    if (argument is null)
                        return null;

                    arguments.Add(argument);
                }

                index++;
                if (end == values.Count)
                    break;

                start = end + 1;
            }

            if (!IsValidArgumentCount(kind, arguments.Count))
                return null;

            return CreateFunction(kind, strategy, arguments, noClamp);
        }

        /// <summary>
        /// https://drafts.csswg.org/css-values-5/#random: random( &lt;random-value-sharing&gt;? , &lt;calc-sum&gt;, &lt;calc-sum&gt;, &lt;calc-sum&gt;? ),
        /// where &lt;random-value-sharing&gt; is a caching key or fixed &lt;number [0,1]&gt;. The function is resolved when its
        /// arguments and its random base value are known; when only the base value is known, it becomes fixed.
        /// </summary>
        private CssCalcNode? ParseRandom(List<CssComponentValue> values, int depth)
        {
            var arguments = ValueMatcher.SplitAtCommas(values);
            CssRandomSharing? sharing = null;
            CssCalcNode? fixedValue = null;
            var fixedIsMathFunction = false;
            var first = ValueMatcher.RemoveWhitespace(arguments[0]);
            if (CssRandom.IsSharingArgument(first))
            {
                if (first[0] is CssPreservedToken fixedKeyword && fixedKeyword.Token.IsIdent("fixed"))
                {
                    if (first.Count != 2)
                        return null;

                    var index = 1;
                    fixedValue = ParseValue(first, ref index, depth + 1);
                    if (fixedValue is null || GetUnitOfType(fixedValue.Type) is not "")
                        return null;

                    // A literal number must be in the range [0,1]; a math function is clamped (the base value is less than 1)
                    if (first[1] is CssPreservedToken { Type: CssTokenType.Number } number && number.Token.NumericValue is < 0 or > 1)
                        return null;

                    fixedIsMathFunction = first[1] is CssFunction;
                }
                else
                {
                    sharing = CssRandom.ParseSharing(first);
                    if (sharing is null)
                        return null;
                }

                arguments.RemoveAt(0);
            }

            if (arguments.Count is not (2 or 3))
                return null;

            var nodes = new List<CssCalcNode?>(arguments.Count);
            foreach (var argument in arguments)
            {
                var node = ParseSum(argument, 0, argument.Count, depth + 1);
                if (node is null)
                    return null;

                nodes.Add(node);
            }

            var type = nodes[0]!.Type;
            for (var i = 1; i < nodes.Count; i++)
            {
                if (!TryAdd(type, nodes[i]!.Type, out type))
                    return null;
            }

            double? baseValue = null;
            if (fixedValue is not null)
            {
                if (IsResolvable(fixedValue))
                {
                    var value = ((CssCalcNumericNode)fixedValue).Value;
                    baseValue = double.IsNaN(value) ? 0 : Math.Clamp(value, 0, Math.BitDecrement(1d));
                }
            }
            else if (_resolveRandomBaseValue is not null)
            {
                baseValue = _resolveRandomBaseValue(sharing ?? CssRandomSharing.Auto);
            }

            // In a specified value, the percentages are not resolved (their basis is not known)
            if (baseValue is { } resolvedBase && nodes.TrueForAll(IsResolvable) && !(_keepRelativeUnits && nodes.Exists(node => node is CssCalcNumericNode { Unit: "%" })))
            {
                var min = (CssCalcNumericNode)nodes[0]!;
                var max = ((CssCalcNumericNode)nodes[1]!).Value;
                var step = nodes.Count == 3 ? ((CssCalcNumericNode)nodes[2]!).Value : (double?)null;
                return new CssCalcNumericNode(ComputeRandom(resolvedBase, min.Value, max, step), min.Unit);
            }

            // The base value is known, but not the arguments (e.g. percentages): the computed value is fixed
            if (baseValue is { } knownBase && fixedValue is null)
            {
                fixedValue = new CssCalcNumericNode(knownBase, "");
                sharing = null;
            }

            return new CssCalcFunctionNode(CssCalcFunctionKind.Random, CssCalcRoundingStrategy.Nearest, nodes, type)
            {
                Sharing = sharing,
                FixedBaseValue = fixedValue,
                FixedIsMathFunction = fixedIsMathFunction,
            };
        }

        /// <summary>
        /// https://drafts.csswg.org/css-values-5/#calc-size: calc-size( &lt;calc-size-basis&gt;, &lt;calc-sum&gt; ), where the basis is a
        /// keyword (validated against the property by the grammar), any, a calc-size() or a &lt;calc-sum&gt; length, and the
        /// calculation is a length that can use the size keyword (unless the basis is any). It is resolved at used time.
        /// </summary>
        private CssCalcFunctionNode? ParseCalcSize(List<CssComponentValue> values, int depth)
        {
            var arguments = ValueMatcher.SplitAtCommas(values);
            if (arguments.Count != 2)
                return null;

            var basis = ValueMatcher.RemoveWhitespace(arguments[0]);
            string? basisKeyword = null;
            CssCalcNode? basisNode = null;
            switch (basis)
            {
                case [CssPreservedToken { Type: CssTokenType.Ident } keyword]:
                    basisKeyword = InfraAscii.ToAsciiLowercase(keyword.Token.Value);
                    if (basisKeyword is "size" or "e" or "pi" or "infinity" or "-infinity" or "nan")
                        return null;

                    break;

                case [CssFunction nested] when GetKind(nested.Name) == CssCalcFunctionKind.CalcSize:
                    basisNode = ParseCalcSize(nested.Value, depth + 1);
                    if (basisNode is null)
                        return null;

                    break;

                default:
                    basisNode = ParseSum(arguments[0], 0, arguments[0].Count, depth + 1);
                    if (basisNode is null || !IsLengthPercentage(basisNode.Type))
                        return null;

                    break;
            }

            var evaluator = this with { AllowsSizeKeyword = basisKeyword != "any" };
            var calculation = evaluator.ParseSum(arguments[1], 0, arguments[1].Count, depth + 1);
            if (calculation is null || !IsLengthPercentage(calculation.Type))
                return null;

            return new CssCalcFunctionNode(CssCalcFunctionKind.CalcSize, CssCalcRoundingStrategy.Nearest, [basisNode, calculation], CssCalcType.FromUnit("px")) { BasisKeyword = basisKeyword };
        }

        /// <summary>
        /// https://drafts.csswg.org/css-values-5/#calc-mix: calc-mix( [ &lt;calc-sum&gt; &lt;percentage [0,100]&gt;? ]# ), the weighted sum
        /// of the values. The weights are normalized: when they add up to more than 100%, they are scaled down to 100% (the
        /// missing weights are 0%); otherwise the missing weights share the remaining percentage. The values whose weight is 0% are
        /// removed, and the result is a zero of the type of the first value when there is none left.
        /// </summary>
        private CssCalcNode? ParseCalcMix(List<CssComponentValue> values, int depth)
        {
            var arguments = ValueMatcher.SplitAtCommas(values);
            var nodes = new List<CssCalcNode>(arguments.Count);
            var weights = new List<double?>(arguments.Count);
            foreach (var argument in arguments)
            {
                var end = argument.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
                if (end < 0)
                    return null;

                double? weight = null;
                if (argument[end] is CssPreservedToken { Type: CssTokenType.Percentage } percentage && ValueMatcher.RemoveWhitespace(argument).Count > 1)
                {
                    if (percentage.Token.NumericValue is < 0 or > 100)
                        return null;

                    weight = percentage.Token.NumericValue;
                }
                else
                {
                    end++;
                }

                var node = ParseSum(argument, 0, end, depth + 1);
                if (node is null)
                    return null;

                nodes.Add(node);
                weights.Add(weight);
            }

            var type = nodes[0].Type;
            for (var i = 1; i < nodes.Count; i++)
            {
                if (!TryAdd(type, nodes[i].Type, out type))
                    return null;
            }

            if (GetUnitOfType(type) is not { } typeUnit)
                return null;

            // The weights are normalized
            var specified = weights.Sum(weight => weight ?? 0);
            var missing = weights.Count(weight => weight is null);
            var normalized = weights.Select(weight => specified > 100 ? (weight ?? 0) * 100 / specified : weight ?? (100 - specified) / missing).ToList();

            var items = new List<CssCalcNode?>();
            var itemWeights = new List<double>();
            for (var i = 0; i < nodes.Count; i++)
            {
                if (normalized[i] > 0)
                {
                    items.Add(nodes[i]);
                    itemWeights.Add(normalized[i]);
                }
            }

            if (items.Count == 0)
                return new CssCalcNumericNode(0, nodes[0] is CssCalcNumericNode first ? first.Unit : typeUnit);

            if (items.TrueForAll(IsResolvable) && items.Select(item => ((CssCalcNumericNode)item!).Unit).Distinct(StringComparer.Ordinal).Count() == 1)
            {
                var sum = 0d;
                for (var i = 0; i < items.Count; i++)
                {
                    sum += ((CssCalcNumericNode)items[i]!).Value * itemWeights[i] / 100;
                }

                return new CssCalcNumericNode(sum, ((CssCalcNumericNode)items[0]!).Unit);
            }

            return new CssCalcFunctionNode(CssCalcFunctionKind.CalcMix, CssCalcRoundingStrategy.Nearest, items, type) { Weights = itemWeights };
        }

        private bool IsLengthPercentage(CssCalcType type) => GetUnitOfType(type) is "px" || (GetUnitOfType(type) is "%" && _percentHint == CssCalcType.NoHint);

        /// <summary>
        /// https://drafts.csswg.org/css-values-5/#random-evaluation: a value between min and max (min when max is less than min),
        /// or one of the steps from min when the step is positive; NaN when an argument is NaN or the range is infinite.
        /// </summary>
        private static double ComputeRandom(double baseValue, double min, double max, double? step)
        {
            if (double.IsNaN(min) || double.IsNaN(max) || (step is { } nanStep && double.IsNaN(nanStep)))
                return double.NaN;

            if (max <= min)
                return min;

            if (double.IsInfinity(min) || double.IsInfinity(max))
                return double.NaN;

            // A negative step is ignored
            if (step is { } s && s > 0)
            {
                if (double.IsInfinity(s))
                    return min;

                var count = Math.Floor((max - min) / s);
                return min + (Math.Min(Math.Floor(baseValue * (count + 1)), count) * s);
            }

            return min + ((max - min) * baseValue);
        }

        private static bool IsValidArgumentCount(CssCalcFunctionKind kind, int count) => kind switch
        {
            CssCalcFunctionKind.Min or CssCalcFunctionKind.Max or CssCalcFunctionKind.Hypot => count >= 1,
            CssCalcFunctionKind.Clamp or CssCalcFunctionKind.Progress => count == 3,
            CssCalcFunctionKind.Round or CssCalcFunctionKind.Log => count is 1 or 2,
            CssCalcFunctionKind.Mod or CssCalcFunctionKind.Rem or CssCalcFunctionKind.Atan2 or CssCalcFunctionKind.Pow => count == 2,
            _ => count == 1,
        };

        private static CssCalcRoundingStrategy? GetRoundingStrategy(string? name) => name is null ? null : InfraAscii.ToAsciiLowercase(name) switch
        {
            "nearest" => CssCalcRoundingStrategy.Nearest,
            "up" => CssCalcRoundingStrategy.Up,
            "down" => CssCalcRoundingStrategy.Down,
            "to-zero" => CssCalcRoundingStrategy.ToZero,
            _ => null,
        };

        private static string? GetSingleIdent(List<CssComponentValue> values, int start, int end)
        {
            start = SkipWhitespace(values, start, end);
            if (start == end || values[start] is not CssPreservedToken { Type: CssTokenType.Ident } ident)
                return null;

            return SkipWhitespace(values, start + 1, end) == end ? ident.Token.Value : null;
        }

        private static int SkipWhitespace(List<CssComponentValue> values, int index, int end)
        {
            while (index < end && values[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                index++;
            }

            return index;
        }

        private static bool IsDelim(CssComponentValue value, char c) => value is CssPreservedToken token && token.Token.IsDelim(c);

        /// <summary>&lt;calc-sum&gt; = &lt;calc-product&gt; [ [ '+' | '-' ] &lt;calc-product&gt; ]*, with whitespace required on both sides of + and -.</summary>
        private CssCalcNode? ParseSum(List<CssComponentValue> values, int start, int end, int depth)
        {
            if (depth > MaxDepth)
                return null;

            var index = SkipWhitespace(values, start, end);
            if (index == end)
                return null;

            var first = ParseProduct(values, ref index, end, depth);
            if (first is null)
                return null;

            List<CssCalcNode>? terms = null;
            while (true)
            {
                var next = SkipWhitespace(values, index, end);
                if (next == end)
                    break;

                // The operator must be preceded and followed by whitespace
                if (next == index)
                    return null;

                var isMinus = IsDelim(values[next], '-');
                if (!isMinus && !IsDelim(values[next], '+'))
                    return null;

                index = next + 1;
                if (index == end || values[index] is not CssPreservedToken { Type: CssTokenType.Whitespace })
                    return null;

                index = SkipWhitespace(values, index, end);
                if (index == end)
                    return null;

                var term = ParseProduct(values, ref index, end, depth);
                if (term is null)
                    return null;

                terms ??= [first];
                terms.Add(isMinus ? Negate(term) : term);
            }

            return terms is null ? first : CreateSum(terms);
        }

        /// <summary>&lt;calc-product&gt; = &lt;calc-value&gt; [ [ '*' | / ] &lt;calc-value&gt; ]*</summary>
        private CssCalcNode? ParseProduct(List<CssComponentValue> values, ref int index, int end, int depth)
        {
            var first = ParseValue(values, ref index, depth);
            if (first is null)
                return null;

            List<CssCalcNode>? factors = null;
            while (true)
            {
                var next = SkipWhitespace(values, index, end);
                if (next == end)
                    break;

                var isDivision = IsDelim(values[next], '/');
                if (!isDivision && !IsDelim(values[next], '*'))
                    break;

                next = SkipWhitespace(values, next + 1, end);
                if (next == end)
                    return null;

                var factor = ParseValue(values, ref next, depth);
                if (factor is null)
                    return null;

                if (isDivision)
                {
                    factor = Invert(factor);
                }

                factors ??= [first];
                factors.Add(factor);
                index = next;
            }

            return factors is null ? first : CreateProduct(factors);
        }

        /// <summary>&lt;calc-value&gt; = &lt;number&gt; | &lt;dimension&gt; | &lt;percentage&gt; | &lt;calc-keyword&gt; | ( &lt;calc-sum&gt; ), or a nested math function.</summary>
        private CssCalcNode? ParseValue(List<CssComponentValue> values, ref int index, int depth)
        {
            var value = values[index];
            index++;
            switch (value)
            {
                case CssPreservedToken { Token: var token }:
                    switch (token.Type)
                    {
                        case CssTokenType.Number:
                            return new CssCalcNumericNode(token.NumericValue, "");

                        case CssTokenType.Percentage:
                            if (_hasBasis)
                                return _basisUnit is null ? null : new CssCalcNumericNode(token.NumericValue * _basisValue / 100, _basisUnit);

                            return new CssCalcNumericNode(token.NumericValue, "%");

                        case CssTokenType.Dimension:
                            return ResolveDimension(token.NumericValue, InfraAscii.ToAsciiLowercase(token.Value), out var resolvedValue, out var resolvedUnit)
                                ? new CssCalcNumericNode(resolvedValue, resolvedUnit)
                                : null;

                        case CssTokenType.Ident when _channelKeywords is not null && _channelKeywords.Contains(InfraAscii.ToAsciiLowercase(token.Value)):
                            return new CssCalcKeywordNode(InfraAscii.ToAsciiLowercase(token.Value));

                        // The size of the basis of calc-size(), a length
                        case CssTokenType.Ident when _sizeKeywordValue is { } sizeValue && token.IsIdent("size"):
                            return new CssCalcNumericNode(sizeValue, "px");

                        case CssTokenType.Ident when AllowsSizeKeyword && token.IsIdent("size"):
                            return new CssCalcKeywordNode("size", CssCalcType.FromUnit("px"));

                        case CssTokenType.Ident:
                            // https://drafts.csswg.org/css-values-4/#calc-constants
                            double? constant = InfraAscii.ToAsciiLowercase(token.Value) switch
                            {
                                "e" => Math.E,
                                "pi" => Math.PI,
                                "infinity" => double.PositiveInfinity,
                                "-infinity" => double.NegativeInfinity,
                                "nan" => double.NaN,
                                _ => null,
                            };

                            return constant is null ? null : new CssCalcNumericNode(constant.Value, "");

                        default:
                            return null;
                    }

                case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
                    return ParseSum(block.Value, 0, block.Value.Count, depth + 1);

                // calc-size() cannot be nested in a calculation
                case CssFunction function when GetKind(function.Name) is { } kind:
                    return kind == CssCalcFunctionKind.CalcSize ? null : ParseFunction(function, kind, depth + 1);

                // https://drafts.csswg.org/css-anchor-position-1/#anchor-pos: anchor() and anchor-size() resolve to a <length>
                case CssFunction function when GetAnchorFunction(function.Name) is { } anchorFunction:
                    if (_resolveAnchorFunction is not null)
                        return _resolveAnchorFunction(function) is { } anchorValue ? new CssCalcNumericNode(anchorValue, "px") : null;

                    return (_anchorFunctions & anchorFunction) != CssAnchorFunctions.None && CssAnchorFunction.IsValid(function, _anchorFunctions) ? new CssCalcKeywordNode(_serializeAnchorFunction?.Invoke(function) ?? CssAnchorFunction.Serialize(function), CssCalcType.FromUnit("px")) : null;

                // https://drafts.csswg.org/css-values-5/#tree-counting: <integer>
                case CssFunction function when InfraAscii.ToAsciiLowercase(function.Name) is "sibling-index" or "sibling-count" && function.Value.All(v => v is CssPreservedToken { Type: CssTokenType.Whitespace }):
                {
                    // In a specified value, the function is kept (e.g. calc(2 * 1turn * sibling-index()) is calc(720deg * sibling-index()))
                    var name = InfraAscii.ToAsciiLowercase(function.Name);
                    if (_resolveTreeCountingFunction is null && _keepRelativeUnits)
                        return new CssCalcKeywordNode(name + "()");

                    return _resolveTreeCountingFunction?.Invoke(name) is { } count ? new CssCalcNumericNode(count, "") : null;
                }

                default:
                    return null;
            }
        }

        private bool ResolveDimension(double value, string unit, out double result, out string canonicalUnit)
        {
            if (TryConvertToCanonicalUnit(value, unit, out result, out canonicalUnit))
                return true;

            if (_keepRelativeUnits && IsRelativeUnit(unit))
            {
                result = value;
                canonicalUnit = unit;
                return true;
            }

            if (_resolveDimension is not null && IsRelativeUnit(unit) && _resolveDimension(value, unit) is { } resolved)
                return TryConvertToCanonicalUnit(resolved.Value, InfraAscii.ToAsciiLowercase(resolved.Unit), out result, out canonicalUnit);

            return false;
        }

        /// <summary>Whether the numeric value of the node is known, so it can be used to compute a function.</summary>
        private bool IsResolvable(CssCalcNode? node) => node is CssCalcNumericNode numeric && (numeric.Unit != "%" || _percentHint == CssCalcType.NoHint) && !IsRelativeUnit(numeric.Unit);

        // Simplification: https://drafts.csswg.org/css-values-4/#calc-simplification
        private static CssCalcNode Negate(CssCalcNode node)
        {
            switch (node)
            {
                case CssCalcNumericNode numeric:
                    return new CssCalcNumericNode(-numeric.Value, numeric.Unit);

                case CssCalcNegateNode negate:
                    return negate.Child;

                case CssCalcSumNode sum when AllNumeric(sum.Children):
                {
                    var children = new List<CssCalcNode>(sum.Children.Count);
                    foreach (var child in sum.Children)
                    {
                        var numeric = (CssCalcNumericNode)child;
                        children.Add(new CssCalcNumericNode(-numeric.Value, numeric.Unit));
                    }

                    return new CssCalcSumNode(children, sum.Type);
                }

                default:
                    return new CssCalcNegateNode(node);
            }
        }

        private static CssCalcNode Invert(CssCalcNode node)
        {
            if (node is CssCalcNumericNode { Unit: "" } number)
                return new CssCalcNumericNode(1 / number.Value, "");

            if (node is CssCalcInvertNode invert)
                return invert.Child;

            return new CssCalcInvertNode(node);
        }

        private static bool AllNumeric(List<CssCalcNode> nodes)
        {
            foreach (var node in nodes)
            {
                if (node is not CssCalcNumericNode)
                    return false;
            }

            return true;
        }

        private CssCalcNode? CreateSum(List<CssCalcNode> terms)
        {
            var type = terms[0].Type;
            for (var i = 1; i < terms.Count; i++)
            {
                if (!TryAdd(type, terms[i].Type, out type))
                    return null;
            }

            var children = new List<CssCalcNode>(terms.Count);
            foreach (var term in terms)
            {
                if (term is CssCalcSumNode sum)
                {
                    foreach (var child in sum.Children)
                    {
                        AddTerm(children, child);
                    }
                }
                else
                {
                    AddTerm(children, term);
                }
            }

            return children.Count == 1 ? children[0] : new CssCalcSumNode(children, type);

            static void AddTerm(List<CssCalcNode> children, CssCalcNode term)
            {
                if (term is CssCalcNumericNode numeric)
                {
                    for (var i = 0; i < children.Count; i++)
                    {
                        if (children[i] is CssCalcNumericNode existing && string.Equals(existing.Unit, numeric.Unit, StringComparison.Ordinal))
                        {
                            children[i] = new CssCalcNumericNode(existing.Value + numeric.Value, numeric.Unit);
                            return;
                        }
                    }
                }

                children.Add(term);
            }
        }

        private static CssCalcNode? CreateProduct(List<CssCalcNode> factors)
        {
            var type = factors[0].Type;
            for (var i = 1; i < factors.Count; i++)
            {
                if (!CssCalcType.TryMultiply(type, factors[i].Type, out type))
                    return null;
            }

            // Flatten the nested products and multiply the numbers together
            var children = new List<CssCalcNode>(factors.Count);
            var numberIndex = -1;
            foreach (var factor in factors)
            {
                if (factor is CssCalcProductNode product)
                {
                    foreach (var child in product.Children)
                    {
                        AddFactor(children, child, ref numberIndex);
                    }
                }
                else
                {
                    AddFactor(children, factor, ref numberIndex);
                }
            }

            if (children.Count == 1)
                return children[0];

            // A number multiplied by a sum of numeric values: distribute the multiplication
            if (children.Count == 2 && numberIndex >= 0 && children[1 - numberIndex] is CssCalcSumNode sum && AllNumeric(sum.Children))
            {
                var factor = ((CssCalcNumericNode)children[numberIndex]).Value;
                var terms = new List<CssCalcNode>(sum.Children.Count);
                foreach (var child in sum.Children)
                {
                    var numeric = (CssCalcNumericNode)child;
                    terms.Add(new CssCalcNumericNode(numeric.Value * factor, numeric.Unit));
                }

                return new CssCalcSumNode(terms, type);
            }

            // Numeric values multiplied by other nodes (e.g. 2 * 1rad * pi * sibling-index()): the numeric values are multiplied
            // together when their product can be expressed with a single unit (360deg * sibling-index())
            var numericCount = children.Count(child => child is CssCalcNumericNode);
            if (numericCount >= 2 && numericCount < children.Count)
            {
                var numericProduct = CreateProduct([.. children.Where(child => child is CssCalcNumericNode)]);
                if (numericProduct is CssCalcNumericNode)
                    return new CssCalcProductNode([numericProduct, .. children.Where(child => child is not CssCalcNumericNode)], type);
            }

            // Only numeric values (possibly inverted): multiply them when the units of the result can be expressed as a single unit
            var exponents = default(CssCalcTypeExponents);
            var value = 1d;
            var dimensionCount = 0;
            string? relativeUnit = null;
            foreach (var child in children)
            {
                CssCalcNumericNode numeric;
                int exponent;
                if (child is CssCalcNumericNode n)
                {
                    numeric = n;
                    exponent = 1;
                    value *= numeric.Value;
                }
                else if (child is CssCalcInvertNode { Child: CssCalcNumericNode inverted })
                {
                    numeric = inverted;
                    exponent = -1;
                    value /= numeric.Value;
                }
                else
                {
                    return new CssCalcProductNode(children, type);
                }

                var index = CssCalcType.GetIndex(numeric.Unit);
                if (index >= 0)
                {
                    exponents[index] += exponent;
                    dimensionCount++;
                    if (IsRelativeUnit(numeric.Unit))
                    {
                        // A relative unit can only be kept when it is the only dimension (e.g. 2em * 3 is 6em, but not 2em * 1px / 1px)
                        if (exponent != 1 || relativeUnit is not null)
                            return new CssCalcProductNode(children, type);

                        relativeUnit = numeric.Unit;
                    }
                }
            }

            if (relativeUnit is not null && dimensionCount > 1)
                return new CssCalcProductNode(children, type);


            var unitIndex = -1;
            for (var i = 0; i < CssCalcType.BaseTypeCount; i++)
            {
                if (exponents[i] == 0)
                    continue;

                if (exponents[i] != 1 || unitIndex >= 0)
                    return new CssCalcProductNode(children, type);

                unitIndex = i;
            }

            return new CssCalcNumericNode(value, relativeUnit ?? (unitIndex < 0 ? "" : CssCalcType.CanonicalUnits[unitIndex]));

            static void AddFactor(List<CssCalcNode> children, CssCalcNode factor, ref int numberIndex)
            {
                if (factor is CssCalcNumericNode { Unit: "" } number)
                {
                    if (numberIndex >= 0)
                    {
                        children[numberIndex] = new CssCalcNumericNode(((CssCalcNumericNode)children[numberIndex]).Value * number.Value, "");
                        return;
                    }

                    numberIndex = children.Count;
                }

                children.Add(factor);
            }
        }

        private CssCalcNode? CreateFunction(CssCalcFunctionKind kind, CssCalcRoundingStrategy strategy, List<CssCalcNode?> arguments, bool noClamp = false)
        {
            // clamp() with none is min() or max() (https://drafts.csswg.org/css-values-4/#calc-simplification)
            if (kind == CssCalcFunctionKind.Clamp && (arguments[0] is null || arguments[2] is null))
            {
                if (arguments[0] is null && arguments[2] is null)
                    return arguments[1];

                return arguments[0] is null
                    ? CreateFunction(CssCalcFunctionKind.Min, strategy, [arguments[1], arguments[2]])
                    : CreateFunction(CssCalcFunctionKind.Max, strategy, [arguments[0], arguments[1]]);
            }

            // Type checking: https://drafts.csswg.org/css-values-4/#calc-type-checking
            CssCalcType type;
            switch (kind)
            {
                case CssCalcFunctionKind.Min or CssCalcFunctionKind.Max or CssCalcFunctionKind.Clamp or CssCalcFunctionKind.Hypot
                    or CssCalcFunctionKind.Round or CssCalcFunctionKind.Mod or CssCalcFunctionKind.Rem or CssCalcFunctionKind.Atan2:
                {
                    if (kind == CssCalcFunctionKind.Round && arguments.Count == 1)
                    {
                        // B can be omitted only when A is a number
                        if (GetUnitOfType(arguments[0]!.Type) is not "")
                            return null;

                        arguments.Add(new CssCalcNumericNode(1, ""));
                    }

                    type = default;
                    var first = true;
                    foreach (var argument in arguments)
                    {
                        if (argument is null)
                            continue;

                        if (first)
                        {
                            type = argument.Type;
                            first = false;
                        }
                        else if (!TryAdd(type, argument.Type, out type))
                        {
                            return null;
                        }
                    }

                    if (kind == CssCalcFunctionKind.Atan2)
                    {
                        type = CssCalcType.FromUnit("deg");
                    }

                    break;
                }

                case CssCalcFunctionKind.Sin or CssCalcFunctionKind.Cos or CssCalcFunctionKind.Tan:
                    if (GetUnitOfType(arguments[0]!.Type) is not ("" or "deg"))
                        return null;

                    type = default;
                    break;

                case CssCalcFunctionKind.Asin or CssCalcFunctionKind.Acos or CssCalcFunctionKind.Atan:
                    if (GetUnitOfType(arguments[0]!.Type) is not "")
                        return null;

                    type = CssCalcType.FromUnit("deg");
                    break;

                case CssCalcFunctionKind.Pow or CssCalcFunctionKind.Sqrt or CssCalcFunctionKind.Log or CssCalcFunctionKind.Exp:
                    foreach (var argument in arguments)
                    {
                        if (GetUnitOfType(argument!.Type) is not "")
                            return null;
                    }

                    type = default;
                    break;

                case CssCalcFunctionKind.Abs:
                    type = arguments[0]!.Type;
                    break;

                case CssCalcFunctionKind.Progress:
                {
                    // https://drafts.csswg.org/css-values-5/#progress: the arguments have a consistent type, the result is a number
                    type = arguments[0]!.Type;
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        if (!TryAdd(type, arguments[i]!.Type, out type))
                            return null;
                    }

                    if (GetUnitOfType(type) is null)
                        return null;

                    type = default;
                    break;
                }

                default: // Sign
                    if (GetUnitOfType(arguments[0]!.Type) is null)
                        return null;

                    type = default;
                    break;
            }

            var allResolvable = true;
            foreach (var argument in arguments)
            {
                if (argument is not null && !IsResolvable(argument))
                {
                    allResolvable = false;
                    break;
                }
            }

            if (allResolvable)
                return Compute(kind, strategy, arguments, noClamp);

            if (kind is CssCalcFunctionKind.Min or CssCalcFunctionKind.Max)
            {
                // Partial simplification: combine the arguments that can be compared
                var children = new List<CssCalcNode?>(arguments.Count);
                foreach (var argument in arguments)
                {
                    if (IsResolvable(argument))
                    {
                        var numeric = (CssCalcNumericNode)argument!;
                        var combined = false;
                        for (var i = 0; i < children.Count; i++)
                        {
                            if (IsResolvable(children[i]) && children[i] is CssCalcNumericNode existing && string.Equals(existing.Unit, numeric.Unit, StringComparison.Ordinal))
                            {
                                var value = kind == CssCalcFunctionKind.Min ? Math.Min(existing.Value, numeric.Value) : Math.Max(existing.Value, numeric.Value);
                                children[i] = new CssCalcNumericNode(value, numeric.Unit);
                                combined = true;
                                break;
                            }
                        }

                        if (combined)
                            continue;
                    }

                    children.Add(argument);
                }

                // A single argument is the result (min(1% + 1px) is calc(1% + 1px))
                if (children is [{ } single])
                    return single;

                arguments = children;
            }

            return new CssCalcFunctionNode(kind, strategy, arguments, type) { NoClamp = noClamp };
        }

        private static CssCalcNumericNode Compute(CssCalcFunctionKind kind, CssCalcRoundingStrategy strategy, List<CssCalcNode?> arguments, bool noClamp)
        {
            var a = arguments[kind == CssCalcFunctionKind.Clamp ? 1 : 0] as CssCalcNumericNode;
            var unit = a!.Unit;
            switch (kind)
            {
                case CssCalcFunctionKind.Min or CssCalcFunctionKind.Max:
                {
                    var value = a.Value;
                    for (var i = 1; i < arguments.Count; i++)
                    {
                        var other = ((CssCalcNumericNode)arguments[i]!).Value;
                        value = kind == CssCalcFunctionKind.Min ? Math.Min(value, other) : Math.Max(value, other);
                    }

                    return new CssCalcNumericNode(value, unit);
                }

                case CssCalcFunctionKind.Clamp:
                {
                    // clamp(MIN, VAL, MAX) = max(MIN, min(VAL, MAX)): MIN wins when MAX is less than MIN
                    var value = a.Value;
                    if (arguments[2] is CssCalcNumericNode max)
                    {
                        value = Math.Min(value, max.Value);
                    }

                    if (arguments[0] is CssCalcNumericNode min)
                    {
                        value = Math.Max(min.Value, value);
                    }

                    return new CssCalcNumericNode(value, unit);
                }

                case CssCalcFunctionKind.Hypot:
                {
                    // NaN wins over infinity (https://drafts.csswg.org/css-values-4/#calc-ieee)
                    var sum = 0d;
                    var infinite = false;
                    var nan = false;
                    foreach (var argument in arguments)
                    {
                        var value = ((CssCalcNumericNode)argument!).Value;
                        infinite |= double.IsInfinity(value);
                        nan |= double.IsNaN(value);
                        sum += value * value;
                    }

                    return new CssCalcNumericNode(nan ? double.NaN : infinite ? double.PositiveInfinity : Math.Sqrt(sum), unit);
                }

                case CssCalcFunctionKind.Round:
                    return new CssCalcNumericNode(Round(strategy, a.Value, ((CssCalcNumericNode)arguments[1]!).Value), unit);

                case CssCalcFunctionKind.Mod or CssCalcFunctionKind.Rem:
                    return new CssCalcNumericNode(Modulo(a.Value, ((CssCalcNumericNode)arguments[1]!).Value, kind == CssCalcFunctionKind.Mod), unit);

                case CssCalcFunctionKind.Sin or CssCalcFunctionKind.Cos or CssCalcFunctionKind.Tan:
                    return new CssCalcNumericNode(Trigonometric(kind, a.Value, isDegrees: unit == "deg"), "");

                case CssCalcFunctionKind.Asin:
                    return new CssCalcNumericNode(Math.Asin(a.Value) * 180 / Math.PI, "deg");

                case CssCalcFunctionKind.Acos:
                    return new CssCalcNumericNode(Math.Acos(a.Value) * 180 / Math.PI, "deg");

                case CssCalcFunctionKind.Atan:
                    return new CssCalcNumericNode(Math.Atan(a.Value) * 180 / Math.PI, "deg");

                case CssCalcFunctionKind.Atan2:
                    return new CssCalcNumericNode(Math.Atan2(a.Value, ((CssCalcNumericNode)arguments[1]!).Value) * 180 / Math.PI, "deg");

                case CssCalcFunctionKind.Pow:
                {
                    // Like JavaScript (https://drafts.csswg.org/css-values-4/#exponent-funcs): a NaN argument and ±1 raised to an infinite
                    // power are NaN
                    var exponent = ((CssCalcNumericNode)arguments[1]!).Value;
                    var nan = double.IsNaN(a.Value) || double.IsNaN(exponent) || (Math.Abs(a.Value) == 1 && double.IsInfinity(exponent));
                    return new CssCalcNumericNode(nan ? double.NaN : Math.Pow(a.Value, exponent), "");
                }

                case CssCalcFunctionKind.Sqrt:
                    return new CssCalcNumericNode(Math.Sqrt(a.Value), "");

                case CssCalcFunctionKind.Log:
                {
                    if (arguments.Count == 1)
                        return new CssCalcNumericNode(Math.Log(a.Value), "");

                    // The logarithm in base 1 is NaN, and the logarithm of 1 is +0 in any base
                    var logBase = Math.Log(((CssCalcNumericNode)arguments[1]!).Value);
                    var result = logBase == 0 ? double.NaN : Math.Log(a.Value) / logBase;
                    return new CssCalcNumericNode(result == 0 ? 0 : result, "");
                }

                case CssCalcFunctionKind.Exp:
                    return new CssCalcNumericNode(Math.Exp(a.Value), "");

                case CssCalcFunctionKind.Abs:
                    return new CssCalcNumericNode(Math.Abs(a.Value), unit);

                case CssCalcFunctionKind.Progress:
                {
                    // (value - start) / (end - start), clamped to [0, 1] (0 when the start and the end are equal) unless no-clamp is specified
                    var start = ((CssCalcNumericNode)arguments[1]!).Value;
                    var end = ((CssCalcNumericNode)arguments[2]!).Value;
                    if (noClamp)
                        return new CssCalcNumericNode(end == start ? (a.Value == start ? 0 : (a.Value > start ? double.PositiveInfinity : double.NegativeInfinity)) : (a.Value - start) / (end - start), "");

                    var progress = end == start ? 0 : (a.Value - start) / (end - start);
                    return new CssCalcNumericNode(double.IsNaN(progress) ? progress : Math.Clamp(progress, 0, 1), "");
                }

                default: // Sign
                    return new CssCalcNumericNode(double.IsNaN(a.Value) || a.Value == 0 ? a.Value : Math.Sign(a.Value), "");
            }
        }

        /// <summary>https://drafts.csswg.org/css-values-4/#round-func</summary>
        private static double Round(CssCalcRoundingStrategy strategy, double a, double b)
        {
            if (double.IsNaN(a) || double.IsNaN(b) || b == 0)
                return double.NaN;

            if (double.IsInfinity(a))
                return double.IsInfinity(b) ? double.NaN : a;

            if (double.IsInfinity(b))
            {
                return strategy switch
                {
                    CssCalcRoundingStrategy.Up => a > 0 ? double.PositiveInfinity : (a < 0 ? -0d : a),
                    CssCalcRoundingStrategy.Down => a < 0 ? double.NegativeInfinity : (a > 0 ? 0d : a),
                    _ => double.IsNegative(a) ? -0d : 0d,
                };
            }

            var step = Math.Abs(b);
            var quotient = a / step;
            var lower = Math.Floor(quotient) * step;
            var upper = Math.Ceiling(quotient) * step;
            var result = strategy switch
            {
                CssCalcRoundingStrategy.Up => upper,
                CssCalcRoundingStrategy.Down => lower,
                CssCalcRoundingStrategy.ToZero => a >= 0 ? lower : upper,
                _ => a - lower < upper - a ? lower : upper, // Ties choose the upper value
            };

            // A zero result has the sign of A
            return result == 0 ? (double.IsNegative(a) ? -0d : 0d) : result;
        }

        /// <summary>https://drafts.csswg.org/css-values-4/#funcdef-mod: mod() takes the sign of B, rem() the sign of A.</summary>
        private static double Modulo(double a, double b, bool isMod)
        {
            if (double.IsNaN(a) || double.IsNaN(b) || b == 0 || double.IsInfinity(a))
                return double.NaN;

            if (double.IsInfinity(b))
            {
                if (isMod && double.IsNegative(a) != double.IsNegative(b))
                    return double.NaN;

                return a;
            }

            var result = a % b;
            if (isMod && result != 0 && (result < 0) != (b < 0))
            {
                result += b;
            }

            // A zero result has the sign of B for mod() and the sign of A for rem()
            if (result == 0)
                return double.IsNegative(isMod ? b : a) ? -0d : 0d;

            return result;
        }

        /// <summary>https://drafts.csswg.org/css-values-4/#trig-funcs</summary>
        private static double Trigonometric(CssCalcFunctionKind kind, double value, bool isDegrees)
        {
            if (isDegrees && double.IsFinite(value))
            {
                // Exact values for the multiples of 90deg (tan() has asymptotes at 90deg and 270deg)
                var degrees = value % 360;
                if (degrees < 0)
                {
                    degrees += 360;
                }

                if (degrees % 90 == 0)
                {
                    var zero = double.IsNegative(value) ? -0d : 0d;
                    return (kind, degrees) switch
                    {
                        (CssCalcFunctionKind.Sin, 0) => zero,
                        (CssCalcFunctionKind.Sin, 90) => 1,
                        (CssCalcFunctionKind.Sin, 180) => 0,
                        (CssCalcFunctionKind.Sin, _) => -1,
                        (CssCalcFunctionKind.Cos, 0) => 1,
                        (CssCalcFunctionKind.Cos, 180) => -1,
                        (CssCalcFunctionKind.Cos, _) => 0,
                        (_, 0) => zero,
                        (_, 90) => double.PositiveInfinity,
                        (_, 180) => 0,
                        _ => double.NegativeInfinity,
                    };
                }

                value = value * Math.PI / 180;
            }
            else if (isDegrees)
            {
                value = double.NaN;
            }

            return kind switch
            {
                CssCalcFunctionKind.Sin => Math.Sin(value),
                CssCalcFunctionKind.Cos => Math.Cos(value),
                _ => Math.Tan(value),
            };
        }

        /// <summary>https://drafts.csswg.org/css-values-4/#css-add-two-types</summary>
        private bool TryAdd(CssCalcType left, CssCalcType right, out CssCalcType result)
        {
            result = default;
            if (left.PercentHint != CssCalcType.NoHint && right.PercentHint != CssCalcType.NoHint && left.PercentHint != right.PercentHint)
                return false;

            if (left.PercentHint != CssCalcType.NoHint && right.PercentHint == CssCalcType.NoHint)
            {
                right = right.WithPercentHint(left.PercentHint);
            }
            else if (right.PercentHint != CssCalcType.NoHint && left.PercentHint == CssCalcType.NoHint)
            {
                left = left.WithPercentHint(right.PercentHint);
            }

            if (left.ExponentsEqual(right))
            {
                result = left;
                return true;
            }

            // A percentage added to another type: the percentages resolve to that type (only the type allowed by the context)
            if ((left.HasPercent || right.HasPercent) && _percentHint != CssCalcType.NoHint && left.PercentHint == CssCalcType.NoHint)
            {
                left = left.WithPercentHint(_percentHint);
                right = right.WithPercentHint(_percentHint);
                if (left.ExponentsEqual(right))
                {
                    result = left;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the canonical unit of the type when it matches a type a math function can resolve to ("" for &lt;number&gt;,
        /// "%" for &lt;percentage&gt; when percentages are their own type, otherwise the unit of the base type); returns null otherwise.
        /// </summary>
        public string? GetUnitOfType(CssCalcType type)
        {
            if (type.PercentHint != CssCalcType.NoHint && type.PercentHint != _percentHint)
                return null;

            var found = -1;
            for (var i = 0; i < CssCalcType.BaseTypeCount; i++)
            {
                var exponent = type.Exponents[i];
                if (exponent == 0)
                    continue;

                if (exponent != 1 || found >= 0)
                    return null;

                found = i;
            }

            if (found < 0)
                return "";

            if (found == CssCalcType.PercentIndex)
                return _percentageTypeUnit;

            return CssCalcType.CanonicalUnits[found];
        }
    }

    /// <summary>https://drafts.csswg.org/css-values-4/#serialize-a-calculation-tree</summary>
    internal static void AppendNode(StringBuilder builder, CssCalcNode node, bool omitParentheses)
    {
        switch (node)
        {
            case CssCalcNumericNode numeric:
                AppendNumeric(builder, numeric.Value, numeric.Unit, omitParentheses);
                return;

            case CssCalcKeywordNode keyword:
                builder.Append(keyword.Name);
                return;

            case CssCalcFunctionNode { Kind: CssCalcFunctionKind.Random } random:
            {
                builder.Append("random(");
                if (random.FixedBaseValue is { } fixedValue)
                {
                    builder.Append("fixed ");
                    if (random.FixedIsMathFunction && fixedValue is CssCalcNumericNode)
                    {
                        builder.Append("calc(");
                        AppendNode(builder, fixedValue, omitParentheses: true);
                        builder.Append(')');
                    }
                    else
                    {
                        AppendNode(builder, fixedValue, omitParentheses: true);
                    }

                    builder.Append(", ");
                }
                else if (random.Sharing is { } sharing)
                {
                    builder.Append(sharing.Serialize()).Append(", ");
                }

                for (var i = 0; i < random.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    AppendNode(builder, random.Arguments[i]!, omitParentheses: true);
                }

                builder.Append(')');
                return;
            }

            case CssCalcFunctionNode { Kind: CssCalcFunctionKind.CalcMix, Weights: { } weights } calcMix:
            {
                builder.Append("calc-mix(");
                for (var i = 0; i < calcMix.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    AppendNode(builder, calcMix.Arguments[i]!, omitParentheses: true);
                    builder.Append(' ').Append(CssSerializer.SerializeNumber(weights[i])).Append('%');
                }

                builder.Append(')');
                return;
            }

            case CssCalcFunctionNode { Kind: CssCalcFunctionKind.CalcSize } calcSize:
            {
                builder.Append("calc-size(");
                if (calcSize.BasisKeyword is { } basisKeyword)
                {
                    builder.Append(basisKeyword);
                }
                else
                {
                    AppendNode(builder, calcSize.Arguments[0]!, omitParentheses: true);
                }

                builder.Append(", ");
                AppendNode(builder, calcSize.Arguments[1]!, omitParentheses: true);
                builder.Append(')');
                return;
            }

            case CssCalcFunctionNode function:
            {
                builder.Append(GetFunctionName(function.Kind)).Append('(');
                if (function.NoClamp)
                {
                    builder.Append("no-clamp ");
                }

                if (function.Strategy != CssCalcRoundingStrategy.Nearest)
                {
                    builder.Append(function.Strategy switch
                    {
                        CssCalcRoundingStrategy.Up => "up",
                        CssCalcRoundingStrategy.Down => "down",
                        _ => "to-zero",
                    }).Append(", ");
                }

                for (var i = 0; i < function.Arguments.Count; i++)
                {
                    if (i > 0)
                    {
                        builder.Append(", ");
                    }

                    if (function.Arguments[i] is { } argument)
                    {
                        AppendNode(builder, argument, omitParentheses: true);
                    }
                    else
                    {
                        builder.Append("none");
                    }
                }

                builder.Append(')');
                return;
            }

            case CssCalcNegateNode negate:
                builder.Append(omitParentheses ? "-1 * " : "(-1 * ");
                AppendNode(builder, negate.Child, omitParentheses: false);
                if (!omitParentheses)
                {
                    builder.Append(')');
                }

                return;

            case CssCalcInvertNode invert:
                builder.Append(omitParentheses ? "1 / " : "(1 / ");
                AppendNode(builder, invert.Child, omitParentheses: false);
                if (!omitParentheses)
                {
                    builder.Append(')');
                }

                return;

            case CssCalcSumNode sum:
            {
                if (!omitParentheses)
                {
                    builder.Append('(');
                }

                var children = SortChildren(sum.Children);
                AppendNode(builder, children[0], omitParentheses: false);
                for (var i = 1; i < children.Length; i++)
                {
                    var child = children[i];
                    if (child is CssCalcNegateNode negateChild)
                    {
                        builder.Append(" - ");
                        AppendNode(builder, negateChild.Child, omitParentheses: false);
                    }
                    else if (child is CssCalcNumericNode { Value: < 0 and > double.NegativeInfinity } negative)
                    {
                        builder.Append(" - ");
                        AppendNumeric(builder, -negative.Value, negative.Unit, omitParentheses: false);
                    }
                    else
                    {
                        builder.Append(" + ");
                        AppendNode(builder, child, omitParentheses: false);
                    }
                }

                if (!omitParentheses)
                {
                    builder.Append(')');
                }

                return;
            }

            case CssCalcProductNode product:
            {
                if (!omitParentheses)
                {
                    builder.Append('(');
                }

                var children = SortChildren(product.Children);
                AppendNode(builder, children[0], omitParentheses: false);
                for (var i = 1; i < children.Length; i++)
                {
                    if (children[i] is CssCalcInvertNode invertChild)
                    {
                        builder.Append(" / ");
                        AppendNode(builder, invertChild.Child, omitParentheses: false);
                    }
                    else
                    {
                        builder.Append(" * ");
                        AppendNode(builder, children[i], omitParentheses: false);
                    }
                }

                if (!omitParentheses)
                {
                    builder.Append(')');
                }

                return;
            }
        }
    }

    /// <summary>Serializes a numeric value; infinite and NaN values are serialized as a product of a keyword and 1 canonical unit (https://drafts.csswg.org/css-values-4/#calc-ieee).</summary>
    internal static void AppendNumeric(StringBuilder builder, double value, string unit, bool omitParentheses)
    {
        if (double.IsFinite(value))
        {
            // A negative zero relative length keeps its sign, since it is resolved later (calc(1px / -0em) is -infinity)
            builder.Append(value == 0 && double.IsNegative(value) && IsRelativeUnit(unit) ? "-0" : CssSerializer.SerializeNumberForComputation(value)).Append(unit);
            return;
        }

        var keyword = double.IsNaN(value) ? "NaN" : value > 0 ? "infinity" : "-infinity";
        if (unit.Length == 0)
        {
            builder.Append(keyword);
            return;
        }

        if (!omitParentheses)
        {
            builder.Append('(');
        }

        builder.Append(keyword).Append(" * 1").Append(unit);
        if (!omitParentheses)
        {
            builder.Append(')');
        }
    }

    /// <summary>https://drafts.csswg.org/css-values-4/#sort-a-calculations-children: the number, the percentage, the dimensions ordered by unit, then the other nodes.</summary>
    private static CssCalcNode[] SortChildren(List<CssCalcNode> nodes)
    {
        var result = nodes.ToArray();

        // Stable insertion sort (there are only a few children)
        for (var i = 1; i < result.Length; i++)
        {
            var node = result[i];
            var j = i - 1;
            while (j >= 0 && Compare(result[j], node) > 0)
            {
                result[j + 1] = result[j];
                j--;
            }

            result[j + 1] = node;
        }

        return result;

        static int Compare(CssCalcNode left, CssCalcNode right)
        {
            var leftRank = GetRank(left);
            var rightRank = GetRank(right);
            if (leftRank != rightRank)
                return leftRank.CompareTo(rightRank);

            if (leftRank == 2)
                return string.CompareOrdinal(((CssCalcNumericNode)left).Unit, ((CssCalcNumericNode)right).Unit);

            return 0;
        }

        static int GetRank(CssCalcNode node) => node switch
        {
            CssCalcNumericNode { Unit: "" } => 0,
            CssCalcNumericNode { Unit: "%" } => 1,
            CssCalcNumericNode => 2,
            _ => 3,
        };
    }

    private static string GetFunctionName(CssCalcFunctionKind kind) => kind switch
    {
        CssCalcFunctionKind.Min => "min",
        CssCalcFunctionKind.Max => "max",
        CssCalcFunctionKind.Clamp => "clamp",
        CssCalcFunctionKind.Round => "round",
        CssCalcFunctionKind.Mod => "mod",
        CssCalcFunctionKind.Rem => "rem",
        CssCalcFunctionKind.Sin => "sin",
        CssCalcFunctionKind.Cos => "cos",
        CssCalcFunctionKind.Tan => "tan",
        CssCalcFunctionKind.Asin => "asin",
        CssCalcFunctionKind.Acos => "acos",
        CssCalcFunctionKind.Atan => "atan",
        CssCalcFunctionKind.Atan2 => "atan2",
        CssCalcFunctionKind.Pow => "pow",
        CssCalcFunctionKind.Sqrt => "sqrt",
        CssCalcFunctionKind.Hypot => "hypot",
        CssCalcFunctionKind.Log => "log",
        CssCalcFunctionKind.Exp => "exp",
        CssCalcFunctionKind.Abs => "abs",
        CssCalcFunctionKind.Sign => "sign",
        CssCalcFunctionKind.Random => "random",
        CssCalcFunctionKind.Progress => "progress",
        CssCalcFunctionKind.CalcSize => "calc-size",
        CssCalcFunctionKind.CalcMix => "calc-mix",
        _ => "calc",
    };
}

/// <summary>The result of a math function (https://drafts.csswg.org/css-values-4/#math).</summary>
internal sealed class CssCalcValue
{
    private readonly bool _percentagesAreUnresolved;

    internal CssCalcValue(CssCalcNode root, string typeUnit, bool percentagesAreUnresolved)
    {
        Root = root;
        TypeUnit = typeUnit;
        _percentagesAreUnresolved = percentagesAreUnresolved;
        if (root is CssCalcNumericNode numeric)
        {
            IsNumeric = true;
            Value = numeric.Value;
            Unit = numeric.Unit;
            IsResolved = !(percentagesAreUnresolved && numeric.Unit == "%");
        }
        else
        {
            Value = double.NaN;
            Unit = typeUnit;
        }
    }

    /// <summary>The simplified calculation tree.</summary>
    internal CssCalcNode Root { get; }

    /// <summary>True when the result is a single numeric value, possibly a percentage that cannot be resolved (e.g. calc(50% * 2) is 100%).</summary>
    public bool IsNumeric { get; }

    /// <summary>True when the result is a single numeric value that is not an unresolved percentage.</summary>
    public bool IsResolved { get; }

    /// <summary>When <see cref="IsNumeric"/> is true, the value; NaN otherwise.</summary>
    public double Value { get; }

    /// <summary>
    /// When <see cref="IsNumeric"/> is true, the canonical unit of the value ("" for &lt;number&gt;, "%" for &lt;percentage&gt;, "px", "deg", "s", "hz", "dppx" or "fr");
    /// otherwise the canonical unit of the type of the calculation (<see cref="TypeUnit"/>).
    /// </summary>
    public string Unit { get; }

    /// <summary>
    /// The canonical unit of the type of the calculation (https://drafts.csswg.org/css-values-4/#calc-type-checking): "" for &lt;number&gt;,
    /// "%" for &lt;percentage&gt; (when percentages are their own type), "px" for &lt;length&gt; or &lt;length-percentage&gt;, etc.
    /// Use it to check that the math function is allowed where it is used (e.g. calc(1deg) is not a &lt;length&gt;).
    /// </summary>
    public string TypeUnit { get; }

    /// <summary>
    /// Clamps a numeric value to the range allowed by its context (https://drafts.csswg.org/css-values-4/#calc-range): NaN becomes 0 and infinite values
    /// become the bounds of the range. A calculation that is not a single numeric value is returned unchanged (it is clamped when it is used).
    /// </summary>
    public CssCalcValue Clamp(double min, double max)
    {
        if (!IsNumeric)
            return this;

        var value = double.IsNaN(Value) ? 0 : Value;
        value = Math.Clamp(value, min, max);
        if (value.Equals(Value))
            return this;

        return new CssCalcValue(new CssCalcNumericNode(value, Unit), TypeUnit, _percentagesAreUnresolved);
    }

    /// <summary>
    /// Serializes the value as a computed value (https://drafts.csswg.org/css-values-4/#serialize-a-math-function): a single numeric value is serialized
    /// as is (16px, 0.5, 50%), an unresolved calculation as a math function (calc(100% - 16px), min(10%, 20px)).
    /// </summary>
    public string Serialize()
    {
        if (Root is CssCalcNumericNode numeric && double.IsFinite(numeric.Value))
            return CssSerializer.SerializeNumber(numeric.Value) + numeric.Unit;

        var builder = new StringBuilder();
        if (Root is CssCalcFunctionNode || CssCalc.IsAnchorFunctionNode(Root))
        {
            CssCalc.AppendNode(builder, Root, omitParentheses: true);
        }
        else
        {
            builder.Append("calc(");
            CssCalc.AppendNode(builder, Root, omitParentheses: true);
            builder.Append(')');
        }

        return builder.ToString();
    }

    public override string ToString() => Serialize();
}

/// <summary>
/// A CSS type (https://drafts.csswg.org/css-values-4/#css-type): the exponents of the base types, and the percent hint.
/// Index 0 is "percent", then length, angle, time, frequency, resolution and flex.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal struct CssCalcType
{
    public const int BaseTypeCount = 7;
    public const int PercentIndex = 0;

    /// <summary>No percent hint.</summary>
    public const sbyte NoHint = 0;

    /// <summary>The percentages resolve to numbers.</summary>
    public const sbyte NumberHint = BaseTypeCount;

    /// <summary>The canonical unit of each base type.</summary>
    public static readonly string[] CanonicalUnits = ["%", "px", "deg", "s", "hz", "dppx", "fr"];

    public CssCalcTypeExponents Exponents;

    /// <summary>The index of the base type the percentages resolve to, <see cref="NoHint"/> or <see cref="NumberHint"/>.</summary>
    public sbyte PercentHint;

    public readonly bool HasPercent => Exponents[PercentIndex] != 0;

    /// <summary>Returns the index of the base type of a canonical unit or a relative length unit, or -1 for a number.</summary>
    public static int GetIndex(string unit) => unit switch
    {
        "%" => 0,
        "px" => 1,
        "deg" => 2,
        "s" => 3,
        "hz" => 4,
        "dppx" => 5,
        "fr" => 6,

        // The relative lengths are only kept in the specified values (CssCalcContext.KeepRelativeUnits)
        _ when CssCalc.IsRelativeUnit(unit) => 1,
        _ => -1,
    };

    public static CssCalcType FromUnit(string unit)
    {
        var type = default(CssCalcType);
        var index = GetIndex(unit);
        if (index >= 0)
        {
            type.Exponents[index] = 1;
        }

        return type;
    }

    /// <summary>https://drafts.csswg.org/css-values-4/#apply-the-percent-hint</summary>
    public readonly CssCalcType WithPercentHint(sbyte hint)
    {
        var result = this;
        if (hint != NumberHint)
        {
            result.Exponents[hint] += result.Exponents[PercentIndex];
        }

        result.Exponents[PercentIndex] = 0;
        result.PercentHint = hint;
        return result;
    }

    public readonly bool ExponentsEqual(in CssCalcType other)
    {
        for (var i = 0; i < BaseTypeCount; i++)
        {
            if (Exponents[i] != other.Exponents[i])
                return false;
        }

        return true;
    }

    /// <summary>https://drafts.csswg.org/css-values-4/#css-multiply-two-types (the type of an Invert node has its exponents negated).</summary>
    public static bool TryMultiply(CssCalcType left, CssCalcType right, out CssCalcType result)
    {
        result = default;
        if (left.PercentHint != NoHint && right.PercentHint != NoHint && left.PercentHint != right.PercentHint)
            return false;

        if (left.PercentHint != NoHint && right.PercentHint == NoHint)
        {
            right = right.WithPercentHint(left.PercentHint);
        }
        else if (right.PercentHint != NoHint && left.PercentHint == NoHint)
        {
            left = left.WithPercentHint(right.PercentHint);
        }

        result = left;
        for (var i = 0; i < BaseTypeCount; i++)
        {
            result.Exponents[i] += right.Exponents[i];
        }

        return true;
    }

    public readonly CssCalcType Invert()
    {
        var result = this;
        for (var i = 0; i < BaseTypeCount; i++)
        {
            result.Exponents[i] = -result.Exponents[i];
        }

        return result;
    }
}

[InlineArray(CssCalcType.BaseTypeCount)]
internal struct CssCalcTypeExponents
{
    private int _element0;
}

/// <summary>A node of a calculation tree (https://drafts.csswg.org/css-values-4/#calculation-tree).</summary>
internal abstract class CssCalcNode(CssCalcType type)
{
    public CssCalcType Type { get; } = type;
}

/// <summary>A numeric value in its canonical unit ("" for a number, "%" for an unresolved percentage).</summary>
internal sealed class CssCalcNumericNode(double value, string unit) : CssCalcNode(CssCalcType.FromUnit(unit))
{
    public double Value { get; } = value;

    public string Unit { get; } = unit;
}

/// <summary>A channel keyword of a relative color (e.g. r in rgb(from red calc(r * 2) g b)): a number whose value is not known.</summary>
internal sealed class CssCalcKeywordNode(string name, CssCalcType type = default) : CssCalcNode(type)
{
    public string Name { get; } = name;
}

internal sealed class CssCalcSumNode(List<CssCalcNode> children, CssCalcType type) : CssCalcNode(type)
{
    public List<CssCalcNode> Children { get; } = children;
}

internal sealed class CssCalcProductNode(List<CssCalcNode> children, CssCalcType type) : CssCalcNode(type)
{
    public List<CssCalcNode> Children { get; } = children;
}

internal sealed class CssCalcNegateNode(CssCalcNode child) : CssCalcNode(child.Type)
{
    public CssCalcNode Child { get; } = child;
}

internal sealed class CssCalcInvertNode(CssCalcNode child) : CssCalcNode(child.Type.Invert())
{
    public CssCalcNode Child { get; } = child;
}

/// <summary>A math function other than calc() whose arguments cannot all be resolved (e.g. min(10%, 20px)). A null argument is the none keyword of clamp().</summary>
internal sealed class CssCalcFunctionNode(CssCalcFunctionKind kind, CssCalcRoundingStrategy strategy, List<CssCalcNode?> arguments, CssCalcType type) : CssCalcNode(type)
{
    public CssCalcFunctionKind Kind { get; } = kind;

    public CssCalcRoundingStrategy Strategy { get; } = strategy;

    public List<CssCalcNode?> Arguments { get; } = arguments;

    /// <summary>The &lt;random-value-sharing&gt; of random() (null when it is omitted or fixed).</summary>
    public CssRandomSharing? Sharing { get; init; }

    /// <summary>The fixed random base value of random() (fixed &lt;number&gt;), or null.</summary>
    public CssCalcNode? FixedBaseValue { get; init; }

    /// <summary>Whether the fixed random base value was specified as a math function (it is serialized as calc()).</summary>
    public bool FixedIsMathFunction { get; init; }

    /// <summary>Whether progress() is not clamped (no-clamp).</summary>
    public bool NoClamp { get; init; }

    /// <summary>The keyword basis of calc-size() (e.g. auto, any), or null when the basis is the first argument.</summary>
    public string? BasisKeyword { get; init; }

    /// <summary>The normalized weights of the values of calc-mix(), in percent.</summary>
    public List<double>? Weights { get; init; }
}
