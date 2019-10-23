namespace Meziantou.Framework.CodeDom
{
    public class UnaryExpression : Expression
    {
        private Expression? _expression;

        public UnaryExpression()
        {
        }

        public UnaryExpression(UnaryOperator op, Expression? expression)
        {
            Operator = op;
            Expression = expression;
        }

        public UnaryOperator Operator { get; set; }

        public Expression? Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }
    }
}
