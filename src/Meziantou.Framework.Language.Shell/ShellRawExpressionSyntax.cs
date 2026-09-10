namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents expression text kept verbatim, without further structure. Used for constructs whose inner grammar the
/// tree does not model, and as the never-throw fallback when text cannot be parsed as an expression.
/// </summary>
public sealed partial class ShellRawExpressionSyntax
{
    /// <summary>The raw expression text.</summary>
    public string Text => TextToken.Text;
}
