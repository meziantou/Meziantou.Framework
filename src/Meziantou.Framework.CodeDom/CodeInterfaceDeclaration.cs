namespace Meziantou.Framework.CodeDom
{
    public class CodeInterfaceDeclaration : CodeTypeDeclaration, IParametrableType, IInheritanceParameters, ITypeDeclarationContainer
    {
        public CodeInterfaceDeclaration()
            : this(null)
        {
        }

        public CodeInterfaceDeclaration(string name)
        {
            Name = name;
            Implements = new CodeObjectCollection<CodeTypeReference>(this);
            Parameters = new CodeObjectCollection<CodeTypeParameter>(this);
            Members = new CodeObjectCollection<CodeMemberDeclaration>(this);
            Types = new CodeObjectCollection<CodeTypeDeclaration>(this);
        }

        public CodeTypeReference BaseType { get; set; }
        public CodeObjectCollection<CodeTypeReference> Implements { get; }
        public CodeObjectCollection<CodeTypeParameter> Parameters { get; }
        public CodeObjectCollection<CodeMemberDeclaration> Members { get; }
        public CodeObjectCollection<CodeTypeDeclaration> Types { get; }
    }
}