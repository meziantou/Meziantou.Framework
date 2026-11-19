using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// A link parameter defined by a <c>param()</c> function (https://drafts.csswg.org/css-link-params-1/#param):
/// <c>param( &lt;param-spec&gt; [ ':' &lt;declaration-value&gt;? ]? )</c>, where
/// <c>&lt;param-spec&gt; = color | accent-color | [ &lt;dashed-ident&gt; &lt;css-type&gt;? ]</c>.
/// </summary>
/// <param name="Name">The name: a &lt;dashed-ident&gt;, <c>color</c> or <c>accent-color</c>.</param>
/// <param name="Type">The &lt;css-type&gt; of a &lt;dashed-ident&gt; parameter, or null.</param>
/// <param name="Value">The value (possibly empty), or null when the colon is omitted: the value is then the value of the property of the same name on the element.</param>
internal sealed record CssLinkParameter(string Name, CssSyntaxDefinition? Type, string? Value)
{
    /// <summary>
    /// Serializes the parameter: <c>param(--a: orange)</c>, <c>param(--a &lt;color&gt;: orange)</c> (a type of a single
    /// syntax component is serialized without <c>type()</c>), <c>param(--a: )</c> for an empty value, <c>param(--a)</c>
    /// without value.
    /// </summary>
    public override string ToString()
    {
        var builder = new StringBuilder("param(");
        builder.Append(CssSerializer.SerializeIdentifier(Name));
        if (Type is not null)
        {
            builder.Append(' ').Append(CssFunctionRule.SerializeType(Type));
        }

        if (Value is not null)
        {
            builder.Append(": ").Append(Value);
        }

        return builder.Append(')').ToString();
    }
}

/// <summary>
/// The link parameters passed to an external resource (https://drafts.csswg.org/css-link-params-1/#using), resolved against the
/// element that uses it: the custom environment variables of the document of the resource (<c>env(--name)</c>, and
/// <c>env(color)</c> and <c>env(accent-color)</c>). Two sets are equal when they have the same names and values, so that the
/// renderings of an SVG image can be cached by their parameters.
/// </summary>
internal sealed class CssLinkParameterValues : IEquatable<CssLinkParameterValues>
{
    private readonly KeyValuePair<string, string>[] _values;

    /// <param name="values">The values by name (the last value of a name wins), sorted by name.</param>
    private CssLinkParameterValues(KeyValuePair<string, string>[] values)
    {
        _values = values;
    }

    /// <summary>The number of parameters.</summary>
    public int Count => _values.Length;

    /// <summary>Creates a set from its values by name, or returns null when there is none.</summary>
    public static CssLinkParameterValues? Create(Dictionary<string, string> values)
    {
        if (values.Count == 0)
            return null;

        var array = values.ToArray();
        Array.Sort(array, static (a, b) => string.CompareOrdinal(a.Key, b.Key));
        return new CssLinkParameterValues(array);
    }

    /// <summary>Returns the value of a parameter, or null when it is not passed.</summary>
    public string? GetValue(string name)
    {
        foreach (var (key, value) in _values)
        {
            if (string.Equals(key, name, StringComparison.Ordinal))
                return value;
        }

        return null;
    }

    public bool Equals([NotNullWhen(true)] CssLinkParameterValues? other)
    {
        if (other is null)
            return false;

        if (ReferenceEquals(this, other))
            return true;

        if (_values.Length != other._values.Length)
            return false;

        for (var i = 0; i < _values.Length; i++)
        {
            if (!string.Equals(_values[i].Key, other._values[i].Key, StringComparison.Ordinal) || !string.Equals(_values[i].Value, other._values[i].Value, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public override bool Equals([NotNullWhen(true)] object? obj) => Equals(obj as CssLinkParameterValues);

    public override int GetHashCode()
    {
        var hash = new HashCode();
        foreach (var (key, value) in _values)
        {
            hash.Add(key, StringComparer.Ordinal);
            hash.Add(value, StringComparer.Ordinal);
        }

        return hash.ToHashCode();
    }
}

/// <summary>
/// CSS Linked Parameters (https://drafts.csswg.org/css-link-params-1/): the <c>param()</c> function, the <c>link-parameters</c>
/// property, the <c>param()</c> &lt;url-modifier&gt; and the link parameter directives of the fragments of the URLs
/// (<c>#:~:param(--color:green)</c>), and the resolution of the parameters passed to a resource.
/// </summary>
internal static class CssLinkParameters
{
    /// <summary>The prefix of a link parameter directive (https://drafts.csswg.org/css-link-params-1/#url-frag).</summary>
    private const string FragmentDirectiveDelimiter = ":~:";

    private const int MaxCachedLists = 256;

    private static readonly ConcurrentDictionary<string, CssLinkParameter[]> ParsedLists = new(StringComparer.Ordinal);

    /// <summary>Returns whether a function is a <c>param()</c> function.</summary>
    public static bool IsParamFunction(CssFunction function) => InfraAscii.EqualsIgnoreAsciiCase(function.Name, "param");

    /// <summary>Parses a <c>param()</c> function, or returns null when it is invalid.</summary>
    public static CssLinkParameter? Parse(CssFunction function)
    {
        if (!IsParamFunction(function))
            return null;

        var arguments = function.Value;
        var index = SkipWhitespace(arguments, 0);
        if (index >= arguments.Count || arguments[index] is not CssPreservedToken { Type: CssTokenType.Ident } nameToken)
            return null;

        var colon = arguments.FindIndex(index + 1, static value => value is CssPreservedToken { Type: CssTokenType.Colon });
        var typeValues = arguments[(index + 1)..(colon < 0 ? arguments.Count : colon)];
        string name;
        CssSyntaxDefinition? type = null;
        if (nameToken.Value.StartsWith("--", StringComparison.Ordinal))
        {
            // <dashed-ident> <css-type>?
            name = nameToken.Value;
            if (!ValueMatcher.IsEmptyOrWhitespace(typeValues) && (type = CssRuleParser.ParseCssType(typeValues)) is null)
                return null;
        }
        else if (InfraAscii.ToAsciiLowercase(nameToken.Value) is "color" or "accent-color" && ValueMatcher.IsEmptyOrWhitespace(typeValues))
        {
            name = InfraAscii.ToAsciiLowercase(nameToken.Value);
        }
        else
        {
            return null;
        }

        if (colon < 0)
            return new CssLinkParameter(name, type, Value: null);

        // ':' <declaration-value>?
        var value = ValueMatcher.TrimWhitespace(arguments[(colon + 1)..]);
        if (!CssShorthands.IsValidDeclarationValue(value, topLevel: true))
            return null;

        if (ValueMatcher.ContainsSubstitutionFunction(value) && !ValueMatcher.AreSubstitutionFunctionsValid(value))
            return null;

        return new CssLinkParameter(name, type, CssSerializer.SerializeValue(value, property: null));
    }

    /// <summary>
    /// Returns the canonical serialization of a value of the <c>link-parameters</c> property (<c>none | &lt;param()&gt;#</c>,
    /// https://drafts.csswg.org/css-link-params-1/#link-param-prop), or null when it is invalid.
    /// </summary>
    public static string? Canonicalize(List<CssComponentValue> significant)
    {
        if (significant is [CssPreservedToken { Type: CssTokenType.Ident } keyword] && keyword.IsIdent("none"))
            return "none";

        var builder = new StringBuilder();
        foreach (var item in ValueMatcher.SplitAtCommas(significant))
        {
            if (item is not [CssFunction function] || Parse(function) is not { } parameter)
                return null;

            if (builder.Length > 0)
            {
                builder.Append(", ");
            }

            builder.Append(parameter);
        }

        return builder.ToString();
    }

    /// <summary>Parses a valid value of the <c>link-parameters</c> property (none is an empty list).</summary>
    public static CssLinkParameter[] ParseList(string value)
    {
        if (value is "none" or "")
            return [];

        if (ParsedLists.TryGetValue(value, out var cached))
            return cached;

        var result = new List<CssLinkParameter>();
        foreach (var item in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(value))))
        {
            if (item is [CssFunction function] && Parse(function) is { } parameter)
            {
                result.Add(parameter);
            }
        }

        var array = result.ToArray();
        if (ParsedLists.Count >= MaxCachedLists)
        {
            ParsedLists.Clear();
        }

        ParsedLists.TryAdd(value, array);
        return array;
    }

    /// <summary>
    /// Returns the link parameter directives of the fragment of a URL (https://drafts.csswg.org/css-link-params-1/#url-frag): the
    /// directives of the fragment directive (after <c>:~:</c>) are separated by <c>&amp;</c>, and each one that is a valid
    /// <c>param()</c> once percent-decoded is a parameter (the other directives, such as the text directives, are ignored).
    /// </summary>
    public static List<CssLinkParameter> ParseFragmentDirectives(string url)
    {
        var result = new List<CssLinkParameter>();
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
            return result;

        var delimiter = url.IndexOf(FragmentDirectiveDelimiter, hash + 1, StringComparison.Ordinal);
        if (delimiter < 0)
            return result;

        foreach (var directive in url[(delimiter + FragmentDirectiveDelimiter.Length)..].Split('&'))
        {
            if (!directive.StartsWith("param(", StringComparison.OrdinalIgnoreCase))
                continue;

            var decoded = directive.Contains('%', StringComparison.Ordinal) ? Encoding.UTF8.GetString(PercentEncoding.PercentDecode(directive)) : directive;
            if (ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(decoded)) is [CssFunction function] && Parse(function) is { } parameter)
            {
                result.Add(parameter);
            }
        }

        return result;
    }

    /// <summary>Returns the URL without its fragment directive (the part of its fragment from <c>:~:</c>), which the document of the resource does not see.</summary>
    public static string RemoveFragmentDirective(string url)
    {
        var hash = url.IndexOf('#', StringComparison.Ordinal);
        if (hash < 0)
            return url;

        var delimiter = url.IndexOf(FragmentDirectiveDelimiter, hash + 1, StringComparison.Ordinal);
        return delimiter < 0 ? url : url[..delimiter];
    }

    /// <summary>
    /// Returns the link parameters passed to a resource used by an element (https://drafts.csswg.org/css-link-params-1/#setting),
    /// or null when there is none: the parameters of its <c>link-parameters</c> property, then the link parameter directives of the
    /// URL of the resource, then the <c>param()</c> modifiers of its <c>url()</c>; the last parameter of a name wins.
    /// </summary>
    /// <param name="style">The style of the element that uses the resource (the element context of the parameters).</param>
    /// <param name="url">The URL of the resource, whose fragment may have link parameter directives, or null.</param>
    /// <param name="urlModifiers">The <c>param()</c> modifiers of the <c>url()</c> of the resource (see <see cref="CssUrlModifiers.GetLinkParameters"/>), or null.</param>
    /// <param name="document">The document of the element, whose base URL the relative URLs of the typed values are resolved against.</param>
    public static CssLinkParameterValues? Resolve(ComputedStyle style, string? url, IReadOnlyList<CssLinkParameter>? urlModifiers, Dom.Document document)
    {
        var property = style.GetComputedValue("link-parameters");
        var hasDirectives = url is not null && url.Contains(FragmentDirectiveDelimiter, StringComparison.Ordinal);
        if (property is "none" or "" && !hasDirectives && urlModifiers is not { Count: > 0 })
            return null;

        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        var baseUrl = document.DocumentBaseUrl.Serialize();
        Add(values, ParseList(property), style, baseUrl);
        if (hasDirectives)
        {
            Add(values, ParseFragmentDirectives(url!), style, baseUrl);
        }

        if (urlModifiers is not null)
        {
            Add(values, urlModifiers, style, baseUrl);
        }

        return CssLinkParameterValues.Create(values);
    }

    /// <summary>
    /// Resolves parameters against the element (https://drafts.csswg.org/css-link-params-1/#param) and adds the valid ones: a
    /// parameter without value takes the value of the property of its name on the element; the arbitrary substitution functions
    /// of the value are substituted; a typed value (the type of <c>color</c> and <c>accent-color</c> is &lt;color&gt;) is
    /// computed as a value of its type (<c>currentcolor</c> is the color of the element), and the parameter is ignored when the
    /// value does not match the type. An untyped value is passed as is.
    /// </summary>
    private static void Add(Dictionary<string, string> values, IEnumerable<CssLinkParameter> parameters, ComputedStyle style, string? baseUrl)
    {
        foreach (var parameter in parameters)
        {
            var name = parameter.Name;
            var value = parameter.Value;
            if (value is null)
            {
                value = name is "color" or "accent-color" ? style.GetComputedValue(name) : style.GetCustomPropertyValue(name);
                if (value is null)
                    continue;
            }
            else if (CssSubstitution.MayContainSubstitution(value))
            {
                if (CssSubstitution.Substitute(value, style) is not { } substituted)
                    continue;

                value = substituted;
            }

            var type = parameter.Type?.Text ?? (name is "color" or "accent-color" ? "<color>" : null);
            if (type is not null and not "*")
            {
                if (CustomPropertySyntax.Parse(type) is not { } syntax || syntax.Compute(style, value, baseUrl) is not { } computed)
                    continue;

                value = computed;
            }

            values[name] = value.Trim();
        }
    }

    private static int SkipWhitespace(List<CssComponentValue> values, int index)
    {
        while (index < values.Count && values[index] is CssPreservedToken { Type: CssTokenType.Whitespace })
        {
            index++;
        }

        return index;
    }
}
