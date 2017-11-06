namespace Meziantou.Framework.CodeDom
{
    public class CodeThrowStatement : CodeStatement
    {
        private CodeExpression _expression;

        public CodeThrowStatement()
        {
        }

        public CodeThrowStatement(CodeExpression expression)
        {
            Expression = expression;
        }

        public CodeExpression Expression
        {
            get { return _expression; }
            set { SetParent(ref _expression, value); }
        }
    }
}