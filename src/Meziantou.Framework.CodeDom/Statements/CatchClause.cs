namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a catch clause in a try-catch statement.</summary>
public class CatchClause : CodeObject, ICommentable
{
    public CatchClause()
    {
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
    }

    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
    public string? ExceptionVariableName { get; set; }
    public TypeReference? ExceptionType { get; set; }

    public StatementCollection? Body
    {
        get;
        set => SetParent(ref field, value);
    }
}
