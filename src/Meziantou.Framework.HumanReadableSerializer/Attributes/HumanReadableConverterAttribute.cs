namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HumanReadableConverterAttribute : HumanReadableAttribute
{
    public HumanReadableConverterAttribute(HumanReadableConverter converterInstance)
    {
        if (converterInstance is null)
            throw new ArgumentNullException(nameof(converterInstance));

        ConverterType = converterInstance.GetType();
        ConverterInstance = converterInstance;
    }

    public HumanReadableConverterAttribute(Type converterType) => ConverterType = converterType;

    public Type ConverterType { get; }
    public HumanReadableConverter ConverterInstance { get; }

    internal void EnsureTypeIsValid()
    {
        if (!typeof(HumanReadableConverter).IsAssignableFrom(ConverterType))
            throw new HumanReadableSerializerException($"The converter '{ConverterType}' must inherit from '{typeof(HumanReadableConverter).AssemblyQualifiedName}'");
    }
}
