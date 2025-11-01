namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a compilation unit (source file) containing namespaces, types, and using directives.</summary>
/// <example>
/// <code>
/// var unit = new CompilationUnit();
/// unit.Usings.Add(new UsingDirective("System"));
/// var ns = unit.AddNamespace("MyNamespace");
/// var cls = ns.AddType(new ClassDeclaration("MyClass"));
/// var code = unit.ToCsharpString();
/// </code>
/// </example>
public class CompilationUnit : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable, INullableContext
{
    public CompilationUnit()
    {
    }

    /// <summary>Gets or sets the nullable reference types context for this compilation unit.</summary>
    public NullableContext NullableContext { get; set; }

    /// <summary>Gets the collection of top-level type declarations in this compilation unit.</summary>
    public CodeObjectCollection<TypeDeclaration> Types
    {
        get
        {
            return field ??= new CodeObjectCollection<TypeDeclaration>(this);
        }
    }

    /// <summary>Gets the collection of namespace declarations in this compilation unit.</summary>
    public CodeObjectCollection<NamespaceDeclaration> Namespaces
    {
        get
        {
            field ??= new CodeObjectCollection<NamespaceDeclaration>(this);
            return field;
        }
    }

    /// <summary>Gets the collection of using directives in this compilation unit.</summary>
    public CodeObjectCollection<UsingDirective> Usings
    {
        get
        {
            return field ??= new CodeObjectCollection<UsingDirective>(this);
        }
    }

    public CommentCollection CommentsAfter => field ??= new CommentCollection(this);

    public CommentCollection CommentsBefore { get => field ??= new CommentCollection(this); private set; }
}
