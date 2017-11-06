namespace Meziantou.Framework.CodeDom
{
    public class CodeUnaryExpression : CodeExpression
    {
        private CodeExpression _expression;

        public CodeUnaryExpression()
        {
        }

        public CodeUnaryExpression(UnaryOperator op, CodeExpression expression)
        {
            Operator = op;
            Expression = expression;
        }

        public UnaryOperator Operator { get; set; }

        public CodeExpression Expression
        {
            get => _expression;
            set => SetParent(ref _expression, value);
        }
    }
}