namespace Meziantou.Framework.Diagrams;

/// <summary>An attribute of an entity: a row of its table, with its type, its name, its keys and its comment.</summary>
public sealed class ErAttribute
{
    /// <summary>Initializes a new instance of the <see cref="ErAttribute"/> class.</summary>
    /// <param name="type">The type, such as <c>string</c> or <c>int</c>. A type that ends with <c>?</c> is optional.</param>
    /// <param name="name">The name.</param>
    public ErAttribute(string type, string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(type);
        ArgumentException.ThrowIfNullOrEmpty(name);
        Type = type;
        Name = name;
    }

    /// <summary>Gets or sets the type, such as <c>string</c> or <c>int</c>. The generic types are written <c>List~int~</c> and shown <c>List&lt;int&gt;</c>.</summary>
    public string Type { get; set; }

    /// <summary>Gets or sets the name.</summary>
    public string Name { get; set; }

    /// <summary>Gets or sets the keys the attribute is part of.</summary>
    public ErAttributeKeys Keys { get; set; }

    /// <summary>Gets or sets the comment, or <see langword="null"/> for none.</summary>
    public string? Comment { get; set; }
}
