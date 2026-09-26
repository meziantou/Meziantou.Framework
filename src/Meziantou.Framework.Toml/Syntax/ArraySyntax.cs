using System;

namespace Meziantou.Framework.Toml.Syntax;

/// <summary>
/// An array TOML node.
/// </summary>
public sealed class ArraySyntax : ValueSyntax
{
    private SyntaxToken? _openBracket;
    private SyntaxToken? _closeBracket;

    /// <summary>
    /// Creates an instance of an <see cref="ArraySyntax"/>
    /// </summary>
    public ArraySyntax() : base(SyntaxKind.Array)
    {
        Items = new SyntaxList<ArrayItemSyntax>() { Parent = this };
    }

    /// <summary>
    /// Creates an instance of an <see cref="ArraySyntax"/>
    /// </summary>
    /// <param name="values">An array of integer values</param>
    public ArraySyntax(int[] values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracket);
        CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracket);
        for (int i = 0; i < values.Length; i++)
        {
            var item = new ArrayItemSyntax {Value = new IntegerValueSyntax(values[i])};
            if (i + 1 < values.Length)
            {
                item.Comma = SyntaxFactory.Token(TokenKind.Comma);
                item.Comma.AddTrailingWhitespace();
            }
            Items.Add(item);
        }
    }

    /// <summary>
    /// Creates an instance of an <see cref="ArraySyntax"/>
    /// </summary>
    /// <param name="values">An array of string values</param>
    public ArraySyntax(string[] values) : this()
    {
        ArgumentNullException.ThrowIfNull(values);
        OpenBracket = SyntaxFactory.Token(TokenKind.OpenBracket);
        CloseBracket = SyntaxFactory.Token(TokenKind.CloseBracket);
        for (int i = 0; i < values.Length; i++)
        {
            var item = new ArrayItemSyntax { Value = new StringValueSyntax(values[i]) };
            if (i + 1 < values.Length)
            {
                item.Comma = SyntaxFactory.Token(TokenKind.Comma);
                item.Comma.AddTrailingWhitespace();
            }
            Items.Add(item);
        }
    }

    /// <summary>
    /// Gets or sets the open bracket `[` token
    /// </summary>
    public SyntaxToken? OpenBracket
    {
        get => _openBracket;
        set => ParentToThis(ref _openBracket, value, TokenKind.OpenBracket);
    }

    /// <summary>
    /// Gets the <see cref="ArrayItemSyntax"/> of this array.
    /// </summary>
    public SyntaxList<ArrayItemSyntax> Items { get; }

    /// <summary>
    /// Gets or sets the close bracket `]` token
    /// </summary>
    public SyntaxToken? CloseBracket
    {
        get => _closeBracket;
        set => ParentToThis(ref _closeBracket, value, TokenKind.CloseBracket);
    }

    /// <inheritdoc />
    public override void Accept(SyntaxVisitor visitor)
    {
        visitor.Visit(this);
    }

    /// <inheritdoc />
    public override int ChildrenCount => 3;

    /// <inheritdoc />
    protected override SyntaxNode? GetChildImpl(int index)
    {
        switch (index)
        {
            case 0:
                return OpenBracket;
            case 1:
                return Items;
            default:
                return CloseBracket;
        }
    }

    /// <inheritdoc />
    protected override string ToDebuggerDisplay()
    {
        return $"{base.ToDebuggerDisplay()} Count = {Items.ChildrenCount}";
    }
}
