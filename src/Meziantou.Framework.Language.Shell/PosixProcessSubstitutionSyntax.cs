namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents process substitution, <c>&lt;(...)</c> or <c>&gt;(...)</c>.</summary>
public sealed partial class PosixProcessSubstitutionSyntax
{
    /// <summary>Returns <see langword="true"/> for <c>&lt;(...)</c>, which the command reads from.</summary>
    public bool IsInput => OpenToken.Kind() == SyntaxKind.LessThanOpenParenToken;

    /// <summary>
    /// Returns <see langword="true"/> for the zsh <c>=(...)</c> form, which writes the output to a temporary file and
    /// passes its name rather than using a pipe.
    /// </summary>
    public bool IsFileSubstitution => OpenToken.Kind() == SyntaxKind.EqualsOpenParenToken;
}
