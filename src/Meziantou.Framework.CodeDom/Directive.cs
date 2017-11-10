namespace Meziantou.Framework.CodeDom
{
    public abstract class Directive : CodeObject, ICommentable
    {
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }

        public Directive()
        {
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
        }
    }
}
