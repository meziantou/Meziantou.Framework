namespace Meziantou.Framework.Annotations;

/// <summary>
/// Indicates that a source generator should create a memory-optimized <c>ToStringFast</c> extension method for the specified enum type.
/// </summary>
/// <example>
/// <code>
/// // Generate a ToStringFast extension method for the Color enum
/// [assembly: FastEnumToString(typeof(MyApp.Color))]
/// 
/// namespace MyApp
/// {
///     public enum Color
///     {
///         Red,
///         Green,
///         Blue
///     }
/// }
/// 
/// // Usage
/// Color color = Color.Red;
/// string colorName = color.ToStringFast(); // Faster than color.ToString()
/// 
/// // With custom namespace and visibility
/// [assembly: FastEnumToString(typeof(MyApp.Status), 
///     IsPublic = true, 
///     ExtensionMethodNamespace = "MyApp.Extensions")]
/// </code>
/// </example>
/// <remarks>
/// <para>
/// This attribute triggers a source generator that creates optimized <c>ToStringFast</c> extension methods
/// for enum types. The generated method uses a switch expression with <c>nameof</c> to avoid boxing
/// and allocations, providing significant performance improvements over the built-in <c>ToString()</c> method.
/// </para>
/// <para>
/// The generated method falls back to <c>ToString()</c> for undefined enum values, ensuring correct behavior
/// for all possible values while maintaining optimal performance for defined enum members.
/// </para>
/// <para>
/// This attribute is marked with <c>Conditional("FastEnumToString_Attributes")</c>, which means it only
/// appears in assemblies when the <c>FastEnumToString_Attributes</c> compilation symbol is defined.
/// </para>
/// <para>
/// For more information, see <see href="https://www.meziantou.net/caching-enum-tostring-to-improve-performance.htm">Caching Enum.ToString to improve performance</see>.
/// </para>
/// </remarks>
[System.Diagnostics.Conditional("FastEnumToString_Attributes")]
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class FastEnumToStringAttribute : Attribute
{
    /// <summary>Initializes a new instance of the <see cref="FastEnumToStringAttribute"/> class for the specified enum type.</summary>
    /// <param name="enumType">The enum type for which to generate a <c>ToStringFast</c> extension method.</param>
    public FastEnumToStringAttribute(Type enumType)
    {
        EnumType = enumType;
    }

    /// <summary>Gets or sets a value indicating whether the generated extension method should be public.</summary>
    /// <value>
    /// <see langword="true"/> to generate a public extension method; <see langword="false"/> to generate an internal method.
    /// The default is <see langword="true"/>.
    /// </value>
    /// <remarks>
    /// The visibility is also influenced by the enum type's visibility. If the enum is internal, the generated
    /// extension method will be internal regardless of this property's value.
    /// </remarks>
    public bool IsPublic { get; set; } = true;

    /// <summary>Gets or sets the namespace for the generated extension method class.</summary>
    /// <value>
    /// The namespace where the <c>FastEnumToStringExtensions</c> class will be generated.
    /// If <see langword="null"/> or empty, the namespace of the enum type is used. The default is <see langword="null"/>.
    /// </value>
    /// <example>
    /// <code>
    /// // Generate extension method in a custom namespace
    /// [assembly: FastEnumToString(typeof(MyApp.Color), 
    ///     ExtensionMethodNamespace = "MyApp.Extensions")]
    /// 
    /// // Later, you can use:
    /// using MyApp.Extensions;
    /// var colorName = Color.Red.ToStringFast();
    /// </code>
    /// </example>
    public string? ExtensionMethodNamespace { get; set; }

    /// <summary>Gets the enum type for which to generate a <c>ToStringFast</c> extension method.</summary>
    public Type EnumType { get; }
}
