#nullable disable
namespace Meziantou.Framework.CodeDom
{
    public class AssignStatement : Statement
    {
        private Expression _leftExpression;
        private Expression _rightExpression;

        public AssignStatement()
        {
        }

        public AssignStatement(Expression leftExpression, Expression rightExpression)
        {
            LeftExpression = leftExpression;
            RightExpression = rightExpression;
        }

        public Expression LeftExpression
        {
            get => _leftExpression;
            set => SetParent(ref _leftExpression, value);
        }

        public Expression RightExpression
        {
            get => _rightExpression;
            set => SetParent(ref _rightExpression, value);
        }
    }
}
