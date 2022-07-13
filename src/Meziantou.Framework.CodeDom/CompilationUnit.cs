namespace Meziantou.Framework.CodeDom;

public class CompilationUnit : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable, INullableContext
{
    private CodeObjectCollection<TypeDeclaration>? _types;
    private CodeObjectCollection<NamespaceDeclaration>? _namespaces;
    private CodeObjectCollection<UsingDirective>? _usings;
    private CommentCollection? _commentsAfter;
    private CommentCollection? _commentsBefore;

    public CompilationUnit()
    {
    }

    public NullableContext NullableContext { get; set; }

    public CodeObjectCollection<TypeDeclaration> Types
    {
        get
        {
            return _types ??= new CodeObjectCollection<TypeDeclaration>(this);
        }
    }

    public CodeObjectCollection<NamespaceDeclaration> Namespaces
    {
        get
        {
            _namespaces ??= new CodeObjectCollection<NamespaceDeclaration>(this);
            return _namespaces;
        }
    }

    public CodeObjectCollection<UsingDirective> Usings
    {
        get
        {
            return _usings ??= new CodeObjectCollection<UsingDirective>(this);
        }
    }

    public CommentCollection CommentsAfter => _commentsAfter ??= new CommentCollection(this);

    public CommentCollection CommentsBefore => _commentsBefore ??= new CommentCollection(this);
}
