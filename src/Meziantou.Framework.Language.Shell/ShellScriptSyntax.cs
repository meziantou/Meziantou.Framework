namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the root node of a shell script and provides replacement helpers.</summary>
public sealed class ShellScriptSyntax : ShellSyntaxNode
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellScriptSyntax(ShellStatementListSyntax statements, ShellSyntaxToken endOfFileToken, string? fullText = null)
        : base(ShellSyntaxKind.ShellScript, fullText ?? BuildScriptText(statements, endOfFileToken), tokens: [endOfFileToken!])
    {
        ArgumentNullException.ThrowIfNull(statements);
        Statements = statements;
        EndOfFileToken = endOfFileToken!;
        _childNodes = [statements];
    }

    public ShellStatementListSyntax Statements { get; }
    public ShellSyntaxToken EndOfFileToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    public ShellScriptSyntax WithStatements(ShellStatementListSyntax statements)
    {
        ArgumentNullException.ThrowIfNull(statements);
        if (ReferenceEquals(statements, Statements))
            return this;

        return Reparse(statements.ToFullString() + EndOfFileToken.ToFullString());
    }

    /// <summary>
    /// Replaces <paramref name="oldNode"/> with <paramref name="newNode"/> and reparses.
    /// </summary>
    /// <remarks>
    /// When <paramref name="newNode"/> carries no leading trivia of its own, the whitespace and comments in front of
    /// <paramref name="oldNode"/> are kept, so replacing an argument does not glue it to the previous word. Supply
    /// leading trivia on <paramref name="newNode"/> to control the surrounding formatting instead.
    /// </remarks>
    public override ShellScriptSyntax ReplaceNode(ShellSyntaxNode oldNode, ShellSyntaxNode newNode)
    {
        ArgumentNullException.ThrowIfNull(oldNode);
        ArgumentNullException.ThrowIfNull(newNode);

        var keepLeadingTrivia = !newNode.StartsWithTrivia;
        if (TryGetNodeSpan(this, oldNode, out var fullSpan))
            return ReplaceSpan(keepLeadingTrivia ? oldNode.SpanWithoutLeadingTrivia : fullSpan, newNode.ToFullString());

        var search = keepLeadingTrivia ? GetTextWithoutLeadingTrivia(oldNode) : oldNode.ToFullString();
        if (TryFindUniqueTextSpan(search, out var span))
            return ReplaceSpan(span, newNode.ToFullString());

        return this;
    }

    /// <inheritdoc cref="ReplaceNode"/>
    public override ShellScriptSyntax ReplaceToken(ShellSyntaxToken oldToken, ShellSyntaxToken newToken)
    {
        ArgumentNullException.ThrowIfNull(oldToken);
        ArgumentNullException.ThrowIfNull(newToken);

        var keepLeadingTrivia = newToken.LeadingTrivia.Count == 0;
        if (ContainsToken(oldToken) && oldToken.FullSpan.End <= ToFullString().Length)
            return ReplaceSpan(keepLeadingTrivia ? TextSpan.FromBounds(oldToken.Span.Start, Math.Max(oldToken.Span.Start, oldToken.FullSpan.End)) : oldToken.FullSpan, newToken.ToFullString());

        var search = keepLeadingTrivia ? oldToken.Text : oldToken.ToFullString();
        if (TryFindUniqueTextSpan(search, out var span))
            return ReplaceSpan(span, newToken.ToFullString());

        return this;
    }

    private static string GetTextWithoutLeadingTrivia(ShellSyntaxNode node)
    {
        var text = node.ToFullString();
        var offset = node.Span.Start - node.FullSpan.Start;

        return offset > 0 && offset <= text.Length ? text[offset..] : text;
    }

    public override ShellScriptSyntax ReplaceTrivia(ShellSyntaxTrivia oldTrivia, ShellSyntaxTrivia newTrivia)
    {
        ArgumentNullException.ThrowIfNull(oldTrivia);
        ArgumentNullException.ThrowIfNull(newTrivia);

        if (ContainsTrivia(oldTrivia) && oldTrivia.FullSpan.End <= ToFullString().Length)
            return ReplaceSpan(oldTrivia.FullSpan, newTrivia.Text);

        if (TryFindUniqueTextSpan(oldTrivia.Text, out var span))
            return ReplaceSpan(span, newTrivia.Text);

        return this;
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitScript(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitScript(this);

    private ShellScriptSyntax Reparse(string text)
    {
        var options = SyntaxTree?.Options ?? new ShellParseOptions(Dialect ?? ShellDialect.Bash);

        return ShellSyntaxTree.ParseText(text, options).Root;
    }

    private ShellScriptSyntax ReplaceSpan(TextSpan span, string newText)
    {
        var source = ToFullString();
        if (span.End > source.Length)
            return this;

        var builder = new StringBuilder(source.Length - span.Length + newText.Length);
        builder.Append(source.AsSpan(0, span.Start));
        builder.Append(newText);
        builder.Append(source.AsSpan(span.End));

        return Reparse(builder.ToString());
    }

    private static bool TryGetNodeSpan(ShellSyntaxNode current, ShellSyntaxNode targetNode, out TextSpan span)
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

    private bool ContainsToken(ShellSyntaxToken token)
    {
        foreach (var currentToken in DescendantTokens())
        {
            if (ReferenceEquals(currentToken, token))
                return true;
        }

        return false;
    }

    private bool ContainsTrivia(ShellSyntaxTrivia trivia)
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

    private static string BuildScriptText(ShellStatementListSyntax statements, ShellSyntaxToken endOfFileToken)
    {
        ArgumentNullException.ThrowIfNull(statements);
        ArgumentNullException.ThrowIfNull(endOfFileToken);

        return statements.ToFullString() + endOfFileToken.ToFullString();
    }
}
