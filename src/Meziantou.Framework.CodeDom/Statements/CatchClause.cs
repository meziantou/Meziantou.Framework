namespace Meziantou.Framework.CodeDom
{
    public class CatchClause : CodeObject, ICommentable
    {
        private TypeReference? _exceptionType;
        private StatementCollection? _body;

        public CatchClause()
        {
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
        }

        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }
        public string? ExceptionVariableName { get; set; }

        public TypeReference? ExceptionType
        {
            get => _exceptionType;
            set => _exceptionType = value;
        }

        public StatementCollection? Body
        {
            get => _body;
            set => SetParent(ref _body, value);
        }
    }
}
