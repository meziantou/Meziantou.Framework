namespace Meziantou.Framework.CodeDom
{
    public class WhileStatement : Statement
    {
        private Expression _condition;
        private StatementCollection _body;

        public Expression Condition
        {
            get { return _condition; }
            set { SetParent(ref _condition, value); }
        }

        public StatementCollection Body
        {
            get => _body;
            set => SetParent(ref _body, value);
        }
    }
}
