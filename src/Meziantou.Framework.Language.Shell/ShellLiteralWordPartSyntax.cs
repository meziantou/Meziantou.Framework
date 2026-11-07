namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents unquoted literal text inside a word.</summary>
public sealed partial class ShellLiteralWordPartSyntax
{
    /// <summary>The literal text with escape sequences resolved.</summary>
    public string Value => TextToken.ValueText;
}
