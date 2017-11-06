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
            set { SetParent(ref _try, value); }
        }

        public CodeCatchClauseCollection Catch
        {
            get { return _catch; }
            set { SetParent(ref _catch, value); }
        }

        public CodeStatementCollection Finally
        {
            get => _finally;
            set => SetParent(ref _finally, value);
        }
    }
}
