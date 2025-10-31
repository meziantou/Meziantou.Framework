namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a statement that adds an event handler (+=).</summary>
public class AddEventHandlerStatement : EventHandlerStatement
{
    public AddEventHandlerStatement()
        : base()
    {
    }

    public AddEventHandlerStatement(Expression? leftExpression, Expression? rightExpression)
        : base(leftExpression, rightExpression)
    {
    }
}
