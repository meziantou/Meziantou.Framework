namespace Meziantou.Framework.CodeDom
{
    public class DefaultValueExpression : Expression
    {
        private TypeReference? _type;

        public DefaultValueExpression()
        {
        }

        public DefaultValueExpression(TypeReference? type)
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
