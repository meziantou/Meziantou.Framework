using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlSourceGeneratedListTypeInfo<TElement> : TomlTypeInfo<List<TElement>>
{
    private readonly TomlSerializerContext _context;
    private TomlTypeInfo? _elementTypeInfo;
    private TomlTypeInfo<TElement>? _typedElementTypeInfo;

    public TomlSourceGeneratedListTypeInfo(TomlSerializerContext context, TomlSerializerOptions? options = null)
        : base(options ?? context.Options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override void Write(TomlWriter writer, List<TElement> value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(value, nameof(value));

        var writeTableArray = ShouldWriteTableArray(writer, value.Count);
        if (writeTableArray)
        {
            writer.WriteStartTableArray();
        }
        else
        {
            writer.WriteStartArray();
        }

        for (var i = 0; i < value.Count; i++)
        {
            WriteElement(writer, value[i], i);
        }

        if (writeTableArray)
        {
            writer.WriteEndTableArray();
        }
        else
        {
            writer.WriteEndArray();
        }
    }

    public override List<TElement>? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        var list = new List<TElement>();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            var elementStartState = reader.CurrentState;
            try
            {
                list.Add(ReadElement(reader));
            }
            catch (TomlException ex) when (reader.TryRecoverValue(ex, elementStartState))
            {
                // The error is recorded with the others, and the next element is read
            }
        }

        reader.Read();
        return list;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (existingValue is not List<TElement> list)
        {
            return Read(reader);
        }

        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            var elementStartState = reader.CurrentState;
            try
            {
                list.Add(ReadElement(reader));
            }
            catch (TomlException ex) when (reader.TryRecoverValue(ex, elementStartState))
            {
                // The error is recorded with the others, and the next element is read
            }
        }

        reader.Read();
        return list;
    }

    private void EnsureElementTypeInfo()
    {
        if (_elementTypeInfo is not null)
        {
            return;
        }

        var resolved = _context.GetTypeInfo(typeof(TElement), Options);
        if (resolved is null)
        {
            throw new InvalidOperationException(
                $"No generated metadata is available for type '{typeof(TElement).FullName}' in the provided context.");
        }

        _elementTypeInfo = resolved;
        _typedElementTypeInfo = resolved as TomlTypeInfo<TElement>;
    }

    private bool ShouldWriteTableArray(TomlWriter writer, int count)
    {
        if (count == 0 || !writer.CanWriteTableArrayValue)
        {
            return false;
        }

        EnsureElementTypeInfo();
        return _elementTypeInfo!.WritesTable;
    }

    private void WriteElement(TomlWriter writer, TElement element, int index = -1)
    {
        EnsureElementTypeInfo();

        if (element is null)
        {
            throw new TomlException(index >= 0
                ? $"The element at index {index} of '{Type.FullName}' is null, which TOML cannot represent."
                : $"An element of '{Type.FullName}' is null, which TOML cannot represent.");
        }

        if (_typedElementTypeInfo is not null)
        {
            _typedElementTypeInfo.Write(writer, element);
            return;
        }

        _elementTypeInfo!.Write(writer, element);
    }

    private TElement ReadElement(TomlReader reader)
    {
        EnsureElementTypeInfo();

        if (_typedElementTypeInfo is not null)
        {
            return _typedElementTypeInfo.Read(reader)!;
        }

        return (TElement)_elementTypeInfo!.ReadAsObject(reader)!;
    }
}
