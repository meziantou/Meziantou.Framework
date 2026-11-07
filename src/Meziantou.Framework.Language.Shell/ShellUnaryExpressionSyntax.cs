namespace Meziantou.Framework.Language.Shell;

/// <summary>
/// Represents a prefix expression such as <c>-x</c>, <c>!$a</c>, or the conditional test <c>-f file</c>, and the
/// postfix increment forms <c>i++</c> and <c>i--</c>.
/// </summary>
public sealed partial class ShellUnaryExpressionSyntax
{
    /// <summary>The operator text, whichever side it is on.</summary>
    public string OperatorText => PrefixOperatorToken.IsPresent() ? PrefixOperatorToken.Text : PostfixOperatorToken.Text;

    /// <summary>Returns <see langword="true"/> when the operator follows the operand, as in <c>i++</c>.</summary>
    public bool IsPostfix => Kind() == SyntaxKind.PostfixUnaryExpression;
}
