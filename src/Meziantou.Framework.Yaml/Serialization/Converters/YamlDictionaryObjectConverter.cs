namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlDictionaryObjectConverter : YamlConverter<Dictionary<string, object?>?>
{
    public static YamlDictionaryObjectConverter Instance { get; } = new();

    public override bool CanPopulate(Type typeToConvert) => typeToConvert == typeof(Dictionary<string, object>);

    public override object? Populate(YamlReader reader, Type typeToConvert, object existingValue)
    {
        ArgumentNullException.ThrowIfNull(existingValue);
        if (existingValue is not Dictionary<string, object?> dictionary)
        {
            throw new InvalidOperationException($"Existing value for '{typeToConvert}' must be a '{typeof(Dictionary<string, object>)}'.");
        }

        return PopulateDictionary(reader, dictionary);
    }

    public override Dictionary<string, object?>? Read(YamlReader reader)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (Dictionary<string, object?>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a dictionary unless ReferenceHandling is Preserve.");
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

        var anchor = reader.Anchor;
        reader.Read();

        var options = reader.Options;
        var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var dict = new Dictionary<string, object?>(comparer);
        var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
        HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(comparer) : null;
        HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<string>(comparer);
        if (reader.ReferenceReader is not null && anchor is not null)
        {
            reader.ReferenceReader.Register(anchor, dict);
        }

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
                ReadAndApplyMerge(reader, dict, explicitKeys);
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

            var value = reader.GetConverter(typeof(object)).Read(reader, typeof(object));
            dict[key] = value;
        }

        reader.Read();
        return dict;
    }

    public override void Write(YamlWriter writer, Dictionary<string, object?>? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        if (writer.ReferenceWriter is not null)
        {
            if (writer.ReferenceWriter.TryGetAnchor(value, out var existing))
            {
                writer.WriteAlias(existing);
                return;
            }

            var anchor = writer.ReferenceWriter.GetOrAddAnchor(value);
            writer.WriteAnchor(anchor);
        }

        writer.WriteStartMapping();
        foreach (var pair in value)
        {
            var key = writer.ConvertDictionaryKey(pair.Key);
            writer.WritePropertyName(key);
            writer.GetConverter(typeof(object)).Write(writer, pair.Value);
        }
        writer.WriteEndMapping();
    }

    private void ReadAndApplyMerge(YamlReader reader, Dictionary<string, object?> dictionary, HashSet<string>? explicitKeys)
    {
        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        if (reader.TokenType == YamlTokenType.StartMapping || reader.TokenType == YamlTokenType.Alias)
        {
            var merged = Read(reader);
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

                var merged = Read(reader);
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

    private static void ApplyMergeDictionary(Dictionary<string, object?> target, Dictionary<string, object?> merged, HashSet<string>? explicitKeys)
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

    private Dictionary<string, object?>? PopulateDictionary(YamlReader reader, Dictionary<string, object?> dictionary)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return (Dictionary<string, object?>)rootAliasValue!;
        }

        if (reader.TokenType == YamlTokenType.Alias)
        {
            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into a dictionary unless ReferenceHandling is Preserve.");
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

        var options = reader.Options;
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
                ReadAndApplyMerge(reader, dictionary, explicitKeys);
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

            var value = reader.GetConverter(typeof(object)).Read(reader, typeof(object));
            dictionary[key] = value;
        }

        reader.Read();
        return dictionary;
    }
}
