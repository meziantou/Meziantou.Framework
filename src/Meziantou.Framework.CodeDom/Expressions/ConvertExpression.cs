namespace Meziantou.Framework.CodeDom
{
    public class ConvertExpression : Expression
    {
        private Expression? _expression;

        public ConvertExpression()
        {
        }

        public ConvertExpression(Expression? expression, TypeReference? type)
        {
            Expression = expression;
            Type = type;
        }

        public Expression? Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }

        public TypeReference? Type { get; set; }
    }
}
