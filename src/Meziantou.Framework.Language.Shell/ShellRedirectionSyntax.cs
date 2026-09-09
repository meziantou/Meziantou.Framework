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
                var ordinal = CountPrecedingHereDocumentsInOwner();
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

    /// <summary>How many other here-document redirections precede this one inside the statement that owns it.</summary>
    private int CountPrecedingHereDocumentsInOwner()
    {
        var owner = Ancestors().OfType<ShellStatementSyntax>().FirstOrDefault();
        if (owner is null)
            return 0;

        var ordinal = 0;
        foreach (var redirection in owner.DescendantNodes().OfType<ShellRedirectionSyntax>())
        {
            if (ReferenceEquals(redirection, this))
                break;

            if (redirection.OperatorToken.Kind() is SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken)
            {
                ordinal++;
            }
        }

        return ordinal;
    }
}
