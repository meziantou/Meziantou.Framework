namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a quoted section of a word.</summary>
public sealed class ShellQuotedStringSyntax : ShellWordPartSyntax
{
    private readonly IReadOnlyList<ShellSyntaxNode> _childNodes;

    public ShellQuotedStringSyntax(ShellSyntaxToken openQuoteToken, IReadOnlyList<ShellWordPartSyntax> parts, ShellSyntaxToken closeQuoteToken)
        : base(
            ShellSyntaxKind.QuotedString,
            BuildText(openQuoteToken, parts, closeQuoteToken),
            openQuoteToken?.FullSpan.Start ?? 0,
            [openQuoteToken!, closeQuoteToken!])
    {
        OpenQuoteToken = openQuoteToken!;
        CloseQuoteToken = closeQuoteToken!;
        Parts = parts ?? [];
        _childNodes = [.. Parts];
    }

    public ShellSyntaxToken OpenQuoteToken { get; }
    public IReadOnlyList<ShellWordPartSyntax> Parts { get; }
    public ShellSyntaxToken CloseQuoteToken { get; }
    public override IReadOnlyList<ShellSyntaxNode> ChildNodes => _childNodes;

    /// <summary>Returns <see langword="true"/> for single-quoted text, where neither expansion nor escaping happens.</summary>
    public bool IsVerbatim => OpenQuoteToken.Kind == ShellSyntaxKind.SingleQuoteToken;

    /// <summary>Returns <see langword="true"/> for the bash <c>$'...'</c> form, which resolves ANSI-C escapes.</summary>
    public bool IsAnsiC => OpenQuoteToken.Kind == ShellSyntaxKind.DollarSingleQuoteToken;

    public ShellQuotedStringSyntax WithParts(IEnumerable<ShellWordPartSyntax>? parts)
    {
        var updated = parts?.ToArray() ?? [];
        if (updated.SequenceEqual(Parts))
            return this;

        return new ShellQuotedStringSyntax(OpenQuoteToken, updated, CloseQuoteToken);
    }

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitQuotedString(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitQuotedString(this);

    private static string BuildText(ShellSyntaxToken openQuoteToken, IReadOnlyList<ShellWordPartSyntax>? parts, ShellSyntaxToken closeQuoteToken)
    {
        ArgumentNullException.ThrowIfNull(openQuoteToken);
        ArgumentNullException.ThrowIfNull(closeQuoteToken);

        var builder = new StringBuilder();
        builder.Append(openQuoteToken.ToFullString());
        foreach (var part in parts ?? [])
        {
            builder.Append(part.ToFullString());
        }

        builder.Append(closeQuoteToken.ToFullString());

        return builder.ToString();
    }
}
