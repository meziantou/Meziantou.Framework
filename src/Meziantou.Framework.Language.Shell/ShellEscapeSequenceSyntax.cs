namespace Meziantou.Framework.Language.Shell;

/// <summary>Represents an escape sequence inside a word, such as <c>\$</c>.</summary>
public sealed partial class ShellEscapeSequenceSyntax
{
    /// <summary>The character the escape sequence produces.</summary>
    public string Value => EscapeToken.ValueText;
}
