namespace Meziantou.Framework.Language.Shell;

/// <summary>Base type for all shell syntax nodes in the immutable syntax tree.</summary>
public abstract class ShellSyntaxNode
{
    protected ShellSyntaxNode(ShellSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<ShellSyntaxToken>? tokens = null)
    {
        Kind = kind;
        FullText = fullText ?? string.Empty;
        FullSpan = new TextSpan(fullStart, FullText.Length);
        Tokens = tokens ?? [];
        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    /// <summary>The exact source text covered by this node, including the trivia of its tokens.</summary>
    protected string FullText { get; }

    public ShellSyntaxKind Kind { get; }

    /// <summary>The child nodes of this node, in source order.</summary>
    public virtual IReadOnlyList<ShellSyntaxNode> ChildNodes => [];

    /// <summary>The tokens owned directly by this node. Tokens of child nodes are not included.</summary>
    public IReadOnlyList<ShellSyntaxToken> Tokens { get; }

    public ShellSyntaxTree? SyntaxTree { get; internal set; }

    public ShellSyntaxNode? Parent => ParentNode;

    /// <summary>The dialect the node was parsed as, or <see langword="null"/> when the node is not attached to a tree.</summary>
    public ShellDialect? Dialect => SyntaxTree?.Dialect ?? Ancestors().Select(node => node.SyntaxTree?.Dialect).FirstOrDefault(dialect => dialect is not null);

    /// <summary>The span of this node excluding leading and trailing trivia.</summary>
    public TextSpan Span
    {
        get
        {
            var start = int.MaxValue;
            var end = -1;
            foreach (var token in DescendantTokens())
            {
                if (token.IsMissing && token.Span.Length == 0)
                    continue;

                start = Math.Min(start, token.Span.Start);
                end = Math.Max(end, token.Span.End);
            }

            if (end < 0)
                return FullSpan;

            return TextSpan.FromBounds(start, end);
        }
    }

    /// <summary>The span of this node including the trivia of its tokens.</summary>
    public TextSpan FullSpan { get; }

    public bool ContainsDiagnostics => SyntaxTree is not null && SyntaxTree.Diagnostics.Count > 0;

    public bool ContainsSkippedText => Kind == ShellSyntaxKind.SkippedText || DescendantNodes().Any(node => node.Kind == ShellSyntaxKind.SkippedText);

    internal ShellSyntaxNode? ParentNode { get; set; }

    /// <summary>Returns the exact source text of this node, including comments and all other trivia.</summary>
    public virtual string ToFullString() => FullText;

    /// <summary>Returns the child nodes and the tokens owned by this node, in source order.</summary>
    public IEnumerable<ShellSyntaxNodeOrToken> ChildNodesAndTokens()
    {
        if (Tokens.Count == 0)
            return ChildNodes.Select(child => new ShellSyntaxNodeOrToken(child));

        if (ChildNodes.Count == 0)
            return Tokens.Select(token => new ShellSyntaxNodeOrToken(token));

        // A node interleaves its own tokens with its children (`$(`, the inner statements, then `)`), so the two
        // lists have to be merged by position rather than concatenated.
        var merged = new List<ShellSyntaxNodeOrToken>(ChildNodes.Count + Tokens.Count);
        merged.AddRange(ChildNodes.Select(child => new ShellSyntaxNodeOrToken(child)));
        merged.AddRange(Tokens.Select(token => new ShellSyntaxNodeOrToken(token)));

        return merged.OrderBy(item => item.FullSpan.Start);
    }

    public IEnumerable<ShellSyntaxNode> DescendantNodes()
    {
        foreach (var child in ChildNodes)
        {
            yield return child;
            foreach (var descendant in child.DescendantNodes())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<ShellSyntaxNode> DescendantNodesAndSelf()
    {
        yield return this;
        foreach (var descendant in DescendantNodes())
        {
            yield return descendant;
        }
    }

    /// <summary>Returns every descendant node and token, in source order.</summary>
    public IEnumerable<ShellSyntaxNodeOrToken> DescendantNodesAndTokens()
    {
        foreach (var item in ChildNodesAndTokens())
        {
            yield return item;
            if (item.IsNode)
            {
                foreach (var descendant in item.Node.DescendantNodesAndTokens())
                {
                    yield return descendant;
                }
            }
        }
    }

    public IEnumerable<ShellSyntaxNode> Ancestors()
    {
        var parent = ParentNode;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.ParentNode;
        }
    }

    public IEnumerable<ShellSyntaxNode> AncestorsAndSelf()
    {
        ShellSyntaxNode? node = this;
        while (node is not null)
        {
            yield return node;
            node = node.ParentNode;
        }
    }

    /// <summary>Returns every token in this subtree, in source order.</summary>
    public IEnumerable<ShellSyntaxToken> DescendantTokens()
    {
        foreach (var item in ChildNodesAndTokens())
        {
            if (item.IsToken)
            {
                yield return item.Token;
                continue;
            }

            foreach (var token in item.Node.DescendantTokens())
            {
                yield return token;
            }
        }
    }

    public IEnumerable<ShellSyntaxTrivia> DescendantTrivia()
    {
        foreach (var token in DescendantTokens())
        {
            foreach (var trivia in token.LeadingTrivia)
            {
                yield return trivia;
            }

            foreach (var trivia in token.TrailingTrivia)
            {
                yield return trivia;
            }
        }
    }

    /// <summary>Returns every comment in this node, in source order.</summary>
    public IEnumerable<ShellSyntaxTrivia> DescendantComments() => DescendantTrivia().Where(trivia => trivia.IsComment);

    public virtual ShellScriptSyntax ReplaceNode(ShellSyntaxNode oldNode, ShellSyntaxNode newNode) => GetScript().ReplaceNode(oldNode, newNode);
    public virtual ShellScriptSyntax ReplaceToken(ShellSyntaxToken oldToken, ShellSyntaxToken newToken) => GetScript().ReplaceToken(oldToken, newToken);
    public virtual ShellScriptSyntax ReplaceTrivia(ShellSyntaxTrivia oldTrivia, ShellSyntaxTrivia newTrivia) => GetScript().ReplaceTrivia(oldTrivia, newTrivia);

    public override string ToString() => ToFullString();

    internal void SetParentAndTree(ShellSyntaxNode? parent, ShellSyntaxTree tree)
    {
        ParentNode = parent;
        SyntaxTree = tree;
        foreach (var child in ChildNodes)
        {
            child.SetParentAndTree(this, tree);
        }

        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    private ShellScriptSyntax GetScript()
    {
        if (this is ShellScriptSyntax script)
            return script;

        if (SyntaxTree is not null)
            return SyntaxTree.Root;

        var parent = ParentNode;
        while (parent is not null)
        {
            if (parent is ShellScriptSyntax parentScript)
                return parentScript;

            parent = parent.ParentNode;
        }

        return ShellSyntaxTree.ParseText(ToFullString(), Dialect ?? ShellDialect.Bash).Root;
    }

    /// <summary>Wraps a required child so a node can build its child list with a collection expression.</summary>
    private protected static ShellSyntaxNode[] SingleNode(ShellSyntaxNode node) => [node];

    /// <summary>Wraps an optional child, yielding nothing when it is absent.</summary>
    private protected static ShellSyntaxNode[] OptionalNode(ShellSyntaxNode? node) => node is null ? [] : [node];

    internal static string BuildFullText(IEnumerable<ShellSyntaxNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.Append(node.ToFullString());
        }

        return builder.ToString();
    }

    internal static string BuildFullText(IEnumerable<ShellSyntaxToken> tokens)
    {
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            builder.Append(token.ToFullString());
        }

        return builder.ToString();
    }

    /// <summary>Returns the start of the first non-empty token or node in <paramref name="parts"/>, or 0 when there is none.</summary>
    internal static int GetFullStart(params ReadOnlySpan<ShellSyntaxNodeOrToken?> parts)
    {
        foreach (var part in parts)
        {
            if (part is { } value && value.FullSpan.Length > 0)
                return value.FullSpan.Start;
        }

        foreach (var part in parts)
        {
            if (part is { } value)
                return value.FullSpan.Start;
        }

        return 0;
    }

    public abstract void Accept(ShellSyntaxVisitor visitor);
    public abstract TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor);
}
