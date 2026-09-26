namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// Represents an invalid <see cref="SyntaxToken"/>
/// </summary>
public sealed class InvalidSyntaxToken : SyntaxToken
{
    /// <summary>
    /// The kind of token which is invalid for the context.
    /// </summary>
    public TokenKind InvalidKind { get; set; }
}
