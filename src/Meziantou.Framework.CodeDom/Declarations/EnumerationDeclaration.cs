namespace Meziantou.Framework.CodeDom
{
    public class EnumerationDeclaration : TypeDeclaration
    {
        public EnumerationDeclaration()
        {
            Members = new CodeObjectCollection<EnumerationMember>(this);
        }

        public EnumerationDeclaration(string? name)
        {
            Members = new CodeObjectCollection<EnumerationMember>(this);
            Name = name;
        }

        public TypeReference? BaseType { get; set; }
        public CodeObjectCollection<EnumerationMember> Members { get; }
    }
}
