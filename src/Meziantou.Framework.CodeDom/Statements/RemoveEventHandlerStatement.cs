namespace Meziantou.Framework.CodeDom;

public class RemoveEventHandlerStatement : EventHandlerStatement
{
    public RemoveEventHandlerStatement()
      : base()
    {
    }

    public RemoveEventHandlerStatement(Expression? leftExpression, Expression? rightExpression)
        : base(leftExpression, rightExpression)
    {
    }
}
