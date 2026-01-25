using System.Diagnostics;
using System.Linq.Expressions;

namespace Meziantou.Framework;

#if PUBLIC_EXPRESSIONEXTENSIONS
public
#else
internal
#endif
static class ExpressionExtensions
{
    /// <summary>
    /// Combines two expressions with a logical AND, properly rebinding parameters.
    /// </summary>
    public static Expression<Func<T, bool>> AndAlso<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        ArgumentNullException.ThrowIfNull(expr1);
        ArgumentNullException.ThrowIfNull(expr2);

        var parameter = Expression.Parameter(typeof(T));

        var leftVisitor = new ReplaceExpressionVisitor(expr1.Parameters[0], parameter);
        var left = leftVisitor.Visit(expr1.Body);

        var rightVisitor = new ReplaceExpressionVisitor(expr2.Parameters[0], parameter);
        var right = rightVisitor.Visit(expr2.Body);

        Debug.Assert(left is not null, "left is null");
        Debug.Assert(right is not null, "right is null");
        return Expression.Lambda<Func<T, bool>>(Expression.AndAlso(left, right), parameter);
    }

    /// <summary>
    /// Combines two expressions with a logical OR, properly rebinding parameters.
    /// </summary>
    public static Expression<Func<T, bool>> OrElse<T>(this Expression<Func<T, bool>> expr1, Expression<Func<T, bool>> expr2)
    {
        ArgumentNullException.ThrowIfNull(expr1);
        ArgumentNullException.ThrowIfNull(expr2);

        var parameter = Expression.Parameter(typeof(T));

        var leftVisitor = new ReplaceExpressionVisitor(expr1.Parameters[0], parameter);
        var left = leftVisitor.Visit(expr1.Body);

        var rightVisitor = new ReplaceExpressionVisitor(expr2.Parameters[0], parameter);
        var right = rightVisitor.Visit(expr2.Body);

        Debug.Assert(left is not null, "left is null");
        Debug.Assert(right is not null, "right is null");
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left, right), parameter);
    }

    /// <summary>
    /// Negates an expression.
    /// </summary>
    public static Expression<Func<T, bool>> Negate<T>(this Expression<Func<T, bool>> expression)
    {
        return Expression.Lambda<Func<T, bool>>(Expression.Not(expression.Body), expression.Parameters);
    }

    private sealed class ReplaceExpressionVisitor : ExpressionVisitor
    {
        private readonly Expression _oldValue;
        private readonly Expression _newValue;

        public ReplaceExpressionVisitor(Expression oldValue, Expression newValue)
        {
            _oldValue = oldValue;
            _newValue = newValue;
        }

        public override Expression? Visit(Expression? node)
        {
            return node == _oldValue ? _newValue : base.Visit(node);
        }
    }
}
