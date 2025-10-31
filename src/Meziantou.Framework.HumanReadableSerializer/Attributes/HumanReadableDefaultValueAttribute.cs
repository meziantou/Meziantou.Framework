namespace Meziantou.Framework.HumanReadable;

/// <summary>Specifies the default value of a property or field used for comparison when serializing.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadableDefaultValueAttribute : HumanReadableAttribute
{
    /// <summary>Gets the default value used for comparison when determining whether to serialize a property or field based on the DefaultIgnoreCondition setting.</summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HumanReadableDefaultValueAttribute"/>.
    /// </summary>
    public HumanReadableDefaultValueAttribute(object? defaultValue)
    {
        DefaultValue = defaultValue;
    }
}
