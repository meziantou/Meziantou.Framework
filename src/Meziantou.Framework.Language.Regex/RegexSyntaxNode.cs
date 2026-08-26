namespace Meziantou.Framework.Language.Regex;

/// <summary>Base type for all regular-expression syntax nodes in the immutable syntax tree.</summary>
public abstract class RegexSyntaxNode
{
    protected RegexSyntaxNode(RegexSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<RegexSyntaxToken>? tokens = null)
    {
        Kind = kind;
        FullText = fullText ?? string.Empty;
        FullSpan = new TextSpan(fullStart, FullText.Length);
        Tokens = Snapshot(tokens);
        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    /// <summary>Builds a node from its parts, in source order.</summary>
    /// <remarks>
    /// The node's text is the concatenation of its parts and its start is where the first one begins, so a node can
    /// never disagree with the source it was built from. Absent optional parts contribute nothing, and a null entry
    /// in <paramref name="tokens"/> is skipped, so a caller does not have to filter either list itself.
    /// </remarks>
    protected RegexSyntaxNode(RegexSyntaxKind kind, IReadOnlyList<RegexSyntaxToken?>? tokens, params ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
    {
        Kind = kind;
        FullText = BuildText(parts);
        FullSpan = new TextSpan(GetFullStart(parts), FullText.Length);
        Tokens = CollectTokens(tokens);
        foreach (var token in Tokens)
        {
            token.Parent = this;
        }
    }

    private static string BuildText(ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
    {
        if (parts.Length == 1)
            return parts[0].ToFullString();

        var builder = new StringBuilder();
        foreach (var part in parts)
        {
            builder.Append(part.ToFullString());
        }

        return builder.ToString();
    }

    /// <summary>Returns where the node starts: the first part that has text, or the first part at all when none does.</summary>
    /// <remarks>
    /// A zero-width part is skipped first, because a missing token synthesized at the start of an incomplete construct
    /// sits at the position of whatever follows it, which is the same position anyway, while a zero-width part in the
    /// middle of the list would otherwise be indistinguishable from a real one.
    /// </remarks>
    private static int GetFullStart(ReadOnlySpan<RegexSyntaxNodeOrToken> parts)
    {
        foreach (var part in parts)
        {
            if (!part.IsNone && part.FullSpan.Length > 0)
                return part.FullSpan.Start;
        }

        foreach (var part in parts)
        {
            if (!part.IsNone)
                return part.FullSpan.Start;
        }

        return 0;
    }

    private static List<RegexSyntaxToken> CollectTokens(IReadOnlyList<RegexSyntaxToken?>? tokens)
    {
        if (tokens is null || tokens.Count == 0)
            return [];

        var collected = new List<RegexSyntaxToken>(tokens.Count);
        foreach (var token in tokens)
        {
            if (token is not null)
            {
                collected.Add(token);
            }
        }

        return collected;
    }

    /// <summary>Copies a caller-supplied collection so later mutation of it cannot reach the tree.</summary>
    /// <remarks>
    /// A node computes its text and its span once, at construction. Holding on to the caller's list would let them
    /// change what <see cref="ChildNodes"/> reports without changing the text those spans were measured against, and
    /// every invariant the tree offers rests on those two agreeing.
    /// </remarks>
    private protected static IReadOnlyList<T> Snapshot<T>(IReadOnlyList<T>? items) =>
        items is { Count: > 0 } ? [.. items] : [];

    /// <summary>Wraps a part of a node, yielding the absent part when it is null.</summary>
    private protected static RegexSyntaxNodeOrToken Part(RegexSyntaxNode? node) => node is null ? default : new RegexSyntaxNodeOrToken(node);

    /// <inheritdoc cref="Part(RegexSyntaxNode?)"/>
    private protected static RegexSyntaxNodeOrToken Part(RegexSyntaxToken? token) => token is null ? default : new RegexSyntaxNodeOrToken(token);

    /// <summary>Builds a node's child list, skipping the children the source did not contain.</summary>
    private protected static IReadOnlyList<RegexSyntaxNode> Children(params ReadOnlySpan<RegexSyntaxNode?> nodes)
    {
        var children = new List<RegexSyntaxNode>(nodes.Length);
        foreach (var node in nodes)
        {
            if (node is not null)
            {
                children.Add(node);
            }
        }

        return children;
    }

    /// <summary>The exact source text covered by this node, including the trivia of its tokens.</summary>
    protected string FullText { get; }

    public RegexSyntaxKind Kind { get; }

    /// <summary>The child nodes of this node, in source order.</summary>
    public virtual IReadOnlyList<RegexSyntaxNode> ChildNodes => [];

    /// <summary>The tokens owned directly by this node. Tokens of child nodes are not included.</summary>
    public IReadOnlyList<RegexSyntaxToken> Tokens { get; }

    public RegexSyntaxTree? SyntaxTree { get; internal set; }

    public RegexSyntaxNode? Parent => ParentNode;

    /// <summary>The flavor the node was parsed as, or <see langword="null"/> when the node is not attached to a tree.</summary>
    public RegexFlavor? Flavor => SyntaxTree?.Flavor ?? Ancestors().Select(node => node.SyntaxTree?.Flavor).FirstOrDefault(flavor => flavor is not null);

    /// <summary>The options in effect at the first character of this node.</summary>
    /// <remarks>
    /// Inline constructs such as <c>(?i)</c> change the options part-way through a pattern, so this records what was
    /// in effect where the node starts rather than what the whole tree was parsed with. A node built by
    /// <see cref="SyntaxFactory"/> rather than parsed reports <see cref="RegexPatternOptions.None"/>.
    /// </remarks>
    public RegexPatternOptions Options { get; internal set; }

    /// <summary>Returns whether the node's text begins with trivia.</summary>
    /// <remarks>
    /// Reading the leading trivia off the first token is not enough. An incomplete construct can begin with a missing
    /// token of no width, as <c>(?&lt;&gt;a)</c> does with its absent group name, and that token carries no trivia even
    /// though the node does: the trivia sits on the first token that has text.
    /// </remarks>
    internal bool StartsWithTrivia => Span.Start > FullSpan.Start;

    /// <summary>
    /// The span to overwrite when a replacement keeps the trivia in front of the node: everything the node owns except
    /// that leading trivia.
    /// </summary>
    /// <remarks>
    /// This is not <see cref="Span"/>. That span stops at the last token with text, so trivia held by a missing token
    /// at the end of an incomplete construct falls outside it while still being part of the node's text. Overwriting
    /// only <see cref="Span"/> would leave that trivia in place and duplicate it.
    /// </remarks>
    internal TextSpan SpanWithoutLeadingTrivia => TextSpan.FromBounds(Span.Start, Math.Max(Span.Start, FullSpan.End));

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

    public bool ContainsSkippedText => Kind == RegexSyntaxKind.SkippedText || DescendantNodes().Any(node => node.Kind == RegexSyntaxKind.SkippedText);

    internal RegexSyntaxNode? ParentNode { get; set; }

    /// <summary>Returns the exact source text of this node, including comments and all other trivia.</summary>
    public virtual string ToFullString() => FullText;

    /// <summary>Returns the child nodes and the tokens owned by this node, in source order.</summary>
    public IEnumerable<RegexSyntaxNodeOrToken> ChildNodesAndTokens()
    {
        if (Tokens.Count == 0)
            return ChildNodes.Select(child => new RegexSyntaxNodeOrToken(child));

        if (ChildNodes.Count == 0)
            return Tokens.Select(token => new RegexSyntaxNodeOrToken(token));

        // A node interleaves its own tokens with its children (`$(`, the inner statements, then `)`), so the two
        // lists have to be merged by position rather than concatenated.
        var merged = new List<RegexSyntaxNodeOrToken>(ChildNodes.Count + Tokens.Count);
        merged.AddRange(ChildNodes.Select(child => new RegexSyntaxNodeOrToken(child)));
        merged.AddRange(Tokens.Select(token => new RegexSyntaxNodeOrToken(token)));

        return merged.OrderBy(item => item.FullSpan.Start);
    }

    /// <summary>Returns every descendant node, in source order.</summary>
    /// <remarks>
    /// Walked with an explicit stack rather than by recursion: a long operator or member chain builds a deeply
    /// left-nested tree, and recursing over one would run the stack out on input that parsed perfectly well.
    /// </remarks>
    public IEnumerable<RegexSyntaxNode> DescendantNodes()
    {
        var stack = new Stack<RegexSyntaxNode>();
        PushInReverse(stack, ChildNodes);

        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            PushInReverse(stack, node.ChildNodes);
        }
    }

    private static void PushInReverse(Stack<RegexSyntaxNode> stack, IReadOnlyList<RegexSyntaxNode> nodes)
    {
        for (var index = nodes.Count - 1; index >= 0; index--)
        {
            stack.Push(nodes[index]);
        }
    }

    public IEnumerable<RegexSyntaxNode> DescendantNodesAndSelf()
    {
        yield return this;
        foreach (var descendant in DescendantNodes())
        {
            yield return descendant;
        }
    }

    /// <summary>Returns every descendant node and token, in source order.</summary>
    public IEnumerable<RegexSyntaxNodeOrToken> DescendantNodesAndTokens()
    {
        var stack = new Stack<RegexSyntaxNodeOrToken>();
        PushInReverse(stack, ChildNodesAndTokens());

        while (stack.Count > 0)
        {
            var item = stack.Pop();
            yield return item;
            if (item.IsNode)
            {
                PushInReverse(stack, item.Node.ChildNodesAndTokens());
            }
        }
    }

    private static void PushInReverse(Stack<RegexSyntaxNodeOrToken> stack, IEnumerable<RegexSyntaxNodeOrToken> items)
    {
        var buffer = items as IList<RegexSyntaxNodeOrToken> ?? [.. items];
        for (var index = buffer.Count - 1; index >= 0; index--)
        {
            stack.Push(buffer[index]);
        }
    }

    public IEnumerable<RegexSyntaxNode> Ancestors()
    {
        var parent = ParentNode;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.ParentNode;
        }
    }

    public IEnumerable<RegexSyntaxNode> AncestorsAndSelf()
    {
        RegexSyntaxNode? node = this;
        while (node is not null)
        {
            yield return node;
            node = node.ParentNode;
        }
    }

    /// <summary>Returns every token in this subtree, in source order.</summary>
    public IEnumerable<RegexSyntaxToken> DescendantTokens()
    {
        foreach (var item in DescendantNodesAndTokens())
        {
            if (item.IsToken)
            {
                yield return item.Token;
            }
        }
    }

    public IEnumerable<RegexSyntaxTrivia> DescendantTrivia()
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
    public IEnumerable<RegexSyntaxTrivia> DescendantComments() => DescendantTrivia().Where(trivia => trivia.IsComment);

    public virtual RegexPatternSyntax ReplaceNode(RegexSyntaxNode oldNode, RegexSyntaxNode newNode) => GetPattern().ReplaceNode(oldNode, newNode);
    public virtual RegexPatternSyntax ReplaceToken(RegexSyntaxToken oldToken, RegexSyntaxToken newToken) => GetPattern().ReplaceToken(oldToken, newToken);
    public virtual RegexPatternSyntax ReplaceTrivia(RegexSyntaxTrivia oldTrivia, RegexSyntaxTrivia newTrivia) => GetPattern().ReplaceTrivia(oldTrivia, newTrivia);

    /// <summary>
    /// Compares this subtree with <paramref name="other"/> structurally, ignoring trivia. Node kinds, child order,
    /// and token text must match; whitespace, line breaks, and comments are not considered.
    /// </summary>
    public bool IsEquivalentTo(RegexSyntaxNode? other)
    {
        if (ReferenceEquals(this, other))
            return true;

        if (other is null || other.Kind != Kind)
            return false;

        var stack = new Stack<(RegexSyntaxNode Left, RegexSyntaxNode Right)>();
        stack.Push((this, other));

        while (stack.Count > 0)
        {
            var (left, right) = stack.Pop();
            if (left.Kind != right.Kind || !TokensAreEquivalent(left.Tokens, right.Tokens))
                return false;

            if (left.ChildNodes.Count != right.ChildNodes.Count)
                return false;

            for (var index = 0; index < left.ChildNodes.Count; index++)
            {
                stack.Push((left.ChildNodes[index], right.ChildNodes[index]));
            }
        }

        return true;
    }

    private static bool TokensAreEquivalent(IReadOnlyList<RegexSyntaxToken> left, IReadOnlyList<RegexSyntaxToken> right)
    {
        if (left.Count != right.Count)
            return false;

        for (var index = 0; index < left.Count; index++)
        {
            // Only the token itself matters; its trivia is formatting.
            if (left[index].Kind != right[index].Kind || !string.Equals(left[index].Text, right[index].Text, StringComparison.Ordinal))
                return false;
        }

        return true;
    }

    public override string ToString() => ToFullString();

    internal void SetParentAndTree(RegexSyntaxNode? parent, RegexSyntaxTree tree)
    {
        var stack = new Stack<(RegexSyntaxNode Node, RegexSyntaxNode? Parent)>();
        stack.Push((this, parent));

        while (stack.Count > 0)
        {
            var (node, nodeParent) = stack.Pop();
            node.ParentNode = nodeParent;
            node.SyntaxTree = tree;

            foreach (var token in node.Tokens)
            {
                token.Parent = node;
            }

            foreach (var child in node.ChildNodes)
            {
                stack.Push((child, node));
            }
        }
    }

    private RegexPatternSyntax GetPattern()
    {
        if (this is RegexPatternSyntax pattern)
            return pattern;

        if (SyntaxTree is not null)
            return SyntaxTree.Root;

        var parent = ParentNode;
        while (parent is not null)
        {
            if (parent is RegexPatternSyntax parentPattern)
                return parentPattern;

            parent = parent.ParentNode;
        }

        return RegexSyntaxTree.ParseText(ToFullString(), Flavor ?? RegexFlavor.Net).Root;
    }

    internal static string BuildFullText(IEnumerable<RegexSyntaxNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.Append(node.ToFullString());
        }

        return builder.ToString();
    }

    internal static string BuildFullText(IEnumerable<RegexSyntaxToken> tokens)
    {
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            builder.Append(token.ToFullString());
        }

        return builder.ToString();
    }

    public abstract void Accept(RegexSyntaxVisitor visitor);
    public abstract TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor);
}
