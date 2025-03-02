namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadableDefaultValueAttribute : HumanReadableAttribute
{
    /// <summary>
    /// Specifies the condition that must be met before a property or field will be ignored.
    /// </summary>
    public object? DefaultValue { get; }

    /// <summary>
    /// Initializes a new instance of <see cref="HumanReadableDefaultValueAttribute"/>.
    /// </summary>
    public HumanReadableDefaultValueAttribute(object? defaultValue)
    {
        DefaultValue = defaultValue;
    }
}
