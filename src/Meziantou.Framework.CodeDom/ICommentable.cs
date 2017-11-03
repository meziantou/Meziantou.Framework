namespace Meziantou.Framework.CodeDom
{
    public interface ICommentable
    {
        CodeCommentCollection CommentsAfter { get; }
        CodeCommentCollection CommentsBefore { get; }

    }
}
