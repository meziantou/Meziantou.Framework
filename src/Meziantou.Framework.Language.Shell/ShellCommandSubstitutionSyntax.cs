namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents a command substitution such as <c>$(...)</c> or a backquoted command.</summary>
public sealed partial class ShellCommandSubstitutionSyntax
{
    /// <summary>Returns <see langword="true"/> for the legacy backquoted form.</summary>
    public bool IsBackquoted => OpenToken.Kind() == SyntaxKind.BacktickToken;
}
