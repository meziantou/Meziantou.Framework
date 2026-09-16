using System.Collections.Frozen;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts <see cref="FrozenDictionary{TKey, TValue}"/> with <see cref="string"/> keys: the entries are read like a
/// <see cref="Dictionary{TKey, TValue}"/>, which is then frozen with the same key comparer.
/// </summary>
internal sealed class YamlFrozenDictionaryConverter<TValue> : YamlConverter<FrozenDictionary<string, TValue>?>
{
    private YamlConverter? _valueConverter;

    public override FrozenDictionary<string, TValue>? Read(YamlReader reader)
    {
        var dictionary = YamlDictionaryConverterHelper.ReadStringDictionary<Dictionary<string, TValue>, TValue>(
            reader,
            ref _valueConverter,
            static options => new Dictionary<string, TValue>(options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal),
            "dictionary");
        return dictionary?.ToFrozenDictionary(dictionary.Comparer);
    }

    public override void Write(YamlWriter writer, FrozenDictionary<string, TValue>? value)
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

/// <summary>Converts <see cref="FrozenDictionary{TKey, TValue}"/> with a non-string key.</summary>
internal sealed class YamlFrozenDictionaryConverter<TKey, TValue> : YamlConverter<FrozenDictionary<TKey, TValue>?> where TKey : notnull
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override FrozenDictionary<TKey, TValue>? Read(YamlReader reader)
    {
        var dictionary = YamlDictionaryConverterHelper.ReadDictionary<Dictionary<TKey, TValue>, TKey, TValue>(
            reader,
            ref _keyConverter,
            ref _valueConverter,
            static () => new Dictionary<TKey, TValue>(),
            "dictionary");
        return dictionary?.ToFrozenDictionary(dictionary.Comparer);
    }

    public override void Write(YamlWriter writer, FrozenDictionary<TKey, TValue>? value)
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
