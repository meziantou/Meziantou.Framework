using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>The closing tag of an element.</summary>
public sealed class XmlElementEndTagSyntax : XmlSyntaxNode
{
    internal XmlElementEndTagSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken LessThanSlashToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));

    /// <summary>
    /// Gets the text a malformed document put between the name and the <c>&gt;</c>. Empty for every valid end tag.
    /// </summary>
    public SyntaxTokenList SkippedTokens => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    public SyntaxToken GreaterThanToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the name of the element the tag closes.</summary>
    public string Name => NameToken.Text;

    /// <summary>Returns this tag closing a different name.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlElementEndTagSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, Name, StringComparison.Ordinal) ? this : WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(NameToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlElementEndTagSyntax Update(SyntaxToken lessThanSlashToken, SyntaxToken nameToken, SyntaxTokenList skippedTokens, SyntaxToken greaterThanToken)
    {
        if (lessThanSlashToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && skippedTokens.Node == Green.GetSlot(2) && greaterThanToken.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.XmlElementEndTag(lessThanSlashToken, nameToken, skippedTokens, greaterThanToken).WithAnnotationsFrom(this);
    }

    public XmlElementEndTagSyntax WithLessThanSlashToken(SyntaxToken lessThanSlashToken) => Update(lessThanSlashToken, NameToken, SkippedTokens, GreaterThanToken);
    public XmlElementEndTagSyntax WithNameToken(SyntaxToken nameToken) => Update(LessThanSlashToken, nameToken, SkippedTokens, GreaterThanToken);
    public XmlElementEndTagSyntax WithSkippedTokens(SyntaxTokenList skippedTokens) => Update(LessThanSlashToken, NameToken, skippedTokens, GreaterThanToken);
    public XmlElementEndTagSyntax WithGreaterThanToken(SyntaxToken greaterThanToken) => Update(LessThanSlashToken, NameToken, SkippedTokens, greaterThanToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitElementEndTag(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitElementEndTag(this);
    }
}
