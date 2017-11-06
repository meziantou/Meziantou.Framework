namespace Meziantou.Framework.CodeDom
{
    public class CodeCatchClause : CodeObject, ICommentable
    {
        private CodeTypeReference _exceptionType;
        private CodeStatementCollection _body;

        public CodeCatchClause()
        {
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
        }

        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }
        public string ExceptionVariableName { get; set; }

        public CodeTypeReference ExceptionType
        {
            get => _exceptionType;
            set => SetParent(ref _exceptionType, value);
        }

        public CodeStatementCollection Body
        {
            get => _body;
            set => SetParent(ref _body, value);
        }
    }
}
