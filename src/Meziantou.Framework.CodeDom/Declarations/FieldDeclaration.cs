namespace Meziantou.Framework.CodeDom;

public class FieldDeclaration : MemberDeclaration, IModifiers
{
    private Expression? _initExpression;

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
        get => _initExpression;
        set => SetParent(ref _initExpression, value);
    }

    public TypeReference? Type { get; set; }

    public Modifiers Modifiers { get; set; }
}
