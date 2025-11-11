namespace Meziantou.Framework.CodeDom;

/// <summary>Base class for all directives (using, etc.).</summary>
public abstract class Directive : CodeObject, ICommentable
{
    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }

    protected Directive()
    {
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
    }
}
