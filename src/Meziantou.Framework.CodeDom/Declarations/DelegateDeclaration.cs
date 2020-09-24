namespace Meziantou.Framework.CodeDom
{
    public class DelegateDeclaration : TypeDeclaration, IParametrableType
    {
        public TypeReference? ReturnType { get; set; }
        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }

        public DelegateDeclaration()
            : this(name: null)
        {
        }

        public DelegateDeclaration(string? name)
        {
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Name = name;
        }
    }
}
