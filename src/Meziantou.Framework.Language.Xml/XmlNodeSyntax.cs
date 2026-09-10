using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml;

/// <summary>A node that can stand in a document or in an element's content.</summary>
public abstract class XmlNodeSyntax : XmlSyntaxNode
{
    private protected XmlNodeSyntax(GreenNode green, SyntaxNode? parent, int position)
        : base(green, parent, position)
    {
    }
}
