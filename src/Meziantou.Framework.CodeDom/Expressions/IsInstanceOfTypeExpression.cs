namespace Meziantou.Framework.CodeDom
{
    public class IsInstanceOfTypeExpression : Expression
    {
        private Expression? _expression;
        private TypeReference? _type;

        public IsInstanceOfTypeExpression()
        {
        }

        public IsInstanceOfTypeExpression(Expression? expression, TypeReference? type)
        {
            Expression = expression;
            Type = type;
        }

        public Expression? Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }

        public TypeReference? Type
        {
            get => _type;
            set => _type = value;
        }
    }
}
