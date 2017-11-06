namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeEventHandlerStatement : CodeStatement
    {
        private CodeExpression _leftExpression;
        private CodeExpression _rightExpression;

        protected CodeEventHandlerStatement()
        {
        }

        protected CodeEventHandlerStatement(CodeExpression leftExpression, CodeExpression rightExpression)
        {
            LeftExpression = leftExpression;
            RightExpression = rightExpression;
        }

        public CodeExpression LeftExpression
        {
            get => _leftExpression;
            set => SetParent(ref _leftExpression, value);
        }

        public CodeExpression RightExpression
        {
            get => _rightExpression;
            set => SetParent(ref _rightExpression, value);
        }
    }
}