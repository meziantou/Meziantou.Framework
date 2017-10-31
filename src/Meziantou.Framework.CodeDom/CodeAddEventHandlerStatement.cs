namespace Meziantou.Framework.CodeDom
{
    public class CodeAddEventHandlerStatement : CodeEventHandlerStatement
    {
        public CodeAddEventHandlerStatement()
            : base()
        {
        }

        public CodeAddEventHandlerStatement(CodeExpression leftExpression, CodeExpression rightExpression)
            : base(leftExpression, rightExpression)
        {
        }
    }
}