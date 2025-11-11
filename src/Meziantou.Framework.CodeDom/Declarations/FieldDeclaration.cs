namespace Meziantou.Framework.CodeDom;

/// <summary>Represents a field declaration.</summary>
/// <example>
/// <code>
/// var field = new FieldDeclaration("_count", typeof(int), Modifiers.Private, new LiteralExpression(0));
/// </code>
/// </example>
public class FieldDeclaration : MemberDeclaration, IModifiers
{
    public FieldDeclaration()
        : this(name: null, type: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FieldDeclaration"/> class with the specified name and type.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="type">The field type.</param>
    public FieldDeclaration(string? name, TypeReference? type)
        : this(name, type, Modifiers.None)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FieldDeclaration"/> class with the specified name, type, and modifiers.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="type">The field type.</param>
    /// <param name="modifiers">The field modifiers.</param>
    public FieldDeclaration(string? name, TypeReference? type, Modifiers modifiers)
        : this(name, type, modifiers, initExpression: null)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="FieldDeclaration"/> class with the specified name, type, modifiers, and initialization expression.</summary>
    /// <param name="name">The field name.</param>
    /// <param name="type">The field type.</param>
    /// <param name="modifiers">The field modifiers.</param>
    /// <param name="initExpression">The field initialization expression.</param>
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
