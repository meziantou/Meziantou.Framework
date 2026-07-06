namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDictionaryConverter<TValue> : YamlConverter<Dictionary<string, TValue>?>
{
    private YamlConverter? _valueConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(Dictionary<string, TValue>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not Dictionary<string, TValue> dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(Dictionary<string, TValue>)}'.");
        }

        return PopulateDictionary(reader, dictionary);
    }

    public override Dictionary<string, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadStringDictionary<Dictionary<string, TValue>, TValue>(
            reader,
            ref _valueConverter,
            static options => new Dictionary<string, TValue>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "dictionary");

    public override void Write(YamlWriter writer, Dictionary<string, TValue>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        WriteEntries(writer, value, value);
    }

    internal void WriteEntries(YamlWriter writer, object referenceValue, IEnumerable<KeyValuePair<string, TValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(referenceValue);
        ArgumentNullException.ThrowIfNull(entries);

        _valueConverter ??= writer.GetConverter(typeof(TValue));
        if (YamlDictionaryConverterHelper.TryWriteReference(writer, referenceValue))
        {
            return;
        }

        YamlDictionaryConverterHelper.WriteEntries(writer, entries, _valueConverter);
    }

    private Dictionary<string, TValue>? PopulateDictionary(YamlReader reader, Dictionary<string, TValue> dictionary)
        => YamlDictionaryConverterHelper.ReadStringDictionary<Dictionary<string, TValue>, TValue>(
            reader,
            ref _valueConverter,
            static options => new Dictionary<string, TValue>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "dictionary",
            dictionary);
}

internal sealed class YamlDictionaryConverter<TKey, TValue> : YamlConverter<Dictionary<TKey, TValue>?> where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(Dictionary<TKey, TValue>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not Dictionary<TKey, TValue> dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(Dictionary<TKey, TValue>)}'.");
        }

        return PopulateDictionary(reader, dictionary);
    }

    public override Dictionary<TKey, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadDictionary<Dictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => new Dictionary<TKey, TValue>(),
            "dictionary");

    public override void Write(YamlWriter writer, Dictionary<TKey, TValue>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        WriteEntries(writer, value, value);
    }

    internal void WriteEntries(YamlWriter writer, object referenceValue, IEnumerable<KeyValuePair<TKey, TValue>> entries)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(referenceValue);
        ArgumentNullException.ThrowIfNull(entries);

        _valueConverter ??= writer.GetConverter(typeof(TValue));
        if (YamlDictionaryConverterHelper.TryWriteReference(writer, referenceValue))
        {
            return;
        }

        YamlDictionaryConverterHelper.WriteEntries(writer, entries, _valueConverter);
    }

    private Dictionary<TKey, TValue>? PopulateDictionary(YamlReader reader, Dictionary<TKey, TValue> dictionary)
        => YamlDictionaryConverterHelper.ReadDictionary<Dictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => new Dictionary<TKey, TValue>(),
            "dictionary",
            dictionary);
}
