using System.ComponentModel;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>case ... in ... esac</c> statement.</summary>
public sealed partial class PosixCaseStatementSyntax
{
    /// <summary>The signature of version 3.0.0, from before <see cref="SubjectTerminatorToken"/> was added; it is kept.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PosixCaseStatementSyntax Update(SyntaxToken caseKeyword, ShellWordSyntax subject, SyntaxToken inKeyword, SyntaxList<PosixCaseClauseSyntax> clauses, SyntaxToken esacKeyword)
        => Update(caseKeyword, subject, SubjectTerminatorToken, inKeyword, clauses, esacKeyword);
}
