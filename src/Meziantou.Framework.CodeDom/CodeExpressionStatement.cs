namespace Meziantou.Framework.CodeDom
{
    public class CodeExpressionStatement : CodeStatement
    {
        private CodeExpression _expression;

        public CodeExpressionStatement()
        {
        }

        public CodeExpressionStatement(CodeExpression expression)
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