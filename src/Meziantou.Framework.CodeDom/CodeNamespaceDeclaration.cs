namespace Meziantou.Framework.CodeDom
{
    public class CodeNamespaceDeclaration : CodeObject, ITypeDeclarationContainer, INamespaceDeclarationContainer, IUsingDirectiveContainer
    {
        public string Name { get; set; }

        public CodeObjectCollection<CodeTypeDeclaration> Types { get; }
        public CodeObjectCollection<CodeUsingDirective> Usings { get; }
        public CodeObjectCollection<CodeNamespaceDeclaration> Namespaces { get; }

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
        }
    }
}