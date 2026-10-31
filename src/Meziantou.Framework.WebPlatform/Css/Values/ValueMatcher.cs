using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>A part of the value matched by a component of the root group of a grammar (used to expand shorthands).</summary>
/// <remarks>
/// Property references (&lt;'margin-top'&gt;) and the types that set a longhand (<see cref="CssPropertyRegistry.GetTypeLonghand"/>)
/// are captured at any depth of the grammar with a ChildIndex of -1.
/// </remarks>
internal readonly record struct GrammarCapture(int ChildIndex, int Start, int End, string? Property = null);

/// <summary>Matches component values against a value definition (https://drafts.csswg.org/css-values-4/#value-defs).</summary>
internal static class ValueMatcher
{
    private const int MaxDepth = 64;

    /// <summary>The CSS-wide keywords (https://drafts.csswg.org/css-values-4/#css-wide-keywords).</summary>
    public static readonly FrozenSet<string> CssWideKeywords = FrozenSet.Create(StringComparer.Ordinal, "initial", "inherit", "unset", "revert", "revert-layer", "revert-rule");

    private static readonly FrozenSet<string> MathFunctions = FrozenSet.Create(StringComparer.Ordinal,
        "calc", "min", "max", "clamp", "round", "mod", "rem", "sin", "cos", "tan", "asin", "acos", "atan", "atan2", "pow", "sqrt", "hypot", "log", "exp", "abs", "sign", "progress", "random", "calc-mix");

    private static readonly FrozenSet<string> LengthUnits = FrozenSet.Create(StringComparer.Ordinal,
        "em", "rem", "ex", "rex", "cap", "rcap", "ch", "rch", "ic", "ric", "lh", "rlh",
        "vw", "vh", "vi", "vb", "vmin", "vmax", "svw", "svh", "svi", "svb", "svmin", "svmax", "lvw", "lvh", "lvi", "lvb", "lvmin", "lvmax",
        "dvw", "dvh", "dvi", "dvb", "dvmin", "dvmax", "cqw", "cqh", "cqi", "cqb", "cqmin", "cqmax",
        "cm", "mm", "q", "in", "pt", "pc", "px");

    private static readonly FrozenSet<string> AngleUnits = FrozenSet.Create(StringComparer.Ordinal, "deg", "grad", "rad", "turn");
    private static readonly FrozenSet<string> TimeUnits = FrozenSet.Create(StringComparer.Ordinal, "s", "ms");
    private static readonly FrozenSet<string> FrequencyUnits = FrozenSet.Create(StringComparer.Ordinal, "hz", "khz");
    private static readonly FrozenSet<string> ResolutionUnits = FrozenSet.Create(StringComparer.Ordinal, "dpi", "dpcm", "dppx", "x");

    /// <summary>Returns whether a unit (in lowercase) is a length unit (https://drafts.csswg.org/css-values-4/#lengths).</summary>
    public static bool IsLengthUnit(string unit) => LengthUnits.Contains(unit);

    /// <summary>Returns whether a lowercase unit is a known CSS dimension unit (https://drafts.csswg.org/css-values-4/#dimensions).</summary>
    public static bool IsKnownUnit(string unit) => LengthUnits.Contains(unit) || AngleUnits.Contains(unit) || TimeUnits.Contains(unit) || FrequencyUnits.Contains(unit) || ResolutionUnits.Contains(unit) || unit == "fr";

    /// <summary>Returns whether the values (whitespace excluded) match the grammar entirely.</summary>
    public static bool Matches(GrammarNode grammar, List<CssComponentValue> values) => MatchAll(grammar, values, captures: null);

    /// <summary>Returns whether the values match the grammar, and the parts matched by the components of the root group.</summary>
    /// <param name="type">The type whose grammar is matched (e.g. grid-line), which may exclude keywords from &lt;custom-ident&gt;.</param>
    public static bool MatchAll(GrammarNode grammar, List<CssComponentValue> values, List<GrammarCapture>? captures, string? type = null)
    {
        var context = new MatchContext(values, captures is not null, type is null ? null : CustomIdentExclusions.GetValueOrDefault(type), AcceptsPercentages(grammar), GetKeywords(grammar), GetAnchorFunctions(grammar), IsFlexBasisGrammar(grammar), AcceptsLengthPercentages(grammar));
        foreach (var (end, path) in MatchNode(grammar, context, 0, 0, root: true))
        {
            if (end == values.Count)
            {
                if (captures is not null)
                {
                    captures.Clear();
                    for (var current = path; current is not null; current = current.Previous)
                    {
                        captures.Add(current.Capture);
                    }

                    captures.Reverse();
                }

                return true;
            }
        }

        return false;
    }

    /// <summary>Removes the whitespace (it is not significant between component values).</summary>
    public static List<CssComponentValue> RemoveWhitespace(IEnumerable<CssComponentValue> values)
    {
        var result = values is ICollection<CssComponentValue> collection ? new List<CssComponentValue>(collection.Count) : [];
        foreach (var value in values)
        {
            if (value is not CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                result.Add(value);
            }
        }

        return result;
    }

    private sealed record CapturePath(GrammarCapture Capture, CapturePath? Previous);

    private sealed class MatchContext(List<CssComponentValue> values, bool capture, FrozenSet<string>? excludedIdents = null, bool percentagesAllowed = true, HashSet<string>? keywords = null, CssAnchorFunctions anchorFunctions = CssAnchorFunctions.None, bool excludesReferencedAnchorFunctions = false, bool percentagesAreLengths = false)
    {
        /// <summary>
        /// Whether the percentages of the grammar resolve against lengths (it accepts a &lt;length-percentage&gt;): calc(10% / 1px) is
        /// then a &lt;number&gt; (https://drafts.csswg.org/css-values-4/#calc-type-checking), e.g. in line-height.
        /// </summary>
        public bool PercentagesAreLengths { get; } = percentagesAreLengths;

        /// <summary>
        /// Whether the properties referenced by the grammar do not accept their anchor functions: <c>flex-basis</c> references
        /// <c>&lt;'width'&gt;</c>, but <c>anchor-size()</c> is only allowed in the sizing, inset and margin properties
        /// (https://drafts.csswg.org/css-anchor-position-1/#anchor-size-fn).
        /// </summary>
        public bool ExcludesReferencedAnchorFunctions { get; } = excludesReferencedAnchorFunctions;

        /// <summary>The anchor functions the grammar of the property accepts (<c>&lt;anchor()&gt;</c>, <c>&lt;anchor-size()&gt;</c>): they are also accepted in its math functions.</summary>
        public CssAnchorFunctions AnchorFunctions { get; } = anchorFunctions;

        /// <summary>The keywords of the grammar of the property, outside of its functions (the basis of calc-size() is one of them).</summary>
        public HashSet<string>? Keywords { get; } = keywords;

        /// <summary>Whether the grammar accepts percentages: the math functions of the numeric types can then contain percentages (e.g. calc(10% / 20%) in opacity).</summary>
        public bool PercentagesAllowed { get; } = percentagesAllowed;

        public List<CssComponentValue> Values { get; } = values;

        public bool Capture { get; } = capture;

        /// <summary>The keywords that are not a &lt;custom-ident&gt; in the grammar being matched.</summary>
        public FrozenSet<string>? ExcludedIdents { get; } = excludedIdents;
    }

    /// <summary>
    /// The keywords excluded from the &lt;custom-ident&gt; of a type (https://drafts.csswg.org/css-grid/#typedef-grid-row-start-grid-line,
    /// https://drafts.csswg.org/css-conditional-5/#typedef-container-name).
    /// </summary>
    private static readonly FrozenDictionary<string, FrozenSet<string>> CustomIdentExclusions = new Dictionary<string, FrozenSet<string>>(StringComparer.Ordinal)
    {
        ["grid-line"] = FrozenSet.Create(StringComparer.Ordinal, "auto", "span"),
        ["container-name"] = FrozenSet.Create(StringComparer.Ordinal, "none", "and", "not", "or"),

        // https://drafts.csswg.org/css-transitions/#transition-property-property: none is not a property name
        ["single-transition-property"] = FrozenSet.Create(StringComparer.Ordinal, "none"),

        // https://drafts.csswg.org/css-lists-3/#typedef-counter-name, https://drafts.csswg.org/css-counter-styles-3/#typedef-counter-style-name:
        // none is not a counter name nor a counter style name
        ["counter-name"] = FrozenSet.Create(StringComparer.Ordinal, "none"),
        ["counter-style-name"] = FrozenSet.Create(StringComparer.Ordinal, "none"),

        // https://drafts.csswg.org/css-color-adjust-1/#color-scheme-prop: normal and only are not color scheme names
        ["color-scheme-ident"] = FrozenSet.Create(StringComparer.Ordinal, "normal", "only"),

        // https://drafts.csswg.org/css-fonts-4/#family-name-syntax: the generic family keywords must be quoted to be family names
        ["font-family-name"] = FrozenSet.Create(StringComparer.Ordinal, "serif", "sans-serif", "system-ui", "cursive", "fantasy", "math", "monospace", "ui-serif", "ui-sans-serif", "ui-monospace", "ui-rounded"),
    }.ToFrozenDictionary(StringComparer.Ordinal);

    private static IEnumerable<(int End, CapturePath? Path)> MatchNode(GrammarNode node, MatchContext context, int position, int depth, bool root = false, CapturePath? path = null)
    {
        if (depth > MaxDepth)
            return [];

        // A component without multiplier matches once: its results are the ones of the component (most components of the
        // grammars), without the iterators of the repetitions
        if (node is { Min: 1, Max: 1, Required: false })
            return MatchOnce(node, context, position, depth, root, path);

        // Repetitions
        return MatchRepeated(node, context, position, depth, 0, path, root);
    }

    private static IEnumerable<(int End, CapturePath? Path)> MatchRepeated(GrammarNode node, MatchContext context, int position, int depth, int count, CapturePath? path, bool root)
    {
        // Greedy: try one more repetition first
        if (count < node.Max)
        {
            var start = position;
            if (count > 0 && node.CommaSeparated)
            {
                if (start < context.Values.Count && context.Values[start] is CssPreservedToken { Type: CssTokenType.Comma })
                {
                    start++;
                }
                else
                {
                    start = -1;
                }
            }

            if (start >= 0)
            {
                foreach (var (end, childPath) in MatchOnce(node, context, start, depth, root, path))
                {
                    if (end == start && (count >= node.Min || node.Required))
                        continue; // No progress, or an empty group that must not be empty (the ! multiplier)

                    foreach (var result in MatchRepeated(node, context, end, depth, count + 1, childPath, root))
                    {
                        yield return result;
                    }
                }
            }
        }

        if (count >= node.Min)
        {
            yield return (position, path);
        }
    }

    private static IEnumerable<(int End, CapturePath? Path)> MatchOnce(GrammarNode node, MatchContext context, int position, int depth, bool root, CapturePath? path)
    {
        switch (node)
        {
            case GroupNode group:
                return MatchGroup(group, context, position, depth, root, path);
            default:
                return MatchSingle(node, context, position, depth, path);
        }
    }

    // The keywords, literals and functions match at most one component value: their result is an array (empty when they do
    // not match, which is the case of most of the alternatives of a grammar) instead of an iterator
    private static IEnumerable<(int End, CapturePath? Path)> MatchSingle(GrammarNode node, MatchContext context, int position, int depth, CapturePath? path)
    {
        switch (node)
        {
            case KeywordNode keyword:
                return position < context.Values.Count && context.Values[position] is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, keyword.Keyword)
                    ? [(position + 1, path)]
                    : [];

            case LiteralNode literal:
                return position < context.Values.Count && IsLiteral(context.Values[position], literal.Literal) ? [(position + 1, path)] : [];

            case FunctionNode function:
                if (position < context.Values.Count && context.Values[position] is CssFunction value && InfraAscii.EqualsIgnoreAsciiCase(value.Name, function.Name))
                {
                    var arguments = RemoveWhitespace(value.Value);
                    if ((function.Arguments is null ? arguments.Count == 0 : MatchAll(function.Arguments, arguments, captures: null)) && IsValidFunction(value))
                        return [(position + 1, path)];
                }

                return [];

            case TypeNode type:
                // The built-in types (e.g. <length>, <ident>) match a single component value
                if (!type.IsProperty && type.Name != "calc-size()" && position < context.Values.Count && MatchBuiltInType(type, context.Values[position], context.ExcludedIdents, context.PercentagesAllowed, percentagesAreLengths: context.PercentagesAreLengths) is { } builtIn)
                    return builtIn ? [(position + 1, CaptureType(type, context, position, position + 1, path))] : [];

                return MatchTypeNode(type, context, position, depth, path);

            default:
                return [];
        }
    }

    private static IEnumerable<(int End, CapturePath? Path)> MatchTypeNode(TypeNode type, MatchContext context, int position, int depth, CapturePath? path)
    {
        foreach (var end in MatchType(type, context, position, depth))
        {
            yield return (end, CaptureType(type, context, position, end, path));
        }
    }

    /// <summary>Captures the part matched by a property reference or by a type that sets a longhand.</summary>
    private static CapturePath? CaptureType(TypeNode type, MatchContext context, int start, int end, CapturePath? path)
    {
        var capturedProperty = !context.Capture || end <= start ? null : type.IsProperty ? type.Name : CssPropertyRegistry.GetTypeLonghand(type.Name);
        return capturedProperty is not null ? new CapturePath(new GrammarCapture(-1, start, end, capturedProperty), path) : path;
    }

    /// <summary>Validates the arguments of the functions whose validity is not described by the grammar (the path data of path()).</summary>
    private static bool IsValidFunction(CssFunction function) => !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "path") || CssPath.IsValid(function);

    private static bool IsLiteral(CssComponentValue value, string literal) => value is CssPreservedToken token && literal switch
    {
        "," => token.Type == CssTokenType.Comma,
        ":" => token.Type == CssTokenType.Colon,
        ";" => token.Type == CssTokenType.Semicolon,
        _ => token.Token.Type == CssTokenType.Delim && token.Token.Value == literal,
    };

    private static IEnumerable<(int End, CapturePath? Path)> MatchGroup(GroupNode group, MatchContext context, int position, int depth, bool root, CapturePath? path)
    {
        switch (group.Combinator)
        {
            case GrammarCombinator.OneOf:
                for (var i = 0; i < group.Children.Count; i++)
                {
                    foreach (var (end, childPath) in MatchNode(group.Children[i], context, position, depth + 1, root: false, path))
                    {
                        yield return (end, Capture(root, context, i, position, end, childPath));
                    }
                }

                break;

            case GrammarCombinator.Sequence:
                foreach (var result in MatchSequence(group, context, 0, position, depth, root, path))
                {
                    yield return result;
                }

                break;

            default:
                foreach (var result in MatchAnyOrder(group, context, position, depth, new bool[group.Children.Count], 0, root, path, group.Combinator == GrammarCombinator.AllInAnyOrder))
                {
                    yield return result;
                }

                break;
        }
    }

    private static CapturePath? Capture(bool root, MatchContext context, int index, int start, int end, CapturePath? path) =>
        root && context.Capture && end > start ? new CapturePath(new GrammarCapture(index, start, end), path) : path;

    /// <param name="omitComma">
    /// True when the next comma of the sequence must be omitted (https://drafts.csswg.org/css-values-4/#comb-comma): all the
    /// components before it were omitted, or the last component that was not omitted is a comma.
    /// </param>
    private static IEnumerable<(int End, CapturePath? Path)> MatchSequence(GroupNode group, MatchContext context, int index, int position, int depth, bool root, CapturePath? path, bool omitComma = false)
    {
        if (index == group.Children.Count)
        {
            yield return (position, path);
            yield break;
        }

        var child = group.Children[index];
        var isComma = child is LiteralNode { Literal: "," } && child.Min == 1;
        if (!(isComma && omitComma))
        {
            foreach (var (end, childPath) in MatchNode(child, context, position, depth + 1, root: false, path))
            {
                var nextOmitComma = end == position ? omitComma || index == 0 : isComma;
                foreach (var result in MatchSequence(group, context, index + 1, end, depth, root, Capture(root, context, index, position, end, childPath), nextOmitComma))
                {
                    yield return result;
                }
            }
        }

        if (isComma)
        {
            // The comma is omitted when it must be, or when all the components after it are omitted
            foreach (var result in MatchSequence(group, context, index + 1, position, depth, root, path, omitComma))
            {
                if (omitComma || result.End == position)
                    yield return result;
            }
        }
    }

    private static IEnumerable<(int End, CapturePath? Path)> MatchAnyOrder(GroupNode group, MatchContext context, int position, int depth, bool[] used, int usedCount, bool root, CapturePath? path, bool requireAll)
    {
        for (var i = 0; i < group.Children.Count; i++)
        {
            if (used[i])
                continue;

            foreach (var (end, childPath) in MatchNode(group.Children[i], context, position, depth + 1, root: false, path))
            {
                if (end == position)
                    continue;

                used[i] = true;
                foreach (var result in MatchAnyOrder(group, context, end, depth, used, usedCount + 1, root, Capture(root, context, i, position, end, childPath), requireAll))
                {
                    yield return result;
                }

                used[i] = false;
            }
        }

        // || needs at least one component; && needs all the components that cannot be empty
        if (requireAll ? AllUnusedCanBeEmpty(group, used) : usedCount > 0)
            yield return (position, path);
    }

    private static bool AllUnusedCanBeEmpty(GroupNode group, bool[] used)
    {
        for (var i = 0; i < group.Children.Count; i++)
        {
            if (!used[i] && !CanBeEmpty(group.Children[i]))
                return false;
        }

        return true;
    }

    private static bool CanBeEmpty(GrammarNode node)
    {
        if (node.Min == 0)
            return true;

        if (node is not GroupNode group || group.Required)
            return false;

        return group.Combinator switch
        {
            GrammarCombinator.Sequence or GrammarCombinator.AllInAnyOrder => group.Children.TrueForAll(CanBeEmpty),
            GrammarCombinator.OneOf => group.Children.Exists(CanBeEmpty),
            _ => false,
        };
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Types
    // ----------------------------------------------------------------------------------------------------------------

    private static IEnumerable<int> MatchType(TypeNode type, MatchContext context, int position, int depth)
    {
        var values = context.Values;
        if (type.IsProperty)
        {
            if (CssPropertyRegistry.Get(type.Name) is { } property && CssPropertyRegistry.GetGrammar(property) is { } propertyGrammar)
            {
                foreach (var (end, _) in MatchNode(propertyGrammar, new MatchContext(values, capture: false, percentagesAllowed: AcceptsPercentages(propertyGrammar), keywords: context.Keywords, anchorFunctions: context.ExcludesReferencedAnchorFunctions || IsFlexBasisGrammar(propertyGrammar) ? CssAnchorFunctions.None : GetAnchorFunctions(propertyGrammar), excludesReferencedAnchorFunctions: context.ExcludesReferencedAnchorFunctions || IsFlexBasisGrammar(propertyGrammar), percentagesAreLengths: AcceptsLengthPercentages(propertyGrammar)), position, depth + 1))
                {
                    yield return end;
                }
            }

            yield break;
        }

        // https://drafts.csswg.org/css-values-5/#calc-size: the keyword basis is a keyword of the property (other than none) or any
        if (type.Name == "calc-size()")
        {
            if (position < values.Count && values[position] is CssFunction calcSize && InfraAscii.EqualsIgnoreAsciiCase(calcSize.Name, "calc-size")
                && CssCalc.Evaluate(calcSize, LengthPercentageTypeCheckingContext) is { TypeUnit: "px" } && HasValidCalcSizeBasis(calcSize, context.Keywords))
            {
                yield return position + 1;
            }

            yield break;
        }

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-pos: anchor() and anchor-size(), whose fallback may be another anchor function
        if (type.Name is "anchor()" or "anchor-size()")
        {
            if (position < values.Count && values[position] is CssFunction anchorFunction && InfraAscii.EqualsIgnoreAsciiCase(anchorFunction.Name, type.Name.AsSpan(0, type.Name.Length - 2))
                && CssAnchorFunction.IsValid(anchorFunction, context.AnchorFunctions))
            {
                yield return position + 1;
            }

            yield break;
        }

        // Built-in types
        if (position < values.Count && MatchBuiltInType(type, values[position], context.ExcludedIdents, context.PercentagesAllowed, context.AnchorFunctions, context.PercentagesAreLengths) is { } builtIn)
        {
            if (builtIn)
                yield return position + 1;

            yield break;
        }

        // A math function mixing percentages and dimensions (e.g. calc(50% + 10px) is a <length-percentage>)
        if (position < values.Count && values[position] is CssFunction mathFunction && MathFunctions.Contains(InfraAscii.ToAsciiLowercase(mathFunction.Name))
            && type.Name switch { "length-percentage" => "px", "angle-percentage" => "deg", "time-percentage" => "s", "frequency-percentage" => "hz", _ => null } is { } percentageUnit)
        {
            if (IsMathFunctionOfType(mathFunction, percentageUnit, percentageUnit, context.AnchorFunctions))
                yield return position + 1;

            yield break;
        }

        switch (type.Name)
        {
            case "color":
                // https://drafts.csswg.org/css-color-4/#typedef-color
                if (position < values.Count && IsColor(values[position]))
                    yield return position + 1;

                yield break;

            case "line-names":
                // https://drafts.csswg.org/css-grid/#typedef-line-names: '[' <custom-ident>* ']' (a [] block)
                if (position < values.Count && values[position] is CssSimpleBlock { AssociatedToken: CssTokenType.LeftSquareBracket } block && RemoveWhitespace(block.Value).TrueForAll(IsLineName))
                    yield return position + 1;

                yield break;

            case "declaration-value" or "any-value":
                // Any non-empty sequence
                for (var end = values.Count; end > position; end--)
                {
                    yield return end;
                }

                yield break;
        }

        // Types defined by the specifications
        if (CssPropertyRegistry.GetTypeGrammar(type.GrammarName) is { } grammar)
        {
            foreach (var (end, _) in MatchNode(grammar, new MatchContext(values, capture: false, CustomIdentExclusions.GetValueOrDefault(type.Name) ?? context.ExcludedIdents, context.PercentagesAllowed, context.Keywords, context.AnchorFunctions, context.ExcludesReferencedAnchorFunctions, context.PercentagesAreLengths), position, depth + 1))
            {
                // The range of the type applies to the numeric values (e.g. <length-percentage [0,∞]>)
                if (end == position + 1 && (type.RangeMin is not null || type.RangeMax is not null) && values[position] is CssPreservedToken { Token: { Type: CssTokenType.Number or CssTokenType.Percentage or CssTokenType.Dimension } numericToken }
                    && (numericToken.NumericValue < type.RangeMin || numericToken.NumericValue > type.RangeMax))
                    continue;

                yield return end;
            }

            yield break;
        }

        // Unknown functional notations match the function of the same name, the other unknown types a single component value
        if (type.Name.EndsWith("()", StringComparison.Ordinal))
        {
            if (position < values.Count && values[position] is CssFunction unknownFunction && InfraAscii.EqualsIgnoreAsciiCase(unknownFunction.Name, type.Name.AsSpan(0, type.Name.Length - 2)))
                yield return position + 1;
        }
        else if (position < values.Count && !IsBuiltInTypeName(type.Name))
        {
            yield return position + 1;
        }
    }

    /// <summary>Returns whether the value is a line name (https://drafts.csswg.org/css-grid/#typedef-line-names): a &lt;custom-ident&gt; other than span and auto.</summary>
    public static bool IsLineName(CssComponentValue value) => value is CssPreservedToken { Type: CssTokenType.Ident } ident
        && InfraAscii.ToAsciiLowercase(ident.Token.Value) is not ("span" or "auto" or "default") and var name && !CssWideKeywords.Contains(name);

    /// <summary>Returns whether the function is sibling-index() or sibling-count() (https://drafts.csswg.org/css-values-5/#tree-counting).</summary>
    internal static bool IsTreeCountingFunction(CssFunction function) => InfraAscii.ToAsciiLowercase(function.Name) is "sibling-index" or "sibling-count" && function.Value.TrueForAll(v => v is CssPreservedToken { Type: CssTokenType.Whitespace });

    private static bool IsBuiltInTypeName(string name) => name is "number" or "integer" or "percentage" or "length" or "angle" or "time" or "frequency" or "resolution" or "flex"
        or "dimension" or "string" or "url" or "ident" or "custom-ident" or "dashed-ident" or "hex-color" or "named-color" or "system-color" or "zero" or "opentype-tag";

    /// <summary>Matches a built-in type against a single component value; returns null when the type is not built in.</summary>
    private static bool? MatchBuiltInType(TypeNode type, CssComponentValue value, FrozenSet<string>? excludedIdents, bool percentagesAllowed = true, CssAnchorFunctions anchorFunctions = CssAnchorFunctions.None, bool percentagesAreLengths = false)
    {
        var name = type.Name;
        switch (name)
        {
            case "number" or "integer" or "percentage" or "length" or "angle" or "time" or "frequency" or "resolution" or "flex" or "dimension" or "zero":
                // https://drafts.csswg.org/css-values-5/#tree-counting: sibling-index() and sibling-count() are <integer>s
                if (value is CssFunction treeCounting && InfraAscii.ToAsciiLowercase(treeCounting.Name) is "sibling-index" or "sibling-count" && RemoveWhitespace(treeCounting.Value).Count == 0)
                    return name is "number" or "integer";

                if (value is CssFunction function && MathFunctions.Contains(InfraAscii.ToAsciiLowercase(function.Name)))
                {
                    // A percentage cannot be resolved where percentages are not accepted, even in sign(50%)
                    // (https://drafts.csswg.org/css-values-4/#calc-type-checking): only a number can be computed from
                    // percentages where the grammar accepts percentages (e.g. calc(10% / 20%) in opacity)
                    if (name != "percentage" && (!percentagesAllowed || name is not ("number" or "integer")) && ContainsPercentage(function.Value))
                        return false;

                    return name switch
                    {
                        "zero" => false,
                        "number" or "integer" => IsMathFunctionOfType(function, "", percentageUnit: null) || (percentagesAreLengths && IsMathFunctionOfType(function, "", percentageUnit: "px")),
                        "percentage" => IsMathFunctionOfType(function, "%", percentageUnit: null),
                        "length" => IsMathFunctionOfType(function, "px", percentageUnit: null, anchorFunctions),
                        "angle" => IsMathFunctionOfType(function, "deg", percentageUnit: null),
                        "time" => IsMathFunctionOfType(function, "s", percentageUnit: null),
                        "frequency" => IsMathFunctionOfType(function, "hz", percentageUnit: null),
                        "resolution" => IsMathFunctionOfType(function, "dppx", percentageUnit: null),
                        "flex" => IsMathFunctionOfType(function, "fr", percentageUnit: null),
                        _ => IsMathFunctionOfType(function, expectedUnit: null, percentageUnit: null),
                    };
                }

                if (value is not CssPreservedToken { Token: var token })
                    return false;

                var matches = name switch
                {
                    "number" => token.Type == CssTokenType.Number,
                    "integer" => token.Type == CssTokenType.Number && token.IsInteger,
                    "percentage" => token.Type == CssTokenType.Percentage,
                    "zero" => token.Type == CssTokenType.Number && token.NumericValue == 0,

                    // Unitless zero is a valid length
                    "length" => (token.Type == CssTokenType.Dimension && LengthUnits.Contains(InfraAscii.ToAsciiLowercase(token.Value))) || (token.Type == CssTokenType.Number && token.NumericValue == 0),
                    "angle" => token.Type == CssTokenType.Dimension && AngleUnits.Contains(InfraAscii.ToAsciiLowercase(token.Value)),
                    "time" => token.Type == CssTokenType.Dimension && TimeUnits.Contains(InfraAscii.ToAsciiLowercase(token.Value)),
                    "frequency" => token.Type == CssTokenType.Dimension && FrequencyUnits.Contains(InfraAscii.ToAsciiLowercase(token.Value)),
                    "resolution" => token.Type == CssTokenType.Dimension && ResolutionUnits.Contains(InfraAscii.ToAsciiLowercase(token.Value)),
                    "flex" => token.Type == CssTokenType.Dimension && InfraAscii.EqualsIgnoreAsciiCase(token.Value, "fr"),
                    _ => token.Type == CssTokenType.Dimension,
                };

                if (!matches)
                    return false;

                // The range of the value
                var numeric = token.NumericValue;
                if (type.RangeMin is { } min && numeric < min && !(name == "length" && numeric == 0 && min <= 0))
                    return false;

                if (type.RangeMax is { } max && numeric > max)
                    return false;

                return true;

            case "string":
                return value is CssPreservedToken { Type: CssTokenType.String };

            case "url":
                // The request URL modifiers follow the URL (https://drafts.csswg.org/css-values-5/#request-url-modifiers)
                return value is CssPreservedToken { Type: CssTokenType.Url } || (value is CssFunction urlFunction && InfraAscii.ToAsciiLowercase(urlFunction.Name) is "url" or "src"
                    && RemoveWhitespace(urlFunction.Value) is [CssPreservedToken { Type: CssTokenType.String }, .. var modifiers] && CssUrlModifiers.Canonicalize(modifiers) is not null);

            case "ident":
                return value is CssPreservedToken { Type: CssTokenType.Ident };

            case "custom-ident":
                return value is CssPreservedToken { Type: CssTokenType.Ident } customIdent && !CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(customIdent.Token.Value)) && !InfraAscii.EqualsIgnoreAsciiCase(customIdent.Token.Value, "default")
                    && !(excludedIdents?.Contains(InfraAscii.ToAsciiLowercase(customIdent.Token.Value)) ?? false);

            case "opentype-tag":
                // https://drafts.csswg.org/css-fonts/#typedef-opentype-tag: four ASCII characters in the range U+20–7E
                return value is CssPreservedToken { Type: CssTokenType.String } tag && tag.Token.Value.Length == 4 && tag.Token.Value.All(c => c is >= ' ' and <= '~');

            case "dashed-ident":
                return value is CssPreservedToken { Type: CssTokenType.Ident } dashed && dashed.Token.Value.StartsWith("--", StringComparison.Ordinal);

            case "hex-color":
                return value is CssPreservedToken { Type: CssTokenType.Hash } hash && hash.Token.Value.Length is 3 or 4 or 6 or 8 && IsHex(hash.Token.Value);

            case "named-color":
                return value is CssPreservedToken { Type: CssTokenType.Ident } named && CssColor.IsNamedColor(named.Token.Value);

            case "system-color":
                return value is CssPreservedToken { Type: CssTokenType.Ident } system && CssColor.IsSystemColor(system.Token.Value);

            default:
                return null;
        }
    }

    /// <summary>
    /// Returns whether the type of a math function is the expected type (https://drafts.csswg.org/css-values-4/#calc-type-checking),
    /// given as a canonical unit ("" for &lt;number&gt;, "%" for &lt;percentage&gt;, "px" for &lt;length&gt;...; null for any dimension).
    /// The percentages stand for <paramref name="percentageUnit"/>, or are their own type when it is null.
    /// </summary>
    private static bool IsMathFunctionOfType(CssFunction function, string? expectedUnit, string? percentageUnit, CssAnchorFunctions anchorFunctions = CssAnchorFunctions.None)
    {
        // The math functions the evaluator does not support (e.g. random(), progress(), calc-size()) are not type-checked
        if (CssCalc.GetKind(function.Name) is null)
            return true;

        var context = percentageUnit switch
        {
            "px" when anchorFunctions != CssAnchorFunctions.None => CssAnchorFunction.GetTypeCheckingContext(anchorFunctions),
            null when anchorFunctions != CssAnchorFunctions.None && expectedUnit == "px" => CssAnchorFunction.GetTypeCheckingContext(anchorFunctions),
            null => TypeCheckingContext,
            "px" => LengthPercentageTypeCheckingContext,
            _ => CreateTypeCheckingContext(percentageUnit),
        };

        if (CssCalc.Evaluate(function, context) is not { } result)
            return false;

        return expectedUnit is null ? result.TypeUnit is not ("" or "%") : result.TypeUnit == expectedUnit;
    }

    private static readonly CssCalcContext TypeCheckingContext = CreateTypeCheckingContext(percentageUnit: null);
    private static readonly CssCalcContext LengthPercentageTypeCheckingContext = CreateTypeCheckingContext("px");

    // The relative lengths and the tree counting functions are resolved to arbitrary values: only the type of the result matters
    private static CssCalcContext CreateTypeCheckingContext(string? percentageUnit) => new()
    {
        ResolveDimension = static (value, unit) => CssCalc.IsRelativeUnit(unit) ? (value, "px") : null,
        ResolveTreeCountingFunction = static _ => 1,
        PercentageUnit = percentageUnit,
    };

    /// <summary>Returns whether the values contain a percentage, except in progress() (its arguments have the same type, the result is a number).</summary>
    internal static bool ContainsPercentage(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Percentage }:
                    return true;
                case CssFunction function when !InfraAscii.EqualsIgnoreAsciiCase(function.Name, "progress") && ContainsPercentage(function.Value):
                    return true;
                case CssSimpleBlock block when ContainsPercentage(block.Value):
                    return true;
            }
        }

        return false;
    }

    private static bool HasValidCalcSizeBasis(CssFunction calcSize, HashSet<string>? keywords)
    {
        var basis = RemoveWhitespace(SplitAtCommas(calcSize.Value)[0]);
        return basis switch
        {
            [CssPreservedToken { Type: CssTokenType.Ident } keyword] => InfraAscii.ToAsciiLowercase(keyword.Token.Value) is var name && (name == "any" || (name != "none" && (keywords?.Contains(name) ?? false))),
            [CssFunction nested] when InfraAscii.EqualsIgnoreAsciiCase(nested.Name, "calc-size") => HasValidCalcSizeBasis(nested, keywords),
            _ => true,
        };
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GrammarNode, HashSet<string>> GrammarKeywords = [];

    /// <summary>Returns the keywords of a grammar, outside of its functions.</summary>
    private static HashSet<string> GetKeywords(GrammarNode grammar) => GrammarKeywords.GetValue(grammar, static grammar =>
    {
        var keywords = new HashSet<string>(StringComparer.Ordinal);
        CollectKeywords(grammar, keywords, new HashSet<string>(StringComparer.Ordinal));
        return keywords;
    });

    private static void CollectKeywords(GrammarNode node, HashSet<string> keywords, HashSet<string> visited)
    {
        switch (node)
        {
            case KeywordNode keyword:
                keywords.Add(InfraAscii.ToAsciiLowercase(keyword.Keyword));
                break;

            case GroupNode group:
                foreach (var child in group.Children)
                {
                    CollectKeywords(child, keywords, visited);
                }

                break;

            case TypeNode type when !type.Name.EndsWith("()", StringComparison.Ordinal) && visited.Add(type.IsProperty ? "'" + type.Name : type.Name):
            {
                var grammar = type.IsProperty
                    ? CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null
                    : CssPropertyRegistry.GetTypeGrammar("<" + type.Name + ">");
                if (grammar is not null)
                {
                    CollectKeywords(grammar, keywords, visited);
                }

                break;
            }
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GrammarNode, System.Runtime.CompilerServices.StrongBox<CssAnchorFunctions>> AnchorFunctionGrammars = [];

    /// <summary>Returns whether a grammar is the grammar of <c>flex-basis</c> (whose <c>&lt;'width'&gt;</c> reference does not accept the anchor functions).</summary>
    private static bool IsFlexBasisGrammar(GrammarNode grammar)
        => ReferenceEquals(grammar, FlexBasisGrammars.Value.Standard) || ReferenceEquals(grammar, FlexBasisGrammars.Value.Prefixed);

    private static readonly Lazy<(GrammarNode? Standard, GrammarNode? Prefixed)> FlexBasisGrammars = new(() => (GetPropertyGrammar("flex-basis"), GetPropertyGrammar("-webkit-flex-basis")));

    private static GrammarNode? GetPropertyGrammar(string name) => CssPropertyRegistry.Get(name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null;

    /// <summary>Returns the anchor functions a grammar accepts (outside of its functions): <c>&lt;anchor()&gt;</c> and <c>&lt;anchor-size()&gt;</c>.</summary>
    private static CssAnchorFunctions GetAnchorFunctions(GrammarNode grammar) => AnchorFunctionGrammars.GetValue(grammar, static grammar => new System.Runtime.CompilerServices.StrongBox<CssAnchorFunctions>(CollectAnchorFunctions(grammar))).Value;

    private static CssAnchorFunctions CollectAnchorFunctions(GrammarNode node)
    {
        switch (node)
        {
            case GroupNode group:
                var result = CssAnchorFunctions.None;
                foreach (var child in group.Children)
                {
                    result |= CollectAnchorFunctions(child);
                }

                return result;
            case TypeNode { Name: "anchor()" }:
                return CssAnchorFunctions.Anchor;
            case TypeNode { Name: "anchor-size()" }:
                return CssAnchorFunctions.AnchorSize;
            default:
                return CssAnchorFunctions.None;
        }
    }

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GrammarNode, System.Runtime.CompilerServices.StrongBox<bool>> PercentageGrammars = [];

    /// <summary>Returns whether a grammar accepts a percentage type (outside of its functions).</summary>
    private static bool AcceptsPercentages(GrammarNode grammar) => PercentageGrammars.GetValue(grammar, static grammar => new System.Runtime.CompilerServices.StrongBox<bool>(ContainsPercentageType(grammar, new HashSet<string>(StringComparer.Ordinal), lengthOnly: false))).Value;

    private static readonly System.Runtime.CompilerServices.ConditionalWeakTable<GrammarNode, System.Runtime.CompilerServices.StrongBox<bool>> LengthPercentageGrammars = [];

    /// <summary>Whether the grammar accepts a &lt;length-percentage&gt;: its percentages resolve against lengths, also in the math functions of its other numeric types.</summary>
    private static bool AcceptsLengthPercentages(GrammarNode grammar) => LengthPercentageGrammars.GetValue(grammar, static grammar => new System.Runtime.CompilerServices.StrongBox<bool>(ContainsPercentageType(grammar, new HashSet<string>(StringComparer.Ordinal), lengthOnly: true))).Value;

    private static bool ContainsPercentageType(GrammarNode node, HashSet<string> visited, bool lengthOnly)
    {
        switch (node)
        {
            case GroupNode group:
                foreach (var child in group.Children)
                {
                    if (ContainsPercentageType(child, visited, lengthOnly))
                        return true;
                }

                return false;

            case TypeNode type when !type.Name.EndsWith("()", StringComparison.Ordinal):
            {
                if (!type.IsProperty && (lengthOnly ? type.Name == "length-percentage" : type.Name == "percentage" || type.Name.EndsWith("-percentage", StringComparison.Ordinal)))
                    return true;

                if (!visited.Add(type.IsProperty ? "'" + type.Name : type.Name))
                    return false;

                var grammar = type.IsProperty
                    ? CssPropertyRegistry.Get(type.Name) is { } property ? CssPropertyRegistry.GetGrammar(property) : null
                    : CssPropertyRegistry.GetTypeGrammar("<" + type.Name + ">");
                return grammar is not null && ContainsPercentageType(grammar, visited, lengthOnly);
            }

            default:
                return false;
        }
    }

    private static bool IsHex(string value)
    {
        foreach (var c in value)
        {
            if (!char.IsAsciiHexDigit(c))
                return false;
        }

        return true;
    }

    /// <summary>Returns whether the value is a &lt;color&gt;: a color, currentcolor or a system color.</summary>
    public static bool IsColor(CssComponentValue value)
    {
        if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && (InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "currentcolor") || CssColor.IsSystemColor(ident.Token.Value)))
            return true;

        if (value is CssFunction function)
        {
            var name = InfraAscii.ToAsciiLowercase(function.Name);

            // https://drafts.csswg.org/css-color-5/#relative-colors
            if (CssRelativeColor.GetChannelKeywords(name) is not null && CssRelativeColor.IsRelativeColorSyntax(function))
                return CssRelativeColor.IsValid(function);

            // https://drafts.csswg.org/css-color-5/#color-mix, https://drafts.csswg.org/css-color-6/#color-layers
            if (name is "color-mix" or "color-layers")
                return CssPropertyRegistry.GetTypeGrammar(name + "()") is { } grammar && Matches(grammar, [value]);

            // https://drafts.csswg.org/css-color-5/#light-dark: light-dark(<color>, <color>)
            if (name == "light-dark")
                return Cascade.CssValueComputer.SplitArguments(function.Value) is [[var light], [var dark]] && IsColor(light) && IsColor(dark);

            // https://drafts.csswg.org/css-color-5/#contrast-color: contrast-color(<color>)
            if (name == "contrast-color")
                return RemoveWhitespace(function.Value) is [var background] && IsColor(background);

            // https://drafts.csswg.org/css-color-hdr-1/#hdr-color-function
            if (name == "color-hdr")
                return CssHdrColor.IsValid(function);

            // The math functions of the channels are validated without being resolved
            if (ContainsMathOrRelative(function))
                return name == "device-cmyk" || CssColor.SerializeWithMathFunctions(function) is not null;
        }

        return CssColor.Parse(value) is not null;
    }

    private static bool ContainsMathOrRelative(CssFunction function)
    {
        foreach (var argument in function.Value)
        {
            if (argument is CssFunction nested && MathFunctions.Contains(InfraAscii.ToAsciiLowercase(nested.Name)))
                return true;

            if (argument is CssPreservedToken { Type: CssTokenType.Ident } ident && InfraAscii.EqualsIgnoreAsciiCase(ident.Token.Value, "from"))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether the var(), env(), attr(), inherit(), ident() and &lt;dashed-function&gt;s of the value have a valid syntax:
    /// var(&lt;custom-property-name&gt; [, &lt;declaration-value&gt;?]?), attr(&lt;attr-name&gt; ...), env(&lt;custom-ident&gt; ...),
    /// inherit(&lt;custom-property-name&gt; [, &lt;declaration-value&gt;?]?), ident(&lt;declaration-value&gt;), --*(&lt;declaration-value&gt;#?).
    /// </summary>
    public static bool AreSubstitutionFunctionsValid(IEnumerable<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function:
                {
                    var name = InfraAscii.ToAsciiLowercase(function.Name);
                    if (name == "var")
                    {
                        // https://drafts.csswg.org/css-variables-2/#funcdef-var: var( <declaration-value> , <declaration-value>? ).
                        // The name is only validated at computed-value time (it can be provided by a substitution function), but
                        // it must not be empty, and a {} block must be the whole argument
                        var comma = function.Value.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
                        var nameArgument = RemoveWhitespace(comma < 0 ? function.Value : function.Value[..comma]);
                        if (nameArgument.Count == 0)
                            return false;

                        if (nameArgument.Exists(v => v is CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket }) && (nameArgument.Count != 1 || RemoveWhitespace(((CssSimpleBlock)nameArgument[0]).Value).Count == 0))
                            return false;

                        // The fallback is a <declaration-value>: a ';' or a '!' at its top level is invalid
                        if (comma >= 0 && !CssShorthands.IsValidDeclarationValue(function.Value[(comma + 1)..], topLevel: true))
                            return false;
                    }
                    else if (IsDashedFunction(function.Name))
                    {
                        if (!AreDashedFunctionArgumentsValid(function.Value))
                            return false;
                    }
                    else if (name == "inherit")
                    {
                        // https://drafts.csswg.org/css-values-5/#inherit-notation
                        var stream = RemoveWhitespace(function.Value);
                        if (stream is not [CssPreservedToken { Type: CssTokenType.Ident } inheritName, ..] || !CssPropertyRegistry.IsCustomProperty(inheritName.Token.Value)
                            || (stream.Count > 1 && stream[1] is not CssPreservedToken { Type: CssTokenType.Comma }))
                            return false;
                    }
                    else if (name == "random-item")
                    {
                        // https://drafts.csswg.org/css-values-5/#funcdef-random-item: random-item( <random-key> , [ <declaration-value>? ]# ),
                        // the key is validated when it is substituted; an item that contains a {} block is a {} block
                        var items = SplitAtCommas(function.Value);
                        if (items.Count < 2 || RemoveWhitespace(items[0]).Count == 0)
                            return false;

                        foreach (var item in items.Skip(1))
                        {
                            var significantItem = RemoveWhitespace(item);
                            if (!CssShorthands.IsValidDeclarationValue(item, topLevel: true)
                                || (significantItem.Exists(v => v is CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket }) && significantItem.Count != 1))
                                return false;
                        }
                    }
                    else if (name == "if")
                    {
                        // https://drafts.csswg.org/css-values-5/#if-notation: the branches are validated when they are substituted
                        if (RemoveWhitespace(function.Value).Count == 0)
                            return false;
                    }
                    else if (name == "ident")
                    {
                        // https://drafts.csswg.org/css-values-5/#typedef-ident-args: the arguments are validated when they are substituted,
                        // but they are not separated by commas and are not {} blocks
                        var identArguments = RemoveWhitespace(function.Value);
                        if (identArguments.Count == 0 || identArguments.Exists(v => v is CssPreservedToken { Type: CssTokenType.Comma } or CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket }))
                            return false;
                    }
                    else if (name == "env")
                    {
                        // https://drafts.csswg.org/css-env-1/#env-function: env( <custom-ident> <integer [0,∞]>* , <declaration-value>? )
                        var comma = function.Value.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
                        var nameArgument = RemoveWhitespace(comma < 0 ? function.Value : function.Value[..comma]);
                        if (nameArgument is not [CssPreservedToken { Type: CssTokenType.Ident }, ..])
                            return false;

                        for (var i = 1; i < nameArgument.Count; i++)
                        {
                            if (nameArgument[i] is not CssPreservedToken { Type: CssTokenType.Number, Token: { IsInteger: true, NumericValue: >= 0 } })
                                return false;
                        }
                    }
                    else if (name == "attr")
                    {
                        // attr() accepts a namespace prefix (attr(|name), attr(ns|name))
                        if (RemoveWhitespace(function.Value).Count == 0)
                            return false;
                    }

                    if (!AreSubstitutionFunctionsValid(function.Value))
                        return false;

                    break;
                }

                case CssSimpleBlock block:
                    if (!AreSubstitutionFunctionsValid(block.Value))
                        return false;

                    break;
            }
        }

        return true;
    }

    /// <summary>
    /// Returns whether the value contains an arbitrary substitution function (https://drafts.csswg.org/css-values-5/#arbitrary-substitution):
    /// var(), env(), attr(), inherit(), ident(), if(), random-item() or a &lt;dashed-function&gt; (https://drafts.csswg.org/css-mixins-1/#typedef-dashed-function).
    /// </summary>
    public static bool ContainsSubstitutionFunction(IEnumerable<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function:
                    if (InfraAscii.ToAsciiLowercase(function.Name) is "var" or "env" or "attr" or "inherit" or "ident" or "if" or "random-item" || IsDashedFunction(function.Name))
                        return true;

                    if (ContainsSubstitutionFunction(function.Value))
                        return true;

                    break;
                case CssSimpleBlock block:
                    if (ContainsSubstitutionFunction(block.Value))
                        return true;

                    break;
            }
        }

        return false;
    }

    /// <summary>Splits component values at the top-level commas.</summary>
    public static List<List<CssComponentValue>> SplitAtCommas(List<CssComponentValue> values)
    {
        var result = new List<List<CssComponentValue>> { new() };
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add([]);
            }
            else
            {
                result[^1].Add(value);
            }
        }

        return result;
    }

    /// <summary>Removes the leading and trailing whitespace of component values.</summary>
    public static List<CssComponentValue> TrimWhitespace(List<CssComponentValue> values)
    {
        var start = values.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        if (start < 0)
            return [];

        var end = values.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        return values[start..(end + 1)];
    }

    /// <summary>Returns whether a function is a &lt;dashed-function&gt;: a call of a custom function (https://drafts.csswg.org/css-mixins-1/#typedef-dashed-function).</summary>
    public static bool IsDashedFunction(string name) => name.StartsWith("--", StringComparison.Ordinal);

    /// <summary>
    /// Returns whether the arguments of a &lt;dashed-function&gt; are a valid &lt;declaration-value&gt;#?: the arguments are not
    /// empty, and an argument that contains a {} block is a {} block whose contents are not empty
    /// (https://drafts.csswg.org/css-values-5/#component-function-commas).
    /// </summary>
    private static bool AreDashedFunctionArgumentsValid(List<CssComponentValue> values)
    {
        if (RemoveWhitespace(values).Count == 0)
            return true;

        foreach (var argument in SplitAtCommas(values))
        {
            var significant = RemoveWhitespace(argument);
            if (significant.Count == 0)
                return false;

            // <dashed-ident> : is reserved for the named arguments (https://github.com/w3c/csswg-drafts/issues/11749)
            if (significant is [CssPreservedToken { Type: CssTokenType.Ident } argumentName, CssPreservedToken { Type: CssTokenType.Colon }, ..] && CssPropertyRegistry.IsCustomProperty(argumentName.Token.Value))
                return false;

            if (significant is [CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket } block])
            {
                if (RemoveWhitespace(block.Value).Count == 0 || !CssShorthands.IsValidDeclarationValue(block.Value, topLevel: true))
                    return false;
            }
            else if (significant.Exists(v => v is CssSimpleBlock { AssociatedToken: CssTokenType.LeftCurlyBracket }) || !CssShorthands.IsValidDeclarationValue(argument, topLevel: true))
            {
                return false;
            }
        }

        return true;
    }
}
