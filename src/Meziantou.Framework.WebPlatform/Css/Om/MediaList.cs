using System.Collections;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Om;

/// <summary>https://drafts.csswg.org/cssom/#the-medialist-interface</summary>
[IdlInterface("MediaList", "https://drafts.csswg.org/cssom/#medialist")]
public sealed class MediaList : IReadOnlyList<string>
{
    private readonly List<string> _queries = [];

    /// <summary>Called when the media queries change (e.g. to update the media attribute of the owner node).</summary>
    private readonly Action<string>? _changed;

    internal MediaList(string? mediaText = null, Action<string>? changed = null)
    {
        if (!string.IsNullOrEmpty(mediaText))
        {
            _queries.AddRange(MediaQueries.ParseList(mediaText));
        }

        _changed = changed;
    }

    internal MediaList(List<CssComponentValue> values)
    {
        _queries.AddRange(MediaQueries.ParseList(values));
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-medialist-mediatext</summary>
    [IdlMember("mediaText", "https://drafts.csswg.org/cssom/#dom-medialist-mediatext")]
    public string MediaText
    {
        get => string.Join(", ", _queries);
        set
        {
            _queries.Clear();
            if (!string.IsNullOrEmpty(value))
            {
                _queries.AddRange(MediaQueries.ParseList(value));
            }

            Cascade.CssStyleVersion.Increment();
            _changed?.Invoke(MediaText);
        }
    }

    /// <summary>Gets the number of media queries in the list.</summary>
    [IdlMember("length", "https://drafts.csswg.org/cssom/#dom-medialist-length")]
    public uint Length => (uint)_queries.Count;

    /// <summary>Gets the number of media queries in the list.</summary>
    [NotInIdl]
    public int Count => _queries.Count;

    /// <summary>Returns the media query at the given index, or null when the index is out of range.</summary>
    [IdlMember("item", "https://drafts.csswg.org/cssom/#dom-medialist-item")]
    public string? Item(uint index) => index < (uint)_queries.Count ? _queries[(int)index] : null;

    /// <summary>Gets the media query at the given index, or null when the index is out of range.</summary>
    [IdlMember("getter", "https://drafts.csswg.org/cssom/#dom-medialist-item")]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string? this[uint index] => Item(index);

    /// <summary>Gets the media query at the given index.</summary>
    [NotInIdl]
    [System.Runtime.CompilerServices.IndexerName("ItemAt")]
    public string this[int index] => _queries[index];

    /// <summary>https://drafts.csswg.org/cssom/#dom-medialist-appendmedium</summary>
    [IdlMember("appendMedium", "https://drafts.csswg.org/cssom/#dom-medialist-appendmedium")]
    public void AppendMedium(string medium)
    {
        ArgumentNullException.ThrowIfNull(medium);
        var queries = MediaQueries.ParseList(medium);
        if (queries.Count != 1)
            return;

        if (_queries.Contains(queries[0]))
            return;

        _queries.Add(queries[0]);
        Cascade.CssStyleVersion.Increment();
        _changed?.Invoke(MediaText);
    }

    /// <summary>https://drafts.csswg.org/cssom/#dom-medialist-deletemedium</summary>
    [IdlMember("deleteMedium", "https://drafts.csswg.org/cssom/#dom-medialist-deletemedium")]
    public void DeleteMedium(string medium)
    {
        ArgumentNullException.ThrowIfNull(medium);
        var queries = MediaQueries.ParseList(medium);
        if (queries.Count != 1 || _queries.RemoveAll(q => q == queries[0]) == 0)
            throw DomException.NotFound("The medium is not in the list");

        Cascade.CssStyleVersion.Increment();
        _changed?.Invoke(MediaText);
    }

    /// <summary>Returns the serialized media query list.</summary>
    [IdlMember("stringifier", "https://drafts.csswg.org/cssom/#dom-medialist-mediatext")]
    public override string ToString() => MediaText;

    /// <summary>Returns an enumerator that iterates through the media queries of the list.</summary>
    [NotInIdl]
    public IEnumerator<string> GetEnumerator() => _queries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
}

/// <summary>Parses and serializes media queries (https://drafts.csswg.org/mediaqueries-4/#mq-syntax, https://drafts.csswg.org/cssom/#serializing-media-queries).</summary>
internal static class MediaQueries
{
    private const string NotAll = "not all";

    /// <summary>Parses a media query list and returns the serialization of each media query ("not all" for the invalid ones).</summary>
    public static List<string> ParseList(string text) => ParseList(CssParser.ParseListOfComponentValues(text));

    public static List<string> ParseList(List<CssComponentValue> values)
    {
        var result = new List<string>();
        var significant = ValueMatcher.RemoveWhitespace(values);
        if (significant.Count == 0)
            return result;

        var current = new List<CssComponentValue>();
        foreach (var value in values)
        {
            if (value is CssPreservedToken { Type: CssTokenType.Comma })
            {
                result.Add(ParseQuery(current) ?? NotAll);
                current = [];
            }
            else
            {
                current.Add(value);
            }
        }

        result.Add(ParseQuery(current) ?? NotAll);
        return result;
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-4/#typedef-media-query: &lt;media-condition&gt; | [ not | only ]? &lt;media-type&gt; [ and &lt;media-condition-without-or&gt; ]?
    /// Returns the serialization of the query, or null when it is invalid.
    /// </summary>
    private static string? ParseQuery(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        if (tokens[0] is not CssPreservedToken { Type: CssTokenType.Ident })
            return ParseCondition(tokens, 0, allowOr: true, out var end) is { } condition && end == tokens.Count ? condition : null;

        var index = 0;
        string? modifier = null;
        var first = InfraAscii.ToAsciiLowercase(((CssPreservedToken)tokens[0]).Token.Value);
        if (first is "not" or "only")
        {
            // "not (...)" is a condition
            if (first == "not" && tokens.Count > 1 && tokens[1] is not CssPreservedToken { Type: CssTokenType.Ident })
                return ParseCondition(tokens, 0, allowOr: true, out var end) is { } condition && end == tokens.Count ? condition : null;

            modifier = first;
            index++;
        }

        if (index >= tokens.Count || tokens[index] is not CssPreservedToken { Type: CssTokenType.Ident } typeToken)
            return null;

        var type = InfraAscii.ToAsciiLowercase(typeToken.Token.Value);
        if (type is "only" or "not" or "and" or "or" or "layer")
            return null;

        index++;
        string? typeCondition = null;
        if (index < tokens.Count)
        {
            if (!(tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } and && and.Token.IsIdent("and")))
                return null;

            typeCondition = ParseCondition(tokens, index + 1, allowOr: false, out var end);
            if (typeCondition is null || end != tokens.Count)
                return null;
        }

        var builder = new StringBuilder();
        if (modifier is not null)
        {
            builder.Append(modifier).Append(' ');
        }

        // "all and (...)" is serialized as "(...)"
        if (type != "all" || modifier is not null || typeCondition is null)
        {
            builder.Append(CssSerializer.SerializeIdentifier(type));
            if (typeCondition is not null)
            {
                builder.Append(" and ");
            }
        }

        if (typeCondition is not null)
        {
            builder.Append(typeCondition);
        }

        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-4/#typedef-media-condition</summary>
    private static string? ParseCondition(List<CssComponentValue> tokens, int index, bool allowOr, out int end)
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
            return "not " + inner;
        }

        var first = ParseInParens(tokens, index);
        if (first is null)
            return null;

        var builder = new StringBuilder(first);
        index++;
        string? combinator = null;
        while (index < tokens.Count && tokens[index] is CssPreservedToken { Type: CssTokenType.Ident } word && InfraAscii.ToAsciiLowercase(word.Token.Value) is "and" or "or")
        {
            var keyword = InfraAscii.ToAsciiLowercase(word.Token.Value);
            if ((combinator is not null && combinator != keyword) || (keyword == "or" && !allowOr))
                return null;

            combinator = keyword;
            var next = ParseInParens(tokens, index + 1);
            if (next is null)
                return null;

            builder.Append(' ').Append(keyword).Append(' ').Append(next);
            index += 2;
        }

        end = index;
        return builder.ToString();
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-4/#typedef-media-in-parens: ( &lt;media-condition&gt; ) | &lt;media-feature&gt; | &lt;general-enclosed&gt;. Returns null on a syntax error.</summary>
    private static string? ParseInParens(List<CssComponentValue> tokens, int index)
    {
        if (index >= tokens.Count)
            return null;

        switch (tokens[index])
        {
            case CssSimpleBlock { AssociatedToken: CssTokenType.LeftParenthesis } block:
            {
                if (!MediaQueryEvaluator.IsAnyValue(block.Value))
                    return null;

                var inner = ValueMatcher.RemoveWhitespace(block.Value);
                if (ParseCondition(inner, 0, allowOr: true, out var end) is { } condition && end == inner.Count)
                    return "(" + condition + ")";

                // A block that is not a known <media-feature> (an unknown feature name, an invalid value...) is a <general-enclosed>
                return ParseFeature(block.Value) ?? SerializeGeneralEnclosed(block);
            }

            case CssFunction function:
                return MediaQueryEvaluator.IsAnyValue(function.Value) ? SerializeGeneralEnclosed(function) : null;

            default:
                return null;
        }
    }

    /// <summary>
    /// https://drafts.csswg.org/mediaqueries-5/#typedef-general-enclosed: valid, but it evaluates to unknown. It is serialized as
    /// specified, like the browsers: the tokens are serialized (https://drafts.csswg.org/css-syntax-3/#serialization), the
    /// whitespace is collapsed and the comments are removed.
    /// </summary>
    private static string SerializeGeneralEnclosed(CssComponentValue value)
    {
        var builder = new StringBuilder();
        SerializeTokens(builder, [value]);
        return builder.ToString();
    }

    private static void SerializeTokens(StringBuilder builder, List<CssComponentValue> values)
    {
        foreach (var value in values)
        {
            switch (value)
            {
                case CssFunction function:
                    builder.Append(CssSerializer.SerializeIdentifier(function.Name)).Append('(');
                    SerializeTokens(builder, function.Value);
                    builder.Append(')');
                    break;

                case CssSimpleBlock block:
                    var (open, close) = block.AssociatedToken switch
                    {
                        CssTokenType.LeftSquareBracket => ('[', ']'),
                        CssTokenType.LeftCurlyBracket => ('{', '}'),
                        _ => ('(', ')'),
                    };
                    builder.Append(open);
                    SerializeTokens(builder, block.Value);
                    builder.Append(close);
                    break;

                case CssPreservedToken { Token: var token }:
                    builder.Append(token.Type switch
                    {
                        CssTokenType.Ident => CssSerializer.SerializeIdentifier(token.Value),
                        CssTokenType.AtKeyword => "@" + CssSerializer.SerializeIdentifier(token.Value),
                        CssTokenType.Hash => "#" + CssSerializer.SerializeIdentifier(token.Value),
                        CssTokenType.String => CssSerializer.SerializeString(token.Value),
                        CssTokenType.Url => CssSerializer.SerializeUrl(token.Value),
                        CssTokenType.Number => SerializeNumber(token),
                        CssTokenType.Percentage => (token.Sign == '+' ? "+" : "") + CssSerializer.SerializeNumber(token.NumericValue) + "%",
                        CssTokenType.Dimension => SerializeNumber(token) + CssSerializer.SerializeIdentifier(token.Value),
                        CssTokenType.Whitespace => builder.Length > 0 && builder[^1] == ' ' ? "" : " ",
                        CssTokenType.Colon => ":",
                        CssTokenType.Semicolon => ";",
                        CssTokenType.Comma => ",",
                        CssTokenType.Cdo => "<!--",
                        CssTokenType.Cdc => "-->",
                        _ => token.Value,
                    });
                    break;
            }
        }

        // The type of the number is kept (e.g. "1.0" is not an <integer>)
        static string SerializeNumber(CssToken token)
        {
            var result = (token.Sign == '+' ? "+" : "") + CssSerializer.SerializeNumber(token.NumericValue);
            return token.IsInteger || !double.IsInteger(token.NumericValue) || result.Contains('e', StringComparison.Ordinal) ? result : result + ".0";
        }
    }

    /// <summary>https://drafts.csswg.org/mediaqueries-4/#typedef-media-feature</summary>
    private static string? ParseFeature(List<CssComponentValue> values)
    {
        var tokens = ValueMatcher.RemoveWhitespace(values);
        if (tokens.Count == 0)
            return null;

        // <mf-boolean> and <mf-plain>
        if (tokens[0] is CssPreservedToken { Type: CssTokenType.Ident } nameToken)
        {
            var name = InfraAscii.ToAsciiLowercase(nameToken.Token.Value);
            if (tokens.Count == 1)
                return GetFeature(name, out var prefixed) is not null && !prefixed ? "(" + name + ")" : null;

            if (tokens[1] is CssPreservedToken { Type: CssTokenType.Colon })
            {
                var value = tokens[2..];
                if (value.Count == 0 || !IsValidValue(name, value, allowPrefix: true))
                    return null;

                return "(" + name + ": " + SerializeValue(value) + ")";
            }
        }

        // <mf-range>: <mf-name> <mf-comparison> <mf-value> | <mf-value> <mf-comparison> <mf-name> | <mf-value> <mf-lt> <mf-name> <mf-lt> <mf-value>
        var parts = new List<List<CssComponentValue>> { new() };
        var comparisons = new List<string>();
        for (var i = 0; i < tokens.Count; i++)
        {
            if (tokens[i] is CssPreservedToken { Token: { Type: CssTokenType.Delim } delim } && delim.Value is "<" or ">" or "=")
            {
                var comparison = delim.Value;
                if (delim.Value is "<" or ">" && i + 1 < tokens.Count && tokens[i + 1] is CssPreservedToken equals && equals.Token.IsDelim('=')
                    && equals.Start == ((CssPreservedToken)tokens[i]).End)
                {
                    comparison += "=";
                    i++;
                }

                comparisons.Add(comparison);
                parts.Add([]);
            }
            else
            {
                parts[^1].Add(tokens[i]);
            }
        }

        if (parts.Exists(p => p.Count == 0))
            return null;

        if (comparisons.Count == 1)
        {
            if (parts[0] is [CssPreservedToken { Type: CssTokenType.Ident } left] && IsRangeName(InfraAscii.ToAsciiLowercase(left.Token.Value)) && IsValidValue(InfraAscii.ToAsciiLowercase(left.Token.Value), parts[1], allowPrefix: false))
                return "(" + InfraAscii.ToAsciiLowercase(left.Token.Value) + " " + comparisons[0] + " " + SerializeValue(parts[1]) + ")";

            if (parts[1] is [CssPreservedToken { Type: CssTokenType.Ident } right] && IsRangeName(InfraAscii.ToAsciiLowercase(right.Token.Value)) && IsValidValue(InfraAscii.ToAsciiLowercase(right.Token.Value), parts[0], allowPrefix: false))
                return "(" + SerializeValue(parts[0]) + " " + comparisons[0] + " " + InfraAscii.ToAsciiLowercase(right.Token.Value) + ")";

            return null;
        }

        if (comparisons.Count == 2)
        {
            // Both comparisons must be in the same direction
            var lessThan = comparisons[0] is "<" or "<=";
            if (comparisons[0] == "=" || comparisons[1] == "=" || lessThan != comparisons[1] is "<" or "<=")
                return null;

            if (parts[1] is not [CssPreservedToken { Type: CssTokenType.Ident } middle])
                return null;

            var name = InfraAscii.ToAsciiLowercase(middle.Token.Value);
            if (!IsRangeName(name) || !IsValidValue(name, parts[0], allowPrefix: false) || !IsValidValue(name, parts[2], allowPrefix: false))
                return null;

            return "(" + SerializeValue(parts[0]) + " " + comparisons[0] + " " + name + " " + comparisons[1] + " " + SerializeValue(parts[2]) + ")";
        }

        return null;
    }

    /// <summary>Returns the media feature (the min-/max- prefixes are resolved when allowed).</summary>
    private static CssDescriptorDefinition? GetFeature(string name, out bool prefixed)
    {
        prefixed = false;
        if (CssPropertyRegistry.GetDescriptor("@media", name) is { } feature)
            return feature;

        if (name.StartsWith("min-", StringComparison.Ordinal) || name.StartsWith("max-", StringComparison.Ordinal))
        {
            prefixed = true;
            var unprefixed = CssPropertyRegistry.GetDescriptor("@media", name[4..]);
            return unprefixed is not null && IsRangeName(name[4..]) ? unprefixed : null;
        }

        return null;
    }

    /// <summary>Returns whether the media feature is a "range" feature (it accepts min-/max- prefixes and range syntax).</summary>
    private static bool IsRangeName(string name) => CssPropertyRegistry.GetDescriptor("@media", name) is { } feature && IsRangeGrammar(feature.Grammar);

    private static bool IsRangeGrammar(string grammar) => grammar.Contains("<length>", StringComparison.Ordinal) || grammar.Contains("<ratio>", StringComparison.Ordinal) || grammar.Contains("<integer>", StringComparison.Ordinal)
        || grammar.Contains("<resolution>", StringComparison.Ordinal) || grammar.Contains("<number>", StringComparison.Ordinal) || grammar.Contains("<mq-boolean>", StringComparison.Ordinal);

    private static bool IsValidValue(string name, List<CssComponentValue> value, bool allowPrefix)
    {
        CssDescriptorDefinition? feature;
        if (allowPrefix)
        {
            feature = GetFeature(name, out _);
        }
        else
        {
            feature = CssPropertyRegistry.GetDescriptor("@media", name);
        }

        if (feature is null)
            return false;

        // The environment variables can be used in media queries (https://drafts.csswg.org/css-env-1/#env-function): the value
        // is validated with their values (or their fallback)
        var tokens = ValueMatcher.RemoveWhitespace(value);
        if (ValueMatcher.ContainsSubstitutionFunction(tokens))
        {
            if (!ValueMatcher.AreSubstitutionFunctionsValid(tokens) || MediaQueryEvaluator.ResolveEnvironmentVariables(tokens, MediaEnvironment.Default) is not { } resolved)
                return false;

            tokens = ValueMatcher.RemoveWhitespace(resolved);
        }

        // A <resolution> is not negative (https://drafts.csswg.org/css-values-4/#resolution)
        if (tokens is [CssPreservedToken { Type: CssTokenType.Dimension } dimension] && dimension.Token.NumericValue < 0 && feature.Grammar.Contains("<resolution>", StringComparison.Ordinal))
            return false;

        return CssPropertyRegistry.GetGrammar(feature) is not { } grammar || ValueMatcher.Matches(grammar, tokens);
    }

    private static string SerializeValue(List<CssComponentValue> value) => CssSerializer.SerializeValue(InsertSpaces(value), property: null);

    private static List<CssComponentValue> InsertSpaces(List<CssComponentValue> values)
    {
        var result = new List<CssComponentValue>();
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
}
