namespace Tomlyn.Parsing;

/// <summary>
/// Controls whether the lexer reads key-oriented or value-oriented tokens.
/// </summary>
public enum TomlLexerMode
{
    /// <summary>Key context.</summary>
    Key = 0,

    /// <summary>Value context.</summary>
    Value = 1,
}
