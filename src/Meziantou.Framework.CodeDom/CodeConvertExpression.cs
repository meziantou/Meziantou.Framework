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
            get => _expression;
            set => SetParent(ref _expression, value);
        }

        public CodeTypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }
    }
}
