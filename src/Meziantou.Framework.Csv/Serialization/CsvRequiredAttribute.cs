namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Requires the column of a property or a field: reading data without this column, or a record without a value for it, throws a <see cref="CsvException"/>.</summary>
/// <remarks>The members declared with the C# <see langword="required"/> keyword are required too.</remarks>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvRequiredAttribute : CsvAttribute
{
}
