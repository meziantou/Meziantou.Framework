namespace Meziantou.Framework.CodeDom
{
    public class CodeUsingStatement : CodeStatement
    {
        private CodeStatement _statement;

        public CodeStatement Statement
        {
            get { return _statement; }
            set
            {
                _statement = value;
                SetParent(value);
            }
        }

        public CodeStatementCollection Body { get; set; }
    }
}
