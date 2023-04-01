namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class AsyncEnumerableConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    private static Type? GetValueType(Type type)
    {
        foreach (var iface in type.GetInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
                continue;

            return iface.GenericTypeArguments[0];
        }

        return null;
    }

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        return (HumanReadableConverter)Activator.CreateInstance(typeof(AsyncEnumerableConverter<>).MakeGenericType(GetValueType(typeToConvert)));
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class AsyncEnumerableConverter<T> : HumanReadableConverter<IAsyncEnumerable<T>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<T> value, HumanReadableSerializerOptions options)
        {
            var task = WriteValue(writer, value, options);
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
            }
            else
            {
                task.AsTask().Wait();
            }

            static async ValueTask WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<T> value, HumanReadableSerializerOptions options)
            {
                var hasItem = false;
                await foreach (var item in value.ConfigureAwait(false))
                {
                    if (!hasItem)
                    {
                        writer.StartArray();
                        hasItem = true;
                    }

                    writer.StartArrayItem();
                    HumanReadableSerializer.Serialize(writer, item, options);
                    writer.EndArrayItem();
                }

                if (hasItem)
                {
                    writer.EndArray();
                }
                else
                {
                    writer.WriteEmptyArray();
                }
            }
        }
    }
}
