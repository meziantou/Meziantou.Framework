namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents <c>%VAR%</c>, delayed expansion <c>!VAR!</c>, an argument such as <c>%1</c> or <c>%~dp0</c>, or a loop variable <c>%%i</c>.</summary>
public sealed partial class CmdVariableReferenceSyntax
{
    /// <summary>The referenced name.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for <c>!VAR!</c>, which is resolved at execution time.</summary>
    public bool IsDelayed => OpenToken.Text == "!";

    /// <summary>Returns <see langword="true"/> for a <c>for</c> loop variable, <c>%%i</c>.</summary>
    public bool IsLoopVariable => OpenToken.Text == "%%";
}
