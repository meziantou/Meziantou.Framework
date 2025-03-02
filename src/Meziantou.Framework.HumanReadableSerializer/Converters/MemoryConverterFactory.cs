namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class MemoryConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Memory<>);

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(MemoryConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class MemoryConverter<T> : HumanReadableConverter<Memory<T>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, Memory<T> value, HumanReadableSerializerOptions options)
        {
            HumanReadableSerializer.Serialize(writer, (ReadOnlyMemory<T>)value, typeof(ReadOnlyMemory<T>), options);
        }
    }
}
