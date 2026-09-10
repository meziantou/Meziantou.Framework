using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Xml;

/// <summary>Visits an XML node and everything below it.</summary>
/// <example>
/// <code>
/// private sealed class AttributeCounter : XmlSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitAttribute(XmlAttributeSyntax node) { Count++; base.VisitAttribute(node); }
/// }
/// </code>
/// </example>
public class XmlSyntaxWalker : XmlSyntaxVisitor
{
    private int _recursionDepth;

    public XmlSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The document is nested too deeply to walk.</exception>
    public override void DefaultVisit(XmlSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            // Turns a document nested too deeply into an exception the caller can catch, rather than a lost process.
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((XmlSyntaxNode)childNode);
            }
            else if (Depth >= SyntaxWalkerDepth.Token)
            {
                VisitToken(child.AsToken());
            }
        }

        _recursionDepth--;
    }

    public virtual void VisitToken(SyntaxToken token)
    {
        if (Depth < SyntaxWalkerDepth.Trivia)
            return;

        foreach (var trivia in token.LeadingTrivia)
        {
            VisitTrivia(trivia);
        }

        foreach (var trivia in token.TrailingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    public virtual void VisitTrivia(SyntaxTrivia trivia)
    {
    }
}
