namespace Meziantou.Framework.CodeDom
{
    public class CodeReturnStatement : CodeStatement
    {
        private CodeExpression _expression;

        public CodeReturnStatement()
        {
        }

        public CodeReturnStatement(CodeExpression expression)
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