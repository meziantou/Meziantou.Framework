namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts <see cref="IReadOnlyDictionary{TKey, TValue}"/> with <see cref="string"/> keys like
/// <see cref="Dictionary{TKey, TValue}"/>: keys are compared using <see cref="YamlSerializerOptions.PropertyNameCaseInsensitive"/>,
/// merge keys are applied, and a <c>null</c> key is the "null" string.
/// </summary>
internal sealed class YamlIReadOnlyDictionaryConverter<TValue> : YamlConverter<IReadOnlyDictionary<string, TValue>?>
{
    private YamlConverter? _valueConverter;

    public override IReadOnlyDictionary<string, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadStringDictionary<Dictionary<string, TValue>, TValue>(
            reader,
            ref _valueConverter,
            static options => new Dictionary<string, TValue>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "dictionary");

    public override void Write(YamlWriter writer, IReadOnlyDictionary<string, TValue>? value)
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

internal sealed class YamlIReadOnlyDictionaryConverter<TKey, TValue> : YamlConverter<IReadOnlyDictionary<TKey, TValue>?> where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override IReadOnlyDictionary<TKey, TValue>? Read(YamlReader reader)
        => YamlDictionaryConverterHelper.ReadDictionary<Dictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => new Dictionary<TKey, TValue>(),
            "dictionary");

    public override void Write(YamlWriter writer, IReadOnlyDictionary<TKey, TValue>? value)
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
