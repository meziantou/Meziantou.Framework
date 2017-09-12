namespace Meziantou.Framework.CodeDom
{
    public class CodeCastExpression : CodeExpression
    {
        private CodeExpression _expression;
        private CodeTypeReference _type;

        public CodeCastExpression()
        {
        }

        public CodeCastExpression(CodeExpression expression, CodeTypeReference type)
        {
            Expression = expression;
            Type = type;
        }

        public CodeExpression Expression
        {
            get { return _expression; }
            set { _expression = SetParent(value); }
        }

        public CodeTypeReference Type
        {
            get { return _type; }
            set { _type = SetParent(value); }
        }
    }
}
