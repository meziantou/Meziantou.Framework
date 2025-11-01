namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a delegate declaration.</summary>
/// <example>
/// <code>
/// var del = new DelegateDeclaration("MyDelegate");
/// del.ReturnType = typeof(void);
/// del.Arguments.Add(new MethodArgumentDeclaration(typeof(string), "message"));
/// </code>
/// </example>
public class DelegateDeclaration : TypeDeclaration, IParametrableType
{
    public TypeReference? ReturnType { get; set; }
    public CodeObjectCollection<TypeParameter> Parameters { get; }
    public MethodArgumentCollection Arguments { get; }

    public DelegateDeclaration()
        : this(name: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="DelegateDeclaration"/> class with the specified name.</summary>
    /// <param name="name">The delegate name.</param>
    public DelegateDeclaration(string? name)
    {
        Arguments = new MethodArgumentCollection(this);
        Parameters = new CodeObjectCollection<TypeParameter>(this);
        Name = name;
    }
}
