namespace Meziantou.Framework.CodeDom;

/// <summary>Represents an event field declaration.</summary>
public class EventFieldDeclaration : MemberDeclaration, IModifiers
{
    public EventFieldDeclaration()
      : this(name: null, type: null)
    {
    }

    public EventFieldDeclaration(string? name, TypeReference? type)
        : this(name, type, Modifiers.None)
    {
    }

    public EventFieldDeclaration(string? name, TypeReference? type, Modifiers modifiers)
        : base(name)
    {
        Modifiers = modifiers;
        Type = type;
    }

    public TypeReference? Type { get; set; }

    public StatementCollection? AddAccessor
    {
        get;
        set => SetParent(ref field, value);
    }

    public StatementCollection? RemoveAccessor
    {
        get;
        set => SetParent(ref field, value);
    }

    public TypeReference? PrivateImplementationType { get; set; }

    public Modifiers Modifiers { get; set; }
}
