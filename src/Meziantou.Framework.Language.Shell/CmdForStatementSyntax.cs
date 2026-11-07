namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>for</c> loop, including the <c>/d</c>, <c>/r</c>, <c>/l</c>, and <c>/f</c> forms.</summary>
public sealed partial class CmdForStatementSyntax
{
    /// <summary>The loop variable name without its leading percent signs.</summary>
    public string VariableName => VariableToken.Text.TrimStart('%');
}
