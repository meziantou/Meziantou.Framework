using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Meziantou.Framework.Analyzers.FullPath;

internal static class ExpressionSyntaxExtensions
{
    /// <summary>
    /// Wraps the expression in parentheses unless it is already a primary expression.
    /// </summary>
    /// <remarks>
    /// Generated code appends to the expression, either as the target of a member access or as an operand of the
    /// <c>/</c> operator. Both bind tighter than most expressions, so an operand such as <c>a ? b : c</c> has to be
    /// parenthesized to keep the meaning of the original code.
    /// </remarks>
    public static ExpressionSyntax Parenthesize(this ExpressionSyntax expression)
    {
        return expression switch
        {
            IdentifierNameSyntax or MemberAccessExpressionSyntax or InvocationExpressionSyntax or ElementAccessExpressionSyntax
                or ParenthesizedExpressionSyntax or ThisExpressionSyntax or BaseExpressionSyntax or LiteralExpressionSyntax
                or PredefinedTypeSyntax or QualifiedNameSyntax => expression,
            _ => SyntaxFactory.ParenthesizedExpression(expression),
        };
    }
}
