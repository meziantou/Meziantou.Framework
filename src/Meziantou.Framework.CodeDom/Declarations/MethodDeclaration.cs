namespace Meziantou.Framework.CodeDom
{
    public class MethodDeclaration : MemberDeclaration, IParametrableType, IModifiers
    {
        public TypeReference? ReturnType { get; set; }
        public TypeReference? PrivateImplementationType { get; set; }
        public CodeObjectCollection<TypeParameter> Parameters { get; }
        public MethodArgumentCollection Arguments { get; }
        public StatementCollection? Statements { get; set; }
        public Modifiers Modifiers { get; set; }

        public MethodDeclaration()
            : this(name: null)
        {
        }

        public MethodDeclaration(string? name)
        {
            Arguments = new MethodArgumentCollection(this);
            Parameters = new CodeObjectCollection<TypeParameter>(this);
            Name = name;
        }
    }
}
