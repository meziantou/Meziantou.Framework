namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Excludes a property or a field from the CSV serialization.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvIgnoreAttribute : CsvAttribute
{
}
