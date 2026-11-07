namespace Meziantou.Framework.WebPlatform.Css.Syntax;

/// <summary>The token types of CSS Syntax (https://drafts.csswg.org/css-syntax-3/#tokenization).</summary>
internal enum CssTokenType : byte
{
    Ident,
    Function,
    AtKeyword,
    Hash,
    String,
    BadString,
    Url,
    BadUrl,
    Delim,
    Number,
    Percentage,
    Dimension,
    UnicodeRange,
    Whitespace,
    Cdo,
    Cdc,
    Colon,
    Semicolon,
    Comma,
    LeftSquareBracket,
    RightSquareBracket,
    LeftParenthesis,
    RightParenthesis,
    LeftCurlyBracket,
    RightCurlyBracket,
    EndOfFile,
}

/// <summary>A CSS token (https://drafts.csswg.org/css-syntax-3/#tokenization).</summary>
internal readonly record struct CssToken
{
    public CssTokenType Type { get; init; }

    /// <summary>
    /// The value of ident, function, at-keyword, hash, string and url tokens; the code point of delim tokens;
    /// the unit of dimension tokens.
    /// </summary>
    public string Value { get; init; }

    /// <summary>The numeric value of number, percentage and dimension tokens.</summary>
    public double NumericValue { get; init; }

    /// <summary>True when the type flag of a number or dimension token is "integer".</summary>
    public bool IsInteger { get; init; }

    /// <summary>The sign character ('+', '-' or '\0' when missing) of numeric tokens.</summary>
    public char Sign { get; init; }

    /// <summary>True when the type flag of a hash token is "id".</summary>
    public bool IsIdHash { get; init; }

    /// <summary>The start and end of a unicode-range token.</summary>
    public int RangeStart { get; init; }

    public int RangeEnd { get; init; }

    /// <summary>The position of the token in the preprocessed input.</summary>
    public int Start { get; init; }

    public int End { get; init; }

    public bool IsDelim(char c) => Type == CssTokenType.Delim && Value.Length == 1 && Value[0] == c;

    public bool IsIdent(string value) => Type == CssTokenType.Ident && Infra.InfraAscii.EqualsIgnoreAsciiCase(Value, value);

    public override string ToString() => Type switch
    {
        CssTokenType.Ident or CssTokenType.Delim => Value,
        CssTokenType.Function => Value + "(",
        CssTokenType.AtKeyword => "@" + Value,
        CssTokenType.Hash => "#" + Value,
        CssTokenType.String => "\"" + Value + "\"",
        CssTokenType.Number => NumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
        CssTokenType.Percentage => NumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
        CssTokenType.Dimension => NumericValue.ToString(System.Globalization.CultureInfo.InvariantCulture) + Value,
        _ => Type.ToString(),
    };
}
