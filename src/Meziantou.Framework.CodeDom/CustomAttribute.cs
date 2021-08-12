namespace Meziantou.Framework.CodeDom;

public class CustomAttribute : CodeObject, ICommentable
{
    public CustomAttribute()
        : this(typeReference: null)
    {
    }

    public CustomAttribute(TypeReference? typeReference)
    {
        Arguments = new CodeObjectCollection<CustomAttributeArgument>(this);
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
        Type = typeReference;
    }

    public TypeReference? Type { get; set; }
    public CustomAttributeTarget? Target { get; }
    public CodeObjectCollection<CustomAttributeArgument> Arguments { get; }
    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
}
