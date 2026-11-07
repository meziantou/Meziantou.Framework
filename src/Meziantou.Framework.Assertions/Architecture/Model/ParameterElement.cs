namespace Meziantou.Framework.Assertions.Architecture;

/// <summary>A parameter of a method or a constructor.</summary>
public sealed class ParameterElement
{
    internal ParameterElement(MethodElement method, string name, int position, TypeReference type, IReadOnlyList<AttributeElement> attributes)
    {
        Method = method;
        Name = name;
        Position = position;
        Type = type;
        Attributes = attributes;
    }

    /// <summary>Gets the method that declares the parameter.</summary>
    public MethodElement Method { get; }

    /// <summary>Gets the name of the parameter, or an empty string when the metadata does not record it.</summary>
    public string Name { get; }

    /// <summary>Gets the 0-based position of the parameter.</summary>
    public int Position { get; }

    /// <summary>Gets the type of the parameter.</summary>
    public TypeReference Type { get; }

    /// <summary>Gets the attributes applied to the parameter.</summary>
    public IReadOnlyList<AttributeElement> Attributes { get; }

    /// <summary>Returns the type and the name of the parameter.</summary>
    /// <returns>The type and the name of the parameter.</returns>
    public override string ToString() => Type + " " + Name;
}
