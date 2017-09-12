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
            get { return _expression; }
            set { _expression = SetParent(value); }
        }
    }
}