using System;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// A TOML table array syntax node.
/// </summary>
public sealed class TableArraySyntax : TableSyntaxBase
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TableArraySyntax"/> class.
    /// </summary>
    public TableArraySyntax() : base(SyntaxKind.TableArray)
    {
    }

    /// <summary>
    /// Initializes a new table array with the specified name.
    /// </summary>
    /// <param name="name">The table array name.</param>
    public TableArraySyntax(string name) : this()
    {
        ArgumentNullException.ThrowIfNull(name);
        Name = new KeySyntax(name);
        OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracketDouble);
        CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracketDouble);
        EndOfLineToken = SyntaxFactory.NewLine();
    }

    /// <summary>
    /// Initializes a new table array with the specified key syntax.
    /// </summary>
    /// <param name="name">The table array name syntax.</param>
    public TableArraySyntax(KeySyntax name) : this()
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracketDouble);
        CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracketDouble);
        EndOfLineToken = SyntaxFactory.NewLine();
    }

    /// <inheritdoc />
    public override void Accept(SyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }

    internal override TokenKind OpenTokenKind => TokenKind.OpenBracketDouble;

    internal override TokenKind CloseTokenKind => TokenKind.CloseBracketDouble;

    /// <inheritdoc />
    protected override string ToDebuggerDisplay()
    {
        return $"{base.ToDebuggerDisplay()} [[{(Name is not null ? Name.ToString() : string.Empty)}]]";
    }
}
