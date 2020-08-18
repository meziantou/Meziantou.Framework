namespace Meziantou.Framework.CodeDom
{
    public class EnumerationDeclaration : TypeDeclaration
    {
        private TypeReference? _baseType;

        public EnumerationDeclaration()
        {
            Members = new CodeObjectCollection<EnumerationMember>(this);
        }

        public EnumerationDeclaration(string? name)
        {
            Members = new CodeObjectCollection<EnumerationMember>(this);
            Name = name;
        }

        public TypeReference? BaseType
        {
            get => _baseType;
            set => _baseType = value;
        }

        public CodeObjectCollection<EnumerationMember> Members { get; }
    }
}
