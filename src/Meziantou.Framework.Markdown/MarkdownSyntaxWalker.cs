using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Markdown;

/// <summary>Visits a Markdown node and everything below it.</summary>
public class MarkdownSyntaxWalker : MarkdownSyntaxVisitor
{
    private int _recursionDepth;

    public MarkdownSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The document is nested too deeply to walk.</exception>
    public override void DefaultVisit(MarkdownSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((MarkdownSyntaxNode)childNode);
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
        {
            return;
        }

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
