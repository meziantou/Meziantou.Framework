namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a method declaration.</summary>
/// <example>
/// <code>
/// var method = new MethodDeclaration("Calculate");
/// method.Modifiers = Modifiers.Public;
/// method.ReturnType = typeof(int);
/// method.Arguments.Add(new MethodArgumentDeclaration(typeof(int), "value"));
/// method.Statements = new ReturnStatement(new LiteralExpression(42));
/// </code>
/// </example>
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

    /// <summary>Initializes a new instance of the <see cref="MethodDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The method name.</param>
    public MethodDeclaration(string? name)
    {
        Arguments = new MethodArgumentCollection(this);
        Parameters = new CodeObjectCollection<TypeParameter>(this);
        Name = name;
    }
}
