namespace Meziantou.Framework.HumanReadable;

/// <summary>Specifies the converter to use when serializing a type, property, or field.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HumanReadableConverterAttribute : HumanReadableAttribute
{
    /// <summary>Initializes a new instance of the <see cref="HumanReadableConverterAttribute"/> class with a converter instance.</summary>
    /// <param name="converterInstance">The converter instance to use.</param>
    public HumanReadableConverterAttribute(HumanReadableConverter converterInstance)
    {
        ArgumentNullException.ThrowIfNull(converterInstance);

        ConverterType = converterInstance.GetType();
        ConverterInstance = converterInstance;
    }

    /// <summary>Initializes a new instance of the <see cref="HumanReadableConverterAttribute"/> class with a converter type.</summary>
    /// <param name="converterType">The type of the converter to use.</param>
    public HumanReadableConverterAttribute(Type converterType) => ConverterType = converterType;

    /// <summary>Gets the type of the converter.</summary>
    public Type ConverterType { get; }

    /// <summary>Gets the converter instance.</summary>
    public HumanReadableConverter ConverterInstance { get; }

    internal void EnsureTypeIsValid()
    {
        if (!typeof(HumanReadableConverter).IsAssignableFrom(ConverterType))
            throw new HumanReadableSerializerException($"The converter '{ConverterType}' must inherit from '{typeof(HumanReadableConverter).AssemblyQualifiedName}'");
    }
}
