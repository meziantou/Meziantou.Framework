using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal sealed class TomlSourceGeneratedImmutableArrayTypeInfo<TElement> : TomlTypeInfo<ImmutableArray<TElement>>
{
    private readonly TomlSerializerContext _context;
    private TomlTypeInfo? _elementTypeInfo;
    private TomlTypeInfo<TElement>? _typedElementTypeInfo;

    public TomlSourceGeneratedImmutableArrayTypeInfo(TomlSerializerContext context, TomlSerializerOptions? options = null)
        : base(options ?? context.Options)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
    }

    public override void Write(TomlWriter writer, ImmutableArray<TElement> value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));

        if (value.IsDefault)
        {
            value = ImmutableArray<TElement>.Empty;
        }

        var writeTableArray = ShouldWriteTableArray(writer, value.Length);
        if (writeTableArray)
        {
            writer.WriteStartTableArray();
        }
        else
        {
            writer.WriteStartArray();
        }

        for (var i = 0; i < value.Length; i++)
        {
            WriteElement(writer, value[i]);
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

    public override ImmutableArray<TElement> Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        var builder = ImmutableArray.CreateBuilder<TElement>();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            var elementStartState = reader.CurrentState;
            try
            {
                builder.Add(ReadElement(reader));
            }
            catch (TomlException ex) when (reader.TryRecoverValue(ex, elementStartState))
            {
                // The error is recorded with the others, and the next element is read
            }
        }

        reader.Read();
        return builder.ToImmutable();
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

    private void WriteElement(TomlWriter writer, TElement element)
    {
        EnsureElementTypeInfo();

        if (element is null)
        {
            throw new TomlException("TOML does not support null values.");
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
