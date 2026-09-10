namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a <c>set</c> statement, including its <c>/a</c> and <c>/p</c> forms.</summary>
public sealed partial class CmdSetStatementSyntax
{
    /// <summary>The variable name, or an empty string when the statement has no name.</summary>
    public string Name => NameToken.ValueText;

    /// <summary>Returns <see langword="true"/> for <c>set /a</c>, which evaluates its value arithmetically.</summary>
    public bool IsArithmetic => string.Equals(SwitchToken.Text, "/a", StringComparison.OrdinalIgnoreCase);

    /// <summary>Returns <see langword="true"/> for <c>set /p</c>, which prompts the user.</summary>
    public bool IsPrompt => string.Equals(SwitchToken.Text, "/p", StringComparison.OrdinalIgnoreCase);
}
