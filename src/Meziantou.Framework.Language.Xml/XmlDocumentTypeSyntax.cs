using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A document type declaration.</summary>
public sealed class XmlDocumentTypeSyntax : XmlNodeSyntax
{
    internal XmlDocumentTypeSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StartDocumentTypeToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken NameToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken ContentToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));
    public SyntaxToken GreaterThanToken => new(this, Green.GetSlot(3), GetChildPosition(3), GetChildIndex(3));

    /// <summary>Gets the name the declaration gives the root element.</summary>
    public string Name => NameToken.Text;

    /// <summary>Gets everything the declaration holds, which begins with the name.</summary>
    public string? Value
    {
        get
        {
            var value = (NameToken.Text + ContentToken.ToFullString()).Trim();

            return value.Length == 0 ? null : value;
        }
    }

    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    public XmlDocumentTypeSyntax WithName(string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        return string.Equals(name, Name, StringComparison.Ordinal) ? this : WithNameToken(SyntaxFactory.Identifier(name).WithTriviaFrom(NameToken));
    }

    /// <summary>Returns this declaration with different content. The name is taken from the front of the value.</summary>
    public XmlDocumentTypeSyntax WithValue(string? value)
    {
        if (string.Equals(value, Value, StringComparison.Ordinal))
            return this;

        return SyntaxFactory.XmlDocumentType(Name, value).WithAnnotationsFrom(this);
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlDocumentTypeSyntax Update(SyntaxToken startDocumentTypeToken, SyntaxToken nameToken, SyntaxToken contentToken, SyntaxToken greaterThanToken)
    {
        if (startDocumentTypeToken.Node == Green.GetSlot(0) && nameToken.Node == Green.GetSlot(1) && contentToken.Node == Green.GetSlot(2) && greaterThanToken.Node == Green.GetSlot(3))
            return this;

        return SyntaxFactory.XmlDocumentType(startDocumentTypeToken, nameToken, contentToken, greaterThanToken).WithAnnotationsFrom(this);
    }

    public XmlDocumentTypeSyntax WithStartDocumentTypeToken(SyntaxToken startDocumentTypeToken) => Update(startDocumentTypeToken, NameToken, ContentToken, GreaterThanToken);
    public XmlDocumentTypeSyntax WithNameToken(SyntaxToken nameToken) => Update(StartDocumentTypeToken, nameToken, ContentToken, GreaterThanToken);
    public XmlDocumentTypeSyntax WithContentToken(SyntaxToken contentToken) => Update(StartDocumentTypeToken, NameToken, contentToken, GreaterThanToken);
    public XmlDocumentTypeSyntax WithGreaterThanToken(SyntaxToken greaterThanToken) => Update(StartDocumentTypeToken, NameToken, ContentToken, greaterThanToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitDocumentType(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitDocumentType(this);
    }
}
