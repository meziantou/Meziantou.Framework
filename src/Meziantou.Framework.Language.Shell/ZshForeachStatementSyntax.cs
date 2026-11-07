namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents the zsh loop forms that take their word list in parentheses: <c>foreach x (a b) ... end</c> and the
/// short <c>for x (a b) command</c>.
/// </summary>
public sealed partial class ZshForeachStatementSyntax
{
    public string VariableName => VariableToken.ValueText;
}
