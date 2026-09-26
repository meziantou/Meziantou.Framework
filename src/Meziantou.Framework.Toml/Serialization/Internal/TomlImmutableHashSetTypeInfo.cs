using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

[RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
[RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
internal sealed class TomlImmutableHashSetTypeInfo<TElement> : TomlTypeInfo<ImmutableHashSet<TElement>>
{
    private TomlTypeInfo? _elementTypeInfo;
    private TomlTypeInfo<TElement>? _typedElementTypeInfo;

    public TomlImmutableHashSetTypeInfo(TomlSerializerOptions options)
        : base(options)
    {
    }

    public override void Write(TomlWriter writer, ImmutableHashSet<TElement> value)
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

        foreach (var element in value)
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

    public override ImmutableHashSet<TElement>? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        if (reader.TokenType != TomlTokenType.StartArray)
        {
            throw reader.CreateException($"Expected {TomlTokenType.StartArray} token but was {reader.TokenType}.");
        }

        var builder = ImmutableHashSet.CreateBuilder<TElement>();
        reader.Read();
        while (reader.TokenType != TomlTokenType.EndArray)
        {
            builder.Add(ReadElement(reader));
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
