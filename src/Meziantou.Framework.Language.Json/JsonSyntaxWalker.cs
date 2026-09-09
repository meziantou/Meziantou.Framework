using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Json;

/// <summary>Visits a JSON node and everything below it.</summary>
/// <example>
/// <code>
/// private sealed class StringCounter : JsonSyntaxWalker
/// {
///     public int Count { get; private set; }
///     public override void VisitJsonString(JsonStringSyntax node) { Count++; base.VisitJsonString(node); }
/// }
/// </code>
/// </example>
public class JsonSyntaxWalker : JsonSyntaxVisitor
{
    private int _recursionDepth;

    public JsonSyntaxWalker(SyntaxWalkerDepth depth = SyntaxWalkerDepth.Node) => Depth = depth;

    /// <summary>Gets how far down this walker goes.</summary>
    protected SyntaxWalkerDepth Depth { get; }

    /// <exception cref="InsufficientExecutionStackException">The document is nested too deeply to walk.</exception>
    public override void DefaultVisit(JsonSyntaxNode node)
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
                Visit((JsonSyntaxNode)childNode);
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
