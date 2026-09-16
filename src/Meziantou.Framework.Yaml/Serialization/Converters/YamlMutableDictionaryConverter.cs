namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts a dictionary type created with its parameterless constructor, such as <see cref="SortedDictionary{TKey, TValue}"/>
/// or <see cref="System.Collections.Concurrent.ConcurrentDictionary{TKey, TValue}"/>.
/// </summary>
internal sealed class YamlMutableDictionaryConverter<TDictionary, TKey, TValue> : YamlConverter<TDictionary?>
    where TDictionary : class, IDictionary<TKey, TValue>, new()
    where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(TDictionary);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not TDictionary dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(TDictionary)}'.");
        }

        return YamlDictionaryConverterHelper.ReadDictionary<TDictionary, TKey, TValue>(reader, ref _keyConverter, ref _valueConverter, static () => new TDictionary(), "dictionary", dictionary);
    }

    public override TDictionary? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadDictionary<TDictionary, TKey, TValue>(reader, ref _keyConverter, ref _valueConverter, static () => new TDictionary(), "dictionary");

    public override void Write(YamlWriter writer, TDictionary? value)
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

        YamlDictionaryConverterHelper.WriteEntries(writer, (IEnumerable<KeyValuePair<TKey, TValue>>)value, _valueConverter);
    }
}
