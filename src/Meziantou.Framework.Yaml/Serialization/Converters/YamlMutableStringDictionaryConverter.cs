namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts a dictionary type with <see cref="string"/> keys created with its parameterless constructor, such as
/// <see cref="SortedDictionary{TKey, TValue}"/>. Merge keys are applied like for <see cref="Dictionary{TKey, TValue}"/>.
/// </summary>
internal sealed class YamlMutableStringDictionaryConverter<TDictionary, TValue> : YamlConverter<TDictionary?>
    where TDictionary : class, IDictionary<string, TValue>, new()
{
    private YamlConverter? _valueConverter;

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(TDictionary);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not TDictionary dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(TDictionary)}'.");
        }

        return YamlDictionaryConverterHelper.ReadStringDictionary<TDictionary, TValue>(reader, ref _valueConverter, static _ => new TDictionary(), "dictionary", dictionary);
    }

    public override TDictionary? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadStringDictionary<TDictionary, TValue>(reader, ref _valueConverter, static _ => new TDictionary(), "dictionary");

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

        YamlDictionaryConverterHelper.WriteEntries(writer, (IEnumerable<KeyValuePair<string, TValue>>)value, _valueConverter);
    }
}
