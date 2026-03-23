namespace Meziantou.Framework.Json.Internals;

/// <summary>Name selector: selects a member of an object by name.</summary>
internal sealed class NameSelector : Selector
{
    public NameSelector(string name)
    {
        Name = name;
    }

    public override SelectorKind Kind => SelectorKind.Name;

    public string Name { get; }
}
