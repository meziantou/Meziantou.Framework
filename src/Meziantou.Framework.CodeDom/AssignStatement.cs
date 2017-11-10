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
            get { return _leftExpression; }
            set { SetParent(ref _leftExpression, value); }
        }

        public Expression RightExpression
        {
            get { return _rightExpression; }
            set { SetParent(ref _rightExpression, value); }
        }
    }
}