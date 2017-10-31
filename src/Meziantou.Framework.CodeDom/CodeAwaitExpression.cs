namespace Meziantou.Framework.CodeDom
{
    public class CodeAwaitExpression : CodeExpression
    {
        private CodeExpression _expression;

        public CodeAwaitExpression()
        {
        }

        public CodeAwaitExpression(CodeExpression expression)
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
