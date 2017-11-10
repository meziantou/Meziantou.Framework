namespace Meziantou.Framework.CodeDom
{
    public class UsingStatement : Statement
    {
        private Statement _statement;

        public Statement Statement
        {
            get { return _statement; }
            set { SetParent(ref _statement, value); }
        }

        public StatementCollection Body { get; set; }
    }
}
