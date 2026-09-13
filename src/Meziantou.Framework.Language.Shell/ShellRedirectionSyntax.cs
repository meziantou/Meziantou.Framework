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
            if (!IsHereDocumentOperator(OperatorToken.Kind()))
                return null;

            foreach (var (redirection, hereDocument) in PairHereDocuments(this))
            {
                if (ReferenceEquals(redirection, this))
                    return hereDocument;
            }

            return null;
        }
    }

    private static bool IsHereDocumentOperator(SyntaxKind kind) => kind is SyntaxKind.LessThanLessThanToken or SyntaxKind.LessThanLessThanDashToken;

    /// <summary>
    /// Pairs the here-document redirections around <paramref name="node"/> with the bodies they introduced.
    /// </summary>
    /// <remarks>
    /// A body starts at the first line break after its redirection, so the bodies come in the same order as the
    /// redirections that introduced them, each one after its own redirection: the n-th body belongs to the n-th
    /// redirection. Where exactly a body sits depends on where that line break is -- after the statement, after a later
    /// statement on the same line, or inside a construct that started on that line -- which is why the pairing is by
    /// order rather than by position.
    /// <para>
    /// A substitution reads its own here-documents, so the pairing is done within the nearest substitution, or the
    /// whole tree, and never looks inside a nested one.
    /// </para>
    /// </remarks>
    internal static IEnumerable<(ShellRedirectionSyntax Redirection, PosixHereDocumentSyntax HereDocument)> PairHereDocuments(ShellSyntaxNode node)
    {
        SyntaxNode scope = node;
        while (scope.Parent is { } parent && scope is not (ShellCommandSubstitutionSyntax or PosixProcessSubstitutionSyntax))
        {
            scope = parent;
        }

        var pending = new Queue<ShellRedirectionSyntax>();
        foreach (var descendant in scope.DescendantNodes(child => ReferenceEquals(child, scope) || child is not (ShellCommandSubstitutionSyntax or PosixProcessSubstitutionSyntax)))
        {
            switch (descendant)
            {
                case ShellRedirectionSyntax redirection when IsHereDocumentOperator(redirection.OperatorToken.Kind()) && redirection.Target is not null && !IsInNestedScope(redirection, scope):
                    pending.Enqueue(redirection);
                    break;

                case PosixHereDocumentSyntax hereDocument when !IsInNestedScope(hereDocument, scope) && pending.TryDequeue(out var owner):
                    yield return (owner, hereDocument);
                    break;
            }
        }
    }

    private static bool IsInNestedScope(SyntaxNode node, SyntaxNode scope)
    {
        for (var ancestor = node.Parent; ancestor is not null && !ReferenceEquals(ancestor, scope); ancestor = ancestor.Parent)
        {
            if (ancestor is ShellCommandSubstitutionSyntax or PosixProcessSubstitutionSyntax)
                return true;
        }

        return false;
    }
}
