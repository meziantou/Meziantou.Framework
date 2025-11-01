namespace Meziantou.Framework.CodeDom;

/// <summary>Base class for all member declarations (methods, properties, fields, etc.).</summary>
public abstract class MemberDeclaration : CodeObject, ICustomAttributeContainer, ICommentable, IXmlCommentable, INullableContext
{
    protected MemberDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="MemberDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The member name.</param>
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
