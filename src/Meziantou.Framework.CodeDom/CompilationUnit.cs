namespace Meziantou.Framework.CodeDom;

public class CompilationUnit : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable, INullableContext
{
    public CompilationUnit()
    {
    }

    public NullableContext NullableContext { get; set; }

    public CodeObjectCollection<TypeDeclaration> Types
    {
        get
        {
            return field ??= new CodeObjectCollection<TypeDeclaration>(this);
        }
    }

    public CodeObjectCollection<NamespaceDeclaration> Namespaces
    {
        get
        {
            field ??= new CodeObjectCollection<NamespaceDeclaration>(this);
            return field;
        }
    }

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
