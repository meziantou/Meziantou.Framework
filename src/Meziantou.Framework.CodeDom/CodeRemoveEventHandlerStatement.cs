namespace Meziantou.Framework.CodeDom
{
    public class CodeRemoveEventHandlerStatement : CodeEventHandlerStatement
    {
        public CodeRemoveEventHandlerStatement()
          : base()
        {
        }

        public CodeRemoveEventHandlerStatement(CodeExpression leftExpression, CodeExpression rightExpression)
            : base(leftExpression, rightExpression)
        {
        }
    }
}