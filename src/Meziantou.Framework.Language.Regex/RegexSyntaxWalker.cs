using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Regex;

/// <summary>Visits a regular-expression node and everything below it.</summary>
/// <example>
/// <code>
/// private sealed class LiteralCounter : RegexSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitLiteral(RegexLiteralSyntax node) { Count++; base.VisitLiteral(node); }
/// }
/// </code>
/// </example>
public class RegexSyntaxWalker : RegexSyntaxVisitor
{
    private int _recursionDepth;

    public RegexSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The pattern is nested too deeply to walk.</exception>
    public override void DefaultVisit(RegexSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            // Turns a pattern nested too deeply into an exception the caller can catch, rather than a lost process.
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((RegexSyntaxNode)childNode);
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
