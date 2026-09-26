using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlSourceGeneratedDictionaryTypeInfo<TDictionary, TValue> : TomlTypeInfo<TDictionary>
    where TDictionary : IEnumerable<KeyValuePair<string, TValue>>
{
    private readonly TomlSerializerContext _context;
    private readonly Func<IDictionary<string, TValue>>? _createDictionary;
    private TomlTypeInfo? _valueTypeInfo;
    private TomlTypeInfo<TValue>? _typedValueTypeInfo;

    public TomlSourceGeneratedDictionaryTypeInfo(TomlSerializerContext context, TomlSerializerOptions? options = null, Func<IDictionary<string, TValue>>? createDictionary = null)
        : base(options ?? context.Options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _createDictionary = createDictionary;
    }

    public override bool WritesTable => true;

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
            WriteValue(writer, pair.Key, pair.Value);
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

        var dict = _createDictionary?.Invoke() ?? new Dictionary<string, TValue>(StringComparer.Ordinal);
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

            var valueStartState = reader.CurrentState;
            try
            {
                dict[key] = ReadValue(reader);
            }
            catch (TomlException ex) when (reader.TryRecoverValue(ex, valueStartState))
            {
                // The error is recorded with the others, and the next value is read
            }
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

            var valueStartState = reader.CurrentState;
            try
            {
                dict[key] = ReadValue(reader);
            }
            catch (TomlException ex) when (reader.TryRecoverValue(ex, valueStartState))
            {
                // The error is recorded with the others, and the next value is read
            }
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

        var resolved = _context.GetTypeInfo(typeof(TValue), Options);
        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"No generated metadata is available for type '{typeof(TValue).FullName}' in the provided context.");
        }

        _valueTypeInfo = resolved;
        _typedValueTypeInfo = resolved as TomlTypeInfo<TValue>;
    }

    private void WriteValue(TomlWriter writer, string key, TValue value)
    {
        EnsureValueTypeInfo();

        if (value is null)
        {
            throw new TomlException($"The value of the key '{key}' in '{Type.FullName}' is null, which TOML cannot represent.");
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
