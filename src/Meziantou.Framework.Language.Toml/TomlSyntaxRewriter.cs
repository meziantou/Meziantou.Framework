namespace Meziantou.Framework.Language.Toml;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
public class TomlSyntaxRewriter : TomlSyntaxVisitor<SyntaxNode?>
{
    public override SyntaxNode? VisitTomlDocument(TomlDocumentSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Entries), VisitToken(node.EndOfFileToken));
    }

    public override SyntaxNode? VisitTomlTable(TomlTableSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.OpenBracketToken), VisitToken(node.NameToken), VisitToken(node.CloseBracketToken));
    }

    public override SyntaxNode? VisitTomlProperty(TomlPropertySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        var key = VisitToken(node.KeyToken);
        var separator = VisitToken(node.SeparatorToken);
        return node.ValueNode.AsNode() is TomlArraySyntax array
            ? node.Update(key, separator, (TomlArraySyntax?)Visit(array) ?? array)
            : node.Update(key, separator, VisitToken(node.ValueNode.AsToken()));
    }

    public override SyntaxNode? VisitTomlArray(TomlArraySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);
        return node.Update(VisitToken(node.OpenBracketToken), VisitList(node.Contents), VisitToken(node.CloseBracketToken));
    }

    public override SyntaxNode? VisitTomlSkippedText(TomlSkippedTextSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }

    /// <summary>Rewrites a token, putting the trivia around it through <see cref="VisitTrivia"/>.</summary>
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
        where TNode : TomlSyntaxNode
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

    public virtual SyntaxNodeOrTokenList VisitList(SyntaxNodeOrTokenList list)
    {
        List<SyntaxNodeOrToken>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var item = list[i];
            SyntaxNodeOrToken visited = item.AsNode(out var node)
                ? (SyntaxNodeOrToken)(Visit((TomlSyntaxNode)node) ?? node)
                : VisitToken(item.AsToken());

            if (rewritten is null && visited == item)
                continue;

            rewritten ??= [.. list.Take(i)];
            rewritten.Add(visited);
        }

        return rewritten is null ? list : new SyntaxNodeOrTokenList(rewritten);
    }
}
