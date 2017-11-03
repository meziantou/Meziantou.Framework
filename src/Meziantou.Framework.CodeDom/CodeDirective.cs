namespace Meziantou.Framework.CodeDom
{
    public abstract class CodeDirective : CodeObject, ICommentable
    {
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }

        public CodeDirective()
        {
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
        }
    }
}
