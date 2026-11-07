using System.ComponentModel;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a parenthesized block, <c>( ... )</c>, and the redirections that apply to all of it.</summary>
public sealed partial class CmdParenthesizedBlockSyntax
{
    /// <summary>The signature of version 3.0.0, from before <see cref="Redirections"/> was added; the redirections are kept.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public CmdParenthesizedBlockSyntax Update(SyntaxToken openParenToken, ShellStatementListSyntax statements, SyntaxToken closeParenToken)
        => Update(openParenToken, statements, closeParenToken, Redirections);
}
