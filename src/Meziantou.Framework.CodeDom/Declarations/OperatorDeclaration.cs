namespace Meziantou.Framework.CodeDom;

public class OperatorDeclaration : MemberDeclaration, IModifiers
{
    public TypeReference? ReturnType { get; set; }

    public MethodArgumentCollection Arguments { get; }
    public StatementCollection Statements { get; }
    public Modifiers Modifiers { get; set; }

    public OperatorDeclaration()
        : this(name: null)
    {
    }

    public OperatorDeclaration(string? name)
    {
        Statements = new StatementCollection(this);
        Arguments = new MethodArgumentCollection(this);
        Name = name;
    }
}
