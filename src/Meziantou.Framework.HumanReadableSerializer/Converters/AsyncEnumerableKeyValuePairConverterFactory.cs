using System.Diagnostics;
using System.Runtime.ConstrainedExecution;
using Meziantou.Framework.HumanReadable.Utils;

namespace Meziantou.Framework.HumanReadable.Converters;

internal sealed class AsyncEnumerableKeyValuePairConverterFactory : HumanReadableConverterFactory
{
    public override bool CanConvert(Type type) => GetValueType(type) != null;

    public override HumanReadableConverter? CreateConverter(Type typeToConvert, HumanReadableSerializerOptions options)
    {
        var type = GetValueType(typeToConvert)!;
        return (HumanReadableConverter?)Activator.CreateInstance(typeof(AsyncEnumerableKeyValuePairConverter<>).MakeGenericType(type));
    }

    private static Type? GetValueType(Type type)
    {
        // IEnumerable<KeyValuePair<string, T>>
        foreach (var iface in type.GetAllInterfaces())
        {
            if (!iface.IsGenericType)
                continue;

            if (iface.GetGenericTypeDefinition() != typeof(IAsyncEnumerable<>))
                continue;

            var enumerableType = iface.GenericTypeArguments[0];
            if (!enumerableType.IsGenericType)
                continue;

            if (enumerableType.GetGenericTypeDefinition() != typeof(KeyValuePair<,>))
                continue;

            var keyType = enumerableType.GenericTypeArguments[0];
            if (keyType == typeof(string))
                return enumerableType.GenericTypeArguments[1];
        }

        return null;
    }

    [SuppressMessage("Performance", "CA1812", Justification = "The class is instantiated using Activator.CreateInstance")]
    private sealed class AsyncEnumerableKeyValuePairConverter<T> : HumanReadableConverter<IAsyncEnumerable<KeyValuePair<string, T>>>
    {
        protected override void WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<KeyValuePair<string, T>>? value, HumanReadableSerializerOptions options)
        {
            Debug.Assert(value != null);

            var task = WriteValue(writer, value, options);
            if (task.IsCompleted)
            {
                task.GetAwaiter().GetResult();
            }
            else
            {
                task.AsTask().Wait();
            }

            static async ValueTask WriteValue(HumanReadableTextWriter writer, IAsyncEnumerable<KeyValuePair<string, T>> value, HumanReadableSerializerOptions options)
            {
                if (options.DictionaryKeyOrder != null)
                {
                    var list = await ToListAsync(value).ConfigureAwait(false);
                    HumanReadableSerializer.Serialize(writer, list, options);
                }
                else
                {
                    var hasItem = false;
                    await foreach (var prop in value.ConfigureAwait(false))
                    {
                        if (!hasItem)
                        {
                            writer.StartObject();
                            hasItem = true;
                        }

                        writer.WritePropertyName(prop.Key);
                        HumanReadableSerializer.Serialize(writer, prop.Value, options);
                    }

                    if (hasItem)
                    {
                        writer.EndObject();
                    }
                    else
                    {
                        writer.WriteEmptyObject();
                    }
                }
            }

            static async Task<List<KeyValuePair<string, T>>> ToListAsync(IAsyncEnumerable<KeyValuePair<string, T>> asyncEnumerable)
            {
                var result = new List<KeyValuePair<string, T>>();
                await foreach (var item in asyncEnumerable.ConfigureAwait(false))
                {
                    result.Add(item);
                }

                return result;
            }
        }
    }
}
