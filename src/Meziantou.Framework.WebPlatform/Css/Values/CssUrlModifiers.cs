using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Css.Values;

/// <summary>
/// The request URL modifiers of url() and src() (https://drafts.csswg.org/css-values-5/#request-url-modifiers):
/// cross-origin( anonymous | use-credentials ), integrity( &lt;string&gt; ) and referrer-policy( &lt;referrer-policy&gt; ).
/// </summary>
internal static class CssUrlModifiers
{
    /// <summary>
    /// Returns the modifiers in their canonical order (cross-origin(), integrity(), then referrer-policy()), "" when there is
    /// none, or null when they are invalid: an unknown modifier, an invalid argument or a modifier specified twice.
    /// </summary>
    public static string? Canonicalize(IReadOnlyList<CssComponentValue> significant)
    {
        if (Parse(significant) is not { } modifiers)
            return null;

        var serialized = new List<string>(3);
        if (modifiers.CrossOrigin is { } crossOrigin)
        {
            serialized.Add("cross-origin(" + crossOrigin + ")");
        }

        if (modifiers.Integrity is { } integrity)
        {
            serialized.Add("integrity(" + CssSerializer.SerializeString(integrity) + ")");
        }

        if (modifiers.ReferrerPolicy.Length > 0)
        {
            serialized.Add("referrer-policy(" + modifiers.ReferrerPolicy + ")");
        }

        return string.Join(' ', serialized);
    }

    /// <summary>
    /// Parses the modifiers that follow the URL of url() or src(), or returns null when they are invalid: an unknown modifier,
    /// an invalid argument or a modifier specified twice.
    /// </summary>
    public static CssRequestModifiers? Parse(IReadOnlyList<CssComponentValue> significant)
    {
        if (significant.Count == 0)
            return CssRequestModifiers.None;

        string? crossOrigin = null;
        string? integrity = null;
        string? referrerPolicy = null;
        foreach (var value in significant)
        {
            if (value is not CssFunction function)
                return null;

            var argument = ValueMatcher.RemoveWhitespace(function.Value);
            switch (InfraAscii.ToAsciiLowercase(function.Name))
            {
                case "cross-origin" when crossOrigin is null && argument is [CssPreservedToken { Type: CssTokenType.Ident } mode] && InfraAscii.ToAsciiLowercase(mode.Token.Value) is "anonymous" or "use-credentials":
                    crossOrigin = InfraAscii.ToAsciiLowercase(mode.Token.Value);
                    break;

                case "integrity" when integrity is null && argument is [CssPreservedToken { Type: CssTokenType.String } metadata]:
                    integrity = metadata.Token.Value;
                    break;

                case "referrer-policy" when referrerPolicy is null && argument is [CssPreservedToken { Type: CssTokenType.Ident } policy] && IsReferrerPolicy(InfraAscii.ToAsciiLowercase(policy.Token.Value)):
                    referrerPolicy = InfraAscii.ToAsciiLowercase(policy.Token.Value);
                    break;

                default:
                    return null;
            }
        }

        return new CssRequestModifiers(crossOrigin, integrity, referrerPolicy ?? "");
    }

    /// <summary>Parses serialized modifiers (e.g. the modifiers of an @import rule), or returns null when they are invalid.</summary>
    public static CssRequestModifiers? Parse(string modifiers)
        => modifiers.Length == 0 ? CssRequestModifiers.None : Parse(ValueMatcher.RemoveWhitespace(CssParser.ParseListOfComponentValues(modifiers)));

    /// <summary>
    /// Returns the URL and the modifiers of a URL value: a URL token (e.g. <c>url(a.png)</c>, which has no modifier), or a
    /// url() or src() function whose first argument is a string. Returns false for another value, or invalid modifiers.
    /// </summary>
    public static bool TryGetUrl(CssComponentValue value, out string url, out CssRequestModifiers modifiers)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Url } urlToken:
                url = urlToken.Token.Value;
                modifiers = CssRequestModifiers.None;
                return true;

            case CssFunction function when InfraAscii.ToAsciiLowercase(function.Name) is "url" or "src"
                && ValueMatcher.RemoveWhitespace(function.Value) is [CssPreservedToken { Type: CssTokenType.String } stringToken, .. var rest]
                && Parse(rest) is { } parsed:
                url = stringToken.Token.Value;
                modifiers = parsed;
                return true;

            default:
                url = "";
                modifiers = CssRequestModifiers.None;
                return false;
        }
    }

    /// <summary>https://w3c.github.io/webappsec-referrer-policy/#referrer-policies (the empty string excluded).</summary>
    private static bool IsReferrerPolicy(string value) => value is "no-referrer" or "no-referrer-when-downgrade" or "same-origin" or "origin"
        or "strict-origin" or "origin-when-cross-origin" or "strict-origin-when-cross-origin" or "unsafe-url";

    /// <summary>Serializes url() or src() with a URL and its modifiers (e.g. url("a.png" cross-origin(anonymous))).</summary>
    public static string Serialize(string functionName, string url, string modifiers) =>
        functionName + "(" + CssSerializer.SerializeString(url) + (modifiers.Length == 0 ? "" : " " + modifiers) + ")";
}

/// <summary>
/// The request URL modifiers of a URL (https://drafts.csswg.org/css-values-5/#request-url-modifiers). Their steps
/// (https://drafts.csswg.org/css-values-5/#url-request-modifier-steps) set the mode, the credentials mode, the integrity
/// metadata and the referrer policy of the request of the resource.
/// </summary>
/// <param name="CrossOrigin">The value of cross-origin() (<c>anonymous</c> or <c>use-credentials</c>), or null.</param>
/// <param name="Integrity">The value of integrity(), or null.</param>
/// <param name="ReferrerPolicy">The value of referrer-policy(), or the empty string.</param>
internal sealed record CssRequestModifiers(string? CrossOrigin, string? Integrity, string ReferrerPolicy)
{
    /// <summary>No modifier.</summary>
    public static CssRequestModifiers None { get; } = new(null, null, "");

    /// <summary>Returns whether the mode of the request is "cors": cross-origin() sets it, else it is the mode of the resource.</summary>
    public bool IsCors(bool defaultCors) => CrossOrigin is not null || defaultCors;

    /// <summary>Whether the credentials mode of the request is "include" (cross-origin(use-credentials)) rather than "same-origin".</summary>
    public bool UsesCredentials => CrossOrigin == "use-credentials";

    /// <summary>The integrity metadata of the request (https://fetch.spec.whatwg.org/#concept-request-integrity-metadata), or the empty string.</summary>
    public string IntegrityMetadata => Integrity ?? "";
}
