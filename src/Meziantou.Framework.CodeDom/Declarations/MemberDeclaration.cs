namespace Meziantou.Framework.CodeDom;

public abstract class MemberDeclaration : CodeObject, ICustomAttributeContainer, ICommentable, IXmlCommentable, INullableContext
{
    protected MemberDeclaration()
        : this(name: null)
    {
    }

    protected MemberDeclaration(string? name)
    {
        CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
        Implements = new CodeObjectCollection<MemberReferenceExpression>(this);
        CommentsBefore = new CommentCollection(this);
        CommentsAfter = new CommentCollection(this);
        XmlComments = new XmlCommentCollection(this);
        Name = name;
    }

    public string? Name { get; set; }
    public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }
    public CodeObjectCollection<MemberReferenceExpression> Implements { get; }
    public CommentCollection CommentsBefore { get; }
    public CommentCollection CommentsAfter { get; }
    public XmlCommentCollection XmlComments { get; }
    public NullableContext NullableContext { get; set; }
}
