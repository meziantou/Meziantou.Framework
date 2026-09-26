using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

[RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
[RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
internal sealed class TomlDictionaryTypeInfo<TDictionary, TValue> : TomlTypeInfo<TDictionary>
    where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
{
    private TomlTypeInfo? _valueTypeInfo;
    private TomlTypeInfo<TValue>? _typedValueTypeInfo;

    public TomlDictionaryTypeInfo(TomlSerializerOptions options)
        : base(options)
    {
    }

    public override bool WritesTable => true;

    // Dictionary<,> for the dictionary interfaces, otherwise the type itself (SortedDictionary<,>, a subclass, ...)
    private static IDictionary<string, TValue> CreateDictionary()
    {
        if (typeof(TDictionary).IsAssignableFrom(typeof(Dictionary<string, TValue>)))
        {
            return new Dictionary<string, TValue>(StringComparer.Ordinal);
        }

        if (!typeof(TDictionary).IsAbstract && typeof(TDictionary).GetConstructor(Type.EmptyTypes) is not null && Activator.CreateInstance<TDictionary>() is IDictionary<string, TValue> dictionary)
        {
            return dictionary;
        }

        throw new TomlException($"Deserializing '{typeof(TDictionary).FullName}' is not supported: the dictionary type must implement IDictionary<string, TValue> and have a public parameterless constructor.");
    }

    public override void Write(TomlWriter writer, TDictionary value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(value, nameof(value));

        writer.WriteStartTable();
        foreach (var pair in value)
        {
            var key = pair.Key;
            if (key is null)
            {
                throw new TomlException("Dictionary keys cannot be null.");
            }

            if (Options.DictionaryKeyPolicy is { } keyPolicy)
            {
                key = keyPolicy.ConvertName(key);
            }

            writer.WritePropertyName(key);
            WriteValue(writer, pair.Value);
        }

        writer.WriteEndTable();
    }

    public override TDictionary? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (reader.TokenType != TomlTokenType.StartTable)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
        }

        var dict = CreateDictionary();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            if (reader.TokenType != TomlTokenType.PropertyName)
            {
                throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
            }

            var key = reader.PropertyName!;
            reader.Read();
            if (Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error &&
                dict.TryGetValue(key, out var existingValue))
            {
                if (TryReadTableHeaderExtension(reader, existingValue, out var mergedValue))
                {
                    dict[key] = mergedValue;
                    continue;
                }

                throw reader.CreateException($"Duplicate key '{key}' was encountered.");
            }

            if (TomlTableHeaderExtensionHelper.IsTableHeaderExtension(reader) && dict.TryGetValue(key, out existingValue))
            {
                if (TryReadTableHeaderExtension(reader, existingValue, out var mergedValue))
                {
                    dict[key] = mergedValue;
                    continue;
                }
            }

            dict[key] = ReadValue(reader);
        }

        reader.Read();
        return (TDictionary)dict;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (existingValue is not IDictionary<string, TValue> dict)
        {
            return Read(reader);
        }

        if (reader.TokenType != TomlTokenType.StartTable)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
        }

        HashSet<string>? seen = null;
        if (Options.DuplicateKeyHandling == TomlDuplicateKeyHandling.Error)
        {
            seen = new HashSet<string>(StringComparer.Ordinal);
        }

        reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            if (reader.TokenType != TomlTokenType.PropertyName)
            {
                throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
            }

            var key = reader.PropertyName!;
            reader.Read();
            if (seen is not null && !seen.Add(key))
            {
                if (dict.TryGetValue(key, out var duplicateExistingValue) && TryReadTableHeaderExtension(reader, duplicateExistingValue, out var mergedValue))
                {
                    dict[key] = mergedValue;
                    continue;
                }

                throw reader.CreateException($"Duplicate key '{key}' was encountered.");
            }

            if (TomlTableHeaderExtensionHelper.IsTableHeaderExtension(reader) && dict.TryGetValue(key, out var currentValue))
            {
                if (TryReadTableHeaderExtension(reader, currentValue, out var mergedValue))
                {
                    dict[key] = mergedValue;
                    continue;
                }
            }

            dict[key] = ReadValue(reader);
        }

        reader.Read();
        return existingValue;
    }

    private void EnsureValueTypeInfo()
    {
        if (_valueTypeInfo is not null)
        {
            return;
        }

        var resolved = TomlTypeInfoResolverPipeline.Resolve(Options, typeof(TValue));
        _valueTypeInfo = resolved;
        _typedValueTypeInfo = resolved as TomlTypeInfo<TValue>;
    }

    private void WriteValue(TomlWriter writer, TValue value)
    {
        EnsureValueTypeInfo();

        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        if (_typedValueTypeInfo is not null)
        {
            _typedValueTypeInfo.Write(writer, value);
            return;
        }

        _valueTypeInfo!.Write(writer, value);
    }

    private TValue ReadValue(TomlReader reader)
    {
        EnsureValueTypeInfo();

        if (_typedValueTypeInfo is not null)
        {
            return _typedValueTypeInfo.Read(reader)!;
        }

        return (TValue)_valueTypeInfo!.ReadAsObject(reader)!;
    }

    private bool TryReadTableHeaderExtension(TomlReader reader, TValue existingValue, out TValue mergedValue)
    {
        EnsureValueTypeInfo();

        if (!TomlTableHeaderExtensionHelper.TryReadIntoExisting(reader, existingValue, _valueTypeInfo!, out var merged))
        {
            mergedValue = existingValue;
            return false;
        }

        mergedValue = (TValue)merged!;
        return true;
    }
}
