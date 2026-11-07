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
                var comparer = options.PropertyNameCaseInsensitive ? UntypedKeyComparer.IgnoreCase : UntypedKeyComparer.Ordinal;
                var dict = new Dictionary<object, object?>(comparer);
                var mergeEnabled = YamlMergeKey.IsEnabled(options);
                HashSet<object>? explicitKeys = mergeEnabled ? new HashSet<object>(comparer) : null;
                HashSet<object>? seenKeys = options.DuplicateKeyHandling == YamlDuplicateKeyHandling.LastWins ? null : new HashSet<object>(comparer);
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

                    if (YamlMergeKey.IsMergeKey(reader))
                    {
                        reader.Read();
                        ReadAndApplyMerge(reader, dict, explicitKeys);
                        continue;
                    }

                    // Keys are resolved like values, so "1" and "'1'" are distinct keys, while "1" and "0x1" are
                    // the same integer.
                    var keyText = reader.ScalarValue ?? string.Empty;
                    var key = YamlScalar.ResolveObject(reader) ?? throw YamlThrowHelper.ThrowNotSupported(reader, "A null mapping key cannot be deserialized into 'object'.");
                    reader.Read();

                    explicitKeys?.Add(key);

                    var wasSeen = seenKeys is not null && !seenKeys.Add(key);
                    if (wasSeen && options.DuplicateKeyHandling == YamlDuplicateKeyHandling.Error)
                    {
                        throw YamlThrowHelper.ThrowDuplicateMappingKey(reader, keyText);
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

        var runtimeType = value.GetType();
        if (runtimeType == typeof(object))
        {
            // A plain System.Object carries no state, and resolving its runtime type leads back to this converter.
            writer.WriteStartMapping();
            writer.WriteEndMapping();
            return;
        }

        var converter = writer.GetConverter(runtimeType);
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

    private void ReadAndApplyMerge(YamlReader reader, Dictionary<object, object?> dictionary, HashSet<object>? explicitKeys)
    {
        if (reader.TokenType == YamlTokenType.Scalar && YamlScalar.IsNull(reader))
        {
            reader.Read();
            return;
        }

        YamlMergeKey.ReplayAlias(reader, typeof(object));
        if (reader.TokenType == YamlTokenType.StartMapping)
        {
            var merged = Read(reader, typeof(object));
            if (merged is Dictionary<object, object?> mergedDict)
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
                YamlMergeKey.ReplayAlias(reader, typeof(object));
                if (reader.TokenType != YamlTokenType.StartMapping)
                {
                    throw new YamlException(reader.SourceName, reader.Start, reader.End, "Merge sequence entries must be mappings.");
                }

                var merged = Read(reader, typeof(object));
                if (merged is not Dictionary<object, object?> mergedDict)
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

    /// <remarks>
    /// A key an earlier mapping of the merge already provided keeps its value, so the merged key is recorded
    /// alongside the explicitly declared ones.
    /// </remarks>
    private static void ApplyMergeDictionary(Dictionary<object, object?> target, Dictionary<object, object?> merged, HashSet<object>? explicitKeys)
    {
        foreach (var pair in merged)
        {
            if (explicitKeys is not null && !explicitKeys.Add(pair.Key))
            {
                continue;
            }

            target[pair.Key] = pair.Value;
        }
    }

    /// <summary>Compares the keys of an untyped mapping by the value they resolve to.</summary>
    /// <remarks>
    /// An integer resolves to <see cref="int"/>, <see cref="long"/>, or <see cref="ulong"/> depending on its magnitude
    /// and on the resolution path, so integers are compared by value whatever their CLR type.
    /// </remarks>
    private sealed class UntypedKeyComparer : IEqualityComparer<object>
    {
        public static UntypedKeyComparer Ordinal { get; } = new(StringComparer.Ordinal);

        public static UntypedKeyComparer IgnoreCase { get; } = new(StringComparer.OrdinalIgnoreCase);

        private readonly StringComparer _stringComparer;

        private UntypedKeyComparer(StringComparer stringComparer) => _stringComparer = stringComparer;

        public new bool Equals(object? x, object? y)
        {
            if (x is string xs && y is string ys)
            {
                return _stringComparer.Equals(xs, ys);
            }

            if (TryGetInteger(x, out var xi) && TryGetInteger(y, out var yi))
            {
                return xi == yi;
            }

            return object.Equals(x, y);
        }

        public int GetHashCode(object obj)
        {
            if (obj is string s)
            {
                return _stringComparer.GetHashCode(s);
            }

            if (TryGetInteger(obj, out var integer))
            {
                return integer.GetHashCode();
            }

            return obj.GetHashCode();
        }

        private static bool TryGetInteger(object? value, out Int128 result)
        {
            switch (value)
            {
                case int i:
                    result = i;
                    return true;
                case long l:
                    result = l;
                    return true;
                case ulong u:
                    result = u;
                    return true;
                default:
                    result = default;
                    return false;
            }
        }
    }
}
