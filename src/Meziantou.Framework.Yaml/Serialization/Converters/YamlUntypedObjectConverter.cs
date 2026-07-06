namespace Meziantou.Framework.Yaml.Serialization.Converters;

internal sealed class YamlUntypedObjectConverter : YamlConverter
{
    public static YamlUntypedObjectConverter Instance { get; } = new();

    public override bool CanConvert(Type typeToConvert) => typeToConvert == typeof(object);

    public override object? Read(YamlReader reader, Type typeToConvert)
    {
        if (reader.TryReadAlias(out var rootAliasValue))
        {
            return rootAliasValue;
        }

        var options = reader.Options;
        if (options.UnsafeAllowDeserializeFromTagTypeName && reader.Tag is not null)
        {
            var activated = YamlUntypedObjectConverter.TryReadUnsafeTaggedValue(reader);
            if (activated is not null)
            {
                return activated;
            }
        }

        switch (reader.TokenType)
        {
            case YamlTokenType.Scalar:
                var scalar = YamlScalar.ResolveObject(reader);
                reader.Read();
                return scalar;

            case YamlTokenType.StartSequence:
                var sequenceAnchor = reader.Anchor;
                reader.Read();
                var list = new List<object?>();
                if (reader.ReferenceReader is not null && sequenceAnchor is not null)
                {
                    reader.ReferenceReader.Register(sequenceAnchor, list);
                }

                while (reader.TokenType != YamlTokenType.EndSequence)
                {
                    list.Add(Read(reader, typeof(object)));
                }
                reader.Read();
                return list;

            case YamlTokenType.StartMapping:
                var mappingAnchor = reader.Anchor;
                reader.Read();
                var comparer = options.PropertyNameCaseInsensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
                var dict = new Dictionary<string, object?>(comparer);
                var mergeEnabled = options.Schema is YamlSchemaKind.Core or YamlSchemaKind.Extended;
                HashSet<string>? explicitKeys = mergeEnabled ? new HashSet<string>(comparer) : null;
                HashSet<string>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<string>(comparer);
                if (reader.ReferenceReader is not null && mappingAnchor is not null)
                {
                    reader.ReferenceReader.Register(mappingAnchor, dict);
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

                    var value = Read(reader, typeof(object));
                    dict[key] = value;
                }
                reader.Read();
                return dict;

            case YamlTokenType.Alias:
                throw new YamlException(reader.SourceName, reader.Start, reader.End, "Aliases are not supported when deserializing into object unless ReferenceHandling is Preserve.");

            default:
                throw YamlThrowHelper.ThrowUnexpectedToken(reader);
        }
    }

    public override void Write(YamlWriter writer, object? value)
    {
        if (value is null)
        {
            writer.WriteNullValue();
            return;
        }

        var converter = writer.GetConverter(value.GetType());
        converter.Write(writer, value);
    }

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2057",
        Justification = "This opt-in feature enables tag-based activation by runtime type name and is not compatible with trimming. It is guarded by UnsafeAllowDeserializeFromTagTypeName.")]
    private static object? TryReadUnsafeTaggedValue(YamlReader reader)
    {
        var tag = reader.Tag;
        if (string.IsNullOrWhiteSpace(tag) || tag[0] != '!')
        {
            return null;
        }

        var typeName = tag.Substring(1);
        var type = Type.GetType(typeName, throwOnError: false);
        if (type is null && typeName.Contains(",mscorlib", StringComparison.Ordinal))
        {
            type = Type.GetType(typeName.Replace(",mscorlib", ",System.Private.CoreLib", StringComparison.Ordinal), throwOnError: false);
        }

        if (type is null)
        {
            return null;
        }

        var converter = reader.GetConverter(type);
        return converter.Read(reader, type);
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
            var merged = Read(reader, typeof(object));
            if (merged is Dictionary<string, object?> mergedDict)
            {
                ApplyMergeDictionary(dictionary, mergedDict, explicitKeys);
                return;
            }

            throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge key value must be a mapping or a sequence of mappings.");
        }

        if (reader.TokenType == YamlTokenType.StartSequence)
        {
            reader.Read();
            while (reader.TokenType != YamlTokenType.EndSequence)
            {
                var merged = Read(reader, typeof(object));
                if (merged is not Dictionary<string, object?> mergedDict)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                ApplyMergeDictionary(dictionary, mergedDict, explicitKeys);
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
}
