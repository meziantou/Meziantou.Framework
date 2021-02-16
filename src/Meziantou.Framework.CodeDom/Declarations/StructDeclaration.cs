namespace Meziantou.Framework.CodeDom
{
    public class StructDeclaration : ClassOrStructDeclaration
    {
        public StructDeclaration()
            : this(name: null)
        {
        }

        public StructDeclaration(string? name)
        {
            Name = name;
        }
    }
}
