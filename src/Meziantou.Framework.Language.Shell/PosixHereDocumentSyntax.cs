namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the body of a here-document, including its closing delimiter line.</summary>
/// <remarks>
/// The body starts on the line after the whole command line, not right after the <c>&lt;&lt;</c> operator, so it
/// follows the statement it belongs to rather than nesting inside it. <see cref="Redirection"/> links back to the
/// redirection that introduced it, and <see cref="ShellRedirectionSyntax.HereDocument"/> is the reverse link.
/// </remarks>
public sealed class PosixHereDocumentSyntax : ShellStatementSyntax
{
    public PosixHereDocumentSyntax(ShellSyntaxToken bodyToken, ShellSyntaxToken delimiterToken, ShellRedirectionSyntax? redirection)
        : base(
            ShellSyntaxKind.PosixHereDocument,
            bodyToken?.ToFullString() + delimiterToken?.ToFullString(),
            bodyToken?.FullSpan.Start ?? 0,
            [bodyToken!, delimiterToken!])
    {
        BodyToken = bodyToken!;
        DelimiterToken = delimiterToken!;
        Redirection = redirection;
    }

    /// <summary>The raw body text, starting with the line break that follows the redirection operator.</summary>
    public ShellSyntaxToken BodyToken { get; }

    /// <summary>The closing delimiter line. Missing when the source ends before the delimiter appears.</summary>
    public ShellSyntaxToken DelimiterToken { get; }

    /// <summary>The redirection that introduced this here-document.</summary>
    public ShellRedirectionSyntax? Redirection { get; }

    /// <summary>Returns <see langword="true"/> when the operator was <c>&lt;&lt;-</c>, which strips leading tabs at runtime.</summary>
    public bool StripsLeadingTabs => Redirection?.OperatorToken.Kind == ShellSyntaxKind.LessThanLessThanDashToken;

    /// <summary>
    /// Returns <see langword="true"/> when the delimiter was quoted, which disables expansion inside the body.
    /// </summary>
    public bool IsQuotedDelimiter => Redirection?.Target?.Parts.Any(part => part is ShellQuotedStringSyntax) == true;

    public override void Accept(ShellSyntaxVisitor visitor) => visitor.VisitHereDocument(this);
    public override TResult Accept<TResult>(ShellSyntaxVisitor<TResult> visitor) => visitor.VisitHereDocument(this);
}
