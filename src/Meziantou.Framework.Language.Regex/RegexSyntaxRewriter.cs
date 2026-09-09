namespace Meziantou.Framework.Language.Regex;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
/// <remarks>
/// A node whose parts all come back unchanged is returned as it was, so rewriting a tree and changing nothing in it
/// costs nothing and keeps every node.
/// </remarks>
/// <example>
/// <code>
/// private sealed class DotToWord : RegexSyntaxRewriter
/// {
///     public override SyntaxNode? VisitAnyCharacter(RegexAnyCharacterSyntax node) => SyntaxFactory.ClassEscape('w');
/// }
/// </code>
/// </example>
public partial class RegexSyntaxRewriter : RegexSyntaxVisitor<SyntaxNode?>
{
    /// <summary>Rewrites a token, putting the trivia around it through <see cref="VisitTrivia"/>.</summary>
    /// <remarks>
    /// Nothing else reaches a token's trivia, so an override of <see cref="VisitTrivia"/> would never be called if
    /// this returned the token untouched. A rewrite that returns the default trivium removes it.
    /// </remarks>
    public virtual SyntaxToken VisitToken(SyntaxToken token)
    {
        var leading = VisitList(token.LeadingTrivia);
        var trailing = VisitList(token.TrailingTrivia);
        if (leading == token.LeadingTrivia && trailing == token.TrailingTrivia)
            return token;

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
                continue;

            rewritten ??= [.. list.Take(i)];
            if (visited.RawKind != 0)
            {
                rewritten.Add(visited);
            }
        }

        return rewritten is null ? list : new SyntaxTriviaList(rewritten);
    }

    public virtual SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        where TNode : RegexSyntaxNode
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

    /// <summary>Rewrites the elements of a separated list, keeping its separators in place.</summary>
    public virtual SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
        where TNode : RegexSyntaxNode
    {
        var withSeparators = list.GetWithSeparators();
        List<SyntaxNodeOrToken>? rewritten = null;
        for (var i = 0; i < withSeparators.Count; i++)
        {
            var item = withSeparators[i];
            SyntaxNodeOrToken visited;
            if (item.AsNode(out var node))
            {
                visited = Visit((RegexSyntaxNode)node) ?? node;
            }
            else
            {
                visited = VisitToken(item.AsToken());
            }

            if (rewritten is null && visited == item)
                continue;

            rewritten ??= [.. withSeparators.Take(i)];
            rewritten.Add(visited);
        }

        return rewritten is null ? list : new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(rewritten));
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
}
