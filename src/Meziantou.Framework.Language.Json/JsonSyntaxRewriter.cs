namespace Meziantou.Framework.Language.Json;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
/// <remarks>
/// A node whose parts all come back unchanged is returned as it was, so rewriting a tree and changing nothing in it
/// costs nothing and keeps every node.
/// </remarks>
/// <example>
/// <code>
/// private sealed class RenameMembers : JsonSyntaxRewriter
/// {
///     public override SyntaxNode? VisitJsonMember(JsonMemberSyntax node) => node.WithName(node.Name.ToUpperInvariant());
/// }
/// </code>
/// </example>
public class JsonSyntaxRewriter : JsonSyntaxVisitor<SyntaxNode?>
{
    public override SyntaxNode? VisitJsonDocument(JsonDocumentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Values), VisitToken(node.EndOfFileToken));
    }

    public override SyntaxNode? VisitJsonObject(JsonObjectSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.OpenBraceToken), VisitList(node.Members), VisitToken(node.CloseBraceToken));
    }

    public override SyntaxNode? VisitJsonMember(JsonMemberSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.NameToken), VisitToken(node.ColonToken), (JsonValueSyntax?)Visit(node.Value) ?? node.Value);
    }

    public override SyntaxNode? VisitJsonArray(JsonArraySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.OpenBracketToken), VisitList(node.Elements), VisitToken(node.CloseBracketToken));
    }

    public override SyntaxNode? VisitJsonString(JsonStringSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StringToken));
    }

    public override SyntaxNode? VisitJsonNumber(JsonNumberSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.NumberToken));
    }

    public override SyntaxNode? VisitJsonLiteral(JsonLiteralSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.LiteralToken));
    }

    public override SyntaxNode? VisitJsonSkippedText(JsonSkippedTextSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }

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
        where TNode : JsonSyntaxNode
    {
        List<TNode>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var visited = Visit(list[i]) as TNode;
            if (rewritten is null && (visited is null || ReferenceEquals(visited, list[i])))
            {
                if (visited is not null)
                    continue;
            }

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
        where TNode : JsonSyntaxNode
    {
        var withSeparators = list.GetWithSeparators();
        List<SyntaxNodeOrToken>? rewritten = null;
        for (var i = 0; i < withSeparators.Count; i++)
        {
            var item = withSeparators[i];
            SyntaxNodeOrToken visited;
            if (item.AsNode(out var node))
            {
                visited = Visit((JsonSyntaxNode)node) ?? node;
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
