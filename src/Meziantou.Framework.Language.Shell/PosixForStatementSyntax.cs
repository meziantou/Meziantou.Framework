namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> or <c>select</c> loop over a word list.</summary>
public sealed partial class PosixForStatementSyntax
{
    public string VariableName => VariableToken.ValueText;

    public bool IsSelect => Kind() == SyntaxKind.PosixSelectStatement;
}
