namespace Meziantou.Framework.Language.Json;

/// <summary>Base type for all JSON syntax nodes in the immutable syntax tree.</summary>
public abstract class JsonSyntaxNode
{
    protected JsonSyntaxNode(JsonSyntaxKind kind, string fullText, int fullStart = 0, IReadOnlyList<JsonSyntaxToken>? tokens = null)
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

    protected string FullText { get; }
    public JsonSyntaxKind Kind { get; }
    public virtual IReadOnlyList<JsonSyntaxNode> ChildNodes => [];
    public IReadOnlyList<JsonSyntaxToken> Tokens { get; }
    public JsonSyntaxTree? SyntaxTree { get; internal set; }
    public JsonSyntaxNode? Parent => ParentNode;
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
    public TextSpan FullSpan { get; }
    public bool ContainsDiagnostics => SyntaxTree is not null && SyntaxTree.Diagnostics.Count > 0;
    public bool ContainsSkippedText => Kind == JsonSyntaxKind.JsonSkippedText || DescendantNodes().Any(node => node.Kind == JsonSyntaxKind.JsonSkippedText);
    internal JsonSyntaxNode? ParentNode { get; set; }

    public virtual string ToFullString() => FullText;

    public IEnumerable<JsonSyntaxNodeOrToken> ChildNodesAndTokens()
    {
        foreach (var child in ChildNodes)
        {
            yield return new JsonSyntaxNodeOrToken(child);
        }

        foreach (var token in Tokens)
        {
            yield return new JsonSyntaxNodeOrToken(token);
        }
    }

    public IEnumerable<JsonSyntaxNode> DescendantNodes()
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

    public IEnumerable<JsonSyntaxNodeOrToken> DescendantNodesAndTokens()
    {
        foreach (var token in Tokens)
        {
            yield return new JsonSyntaxNodeOrToken(token);
        }

        foreach (var child in ChildNodes)
        {
            yield return new JsonSyntaxNodeOrToken(child);
            foreach (var descendant in child.DescendantNodesAndTokens())
            {
                yield return descendant;
            }
        }
    }

    public IEnumerable<JsonSyntaxNode> Ancestors()
    {
        var parent = ParentNode;
        while (parent is not null)
        {
            yield return parent;
            parent = parent.ParentNode;
        }
    }

    public IEnumerable<JsonSyntaxNode> AncestorsAndSelf()
    {
        JsonSyntaxNode? node = this;
        while (node is not null)
        {
            yield return node;
            node = node.ParentNode;
        }
    }

    public IEnumerable<JsonSyntaxToken> DescendantTokens()
    {
        foreach (var token in Tokens)
        {
            yield return token;
        }

        foreach (var child in ChildNodes)
        {
            foreach (var token in child.DescendantTokens())
            {
                yield return token;
            }
        }
    }

    public IEnumerable<JsonSyntaxTrivia> DescendantTrivia()
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

    public virtual JsonDocumentSyntax ReplaceNode(JsonSyntaxNode oldNode, JsonSyntaxNode newNode) => GetDocument().ReplaceNode(oldNode, newNode);
    public virtual JsonDocumentSyntax ReplaceToken(JsonSyntaxToken oldToken, JsonSyntaxToken newToken) => GetDocument().ReplaceToken(oldToken, newToken);
    public virtual JsonDocumentSyntax ReplaceTrivia(JsonSyntaxTrivia oldTrivia, JsonSyntaxTrivia newTrivia) => GetDocument().ReplaceTrivia(oldTrivia, newTrivia);

    internal void SetParentAndTree(JsonSyntaxNode? parent, JsonSyntaxTree tree)
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

    private JsonDocumentSyntax GetDocument()
    {
        if (this is JsonDocumentSyntax document)
            return document;

        if (SyntaxTree is not null)
            return SyntaxTree.Root;

        var parent = ParentNode;
        while (parent is not null)
        {
            if (parent is JsonDocumentSyntax parentDocument)
                return parentDocument;

            parent = parent.ParentNode;
        }

        return JsonSyntaxTree.ParseText(ToFullString()).Root;
    }

    internal static string BuildFullText(IEnumerable<JsonSyntaxNode> nodes)
    {
        var builder = new StringBuilder();
        foreach (var node in nodes)
        {
            builder.Append(node.ToFullString());
        }

        return builder.ToString();
    }

    internal static string BuildFullText(IEnumerable<JsonSyntaxToken> tokens)
    {
        var builder = new StringBuilder();
        foreach (var token in tokens)
        {
            builder.Append(token.ToFullString());
        }

        return builder.ToString();
    }

    public abstract void Accept(JsonSyntaxVisitor visitor);
    public abstract TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor);
}
