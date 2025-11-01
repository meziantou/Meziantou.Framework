namespace Meziantou.Framework.Annotations;

/// <summary>
/// Specifies that a fast ToString extension method should be generated for the specified enum type.
/// </summary>
[System.Diagnostics.Conditional("FastEnumToString_Attributes")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FastEnumToStringAttribute : Attribute
{
    /// <summary>
    /// Initializes a new instance of the <see cref="FastEnumToStringAttribute"/> class.
    /// </summary>
    /// <param name="enumType">The enum type for which to generate a fast ToString extension method.</param>
    public FastEnumToStringAttribute(Type enumType)
    {
        EnumType = enumType;
    }

    /// <summary>
    /// Gets or sets a value indicating whether the generated extension method should be public.
    /// </summary>
    public bool IsPublic { get; set; } = true;

    /// <summary>
    /// Gets or sets the namespace where the extension method will be generated.
    /// </summary>
    public string? ExtensionMethodNamespace { get; set; }

    /// <summary>
    /// Gets the enum type for which to generate a fast ToString extension method.
    /// </summary>
    public Type EnumType { get; }
}
