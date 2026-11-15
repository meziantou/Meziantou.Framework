using System.Runtime.CompilerServices;
using Meziantou.Framework.Language.InternalSyntax;
using Meziantou.Framework.Markdown.Syntax.InternalSyntax;
using SyntaxToken = Meziantou.Framework.Language.SyntaxToken;
using SyntaxTrivia = Meziantou.Framework.Language.SyntaxTrivia;

namespace Meziantou.Framework.Markdown;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
/// <remarks>
/// Every node is rewritten the same way: its children are visited, a child for which the rewrite returns
/// <see langword="null"/> is removed, and a node whose children all come back unchanged is returned as it was, so
/// rewriting a tree and changing nothing in it costs nothing and keeps every node. The text of the removed nodes goes with
/// them, the trivia included: removing a block removes the line endings and the markers of its lines too.
/// </remarks>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Markdown.Tests/MarkdownSyntaxTreeTests.cs" ref:region="MarkdownSyntaxRewriter_RemoveHtml" ref:dedent="true">
/// private sealed class RemoveHtml : MarkdownSyntaxRewriter
/// {
///     public override SyntaxNode? VisitMarkdownHtmlBlock(MarkdownHtmlBlockSyntax node) =&gt; null;
///
///     public override SyntaxNode? VisitMarkdownRawHtml(MarkdownRawHtmlSyntax node) =&gt; null;
/// }
/// </code>
/// </example>
public class MarkdownSyntaxRewriter : MarkdownSyntaxVisitor<SyntaxNode?>
{
    private int _recursionDepth;

    /// <exception cref="InsufficientExecutionStackException">The document is nested too deeply to rewrite.</exception>
    /// <exception cref="InvalidOperationException">A rewrite returned a node that is not a Markdown node.</exception>
    public override SyntaxNode? DefaultVisit(MarkdownSyntaxNode node)
    {
        ArgumentNullException.ThrowIfNull(node);

        _recursionDepth++;
        if (_recursionDepth % 20 == 0)
        {
            RuntimeHelpers.EnsureSufficientExecutionStack();
        }

        var children = node.ChildNodesAndTokens();
        GreenNode?[]? rewritten = null;
        for (var i = 0; i < children.Count; i++)
        {
            var child = children[i];
            GreenNode? original;
            GreenNode? green;
            if (child.AsNode(out var childNode))
            {
                original = childNode.Green;
                green = Visit((MarkdownSyntaxNode)childNode) switch
                {
                    null => null,
                    MarkdownSyntaxNode result => result.Green,
                    var other => throw new InvalidOperationException($"A {node.Kind()} cannot have a child of kind {other.RawKind}: it has to be a Markdown node."),
                };
            }
            else
            {
                var token = child.AsToken();
                original = token.Node;
                green = VisitToken(token).Node;
            }

            if (rewritten is null && ReferenceEquals(green, original))
            {
                continue;
            }

            if (rewritten is null)
            {
                rewritten = new GreenNode?[children.Count];
                for (var j = 0; j < i; j++)
                {
                    rewritten[j] = children[j].AsNode(out var previous) ? previous.Green : children[j].AsToken().Node;
                }
            }

            rewritten[i] = green;
        }

        _recursionDepth--;
        if (rewritten is null)
        {
            return node;
        }

        GreenNode updated = new MarkdownNode(node.Kind(), rewritten);
        if (node.Green.GetAnnotations() is { Length: > 0 } annotations)
        {
            updated = updated.SetAnnotations(annotations);
        }

        return updated.CreateRed(parent: null, position: 0);
    }

    /// <summary>Rewrites a token, putting the trivia around it through <see cref="VisitTrivia"/>.</summary>
    public virtual SyntaxToken VisitToken(SyntaxToken token)
    {
        var leading = VisitList(token.LeadingTrivia);
        var trailing = VisitList(token.TrailingTrivia);
        if (leading == token.LeadingTrivia && trailing == token.TrailingTrivia)
        {
            return token;
        }

        return token.WithLeadingTrivia(leading).WithTrailingTrivia(trailing);
    }

    public virtual SyntaxTrivia VisitTrivia(SyntaxTrivia trivia) => trivia;

    /// <summary>Rewrites each trivium of a list, dropping the ones a rewrite turned into the default trivium.</summary>
    public virtual SyntaxTriviaList VisitList(SyntaxTriviaList list)
    {
        List<SyntaxTrivia>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var visited = VisitTrivia(list[i]);
            if (rewritten is null && visited == list[i])
            {
                continue;
            }

            rewritten ??= [.. list.Take(i)];
            if (visited.RawKind != 0)
            {
                rewritten.Add(visited);
            }
        }

        return rewritten is null ? list : new SyntaxTriviaList(rewritten);
    }
}
