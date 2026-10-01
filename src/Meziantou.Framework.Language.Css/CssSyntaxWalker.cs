using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Css;

/// <summary>Visits a CSS node and everything below it.</summary>
/// <example>
/// <code>
/// private sealed class DeclarationCounter : CssSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitDeclaration(CssDeclarationSyntax node) { Count++; base.VisitDeclaration(node); }
/// }
/// </code>
/// </example>
public class CssSyntaxWalker : CssSyntaxVisitor
{
    private int _recursionDepth;

    public CssSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The style sheet is nested too deeply to walk.</exception>
    public override void DefaultVisit(CssSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            // Deeply nested blocks and selectors build a deep tree, so this turns input that parsed
            // perfectly well into an exception the caller can catch rather than a lost process.
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((CssSyntaxNode)childNode);
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
