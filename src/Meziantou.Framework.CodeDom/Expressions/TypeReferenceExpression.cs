namespace Meziantou.Framework.CodeDom
{
    public class TypeReferenceExpression : Expression
    {
        private TypeReference? _type;

        public TypeReferenceExpression()
        {
        }

        public TypeReferenceExpression(TypeReference? type)
        {
            Type = type;
        }

        public TypeReference? Type
        {
            get => _type;
            set => _type = value;
        }
    }
}
