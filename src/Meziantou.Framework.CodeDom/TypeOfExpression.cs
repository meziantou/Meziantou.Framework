namespace Meziantou.Framework.CodeDom
{
    public class TypeOfExpression : Expression
    {
        private TypeReference _type;

        public TypeOfExpression()
        {
        }

        public TypeOfExpression(TypeReference type)
        {
            Type = type;
        }

        public TypeReference Type
        {
            get => _type;
            set => SetParent(ref _type, value);
        }
    }
}
