namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlIDictionaryConverter<TKey, TValue> : YamlConverter<IDictionary<TKey, TValue>?> where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(IDictionary<TKey, TValue>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not IDictionary<TKey, TValue> dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must implement '{typeof(IDictionary<TKey, TValue>)}'.");
        }

        return YamlDictionaryConverterHelper.ReadDictionary<IDictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => (IDictionary<TKey, TValue>)new Dictionary<TKey, TValue>(),
            "dictionary",
            dictionary);
    }

    public override IDictionary<TKey, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadDictionary<IDictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => (IDictionary<TKey, TValue>)new Dictionary<TKey, TValue>(),
            "dictionary");

    public override void Write(YamlWriter writer, IDictionary<TKey, TValue>? value)
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
