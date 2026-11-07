using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A CDATA section, whose text is taken literally.</summary>
public sealed class XmlCDataSectionSyntax : XmlNodeSyntax
{
    internal XmlCDataSectionSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }

    public SyntaxToken StartCDataToken => new(this, Green.GetSlot(0), Position, GetChildIndex(0));
    public SyntaxToken TextToken => new(this, Green.GetSlot(1), GetChildPosition(1), GetChildIndex(1));
    public SyntaxToken EndCDataToken => new(this, Green.GetSlot(2), GetChildPosition(2), GetChildIndex(2));

    /// <summary>Gets the text between the section delimiters.</summary>
    public string Text => TextToken.Text;

    /// <exception cref="ArgumentNullException"><paramref name="text"/> is <see langword="null"/>.</exception>
    public XmlCDataSectionSyntax WithText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);

        return string.Equals(text, Text, StringComparison.Ordinal) ? this : WithTextToken(SyntaxFactory.Token(SyntaxKind.CDataToken, text).WithTriviaFrom(TextToken));
    }

    /// <summary>Returns this node with the given parts, or itself when nothing changed.</summary>
    public XmlCDataSectionSyntax Update(SyntaxToken startCDataToken, SyntaxToken textToken, SyntaxToken endCDataToken)
    {
        if (startCDataToken.Node == Green.GetSlot(0) && textToken.Node == Green.GetSlot(1) && endCDataToken.Node == Green.GetSlot(2))
            return this;

        return SyntaxFactory.XmlCDataSection(startCDataToken, textToken, endCDataToken).WithAnnotationsFrom(this);
    }

    public XmlCDataSectionSyntax WithStartCDataToken(SyntaxToken startCDataToken) => Update(startCDataToken, TextToken, EndCDataToken);
    public XmlCDataSectionSyntax WithTextToken(SyntaxToken textToken) => Update(StartCDataToken, textToken, EndCDataToken);
    public XmlCDataSectionSyntax WithEndCDataToken(SyntaxToken endCDataToken) => Update(StartCDataToken, TextToken, endCDataToken);

    internal override SyntaxNode? GetNodeSlot(int index) => null;
    internal override SyntaxNode? GetCachedSlot(int index) => null;

    public override void Accept(XmlSyntaxVisitor visitor)
    {
        ArgumentNullException.ThrowIfNull(visitor);

        visitor.VisitCDataSection(this);
    }

    public override TResult? Accept<TResult>(XmlSyntaxVisitor<TResult> visitor)
        where TResult : default
    {
        ArgumentNullException.ThrowIfNull(visitor);

        return visitor.VisitCDataSection(this);
    }
}
