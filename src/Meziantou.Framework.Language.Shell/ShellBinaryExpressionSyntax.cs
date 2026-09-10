namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents an infix expression such as <c>a + b</c> in arithmetic or <c>$x == y</c> in a conditional. The operator
/// is kept as a token rather than an enum, because the operator sets differ between the two grammars.
/// </summary>
public sealed partial class ShellBinaryExpressionSyntax
{
    /// <summary>The operator text, such as <c>+</c>, <c>-eq</c>, or <c>=~</c>.</summary>
    public string OperatorText => OperatorToken.Text;
}
