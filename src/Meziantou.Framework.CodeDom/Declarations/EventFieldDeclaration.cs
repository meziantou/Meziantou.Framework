namespace Meziantou.Framework.CodeDom;

public class EventFieldDeclaration : MemberDeclaration, IModifiers
{
    private StatementCollection? _removeAccessor;
    private StatementCollection? _addAccessor;

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
        get => _addAccessor;
        set => SetParent(ref _addAccessor, value);
    }

    public StatementCollection? RemoveAccessor
    {
        get => _removeAccessor;
        set => SetParent(ref _removeAccessor, value);
    }

    public TypeReference? PrivateImplementationType { get; set; }

    public Modifiers Modifiers { get; set; }
}
