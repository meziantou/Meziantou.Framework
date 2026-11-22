namespace Meziantou.Framework.Csv.Serialization;

/// <summary>Sets the converter of a property or a field.</summary>
/// <param name="converterType">The type of the converter, a <see cref="CsvConverter{T}"/> or a <see cref="CsvConverterFactory"/> with a public parameterless constructor.</param>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class CsvConverterAttribute([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] Type converterType) : CsvAttribute
{
    /// <summary>Gets the type of the converter.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)]
    public Type ConverterType { get; } = converterType;
}
