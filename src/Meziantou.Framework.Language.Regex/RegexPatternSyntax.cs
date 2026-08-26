namespace Meziantou.Framework.Language.Regex;

/// <summary>Represents the root node of a pattern and provides replacement helpers.</summary>
public sealed class RegexPatternSyntax : RegexSyntaxNode
{
    private readonly IReadOnlyList<RegexSyntaxNode> _childNodes;

    public RegexPatternSyntax(
        RegexAlternationSyntax alternation,
        RegexSyntaxToken endOfPatternToken,
        RegexSyntaxToken? openSlashToken = null,
        RegexSyntaxToken? closeSlashToken = null,
        RegexSyntaxToken? flagsToken = null,
        RegexSyntaxToken? trailingToken = null,
        string? fullText = null)
        : base(
            RegexSyntaxKind.Pattern,
            fullText ?? BuildPatternText(openSlashToken, alternation, closeSlashToken, flagsToken, trailingToken, endOfPatternToken),
            openSlashToken?.FullSpan.Start ?? alternation.FullSpan.Start,
            CollectRootTokens(openSlashToken, closeSlashToken, flagsToken, trailingToken, endOfPatternToken))
    {
        Alternation = alternation;
        EndOfPatternToken = endOfPatternToken;
        OpenSlashToken = openSlashToken;
        CloseSlashToken = closeSlashToken;
        FlagsToken = flagsToken;
        TrailingToken = trailingToken;
        _childNodes = [alternation];
    }

    /// <summary>The body of the pattern.</summary>
    public RegexAlternationSyntax Alternation { get; }

    /// <summary>The zero-width token that marks the end of the pattern.</summary>
    public RegexSyntaxToken EndOfPatternToken { get; }

    /// <summary>The opening <c>/</c> when the pattern came from a JavaScript literal.</summary>
    public RegexSyntaxToken? OpenSlashToken { get; }

    /// <summary>The closing <c>/</c> when the pattern came from a JavaScript literal.</summary>
    public RegexSyntaxToken? CloseSlashToken { get; }

    /// <summary>The flag letters that followed a JavaScript literal, such as <c>giu</c>.</summary>
    public RegexSyntaxToken? FlagsToken { get; }

    /// <summary>Content that followed a JavaScript literal's flags, which a literal may not have.</summary>
    public RegexSyntaxToken? TrailingToken { get; }

    /// <summary>Returns <see langword="true"/> when the pattern was read from a JavaScript literal.</summary>
    public bool IsJavaScriptLiteral => OpenSlashToken is not null;

    public override IReadOnlyList<RegexSyntaxNode> ChildNodes => _childNodes;

    public RegexPatternSyntax WithAlternation(RegexAlternationSyntax alternation)
    {
        ArgumentNullException.ThrowIfNull(alternation);
        if (ReferenceEquals(alternation, Alternation))
            return this;

        return Reparse(
            (OpenSlashToken?.ToFullString() ?? string.Empty) + alternation.ToFullString() +
            (CloseSlashToken?.ToFullString() ?? string.Empty) + (FlagsToken?.ToFullString() ?? string.Empty) +
            (TrailingToken?.ToFullString() ?? string.Empty) + EndOfPatternToken.ToFullString());
    }

    /// <summary>
    /// Replaces <paramref name="oldNode"/> with <paramref name="newNode"/> and reparses.
    /// </summary>
    /// <remarks>
    /// When <paramref name="newNode"/> carries no leading trivia of its own, the whitespace and comments in front of
    /// <paramref name="oldNode"/> are kept, so replacing an argument does not glue it to the previous word. Supply
    /// leading trivia on <paramref name="newNode"/> to control the surrounding formatting instead.
    /// </remarks>
    public override RegexPatternSyntax ReplaceNode(RegexSyntaxNode oldNode, RegexSyntaxNode newNode)
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
    public override RegexPatternSyntax ReplaceToken(RegexSyntaxToken oldToken, RegexSyntaxToken newToken)
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

    private static string GetTextWithoutLeadingTrivia(RegexSyntaxNode node)
    {
        var text = node.ToFullString();
        var offset = node.Span.Start - node.FullSpan.Start;

        return offset > 0 && offset <= text.Length ? text[offset..] : text;
    }

    public override RegexPatternSyntax ReplaceTrivia(RegexSyntaxTrivia oldTrivia, RegexSyntaxTrivia newTrivia)
    {
        ArgumentNullException.ThrowIfNull(oldTrivia);
        ArgumentNullException.ThrowIfNull(newTrivia);

        if (ContainsTrivia(oldTrivia) && oldTrivia.FullSpan.End <= ToFullString().Length)
            return ReplaceSpan(oldTrivia.FullSpan, newTrivia.Text);

        if (TryFindUniqueTextSpan(oldTrivia.Text, out var span))
            return ReplaceSpan(span, newTrivia.Text);

        return this;
    }

    public override void Accept(RegexSyntaxVisitor visitor) => visitor.VisitPattern(this);
    public override TResult Accept<TResult>(RegexSyntaxVisitor<TResult> visitor) => visitor.VisitPattern(this);

    /// <summary>Reparses the spliced text the way this pattern was parsed in the first place.</summary>
    /// <remarks>
    /// Going through the tree keeps a JavaScript literal a literal. Without that, replacing a node inside <c>/a/g</c>
    /// would return a bare pattern whose delimiters had become literal slashes.
    /// </remarks>
    private RegexPatternSyntax Reparse(string text)
    {
        if (SyntaxTree is { } tree)
            return tree.Reparse(text).Root;

        return RegexSyntaxTree.ParseText(text, new RegexParseOptions(Flavor ?? RegexFlavor.Net)).Root;
    }

    private RegexPatternSyntax ReplaceSpan(TextSpan span, string newText)
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

    /// <summary>Finds <paramref name="targetNode"/> by identity and reports the span it occupies.</summary>
    /// <remarks>
    /// Walked with a stack rather than by recursion. The parser accepts operator and member chains of any length, so
    /// the tree can be as deep as the pattern is long and recursing on that depth would run the stack out.
    /// </remarks>
    private static bool TryGetNodeSpan(RegexSyntaxNode current, RegexSyntaxNode targetNode, out TextSpan span)
    {
        var pending = new Stack<RegexSyntaxNode>();
        pending.Push(current);

        while (pending.Count > 0)
        {
            var node = pending.Pop();
            if (ReferenceEquals(node, targetNode))
            {
                span = node.FullSpan;
                return true;
            }

            foreach (var child in node.ChildNodes)
            {
                pending.Push(child);
            }
        }

        span = default;
        return false;
    }

    private bool ContainsToken(RegexSyntaxToken token)
    {
        foreach (var currentToken in DescendantTokens())
        {
            if (ReferenceEquals(currentToken, token))
                return true;
        }

        return false;
    }

    private bool ContainsTrivia(RegexSyntaxTrivia trivia)
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

    private static string BuildPatternText(
        RegexSyntaxToken? openSlashToken,
        RegexAlternationSyntax alternation,
        RegexSyntaxToken? closeSlashToken,
        RegexSyntaxToken? flagsToken,
        RegexSyntaxToken? trailingToken,
        RegexSyntaxToken endOfPatternToken)
    {
        ArgumentNullException.ThrowIfNull(alternation);
        ArgumentNullException.ThrowIfNull(endOfPatternToken);

        return (openSlashToken?.ToFullString() ?? string.Empty) + alternation.ToFullString() +
            (closeSlashToken?.ToFullString() ?? string.Empty) + (flagsToken?.ToFullString() ?? string.Empty) +
            (trailingToken?.ToFullString() ?? string.Empty) + endOfPatternToken.ToFullString();
    }

    private static List<RegexSyntaxToken> CollectRootTokens(
        RegexSyntaxToken? openSlashToken,
        RegexSyntaxToken? closeSlashToken,
        RegexSyntaxToken? flagsToken,
        RegexSyntaxToken? trailingToken,
        RegexSyntaxToken endOfPatternToken)
    {
        if (openSlashToken is null && closeSlashToken is null && flagsToken is null && trailingToken is null)
            return [endOfPatternToken];

        var tokens = new List<RegexSyntaxToken>(5);
        if (openSlashToken is not null)
        {
            tokens.Add(openSlashToken);
        }

        if (closeSlashToken is not null)
        {
            tokens.Add(closeSlashToken);
        }

        if (flagsToken is not null)
        {
            tokens.Add(flagsToken);
        }

        if (trailingToken is not null)
        {
            tokens.Add(trailingToken);
        }

        tokens.Add(endOfPatternToken);

        return tokens;
    }
}
