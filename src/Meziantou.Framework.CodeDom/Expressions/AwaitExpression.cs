namespace Meziantou.Framework.CodeDom;

public class AwaitExpression : Expression
{
    public AwaitExpression()
    {
    }

    public AwaitExpression(Expression? expression)
    {
        Expression = expression;
    }

    public Expression? Expression
    {
        get;
        set => SetParent(ref field, value);
    }

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
