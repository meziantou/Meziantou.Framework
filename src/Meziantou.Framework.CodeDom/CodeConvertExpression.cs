namespace Meziantou.Framework.CodeDom
{
    public class CodeConvertExpression : CodeExpression
    {
        private CodeExpression _expression;
        private CodeTypeReference _type;

        public CodeConvertExpression()
        {
        }

        public CodeConvertExpression(CodeExpression expression, CodeTypeReference type)
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
