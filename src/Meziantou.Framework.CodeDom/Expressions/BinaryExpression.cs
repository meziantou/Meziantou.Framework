namespace Meziantou.Framework.CodeDom
{
    public class BinaryExpression : Expression
    {
        private Expression? _rightExpression;
        private Expression? _leftExpression;

        public BinaryExpression()
        {
        }

        public BinaryExpression(BinaryOperator op, Expression? leftExpression, Expression? rightExpression)
        {
            Operator = op;
            LeftExpression = leftExpression;
            RightExpression = rightExpression;
        }

        public BinaryOperator Operator { get; set; }

        public Expression? LeftExpression
        {
            get => _leftExpression;
            set => SetParent(ref _leftExpression, value);
        }

        public Expression? RightExpression
        {
            get => _rightExpression;
            set => SetParent(ref _rightExpression, value);
        }
    }
}
