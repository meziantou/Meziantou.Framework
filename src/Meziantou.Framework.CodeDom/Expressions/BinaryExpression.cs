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

        public static BinaryExpression And(Expression expr1, Expression expr2, params Expression[] expressions)
        {
            return Create(BinaryOperator.And, expr1, expr2, expressions);
        }

        public static BinaryExpression Or(Expression expr1, Expression expr2, params Expression[] expressions)
        {
            return Create(BinaryOperator.Or, expr1, expr2, expressions);
        }

        private static BinaryExpression Create(BinaryOperator op, Expression expr1, Expression expr2, params Expression[] expressions)
        {
            var result = new BinaryExpression(op, expr1, expr2);
            foreach (var expr in expressions)
            {
                result = new BinaryExpression(op, result, expr);
            }

            return result;
        }
    }
}
