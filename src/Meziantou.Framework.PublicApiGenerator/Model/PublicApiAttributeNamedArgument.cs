namespace Meziantou.Framework.PublicApiGenerator;

/// <summary>A value assigned to a field or a property of a custom attribute.</summary>
public sealed class PublicApiAttributeNamedArgument
{
    internal PublicApiAttributeNamedArgument(string name, PublicApiAttributeNamedArgumentKind kind, PublicApiAttributeArgument value)
    {
        Name = name;
        Kind = kind;
        Value = value;
    }

    public string Name { get; }

    public PublicApiAttributeNamedArgumentKind Kind { get; }

    public PublicApiAttributeArgument Value { get; }

    public override string ToString() => Name + " = " + Value;
}
