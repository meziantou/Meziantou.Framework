namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Selects the constructor used to create the instances when reading CSV data.</summary>
/// <remarks>
/// Without this attribute, the serializer uses the public parameterless constructor, or the public constructor when the type has only one.
/// The parameters of the constructor are matched to the members by name, ignoring case.
/// </remarks>
[AttributeUsage(AttributeTargets.Constructor, AllowMultiple = false)]
public sealed class CsvConstructorAttribute : CsvAttribute
{
}
