namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a property accessor (get or set).</summary>
public class PropertyAccessorDeclaration : CodeObject
{
    public PropertyAccessorDeclaration()
        : this(statements: null)
    {
    }

    public PropertyAccessorDeclaration(StatementCollection? statements)
    {
        Statements = statements ?? [];
        CustomAttributes = new CodeObjectCollection<CustomAttribute>(this);
    }

    public Modifiers Modifiers { get; set; }
    public CodeObjectCollection<CustomAttribute> CustomAttributes { get; }

    public StatementCollection? Statements
    {
        get;
        set => SetParent(ref field, value);
    }

    public static implicit operator PropertyAccessorDeclaration(StatementCollection statements) => new(statements);

    public static implicit operator PropertyAccessorDeclaration(Statement statement) => new() { Statements = statement };
}
