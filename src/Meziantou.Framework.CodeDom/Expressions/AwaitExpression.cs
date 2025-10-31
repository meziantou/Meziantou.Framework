namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an await expression for asynchronous operations.</summary>
public class AwaitExpression : Expression
{
    public AwaitExpression()
    {
    }

    /// <summary>Initializes a new instance of the <see cref="AwaitExpression"/> class with the specified expression.</summary>
    /// <param name="expression">The async expression to await.</param>
    public AwaitExpression(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }

    /// <summary>Configures the awaiter to continue on the captured context.</summary>
    /// <param name="continueOnCapturedContext">True to attempt to marshal back to the original context; otherwise, false.</param>
    /// <returns>This <see cref="AwaitExpression"/> instance.</returns>
    public AwaitExpression ConfigureAwait(bool continueOnCapturedContext)
    {
        if (Expression is null)
            return this;

        var awaitExpression = Expression;
        Expression = null; // detach Expression
        Expression = new MethodInvokeExpression(
            new MemberReferenceExpression(awaitExpression, nameof(Task.ConfigureAwait)),
            new LiteralExpression(continueOnCapturedContext));
        return this;
    }
}
