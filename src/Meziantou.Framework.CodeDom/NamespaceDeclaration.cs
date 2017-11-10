namespace Meziantou.Framework.CodeDom
{
    public class NamespaceDeclaration : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable
    {
        public string Name { get; set; }

        public CodeObjectCollection<TypeDeclaration> Types { get; }
        public CodeObjectCollection<UsingDirective> Usings { get; }
        public CodeObjectCollection<NamespaceDeclaration> Namespaces { get; }
        public CommentCollection CommentsBefore { get; }
        public CommentCollection CommentsAfter { get; }

        public NamespaceDeclaration()
            : this(null)
        {
        }

        public NamespaceDeclaration(string name)
        {
            Name = name;
            Types = new CodeObjectCollection<TypeDeclaration>(this);
            Usings = new CodeObjectCollection<UsingDirective>(this);
            Namespaces = new CodeObjectCollection<NamespaceDeclaration>(this);
            CommentsBefore = new CommentCollection(this);
            CommentsAfter = new CommentCollection(this);
        }
    }
}