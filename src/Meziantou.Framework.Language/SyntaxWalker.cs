using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language;

/// <summary>Walks a tree without knowing the language that produced it.</summary>
/// <remarks>
/// This walker dispatches on structure alone. A language that wants to dispatch on kind offers a walker of its own,
/// built on its visitor.
/// </remarks>
/// <example>
/// <code>
/// private sealed class TokenCounter : SyntaxWalker
/// {
///     public TokenCounter() : base(SyntaxWalkerDepth.Token) { }
///     public int Count { get; private set; }
///     protected override void VisitToken(SyntaxToken token) => Count++;
/// }
/// </code>
/// </example>
public abstract class SyntaxWalker
{
    private int _recursionDepth;

    protected SyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <summary>Visits <paramref name="node"/> and everything below it.</summary>
    /// <exception cref="ArgumentNullException"><paramref name="node"/> is <see langword="null"/>.</exception>
    /// <exception cref="InsufficientExecutionStackException">The tree is nested too deeply to walk.</exception>
    public virtual void Visit(SyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            // Turns a deeply nested document into an exception the caller can catch, rather than a lost process.
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit(childNode);
            }
            else if (Depth >= SyntaxWalkerDepth.Token)
            {
                VisitToken(child.AsToken());
            }
        }

        _recursionDepth--;
    }

    protected virtual void VisitToken(SyntaxToken token)
    {
        if (Depth < SyntaxWalkerDepth.Trivia)
            return;

        VisitLeadingTrivia(token);
        VisitTrailingTrivia(token);
    }

    protected virtual void VisitLeadingTrivia(SyntaxToken token)
    {
        foreach (var trivia in token.LeadingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    protected virtual void VisitTrailingTrivia(SyntaxToken token)
    {
        foreach (var trivia in token.TrailingTrivia)
        {
            VisitTrivia(trivia);
        }
    }

    protected virtual void VisitTrivia(SyntaxTrivia trivia)
    {
    }
}
