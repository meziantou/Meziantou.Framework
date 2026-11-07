using System.ComponentModel;

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

    /// <summary>The signature of version 3.0.0, from before <see cref="Redirections"/> was added; the redirections are kept.</summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public CmdSetStatementSyntax Update(SyntaxToken setKeyword, SyntaxToken switchToken, SyntaxToken nameToken, SyntaxToken equalsToken, ShellWordSyntax? value)
        => Update(setKeyword, switchToken, nameToken, equalsToken, value, Redirections);
}
