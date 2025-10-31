namespace Meziantou.Framework.HumanReadable;

/// <summary>Specifies the name to use when serializing a property or field.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadablePropertyNameAttribute : HumanReadableAttribute
{
    /// <summary>Initializes a new instance of the <see cref="HumanReadablePropertyNameAttribute"/> class.</summary>
    /// <param name="name">The name to use when serializing.</param>
    public HumanReadablePropertyNameAttribute(string name) => Name = name;

    /// <summary>Gets the name to use when serializing.</summary>
    public string Name { get; }
}
