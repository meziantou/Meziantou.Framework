namespace Meziantou.Framework.CodeDom;

public abstract class Statement : CodeObject, ICommentable, INullableContext
{
    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
    public NullableContext NullableContext { get; set; }

    protected Statement()
    {
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
    }

    public static implicit operator Statement(Expression expression) => new ExpressionStatement(expression);
}
