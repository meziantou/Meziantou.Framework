namespace Meziantou.Framework.CodeDom
{
    public class CodeTypeOfExpression : CodeExpression
    {
        private CodeTypeReference _type;

        public CodeTypeOfExpression()
        {
        }

        public CodeTypeOfExpression(CodeTypeReference type)
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
