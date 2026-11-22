namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Declares a type for which a <see cref="CsvSerializerContext"/> generates the serialization metadata.</summary>
/// <param name="type">The type of the records to serialize.</param>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class CsvSerializableAttribute(Type type) : CsvAttribute
{
    /// <summary>Gets the type of the records to serialize.</summary>
    public Type Type { get; } = type;

    /// <summary>Gets or sets the name of the generated property that returns the metadata of the type. Default is the name of the type.</summary>
    public string? TypeInfoPropertyName { get; set; }
}
