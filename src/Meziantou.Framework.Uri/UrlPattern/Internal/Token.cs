namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#tokens

/// <summary>Represents a single lexical token within a pattern string.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#tokens">WHATWG URL Pattern Spec - Tokens</see>
/// </remarks>
internal readonly struct Token
{
    public Token(TokenType type, int index, string value)
    {
        Type = type;
        Index = index;
        Value = value;
    }

    /// <summary>Gets the type of the token.</summary>
    public TokenType Type { get; }

    /// <summary>Gets the position of the first code point in the pattern string represented by the token.</summary>
    public int Index { get; }

    /// <summary>Gets the code points from the pattern string represented by the token.</summary>
    public string Value { get; }
}
