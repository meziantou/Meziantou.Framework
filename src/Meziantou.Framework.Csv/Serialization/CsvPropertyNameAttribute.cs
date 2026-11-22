namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the name of the column of a property or a field. It takes precedence over <see cref="CsvSerializerOptions.PropertyNamingPolicy"/>.</summary>
/// <param name="name">The name of the column.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvPropertyNameAttribute(string name) : CsvAttribute
{
    /// <summary>Gets the name of the column.</summary>
    public string Name { get; } = name;
}
