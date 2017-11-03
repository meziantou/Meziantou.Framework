namespace Meziantou.Framework.CodeDom
{
    public class CodeNamespaceDeclaration : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer, ICommentable
    {
        public string Name { get; set; }

        public CodeObjectCollection<CodeTypeDeclaration> Types { get; }
        public CodeObjectCollection<CodeUsingDirective> Usings { get; }
        public CodeObjectCollection<CodeNamespaceDeclaration> Namespaces { get; }
        public CodeCommentCollection CommentsBefore { get; }
        public CodeCommentCollection CommentsAfter { get; }

        public CodeNamespaceDeclaration()
            : this(null)
        {
        }

        public CodeNamespaceDeclaration(string name)
        {
            Name = name;
            Types = new CodeObjectCollection<CodeTypeDeclaration>(this);
            Usings = new CodeObjectCollection<CodeUsingDirective>(this);
            Namespaces = new CodeObjectCollection<CodeNamespaceDeclaration>(this);
            CommentsBefore = new CodeCommentCollection(this);
            CommentsAfter = new CodeCommentCollection(this);
        }
    }
}