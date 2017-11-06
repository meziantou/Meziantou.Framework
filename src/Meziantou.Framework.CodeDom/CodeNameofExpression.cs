namespace Meziantou.Framework.CodeDom
{
    public class CodeNameofExpression : CodeExpression
    {
        private CodeExpression _expression;

        public CodeNameofExpression()
        {
        }

        public CodeNameofExpression(CodeExpression expression)
        {
            Expression = expression;
        }

        public CodeExpression Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }
    }
}