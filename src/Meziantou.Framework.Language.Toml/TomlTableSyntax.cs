using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Toml;

/// <summary>A table header such as <c>[database]</c>.</summary>
public sealed class TomlTableSyntax : TomlEntrySyntax
{
    internal TomlTableSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken OpenBracketToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>Gets the table name.</summary>
    public string Name => NameToken.ValueText;

    public SyntaxToken CloseBracketToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Returns this table with the given parts, or itself when nothing changed.</summary>
    public TomlTableSyntax Update(SyntaxToken openBracketToken, SyntaxToken nameToken, SyntaxToken closeBracketToken)
    {
        if (openBracketToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && closeBracketToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.TomlTable(openBracketToken, nameToken, closeBracketToken).WithAnnotationsFrom(this);
    }

    public TomlTableSyntax WithOpenBracketToken(SyntaxToken openBracketToken) => Update(openBracketToken, NameToken, CloseBracketToken);
    public TomlTableSyntax WithNameToken(SyntaxToken nameToken) => Update(OpenBracketToken, nameToken, CloseBracketToken);

    /// <summary>Returns this table with a new name.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public TomlTableSyntax WithName(string name) => WithNameToken(SyntaxFactory.Key(name).WithTriviaFrom(NameToken));

    public TomlTableSyntax WithCloseBracketToken(SyntaxToken closeBracketToken) => Update(OpenBracketToken, NameToken, closeBracketToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(TomlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitTomlTable(this);
    }

    public override TResult? Accept<TResult>(TomlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitTomlTable(this);
    }
}
