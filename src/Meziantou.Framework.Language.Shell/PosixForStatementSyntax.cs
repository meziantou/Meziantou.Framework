using System.ComponentModel;

namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> or <c>select</c> loop over a word list.</summary>
public sealed partial class PosixForStatementSyntax
{
    public string VariableName => VariableToken.ValueText;

    public bool IsSelect => Kind() == SyntaxKind.PosixSelectStatement;

    /// <summary>The signature of version 3.0.0, from before <see cref="AdditionalVariableTokens"/> was added; they are kept.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public PosixForStatementSyntax Update(SyntaxToken keyword, SyntaxToken variableToken, SyntaxToken inKeyword, SyntaxList<ShellWordSyntax> items, SyntaxToken listTerminatorToken, SyntaxToken doKeyword, ShellStatementListSyntax body, SyntaxToken doneKeyword)
        => Update(keyword, variableToken, AdditionalVariableTokens, inKeyword, items, listTerminatorToken, doKeyword, body, doneKeyword);
}
