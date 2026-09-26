using System;
using System.Diagnostics;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A factory for <see cref="SyntaxNode"/>
/// </summary>
public static class SyntaxFactory
{
    /// <summary>
    /// Creates a trivia whitespace.
    /// </summary>
    /// <returns>A trivia whitespace.</returns>
    public static SyntaxTrivia Whitespace()
    {
        return new SyntaxTrivia(TokenKind.Whitespaces, " ");
    }

    /// <summary>
    /// Creates a newline trivia.
    /// </summary>
    /// <returns>A new line trivia</returns>
    public static SyntaxTrivia NewLineTrivia()
    {
        return new SyntaxTrivia(TokenKind.NewLine, "\n");
    }

    /// <summary>
    /// Creates a comment trivia.
    /// </summary>
    /// <param name="comment">A comment trivia</param>
    /// <returns>A comment trivia</returns>
    /// <exception cref="ArgumentException"><paramref name="comment"/> contains a control character other than tab, such as a newline.</exception>
    public static SyntaxTrivia Comment(string comment)
    {
        ArgumentNullException.ThrowIfNull(comment);

        // A newline would end the comment, and turn the rest of the text into TOML
        foreach (var c in comment)
        {
            if (c != '\t' && CharHelper.IsControlCharacter(c))
            {
                throw new ArgumentException("A comment cannot contain a control character other than tab. Create one comment per line.", nameof(comment));
            }
        }

        return new SyntaxTrivia(TokenKind.Comment, $"# {comment}");
    }

    /// <summary>
    /// Creates a newline token.
    /// </summary>
    /// <returns>A new line token</returns>
    public static SyntaxToken NewLine()
    {
        return new SyntaxToken(TokenKind.NewLine, "\n");
    }

    /// <summary>
    /// Creates a token from the specified token kind.
    /// </summary>
    /// <param name="kind">The token kind</param>
    /// <returns>The token</returns>
    public static SyntaxToken Token(TokenKind kind)
    {
        if (kind == TokenKind.NewLine || !kind.IsToken()) throw new ArgumentOutOfRangeException($"The token kind `{kind}` is not supported for a plain token without a predefined value");
        var text = kind.ToText();
        Debug.Assert(text != null);
        return new SyntaxToken(kind, text);
    }
}
