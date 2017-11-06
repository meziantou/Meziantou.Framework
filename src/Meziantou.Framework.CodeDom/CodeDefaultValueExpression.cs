namespace Meziantou.Framework.CodeDom
{
    public class CodeDefaultValueExpression : CodeExpression
    {
        private CodeTypeReference _type;

        public CodeDefaultValueExpression()
        {
        }

        public CodeDefaultValueExpression(CodeTypeReference type)
        {
            Type = type;
        }

        public CodeTypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }
    }
}