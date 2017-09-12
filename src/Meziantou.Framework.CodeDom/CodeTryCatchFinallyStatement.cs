namespace Meziantou.Framework.CodeDom
{
    public class CodeTryCatchFinallyStatement : CodeStatement
    {
        private CodeStatementCollection _try;
        private CodeCatchClauseCollection _catch;
        private CodeStatementCollection _finally;

        public CodeStatementCollection Try
        {
            get { return _try; }
            set { _try = SetParent(value); }
        }

        public CodeCatchClauseCollection Catch
        {
            get { return _catch; }
            set { _catch = SetParent(value); }
        }

        public CodeStatementCollection Finally
        {
            get => _finally;
            set => _finally = SetParent(value);
        }
    }
}
