namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an input or output redirection attached to a command.</summary>
public sealed partial class ShellRedirectionSyntax
{
    /// <summary>
    /// The here-document body this redirection introduced, for <c>&lt;&lt;</c> and <c>&lt;&lt;-</c>. The body text
    /// lives after the command line, so it is not part of this node's own text.
    /// </summary>
    public PosixHereDocumentSyntax? HereDocument
    {
        get
        {
            if (OperatorToken.Kind() is not (SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken))
                return null;

            foreach (var ancestor in Ancestors())
            {
                if (ancestor is not ShellStatementListSyntax list)
                    continue;

                var statements = list.Statements;
                var owner = statements.IndexOf(node => node.DescendantNodesAndSelf().Contains(this));
                if (owner < 0)
                    continue;

                // The bodies follow the statement in the order their redirections appear inside it.
                var ordinal = CountPrecedingHereDocumentsIn(statements[owner]);
                for (var index = owner + 1; index < statements.Count; index++)
                {
                    if (statements[index] is not PosixHereDocumentSyntax hereDocument)
                        break;

                    if (ordinal-- == 0)
                        return hereDocument;
                }

                return null;
            }

            return null;
        }
    }

    /// <summary>
    /// The here-document redirections of <paramref name="owner"/>, in source order.
    /// </summary>
    /// <remarks>
    /// Counted over the whole statement the bodies are ordered against, not over the nearest statement above the
    /// redirection: a pipeline is one statement holding several, and its bodies follow it in one sequence, so
    /// counting inside the nearer one would give every branch of the pipeline the same ordinal.
    /// <para>
    /// A statement list of its own is where the walk stops. The body of a substitution has its own list and its own
    /// here-documents, which follow the command line <em>inside</em> the substitution; counting them here would
    /// shift every body of the list outside.
    /// </para>
    /// </remarks>
    internal static IEnumerable<ShellRedirectionSyntax> HereDocumentRedirectionsIn(ShellStatementSyntax owner)
        => owner.DescendantNodes(node => node is not ShellStatementListSyntax)
            .OfType<ShellRedirectionSyntax>()
            .Where(redirection => redirection.OperatorToken.Kind() is SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken);

    private int CountPrecedingHereDocumentsIn(ShellStatementSyntax owner)
    {
        var ordinal = 0;
        foreach (var redirection in HereDocumentRedirectionsIn(owner))
        {
            if (ReferenceEquals(redirection, this))
                break;

            ordinal++;
        }

        return ordinal;
    }
}
