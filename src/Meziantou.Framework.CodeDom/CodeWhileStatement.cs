namespace Meziantou.Framework.CodeDom
{
    public class CodeWhileStatement : CodeStatement
    {
        private CodeExpression _condition;
        private CodeStatementCollection _body;

        public CodeExpression Condition
        {
            get { return _condition; }
            set { SetParent(ref _condition, value); }
        }

        public CodeStatementCollection Body
        {
            get => _body;
            set => SetParent(ref _body, value);
        }
    }
}
