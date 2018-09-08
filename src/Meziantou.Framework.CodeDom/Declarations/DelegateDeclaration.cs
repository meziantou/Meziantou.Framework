namespace Meziantou.Framework.CodeDom
{
    public class DelegateDeclaration : TypeDeclaration, IParametrableType
    {
        private TypeReference _returnType;

        public TypeReference ReturnType
        {
            get => _returnType;
            set => SetParent(ref _returnType, value);
        }

        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public CodeObjectCollection<MethodArgumentDeclaration> Arguments { get; }

        public DelegateDeclaration()
            : this(null)
        {
        }

        public DelegateDeclaration(string name)
        {
            Arguments = new CodeObjectCollection<MethodArgumentDeclaration>(this);
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Name = name;
        }
    }
}