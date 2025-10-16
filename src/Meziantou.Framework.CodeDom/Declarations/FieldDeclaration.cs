namespace Meziantou.Framework.CodeDom;

public class FieldDeclaration : MemberDeclaration, IModifiers
{
    public FieldDeclaration()
        : this(name: null, type: null)
    {
    }

    public FieldDeclaration(string? name, TypeReference? type)
        : this(name, type, Modifiers.None)
    {
    }

    public FieldDeclaration(string? name, TypeReference? type, Modifiers modifiers)
        : this(name, type, modifiers, initExpression: null)
    {
    }

    public FieldDeclaration(string? name, TypeReference? type, Modifiers modifiers, Expression? initExpression)
        : base(name)
    {
        Modifiers = modifiers;
        Type = type;
        InitExpression = initExpression;
    }

    public Expression? InitExpression
    {
        get;
        set => SetParent(ref field, value);
    }

    public TypeReference? Type { get; set; }

    public Modifiers Modifiers { get; set; }
}
