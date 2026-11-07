namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a leaf of an expression: a number, a variable, or a word. The value is held as a
/// <see cref="ShellWordSyntax"/> so quoting, expansions, and globs keep the structure they have everywhere else.
/// </summary>
public sealed partial class ShellOperandExpressionSyntax
{
    /// <summary>The operand text with quoting resolved, or <see langword="null"/> when it needs runtime expansion.</summary>
    public string? Value => Word.Value;
}
