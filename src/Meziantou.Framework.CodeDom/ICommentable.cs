namespace Meziantou.Framework.CodeDom
{
    public interface ICommentable
    {
        CommentCollection CommentsAfter { get; }
        CommentCollection CommentsBefore { get; }
    }
}
