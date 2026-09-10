using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Visits a shell node and everything below it.</summary>
/// <example>
/// <code>
/// private sealed class CommandCounter : ShellSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitCommand(ShellCommandSyntax node) { Count++; base.VisitCommand(node); }
/// }
/// </code>
/// </example>
public class ShellSyntaxWalker : ShellSyntaxVisitor
{
    private int _recursionDepth;

    public ShellSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The script is nested too deeply to walk.</exception>
    public override void DefaultVisit(ShellSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            // A long operator or member chain builds a deeply left-nested tree, so this turns input that parsed
            // perfectly well into an exception the caller can catch rather than a lost process.
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        foreach (var child in node.ChildNodesAndTokens())
        {
            if (child.AsNode(out var childNode))
            {
                Visit((ShellSyntaxNode)childNode);
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
