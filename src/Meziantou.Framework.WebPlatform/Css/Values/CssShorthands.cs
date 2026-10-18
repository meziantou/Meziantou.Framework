using System.Collections.Concurrent;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>The value of a longhand, as set by a declaration.</summary>
/// <param name="Name">The name of the longhand.</param>
/// <param name="Value">The serialized value (empty when the value is only known through the shorthand).</param>
/// <param name="ShorthandName">
/// The shorthand that set the longhand when its value is a pending-substitution value
/// (https://drafts.csswg.org/css-variables/#pending-substitution-value) or when the shorthand cannot be expanded.
/// </param>
/// <param name="ShorthandValue">The serialized value of the shorthand.</param>
/// <param name="ComputationValue">
/// The value the computed value is computed from, when it differs from the serialized specified value (see <see cref="CssSerializer.SerializeValueForComputation"/>).
/// </param>
/// <param name="TypedValue">
/// The value as specified, when its text differs from the serialized value: the Typed OM reifies it, since the serialization
/// does not keep the structure of every value (e.g. the percentage of <c>opacity: 50%</c>, serialized as <c>0.5</c>, or the
/// keyword <c>step-start</c>, serialized as <c>steps(1, start)</c>).
/// </param>
/// <param name="TypedValueIsRepresentation">
/// Whether <paramref name="TypedValue"/> is the internal representation of keywords, numeric values or transform values
/// set through the Typed OM (https://drafts.css-houdini.org/css-typed-om-1/#create-an-internal-representation): they are
/// reified as they were set (e.g. the math functions whose units are canonical are not simplified).
/// </param>
internal readonly record struct CssLonghandValue(string Name, string Value, string? ShorthandName = null, string? ShorthandValue = null, string? ComputationValue = null, string? TypedValue = null, bool TypedValueIsRepresentation = false);

/// <summary>Parses the values of properties, expands the shorthands and serializes them (https://drafts.csswg.org/cssom/#serialize-a-css-value).</summary>
internal static class CssShorthands
{
    private static readonly ConcurrentDictionary<string, string?> InitialValues = new(StringComparer.Ordinal);

    /// <summary>The values of the longhands omitted from a shorthand, when it is not their initial value (https://drafts.csswg.org/css-flexbox/#flex-property).</summary>
    private static readonly Dictionary<(string Shorthand, string Longhand), string> OmittedValues = new()
    {
        [("flex", "flex-grow")] = "1",
        [("flex", "flex-shrink")] = "1",
        [("flex", "flex-basis")] = "0%",

        // https://drafts.csswg.org/css-inline-3/#text-box-shorthand: "omitting the text-box-trim value sets it to trim-both (not the initial value)"
        [("text-box", "text-box-trim")] = "trim-both",
    };

    /// <summary>The expansion of the keywords of shorthands that are not the keyword applied to all the longhands.</summary>
    private static readonly Dictionary<(string Shorthand, string Keyword), string[]> KeywordExpansions = new()
    {
        [("flex", "none")] = ["0", "0", "auto"],
        [("flex", "auto")] = ["1", "1", "auto"],

        // https://drafts.csswg.org/css-text-4/#white-space-property
        [("white-space", "normal")] = ["collapse", "wrap"],
        [("white-space", "pre")] = ["preserve", "nowrap"],
        [("white-space", "nowrap")] = ["collapse", "nowrap"],
        [("white-space", "pre-wrap")] = ["preserve", "wrap"],
        [("white-space", "break-spaces")] = ["break-spaces", "wrap"],
        [("white-space", "pre-line")] = ["preserve-breaks", "wrap"],

        // https://drafts.csswg.org/css-text-4/#text-spacing-property
        [("text-spacing", "none")] = ["space-all", "no-autospace"],
        [("text-spacing", "auto")] = ["auto", "auto"],

        // https://drafts.csswg.org/css-inline-3/#valdef-text-box-normal: "it sets text-box-trim to none and text-box-edge to auto"
        [("text-box", "normal")] = ["none", "auto"],

        // https://drafts.csswg.org/css-text-4/#text-align-property: text-align-last is auto, except for justify-all and match-parent
        [("text-align", "start")] = ["start", "auto"],
        [("text-align", "end")] = ["end", "auto"],
        [("text-align", "left")] = ["left", "auto"],
        [("text-align", "right")] = ["right", "auto"],
        [("text-align", "center")] = ["center", "auto"],
        [("text-align", "justify")] = ["justify", "auto"],
        [("text-align", "match-parent")] = ["match-parent", "match-parent"],
        [("text-align", "justify-all")] = ["justify", "justify"],
    };

    /// <summary>The shorthands that are serialized in their complete form (e.g. flex: 0 1 0px).</summary>
    private static readonly HashSet<string> CompleteForm = new(StringComparer.Ordinal) { "flex" };

    private static int GetResetOnlyCount(CssPropertyDefinition shorthand) => CssPropertyRegistry.GetResetOnlyLonghands(shorthand.Name).Length;

    /// <summary>
    /// Parses the value of a property (https://drafts.csswg.org/cssom/#parse-a-css-value) and returns the values of the
    /// longhands (a longhand returns itself), or null when the value is invalid.
    /// </summary>
    public static List<CssLonghandValue>? ParseValue(string name, List<CssComponentValue> values, string? originalText)
    {
        if (CssPropertyRegistry.IsCustomProperty(name))
            return ParseCustomPropertyValue(name, values, originalText);

        var property = CssPropertyRegistry.Get(name);
        if (property is null)
            return null;

        var significant = ValueMatcher.RemoveWhitespace(values);
        if (significant.Count == 0 || !IsValidDeclarationValue(values, topLevel: true))
            return null;

        // CSS-wide keywords apply to all the longhands
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } keywordToken] && ValueMatcher.CssWideKeywords.Contains(InfraAscii.ToAsciiLowercase(keywordToken.Token.Value)))
        {
            var keyword = InfraAscii.ToAsciiLowercase(keywordToken.Token.Value);
            return property.IsShorthand ? [.. CssPropertyRegistry.GetLonghandsRecursive(property).Select(l => new CssLonghandValue(l, keyword))] : [new CssLonghandValue(property.Name, keyword)];
        }

        // https://drafts.csswg.org/css-variables/#variables-in-shorthands
        if (ValueMatcher.ContainsSubstitutionFunction(significant))
        {
            // The value is kept as specified (https://drafts.csswg.org/css-variables/#serializing-custom-props)
            if (!ValueMatcher.AreSubstitutionFunctionsValid(significant))
                return null;

            var text = originalText ?? CssSerializer.SerializeValue(values, property);
            return property.IsShorthand
                ? [.. CssPropertyRegistry.GetLonghandsRecursive(property).Select(l => new CssLonghandValue(l, "", property.Name, text))]
                : [new CssLonghandValue(property.Name, text)];
        }

        // The random() functions get their random caching key (https://drafts.csswg.org/css-values-5/#random-caching-key)
        if (CssRandom.AssignCachingKeys(property.Name, values) is var keyed && !ReferenceEquals(keyed, values))
        {
            values = keyed;
            significant = ValueMatcher.RemoveWhitespace(values);
        }

        if (!property.IsShorthand)
        {
            // Like the browsers, the d property treats an empty path (path("")) as none
            if (property.Name == "d" && CssPath.IsEmpty(significant))
                return [new CssLonghandValue(property.Name, "none")];

            var grammar = CssPropertyRegistry.GetGrammar(property);
            if (grammar is not null && (!ValueMatcher.Matches(grammar, significant) || !CssPropertyRules.IsValid(property, significant)))
                return null;

            var grid = CssGrid.CanonicalizeLonghand(property, significant);
            if (grid is { Length: 0 })
                return null;

            var computationValue = CssSerializer.SerializeValueForComputation(values, property);
            if (computationValue is null && IsEasingProperty(property.Name))
            {
                // The animations use the easing functions with the full precision of their numbers
                computationValue = CssSerializer.SerializeValuePreservingNumbers(values, property);
            }

            if (computationValue is null && CssSerializer.ContainsConvertedDimension(significant))
            {
                // The units converted by the serialization of the math functions are rounded: the computed value is computed
                // from the values with their full precision (100dpi - 20dpi is 0.833333dppx, not 1.041667dppx - 0.208333dppx)
                var precise = CssSerializer.WithPreservedNumbers(() => CssSerializer.SerializeValue(values, property));
                if (!string.Equals(precise, CssSerializer.SerializeValue(values, property), StringComparison.Ordinal))
                {
                    computationValue = precise;
                }
            }

            var serialized = grid ?? CssPropertyRules.CanonicalizeLonghand(property, significant) ?? CssBackgrounds.CanonicalizeLonghand(property.Name, significant) ?? SerializeLonghand(property, grammar, values, significant);

            // The Typed OM reifies the value as specified: the serialization does not always keep its structure
            var typedValue = originalText is not null && !string.Equals(originalText, serialized, StringComparison.OrdinalIgnoreCase) ? originalText : null;
            return [new CssLonghandValue(property.Name, serialized, ComputationValue: computationValue, TypedValue: typedValue)];
        }

        if (!CssPropertyRules.IsValid(property, significant))
            return null;

        var longhands = Expand(property, values, significant);
        if (longhands is not null && property.Name is "animation" or "transition" && CssSerializer.ContainsRoundedNumber(significant))
        {
            // The easing functions keep the full precision of their numbers (see above)
            var precise = CssSerializer.WithPreservedNumbers(() => Expand(property, values, significant));
            for (var i = 0; i < longhands.Count; i++)
            {
                if (IsEasingProperty(longhands[i].Name) && precise?.Find(l => l.Name == longhands[i].Name) is { Value: { } preciseValue } && preciseValue != longhands[i].Value)
                {
                    longhands[i] = longhands[i] with { ComputationValue = preciseValue };
                }
            }
        }

        return longhands;
    }

    private static bool IsEasingProperty(string name) => name is "animation-timing-function" or "transition-timing-function";

    /// <summary>Serializes the specified value of a longhand, its components in the canonical order of the grammar.</summary>
    private static string SerializeLonghand(CssPropertyDefinition property, GrammarNode? grammar, List<CssComponentValue> values, List<CssComponentValue> significant)
    {
        if (grammar is not null && CssCanonicalOrder.Reorder(property, grammar, significant) is { } reordered)
            return CssSerializer.SerializeValue(InsertSpaces(reordered), property);

        return CssSerializer.SerializeValue(values, property);
    }

    /// <summary>
    /// Parses the value of a custom property, or of a descriptor that accepts any value like a custom property (the result
    /// descriptor of @function): the value is kept as specified.
    /// </summary>
    internal static List<CssLonghandValue>? ParseCustomPropertyValue(string name, List<CssComponentValue> values, string? originalText)
    {
        if (!IsValidDeclarationValue(values, topLevel: true))
            return null;

        if (ValueMatcher.ContainsSubstitutionFunction(values) && !ValueMatcher.AreSubstitutionFunctionsValid(values))
            return null;

        return [new CssLonghandValue(name, originalText ?? CssSerializer.SerializeValue(values, property: null))];
    }

    /// <summary>
    /// Returns whether the values are a valid &lt;declaration-value&gt; (https://drafts.csswg.org/css-syntax-3/#typedef-declaration-value):
    /// no bad tokens, no unmatched closing brackets, no top-level semicolons or "!".
    /// </summary>
    internal static bool IsValidDeclarationValue(List<CssComponentValue> values, bool topLevel)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Token: var token }:
                    if (token.Type is CssTokenType.BadString or CssTokenType.BadUrl or CssTokenType.RightParenthesis or CssTokenType.RightSquareBracket or CssTokenType.RightCurlyBracket)
                        return false;

                    if (topLevel && (token.Type == CssTokenType.Semicolon || token.IsDelim('!')))
                        return false;

                    break;
                case CssFunction function:
                    if (!IsValidDeclarationValue(function.Value, topLevel: false))
                        return false;

                    break;
                case CssSimpleBlock block:
                    if (!IsValidDeclarationValue(block.Value, topLevel: false))
                        return false;

                    break;
            }
        }

        return true;
    }

    /// <summary>Returns the serialized initial value of a longhand, or null when it is not known.</summary>
    public static string? GetInitialValue(string longhand)
    {
        return InitialValues.GetOrAdd(longhand, static name =>
        {
            if (CssPropertyRegistry.Get(name) is not { } property || property.Initial is not { Length: > 0 } initial)
                return null;

            var values = CssParser.ParseListOfComponentValues(initial);
            var significant = ValueMatcher.RemoveWhitespace(values);
            if (significant.Count == 0)
                return null;

            // The initial value of some properties is a description ("see individual properties", "depends on user agent"...)
            if (CssPropertyRegistry.GetGrammar(property) is { } grammar && !ValueMatcher.Matches(grammar, significant))
                return null;

            return CssSerializer.SerializeValue(values, property);
        });
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Expansion
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Returns the node of the grammar, ignoring the groups that only contain one node without multiplier.</summary>
    private static GrammarNode Unwrap(GrammarNode node)
    {
        while (node is GroupNode { Children: [var child] } group && group.Min == 1 && group.Max == 1)
        {
            node = child;
        }

        return node;
    }

    /// <summary>Returns whether the shorthand sets its longhands like the box properties (margin: top right bottom left).</summary>
    private static bool IsBoxShorthand(CssPropertyDefinition shorthand, GrammarNode grammar)
    {
        var node = Unwrap(grammar);
        return node.Min == 1 && !node.CommaSeparated && node.Max == shorthand.Longhands.Length && node.Max is 2 or 4;
    }

    /// <summary>
    /// Splits the value of a box shorthand (see <see cref="IsBoxShorthand"/>) into the values of its sides: one component value
    /// per side, or, when the repeated item of the grammar is a sequence, the split of the components that matches the grammars
    /// of the longhands of a two-sided shorthand. Returns null when the value cannot be split.
    /// </summary>
    private static List<List<CssComponentValue>>? SplitSides(CssPropertyDefinition shorthand, GrammarNode grammar, List<CssComponentValue> significant)
    {
        if (Unwrap(grammar) is not GroupNode { Combinator: GrammarCombinator.Sequence, Children.Count: > 1 } || shorthand.Longhands.Length != 2)
        {
            return significant.Count > shorthand.Longhands.Length ? null : significant.Select(v => new List<CssComponentValue> { v }).ToList();
        }

        if (CssPropertyRegistry.Get(shorthand.Longhands[0]) is not { } first || CssPropertyRegistry.GetGrammar(first) is not { } firstGrammar
            || CssPropertyRegistry.Get(shorthand.Longhands[1]) is not { } second || CssPropertyRegistry.GetGrammar(second) is not { } secondGrammar)
        {
            return null;
        }

        for (var count = 1; count <= significant.Count; count++)
        {
            var firstValues = significant.GetRange(0, count);
            if (!ValueMatcher.Matches(firstGrammar, firstValues))
                continue;

            if (count == significant.Count)
                return [firstValues];

            var secondValues = significant.GetRange(count, significant.Count - count);
            if (ValueMatcher.Matches(secondGrammar, secondValues))
                return [firstValues, secondValues];
        }

        return null;
    }

    private static List<CssLonghandValue>? Expand(CssPropertyDefinition shorthand, List<CssComponentValue> values, List<CssComponentValue> significant)
    {
        var grammar = CssPropertyRegistry.GetGrammar(shorthand);
        if (grammar is null)
            return Opaque(shorthand, values);

        var captures = new List<GrammarCapture>();
        if (!ValueMatcher.MatchAll(grammar, significant, captures))
            return null;

        if (CssBackgrounds.IsLayered(shorthand.Name))
            return CssBackgrounds.Expand(shorthand, significant) ?? Opaque(shorthand, values);

        if (CssGrid.IsShorthand(shorthand.Name))
            return CssGrid.Expand(shorthand, significant);

        if (CssGaps.IsShorthand(shorthand))
            return CssGaps.Expand(shorthand, values, significant);

        if (CssPropertyRules.IsShorthand(shorthand.Name))
            return CssPropertyRules.Expand(shorthand, significant) ?? Opaque(shorthand, values);

        var assigned = new Dictionary<string, List<CssComponentValue>>(StringComparer.Ordinal);

        // A keyword alternative of the shorthand (e.g. font-variant: none, marker: none)
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } keywordToken] && Unwrap(grammar) is GroupNode { Combinator: GrammarCombinator.OneOf } oneOf
            && oneOf.Children.Exists(c => c is KeywordNode k && keywordToken.Token.IsIdent(k.Keyword)))
        {
            var keyword = InfraAscii.ToAsciiLowercase(keywordToken.Token.Value);
            if (KeywordExpansions.TryGetValue((shorthand.Name, keyword), out var expansion))
                return [.. shorthand.Longhands.Select((l, i) => new CssLonghandValue(l, expansion[i]))];

            var keywordResult = new List<CssLonghandValue>();
            foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
            {
                var longhandProperty = CssPropertyRegistry.Get(longhand);
                if (longhandProperty is not null && CssPropertyRegistry.GetGrammar(longhandProperty) is { } longhandGrammar && ValueMatcher.Matches(longhandGrammar, significant))
                {
                    keywordResult.Add(new CssLonghandValue(longhand, keyword));
                }
                else if (GetInitialValue(longhand) is { } initialValue)
                {
                    keywordResult.Add(new CssLonghandValue(longhand, initialValue));
                }
                else
                {
                    return Opaque(shorthand, values);
                }
            }

            return keywordResult;
        }

        if (IsBoxShorthand(shorthand, grammar))
        {
            // Each component value is one side (the sides are single component values: lengths, colors, keywords, math functions...),
            // except when a side is a sequence (contain-intrinsic-size: [ auto? [ none | <length> ] ]{1,2})
            if (SplitSides(shorthand, grammar, significant) is not { } sides)
                return Opaque(shorthand, values);

            if (shorthand.Longhands.Length == 4)
            {
                // https://drafts.csswg.org/css-box-4/#margin-shorthand: top, right (= top), bottom (= top), left (= right)
                var top = sides[0];
                var right = sides.Count > 1 ? sides[1] : top;
                var bottom = sides.Count > 2 ? sides[2] : top;
                var left = sides.Count > 3 ? sides[3] : right;
                assigned[shorthand.Longhands[0]] = top;
                assigned[shorthand.Longhands[1]] = right;
                assigned[shorthand.Longhands[2]] = bottom;
                assigned[shorthand.Longhands[3]] = left;
            }
            else
            {
                assigned[shorthand.Longhands[0]] = sides[0];
                assigned[shorthand.Longhands[1]] = sides.Count > 1 ? sides[1] : sides[0];
            }
        }
        else if (!AssignCaptures(shorthand, grammar, significant, captures, assigned))
        {
            return Opaque(shorthand, values);
        }

        var result = new List<CssLonghandValue>();
        foreach (var longhand in shorthand.Longhands)
        {
            var longhandProperty = CssPropertyRegistry.Get(longhand);
            if (longhandProperty is null)
                continue;

            if (longhandProperty.IsShorthand)
            {
                if (assigned.TryGetValue(longhand, out var shorthandValues))
                {
                    var expanded = Expand(longhandProperty, shorthandValues, shorthandValues);
                    if (expanded is null || expanded.Exists(e => e.ShorthandName is not null))
                        return Opaque(shorthand, values);

                    result.AddRange(expanded);
                }
                else
                {
                    foreach (var nested in CssPropertyRegistry.GetLonghandsRecursive(longhandProperty))
                    {
                        if (!AddLonghand(result, nested, assigned))
                            return Opaque(shorthand, values);
                    }
                }
            }
            else if (!assigned.ContainsKey(longhand) && OmittedValues.TryGetValue((shorthand.Name, longhand), out var omittedValue))
            {
                result.Add(new CssLonghandValue(longhand, omittedValue));
            }
            else if (!AddLonghand(result, longhand, assigned))
            {
                return Opaque(shorthand, values);
            }
        }

        return result;
    }

    private static bool AddLonghand(List<CssLonghandValue> result, string longhand, Dictionary<string, List<CssComponentValue>> assigned)
    {
        if (assigned.TryGetValue(longhand, out var longhandValues))
        {
            var property = CssPropertyRegistry.Get(longhand);
            var grammar = property is null ? null : CssPropertyRegistry.GetGrammar(property);

            // The value matched by the grammar of the shorthand must be valid for the longhand (e.g. the <color> of border-top)
            if (grammar is not null && !ValueMatcher.Matches(grammar, longhandValues))
                return false;

            // The canonical form of the longhand (e.g. the offset-path of offset)
            if (property is not null && CssPropertyRules.CanonicalizeLonghand(property, longhandValues) is { } canonical)
            {
                result.Add(new CssLonghandValue(longhand, canonical));
                return true;
            }

            var ordered = grammar is null ? null : CssCanonicalOrder.Reorder(property!, grammar, longhandValues);
            result.Add(new CssLonghandValue(longhand, CssSerializer.SerializeValue(InsertSpaces(ordered ?? longhandValues), property)));
            return true;
        }

        // https://drafts.csswg.org/css-cascade-5/#shorthand: the omitted longhands are set to their initial value
        var initial = GetInitialValue(longhand);
        if (initial is null)
            return false;

        result.Add(new CssLonghandValue(longhand, initial));
        return true;
    }

    /// <summary>Inserts whitespace between the significant component values, so that they are serialized separated by a space.</summary>
    internal static List<CssComponentValue> InsertSpaces(List<CssComponentValue> values)
    {
        if (values.Count <= 1)
            return values;

        var result = new List<CssComponentValue>(values.Count * 2);
        foreach (var value in values)
        {
            if (result.Count > 0)
            {
                result.Add(new CssPreservedToken(new CssToken { Type = CssTokenType.Whitespace, Value = " " }));
            }

            result.Add(value);
        }

        return result;
    }

    /// <summary>Maps the component values matched by the grammar to the longhands.</summary>
    private static bool AssignCaptures(CssPropertyDefinition shorthand, GrammarNode grammar, List<CssComponentValue> values, List<GrammarCapture> captures, Dictionary<string, List<CssComponentValue>> assigned)
    {
        var longhands = new HashSet<string>(shorthand.Longhands, StringComparer.Ordinal);
        foreach (var longhand in CssPropertyRegistry.GetLonghandsRecursive(shorthand))
        {
            longhands.Add(longhand);
        }

        // The property references (<'flex-grow'>) name the longhand
        foreach (var capture in captures)
        {
            if (capture.Property is null)
                continue;

            var name = CssPropertyRegistry.Get(capture.Property)?.Name ?? capture.Property;

            // e.g. <'border-block-start'> for border-block and border-inline: the reference is a shorthand with the same
            // grammar as the longhands, which are all set to the value (even when it is one of them)
            if ((!longhands.Contains(name) || shorthand.Longhands.Length > 1) && captures.Count(c => c.Property is not null) == 1 && shorthand.Longhands.All(l => CssPropertyRegistry.Get(l) is { IsShorthand: true }))
            {
                foreach (var longhand in shorthand.Longhands)
                {
                    assigned[longhand] = values[capture.Start..capture.End];
                }

                continue;
            }

            if (!longhands.Contains(name))
                return false;

            if (assigned.ContainsKey(name))
                return false;

            assigned[name] = values[capture.Start..capture.End];
        }

        // A group alternative of the root whose components are the longhands (e.g. font-variant: normal | none | [ <ligatures> || <caps> || ... ])
        if (grammar is GroupNode { Combinator: GrammarCombinator.OneOf } alternatives && captures.Count(c => c.ChildIndex >= 0) == 1)
        {
            var alternative = captures.First(c => c.ChildIndex >= 0);
            if (alternatives.Children[alternative.ChildIndex] is GroupNode { Combinator: not GrammarCombinator.OneOf } group && IsLonghandGroup(shorthand, group) && !ContainsPropertyReference(group))
            {
                var alternativeValues = values[alternative.Start..alternative.End];
                var alternativeCaptures = new List<GrammarCapture>();
                return ValueMatcher.MatchAll(group, alternativeValues, alternativeCaptures) && AssignCaptures(shorthand, group, alternativeValues, alternativeCaptures, assigned);
            }
        }

        // The other components of the root group are mapped to the longhands by index (e.g. border-top: <line-width> || <line-style> || <color>)
        if (grammar is GroupNode root)
        {
            foreach (var capture in captures)
            {
                if (capture.Property is not null || capture.ChildIndex < 0)
                    continue;

                var child = root.Children[capture.ChildIndex];
                if (ContainsPropertyReference(child))
                    continue;

                // The reset-only longhands are not in the grammar (e.g. border-image for border)
                if (root.Children.Count != shorthand.Longhands.Length - GetResetOnlyCount(shorthand) || child is LiteralNode)
                    return false;

                var longhand = shorthand.Longhands[capture.ChildIndex];
                if (assigned.ContainsKey(longhand))
                    return false;

                assigned[longhand] = values[capture.Start..capture.End];
            }

            // gap: <'row-gap'> <'column-gap'>?: the omitted second value is copied from the first
            if (root.Combinator == GrammarCombinator.Sequence && shorthand.Longhands.Length == 2 && root.Children.Count == 2 && root.Children.TrueForAll(c => c is TypeNode { IsProperty: true })
                && assigned.TryGetValue(shorthand.Longhands[0], out var first) && !assigned.ContainsKey(shorthand.Longhands[1]))
            {
                assigned[shorthand.Longhands[1]] = first;
            }
        }

        return assigned.Count > 0;
    }

    /// <summary>Returns whether the components of the group are the longhands of the shorthand, in order.</summary>
    private static bool IsLonghandGroup(CssPropertyDefinition shorthand, GroupNode group) => group.Children.Count == shorthand.Longhands.Length - GetResetOnlyCount(shorthand);

    private static bool ContainsPropertyReference(GrammarNode node) => node switch
    {
        TypeNode type => type.IsProperty,
        GroupNode group => group.Children.Exists(ContainsPropertyReference),
        _ => false,
    };

    /// <summary>
    /// The shorthand is valid but cannot be expanded: the longhands keep a reference to the value of the shorthand, like
    /// pending-substitution values.
    /// </summary>
    private static List<CssLonghandValue> Opaque(CssPropertyDefinition shorthand, List<CssComponentValue> values)
    {
        var text = CssSerializer.SerializeValue(values, shorthand);
        return [.. CssPropertyRegistry.GetLonghandsRecursive(shorthand).Select(l => new CssLonghandValue(l, "", shorthand.Name, text))];
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Serialization
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Serializes a shorthand from the values of its longhands (https://drafts.csswg.org/cssom/#serialize-a-css-value);
    /// returns the empty string when the values cannot be represented by the shorthand.
    /// </summary>
    public static string Serialize(CssPropertyDefinition shorthand, Func<string, CssLonghandValue?> getLonghand)
    {
        var longhands = CssPropertyRegistry.GetLonghandsRecursive(shorthand);
        var values = new List<CssLonghandValue>(longhands.Count);
        foreach (var longhand in longhands)
        {
            if (getLonghand(longhand) is not { } value)
                return "";

            values.Add(value);
        }

        if (values.Count == 0)
            return "";

        // Pending-substitution values: only the shorthand that set them can be serialized
        if (values[0].ShorthandName is { } pendingShorthand)
        {
            var pendingValue = values[0].ShorthandValue;
            foreach (var value in values)
            {
                if (value.ShorthandName != pendingShorthand || value.ShorthandValue != pendingValue)
                    return "";
            }

            return pendingShorthand == shorthand.Name ? pendingValue ?? "" : "";
        }

        foreach (var value in values)
        {
            if (value.ShorthandName is not null || value.Value.Length == 0)
                return "";
        }

        // CSS-wide keywords: all the longhands must have the same keyword
        var firstKeyword = ValueMatcher.CssWideKeywords.Contains(values[0].Value) ? values[0].Value : null;
        foreach (var value in values)
        {
            var isKeyword = ValueMatcher.CssWideKeywords.Contains(value.Value);
            if (isKeyword != (firstKeyword is not null) || (isKeyword && value.Value != firstKeyword))
                return "";

            // Variable references in longhands cannot be represented by the shorthand
            if (Cascade.CssSubstitution.MayContainSubstitution(value.Value))
                return "";
        }

        if (firstKeyword is not null)
            return firstKeyword;

        var lookup = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);
        return SerializeFromLonghands(shorthand, lookup) ?? "";
    }

    /// <summary>Returns whether the value of the shorthand sets its longhands to the values of the lookup.</summary>
    private static bool RoundTrips(CssPropertyDefinition shorthand, string text, Dictionary<string, string> lookup)
    {
        // The same candidates are serialized for many declaration blocks (e.g. margin: 0)
        var cache = text.Length <= ParsedValueCache.MaxTextLength ? ParsedValueCache.SerializedShorthands : null;
        if (cache is null || !cache.TryGetValue(atRule: null, shorthand.Name, text, out var reparsed))
        {
            reparsed = ParseValue(shorthand.Name, CssParser.ParseListOfComponentValues(text), originalText: null);
            cache?.Add(atRule: null, shorthand.Name, text, reparsed);
        }

        if (reparsed is null)
            return false;

        foreach (var value in reparsed)
        {
            if (value.ShorthandName is not null)
                return false;

            if (!lookup.TryGetValue(value.Name, out var expected) || expected != value.Value)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Serializes a shorthand from the values of its longhands: the candidates (a keyword alternative of the grammar, the
    /// shortest form, the complete form) are checked by parsing them back.
    /// </summary>
    private static string? SerializeFromLonghands(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var grammar = CssPropertyRegistry.GetGrammar(shorthand);
        if (grammar is null)
            return null;

        // The grid-lanes shorthand ignores the template of the stacking axis (it does not set it back), as in the browsers (WPT
        // css/css-grid/grid-lanes/tentative/parsing/grid-lanes-shorthand-serialization.html)
        var expected = lookup;
        if (shorthand.Name == "grid-lanes" && lookup.TryGetValue("grid-lanes-direction", out var direction))
        {
            expected = new Dictionary<string, string>(lookup, StringComparer.Ordinal) { [direction.StartsWith("row", StringComparison.Ordinal) ? "grid-template-columns" : "grid-template-rows"] = "none" };
        }

        // The corners without radius whose shape is round are serialized as normal by the corner shorthands (0% round is normal)
        expected = CssPropertyRules.GetNormalCornerLonghands(shorthand, expected);

        // The font shorthand represents a font width that is the percentage of a <font-width-css3> keyword with the keyword
        if (CssPropertyRules.GetFontWidthKeywordLonghands(shorthand, expected) is { } fontWidthKeyword)
        {
            lookup = expected = fontWidthKeyword;
        }

        foreach (var candidate in GetCandidates(shorthand, grammar, lookup))
        {
            if (!string.IsNullOrEmpty(candidate) && RoundTrips(shorthand, candidate, expected))
                return candidate;
        }

        return null;
    }

    private static IEnumerable<string?> GetCandidates(CssPropertyDefinition shorthand, GrammarNode grammar, Dictionary<string, string> lookup)
    {
        if (CssBackgrounds.IsLayered(shorthand.Name))
        {
            yield return CssBackgrounds.Serialize(shorthand, lookup);
            yield break;
        }

        if (CssGrid.IsShorthand(shorthand.Name))
        {
            yield return CssGrid.Serialize(shorthand, lookup);
            yield break;
        }

        if (CssGaps.IsShorthand(shorthand))
        {
            yield return CssGaps.Serialize(shorthand, lookup);
            yield break;
        }

        if (CssPropertyRules.IsShorthand(shorthand.Name))
        {
            yield return CssPropertyRules.Serialize(shorthand, lookup);
            yield break;
        }

        if (IsBoxShorthand(shorthand, grammar))
        {
            yield return SerializeBox(shorthand, lookup);
            yield break;
        }

        // Keyword alternatives (e.g. font-variant: normal); flex: none is serialized in its complete form (0 0 auto), as in the browsers
        if (!CompleteForm.Contains(shorthand.Name) && Unwrap(grammar) is GroupNode { Combinator: GrammarCombinator.OneOf } oneOf)
        {
            foreach (var child in oneOf.Children)
            {
                if (child is KeywordNode keyword)
                    yield return keyword.Keyword;
            }
        }

        // e.g. border-block: <'border-block-start'>: all the longhand shorthands must have the same value
        if (grammar is TypeNode { IsProperty: true } && shorthand.Longhands.All(l => CssPropertyRegistry.Get(l) is { IsShorthand: true }))
        {
            string? common = null;
            foreach (var longhand in shorthand.Longhands)
            {
                var value = SerializeFromLonghands(CssPropertyRegistry.Get(longhand)!, lookup);
                if (value is null || (common is not null && common != value))
                    yield break;

                common = value;
            }

            yield return common;
            yield break;
        }

        if (!CompleteForm.Contains(shorthand.Name))
        {
            yield return new ShorthandGenerator(shorthand, grammar, lookup, minimal: true).Generate();
        }

        yield return new ShorthandGenerator(shorthand, grammar, lookup, minimal: false).Generate();
    }

    private static string? SerializeBox(CssPropertyDefinition shorthand, Dictionary<string, string> lookup)
    {
        var sides = new string[shorthand.Longhands.Length];
        for (var i = 0; i < sides.Length; i++)
        {
            if (!lookup.TryGetValue(shorthand.Longhands[i], out var side))
                return null;

            sides[i] = side;
        }

        if (sides.Length == 2)
            return sides[0] == sides[1] ? sides[0] : sides[0] + " " + sides[1];

        // top right bottom left
        if (sides[1] == sides[3])
        {
            if (sides[0] == sides[2])
                return sides[0] == sides[1] ? sides[0] : sides[0] + " " + sides[1];

            return sides[0] + " " + sides[1] + " " + sides[2];
        }

        return string.Join(' ', sides);
    }

    /// <summary>Returns the value of a direct longhand of a shorthand, which can itself be a shorthand (e.g. border-width for border).</summary>
    private static string? GetDirectLonghandValue(string longhand, Dictionary<string, string> lookup)
    {
        if (lookup.TryGetValue(longhand, out var value))
            return value;

        if (CssPropertyRegistry.Get(longhand) is not { IsShorthand: true } nested)
            return null;

        // The box shorthands must be representable by a single value when they are part of another shorthand (e.g. the 4 sides of border-width in border)
        var grammar = CssPropertyRegistry.GetGrammar(nested);
        if (grammar is not null && IsBoxShorthand(nested, grammar))
        {
            string? common = null;
            foreach (var side in nested.Longhands)
            {
                if (!lookup.TryGetValue(side, out var sideValue) || (common is not null && common != sideValue))
                    return null;

                common = sideValue;
            }

            return common;
        }

        return SerializeFromLonghands(nested, lookup);
    }

    /// <summary>Returns the value of a longhand when it is omitted from the shorthand (usually its initial value).</summary>
    private static string? GetOmittedValue(CssPropertyDefinition shorthand, string longhand)
    {
        if (OmittedValues.TryGetValue((shorthand.Name, longhand), out var value))
            return value;

        if (CssPropertyRegistry.Get(longhand) is { IsShorthand: true } nested)
        {
            var initial = CssPropertyRegistry.GetLonghandsRecursive(nested).ToDictionary(l => l, l => GetInitialValue(l) ?? "", StringComparer.Ordinal);
            return GetDirectLonghandValue(longhand, initial);
        }

        return GetInitialValue(longhand);
    }

    /// <summary>
    /// Generates the value of a shorthand from the grammar and the values of the longhands. In minimal mode, the optional
    /// components whose value is the value they get when omitted are omitted.
    /// </summary>
    private sealed class ShorthandGenerator(CssPropertyDefinition shorthand, GrammarNode grammar, Dictionary<string, string> lookup, bool minimal)
    {
        private readonly HashSet<string> _used = new(StringComparer.Ordinal);

        public string? Generate()
        {
            var result = Generate(grammar, optional: false, root: true);
            if (result is null)
                return null;

            // The longhands that are not in the value must have the value they get when omitted (e.g. border-image in border)
            foreach (var longhand in shorthand.Longhands)
            {
                if (_used.Contains(longhand))
                    continue;

                var value = GetDirectLonghandValue(longhand, lookup);
                if (value is null || value != GetOmittedValue(shorthand, longhand))
                    return null;
            }

            return result;
        }

        private string? Generate(GrammarNode node, bool optional, bool root = false, int childIndex = -1, int childCount = 0)
        {
            optional |= node.Min == 0;

            // A group that is a component of the root, mapped to a longhand by index (e.g. [ small-caps | all-small-caps | ... ] in font-variant)
            if (node is GroupNode { Max: 1 } && childIndex >= 0 && childCount == shorthand.Longhands.Length - GetResetOnlyCount(shorthand) && !ContainsPropertyReference(node))
            {
                var longhand = shorthand.Longhands[childIndex];
                if (!_used.Add(longhand) || GetDirectLonghandValue(longhand, lookup) is not { } value)
                    return null;

                if (minimal && optional && value == GetOmittedValue(shorthand, longhand))
                    return "";

                return ValueMatcher.Matches(node, ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))) ? value : null;
            }

            switch (node)
            {
                case LiteralNode literal:
                    return literal.Literal;

                case TypeNode type:
                {
                    var typeLonghand = type.IsProperty ? null : CssPropertyRegistry.GetTypeLonghand(type.Name);
                    var longhand = type.IsProperty ? CssPropertyRegistry.Get(type.Name)?.Name : typeLonghand ?? FindLonghand(type, childIndex, childCount);
                    if (longhand is null || !shorthand.Longhands.Contains(longhand) || !_used.Add(longhand))
                        return null;

                    var value = GetDirectLonghandValue(longhand, lookup);
                    if (value is null)
                        return null;

                    // The value of the longhand must be a value of the type (e.g. font-variant: all-small-caps cannot be set by font)
                    if (typeLonghand is not null && !MatchesType(type, value))
                        return null;

                    if (minimal && optional && value == GetOmittedValue(shorthand, longhand))
                        return "";

                    return value;
                }

                case GroupNode group when node.Max == 1:
                {
                    var parts = new List<string>();
                    switch (group.Combinator)
                    {
                        case GrammarCombinator.OneOf:
                            foreach (var alternative in group.Children)
                            {
                                if (alternative is KeywordNode)
                                    continue;

                                var saved = new HashSet<string>(_used, StringComparer.Ordinal);
                                var value = Generate(alternative, optional, root);
                                if (value is not null)
                                    return value;

                                _used.Clear();
                                _used.UnionWith(saved);
                            }

                            return null;

                        case GrammarCombinator.AnyInAnyOrder:
                        {
                            string? first = null;
                            for (var i = 0; i < group.Children.Count; i++)
                            {
                                var value = Generate(group.Children[i], optional: true, childIndex: root ? i : -1, childCount: group.Children.Count);
                                if (value is null)
                                    return null;

                                if (value.Length > 0)
                                {
                                    parts.Add(value);
                                }
                                else if (first is null && !optional)
                                {
                                    // At least one component is required: the first one is kept when all are omitted
                                    first = new ShorthandGenerator(shorthand, group.Children[i], lookup, minimal: false).GenerateComponent();
                                }
                            }

                            if (parts.Count == 0 && first is not null)
                            {
                                parts.Add(first);
                            }

                            break;
                        }

                        default:
                        {
                            var hasValue = false;
                            for (var i = 0; i < group.Children.Count; i++)
                            {
                                var child = group.Children[i];
                                var value = Generate(child, optional: false, childIndex: root ? i : -1, childCount: group.Children.Count);
                                if (value is null)
                                    return null;

                                if (value.Length > 0)
                                {
                                    parts.Add(value);
                                    hasValue |= child is not LiteralNode;
                                }
                            }

                            // An optional group whose components have their omitted value is omitted (e.g. [ / <'line-height'> ]?)
                            if (minimal && optional && !hasValue)
                                return "";

                            if (minimal && optional && IsOmittable(group))
                                return "";

                            break;
                        }
                    }

                    return string.Join(' ', parts);
                }

                default:
                    return null;
            }
        }

        /// <summary>Generates a single component (used when all the components of a || group are omitted).</summary>
        private string? GenerateComponent() => Generate(grammar, optional: false, root: false);

        /// <summary>Returns whether all the longhands referenced by the group have their omitted value.</summary>
        private bool IsOmittable(GroupNode group)
        {
            foreach (var child in group.Children)
            {
                switch (child)
                {
                    case TypeNode { IsProperty: true } reference when CssPropertyRegistry.Get(reference.Name)?.Name is { } name:
                        if (GetDirectLonghandValue(name, lookup) != GetOmittedValue(shorthand, name))
                            return false;

                        break;
                    case GroupNode nested:
                        if (!IsOmittable(nested))
                            return false;

                        break;
                    case LiteralNode:
                        break;
                    default:
                        return false;
                }
            }

            return true;
        }

        /// <summary>Returns the longhand of a component that is not a property reference: by index in the root group, or the first unused longhand whose value matches the type.</summary>
        private string? FindLonghand(TypeNode type, int childIndex, int childCount)
        {
            if (childIndex >= 0 && childCount == shorthand.Longhands.Length - GetResetOnlyCount(shorthand))
                return shorthand.Longhands[childIndex];

            foreach (var longhand in shorthand.Longhands)
            {
                if (_used.Contains(longhand))
                    continue;

                var value = GetDirectLonghandValue(longhand, lookup);
                if (value is not null && MatchesType(type, value))
                    return longhand;
            }

            return null;
        }

        private static bool MatchesType(TypeNode type, string value)
        {
            var node = new TypeNode(type.Name, isProperty: false) { RangeMin = type.RangeMin, RangeMax = type.RangeMax };
            return ValueMatcher.Matches(node, ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value)));
        }
    }
}
