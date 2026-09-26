// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json.Serialization;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Serialization.Converters;
using Tomlyn.Text;

namespace Tomlyn.Serialization.Internal;

[RequiresUnreferencedCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
[RequiresDynamicCode("Reflection-based TOML serialization is not compatible with trimming/NativeAOT. Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.")]
internal sealed class TomlPolymorphicTypeInfo : TomlTypeInfo
{
    private readonly TomlTypeInfo? _baseTypeInfo;
    private readonly string _discriminatorPropertyName;
    private readonly TomlUnknownDerivedTypeHandling _unknownDerivedTypeHandling;
    private readonly Dictionary<string, Type> _derivedTypeByDiscriminator;
    private readonly Dictionary<Type, string?> _discriminatorByDerivedType;
    private readonly Type? _defaultDerivedType;

    private TomlPolymorphicTypeInfo(
        Type type,
        TomlSerializerOptions options,
        TomlTypeInfo? baseTypeInfo,
        string discriminatorPropertyName,
        TomlUnknownDerivedTypeHandling unknownDerivedTypeHandling,
        Dictionary<string, Type> derivedTypeByDiscriminator,
        Dictionary<Type, string?> discriminatorByDerivedType,
        Type? defaultDerivedType)
        : base(type, options)
    {
        _baseTypeInfo = baseTypeInfo;
        _discriminatorPropertyName = discriminatorPropertyName;
        _unknownDerivedTypeHandling = unknownDerivedTypeHandling;
        _derivedTypeByDiscriminator = derivedTypeByDiscriminator;
        _discriminatorByDerivedType = discriminatorByDerivedType;
        _defaultDerivedType = defaultDerivedType;
    }

    public static TomlTypeInfo TryWrap(TomlTypeInfo typeInfo)
    {
        return TryCreate(typeInfo.Type, typeInfo.Options, typeInfo) ?? typeInfo;
    }

    public static TomlTypeInfo? TryCreate(Type type, TomlSerializerOptions options, TomlTypeInfo? baseTypeInfo)
    {
        ArgumentGuard.ThrowIfNull(type, nameof(type));
        ArgumentGuard.ThrowIfNull(options, nameof(options));

        if (baseTypeInfo is TomlPolymorphicTypeInfo)
        {
            return baseTypeInfo;
        }

        if (!type.IsClass && !type.IsInterface)
        {
            return null;
        }

        var tomlDerivedAttributes = type.GetCustomAttributes<TomlDerivedTypeAttribute>(inherit: false).ToArray();
        var jsonDerivedAttributes = type.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false).ToArray();
        var hasRuntimeMappings = options.PolymorphismOptions.DerivedTypeMappings.TryGetValue(type, out var runtimeDerivedTypes) &&
                                 runtimeDerivedTypes.Count > 0;

        if (tomlDerivedAttributes.Length == 0 && jsonDerivedAttributes.Length == 0 && !hasRuntimeMappings)
        {
            return null;
        }

        var tomlPolymorphicAttribute = type.GetCustomAttribute<TomlPolymorphicAttribute>(inherit: false);
        var jsonPolymorphicAttribute = type.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false);

        var discriminatorPropertyName =
            tomlPolymorphicAttribute?.TypeDiscriminatorPropertyName ??
            jsonPolymorphicAttribute?.TypeDiscriminatorPropertyName ??
            options.PolymorphismOptions.TypeDiscriminatorPropertyName;

        if (string.IsNullOrEmpty(discriminatorPropertyName))
        {
            throw new TomlException($"Polymorphic type '{type.FullName}' must specify a discriminator property name.");
        }

        var derivedByDiscriminator = new Dictionary<string, Type>(StringComparer.Ordinal);
        var discriminatorByDerived = new Dictionary<Type, string?>();
        Type? defaultDerivedType = null;

        foreach (var attr in tomlDerivedAttributes)
        {
            if (attr.Discriminator is null)
            {
                SetDefaultDerivedType(type, ref defaultDerivedType, discriminatorByDerived, attr.DerivedType);
            }
            else
            {
                AddDerivedType(type, derivedByDiscriminator, discriminatorByDerived, attr.DerivedType, attr.Discriminator);
            }
        }

        foreach (var attr in jsonDerivedAttributes)
        {
            var discriminator = attr.TypeDiscriminator;
            if (discriminator is null)
            {
                ValidateDefaultDerivedType(type, attr.DerivedType);
                if (ShouldAddLowerPrecedenceMapping(attr.DerivedType, discriminator: null, defaultDerivedType, derivedByDiscriminator, discriminatorByDerived))
                {
                    SetDefaultDerivedType(type, ref defaultDerivedType, discriminatorByDerived, attr.DerivedType);
                }
                continue;
            }

            var discriminatorText = discriminator switch
            {
                string s => s,
                IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                _ => discriminator.ToString() ?? string.Empty,
            };

            ValidateDerivedType(type, attr.DerivedType, discriminatorText);
            if (ShouldAddLowerPrecedenceMapping(attr.DerivedType, discriminatorText, defaultDerivedType, derivedByDiscriminator, discriminatorByDerived))
            {
                AddDerivedType(type, derivedByDiscriminator, discriminatorByDerived, attr.DerivedType, discriminatorText);
            }
        }

        if (hasRuntimeMappings)
        {
            foreach (var entry in runtimeDerivedTypes!)
            {
                if (entry.Discriminator is null)
                {
                    ValidateDefaultDerivedType(type, entry.DerivedType);
                    if (ShouldAddLowerPrecedenceMapping(entry.DerivedType, discriminator: null, defaultDerivedType, derivedByDiscriminator, discriminatorByDerived))
                    {
                        SetDefaultDerivedType(type, ref defaultDerivedType, discriminatorByDerived, entry.DerivedType);
                    }

                    continue;
                }

                ValidateDerivedType(type, entry.DerivedType, entry.Discriminator);
                if (ShouldAddLowerPrecedenceMapping(entry.DerivedType, entry.Discriminator, defaultDerivedType, derivedByDiscriminator, discriminatorByDerived))
                {
                    AddDerivedType(type, derivedByDiscriminator, discriminatorByDerived, entry.DerivedType, entry.Discriminator);
                }
            }
        }

        if (derivedByDiscriminator.Count == 0 && defaultDerivedType is null)
        {
            return null;
        }

        // Priority chain: TomlPolymorphicAttribute → JsonPolymorphicAttribute → options
        var unknownHandling = options.PolymorphismOptions.UnknownDerivedTypeHandling;
        if (tomlPolymorphicAttribute is not null &&
            tomlPolymorphicAttribute.UnknownDerivedTypeHandling != TomlUnknownDerivedTypeHandling.Unspecified)
        {
            unknownHandling = tomlPolymorphicAttribute.UnknownDerivedTypeHandling;
        }
        else if (jsonPolymorphicAttribute is not null)
        {
            unknownHandling = jsonPolymorphicAttribute.UnknownDerivedTypeHandling switch
            {
                JsonUnknownDerivedTypeHandling.FallBackToBaseType => TomlUnknownDerivedTypeHandling.FallBackToBaseType,
                _ => TomlUnknownDerivedTypeHandling.Fail,
            };
        }
        return new TomlPolymorphicTypeInfo(
            type,
            options,
            baseTypeInfo,
            discriminatorPropertyName,
            unknownHandling,
            derivedByDiscriminator,
            discriminatorByDerived,
            defaultDerivedType);
    }

    private static void AddDerivedType(
        Type baseType,
        Dictionary<string, Type> derivedTypeByDiscriminator,
        Dictionary<Type, string?> discriminatorByDerivedType,
        Type derivedType,
        string discriminator)
    {
        ValidateDerivedType(baseType, derivedType, discriminator);

        if (derivedTypeByDiscriminator.ContainsKey(discriminator))
        {
            throw new TomlException($"Multiple derived types are registered with discriminator '{discriminator}' for base '{baseType.FullName}'.");
        }

        if (discriminatorByDerivedType.ContainsKey(derivedType))
        {
            throw new TomlException($"Derived type '{derivedType.FullName}' is registered multiple times for base '{baseType.FullName}'.");
        }

        derivedTypeByDiscriminator.Add(discriminator, derivedType);
        discriminatorByDerivedType.Add(derivedType, discriminator);
    }

    private static void SetDefaultDerivedType(
        Type baseType,
        ref Type? defaultDerivedType,
        Dictionary<Type, string?> discriminatorByDerivedType,
        Type derivedType)
    {
        ValidateDefaultDerivedType(baseType, derivedType);

        if (defaultDerivedType is not null)
        {
            throw new TomlException($"Multiple default derived types are registered for base '{baseType.FullName}'.");
        }

        if (discriminatorByDerivedType.ContainsKey(derivedType))
        {
            throw new TomlException($"Derived type '{derivedType.FullName}' is registered multiple times for base '{baseType.FullName}'.");
        }

        defaultDerivedType = derivedType;
        discriminatorByDerivedType.Add(derivedType, null);
    }

    private static void ValidateDefaultDerivedType(Type baseType, Type derivedType)
    {
        ArgumentGuard.ThrowIfNull(derivedType, nameof(derivedType));

        if (!baseType.IsAssignableFrom(derivedType))
        {
            throw new TomlException($"Derived type '{derivedType.FullName}' is not assignable to base type '{baseType.FullName}'.");
        }
    }

    private static void ValidateDerivedType(Type baseType, Type derivedType, string discriminator)
    {
        ValidateDefaultDerivedType(baseType, derivedType);
        ArgumentGuard.ThrowIfNull(discriminator, nameof(discriminator));

        if (discriminator.Length == 0)
        {
            throw new TomlException($"Derived type discriminator for base '{baseType.FullName}' cannot be empty.");
        }
    }

    private static bool ShouldAddLowerPrecedenceMapping(
        Type derivedType,
        string? discriminator,
        Type? defaultDerivedType,
        Dictionary<string, Type> derivedTypeByDiscriminator,
        Dictionary<Type, string?> discriminatorByDerivedType)
    {
        if (discriminatorByDerivedType.ContainsKey(derivedType))
        {
            return false;
        }

        if (discriminator is null)
        {
            return defaultDerivedType is null;
        }

        return !derivedTypeByDiscriminator.ContainsKey(discriminator);
    }

    public override void Write(TomlWriter writer, object? value)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        if (value is null)
        {
            throw new TomlException("TOML does not support null values.");
        }

        var runtimeType = value.GetType();
        if (runtimeType == Type)
        {
            if (_baseTypeInfo is null)
            {
                throw new TomlException($"Type '{Type.FullName}' is polymorphic and requires a discriminator to serialize derived types.");
            }

            _baseTypeInfo.Write(writer, value);
            return;
        }

        if (!_discriminatorByDerivedType.TryGetValue(runtimeType, out var discriminator))
        {
            if (_unknownDerivedTypeHandling == TomlUnknownDerivedTypeHandling.FallBackToBaseType)
            {
                if (_baseTypeInfo is null)
                {
                    throw new TomlException($"Type '{runtimeType.FullName}' is not registered as a derived type for '{Type.FullName}'.");
                }

                _baseTypeInfo.Write(writer, value);
                return;
            }

            throw new TomlException($"Type '{runtimeType.FullName}' is not registered as a derived type for '{Type.FullName}'.");
        }

        var runtimeTypeInfo = writer.ResolveTypeInfo(runtimeType);

        // Default derived type (null discriminator) - serialize without discriminator
        if (discriminator is null)
        {
            runtimeTypeInfo.Write(writer, value);
            return;
        }

        var tempWriter = new TomlWriter(TextWriter.Null, Options, writer.OperationState);
        tempWriter.WriteStartDocument();
        runtimeTypeInfo.Write(tempWriter, value);

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

    public override object? ReadAsObject(TomlReader reader)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));

        if (reader.TokenType != TomlTokenType.StartTable)
        {
            if (_baseTypeInfo is null)
            {
                throw reader.CreateException($"Expected {TomlTokenType.StartTable} token but was {reader.TokenType}.");
            }

            return _baseTypeInfo.ReadAsObject(reader);
        }

        var buffer = reader.CaptureCurrentValueToBuffer();
        if (!TryReadDiscriminator(buffer, out var discriminator, out var discriminatorSpan))
        {
            // No discriminator found - try default derived type first
            if (_defaultDerivedType is not null)
            {
                var defaultTypeInfo = reader.ResolveTypeInfo(_defaultDerivedType);
                var defaultReader = TomlReader.Create(buffer);
                defaultReader.Read(); // StartDocument
                defaultReader.Read(); // value start
                return defaultTypeInfo.ReadAsObject(defaultReader);
            }

            if (Type.IsInterface || Type.IsAbstract)
            {
                var span = GetTableStartSpan(buffer);
                if (span is { } tableSpan)
                {
                    throw new TomlException(tableSpan, $"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{Type.FullName}'.");
                }

                throw new TomlException($"Missing discriminator key '{_discriminatorPropertyName}' when deserializing '{Type.FullName}'.");
            }

            var fallbackReader = TomlReader.Create(buffer);
            fallbackReader.Read(); // StartDocument
            fallbackReader.Read(); // value start
            return _baseTypeInfo!.ReadAsObject(fallbackReader);
        }

        TomlTypeInfo targetTypeInfo;
        string? propertyNameToFilter = _discriminatorPropertyName;

        if (_derivedTypeByDiscriminator.TryGetValue(discriminator, out var derivedType))
        {
            targetTypeInfo = reader.ResolveTypeInfo(derivedType);
        }
        else if (_defaultDerivedType is not null)
        {
            // Unknown discriminator - use default derived type
            targetTypeInfo = reader.ResolveTypeInfo(_defaultDerivedType);
        }
        else if (_unknownDerivedTypeHandling == TomlUnknownDerivedTypeHandling.FallBackToBaseType && !Type.IsInterface && !Type.IsAbstract)
        {
            if (_baseTypeInfo is null)
            {
                throw new TomlException($"Unknown discriminator '{discriminator}' when deserializing '{Type.FullName}'.");
            }

            targetTypeInfo = _baseTypeInfo;
        }
        else
        {
            if (discriminatorSpan is { } span)
            {
                throw new TomlException(span, $"Unknown discriminator '{discriminator}' when deserializing '{Type.FullName}'.");
            }

            throw new TomlException($"Unknown discriminator '{discriminator}' when deserializing '{Type.FullName}'.");
        }

        var payloadReader = TomlReader.Create(buffer, propertyNameToFilter);
        payloadReader.Read(); // StartDocument
        payloadReader.Read(); // value start
        return targetTypeInfo.ReadAsObject(payloadReader);
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

    private static TomlSourceSpan? GetTableStartSpan(TomlReaderBuffer buffer)
    {
        var reader = TomlReader.Create(buffer);
        reader.Read(); // StartDocument
        reader.Read(); // value start
        return reader.CurrentSpan;
    }
}
