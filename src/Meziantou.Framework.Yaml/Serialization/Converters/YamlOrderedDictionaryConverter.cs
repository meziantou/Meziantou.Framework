#if NET9_0_OR_GREATER
namespace Meziantou.Framework.Yaml.Serialization.Converters;
internal sealed class YamlOrderedDictionaryConverter<TValue> : YamlConverter<OrderedDictionary<string, TValue>?>
{
    private YamlConverter? _valueConverter;

    public override OrderedDictionary<string, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadStringDictionary<OrderedDictionary<string, TValue>, TValue>(
            reader,
            ref _valueConverter,
            static options => new OrderedDictionary<string, TValue>(
                0,
                options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "ordered dictionary");

    public override void Write(YamlWriter writer, OrderedDictionary<string, TValue>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _valueConverter ??= writer.GetConverter(typeof(TValue));

        if (YamlDictionaryConverterHelper.TryWriteReference(writer, value))
        {
            return;
        }

        YamlDictionaryConverterHelper.WriteEntries(writer, value, _valueConverter);
    }
}
internal sealed class YamlOrderedDictionaryConverter<TKey, TValue> : YamlConverter<OrderedDictionary<TKey, TValue>?> where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override OrderedDictionary<TKey, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadDictionary<OrderedDictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => new OrderedDictionary<TKey, TValue>(),
            "ordered dictionary");

    public override void Write(YamlWriter writer, OrderedDictionary<TKey, TValue>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        _valueConverter ??= writer.GetConverter(typeof(TValue));

        if (YamlDictionaryConverterHelper.TryWriteReference(writer, value))
        {
            return;
        }

        YamlDictionaryConverterHelper.WriteEntries(writer, value, _valueConverter);
    }
}
#endif
