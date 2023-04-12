namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Class | AttributeTargets.Interface, AllowMultiple = false)]
public sealed class HumanReadableConverterAttribute : HumanReadableAttribute
{
    public HumanReadableConverterAttribute(Type converterType) => ConverterType = converterType;

    public Type ConverterType { get; }

    internal void EnsureTypeIsValid()
    {
        if (!typeof(HumanReadableConverter).IsAssignableFrom(ConverterType))
            throw new HumanReadableSerializerException($"The converter '{ConverterType}' must inherit from '{typeof(HumanReadableConverter).AssemblyQualifiedName}'");
    }
}
