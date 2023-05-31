namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class ReadOnlyMemoryConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(ReadOnlyMemory<>);

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        if (typeToConvert == typeof(ReadOnlyMemory<char>))
            return new ReadOnlyMemoryCharConverter();

        if (typeToConvert == typeof(ReadOnlyMemory<byte>))
            return new ReadOnlyMemoryByteConverter();

        return (HumanReadableConverter?)Activator.CreateInstance(typeof(ReadOnlyMemoryConverter<>).MakeGenericType(typeToConvert.GetGenericArguments()[0]));
    }

    private sealed class ReadOnlyMemoryConverter<T> : HumanReadableConverter<ReadOnlyMemory<T>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, ReadOnlyMemory<T> value, HumanReadableSerializerOptions options)
        {
            if (value.IsEmpty)
            {
                writer.WriteEmptyArray();
            }
            else
            {
                writer.StartArray();
                foreach (var item in value.Span)
                {
                    writer.StartArrayItem();
                    HumanReadableSerializer.Serialize(writer, item, typeof(T), options);
                    writer.EndArrayItem();
                }
                writer.EndArray();
            }
        }
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class ReadOnlyMemoryByteConverter : HumanReadableConverter<ReadOnlyMemory<byte>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, ReadOnlyMemory<byte> value, HumanReadableSerializerOptions options)
        {
#if NET6_0_OR_GREATER
            writer.WriteValue(Convert.ToBase64String(value.Span));
#else
            writer.WriteValue(Convert.ToBase64String(value.Span.ToArray()));
#endif
        }
    }

    private sealed class ReadOnlyMemoryCharConverter : HumanReadableConverter<ReadOnlyMemory<char>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, ReadOnlyMemory<char> value, HumanReadableSerializerOptions options)
        {
            writer.WriteValue(value.Span);
        }
    }
}
