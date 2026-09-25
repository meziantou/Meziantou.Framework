using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Language.Toml;

/// <summary>Builds a new tree by visiting an old one and returning replacements.</summary>
/// <remarks>
/// A node whose parts all come back unchanged is returned as it was, so rewriting a tree and changing nothing in it
/// costs nothing and keeps every node.
/// </remarks>
/// <example>
/// <code>
/// private sealed class DoubleIntegers : TomlSyntaxRewriter
/// {
///     public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node) => node.WithValue(node.Value * 2);
/// }
/// </code>
/// </example>
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

        return node.Update(VisitToken(node.OpenBracketToken), VisitRequired(node.Key), VisitToken(node.CloseBracketToken));
    }

    public override SyntaxNode? VisitTomlProperty(TomlPropertySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitRequired(node.Key), VisitToken(node.EqualsToken), VisitRequired(node.Value));
    }

    public override SyntaxNode? VisitTomlKey(TomlKeySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }

    public override SyntaxNode? VisitTomlSkippedText(TomlSkippedTextSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }

    /// <exception cref="InsufficientExecutionStackException">The value is nested too deeply to rewrite.</exception>
    public override SyntaxNode? VisitTomlArray(TomlArraySyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Arrays and inline tables are the only nodes that nest, and a tree built by hand can nest them deeper than
        // the stack holds: this throws an exception that can be caught rather than overflowing the stack.
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return node.Update(VisitToken(node.OpenBracketToken), VisitList(node.Elements), VisitToken(node.CloseBracketToken));
    }

    /// <exception cref="InsufficientExecutionStackException">The value is nested too deeply to rewrite.</exception>
    public override SyntaxNode? VisitTomlInlineTable(TomlInlineTableSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        // Arrays and inline tables are the only nodes that nest, and a tree built by hand can nest them deeper than
        // the stack holds: this throws an exception that can be caught rather than overflowing the stack.
        RuntimeHelpers.EnsureSufficientExecutionStack();

        return node.Update(VisitToken(node.OpenBraceToken), VisitList(node.Properties), VisitToken(node.CloseBraceToken));
    }

    public override SyntaxNode? VisitTomlString(TomlStringSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.StringToken));
    }

    public override SyntaxNode? VisitTomlInteger(TomlIntegerSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.IntegerToken));
    }

    public override SyntaxNode? VisitTomlFloat(TomlFloatSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.FloatToken));
    }

    public override SyntaxNode? VisitTomlBoolean(TomlBooleanSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.BooleanToken));
    }

    public override SyntaxNode? VisitTomlDateTime(TomlDateTimeSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitToken(node.DateTimeToken));
    }

    public override SyntaxNode? VisitTomlSkippedValue(TomlSkippedValueSyntax node)
    {
        ArgumentNullException.ThrowIfNull(node);

        return node.Update(VisitList(node.Tokens));
    }

    /// <summary>Rewrites a node that has to be there, keeping it when the rewrite returns <see langword="null"/>.</summary>
    /// <exception cref="InvalidOperationException">The rewrite returned a node that cannot take the place of <paramref name="node"/>.</exception>
    protected TNode VisitRequired<TNode>(TNode node)
        where TNode : TomlSyntaxNode
    {
        ArgumentNullException.ThrowIfNull(node);

        return VisitListElement(node) ?? node;
    }

    /// <summary>Rewrites a node, returning <see langword="null"/> when the rewrite removes it.</summary>
    /// <exception cref="InvalidOperationException">The rewrite returned a node that cannot take the place of <paramref name="node"/>.</exception>
    private TNode? VisitListElement<TNode>(TNode node)
        where TNode : TomlSyntaxNode
        => Visit(node) switch
        {
            null => null,
            TNode result => result,
            var other => throw new InvalidOperationException($"A {node.Kind()} cannot be replaced with a {(SyntaxKind)other.RawKind}: it has to be a {typeof(TNode).Name}."),
        };

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

    /// <summary>Rewrites each node of a list, dropping the ones the rewrite returns <see langword="null"/> for.</summary>
    /// <exception cref="InvalidOperationException">The rewrite returned a node that cannot take the place of the one it rewrote.</exception>
    public virtual SyntaxList<TNode> VisitList<TNode>(SyntaxList<TNode> list)
        where TNode : TomlSyntaxNode
    {
        List<TNode>? rewritten = null;
        for (var i = 0; i < list.Count; i++)
        {
            var visited = VisitListElement(list[i]);
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

    /// <summary>Rewrites the elements of a separated list and its separators.</summary>
    /// <remarks>
    /// An element the rewrite returns <see langword="null"/> for is removed, together with the separator after it, or
    /// the one before it when it is the last element, and the trivia they hold.
    /// </remarks>
    /// <exception cref="InvalidOperationException">The rewrite returned a node that cannot take the place of the one it rewrote.</exception>
    public virtual SeparatedSyntaxList<TNode> VisitList<TNode>(SeparatedSyntaxList<TNode> list)
        where TNode : TomlSyntaxNode
    {
        var withSeparators = list.GetWithSeparators();
        List<SyntaxNodeOrToken>? rewritten = null;
        var skipNextSeparator = false;
        for (var i = 0; i < withSeparators.Count; i++)
        {
            var item = withSeparators[i];
            if (item.AsNode(out var node))
            {
                var visited = VisitListElement((TNode)node);
                if (visited is null)
                {
                    rewritten ??= [.. withSeparators.Take(i)];
                    if (i + 1 < withSeparators.Count)
                    {
                        skipNextSeparator = true;
                    }
                    else if (rewritten.Count > 0)
                    {
                        // The last element has no separator after it, so the one before it goes.
                        rewritten.RemoveAt(rewritten.Count - 1);
                    }

                    continue;
                }

                if (rewritten is null && ReferenceEquals(visited, node))
                    continue;

                rewritten ??= [.. withSeparators.Take(i)];
                rewritten.Add(visited);
            }
            else
            {
                if (skipNextSeparator)
                {
                    skipNextSeparator = false;
                    continue;
                }

                var visited = VisitToken(item.AsToken());
                if (rewritten is null && visited == item.AsToken())
                    continue;

                rewritten ??= [.. withSeparators.Take(i)];
                rewritten.Add(visited);
            }
        }

        return rewritten is null ? list : new SeparatedSyntaxList<TNode>(new SyntaxNodeOrTokenList(rewritten));
    }
}
