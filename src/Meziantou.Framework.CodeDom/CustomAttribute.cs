namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a custom attribute that can be applied to code declarations.</summary>
/// <example>
/// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.CodeDom.Tests/CSharpCodeGeneratorTests.cs" ref:region="CustomAttribute_Example" ref:dedent="true">
/// var attr = new CustomAttribute(typeof(ObsoleteAttribute));
/// attr.Arguments.Add(new CustomAttributeArgument(new LiteralExpression("Use NewMethod instead")));
///
/// Assert.Equal("""[global::System.ObsoleteAttribute("Use NewMethod instead")]""", attr.ToCsharpString());
/// </code>
/// </example>
public class CustomAttribute : CodeObject, ICommentable
{
    public CustomAttribute()
        : this(typeReference: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="CustomAttribute"/> class with the specified attribute type.</summary>
    /// <param name="typeReference">The type of the attribute.</param>
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
