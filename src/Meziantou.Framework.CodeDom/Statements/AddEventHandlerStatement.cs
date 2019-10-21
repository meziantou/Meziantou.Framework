#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class AddEventHandlerStatement : EventHandlerStatement
    {
        public AddEventHandlerStatement()
            : base()
        {
        }

        public AddEventHandlerStatement(Expression leftExpression, Expression rightExpression)
            : base(leftExpression, rightExpression)
        {
        }
    }
}
