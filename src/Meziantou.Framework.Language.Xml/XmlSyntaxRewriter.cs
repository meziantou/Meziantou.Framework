namespace Meziantou.Framework.Language.Xml;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
/// <remarks>
/// A node whose parts all come back unchanged is returned as it was, so rewriting a tree and changing nothing in it
/// costs nothing and keeps every node.
/// </remarks>
/// <example>
/// <code>
/// sealed class RenameRoot : XmlSyntaxRewriter
/// {
///     public override SyntaxNode? VisitElement(XmlElementSyntax node)
///         => node.Name == "root" ? node.WithName("renamed") : base.VisitElement(node);
/// }
/// </code>
/// </example>
public class XmlSyntaxRewriter : XmlSyntaxVisitor<SyntaxNode?>
{
    public virtual SyntaxToken VisitToken(SyntaxToken token) => token;

    public virtual SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) => trivia;

    public virtual SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        where TNode : XmlSyntaxNode
    {
        List<TNode>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var visited = Visit(list[i]) as TNode;
            if (rewritten is null && visited is not null && ReferenceEquals(visited, list[i]))
                continue;

            rewritten ??= [.. list.Take(i)];
            if (visited is not null)
            {
                rewritten.Add(visited);
            }
        }

        return rewritten is null ? list : new SyntaxList<TNode>(rewritten);
    }

    public virtual SyntaxTokenList VisitList(SyntaxTokenList list)
    {
        List<SyntaxToken>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var visited = VisitToken(list[i]);
            if (rewritten is null && visited == list[i])
                continue;

            rewritten ??= [.. list.Take(i)];
            rewritten.Add(visited);
        }

        return rewritten is null ? list : new SyntaxTokenList(rewritten);
    }

    public override SyntaxNode? VisitDocument(XmlDocumentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Nodes), VisitToken(node.EndOfFileToken));
    }

    public override SyntaxNode? VisitElement(XmlElementSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(
            (XmlElementStartTagSyntax?)Visit(node.StartTag) ?? node.StartTag,
            VisitList(node.Content),
            node.EndTag is null ? null : (XmlElementEndTagSyntax?)Visit(node.EndTag));
    }

    public override SyntaxNode? VisitEmptyElement(XmlEmptyElementSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.LessThanToken), VisitToken(node.NameToken), VisitList(node.Attributes), VisitToken(node.SlashGreaterThanToken));
    }

    public override SyntaxNode? VisitElementStartTag(XmlElementStartTagSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.LessThanToken), VisitToken(node.NameToken), VisitList(node.Attributes), VisitToken(node.GreaterThanToken));
    }

    public override SyntaxNode? VisitElementEndTag(XmlElementEndTagSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.LessThanSlashToken), VisitToken(node.NameToken), VisitList(node.SkippedTokens), VisitToken(node.GreaterThanToken));
    }

    public override SyntaxNode? VisitAttribute(XmlAttributeSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.NameToken), VisitToken(node.EqualsToken), VisitToken(node.StartQuoteToken), VisitToken(node.ValueToken), VisitToken(node.EndQuoteToken));
    }

    public override SyntaxNode? VisitText(XmlTextSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.TextToken));
    }

    public override SyntaxNode? VisitComment(XmlCommentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StartCommentToken), VisitToken(node.TextToken), VisitToken(node.EndCommentToken));
    }

    public override SyntaxNode? VisitCDataSection(XmlCDataSectionSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StartCDataToken), VisitToken(node.TextToken), VisitToken(node.EndCDataToken));
    }

    public override SyntaxNode? VisitDeclaration(XmlDeclarationSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StartDeclarationToken), VisitList(node.Attributes), VisitToken(node.EndDeclarationToken));
    }

    public override SyntaxNode? VisitProcessingInstruction(XmlProcessingInstructionSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StartProcessingInstructionToken), VisitToken(node.NameToken), VisitToken(node.DataToken), VisitToken(node.EndProcessingInstructionToken));
    }

    public override SyntaxNode? VisitDocumentType(XmlDocumentTypeSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StartDocumentTypeToken), VisitToken(node.NameToken), VisitToken(node.ContentToken), VisitToken(node.GreaterThanToken));
    }

    public override SyntaxNode? VisitSkippedText(XmlSkippedTextSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }
}
