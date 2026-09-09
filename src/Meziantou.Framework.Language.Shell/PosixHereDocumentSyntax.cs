namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents the body of a here-document, including its closing delimiter line.</summary>
/// <remarks>
/// The body starts on the line after the whole command line, not right after the <c>&lt;&lt;</c> operator, so it
/// follows the statement it belongs to rather than nesting inside it. <see cref="Redirection"/> links back to the
/// redirection that introduced it, and <see cref="ShellRedirectionSyntax.HereDocument"/> is the reverse link.
/// </remarks>
public sealed partial class PosixHereDocumentSyntax
{
    /// <summary>The redirection that introduced this here-document.</summary>
    /// <remarks>
    /// Worked out from the tree rather than stored. A body sits after the command line that introduced it, so the
    /// here-documents following a statement pair up in order with the <c>&lt;&lt;</c> redirections inside it.
    /// </remarks>
    public ShellRedirectionSyntax? Redirection
    {
        get
        {
            if (Parent is not ShellStatementListSyntax list)
                return null;

            var statements = list.Statements;
            var index = statements.IndexOf(this);
            if (index < 0)
                return null;

            // Count how many here-document bodies stand between the statement that introduced them and this one.
            var ordinal = 0;
            var owner = index - 1;
            while (owner >= 0 && statements[owner] is PosixHereDocumentSyntax)
            {
                ordinal++;
                owner--;
            }

            if (owner < 0)
                return null;

            foreach (var redirection in ShellRedirectionSyntax.HereDocumentRedirectionsIn(statements[owner]))
            {
                if (ordinal-- == 0)
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
    public bool IsQuotedDelimiter => Redirection?.Target?.Parts.Any(part => part is ShellQuotedStringSyntax) == true;
}
