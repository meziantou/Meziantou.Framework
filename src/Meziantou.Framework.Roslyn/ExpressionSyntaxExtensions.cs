#if !MEZIANTOU_FRAMEWORK_ROSLYN_ENABLE_WARNINGS
#pragma warning disable
#endif
#nullable enable
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
#if ROSLYN_WORKSPACES
using Microsoft.CodeAnalysis.Simplification;
#endif

namespace Meziantou.Framework.Roslyn;

#if !MEZIANTOU_FRAMEWORK_ROSLYN_DISABLE_EMBEDDEDATTRIBUTE
[Microsoft.CodeAnalysis.Embedded]
#endif
internal static partial class ExpressionSyntaxExtensions
{
    /// <summary>
    /// Wraps the expression in parentheses.
    /// </summary>
    /// <remarks>
    /// Generated code usually appends to an expression, either as the target of a member access or as an operand of a
    /// binary operator. Both bind tighter than most expressions, so an operand such as <c>a ? b : c</c> has to be
    /// parenthesized to preserve the meaning of the original code.
    /// <para>
    /// The trivia of the expression stays inside of the parentheses, so the trailing comment of
    /// <c>a + b // comment</c> ends up before the closing parenthesis.
    /// </para>
    /// <para>
    /// When the project references a Roslyn workspaces package, the parentheses are annotated with
    /// <c>Simplifier.Annotation</c>, so a code fix removes the ones the final document doesn't need while
    /// post-processing it.
    /// </para>
    /// </remarks>
    public static ExpressionSyntax Parenthesize(this ExpressionSyntax expression)
    {
        var parenthesized = SyntaxFactory.ParenthesizedExpression(expression);
#if ROSLYN_WORKSPACES
        parenthesized = parenthesized.WithAdditionalAnnotations(Simplifier.Annotation);
#endif

        return parenthesized;
    }

    /// <summary>
    /// Wraps the node in parentheses when it is an expression.
    /// </summary>
    /// <remarks>
    /// Nodes that are not an <see cref="ExpressionSyntax"/>, such as a statement or a type declaration, cannot be
    /// parenthesized and are returned as-is. See <see cref="Parenthesize(ExpressionSyntax)"/> for the details of the
    /// generated parentheses.
    /// </remarks>
    public static SyntaxNode Parenthesize(this SyntaxNode node)
    {
        return node is ExpressionSyntax expression ? expression.Parenthesize() : node;
    }
}
