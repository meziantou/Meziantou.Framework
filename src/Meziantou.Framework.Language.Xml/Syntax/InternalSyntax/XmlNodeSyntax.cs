using Meziantou.Framework.Language.InternalSyntax;

namespace Meziantou.Framework.Language.Xml.Syntax.InternalSyntax;

/// <summary>A node that can stand in a document or in an element's content.</summary>
internal abstract class XmlNodeSyntax : XmlSyntaxNode
{
    protected XmlNodeSyntax(SyntaxKind kind, SyntaxDiagnosticInfo[]? diagnostics, SyntaxAnnotation[]? annotations)
        : base(kind, diagnostics, annotations)
    {
    }
}
