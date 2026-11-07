using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>The container features of a container query, which determine the eligible query containers.</summary>
[Flags]
internal enum ContainerQueryFeatures
{
    None = 0,

    /// <summary>A size feature of the physical horizontal axis (width).</summary>
    Width = 1,

    /// <summary>A size feature of the physical vertical axis (height).</summary>
    Height = 2,

    /// <summary>A size feature of the inline axis of the container (inline-size).</summary>
    InlineSize = 4,

    /// <summary>A size feature of the block axis of the container (block-size).</summary>
    BlockSize = 8,

    /// <summary>A style feature (style()): all the elements are style containers.</summary>
    Style = 16,

    /// <summary>A scroll-state feature (scroll-state()).</summary>
    ScrollState = 32,

    /// <summary>An unknown feature or a &lt;general-enclosed&gt;: no query container is selected.</summary>
    Unknown = 64,

    /// <summary>An anchored feature (anchored(), https://drafts.csswg.org/css-anchor-position-2/#container-rule-anchored).</summary>
    Anchored = 128,
}

/// <summary>A &lt;container-condition&gt; of a @container rule (https://drafts.csswg.org/css-conditional-5/#container-rule).</summary>
internal sealed class ContainerCondition
{
    /// <summary>The &lt;container-name&gt;, or null.</summary>
    public required string? Name { get; init; }

    /// <summary>The &lt;container-query&gt;, or null when the condition only has a name.</summary>
    public required ContainerQueryNode? Query { get; init; }

    /// <summary>The features of the query.</summary>
    public required ContainerQueryFeatures Features { get; init; }

    /// <summary>The serialization of the name (https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-name).</summary>
    public required string NameText { get; init; }

    /// <summary>The serialization of the query (https://drafts.csswg.org/css-conditional-5/#dom-csscontainercondition-query).</summary>
    public required string QueryText { get; init; }
}

/// <summary>A node of a container query: the results use a three-valued logic (true, false, unknown).</summary>
internal abstract class ContainerQueryNode
{
    public abstract bool? Evaluate(ContainerQueryContext context);
}

/// <summary>The comparisons of the range syntax (https://drafts.csswg.org/mediaqueries-5/#mq-range-context).</summary>
internal enum ContainerQueryComparison
{
    Equal,
    Less,
    LessOrEqual,
    Greater,
    GreaterOrEqual,
}

/// <summary>The size features (https://drafts.csswg.org/css-conditional-5/#size-container).</summary>
internal enum ContainerSizeFeature
{
    Width,
    Height,
    InlineSize,
    BlockSize,
    AspectRatio,
    Orientation,
}

/// <summary>Parses the container conditions of the @container rules (https://drafts.csswg.org/css-conditional-5/#container-rule).</summary>
internal sealed class ContainerQueryParser
{
    private static readonly Dictionary<string, (ContainerSizeFeature Feature, ContainerQueryFeatures Axes)> SizeFeatures = new(StringComparer.Ordinal)
    {
        ["width"] = (ContainerSizeFeature.Width, ContainerQueryFeatures.Width),
        ["height"] = (ContainerSizeFeature.Height, ContainerQueryFeatures.Height),
        ["inline-size"] = (ContainerSizeFeature.InlineSize, ContainerQueryFeatures.InlineSize),
        ["block-size"] = (ContainerSizeFeature.BlockSize, ContainerQueryFeatures.BlockSize),
        ["aspect-ratio"] = (ContainerSizeFeature.AspectRatio, ContainerQueryFeatures.Width | ContainerQueryFeatures.Height),
        ["orientation"] = (ContainerSizeFeature.Orientation, ContainerQueryFeatures.Width | ContainerQueryFeatures.Height),
    };

    /// <summary>The scroll-state features and their values (https://drafts.csswg.org/css-conditional-5/#scroll-state-container).</summary>
    private static readonly Dictionary<string, (ContainerScrollStateFeature Feature, string[] Values)> ScrollStateFeatures = new(StringComparer.Ordinal)
    {
        ["stuck"] = (ContainerScrollStateFeature.Stuck, ["none", "top", "right", "bottom", "left", "block-start", "inline-start", "block-end", "inline-end"]),
        ["snapped"] = (ContainerScrollStateFeature.Snapped, ["none", "x", "y", "block", "inline", "both"]),
        ["scrollable"] = (ContainerScrollStateFeature.Scrollable, ["none", "top", "right", "bottom", "left", "block-start", "inline-start", "block-end", "inline-end", "x", "y", "block", "inline"]),
        ["scrolled"] = (ContainerScrollStateFeature.Scrolled, ["none", "top", "right", "bottom", "left", "block-start", "inline-start", "block-end", "inline-end", "x", "y", "block", "inline"]),
    };

    private static readonly Lazy<GrammarNode> LengthGrammar = new(() => ValueGrammarParser.Parse("<length>"));
    private static readonly Lazy<GrammarNode> RatioGrammar = new(() => ValueGrammarParser.Parse("<ratio>"));

    /// <summary>The value of the fallback feature (https://drafts.csswg.org/css-anchor-position-2/#descdef-container-fallback).</summary>
    private static readonly Lazy<GrammarNode> FallbackGrammar = new(() => ValueGrammarParser.Parse("none | [ [<dashed-ident> || <try-tactic>] | <position-area-query> ]"));

    private readonly string _source;
    private ContainerQueryFeatures _features;

    private ContainerQueryParser(string source) => _source = source;

    /// <summary>Parses a &lt;container-condition&gt; = [ &lt;container-name&gt;? &lt;container-query&gt;? ]!; returns null when it is invalid.</summary>
    /// <param name="values">The component values of the condition.</param>
    /// <param name="source">The source the positions of the component values are relative to.</param>
    public static ContainerCondition? ParseCondition(List<CssComponentValue> values, string source)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        string? name = null;
        var index = 0;
        if (tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } ident && IsContainerName(ident.Token.Value))
        {
            name = ident.Token.Value;
            index = 1;
        }

        var parser = new ContainerQueryParser(source);
        ContainerQueryNode? query = null;
        var queryText = "";
        if (index < tokens.Count)
        {
            if (parser.ParseQuery(tokens, index, out var end) is not { } parsed || end != tokens.Count)
                return null;

            (query, queryText) = parsed;
        }

        return new ContainerCondition
        {
            Name = name,
            Query = query,
            Features = parser._features,
            NameText = name is null ? "" : CssSerializer.SerializeIdentifier(name),
            QueryText = queryText,
        };
    }

    /// <summary>
    /// Parses the &lt;style-query&gt; of a style() test of an if() condition (https://drafts.csswg.org/css-values-5/#typedef-if-test);
    /// returns null when it is invalid.
    /// </summary>
    /// <param name="values">The component values of the style() function.</param>
    /// <param name="source">The source the positions of the component values are relative to.</param>
    public static ContainerQueryNode? ParseStyleTest(List<CssComponentValue> values, string source)
    {
        if (!IsAnyValue(values))
            return null;

        return new ContainerQueryParser(source).ParseStyleQuery(values)?.Node;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-conditional-5/#typedef-container-name: a &lt;custom-ident&gt;, excluding none, and, not and or.
    /// </summary>
    private static bool IsContainerName(string value)
    {
        var lower = InfraAscii.ToAsciiLowercase(value);
        return lower is not ("none" or "and" or "not" or "or" or "default") && !ValueMatcher.CssWideKeywords.Contains(lower);
    }

    /// <summary>
    /// &lt;container-query&gt; = not &lt;query-in-parens&gt; | &lt;query-in-parens&gt; [ [ and &lt;query-in-parens&gt; ]* | [ or &lt;query-in-parens&gt; ]* ]
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseQuery(List<CssComponentValue> tokens, int index, out int end)
        => ParseBooleanCondition(tokens, index, ParseQueryInParens, out end);

    /// <summary>The boolean syntax shared by the container queries, the style queries and the scroll-state queries.</summary>
    private static (ContainerQueryNode Node, string Text)? ParseBooleanCondition(List<CssComponentValue> tokens, int index, Func<CssComponentValue, (ContainerQueryNode Node, string Text)?> parseInParens, out int end)
    {
        end = index;
        if (index >= tokens.Count)
            return null;

        if (tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } not && not.Token.IsIdent("not"))
        {
            if (index + 1 >= tokens.Count || parseInParens(tokens[index + 1]) is not { } negated)
                return null;

            end = index + 2;
            return (new ContainerNotNode(negated.Node), "not " + negated.Text);
        }

        if (parseInParens(tokens[index]) is not { } first)
            return null;

        var nodes = new List<ContainerQueryNode> { first.Node };
        var builder = new StringBuilder(first.Text);
        string? combinator = null;
        index++;
        while (index < tokens.Count && tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } word && InfraAscii.ToAsciiLowercase(word.Token.Value) is "and" or "or")
        {
            // "and" and "or" cannot be mixed at the same level
            var keyword = InfraAscii.ToAsciiLowercase(word.Token.Value);
            if (combinator is not null && combinator != keyword)
                return null;

            combinator = keyword;
            if (index + 1 >= tokens.Count || parseInParens(tokens[index + 1]) is not { } next)
                return null;

            nodes.Add(next.Node);
            builder.Append(' ').Append(keyword).Append(' ').Append(next.Text);
            index += 2;
        }

        end = index;
        if (nodes.Count == 1)
            return first;

        return (combinator == "and" ? new ContainerAndNode([.. nodes]) : new ContainerOrNode([.. nodes]), builder.ToString());
    }

    /// <summary>
    /// &lt;query-in-parens&gt; = ( &lt;container-query&gt; ) | ( &lt;size-feature&gt; ) | style( &lt;style-query&gt; )
    /// | scroll-state( &lt;scroll-state-query&gt; ) | &lt;general-enclosed&gt;
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseQueryInParens(CssComponentValue value)
    {
        switch (value)
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
            {
                if (!IsAnyValue(block.Value))
                    return null;

                var inner = ValueMatcher.RemoveWhitespace(block.Value);
                var features = _features;
                if (ParseQuery(inner, 0, out var end) is { } query && end == inner.Count)
                    return (query.Node, "(" + query.Text + ")");

                _features = features;
                if (ParseSizeFeature(inner) is { } feature)
                    return feature;

                _features = features;
                return GeneralEnclosed(block);
            }

            case CssFunction function:
            {
                if (!IsAnyValue(function.Value))
                    return null;

                var features = _features;
                switch (InfraAscii.ToAsciiLowercase(function.Name))
                {
                    case "style":
                        if (ParseStyleQuery(function.Value) is { } style)
                        {
                            _features |= ContainerQueryFeatures.Style;
                            return (style.Node, "style(" + style.Text + ")");
                        }

                        break;

                    case "scroll-state":
                        if (ParseScrollStateQuery(function.Value) is { } scrollState)
                        {
                            _features |= ContainerQueryFeatures.ScrollState;
                            return (scrollState.Node, "scroll-state(" + scrollState.Text + ")");
                        }

                        break;

                    case "anchored":
                        if (ParseAnchoredQuery(function.Value) is { } anchored)
                        {
                            _features |= ContainerQueryFeatures.Anchored;
                            return (anchored.Node, "anchored(" + anchored.Text + ")");
                        }

                        break;
                }

                _features = features;
                return GeneralEnclosed(function);
            }

            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-general-enclosed: valid, but unknown; it is serialized as specified.</summary>
    private (ContainerQueryNode Node, string Text) GeneralEnclosed(CssComponentValue value)
    {
        _features |= ContainerQueryFeatures.Unknown;
        return (ContainerUnknownNode.Instance, GetRawText(value));
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Size features (https://drafts.csswg.org/css-conditional-5/#size-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>&lt;size-feature&gt;: the syntax of a media feature (https://drafts.csswg.org/mediaqueries-5/#typedef-media-feature).</summary>
    private (ContainerQueryNode Node, string Text)? ParseSizeFeature(List<CssComponentValue> tokens)
    {
        if (tokens.Count == 0)
            return null;

        if (tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } nameToken)
        {
            var name = InfraAscii.ToAsciiLowercase(nameToken.Token.Value);

            // <mf-boolean>
            if (tokens.Count == 1)
            {
                if (!SizeFeatures.TryGetValue(name, out var feature))
                    return null;

                _features |= feature.Axes;
                return (new ContainerSizeFeatureNode(feature.Feature, []), "(" + name + ")");
            }

            // <mf-plain>
            if (tokens[1] is CssPreservedToken { Type: CssTokenType.Colon })
            {
                var value = tokens[2..];
                var comparison = ContainerQueryComparison.Equal;
                var featureName = name;
                if (!SizeFeatures.ContainsKey(name) && (name.StartsWith("min-", StringComparison.Ordinal) || name.StartsWith("max-", StringComparison.Ordinal)))
                {
                    comparison = name.StartsWith("min-", StringComparison.Ordinal) ? ContainerQueryComparison.GreaterOrEqual : ContainerQueryComparison.LessOrEqual;
                    featureName = name[4..];

                    // The min-/max- prefixes are only valid for the range features
                    if (featureName == "orientation")
                        return null;
                }

                if (!SizeFeatures.TryGetValue(featureName, out var feature) || !IsValidSizeValue(feature.Feature, value))
                    return null;

                _features |= feature.Axes;
                return (new ContainerSizeFeatureNode(feature.Feature, [(comparison, value)]), "(" + name + ": " + SerializeValue(value) + ")");
            }
        }

        return ParseSizeRange(tokens);
    }

    /// <summary>
    /// &lt;mf-range&gt; = &lt;mf-name&gt; &lt;mf-comparison&gt; &lt;mf-value&gt; | &lt;mf-value&gt; &lt;mf-comparison&gt; &lt;mf-name&gt;
    /// | &lt;mf-value&gt; &lt;mf-lt&gt; &lt;mf-name&gt; &lt;mf-lt&gt; &lt;mf-value&gt; | &lt;mf-value&gt; &lt;mf-gt&gt; &lt;mf-name&gt; &lt;mf-gt&gt; &lt;mf-value&gt;
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseSizeRange(List<CssComponentValue> tokens)
    {
        if (SplitRange(tokens) is not var (parts, comparisons))
            return null;

        if (comparisons.Count == 1)
        {
            // <mf-name> <mf-comparison> <mf-value>, or <mf-value> <mf-comparison> <mf-name> ("value < name" is "name > value")
            if (GetRangeFeature(parts[0]) is { } left && IsValidSizeValue(left.Feature, parts[1]))
            {
                _features |= left.Axes;
                return (new ContainerSizeFeatureNode(left.Feature, [(comparisons[0], parts[1])]), "(" + left.Name + " " + Serialize(comparisons[0]) + " " + SerializeValue(parts[1]) + ")");
            }

            if (GetRangeFeature(parts[1]) is { } right && IsValidSizeValue(right.Feature, parts[0]))
            {
                _features |= right.Axes;
                return (new ContainerSizeFeatureNode(right.Feature, [(Reverse(comparisons[0]), parts[0])]), "(" + SerializeValue(parts[0]) + " " + Serialize(comparisons[0]) + " " + right.Name + ")");
            }

            return null;
        }

        if (comparisons.Count == 2 && AreSameDirection(comparisons[0], comparisons[1]) && GetRangeFeature(parts[1]) is { } middle
            && IsValidSizeValue(middle.Feature, parts[0]) && IsValidSizeValue(middle.Feature, parts[2]))
        {
            _features |= middle.Axes;
            var text = "(" + SerializeValue(parts[0]) + " " + Serialize(comparisons[0]) + " " + middle.Name + " " + Serialize(comparisons[1]) + " " + SerializeValue(parts[2]) + ")";
            return (new ContainerSizeFeatureNode(middle.Feature, [(Reverse(comparisons[0]), parts[0]), (comparisons[1], parts[2])]), text);
        }

        return null;
    }

    private static (ContainerSizeFeature Feature, ContainerQueryFeatures Axes, string Name)? GetRangeFeature(List<CssComponentValue> part)
    {
        if (part is not [CssPreservedToken { Type: CssTokenType.Ident } ident])
            return null;

        var name = InfraAscii.ToAsciiLowercase(ident.Token.Value);
        return SizeFeatures.TryGetValue(name, out var feature) && feature.Feature != ContainerSizeFeature.Orientation ? (feature.Feature, feature.Axes, name) : null;
    }

    /// <summary>Validates the value of a size feature; the values that contain arbitrary substitution functions are validated when they are evaluated.</summary>
    private static bool IsValidSizeValue(ContainerSizeFeature feature, List<CssComponentValue> value)
    {
        if (value.Count == 0)
            return false;

        if (feature == ContainerSizeFeature.Orientation)
            return value is [CssPreservedToken { Type: CssTokenType.Ident } ident] && InfraAscii.ToAsciiLowercase(ident.Token.Value) is "portrait" or "landscape";

        if (ValueMatcher.ContainsSubstitutionFunction(value))
            return value.Count == 1 || (feature == ContainerSizeFeature.AspectRatio && value.Count == 3);

        return ValueMatcher.Matches(feature == ContainerSizeFeature.AspectRatio ? RatioGrammar.Value : LengthGrammar.Value, value);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Style features (https://drafts.csswg.org/css-conditional-5/#style-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// &lt;style-query&gt; = not &lt;style-in-parens&gt; | &lt;style-in-parens&gt; [ [ and &lt;style-in-parens&gt; ]* | [ or &lt;style-in-parens&gt; ]* ] | &lt;style-feature&gt;
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseStyleQuery(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        var features = _features;
        if (ParseBooleanCondition(tokens, 0, ParseStyleInParens, out var end) is { } condition && end == tokens.Count)
            return condition;

        _features = features;
        return ParseStyleFeature(values);
    }

    /// <summary>&lt;style-in-parens&gt; = ( &lt;style-query&gt; ) | ( &lt;style-feature&gt; ) | &lt;general-enclosed&gt;</summary>
    private (ContainerQueryNode Node, string Text)? ParseStyleInParens(CssComponentValue value)
    {
        switch (value)
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block when IsAnyValue(block.Value):
            {
                var features = _features;
                if (ParseStyleQuery(block.Value) is { } query)
                    return (query.Node, "(" + query.Text + ")");

                _features = features;
                return GeneralEnclosed(block);
            }

            case CssFunction function when IsAnyValue(function.Value):
                return GeneralEnclosed(function);

            default:
                return null;
        }
    }

    /// <summary>&lt;style-feature&gt; = &lt;style-feature-plain&gt; | &lt;style-feature-boolean&gt; | &lt;style-range&gt;</summary>
    private (ContainerQueryNode Node, string Text)? ParseStyleFeature(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        if (tokens.Exists(IsComparisonDelim))
            return ParseStyleRange(tokens);

        if (tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken || GetStyleFeatureName(nameToken.Token.Value) is not { } name)
            return null;

        var serializedName = CssSerializer.SerializeIdentifier(name);

        // <style-feature-boolean>
        if (tokens.Count == 1)
            return (new ContainerStyleFeatureNode(name, value: null), serializedName);

        // <style-feature-plain> = <style-feature-name> : <style-feature-value>
        if (tokens[1] is not CssPreservedToken { Type: CssTokenType.Colon })
            return null;

        var colonIndex = values.IndexOf(tokens[1]);
        var value = values[(colonIndex + 1)..];

        // The value of a declaration can end with !important
        var significant = ValueMatcher.RemoveWhitespace(value);
        if (significant is [.., CssPreservedToken bang, CssPreservedToken { Type: CssTokenType.Ident } important] && bang.Token.IsDelim('!') && important.Token.IsIdent("important"))
        {
            value = value[..value.IndexOf(bang)];
        }

        if (!CssShorthands.IsValidDeclarationValue(value, topLevel: true))
            return null;

        var text = GetRawText(value);
        return (new ContainerStyleFeatureNode(name, text), serializedName + ": " + text);
    }

    /// <summary>
    /// The name of a style feature: a custom property name (case-sensitive) or a supported CSS property (ASCII case-insensitive); null when it is not supported.
    /// </summary>
    private static string? GetStyleFeatureName(string name)
    {
        if (CssPropertyRegistry.IsCustomProperty(name))
            return name;

        var lower = InfraAscii.ToAsciiLowercase(name);
        return CssPropertyRegistry.Get(lower) is not null ? lower : null;
    }

    /// <summary>
    /// &lt;style-range&gt; = &lt;style-range-value&gt; &lt;mf-comparison&gt; &lt;style-range-value&gt;
    /// | &lt;style-range-value&gt; &lt;mf-lt&gt; &lt;style-range-value&gt; &lt;mf-lt&gt; &lt;style-range-value&gt;
    /// | &lt;style-range-value&gt; &lt;mf-gt&gt; &lt;style-range-value&gt; &lt;mf-gt&gt; &lt;style-range-value&gt;
    /// </summary>
    private static (ContainerQueryNode Node, string Text)? ParseStyleRange(List<CssComponentValue> tokens)
    {
        if (SplitRange(tokens) is not var (parts, comparisons))
            return null;

        if (comparisons.Count == 2 && !AreSameDirection(comparisons[0], comparisons[1]))
            return null;

        if (comparisons.Count > 2)
            return null;

        // Each <style-range-value> is a single value: a custom property name or a <style-feature-value>
        var operands = new ContainerStyleRangeOperand[parts.Count];
        var builder = new StringBuilder();
        for (var i = 0; i < parts.Count; i++)
        {
            if (parts[i] is not [var value] || !CssShorthands.IsValidDeclarationValue(parts[i], topLevel: true))
                return null;

            if (i > 0)
            {
                builder.Append(' ').Append(Serialize(comparisons[i - 1])).Append(' ');
            }

            if (value is CssPreservedToken { Type: CssTokenType.Ident } ident && CssPropertyRegistry.IsCustomProperty(ident.Token.Value))
            {
                operands[i] = new ContainerStyleRangeOperand(CustomPropertyName: ident.Token.Value, Value: null);
                builder.Append(CssSerializer.SerializeIdentifier(ident.Token.Value));
            }
            else
            {
                var text = CssSerializer.SerializeValue(parts[i], property: null);
                operands[i] = new ContainerStyleRangeOperand(CustomPropertyName: null, Value: text);
                builder.Append(text);
            }
        }

        return (new ContainerStyleRangeNode(operands, [.. comparisons]), builder.ToString());
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Scroll-state features (https://drafts.csswg.org/css-conditional-5/#scroll-state-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// &lt;scroll-state-query&gt; = not &lt;scroll-state-in-parens&gt; | &lt;scroll-state-in-parens&gt; [ [ and &lt;scroll-state-in-parens&gt; ]* | [ or &lt;scroll-state-in-parens&gt; ]* ]
    /// | &lt;scroll-state-feature&gt;
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseScrollStateQuery(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        var features = _features;
        if (ParseBooleanCondition(tokens, 0, ParseScrollStateInParens, out var end) is { } condition && end == tokens.Count)
            return condition;

        _features = features;
        return ParseScrollStateFeature(tokens);
    }

    private (ContainerQueryNode Node, string Text)? ParseScrollStateInParens(CssComponentValue value)
    {
        switch (value)
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block when IsAnyValue(block.Value):
            {
                var features = _features;
                if (ParseScrollStateQuery(block.Value) is { } query)
                    return (query.Node, "(" + query.Text + ")");

                _features = features;
                return GeneralEnclosed(block);
            }

            case CssFunction function when IsAnyValue(function.Value):
                return GeneralEnclosed(function);

            default:
                return null;
        }
    }

    /// <summary>&lt;scroll-state-feature&gt;: the syntax of a media feature, with the discrete scroll-state features.</summary>
    private static (ContainerQueryNode Node, string Text)? ParseScrollStateFeature(List<CssComponentValue> tokens)
    {
        if (tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken)
            return null;

        var name = InfraAscii.ToAsciiLowercase(nameToken.Token.Value);
        if (!ScrollStateFeatures.TryGetValue(name, out var feature))
            return null;

        if (tokens.Count == 1)
            return (new ContainerScrollStateFeatureNode(feature.Feature, value: null), name);

        if (tokens is [_, CssPreservedToken { Type: CssTokenType.Colon }, CssPreservedToken { Type: CssTokenType.Ident } valueToken] && feature.Values.Contains(InfraAscii.ToAsciiLowercase(valueToken.Token.Value), StringComparer.Ordinal))
        {
            var value = InfraAscii.ToAsciiLowercase(valueToken.Token.Value);
            return (new ContainerScrollStateFeatureNode(feature.Feature, value), name + ": " + value);
        }

        return null;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Anchored features (https://drafts.csswg.org/css-anchor-position-2/#anchored-container)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// &lt;anchored-query&gt; = not &lt;anchored-in-parens&gt; | &lt;anchored-in-parens&gt; [ [ and &lt;anchored-in-parens&gt; ]* | [ or &lt;anchored-in-parens&gt; ]* ]
    /// | &lt;anchored-feature&gt;
    /// </summary>
    private (ContainerQueryNode Node, string Text)? ParseAnchoredQuery(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        var features = _features;
        if (ParseBooleanCondition(tokens, 0, ParseAnchoredInParens, out var end) is { } condition && end == tokens.Count)
            return condition;

        _features = features;
        return ParseAnchoredFeature(tokens);
    }

    /// <summary>&lt;anchored-in-parens&gt; = ( &lt;anchored-query&gt; ) | ( &lt;anchored-feature&gt; ) | &lt;general-enclosed&gt;</summary>
    private (ContainerQueryNode Node, string Text)? ParseAnchoredInParens(CssComponentValue value)
    {
        switch (value)
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block when IsAnyValue(block.Value):
            {
                var features = _features;
                if (ParseAnchoredQuery(block.Value) is { } query)
                    return (query.Node, "(" + query.Text + ")");

                _features = features;
                return GeneralEnclosed(block);
            }

            case CssFunction function when IsAnyValue(function.Value):
                return GeneralEnclosed(function);

            default:
                return null;
        }
    }

    /// <summary>
    /// &lt;anchored-feature&gt;: the syntax of a media feature with the discrete <c>fallback</c> feature
    /// (https://drafts.csswg.org/css-anchor-position-2/#descdef-container-fallback). The keywords are serialized in lowercase.
    /// </summary>
    private static (ContainerQueryNode Node, string Text)? ParseAnchoredFeature(List<CssComponentValue> tokens)
    {
        if (tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken || !nameToken.Token.IsIdent("fallback"))
            return null;

        if (tokens.Count == 1)
            return (new ContainerAnchoredFallbackNode(value: null), "fallback");

        if (tokens.Count < 3 || tokens[1] is not CssPreservedToken { Type: CssTokenType.Colon })
            return null;

        var value = tokens[2..];
        if (!ValueMatcher.Matches(FallbackGrammar.Value, value))
            return null;

        var keywords = new List<string>(value.Count);
        foreach (var token in value)
        {
            if (token is not CssPreservedToken { Type: CssTokenType.Ident } ident)
                return null;

            keywords.Add(ident.Token.Value.StartsWith("--", StringComparison.Ordinal) ? ident.Token.Value : InfraAscii.ToAsciiLowercase(ident.Token.Value));
        }

        var text = string.Join(' ', keywords.Select(keyword => CssSerializer.SerializeIdentifier(keyword)));
        return (new ContainerAnchoredFallbackNode(AnchoredFallbackQuery.Create(keywords)), "fallback: " + text);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Helpers
    // ----------------------------------------------------------------------------------------------------------------

    private static bool IsComparisonDelim(CssComponentValue value) => value is CssPreservedToken { Token: { Type: CssTokenType.Delim, Value: "<" or ">" or "=" } };

    /// <summary>Splits a range on its comparisons ("&lt;=" and "&gt;=" are two delim tokens without whitespace in between); null when a part is empty.</summary>
    private static (List<List<CssComponentValue>> Parts, List<ContainerQueryComparison> Comparisons)? SplitRange(List<CssComponentValue> tokens)
    {
        var parts = new List<List<CssComponentValue>> { new() };
        var comparisons = new List<ContainerQueryComparison>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is CssPreservedToken { Token: { Type: CssTokenType.Delim } delim } delimToken && delim.Value is "<" or ">" or "=")
            {
                var orEqual = false;
                if (delim.Value is "<" or ">" && i + 1 < tokens.Count && tokens[i + 1] is CssPreservedToken equals && equals.Token.IsDelim('=') && equals.Start == delimToken.End)
                {
                    orEqual = true;
                    i++;
                }

                comparisons.Add(delim.Value switch
                {
                    "<" => orEqual ? ContainerQueryComparison.LessOrEqual : ContainerQueryComparison.Less,
                    ">" => orEqual ? ContainerQueryComparison.GreaterOrEqual : ContainerQueryComparison.Greater,
                    _ => ContainerQueryComparison.Equal,
                });
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(tokens[i]);
            }
        }

        if (comparisons.Count == 0 || comparisons.Count > 2 || parts.Exists(p => p.Count == 0))
            return null;

        return (parts, comparisons);
    }

    /// <summary>The two comparisons of a range are both &lt; (or &lt;=), or both &gt; (or &gt;=).</summary>
    private static bool AreSameDirection(ContainerQueryComparison first, ContainerQueryComparison second)
    {
        if (first == ContainerQueryComparison.Equal || second == ContainerQueryComparison.Equal)
            return false;

        return first is ContainerQueryComparison.Less or ContainerQueryComparison.LessOrEqual == second is ContainerQueryComparison.Less or ContainerQueryComparison.LessOrEqual;
    }

    private static ContainerQueryComparison Reverse(ContainerQueryComparison comparison) => comparison switch
    {
        ContainerQueryComparison.Less => ContainerQueryComparison.Greater,
        ContainerQueryComparison.LessOrEqual => ContainerQueryComparison.GreaterOrEqual,
        ContainerQueryComparison.Greater => ContainerQueryComparison.Less,
        ContainerQueryComparison.GreaterOrEqual => ContainerQueryComparison.LessOrEqual,
        _ => comparison,
    };

    private static string Serialize(ContainerQueryComparison comparison) => comparison switch
    {
        ContainerQueryComparison.Less => "<",
        ContainerQueryComparison.LessOrEqual => "<=",
        ContainerQueryComparison.Greater => ">",
        ContainerQueryComparison.GreaterOrEqual => ">=",
        _ => "=",
    };

    /// <summary>https://drafts.csswg.org/css-syntax-3/#typedef-any-value: no bad-string, bad-url or unmatched closing bracket.</summary>
    private static bool IsAnyValue(List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.BadString or CssTokenType.BadUrl or CssTokenType.RightParenthesis or CssTokenType.RightSquareBracket or CssTokenType.RightCurlyBracket }:
                    return false;

                case CssSimpleBlock block when !IsAnyValue(block.Value):
                    return false;

                case CssFunction function when !IsAnyValue(function.Value):
                    return false;
            }
        }

        return true;
    }

    /// <summary>Serializes the significant component values of a value, separated by a space.</summary>
    private static string SerializeValue(List<CssComponentValue> values)
    {
        var builder = new StringBuilder();
        foreach (var value in values)
        {
            if (builder.Length > 0)
            {
                builder.Append(' ');
            }

            builder.Append(CssSerializer.SerializeValue([value], property: null));
        }

        return builder.ToString();
    }

    /// <summary>Returns the text of a component value as specified.</summary>
    private string GetRawText(CssComponentValue value) => GetRawText([value]);

    /// <summary>Returns the text of component values as specified, without the leading and trailing whitespace.</summary>
    private string GetRawText(List<CssComponentValue> values)
    {
        var start = values.FindIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        var end = values.FindLastIndex(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace });
        if (start < 0)
            return "";

        var from = values[start].Start;
        var to = values[end].End;
        if (from < 0 || to > _source.Length || from > to)
            return CssSerializer.SerializeValue(values[start..(end + 1)], property: null);

        return _source[from..to];
    }
}

/// <summary>&lt;general-enclosed&gt;, or an unknown feature: unknown.</summary>
internal sealed class ContainerUnknownNode : ContainerQueryNode
{
    public static ContainerUnknownNode Instance { get; } = new();

    public override bool? Evaluate(ContainerQueryContext context) => null;
}

internal sealed class ContainerNotNode(ContainerQueryNode node) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => node.Evaluate(context) is { } result ? !result : null;
}

internal sealed class ContainerAndNode(ContainerQueryNode[] nodes) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context)
    {
        bool? result = true;
        foreach (var node in nodes)
        {
            var value = node.Evaluate(context);
            if (value == false)
                return false;

            if (value is null)
            {
                result = null;
            }
        }

        return result;
    }
}

internal sealed class ContainerOrNode(ContainerQueryNode[] nodes) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context)
    {
        bool? result = false;
        foreach (var node in nodes)
        {
            var value = node.Evaluate(context);
            if (value == true)
                return true;

            if (value is null)
            {
                result = null;
            }
        }

        return result;
    }
}

/// <summary>A size feature: in a boolean context when there is no comparison, otherwise "feature comparison value" for each comparison.</summary>
internal sealed class ContainerSizeFeatureNode(ContainerSizeFeature feature, (ContainerQueryComparison Comparison, List<CssComponentValue> Value)[] comparisons) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => context.EvaluateSizeFeature(feature, comparisons);
}

/// <summary>A &lt;style-feature-plain&gt; (with a value) or a &lt;style-feature-boolean&gt; (without value).</summary>
internal sealed class ContainerStyleFeatureNode(string name, string? value) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => context.EvaluateStyleFeature(name, value);
}

/// <summary>An operand of a style range: a custom property name, or a value.</summary>
internal readonly record struct ContainerStyleRangeOperand(string? CustomPropertyName, string? Value);

internal sealed class ContainerStyleRangeNode(ContainerStyleRangeOperand[] operands, ContainerQueryComparison[] comparisons) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => context.EvaluateStyleRange(operands, comparisons);
}

/// <summary>The scroll-state features (https://drafts.csswg.org/css-conditional-5/#scroll-state-container).</summary>
internal enum ContainerScrollStateFeature
{
    Stuck,
    Snapped,
    Scrollable,
    Scrolled,
}

/// <summary>
/// A scroll-state feature in a boolean context (without value: true when the state is not none), or with a value (a discrete
/// feature, true when the state matches the value). The state is the snapshot of the query container (see
/// <see cref="ContainerQueryContext.EvaluateScrollStateFeature"/>).
/// </summary>
internal sealed class ContainerScrollStateFeatureNode(ContainerScrollStateFeature feature, string? value) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => context.EvaluateScrollStateFeature(feature, value);
}

/// <summary>
/// The value of a <c>fallback</c> feature (https://drafts.csswg.org/css-anchor-position-2/#descdef-container-fallback): none, a
/// <c>@position-try</c> name and try tactics, or a &lt;position-area-query&gt; (one or two keywords, with <c>any</c>).
/// </summary>
internal sealed class AnchoredFallbackQuery
{
    private AnchoredFallbackQuery()
    {
    }

    /// <summary>Whether the value is <c>none</c>: no position option of <c>position-try-fallbacks</c> is applied.</summary>
    public bool IsNone { get; private init; }

    /// <summary>The <c>@position-try</c> name (not tree-scoped for the matching), or null.</summary>
    public string? RuleName { get; private init; }

    /// <summary>The try tactics, in order.</summary>
    public IReadOnlyList<Layout.TryTactic> Tactics { get; private init; } = [];

    /// <summary>The keywords of the &lt;position-area-query&gt;, or null.</summary>
    public IReadOnlyList<string>? PositionArea { get; private init; }

    /// <summary>Creates the value from its keywords (valid for the grammar of the feature; the keywords are lowercase, except the dashed idents).</summary>
    public static AnchoredFallbackQuery Create(List<string> keywords)
    {
        if (keywords is ["none"])
            return new AnchoredFallbackQuery { IsNone = true };

        string? ruleName = null;
        var tactics = new List<Layout.TryTactic>();
        foreach (var keyword in keywords)
        {
            if (keyword.StartsWith("--", StringComparison.Ordinal))
            {
                ruleName = keyword;
                continue;
            }

            Layout.TryTactic? tactic = keyword switch
            {
                "flip-block" => Layout.TryTactic.FlipBlock,
                "flip-inline" => Layout.TryTactic.FlipInline,
                "flip-start" => Layout.TryTactic.FlipStart,
                "flip-x" => Layout.TryTactic.FlipX,
                "flip-y" => Layout.TryTactic.FlipY,
                _ => null,
            };

            // A keyword that is neither a name nor a try tactic: the value is a <position-area-query>
            if (tactic is not { } value)
                return new AnchoredFallbackQuery { PositionArea = keywords };

            tactics.Add(value);
        }

        return new AnchoredFallbackQuery { RuleName = ruleName, Tactics = tactics };
    }
}

/// <summary>
/// The <c>fallback</c> feature of an anchored query (https://drafts.csswg.org/css-anchor-position-2/#anchored), in a boolean context
/// (without value: true when a position option of <c>position-try-fallbacks</c> is applied) or with a value. The applied position
/// option is the one chosen by the last layout of the query container (see <see cref="ContainerQueryContext.EvaluateAnchoredFallback"/>).
/// </summary>
internal sealed class ContainerAnchoredFallbackNode(AnchoredFallbackQuery? value) : ContainerQueryNode
{
    public override bool? Evaluate(ContainerQueryContext context) => context.EvaluateAnchoredFallback(value);
}
