namespace Meziantou.Framework.CodeDom;

public class ConstructorDeclaration : MemberDeclaration, IModifiers
{
    public ConstructorDeclaration()
    {
        Arguments = new MethodArgumentCollection(this);
        Statements = new StatementCollection(this);
    }

    public MethodArgumentCollection Arguments { get; }

    public StatementCollection? Statements
    {
        get;
        set => SetParent(ref field, value);
    }

    public ConstructorInitializer? Initializer
    {
        get;
        set => SetParent(ref field, value);
    }

    public Modifiers Modifiers { get; set; }

    public TypeDeclaration? ParentType => Parent.SelfOrAnscestorOfType<TypeDeclaration>();
}
