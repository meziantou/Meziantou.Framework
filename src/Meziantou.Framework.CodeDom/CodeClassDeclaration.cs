namespace Meziantou.Framework.CodeDom
{

    public class CodeClassDeclaration : CodeTypeDeclaration, ITypeParameters, ITypeDeclarationContainer
    {
        public CodeClassDeclaration()
            : this(null)
        {
        }

        public CodeClassDeclaration(string name)
        {
            Name = name;
            Implements = new CodeObjectCollection<CodeTypeReference>(this);
            Parameters = new CodeObjectCollection<CodeTypeReference>(this);
            Members = new CodeObjectCollection<CodeMemberDeclaration>(this);
            Types = new CodeObjectCollection<CodeTypeDeclaration>(this);
        }

        public CodeTypeReference BaseType { get; set; }
        public CodeObjectCollection<CodeTypeReference> Implements { get; }
        public CodeObjectCollection<CodeTypeReference> Parameters { get; }
        public CodeObjectCollection<CodeMemberDeclaration> Members { get; }
        public CodeObjectCollection<CodeTypeDeclaration> Types { get; }
    }
}