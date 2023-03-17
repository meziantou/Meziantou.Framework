namespace Meziantou.Framework.HumanReadable;

[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false)]
public sealed class HumanReadableConverterAttribute : HumanReadableAttribute
{
    public HumanReadableConverterAttribute(Type converterType) => ConverterType = converterType;

    public Type ConverterType { get; }
}
