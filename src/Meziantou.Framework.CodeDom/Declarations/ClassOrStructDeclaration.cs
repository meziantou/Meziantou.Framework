namespace Meziantou.Framework.CodeDom
{
    public abstract class ClassOrStructDeclaration : TypeDeclaration, IParametrableType, ITypeDeclarationContainer, IMemberContainer
    {
        protected ClassOrStructDeclaration()
        {
            Implements = new List<TypeReference>();
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Members = new CodeObjectCollection<MemberDeclaration>(this);
            Types = new CodeObjectCollection<TypeDeclaration>(this);
        }

        public IList<TypeReference> Implements { get; }
        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public CodeObjectCollection<MemberDeclaration> Members { get; }
        public CodeObjectCollection<TypeDeclaration> Types { get; }
    }
}
