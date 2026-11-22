namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Includes a non-public property, or a field, in the CSV serialization.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvIncludeAttribute : CsvAttribute
{
}
