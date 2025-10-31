namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a statement that removes an event handler (-=).</summary>
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
