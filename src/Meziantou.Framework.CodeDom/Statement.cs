namespace Meziantou.Framework.CodeDom
{
    public class Statement : CodeObject, ICommentable
    {
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }

        public Statement()
        {
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
        }

        public static implicit operator Statement(Expression expression)
        {
            return new ExpressionStatement(expression);
        }
    }
}