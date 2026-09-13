namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the body of a here-document, including its closing delimiter line.</summary>
/// <remarks>
/// The body starts at the first line break after the <c>&lt;&lt;</c> operator, not right after it, so it follows the
/// command line it belongs to rather than nesting inside it. <see cref="Redirection"/> links back to the redirection
/// that introduced it, and <see cref="ShellRedirectionSyntax.HereDocument"/> is the reverse link.
/// </remarks>
public sealed partial class PosixHereDocumentSyntax
{
    /// <summary>The redirection that introduced this here-document.</summary>
    /// <remarks>
    /// Worked out from the tree rather than stored. Bodies come in the order of the <c>&lt;&lt;</c> redirections that
    /// introduced them, so the n-th body pairs up with the n-th redirection.
    /// </remarks>
    public ShellRedirectionSyntax? Redirection
    {
        get
        {
            foreach (var (redirection, hereDocument) in ShellRedirectionSyntax.PairHereDocuments(this))
            {
                if (ReferenceEquals(hereDocument, this))
                    return redirection;
            }

            return null;
        }
    }

    /// <summary>Returns <see langword="true"/> when the operator was <c>&lt;&lt;-</c>, which strips leading tabs at runtime.</summary>
    public bool StripsLeadingTabs => Redirection?.OperatorToken.Kind() == SyntaxKind.LessThanLessThanDashToken;

    /// <summary>
    /// Returns <see langword="true"/> when the delimiter was quoted, which disables expansion inside the body.
    /// </summary>
    public bool IsQuotedDelimiter => Redirection?.Target?.Parts.Any(part => part is ShellQuotedStringSyntax or ShellEscapeSequenceSyntax) == true;
}
