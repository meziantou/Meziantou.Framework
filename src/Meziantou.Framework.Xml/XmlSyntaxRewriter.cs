namespace Meziantou.Framework.Xml;

/// <summary>Visitor that can produce a rewritten XML syntax tree.</summary>
/// <example>
/// <code>
/// sealed class RenameRoot : XmlSyntaxRewriter
/// {
///     public override XmlSyntaxNode? VisitElement(XmlElementSyntax node)
///         => node.Name == "root" ? node.WithName("renamed") : base.VisitElement(node);
/// }
/// </code>
/// </example>
public class XmlSyntaxRewriter : XmlSyntaxVisitor<XmlSyntaxNode?>
{
    public override XmlSyntaxNode? VisitDocument(XmlDocumentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenChildren = RewriteNodes(node.ChildNodes);
        if (rewrittenChildren is null)
            return node;

        return node.WithChildNodes(rewrittenChildren);
    }

    public override XmlSyntaxNode? VisitElement(XmlElementSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        var rewrittenAttributes = RewriteNodes(node.Attributes);
        var rewrittenContent = RewriteNodes(node.Content);
        var updatedName = node.Name;
        var hasChanges = rewrittenAttributes is not null || rewrittenContent is not null;

        if (!hasChanges)
            return node;

        return SyntaxFactory.Element(
            updatedName,
            rewrittenAttributes?.Cast<XmlAttributeSyntax>() ?? node.Attributes,
            rewrittenContent ?? node.Content,
            node.IsSelfClosing);
    }

    public override XmlSyntaxNode? VisitEndTag(XmlEndTagSyntax node) => node;
    public override XmlSyntaxNode? VisitAttribute(XmlAttributeSyntax node) => node;
    public override XmlSyntaxNode? VisitText(XmlTextSyntax node) => node;
    public override XmlSyntaxNode? VisitComment(XmlCommentSyntax node) => node;
    public override XmlSyntaxNode? VisitCDataSection(XmlCDataSectionSyntax node) => node;
    public override XmlSyntaxNode? VisitDeclaration(XmlDeclarationSyntax node) => node;
    public override XmlSyntaxNode? VisitProcessingInstruction(XmlProcessingInstructionSyntax node) => node;
    public override XmlSyntaxNode? VisitDocumentType(XmlDocumentTypeSyntax node) => node;
    public override XmlSyntaxNode? VisitSkippedText(XmlSkippedTextSyntax node) => node;

    protected virtual XmlSyntaxNode? VisitCore(XmlSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return Visit(node);
    }

    private List<XmlSyntaxNode>? RewriteNodes<TNode>(IReadOnlyList<TNode> nodes)
        where TNode : XmlSyntaxNode
    {
        List<XmlSyntaxNode>? rewritten = null;
        for (var index = 0; index < nodes.Count; index++)
        {
            var current = nodes[index];
            var updated = VisitCore(current) ?? current;
            if (rewritten is null)
            {
                if (!ReferenceEquals(current, updated))
                {
                    rewritten = [];
                    for (var copyIndex = 0; copyIndex < index; copyIndex++)
                    {
                        rewritten.Add(nodes[copyIndex]);
                    }

                    rewritten.Add(updated);
                }
            }
            else
            {
                rewritten.Add(updated);
            }
        }

        return rewritten;
    }
}
