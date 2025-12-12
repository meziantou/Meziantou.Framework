namespace Meziantou.Framework.UrlPatternInternal;

// https://urlpattern.spec.whatwg.org/#tokens

/// <summary>Represents the type of a token in a pattern string.</summary>
/// <remarks>
/// <see href="https://urlpattern.spec.whatwg.org/#tokens">WHATWG URL Pattern Spec - Tokens</see>
/// </remarks>
internal enum TokenType
{
    /// <summary>The token represents a code point that is invalid in the pattern.</summary>
    InvalidChar,

    /// <summary>The token represents a U+007B ({) code point.</summary>
    Open,

    /// <summary>The token represents a U+007D (}) code point.</summary>
    Close,

    /// <summary>
    /// The token represents a string of the form "(&lt;regular expression&gt;)".
    /// </summary>
    Regexp,

    /// <summary>
    /// The token represents a string of the form ":&lt;name&gt;".
    /// </summary>
    Name,

    /// <summary>The token represents a valid pattern code point without any special syntactical meaning.</summary>
    Char,

    /// <summary>
    /// The token represents a code point escaped using a backslash like "\&lt;char&gt;".
    /// </summary>
    EscapedChar,

    /// <summary>The token represents a matching group modifier that is either the U+003F (?) or U+002B (+) code points.</summary>
    OtherModifier,

    /// <summary>The token represents a U+002A (*) code point that can be either a wildcard matching group or a matching group modifier.</summary>
    Asterisk,

    /// <summary>The token represents the end of the pattern string.</summary>
    End,
}
