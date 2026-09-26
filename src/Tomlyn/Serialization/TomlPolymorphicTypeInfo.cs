// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Serialization.Converters;
using Tomlyn.Text;

namespace Tomlyn.Serialization;

/// <summary>
/// Provides source-generator-friendly discriminator-based polymorphism support for TOML serialization.
/// </summary>
/// <typeparam name="TBase">The base type.</typeparam>
public sealed class TomlPolymorphicTypeInfo<TBase> : TomlTypeInfo<TBase>
{
    private readonly TomlTypeInfo<TBase>? _baseTypeInfo;
    private readonly string _discriminatorPropertyName;
    private readonly Dictionary<string, TomlTypeInfo> _derivedTypeInfoByDiscriminator;
    private readonly Dictionary<Type, (string? Discriminator, TomlTypeInfo TypeInfo)> _derivedTypeInfoByRuntimeType;
    private readonly TomlUnknownDerivedTypeHandling _unknownDerivedTypeHandling;
    private readonly TomlTypeInfo? _defaultDerivedTypeInfo;

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPolymorphicTypeInfo{TBase}"/> class.
    /// </summary>
    /// <param name="options">The serializer options.</param>
    /// <param name="baseTypeInfo">Optional base type metadata used when falling back to base types.</param>
    /// <param name="discriminatorPropertyName">Optional discriminator key name. When <see langword="null"/> or empty, <see cref="TomlSerializerOptions.PolymorphismOptions"/> is used.</param>
    /// <param name="derivedTypeInfoByDiscriminator">A mapping from discriminator values to derived type metadata.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="derivedTypeInfoByDiscriminator"/> is <see langword="null"/>.</exception>
    /// <exception cref="TomlException">Thrown when the mapping is invalid.</exception>
    public TomlPolymorphicTypeInfo(
        TomlSerializerOptions options,
        TomlTypeInfo<TBase>? baseTypeInfo,
        string? discriminatorPropertyName,
        IReadOnlyDictionary<string, TomlTypeInfo> derivedTypeInfoByDiscriminator)
        : this(options, baseTypeInfo, discriminatorPropertyName, derivedTypeInfoByDiscriminator, null, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPolymorphicTypeInfo{TBase}"/> class with an optional default derived type.
    /// </summary>
    /// <param name="options">The serializer options.</param>
    /// <param name="baseTypeInfo">Optional base type metadata used when falling back to base types.</param>
    /// <param name="discriminatorPropertyName">Optional discriminator key name. When <see langword="null"/> or empty, <see cref="TomlSerializerOptions.PolymorphismOptions"/> is used.</param>
    /// <param name="derivedTypeInfoByDiscriminator">A mapping from discriminator values to derived type metadata.</param>
    /// <param name="defaultDerivedTypeInfo">Optional default derived type metadata, used when the discriminator is missing or unrecognized.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="derivedTypeInfoByDiscriminator"/> is <see langword="null"/>.</exception>
    /// <exception cref="TomlException">Thrown when the mapping is invalid.</exception>
    public TomlPolymorphicTypeInfo(
        TomlSerializerOptions options,
        TomlTypeInfo<TBase>? baseTypeInfo,
        string? discriminatorPropertyName,
        IReadOnlyDictionary<string, TomlTypeInfo> derivedTypeInfoByDiscriminator,
        TomlTypeInfo? defaultDerivedTypeInfo)
        : this(options, baseTypeInfo, discriminatorPropertyName, derivedTypeInfoByDiscriminator, defaultDerivedTypeInfo, null)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TomlPolymorphicTypeInfo{TBase}"/> class with optional default derived type and unknown handling override.
    /// </summary>
    /// <param name="options">The serializer options.</param>
    /// <param name="baseTypeInfo">Optional base type metadata used when falling back to base types.</param>
    /// <param name="discriminatorPropertyName">Optional discriminator key name. When <see langword="null"/> or empty, <see cref="TomlSerializerOptions.PolymorphismOptions"/> is used.</param>
    /// <param name="derivedTypeInfoByDiscriminator">A mapping from discriminator values to derived type metadata.</param>
    /// <param name="defaultDerivedTypeInfo">Optional default derived type metadata, used when the discriminator is missing or unrecognized.</param>
    /// <param name="unknownDerivedTypeHandling">Optional override for unknown discriminator handling. When <see langword="null"/>, uses options default.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="options"/> or <paramref name="derivedTypeInfoByDiscriminator"/> is <see langword="null"/>.</exception>
    /// <exception cref="TomlException">Thrown when the mapping is invalid.</exception>
    public TomlPolymorphicTypeInfo(
        TomlSerializerOptions options,
        TomlTypeInfo<TBase>? baseTypeInfo,
        string? discriminatorPropertyName,
        IReadOnlyDictionary<string, TomlTypeInfo> derivedTypeInfoByDiscriminator,
        TomlTypeInfo? defaultDerivedTypeInfo,
        TomlUnknownDerivedTypeHandling? unknownDerivedTypeHandling)
        : base(options)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(derivedTypeInfoByDiscriminator, nameof(derivedTypeInfoByDiscriminator));

        _baseTypeInfo = baseTypeInfo;
        _defaultDerivedTypeInfo = defaultDerivedTypeInfo;
        _unknownDerivedTypeHandling = unknownDerivedTypeHandling ?? options.PolymorphismOptions.UnknownDerivedTypeHandling;
        _discriminatorPropertyName = string.IsNullOrEmpty(discriminatorPropertyName)
            ? options.PolymorphismOptions.TypeDiscriminatorPropertyName
            : discriminatorPropertyName;

        if (string.IsNullOrEmpty(_discriminatorPropertyName))
        {
            throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' must specify a discriminator property name.");
        }

        _derivedTypeInfoByDiscriminator = new Dictionary<string, TomlTypeInfo>(derivedTypeInfoByDiscriminator.Count, StringComparer.Ordinal);
        _derivedTypeInfoByRuntimeType = new Dictionary<Type, (string?, TomlTypeInfo)>(derivedTypeInfoByDiscriminator.Count + (defaultDerivedTypeInfo is not null ? 1 : 0));

        foreach (var pair in derivedTypeInfoByDiscriminator)
        {
            var discriminator = pair.Key;
            var typeInfo = pair.Value;
            if (string.IsNullOrEmpty(discriminator))
            {
                throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' cannot register an empty discriminator.");
            }

            if (typeInfo is null)
            {
                throw new ArgumentException("Derived type metadata cannot be null.", nameof(derivedTypeInfoByDiscriminator));
            }

            if (_derivedTypeInfoByDiscriminator.ContainsKey(discriminator))
            {
                throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' cannot register discriminator '{discriminator}' more than once.");
            }

            var runtimeType = typeInfo.Type;
            if (_derivedTypeInfoByRuntimeType.ContainsKey(runtimeType))
            {
                throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' cannot register derived type '{runtimeType.FullName}' more than once.");
            }

            _derivedTypeInfoByDiscriminator.Add(discriminator, typeInfo);
            _derivedTypeInfoByRuntimeType.Add(runtimeType, (discriminator, typeInfo));
        }

        if (defaultDerivedTypeInfo is not null)
        {
            var defaultType = defaultDerivedTypeInfo.Type;
            if (_derivedTypeInfoByRuntimeType.ContainsKey(defaultType))
            {
                throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' cannot register derived type '{defaultType.FullName}' more than once.");
            }

            _derivedTypeInfoByRuntimeType.Add(defaultType, (null, defaultDerivedTypeInfo));
        }

        if (_derivedTypeInfoByDiscriminator.Count == 0 && _defaultDerivedTypeInfo is null)
        {
            throw new TomlException($"Polymorphic type '{typeof(TBase).FullName}' must register at least one derived type.");
        }
    }

    /// <inheritdoc />
    public override void Write(TomlWriter writer, TBase value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        var runtimeType = value.GetType();
        if (!_derivedTypeInfoByRuntimeType.TryGetValue(runtimeType, out var dispatch))
        {
            if (_unknownDerivedTypeHandling == TomlUnknownDerivedTypeHandling.FallBackToBaseType && _baseTypeInfo is not null)
            {
                _baseTypeInfo.Write(writer, value);
                return;
            }

            throw new TomlException($"Unknown derived type '{runtimeType.FullName}' when serializing '{typeof(TBase).FullName}'.");
        }

        var (discriminator, runtimeTypeInfo) = dispatch;

        // Default derived type (null discriminator) - serialize without discriminator
        if (discriminator is null)
        {
            runtimeTypeInfo.Write(writer, value);
            return;
        }

        var tempWriter = new TomlWriter(TextWriter.Null, Options, writer.OperationState);
        tempWriter.WriteStartDocument();
        runtimeTypeInfo.Write(tempWriter, value);
        tempWriter.WriteEndDocument();

        if (tempWriter.RootValue is not TomlTable table)
        {
            throw new TomlException($"Polymorphic TOML values must be tables. Type '{runtimeType.FullName}' did not write a table.");
        }

        if (table.ContainsKey(_discriminatorPropertyName))
        {
            throw new TomlException($"The discriminator key '{_discriminatorPropertyName}' conflicts with an existing member when serializing '{runtimeType.FullName}'.");
        }

        table[_discriminatorPropertyName] = discriminator;
        TomlUntypedObjectConverter.Instance.Write(writer, table);
    }

    /// <inheritdoc />
    public override TBase? Read(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        if (reader.TokenType != TomlTokenType.StartTable)
        {
            if (_baseTypeInfo is null)
            {
                throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
            }

            return _baseTypeInfo.Read(reader);
        }

        var tableStartSpan = reader.CurrentSpan;
        var buffer = reader.CaptureCurrentValueToBuffer();
        if (!TryReadDiscriminator(buffer, out var discriminator, out var discriminatorSpan))
        {
            // No discriminator found - try default derived type first
            if (_defaultDerivedTypeInfo is not null)
            {
                var defaultReader = TomlReader.Create(buffer);
                defaultReader.Read(); // StartDocument
                defaultReader.Read(); // value start
                return (TBase?)_defaultDerivedTypeInfo.ReadAsObject(defaultReader);
            }

            var span = tableStartSpan ?? GetTableSpan(buffer);
            if (typeof(TBase).IsInterface || typeof(TBase).IsAbstract)
            {
                if (span is { } tableSpan)
                {
                    throw new TomlException(tableSpan, $"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{typeof(TBase).FullName}'.");
                }

                throw new TomlException($"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{typeof(TBase).FullName}'.");
            }

            if (_baseTypeInfo is null)
            {
                if (span is { } locatedSpan)
                {
                    throw new TomlException(locatedSpan, $"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{typeof(TBase).FullName}'.");
                }

                throw new TomlException($"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{typeof(TBase).FullName}'.");
            }

            var fallbackReader = TomlReader.Create(buffer);
            fallbackReader.Read(); // StartDocument
            fallbackReader.Read(); // value start
            return _baseTypeInfo.Read(fallbackReader);
        }

        TomlTypeInfo targetTypeInfo;

        if (_derivedTypeInfoByDiscriminator.TryGetValue(discriminator, out var derivedTypeInfo))
        {
            targetTypeInfo = derivedTypeInfo;
        }
        else if (_defaultDerivedTypeInfo is not null)
        {
            // Unknown discriminator - use default derived type
            targetTypeInfo = _defaultDerivedTypeInfo;
        }
        else if (_unknownDerivedTypeHandling == TomlUnknownDerivedTypeHandling.FallBackToBaseType && _baseTypeInfo is not null && !typeof(TBase).IsInterface && !typeof(TBase).IsAbstract)
        {
            targetTypeInfo = _baseTypeInfo;
        }
        else
        {
            if (discriminatorSpan is { } span)
            {
                throw new TomlException(span, $"Unknown discriminator '{discriminator}' when deserializing '{typeof(TBase).FullName}'.");
            }

            throw new TomlException($"Unknown discriminator '{discriminator}' when deserializing '{typeof(TBase).FullName}'.");
        }

        var payloadReader = TomlReader.Create(buffer, _discriminatorPropertyName);
        payloadReader.Read(); // StartDocument
        payloadReader.Read(); // value start
        return (TBase?)targetTypeInfo.ReadAsObject(payloadReader);
    }

    private bool TryReadDiscriminator(TomlReaderBuffer buffer, out string discriminator, out TomlSourceSpan? discriminatorSpan)
    {
        discriminator = string.Empty;
        discriminatorSpan = null;

        var reader = TomlReader.Create(buffer);
        reader.Read(); // StartDocument
        reader.Read(); // value start
        if (reader.TokenType != TomlTokenType.StartTable)
        {
            return false;
        }

        reader.Read();
        while (reader.TokenType != TomlTokenType.EndTable)
        {
            if (reader.TokenType != TomlTokenType.PropertyName)
            {
                reader.Skip();
                continue;
            }

            var name = reader.PropertyName ?? string.Empty;
            reader.Read(); // value
            if (string.Equals(name, _discriminatorPropertyName, StringComparison.Ordinal))
            {
                if (reader.TokenType != TomlTokenType.String)
                {
                    throw reader.CreateException($"Discriminator key '{_discriminatorPropertyName}' must be a string.");
                }

                discriminatorSpan = reader.CurrentSpan;
                discriminator = reader.GetString();
                return true;
            }

            reader.Skip();
        }

        return false;
    }

    private static TomlSourceSpan? GetTableSpan(TomlReaderBuffer buffer)
    {
        foreach (var token in buffer.Tokens)
        {
            if (token.TokenType == TomlTokenType.StartTable && token.Span is { } span)
            {
                return span;
            }
        }

        foreach (var token in buffer.Tokens)
        {
            if (token.TokenType == TomlTokenType.EndTable && token.Span is { } span)
            {
                return span;
            }
        }

        return null;
    }
}
