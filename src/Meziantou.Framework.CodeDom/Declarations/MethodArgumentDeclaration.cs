namespace Meziantou.Framework.CodeDom;

public class MethodArgumentDeclaration : CodeObject, ICustomAttributeContainer, ICommentable
{
    public MethodArgumentDeclaration()
        : this(type: null, name: null)
    {
    }

    public MethodArgumentDeclaration(TypeReference? type, string? name)
    {
        CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);

        Type = type;
        Name = name;
    }

    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
    public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
    public string? Name { get; set; }
    public bool IsExtension { get; set; }
    public TypeReference? Type { get; set; }

    public Expression? DefaultValue
    {
        get;
        set => SetParent(ref field, value);
    }

    public Direction Direction { get; set; }
}
