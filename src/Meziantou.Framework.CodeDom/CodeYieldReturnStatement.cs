namespace Meziantou.Framework.CodeDom
{
    public class CodeYieldReturnStatement : CodeStatement
    {
        private CodeExpression _expression;

        public CodeYieldReturnStatement()
        {
        }

        public CodeYieldReturnStatement(CodeExpression expression)
        {
            Expression = expression;
        }

        public CodeExpression Expression
        {
            get { return _expression; }
            set { _expression = SetParent(value); }
        }
    }
}