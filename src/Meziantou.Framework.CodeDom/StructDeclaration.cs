namespace Meziantou.Framework.CodeDom
{
    public class StructDeclaration : TypeDeclaration, IParametrableType, ITypeDeclarationContainer
    {
        public StructDeclaration()
            : this(null)
        {
        }

        public StructDeclaration(string name)
        {
            Name = name;
            Implements = new CodeObjectCollection<TypeReference>(this);
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Members = new CodeObjectCollection<MemberDeclaration>(this);
            Types = new CodeObjectCollection<TypeDeclaration>(this);
        }

        public CodeObjectCollection<TypeReference> Implements { get; }
        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public CodeObjectCollection<MemberDeclaration> Members { get; }
        public CodeObjectCollection<TypeDeclaration> Types { get; }
    }
}