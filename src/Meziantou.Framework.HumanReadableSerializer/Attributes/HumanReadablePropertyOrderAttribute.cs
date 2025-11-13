namespace Meziantou.Framework.HumanReadable;

/// <summary>
/// Specifies the property order that is present in the output when serializing. Lower values are serialized first.
/// If the attribute is not specified, the default value is 0.
/// </summary>
/// <remarks>If multiple properties have the same value, the ordering is undefined between them.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadablePropertyOrderAttribute : HumanReadableAttribute
{
    /// <summary>
    /// Initializes a new instance of <see cref="HumanReadablePropertyOrderAttribute"/> with the specified order.
    /// </summary>
    /// <param name="order">The order of the property.</param>
    public HumanReadablePropertyOrderAttribute(int order)
    {
        Order = order;
    }

    /// <summary>The serialization order of the property.</summary>
    public int Order { get; }
}
