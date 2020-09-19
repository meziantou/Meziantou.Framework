namespace Meziantou.Framework.CodeDom
{
    public class TypeOfExpression : Expression
    {
        public TypeOfExpression()
        {
        }

        public TypeOfExpression(TypeReference? type)
        {
            Type = type;
        }

        public TypeReference? Type { get; set; }
    }
}
