namespace Meziantou.Framework.HumanReadable;

/// <summary>Indicates that a property or field should be included during serialization even if it would normally be ignored.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadableIncludeAttribute : HumanReadableAttribute
{
}
