namespace Meziantou.Framework.CodeDom
{
    public class CodeUsingStatement : CodeStatement
    {
        private CodeStatement _statement;

        public CodeStatement Statement
        {
            get { return _statement; }
            set { SetParent(ref _statement, value); }
        }

        public CodeStatementCollection Body { get; set; }
    }
}
