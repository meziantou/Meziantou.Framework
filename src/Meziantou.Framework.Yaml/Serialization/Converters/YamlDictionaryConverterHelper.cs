using System.Diagnostics;

namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal static class YamlDictionaryConverterHelper
{
    internal static bool TryWriteReference(YamlWriter writer, object referenceValue)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(referenceValue);

        if (writer.ReferenceWriter is null)
        {
            return false;
        }

        if (writer.ReferenceWriter.TryGetAnchor(referenceValue, out var existing))
        {
            writer.WriteAlias(existing);
            return true;
        }

        var anchor = writer.ReferenceWriter.GetOrAddAnchor(referenceValue);
        writer.WriteAnchor(anchor);
        return false;
    }

    internal static void WriteEntries<TValue>(YamlWriter writer, IEnumerable<KeyValuePair<string, TValue>> entries, YamlConverter valueConverter)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(valueConverter);

        writer.WriteStartMapping();
        foreach (var pair in entries)
        {
            var key = writer.ConvertDictionaryKey(pair.Key);
            writer.WritePropertyName(key);
            valueConverter.Write(writer, pair.Value);
        }

        writer.WriteEndMapping();
    }

    internal static void WriteEntries<TKey, TValue>(YamlWriter writer, IEnumerable<KeyValuePair<TKey, TValue>> entries, YamlConverter valueConverter)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(valueConverter);

        writer.WriteStartMapping();
        foreach (var pair in entries)
        {
            WriteKey(writer, pair.Key);
            valueConverter.Write(writer, pair.Value);
        }

        writer.WriteEndMapping();
    }

    internal static void WriteKey<TKey>(YamlWriter writer, TKey key)
    {
        if (key is null)
        {
            throw new YamlException(Mark.Empty, Mark.Empty, "Dictionary key cannot be null.");
        }

        if (key is string textKey)
        {
            writer.WritePropertyName(writer.ConvertDictionaryKey(textKey));
            return;
        }

        writer.WritePropertyName(FormatNonStringKey(key));
    }

    internal static string FormatNonStringKey<T>(T key)
    {
        if (key is bool boolValue)
        {
            return boolValue ? "true" : "false";
        }

        if (key is double doubleValue)
        {
            if (double.IsPositiveInfinity(doubleValue))
            {
                return ".inf";
            }

            if (double.IsNegativeInfinity(doubleValue))
            {
                return "-.inf";
            }

            if (double.IsNaN(doubleValue))
            {
                return ".nan";
            }

            return doubleValue.ToString("R", CultureInfo.InvariantCulture);
        }

        if (key is float floatValue)
        {
            if (float.IsPositiveInfinity(floatValue))
            {
                return ".inf";
            }

            if (float.IsNegativeInfinity(floatValue))
            {
                return "-.inf";
            }

            if (float.IsNaN(floatValue))
            {
                return ".nan";
            }

            return floatValue.ToString("R", CultureInfo.InvariantCulture);
        }

        if (key is DateTime dateTime)
        {
            return dateTime.ToString("O", CultureInfo.InvariantCulture);
        }

        if (key is DateTimeOffset dateTimeOffset)
        {
            return dateTimeOffset.ToString("O", CultureInfo.InvariantCulture);
        }

        if (key is Guid guid)
        {
            return guid.ToString("D");
        }

        if (key is TimeSpan timeSpan)
        {
            return timeSpan.ToString("c", CultureInfo.InvariantCulture);
        }

        if (key is DateOnly dateOnly)
        {
            return dateOnly.ToString("O", CultureInfo.InvariantCulture);
        }

        if (key is TimeOnly timeOnly)
        {
            return timeOnly.ToString("O", CultureInfo.InvariantCulture);
        }

        if (key is IFormattable formattable)
        {
            return formattable.ToString(format: null, CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return key is null ? string.Empty : key.ToString() ?? string.Empty;
    }

    internal static TDictionary? ReadStringDictionary<TDictionary, TValue>(
        YamlReader reader,
        ref YamlConverter? valueConverter,
        Func<YamlSerializerOptions, TDictionary> createDictionary,
        string containerKind,
        TDictionary? dictionary = null)
        where TDictionary : class, IDictionary<string, TValue>
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (TDictionary)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into a {containerKind} unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        valueConverter ??= reader.GetConverter(typeof(TValue));

        var options = reader.Options;
        dictionary ??= createDictionary(options);
        var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(comparer) : null;
        HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<string>(comparer);
        if (reader.ReferenceReader is not null && reader.Anchor is not null)
        {
            reader.ReferenceReader.Register(reader.Anchor, dictionary);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var key = reader.ScalarValue ?? string.Empty;
            reader.Read();

            if (mergeEnabled && string.Equals(key, "<<", StringComparison.Ordinal))
            {
                if (!options.AllowMergeKeys)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "YAML merge keys are not allowed.");
                }

                ReadAndApplyMerge<TDictionary, TValue>(reader, ref valueConverter, dictionary, explicitKeys, createDictionary, containerKind);
                continue;
            }

            explicitKeys?.Add(key);

            var wasSeen = seenKeys is not null && !seenKeys.Add(key);
            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
            {
                throw YamlThrowHelper.ThrowDuplicateMappingKey(reader, key);
            }

            if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.FirstWins)
            {
                reader.Skip();
                continue;
            }

            Debug.Assert(valueConverter is not null);
            var value = valueConverter.Read(reader, typeof(TValue));
            dictionary[key] = (TValue)value!;
        }

        reader.Read();
        return dictionary;
    }

    internal static TDictionary? ReadDictionary<TDictionary, TKey, TValue>(
        YamlReader reader,
        ref YamlConverter? keyConverter,
        ref YamlConverter? valueConverter,
        Func<TDictionary> createDictionary,
        string containerKind,
        TDictionary? dictionary = null)
        where TDictionary : class, IDictionary<TKey, TValue>
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (TDictionary)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, $"Aliases are not supported when deserializing into a {containerKind} unless ReferenceHandling is Preserve.");
        }

        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return null;
        }

        if (reader.TokenType != YamlTokenType.StartMapping)
        {
            throw YamlThrowHelper.ThrowExpectedMapping(reader);
        }

        keyConverter ??= reader.GetConverter(typeof(TKey));
        valueConverter ??= reader.GetConverter(typeof(TValue));

        dictionary ??= createDictionary();
        var options = reader.Options;
        if (reader.ReferenceReader is not null && reader.Anchor is not null)
        {
            reader.ReferenceReader.Register(reader.Anchor, dictionary);
        }

        reader.Read();
        while (reader.TokenType != YamlTokenType.EndMapping)
        {
            if (reader.TokenType != YamlTokenType.Scalar)
            {
                throw YamlThrowHelper.ThrowExpectedScalarKey(reader);
            }

            var keyStart = reader.Start;
            var keyEnd = reader.End;
            object? rawKey;
            try
            {
                rawKey = keyConverter.Read(reader, typeof(TKey));
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
            }

            if (rawKey is null)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, "Dictionary key cannot be null.");
            }

            object? rawValue;
            try
            {
                rawValue = valueConverter.Read(reader, typeof(TValue));
            }
            catch (YamlException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new YamlException(reader.SourceName, keyStart, keyEnd, exception.Message, exception);
            }

            var key = (TKey)rawKey;
            if (dictionary.ContainsKey(key))
            {
                switch (options.DuplicateKeyHandling)
                {
                    case YamlDuplicateKeyHandling.Error:
                        throw YamlThrowHelper.ThrowDuplicateMappingKey(reader, key.ToString() ?? string.Empty);
                    case YamlDuplicateKeyHandling.FirstWins:
                        break;
                    case YamlDuplicateKeyHandling.LastWins:
                        dictionary[key] = (TValue)rawValue!;
                        break;
                }
            }
            else
            {
                dictionary[key] = (TValue)rawValue!;
            }
        }

        reader.Read();
        return dictionary;
    }

    private static void ReadAndApplyMerge<TDictionary, TValue>(
        YamlReader reader,
        ref YamlConverter? valueConverter,
        TDictionary dictionary,
        HashSet<string>? explicitKeys,
        Func<YamlSerializerOptions, TDictionary> createDictionary,
        string containerKind)
        where TDictionary : class, IDictionary<string, TValue>
    {
        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        if (reader.TokenType == YamlTokenType.StartMapping || reader.TokenType == YamlTokenType.Alias)
        {
            var merged = ReadStringDictionary<TDictionary, TValue>(reader, ref valueConverter, createDictionary, containerKind);
            if (merged is null)
            {
                return;
            }

            ApplyMergeDictionary(dictionary, merged, explicitKeys);
            return;
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            reader.Read();
            while (reader.TokenType != YamlTokenType.EndSequence)
            {
                if (reader.TokenType != YamlTokenType.StartMapping && reader.TokenType != YamlTokenType.Alias)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                var merged = ReadStringDictionary<TDictionary, TValue>(reader, ref valueConverter, createDictionary, containerKind);
                if (merged is not null)
                {
                    ApplyMergeDictionary(dictionary, merged, explicitKeys);
                }
            }

            reader.Read();
            return;
        }

        throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge key value must be a mapping or a sequence of mappings.");
    }

    private static void ApplyMergeDictionary<TValue>(
        IDictionary<string, TValue> target,
        IEnumerable<KeyValuePair<string, TValue>> merged,
        HashSet<string>? explicitKeys)
    {
        foreach (var pair in merged)
        {
            if (explicitKeys is not null && explicitKeys.Contains(pair.Key))
            {
                continue;
            }

            target[pair.Key] = pair.Value;
        }
    }
}
