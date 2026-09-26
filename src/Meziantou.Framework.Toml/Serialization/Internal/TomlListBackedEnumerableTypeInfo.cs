using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

[RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
[RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
internal sealed class TomlListBackedEnumerableTypeInfo<TEnumerable, TElement> : TomlTypeInfo<TEnumerable>
    where TEnumerable : IEnumerable<TElement>
{
    private TomlTypeInfo? _elementTypeInfo;
    private TomlTypeInfo<TElement>? _typedElementTypeInfo;

    public TomlListBackedEnumerableTypeInfo(TomlSerializerOptions options)
        : base(options)
    {
    }

    public override void Write(TomlWriter writer, TEnumerable value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(value, nameof(value));

        var items = (IEnumerable<TElement>)value;
        var count = GetCollectionCount(value, ref items);
        var writeTableArray = ShouldWriteTableArray(writer, count);
        if (writeTableArray)
        {
            writer.WriteStartTableArray();
        }
        else
        {
            writer.WriteStartArray();
        }

        foreach (var element in items)
        {
            WriteElement(writer, element);
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

    public override TEnumerable? Read(TomlReader reader)
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
            list.Add(ReadElement(reader));
        }

        reader.Read();
        return (TEnumerable)(object)list;
    }

    public override object? ReadInto(TomlReader reader, object? existingValue)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (existingValue is not ICollection<TElement> collection)
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
            collection.Add(ReadElement(reader));
        }

        reader.Read();
        return existingValue;
    }

    private void EnsureElementTypeInfo()
    {
        if (_elementTypeInfo is not null)
        {
            return;
        }

        var resolved = TomlTypeInfoResolverPipeline.Resolve(Options, typeof(TElement));
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

    private static int GetCollectionCount(TEnumerable value, ref IEnumerable<TElement> items)
    {
        if (value is ICollection<TElement> collection)
        {
            return collection.Count;
        }

        if (value is IReadOnlyCollection<TElement> readOnlyCollection)
        {
            return readOnlyCollection.Count;
        }

        var list = new List<TElement>(items);
        items = list;
        return list.Count;
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
