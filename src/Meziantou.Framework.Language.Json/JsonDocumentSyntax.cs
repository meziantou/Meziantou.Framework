namespace Meziantou.Framework.Language.Json;

/// <summary>Represents the root JSON document node and provides replacement helpers.</summary>
public sealed class JsonDocumentSyntax : JsonSyntaxNode
{
    private readonly IReadOnlyList<JsonSyntaxNode> _childNodes;

    public JsonDocumentSyntax(IReadOnlyList<JsonSyntaxNode> childNodes, JsonSyntaxToken endOfFileToken, string? fullText = null)
        : base(JsonSyntaxKind.JsonDocument, fullText ?? BuildDocumentText(childNodes, endOfFileToken), tokens: [endOfFileToken])
    {
        _childNodes = childNodes ?? [];
        EndOfFileToken = endOfFileToken;
    }

    public override IReadOnlyList<JsonSyntaxNode> ChildNodes => _childNodes;
    public JsonValueSyntax? Value => ChildNodes.OfType<JsonValueSyntax>().FirstOrDefault();
    public JsonSyntaxToken EndOfFileToken { get; }

    public JsonDocumentSyntax WithChildNodes(IEnumerable<JsonSyntaxNode> childNodes)
    {
        var nodes = childNodes?.ToArray() ?? [];
        if (nodes.SequenceEqual(ChildNodes))
            return this;

        return JsonSyntaxTree.ParseText(BuildFullText(nodes) + EndOfFileToken.ToFullString()).Root;
    }

    public override JsonDocumentSyntax ReplaceNode(JsonSyntaxNode oldNode, JsonSyntaxNode newNode)
    {
        ArgumentNullException.ThrowIfNull(oldNode);
        ArgumentNullException.ThrowIfNull(newNode);

        if (TryGetNodeSpan(this, oldNode, out var span) || TryFindUniqueTextSpan(oldNode.ToFullString(), out span))
            return ReplaceSpan(span, newNode.ToFullString());

        return this;
    }

    public override JsonDocumentSyntax ReplaceToken(JsonSyntaxToken oldToken, JsonSyntaxToken newToken)
    {
        ArgumentNullException.ThrowIfNull(oldToken);
        ArgumentNullException.ThrowIfNull(newToken);

        if (oldToken.Parent is not null && oldToken.FullSpan.End <= ToFullString().Length)
            return ReplaceSpan(oldToken.FullSpan, newToken.ToFullString());

        if (TryFindUniqueTextSpan(oldToken.ToFullString(), out var span))
            return ReplaceSpan(span, newToken.ToFullString());

        return this;
    }

    public override JsonDocumentSyntax ReplaceTrivia(JsonSyntaxTrivia oldTrivia, JsonSyntaxTrivia newTrivia)
    {
        ArgumentNullException.ThrowIfNull(oldTrivia);
        ArgumentNullException.ThrowIfNull(newTrivia);

        if (ContainsTrivia(oldTrivia) && oldTrivia.FullSpan.End <= ToFullString().Length)
            return ReplaceSpan(oldTrivia.FullSpan, newTrivia.Text);

        if (TryFindUniqueTextSpan(oldTrivia.Text, out var span))
            return ReplaceSpan(span, newTrivia.Text);

        return this;
    }

    public override void Accept(JsonSyntaxVisitor visitor) => visitor.VisitDocument(this);
    public override TResult Accept<TResult>(JsonSyntaxVisitor<TResult> visitor) => visitor.VisitDocument(this);

    private JsonDocumentSyntax ReplaceSpan(TextSpan span, string newText)
    {
        var source = ToFullString();
        if (span.Start < 0 || span.End > source.Length)
            return this;

        var builder = new StringBuilder(source.Length - span.Length + newText.Length);
        builder.Append(source.AsSpan(0, span.Start));
        builder.Append(newText);
        builder.Append(source.AsSpan(span.End));

        return JsonSyntaxTree.ParseText(builder.ToString()).Root;
    }

    private static bool TryGetNodeSpan(JsonSyntaxNode current, JsonSyntaxNode targetNode, out TextSpan span)
    {
        if (ReferenceEquals(current, targetNode))
        {
            span = current.FullSpan;
            return true;
        }

        foreach (var child in current.ChildNodes)
        {
            if (TryGetNodeSpan(child, targetNode, out span))
                return true;
        }

        span = default;
        return false;
    }

    private bool ContainsTrivia(JsonSyntaxTrivia trivia)
    {
        foreach (var currentTrivia in DescendantTrivia())
        {
            if (ReferenceEquals(currentTrivia, trivia))
                return true;
        }

        return false;
    }

    private bool TryFindUniqueTextSpan(string text, out TextSpan span)
    {
        if (text.Length == 0)
        {
            span = default;
            return false;
        }

        var source = ToFullString();
        var firstIndex = source.IndexOf(text, StringComparison.Ordinal);
        if (firstIndex < 0)
        {
            span = default;
            return false;
        }

        var secondIndex = source.IndexOf(text, firstIndex + text.Length, StringComparison.Ordinal);
        if (secondIndex >= 0)
        {
            span = default;
            return false;
        }

        span = TextSpan.FromBounds(firstIndex, firstIndex + text.Length);
        return true;
    }

    private static string BuildDocumentText(IReadOnlyList<JsonSyntaxNode>? childNodes, JsonSyntaxToken endOfFileToken)
    {
        return BuildFullText(childNodes ?? []) + endOfFileToken.ToFullString();
    }
}
