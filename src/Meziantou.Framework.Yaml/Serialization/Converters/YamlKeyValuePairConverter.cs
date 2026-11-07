namespace Meziantou.Framework.Yaml.Serialization.Converters;

/// <summary>
/// Converts <see cref="KeyValuePair{TKey, TValue}"/> as a mapping with a <c>Key</c> and a <c>Value</c> entry, whose
/// names follow <see cref="YamlSerializerOptions.PropertyNamingPolicy"/>.
/// </summary>
/// <remarks>
/// A missing entry keeps its default value and an unknown entry is skipped. The source generator emits the same shape.
/// </remarks>
internal sealed class YamlKeyValuePairConverter<TKey, TValue> : YamlConverter<KeyValuePair<TKey, TValue>>
{
    private YamlConverter? _keyConverter;
    private YamlConverter? _valueConverter;

    public override KeyValuePair<TKey, TValue> Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (KeyValuePair<TKey, TValue>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return default;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        _keyConverter ??= reader.GetConverter(typeof(TKey));
        _valueConverter ??= reader.GetConverter(typeof(TValue));
        var anchor = reader.Anchor;
        var comparison = reader.Options.PropertyNameCaseInsensitive ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var keyName = reader.ConvertName("Key");
        var valueName = reader.ConvertName("Value");
        var key = default(TKey);
        var value = default(TValue);
        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var name = reader.ScalarValue ?? string.Empty;
            reader.Read();
            if (string.Equals(name, keyName, comparison))
            {
                key = (TKey)_keyConverter.Read(reader, typeof(TKey))!;
            }
            else if (string.Equals(name, valueName, comparison))
            {
                value = (TValue)_valueConverter.Read(reader, typeof(TValue))!;
            }
            else
            {
                reader.Skip();
            }
        }

        reader.Read();
        var result = new KeyValuePair<TKey, TValue>(key!, value!);
        if (anchor is not null)
        {
            reader.RegisterAnchor(anchor, result);
        }

        return result;
    }

    public override void Write(YamlWriter writer, KeyValuePair<TKey, TValue> value)
    {
        _keyConverter ??= writer.GetConverter(typeof(TKey));
        _valueConverter ??= writer.GetConverter(typeof(TValue));
        writer.WriteStartMapping();
        writer.WritePropertyName(writer.ConvertName("Key"));
        _keyConverter.Write(writer, value.Key);
        writer.WritePropertyName(writer.ConvertName("Value"));
        _valueConverter.Write(writer, value.Value);
        writer.WriteEndMapping();
    }
}
