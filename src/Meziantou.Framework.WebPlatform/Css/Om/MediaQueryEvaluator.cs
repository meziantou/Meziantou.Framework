using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>
/// The environment media queries are evaluated against (a virtual device: nothing is rendered).
/// https://drafts.csswg.org/mediaqueries-5/#media
/// </summary>
internal sealed record MediaEnvironment
{
    public static MediaEnvironment Default { get; } = new();

    /// <summary>The width of the viewport in CSS pixels (https://drafts.csswg.org/mediaqueries-5/#width).</summary>
    public double Width { get; init; } = 800;

    /// <summary>The height of the viewport in CSS pixels (https://drafts.csswg.org/mediaqueries-5/#height).</summary>
    public double Height { get; init; } = 600;

    /// <summary>The width of the screen in CSS pixels (https://drafts.csswg.org/mediaqueries-5/#device-width).</summary>
    public double DeviceWidth { get; init; } = 800;

    /// <summary>The height of the screen in CSS pixels (https://drafts.csswg.org/mediaqueries-5/#device-height).</summary>
    public double DeviceHeight { get; init; } = 600;

    /// <summary>The number of device pixels per CSS pixel (https://drafts.csswg.org/mediaqueries-5/#resolution).</summary>
    public double DevicePixelRatio { get; init; } = 1;

    /// <summary>The media type: "screen" or "print" (https://drafts.csswg.org/mediaqueries-5/#media-types).</summary>
    public string MediaType { get; init; } = "screen";

    /// <summary>The initial font size in CSS pixels, used to resolve the font-relative units (https://drafts.csswg.org/mediaqueries-5/#units).</summary>
    public double FontSize { get; init; } = 16;

    /// <summary>light | dark (https://drafts.csswg.org/mediaqueries-5/#prefers-color-scheme).</summary>
    public string PrefersColorScheme { get; init; } = "light";

    /// <summary>
    /// The page's supported color schemes (the <c>meta</c> element named <c>color-scheme</c>,
    /// https://html.spec.whatwg.org/multipage/semantics.html#meta-color-scheme), which are the color schemes of
    /// <c>color-scheme: normal</c>, or null.
    /// </summary>
    public string? PageColorSchemes { get; init; }

    /// <summary>no-preference | more | less | custom (https://drafts.csswg.org/mediaqueries-5/#prefers-contrast).</summary>
    public string PrefersContrast { get; init; } = "no-preference";

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#prefers-reduced-motion</summary>
    public bool PrefersReducedMotion { get; init; }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#prefers-reduced-transparency</summary>
    public bool PrefersReducedTransparency { get; init; }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#prefers-reduced-data</summary>
    public bool PrefersReducedData { get; init; }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#forced-colors</summary>
    public bool ForcedColors { get; init; }

    /// <summary>
    /// The color palette of the forced colors mode (https://drafts.csswg.org/css-color-adjust-1/#forced): the values of the
    /// system colors, whatever the color scheme of the elements; null when the mode is not active.
    /// </summary>
    public Cascade.ForcedColorsPalette? ForcedColorsPalette { get; init; }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#inverted</summary>
    public bool InvertedColors { get; init; }

    /// <summary>scripting: enabled when true, none otherwise (https://drafts.csswg.org/mediaqueries-5/#scripting).</summary>
    public bool ScriptingEnabled { get; init; }

    /// <summary>hover | none, also used by any-hover (https://drafts.csswg.org/mediaqueries-5/#hover).</summary>
    public string Hover { get; init; } = "hover";

    /// <summary>none | coarse | fine, also used by any-pointer (https://drafts.csswg.org/mediaqueries-5/#pointer).</summary>
    public string Pointer { get; init; } = "fine";

    /// <summary>The number of bits per color component, 0 for a non-color device (https://drafts.csswg.org/mediaqueries-5/#color).</summary>
    public int ColorBits { get; init; } = 8;

    /// <summary>The number of bits per pixel of a monochrome device, 0 for a color device (https://drafts.csswg.org/mediaqueries-5/#monochrome).</summary>
    public int MonochromeBits { get; init; }

    /// <summary>srgb | p3 | rec2020 (https://drafts.csswg.org/mediaqueries-5/#color-gamut).</summary>
    public string ColorGamut { get; init; } = "srgb";

    /// <summary>standard | high (https://drafts.csswg.org/mediaqueries-5/#dynamic-range).</summary>
    public string DynamicRange { get; init; } = "standard";

    /// <summary>fullscreen | standalone | minimal-ui | browser | picture-in-picture (https://drafts.csswg.org/mediaqueries-5/#display-mode).</summary>
    public string DisplayMode { get; init; } = "browser";

    /// <summary>
    /// Whether every media query list matches, whatever the other values of the environment: the cascade then uses the
    /// rules of all the media (e.g. to know the styles an element may have in any environment).
    /// </summary>
    public bool MatchAllMediaQueries { get; init; }
}

/// <summary>
/// Evaluates media queries against a <see cref="MediaEnvironment"/>
/// (https://drafts.csswg.org/mediaqueries-5/#mq-syntax, https://drafts.csswg.org/mediaqueries-5/#evaluating).
/// The results use a three-valued logic (true, false, unknown); a media query that evaluates to unknown does not match.
/// </summary>
internal static class MediaQueryEvaluator
{
    private const int MaxCacheSize = 4096;

    /// <summary>The tolerance used to compare numbers (unit conversions are not exact).</summary>
    private const double Epsilon = 1e-9;

    /// <summary>The resolution of the print media in dppx: 300dpi, as in Chromium.</summary>
    private const double PrintResolution = 300d / 96;

    private static readonly ConcurrentDictionary<string, MediaQuery?[]> Cache = new(StringComparer.Ordinal);
    private static readonly ConditionalWeakTable<List<CssComponentValue>, MediaQuery?[]> ComponentValueCache = new();

    private static readonly FrozenDictionary<string, MediaFeature> Features = CreateFeatures();

    /// <summary>The items of a MediaList (each already serialized, "not all" for invalid ones): true when the list is empty or any query matches.</summary>
    public static bool Matches(IReadOnlyList<string> mediaQueries, MediaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(mediaQueries);
        ArgumentNullException.ThrowIfNull(environment);
        if (mediaQueries.Count == 0)
            return true;

        foreach (var query in mediaQueries)
        {
            if (Matches(GetOrParse(query), environment))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Evaluates a length (a dimension or a math function, e.g. <c>50vw</c> or <c>calc(100vw - 10px)</c>) in CSS px with the
    /// environment, or returns null when the value is not a length (a unitless zero is a length).
    /// </summary>
    public static double? EvaluateLength(CssComponentValue value, MediaEnvironment environment)
    {
        if (CalcParser.ParseNumeric(value) is not { } node || node.Evaluate(environment) is not { } result)
            return null;

        if (result.Kind == CalcKind.Length)
            return result.Value;

        return result.Kind == CalcKind.Number && result.Value == 0 && node is CalcLeaf ? 0 : null;
    }

    /// <summary>A media query list as text (e.g. the media attribute or an @media prelude): empty/whitespace-only matches.</summary>
    public static bool Matches(string mediaQueryList, MediaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(mediaQueryList);
        ArgumentNullException.ThrowIfNull(environment);
        var queries = GetOrParse(mediaQueryList);
        return queries.Length == 0 || Matches(queries, environment);
    }

    /// <summary>
    /// A media query list as component values (split on top-level commas): an empty list matches.
    /// The parsed queries are cached per list instance, so the list must not be modified afterwards.
    /// </summary>
    public static bool Matches(List<CssComponentValue> values, MediaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(environment);
        var queries = ComponentValueCache.GetValue(values, ParseList);
        return queries.Length == 0 || Matches(queries, environment);
    }

    /// <summary>
    /// A <c>&lt;media-condition&gt;</c> as component values (e.g. the condition of a source size of the sizes attribute,
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-media-condition): an invalid condition (e.g. a media type) does not match.
    /// </summary>
    public static bool MatchesCondition(List<CssComponentValue> values, MediaEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(environment);
        if (ParseConditionQuery(ValueMatcher.RemoveWhitespace(values)) is not { } query)
            return false;

        return environment.MatchAllMediaQueries || query.Evaluate(environment) == true;
    }

    private static bool Matches(MediaQuery?[] queries, MediaEnvironment environment)
    {
        if (environment.MatchAllMediaQueries)
            return true;

        foreach (var query in queries)
        {
            // An invalid media query is "not all"; a media query that evaluates to unknown does not match
            if (query is not null && query.Evaluate(environment) == true)
                return true;
        }

        return false;
    }

    private static MediaQuery?[] GetOrParse(string text)
    {
        if (Cache.TryGetValue(text, out var queries))
            return queries;

        queries = ParseList(CssParser.ParseListOfComponentValues(text));
        if (Cache.Count >= MaxCacheSize)
        {
            Cache.Clear();
        }

        Cache.TryAdd(text, queries);
        return queries;
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-media-query-list: the invalid media queries are null.</summary>
    private static MediaQuery?[] ParseList(List<CssComponentValue> values)
    {
        if (!values.Exists(v => v is not CssPreservedToken { Type: CssTokenType.Whitespace }))
            return [];

        var result = new List<MediaQuery?>();
        var current = new List<CssComponentValue>();
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add(ParseQuery(ValueMatcher.RemoveWhitespace(current)));
                current.Clear();
            }
            else
            {
                current.Add(value);
            }
        }

        result.Add(ParseQuery(ValueMatcher.RemoveWhitespace(current)));
        return [.. result];
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-media-query:
    /// &lt;media-condition&gt; | [ not | only ]? &lt;media-type&gt; [ and &lt;media-condition-without-or&gt; ]?
    /// Returns null when the media query is invalid.
    /// </summary>
    private static MediaQuery? ParseQuery(List<CssComponentValue> tokens)
    {
        if (tokens.Count == 0)
            return null;

        if (tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident } firstToken)
            return ParseConditionQuery(tokens);

        var index = 0;
        var negated = false;
        var first = InfraAscii.ToAsciiLowercase(firstToken.Token.Value);
        if (first is "not" or "only")
        {
            // "not (...)" is a <media-condition>
            if (first == "not" && tokens.Count > 1 && tokens[1] is not CssPreservedToken { Type: CssTokenType.Ident })
                return ParseConditionQuery(tokens);

            negated = first == "not";
            index++;
        }

        if (index >= tokens.Count || tokens[index] is not CssPreservedToken { Type: CssTokenType.Ident } typeToken)
            return null;

        // https://drafts.csswg.org/mediaqueries-5/#typedef-media-type: the keywords only, not, and, or and layer are not media types
        var type = InfraAscii.ToAsciiLowercase(typeToken.Token.Value);
        if (type is "only" or "not" or "and" or "or" or "layer")
            return null;

        index++;
        Condition? condition = null;
        if (index < tokens.Count)
        {
            if (!(tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } and && and.Token.IsIdent("and")))
                return null;

            condition = ParseCondition(tokens, index + 1, allowOr: false, out var end);
            if (condition is null || end != tokens.Count)
                return null;
        }

        return new MediaQuery(negated, type, condition);
    }

    private static MediaQuery? ParseConditionQuery(List<CssComponentValue> tokens)
    {
        var condition = ParseCondition(tokens, 0, allowOr: true, out var end);
        return condition is not null && end == tokens.Count ? new MediaQuery(negated: false, mediaType: null, condition) : null;
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-media-condition:
    /// &lt;media-not&gt; | &lt;media-in-parens&gt; [ &lt;media-and&gt;* | &lt;media-or&gt;* ]
    /// </summary>
    private static Condition? ParseCondition(List<CssComponentValue> tokens, int index, bool allowOr, out int end)
    {
        end = index;
        if (index >= tokens.Count)
            return null;

        if (tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } not && not.Token.IsIdent("not"))
        {
            var inner = ParseInParens(tokens, index + 1);
            if (inner is null)
                return null;

            end = index + 2;
            return new NotCondition(inner);
        }

        var first = ParseInParens(tokens, index);
        if (first is null)
            return null;

        var conditions = new List<Condition> { first };
        index++;
        string? combinator = null;
        while (index < tokens.Count && tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } word && InfraAscii.ToAsciiLowercase(word.Token.Value) is "and" or "or")
        {
            // "and" and "or" cannot be mixed at the same level
            var keyword = InfraAscii.ToAsciiLowercase(word.Token.Value);
            if ((combinator is not null && combinator != keyword) || (keyword == "or" && !allowOr))
                return null;

            combinator = keyword;
            var next = ParseInParens(tokens, index + 1);
            if (next is null)
                return null;

            conditions.Add(next);
            index += 2;
        }

        end = index;
        if (conditions.Count == 1)
            return first;

        return combinator == "and" ? new AndCondition([.. conditions]) : new OrCondition([.. conditions]);
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-media-in-parens: ( &lt;media-condition&gt; ) | ( &lt;media-feature&gt; ) | &lt;general-enclosed&gt;
    /// Returns null on a syntax error.
    /// </summary>
    private static Condition? ParseInParens(List<CssComponentValue> tokens, int index)
    {
        if (index >= tokens.Count)
            return null;

        switch (tokens[index])
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
            {
                if (!IsAnyValue(block.Value))
                    return null;

                var inner = ValueMatcher.RemoveWhitespace(block.Value);
                if (ParseCondition(inner, 0, allowOr: true, out var end) is { } condition && end == inner.Count)
                    return condition;

                // A block that is not a valid <media-feature> is a <general-enclosed>: it evaluates to unknown
                return ParseFeature(inner);
            }

            case CssFunction function:
                // https://drafts.csswg.org/mediaqueries-5/#typedef-general-enclosed
                return IsAnyValue(function.Value) ? UnknownCondition.Instance : null;

            default:
                return null;
        }
    }

    /// <summary>https://drafts.csswg.org/css-syntax-3/#typedef-any-value: no bad-string, bad-url or unmatched closing bracket.</summary>
    internal static bool IsAnyValue(List<CssComponentValue> values)
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

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-media-feature: [ &lt;mf-plain&gt; | &lt;mf-boolean&gt; | &lt;mf-range&gt; ].
    /// An unknown feature name, an invalid value or an invalid syntax results in <see cref="UnknownCondition"/> (the block is a &lt;general-enclosed&gt;).
    /// </summary>
    private static Condition ParseFeature(List<CssComponentValue> tokens)
    {
        if (tokens.Count == 0)
            return UnknownCondition.Instance;

        if (tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } nameToken)
        {
            var name = InfraAscii.ToAsciiLowercase(nameToken.Token.Value);

            // <mf-boolean>: the min-/max- prefixes are not allowed
            if (tokens.Count == 1)
                return Features.TryGetValue(name, out var feature) ? new BooleanFeatureCondition(feature) : UnknownCondition.Instance;

            // <mf-plain>
            if (tokens[1] is CssPreservedToken { Type: CssTokenType.Colon })
                return ParsePlainFeature(name, tokens[2..]);
        }

        return ParseRangeFeature(tokens);
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-mf-plain: &lt;mf-name&gt; : &lt;mf-value&gt;</summary>
    private static Condition ParsePlainFeature(string name, List<CssComponentValue> valueTokens)
    {
        var value = ParseValue(valueTokens);
        if (value is null)
            return UnknownCondition.Instance;

        if (Features.TryGetValue(name, out var feature))
        {
            if (feature.IsRange)
                return new RangeFeatureCondition(feature, [(Comparison.Equal, value)]);

            if (feature.Type == MediaFeatureType.Boolean)
                return value is NumericValue ? new DiscreteFeatureCondition(feature, value) : UnknownCondition.Instance;

            return value is IdentValue ident && feature.Keywords is { } keywords && keywords.Contains(ident.Value, StringComparer.Ordinal) ? new DiscreteFeatureCondition(feature, value) : UnknownCondition.Instance;
        }

        // https://drafts.csswg.org/mediaqueries-5/#mq-min-max: only "range" features accept the min-/max- prefixes
        Comparison comparison;
        string unprefixedName;
        if (name is "-webkit-min-device-pixel-ratio" or "-webkit-max-device-pixel-ratio")
        {
            // https://compat.spec.whatwg.org/#css-media-queries-webkit-device-pixel-ratio
            comparison = name.StartsWith("-webkit-min-", StringComparison.Ordinal) ? Comparison.GreaterOrEqual : Comparison.LessOrEqual;
            unprefixedName = "-webkit-device-pixel-ratio";
        }
        else if (name.StartsWith("min-", StringComparison.Ordinal) || name.StartsWith("max-", StringComparison.Ordinal))
        {
            comparison = name.StartsWith("min-", StringComparison.Ordinal) ? Comparison.GreaterOrEqual : Comparison.LessOrEqual;
            unprefixedName = name[4..];
        }
        else
        {
            return UnknownCondition.Instance;
        }

        if (!Features.TryGetValue(unprefixedName, out feature) || !feature.IsRange)
            return UnknownCondition.Instance;

        return new RangeFeatureCondition(feature, [(comparison, value)]);
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-mf-range:
    /// &lt;mf-name&gt; &lt;mf-comparison&gt; &lt;mf-value&gt; | &lt;mf-value&gt; &lt;mf-comparison&gt; &lt;mf-name&gt;
    /// | &lt;mf-value&gt; &lt;mf-lt&gt; &lt;mf-name&gt; &lt;mf-lt&gt; &lt;mf-value&gt; | &lt;mf-value&gt; &lt;mf-gt&gt; &lt;mf-name&gt; &lt;mf-gt&gt; &lt;mf-value&gt;
    /// </summary>
    private static Condition ParseRangeFeature(List<CssComponentValue> tokens)
    {
        var parts = new List<List<CssComponentValue>> { new() };
        var comparisons = new List<Comparison>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is CssPreservedToken { Token: { Type: CssTokenType.Delim } delim } delimToken && delim.Value is "<" or ">" or "=")
            {
                // "<=" and ">=" are two delim tokens with no whitespace in between
                var orEqual = false;
                if (delim.Value is "<" or ">" && i + 1 < tokens.Count && tokens[i + 1] is CssPreservedToken equals && equals.Token.IsDelim('=') && equals.Start == delimToken.End)
                {
                    orEqual = true;
                    i++;
                }

                comparisons.Add(delim.Value switch
                {
                    "<" => orEqual ? Comparison.LessOrEqual : Comparison.Less,
                    ">" => orEqual ? Comparison.GreaterOrEqual : Comparison.Greater,
                    _ => Comparison.Equal,
                });
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(tokens[i]);
            }
        }

        if (parts.Exists(p => p.Count == 0))
            return UnknownCondition.Instance;

        if (comparisons.Count == 1)
        {
            // <mf-name> <mf-comparison> <mf-value>
            if (parts[0] is [CssPreservedToken { Type: CssTokenType.Ident } left] && Features.ContainsKey(InfraAscii.ToAsciiLowercase(left.Token.Value)))
                return CreateRangeFeature(InfraAscii.ToAsciiLowercase(left.Token.Value), (comparisons[0], parts[1]));

            // <mf-value> <mf-comparison> <mf-name>: "value < name" is "name > value"
            if (parts[1] is [CssPreservedToken { Type: CssTokenType.Ident } right])
                return CreateRangeFeature(InfraAscii.ToAsciiLowercase(right.Token.Value), (Reverse(comparisons[0]), parts[0]));

            return UnknownCondition.Instance;
        }

        if (comparisons.Count == 2)
        {
            // Both comparisons must be in the same direction
            var first = comparisons[0];
            var second = comparisons[1];
            var lessThan = first is Comparison.Less or Comparison.LessOrEqual;
            if (first == Comparison.Equal || second == Comparison.Equal || lessThan != second is Comparison.Less or Comparison.LessOrEqual)
                return UnknownCondition.Instance;

            if (parts[1] is not [CssPreservedToken { Type: CssTokenType.Ident } middle])
                return UnknownCondition.Instance;

            return CreateRangeFeature(InfraAscii.ToAsciiLowercase(middle.Token.Value), (Reverse(first), parts[0]), (second, parts[2]));
        }

        return UnknownCondition.Instance;
    }

    private static Condition CreateRangeFeature(string name, params ReadOnlySpan<(Comparison Comparison, List<CssComponentValue> Value)> comparisons)
    {
        // The min-/max- prefixes are not allowed in the range syntax
        if (!Features.TryGetValue(name, out var feature) || !feature.IsRange)
            return UnknownCondition.Instance;

        var result = new (Comparison Comparison, MediaFeatureValue Value)[comparisons.Length];
        for (var i = 0; i < comparisons.Length; i++)
        {
            var value = ParseValue(comparisons[i].Value);
            if (value is null)
                return UnknownCondition.Instance;

            result[i] = (comparisons[i].Comparison, value);
        }

        return new RangeFeatureCondition(feature, result);
    }

    private static Comparison Reverse(Comparison comparison) => comparison switch
    {
        Comparison.Less => Comparison.Greater,
        Comparison.LessOrEqual => Comparison.GreaterOrEqual,
        Comparison.Greater => Comparison.Less,
        Comparison.GreaterOrEqual => Comparison.LessOrEqual,
        _ => comparison,
    };

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-mf-value: &lt;number&gt; | &lt;dimension&gt; | &lt;ident&gt; | &lt;ratio&gt;</summary>
    private static MediaFeatureValue? ParseValue(List<CssComponentValue> tokens)
    {
        switch (tokens)
        {
            case [CssPreservedToken { Type: CssTokenType.Ident } ident]:
                return new IdentValue(InfraAscii.ToAsciiLowercase(ident.Token.Value));

            case [var single]:
                return CalcParser.ParseNumeric(single) is { } expression ? new NumericValue(expression) : null;

            // https://drafts.csswg.org/css-values-4/#ratio-value: <number [0,∞]> [ / <number [0,∞]> ]?
            case [var numerator, CssPreservedToken { Token: { Type: CssTokenType.Delim, Value: "/" } }, var denominator]:
                return CalcParser.ParseNumeric(numerator) is { } left && CalcParser.ParseNumeric(denominator) is { } right ? new RatioValue(left, right) : null;

            default:
                return null;
        }
    }

    private static bool CompareValues(double featureValue, Comparison comparison, double value)
    {
        if (double.IsNaN(featureValue) || double.IsNaN(value))
            return false;

        var result = featureValue.CompareTo(value);
        if (result != 0 && double.IsFinite(featureValue) && double.IsFinite(value) && Math.Abs(featureValue - value) <= Epsilon * Math.Max(1, Math.Max(Math.Abs(featureValue), Math.Abs(value))))
        {
            result = 0;
        }

        return comparison switch
        {
            Comparison.Less => result < 0,
            Comparison.LessOrEqual => result <= 0,
            Comparison.Greater => result > 0,
            Comparison.GreaterOrEqual => result >= 0,
            _ => result == 0,
        };
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#evaluating: Kleene's three-valued logic (null is unknown).</summary>
    private static bool? And(bool? left, bool? right)
    {
        if (left == false || right == false)
            return false;

        return left == true && right == true ? true : null;
    }

    private static bool? Or(bool? left, bool? right)
    {
        if (left == true || right == true)
            return true;

        return left == false && right == false ? false : null;
    }

    private static bool? Not(bool? value) => value is null ? null : !value.Value;

    private static bool IsPrint(MediaEnvironment environment) => InfraAscii.EqualsIgnoreAsciiCase(environment.MediaType, "print");

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#media-types: the deprecated and unknown media types match nothing.</summary>
    private static bool MatchesMediaType(string type, MediaEnvironment environment)
        => type == "all" || (type is "screen" or "print" && InfraAscii.EqualsIgnoreAsciiCase(type, environment.MediaType));

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#mq-features</summary>
    private static FrozenDictionary<string, MediaFeature> CreateFeatures()
    {
        static int GamutRank(string gamut) => InfraAscii.ToAsciiLowercase(gamut) switch
        {
            "srgb" => 0,
            "p3" => 1,
            "rec2020" => 2,
            _ => -1,
        };

        var colorGamut = new MediaFeature(MediaFeatureType.Discrete)
        {
            // A device that covers the p3 gamut also covers srgb (https://drafts.csswg.org/mediaqueries-5/#color-gamut)
            Keywords = ["srgb", "p3", "rec2020"],
            MatchKeyword = (environment, keyword) => environment.ColorBits > 0 && GamutRank(environment.ColorGamut) >= GamutRank(keyword),
            BooleanContext = environment => environment.ColorBits > 0 && GamutRank(environment.ColorGamut) >= 0,
        };

        var dynamicRange = new MediaFeature(MediaFeatureType.Discrete)
        {
            // "high" implies "standard" (https://drafts.csswg.org/mediaqueries-5/#dynamic-range)
            Keywords = ["standard", "high"],
            MatchKeyword = (environment, keyword) => keyword == "standard" || InfraAscii.EqualsIgnoreAsciiCase(environment.DynamicRange, "high"),
            BooleanContext = _ => true,
        };

        var hover = Keywords(["none", "hover"], environment => environment.Hover);
        var pointer = Keywords(["none", "coarse", "fine"], environment => environment.Pointer);

        var features = new Dictionary<string, MediaFeature>(StringComparer.Ordinal)
        {
            // https://drafts.csswg.org/mediaqueries-5/#mf-dimensions
            ["width"] = Range(MediaFeatureType.Length, environment => environment.Width),
            ["height"] = Range(MediaFeatureType.Length, environment => environment.Height),
            ["aspect-ratio"] = Range(MediaFeatureType.Ratio, environment => environment.Width / environment.Height),
            ["orientation"] = Keywords(["portrait", "landscape"], environment => environment.Height >= environment.Width ? "portrait" : "landscape"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-deprecated
            ["device-width"] = Range(MediaFeatureType.Length, environment => environment.DeviceWidth),
            ["device-height"] = Range(MediaFeatureType.Length, environment => environment.DeviceHeight),
            ["device-aspect-ratio"] = Range(MediaFeatureType.Ratio, environment => environment.DeviceWidth / environment.DeviceHeight),

            // https://drafts.csswg.org/mediaqueries-5/#mf-display-quality: the resolution of the output device. As in Chromium, the
            // resolution of the print media is 300dpi, the minimum of the printers, whatever the screen
            ["resolution"] = Range(MediaFeatureType.Resolution, environment => environment.MediaType == "print" ? PrintResolution : environment.DevicePixelRatio),
            ["scan"] = new MediaFeature(MediaFeatureType.Discrete)
            {
                // The scanning process only applies to the "tv" devices (as in the browsers): the screens and the printers match no value
                Keywords = ["interlace", "progressive"],
                MatchKeyword = (_, _) => false,
                BooleanContext = _ => false,
            },
            ["grid"] = Range(MediaFeatureType.Boolean, _ => 0),
            ["update"] = Keywords(["none", "slow", "fast"], environment => IsPrint(environment) ? "none" : "fast"),
            ["overflow-block"] = Keywords(["none", "scroll", "paged"], environment => IsPrint(environment) ? "paged" : "scroll"),
            ["overflow-inline"] = Keywords(["none", "scroll"], environment => IsPrint(environment) ? "none" : "scroll"),
            ["environment-blending"] = Keywords(["opaque", "additive", "subtractive"], _ => "opaque"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-colors
            ["color"] = Range(MediaFeatureType.Integer, environment => environment.ColorBits),
            ["color-index"] = Range(MediaFeatureType.Integer, _ => 0),
            ["monochrome"] = Range(MediaFeatureType.Integer, environment => environment.MonochromeBits),
            ["color-gamut"] = colorGamut,
            ["video-color-gamut"] = colorGamut,
            ["dynamic-range"] = dynamicRange,
            ["video-dynamic-range"] = dynamicRange,
            ["inverted-colors"] = Keywords(["none", "inverted"], environment => environment.InvertedColors ? "inverted" : "none"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-interaction
            ["pointer"] = pointer,
            ["any-pointer"] = pointer,
            ["hover"] = hover,
            ["any-hover"] = hover,
            ["nav-controls"] = Keywords(["none", "back"], _ => "back"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-user-preferences
            ["prefers-reduced-motion"] = Keywords(["no-preference", "reduce"], environment => environment.PrefersReducedMotion ? "reduce" : "no-preference"),
            ["prefers-reduced-transparency"] = Keywords(["no-preference", "reduce"], environment => environment.PrefersReducedTransparency ? "reduce" : "no-preference"),
            ["prefers-contrast"] = Keywords(["no-preference", "less", "more", "custom"], environment => environment.PrefersContrast),
            ["forced-colors"] = Keywords(["none", "active"], environment => environment.ForcedColors ? "active" : "none"),
            ["prefers-color-scheme"] = Keywords(["light", "dark"], environment => environment.PrefersColorScheme),
            ["ua-color-scheme"] = Keywords(["light", "dark"], environment => environment.PrefersColorScheme),
            ["prefers-reduced-data"] = Keywords(["no-preference", "reduce"], environment => environment.PrefersReducedData ? "reduce" : "no-preference"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-scripting
            ["scripting"] = Keywords(["none", "initial-only", "enabled"], environment => environment.ScriptingEnabled ? "enabled" : "none"),

            // https://w3c.github.io/manifest/#the-display-mode-media-feature, https://drafts.csswg.org/mediaqueries-5/#display-modes
            ["display-mode"] = Keywords(["fullscreen", "standalone", "minimal-ui", "browser", "picture-in-picture"], environment => environment.DisplayMode),
            ["display-state"] = Keywords(["normal", "minimized", "maximized", "fullscreen"], _ => "normal"),
            ["resizable"] = Keywords(["true", "false"], _ => "true"),
            ["shape"] = Keywords(["rect", "round"], _ => "rect"),

            // https://drafts.csswg.org/mediaqueries-5/#mf-horizontal-viewport-segments
            ["horizontal-viewport-segments"] = Range(MediaFeatureType.Integer, _ => 1),
            ["vertical-viewport-segments"] = Range(MediaFeatureType.Integer, _ => 1),

            // https://www.w3.org/TR/device-posture/#the-device-posture-media-feature
            ["device-posture"] = Keywords(["continuous", "folded"], _ => "continuous"),

            // https://compat.spec.whatwg.org/#css-media-queries-webkit-device-pixel-ratio
            ["-webkit-device-pixel-ratio"] = Range(MediaFeatureType.Number, environment => environment.DevicePixelRatio),

            // https://compat.spec.whatwg.org/#css-media-queries-webkit-transform-3d
            ["-webkit-transform-3d"] = Range(MediaFeatureType.Boolean, _ => 1),
        };

        return features.ToFrozenDictionary(StringComparer.Ordinal);

        static MediaFeature Range(MediaFeatureType type, Func<MediaEnvironment, double> value) => new(type) { Value = value };

        static MediaFeature Keywords(string[] keywords, Func<MediaEnvironment, string> current) => new(MediaFeatureType.Discrete)
        {
            Keywords = keywords,
            MatchKeyword = (environment, keyword) => InfraAscii.EqualsIgnoreAsciiCase(current(environment), keyword),

            // https://drafts.csswg.org/mediaqueries-5/#mq-boolean-context: none, no-preference (and false) are false
            BooleanContext = environment => current(environment) is var value
                && !InfraAscii.EqualsIgnoreAsciiCase(value, "none")
                && !InfraAscii.EqualsIgnoreAsciiCase(value, "no-preference")
                && !InfraAscii.EqualsIgnoreAsciiCase(value, "false"),
        };
    }

    private enum MediaFeatureType
    {
        /// <summary>A "discrete" feature whose values are keywords.</summary>
        Discrete,

        /// <summary>A "discrete" feature whose value is &lt;mq-boolean&gt; (0 or 1).</summary>
        Boolean,

        Length,
        Ratio,
        Resolution,
        Integer,
        Number,
    }

    private enum Comparison
    {
        Equal,
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual,
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#media-feature</summary>
    private sealed class MediaFeature(MediaFeatureType type)
    {
        public MediaFeatureType Type { get; } = type;

        /// <summary>Whether this is a "range" feature: it accepts the min-/max- prefixes and the range syntax.</summary>
        public bool IsRange => Type is not MediaFeatureType.Discrete and not MediaFeatureType.Boolean;

        /// <summary>The value of the numeric features.</summary>
        public Func<MediaEnvironment, double>? Value { get; init; }

        /// <summary>The keywords of the discrete features.</summary>
        public string[]? Keywords { get; init; }

        public Func<MediaEnvironment, string, bool>? MatchKeyword { get; init; }

        public Func<MediaEnvironment, bool>? BooleanContext { get; init; }

        /// <summary>https://drafts.csswg.org/mediaqueries-5/#mq-boolean-context</summary>
        public bool EvaluateInBooleanContext(MediaEnvironment environment)
        {
            if (BooleanContext is not null)
                return BooleanContext(environment);

            var value = Value!(environment);
            return !double.IsNaN(value) && value != 0;
        }

        /// <summary>Resolves the value of a query for this feature (in CSS px, dppx or numbers); null when the value is invalid.</summary>
        public double? Resolve(MediaFeatureValue value, MediaEnvironment environment)
        {
            switch (Type)
            {
                case MediaFeatureType.Length when value is NumericValue numeric:
                {
                    // A unitless zero is a valid <length>
                    if (numeric.Expression is CalcLeaf { Unit: null, Value: 0 })
                        return 0;

                    return EvaluateAs(numeric.Expression, CalcKind.Length, environment);
                }

                case MediaFeatureType.Resolution when value is IdentValue { Value: "infinite" }:
                    return double.PositiveInfinity;

                // A <resolution> is not negative (https://drafts.csswg.org/css-values-4/#resolution)
                case MediaFeatureType.Resolution when value is NumericValue numeric:
                    return numeric.Expression is CalcLeaf { Value: < 0 } ? null : EvaluateAs(numeric.Expression, CalcKind.Resolution, environment);

                case MediaFeatureType.Number when value is NumericValue numeric:
                    return EvaluateAs(numeric.Expression, CalcKind.Number, environment);

                case MediaFeatureType.Integer or MediaFeatureType.Boolean when value is NumericValue numeric:
                {
                    if (numeric.Expression is CalcLeaf { Unit: null } leaf)
                        return leaf.IsInteger ? leaf.Value : null;

                    // A math function is rounded to the nearest integer (https://drafts.csswg.org/css-values-4/#calc-range)
                    return EvaluateAs(numeric.Expression, CalcKind.Number, environment) is { } rounded ? Math.Floor(rounded + 0.5) : null;
                }

                // https://drafts.csswg.org/css-values-4/#ratio-value: a single <number> is n/1; 1/0 is infinite, and so is the
                // degenerate ratio 0/0 (it behaves as 1/0)
                case MediaFeatureType.Ratio when value is NumericValue numeric:
                    return EvaluateAs(numeric.Expression, CalcKind.Number, environment) is >= 0 and var number ? number : null;

                case MediaFeatureType.Ratio when value is RatioValue ratio:
                {
                    var numerator = EvaluateAs(ratio.Numerator, CalcKind.Number, environment);
                    var denominator = EvaluateAs(ratio.Denominator, CalcKind.Number, environment);
                    if (numerator is not >= 0 || denominator is not >= 0)
                        return null;

                    return numerator == 0 && denominator == 0 ? double.PositiveInfinity : numerator / denominator;
                }

                default:
                    return null;
            }
        }

        private static double? EvaluateAs(CalcNode expression, CalcKind kind, MediaEnvironment environment)
            => expression.Evaluate(environment) is { } result && result.Kind == kind ? result.Value : null;
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-media-query</summary>
    private sealed class MediaQuery(bool negated, string? mediaType, Condition? condition)
    {
        public bool? Evaluate(MediaEnvironment environment)
        {
            bool? result = mediaType is null || MatchesMediaType(mediaType, environment);
            if (condition is not null)
            {
                result = And(result, condition.Evaluate(environment));
            }

            return negated ? Not(result) : result;
        }
    }

    private abstract class Condition
    {
        public abstract bool? Evaluate(MediaEnvironment environment);
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#typedef-general-enclosed (and the unknown media features): unknown.</summary>
    private sealed class UnknownCondition : Condition
    {
        public static UnknownCondition Instance { get; } = new();

        public override bool? Evaluate(MediaEnvironment environment) => null;
    }

    private sealed class NotCondition(Condition condition) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment) => Not(condition.Evaluate(environment));
    }

    private sealed class AndCondition(Condition[] conditions) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment)
        {
            bool? result = true;
            foreach (var condition in conditions)
            {
                result = And(result, condition.Evaluate(environment));
                if (result == false)
                    return false;
            }

            return result;
        }
    }

    private sealed class OrCondition(Condition[] conditions) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment)
        {
            bool? result = false;
            foreach (var condition in conditions)
            {
                result = Or(result, condition.Evaluate(environment));
                if (result == true)
                    return true;
            }

            return result;
        }
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-5/#mq-boolean-context</summary>
    private sealed class BooleanFeatureCondition(MediaFeature feature) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment) => feature.EvaluateInBooleanContext(environment);
    }

    /// <summary>A discrete feature in the plain syntax (https://drafts.csswg.org/mediaqueries-5/#mq-features).</summary>
    private sealed class DiscreteFeatureCondition(MediaFeature feature, MediaFeatureValue value) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment)
        {
            if (value is IdentValue ident)
                return feature.MatchKeyword!(environment, ident.Value);

            // <mq-boolean> = <integer [0,1]>
            var number = feature.Resolve(value, environment);
            if (number is not (0d or 1d))
                return null;

            return CompareValues(feature.Value!(environment), Comparison.Equal, number.Value);
        }
    }

    /// <summary>A range feature in the plain syntax (possibly with the min-/max- prefixes) or in the range syntax: "feature comparison value" for each comparison.</summary>
    private sealed class RangeFeatureCondition(MediaFeature feature, (Comparison Comparison, MediaFeatureValue Value)[] comparisons) : Condition
    {
        public override bool? Evaluate(MediaEnvironment environment)
        {
            var featureValue = feature.Value!(environment);
            var result = true;
            foreach (var (comparison, value) in comparisons)
            {
                if (feature.Resolve(value, environment) is not { } resolved)
                    return null;

                result &= CompareValues(featureValue, comparison, resolved);
            }

            return result;
        }
    }

    private abstract class MediaFeatureValue;

    private sealed class IdentValue(string value) : MediaFeatureValue
    {
        public string Value { get; } = value;
    }

    private sealed class NumericValue(CalcNode expression) : MediaFeatureValue
    {
        public CalcNode Expression { get; } = expression;
    }

    private sealed class RatioValue(CalcNode numerator, CalcNode denominator) : MediaFeatureValue
    {
        public CalcNode Numerator { get; } = numerator;

        public CalcNode Denominator { get; } = denominator;
    }

    /// <summary>
    /// The type of a calculation (https://drafts.csswg.org/css-values-4/#calc-type-checking): the powers of the length (in
    /// CSS px) and resolution (in dppx) base types. Products and quotients combine the powers (e.g. <c>1vh * 1em / 1px</c> is
    /// a length), so intermediate values can have any power; only the final type must match the context.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CalcKind(int LengthPower, int ResolutionPower)
    {
        public static CalcKind Number => default;

        /// <summary>A length in CSS px.</summary>
        public static CalcKind Length => new(1, 0);

        /// <summary>A resolution in dppx.</summary>
        public static CalcKind Resolution => new(0, 1);

        /// <summary>https://drafts.csswg.org/css-values-4/#cssnumericvalue-multiply-two-types</summary>
        public CalcKind Multiply(CalcKind other) => new(LengthPower + other.LengthPower, ResolutionPower + other.ResolutionPower);

        /// <summary>https://drafts.csswg.org/css-values-4/#cssnumericvalue-invert-a-type</summary>
        public CalcKind Invert() => new(-LengthPower, -ResolutionPower);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct CalcValue(double Value, CalcKind Kind);

    /// <summary>A numeric value: a number, a dimension or a math function (https://drafts.csswg.org/css-values-4/#math).</summary>
    private abstract class CalcNode
    {
        /// <summary>Returns null when the expression cannot be evaluated (e.g. incompatible types).</summary>
        public abstract CalcValue? Evaluate(MediaEnvironment environment);
    }

    private sealed class CalcLeaf(double value, string? unit, bool isInteger) : CalcNode
    {
        public double Value { get; } = value;

        /// <summary>The lowercase unit of a dimension, null for a number.</summary>
        public string? Unit { get; } = unit;

        public bool IsInteger { get; } = isInteger;

        public override CalcValue? Evaluate(MediaEnvironment environment)
        {
            if (Unit is null)
                return new CalcValue(Value, CalcKind.Number);

            if (GetResolutionFactor(Unit) is { } resolution)
                return new CalcValue(Value * resolution, CalcKind.Resolution);

            return GetLengthFactor(Unit, environment) is { } length ? new CalcValue(Value * length, CalcKind.Length) : null;
        }

        /// <summary>https://drafts.csswg.org/css-values-4/#resolution: the number of dppx in one unit.</summary>
        public static double? GetResolutionFactor(string unit) => unit switch
        {
            "dppx" or "x" => 1,
            "dpi" => 1d / 96,
            "dpcm" => 2.54 / 96,
            _ => null,
        };

        /// <summary>
        /// https://drafts.csswg.org/css-values-4/#lengths: the number of CSS px in one unit.
        /// In media queries the font-relative units use the initial font (https://drafts.csswg.org/mediaqueries-5/#units),
        /// and the container query units fall back to the small viewport units.
        /// </summary>
        public static double? GetLengthFactor(string unit, MediaEnvironment environment)
        {
            var fontSize = environment.FontSize;
            var width = environment.Width / 100;
            var height = environment.Height / 100;
            return unit switch
            {
                "px" => 1,
                "cm" => 96 / 2.54,
                "mm" => 96 / 25.4,
                "q" => 96 / 101.6,
                "in" => 96,
                "pt" => 96d / 72,
                "pc" => 16,
                "em" or "rem" => fontSize,
                "ex" or "rex" => fontSize * Fonts.BundledFonts.DefaultRatios.XHeight,
                "ch" or "rch" => fontSize * Fonts.BundledFonts.DefaultRatios.ZeroAdvance,
                "cap" or "rcap" => fontSize * Fonts.BundledFonts.DefaultRatios.CapHeight,
                "ic" or "ric" => fontSize * Fonts.BundledFonts.DefaultRatios.IdeographicAdvance,
                "lh" or "rlh" => fontSize * Fonts.BundledFonts.DefaultRatios.NormalLineHeight,

                // Horizontal writing mode: the inline axis is the horizontal axis
                "vw" or "svw" or "lvw" or "dvw" or "vi" or "svi" or "lvi" or "dvi" or "cqw" or "cqi" => width,
                "vh" or "svh" or "lvh" or "dvh" or "vb" or "svb" or "lvb" or "dvb" or "cqh" or "cqb" => height,
                "vmin" or "svmin" or "lvmin" or "dvmin" or "cqmin" => Math.Min(width, height),
                "vmax" or "svmax" or "lvmax" or "dvmax" or "cqmax" => Math.Max(width, height),
                _ => null,
            };
        }
    }

    private sealed class CalcBinary(char op, CalcNode left, CalcNode right) : CalcNode
    {
        /// <summary>https://drafts.csswg.org/css-values-4/#calc-type-checking</summary>
        public override CalcValue? Evaluate(MediaEnvironment environment)
        {
            if (left.Evaluate(environment) is not { } l || right.Evaluate(environment) is not { } r)
                return null;

            return op switch
            {
                '+' when l.Kind == r.Kind => new CalcValue(l.Value + r.Value, l.Kind),
                '-' when l.Kind == r.Kind => new CalcValue(l.Value - r.Value, l.Kind),
                '*' => new CalcValue(l.Value * r.Value, l.Kind.Multiply(r.Kind)),
                '/' => new CalcValue(l.Value / r.Value, l.Kind.Multiply(r.Kind.Invert())),
                _ => null,
            };
        }
    }

    /// <summary>https://drafts.csswg.org/css-values-4/#math-function</summary>
    private sealed class CalcFunction(string name, CalcNode[] arguments) : CalcNode
    {
        public override CalcValue? Evaluate(MediaEnvironment environment)
        {
            var values = new CalcValue[arguments.Length];
            for (var i = 0; i < arguments.Length; i++)
            {
                // All the arguments must have the same type
                if (arguments[i].Evaluate(environment) is not { } value || (i > 0 && value.Kind != values[0].Kind))
                    return null;

                values[i] = value;
            }

            var kind = values[0].Kind;
            switch (name)
            {
                case "calc":
                    return values[0];

                case "min":
                {
                    var result = values[0].Value;
                    for (var i = 1; i < values.Length; i++)
                    {
                        result = Math.Min(result, values[i].Value);
                    }

                    return new CalcValue(result, kind);
                }

                case "max":
                {
                    var result = values[0].Value;
                    for (var i = 1; i < values.Length; i++)
                    {
                        result = Math.Max(result, values[i].Value);
                    }

                    return new CalcValue(result, kind);
                }

                case "clamp":
                    return new CalcValue(Math.Max(values[0].Value, Math.Min(values[1].Value, values[2].Value)), kind);

                case "abs":
                    return new CalcValue(Math.Abs(values[0].Value), kind);

                case "sign":
                {
                    var value = values[0].Value;
                    return new CalcValue(value > 0 ? 1 : value < 0 ? -1 : value, CalcKind.Number);
                }

                default:
                    return null;
            }
        }
    }

    /// <summary>
    /// An env() function (https://drafts.csswg.org/css-env-1/#env-function): environment variables can be used in media
    /// queries. The variable, or its fallback, is resolved with the environment.
    /// </summary>
    private sealed class CalcEnvironmentVariable(CssFunction function) : CalcNode
    {
        public override CalcValue? Evaluate(MediaEnvironment environment)
        {
            if (ResolveEnvironmentVariable(function, environment) is not { } values || ValueMatcher.RemoveWhitespace(values) is not [var value])
                return null;

            return CalcParser.ParseNumeric(value)?.Evaluate(environment);
        }
    }

    /// <summary>
    /// Returns the component values of an env() function (https://drafts.csswg.org/css-env-1/#env-function): the value of the
    /// variable, or the fallback (whose env() functions are resolved), or null when the variable is unknown and has no
    /// fallback. The indices of the two-dimensional variables (viewport-segment-*) are not supported.
    /// </summary>
    internal static List<CssComponentValue>? ResolveEnvironmentVariable(CssFunction function, MediaEnvironment environment)
    {
        var comma = function.Value.FindIndex(v => v is CssPreservedToken { Type: CssTokenType.Comma });
        var name = ValueMatcher.RemoveWhitespace(comma < 0 ? function.Value : function.Value[..comma]);
        if (name is [CssPreservedToken { Type: CssTokenType.Ident } nameToken] && Cascade.CssSubstitution.GetEnvironmentVariable(nameToken.Token.Value, environment) is { } text)
            return CssParser.ParseListOfComponentValues(text);

        return comma < 0 ? null : ResolveEnvironmentVariables(function.Value[(comma + 1)..], environment);
    }

    /// <summary>Replaces the env() functions of component values (see <see cref="ResolveEnvironmentVariable"/>); null when one of them has no value.</summary>
    internal static List<CssComponentValue>? ResolveEnvironmentVariables(List<CssComponentValue> values, MediaEnvironment environment)
    {
        var result = new List<CssComponentValue>(values.Count);
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "env"):
                {
                    if (ResolveEnvironmentVariable(function, environment) is not { } replacement)
                        return null;

                    result.AddRange(replacement);
                    break;
                }

                case CssFunction function:
                {
                    if (ResolveEnvironmentVariables(function.Value, environment) is not { } arguments)
                        return null;

                    var copy = new CssFunction(function.Name) { Start = function.Start, End = function.End };
                    copy.Value.AddRange(arguments);
                    result.Add(copy);
                    break;
                }

                case CssSimpleBlock block:
                {
                    if (ResolveEnvironmentVariables(block.Value, environment) is not { } blockValues)
                        return null;

                    var copy = new CssSimpleBlock(block.AssociatedToken) { Start = block.Start, End = block.End };
                    copy.Value.AddRange(blockValues);
                    result.Add(copy);
                    break;
                }

                default:
                    result.Add(value);
                    break;
            }
        }

        return result;
    }

    /// <summary>Parses math functions (https://drafts.csswg.org/css-values-4/#calc-syntax).</summary>
    private sealed class CalcParser
    {
        private readonly List<CssComponentValue> _values;
        private int _index;

        private CalcParser(List<CssComponentValue> values) => _values = values;

        /// <summary>Parses a number, a dimension or a math function.</summary>
        public static CalcNode? ParseNumeric(CssComponentValue value)
        {
            switch (value)
            {
                case CssPreservedToken { Type: CssTokenType.Number } number:
                    return new CalcLeaf(number.Token.NumericValue, unit: null, number.Token.IsInteger);

                case CssPreservedToken { Type: CssTokenType.Dimension } dimension:
                {
                    var unit = InfraAscii.ToAsciiLowercase(dimension.Token.Value);
                    if (CalcLeaf.GetResolutionFactor(unit) is null && CalcLeaf.GetLengthFactor(unit, MediaEnvironment.Default) is null)
                        return null;

                    return new CalcLeaf(dimension.Token.NumericValue, unit, isInteger: false);
                }

                case CssFunction function when InfraAscii.EqualsIgnoreAsciiCase(function.Name, "env"):
                    return ValueMatcher.AreSubstitutionFunctionsValid([function]) ? new CalcEnvironmentVariable(function) : null;

                case CssFunction function:
                    return ParseFunction(function);

                default:
                    return null;
            }
        }

        private static CalcFunction? ParseFunction(CssFunction function)
        {
            var name = InfraAscii.ToAsciiLowercase(function.Name);
            var argumentCount = name switch
            {
                "calc" or "abs" or "sign" => 1,
                "clamp" => 3,
                "min" or "max" => -1,
                _ => 0,
            };

            if (argumentCount == 0)
                return null;

            var arguments = new List<CalcNode>();
            var current = new List<CssComponentValue>();
            foreach (var value in function.Value)
            {
                if (value is CssPreservedToken { Type: CssTokenType.Comma })
                {
                    if (ParseSum(current) is not { } argument)
                        return null;

                    arguments.Add(argument);
                    current = [];
                }
                else
                {
                    current.Add(value);
                }
            }

            if (ParseSum(current) is not { } last)
                return null;

            arguments.Add(last);
            if (argumentCount > 0 && arguments.Count != argumentCount)
                return null;

            return new CalcFunction(name, [.. arguments]);
        }

        /// <summary>&lt;calc-sum&gt; that must span all the values.</summary>
        private static CalcNode? ParseSum(List<CssComponentValue> values)
        {
            var parser = new CalcParser(values);
            var result = parser.ParseSum();
            parser.SkipWhitespace();
            return parser._index == values.Count ? result : null;
        }

        private bool SkipWhitespace()
        {
            var skipped = false;
            while (_index < _values.Count && _values[_index] is CssPreservedToken { Type: CssTokenType.Whitespace })
            {
                _index++;
                skipped = true;
            }

            return skipped;
        }

        private bool IsDelim(out char delim)
        {
            if (_index < _values.Count && _values[_index] is CssPreservedToken { Token: { Type: CssTokenType.Delim, Value: ['+' or '-' or '*' or '/'] } } token)
            {
                delim = token.Token.Value[0];
                return true;
            }

            delim = default;
            return false;
        }

        /// <summary>&lt;calc-sum&gt; = &lt;calc-product&gt; [ [ '+' | '-' ] &lt;calc-product&gt; ]*</summary>
        private CalcNode? ParseSum()
        {
            var left = ParseProduct();
            while (left is not null)
            {
                var start = _index;
                var whitespaceBefore = SkipWhitespace();
                if (!IsDelim(out var op) || op is not ('+' or '-'))
                {
                    _index = start;
                    return left;
                }

                // The + and - operators must be surrounded by whitespace
                if (!whitespaceBefore || _index + 1 >= _values.Count || _values[_index + 1] is not CssPreservedToken { Type: CssTokenType.Whitespace })
                    return null;

                _index++;
                var right = ParseProduct();
                left = right is null ? null : new CalcBinary(op, left, right);
            }

            return null;
        }

        /// <summary>&lt;calc-product&gt; = &lt;calc-value&gt; [ [ '*' | '/' ] &lt;calc-value&gt; ]*</summary>
        private CalcNode? ParseProduct()
        {
            var left = ParseTerm();
            while (left is not null)
            {
                var start = _index;
                SkipWhitespace();
                if (!IsDelim(out var op) || op is not ('*' or '/'))
                {
                    _index = start;
                    return left;
                }

                _index++;
                var right = ParseTerm();
                left = right is null ? null : new CalcBinary(op, left, right);
            }

            return null;
        }

        /// <summary>&lt;calc-value&gt; = &lt;number&gt; | &lt;dimension&gt; | &lt;calc-keyword&gt; | ( &lt;calc-sum&gt; ) | a math function</summary>
        private CalcNode? ParseTerm()
        {
            SkipWhitespace();
            if (_index >= _values.Count)
                return null;

            var value = _values[_index++];
            return value switch
            {
                CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block => ParseSum(block.Value),

                // https://drafts.csswg.org/css-values-4/#calc-constants
                CssPreservedToken { Type: CssTokenType.Ident } ident => InfraAscii.ToAsciiLowercase(ident.Token.Value) switch
                {
                    "e" => new CalcLeaf(Math.E, unit: null, isInteger: false),
                    "pi" => new CalcLeaf(Math.PI, unit: null, isInteger: false),
                    "infinity" => new CalcLeaf(double.PositiveInfinity, unit: null, isInteger: false),
                    "-infinity" => new CalcLeaf(double.NegativeInfinity, unit: null, isInteger: false),
                    "nan" => new CalcLeaf(double.NaN, unit: null, isInteger: false),
                    _ => null,
                },
                _ => ParseNumeric(value),
            };
        }
    }
}
