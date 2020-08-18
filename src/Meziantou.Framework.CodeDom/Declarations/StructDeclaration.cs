using System.Collections.Generic;

namespace Meziantou.Framework.CodeDom
{
    public class StructDeclaration : TypeDeclaration, IParametrableType, ITypeDeclarationContainer, IMemberContainer
    {
        public StructDeclaration()
            : this(name: null)
        {
        }

        public StructDeclaration(string? name)
        {
            Name = name;
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
