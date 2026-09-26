using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text.Json.Serialization;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Serialization.Converters;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal static class TomlTypeInfoResolverPipeline
{
    internal const string ReflectionBasedSerializationMessage =
        "Reflection-based TOML serialization is not compatible with trimming/NativeAOT. " +
        "Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.";
    private const string ReflectionSwitchName = TomlSerializerFeatureSwitches.ReflectionSwitchName;

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static TomlTypeInfo Resolve(TomlSerializerOptions options, Type type)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        return Resolve(new TomlSerializationOperationState(options), type);
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static TomlTypeInfo Resolve(TomlSerializationOperationState state, Type type)
    {
        ArgumentGuard.ThrowIfNull(state, nameof(state));
        ArgumentGuard.ThrowIfNull(type, nameof(type));

        if (state.TryGetCachedTypeInfo(type, out var cached))
        {
            return cached;
        }

        var resolved = ResolveUncached(state, type, out var errorMessage) ?? throw new TomlException(errorMessage!);
        state.CacheTypeInfo(type, resolved);
        return resolved;
    }

    /// <summary>
    /// Resolves the metadata of a type, or returns <see langword="null"/> when no metadata is available.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static TomlTypeInfo? TryResolve(TomlSerializerOptions options, Type type)
    {
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(type, nameof(type));

        var state = new TomlSerializationOperationState(options);
        TomlTypeInfo? resolved;
        try
        {
            resolved = ResolveUncached(state, type, out _);
        }
        catch (TomlException)
        {
            // Reflection throws when it cannot create metadata for the type, for example for a delegate
            return null;
        }

        if (resolved is not null)
        {
            state.CacheTypeInfo(type, resolved);
        }

        return resolved;
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlTypeInfo? ResolveUncached(TomlSerializationOperationState state, Type type, out string? errorMessage)
    {
        errorMessage = null;
        var options = state.Options;
        var fromTypeConverterAttribute = TryResolveFromConverterAttributes(options, type);
        if (fromTypeConverterAttribute is not null)
        {
            return fromTypeConverterAttribute;
        }

        var fromConverters = TryResolveFromConverters(options, type);
        if (fromConverters is not null)
        {
            return fromConverters;
        }

        var fromResolver = options.TypeInfoResolver?.GetTypeInfo(type, options);
        if (fromResolver is not null)
        {
            return TomlPolymorphicTypeInfo.TryWrap(fromResolver);
        }

        var builtIn = TomlBuiltInTypeInfoResolver.GetTypeInfo(type, options);
        if (builtIn is not null)
        {
            return TomlPolymorphicTypeInfo.TryWrap(builtIn);
        }

        var nullable = TryResolveNullable(type, state);
        if (nullable is not null)
        {
            return nullable;
        }

        if (IsNonStringKeyDictionary(type))
        {
            errorMessage = $"Dictionaries must have string keys to be representable as TOML tables. Type '{type.FullName}' is not supported without a custom converter.";
            return null;
        }

        if (TomlSerializer.IsReflectionEnabledByDefault)
        {
            var typeInfo = ResolveFromReflection(options, type);
            return TomlPolymorphicTypeInfo.TryWrap(typeInfo);
        }

        var polymorphicDispatch = TomlPolymorphicTypeInfo.TryCreate(type, options, baseTypeInfo: null);
        if (polymorphicDispatch is not null)
        {
            return polymorphicDispatch;
        }

        errorMessage =
            $"Reflection serialization is disabled and no TOML metadata was found for type '{type.FullName}'. " +
            $"Provide {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.TypeInfoResolver)} (source generation) or a custom resolver, " +
            $"or enable the '{ReflectionSwitchName}' AppContext switch.";
        return null;
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlUntypedNullableTypeInfo? TryResolveNullable(Type type, TomlSerializationOperationState state)
    {
        var underlyingType = Nullable.GetUnderlyingType(type);
        if (underlyingType is null)
        {
            return null;
        }

        var inner = Resolve(state, underlyingType);
        return new TomlUntypedNullableTypeInfo(type, state.Options, inner);
    }

    private static bool IsNonStringKeyDictionary([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.Interfaces)] Type type)
    {
        if (type == typeof(string))
        {
            return false;
        }

        var nonGenericDictionary = typeof(IDictionary).IsAssignableFrom(type);
        var hasStringKeyDictionaryInterface = false;

        // Scan the type itself and its interfaces for generic dictionary shapes.
        var interfaces = type.GetInterfaces();
        for (var i = -1; i < interfaces.Length; i++)
        {
            var iface = i < 0 ? type : interfaces[i];
            if (!iface.IsGenericType)
            {
                continue;
            }

            var definition = iface.GetGenericTypeDefinition();
            if (definition != typeof(System.Collections.Generic.IDictionary<,>) &&
                definition != typeof(System.Collections.Generic.IReadOnlyDictionary<,>))
            {
                continue;
            }

            var args = iface.GetGenericArguments();
            if (args.Length != 2)
            {
                continue;
            }

            if (args[0] == typeof(string))
            {
                hasStringKeyDictionaryInterface = true;
                continue;
            }

            return true;
        }

        // If the type is a non-generic IDictionary but does not expose a string-key generic dictionary interface,
        // treat it as non-string-key and reject by default.
        return nonGenericDictionary && !hasStringKeyDictionaryInterface;
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static ConverterTomlTypeInfo? TryResolveFromConverterAttributes(TomlSerializerOptions options, Type type)
    {
        var tomlConverterAttribute = type.GetCustomAttribute<TomlConverterAttribute>(inherit: true);
        if (tomlConverterAttribute is not null)
        {
            var converter = CreateConverterFromAttribute(tomlConverterAttribute.ConverterType, type, options);
            return new ConverterTomlTypeInfo(type, options, converter);
        }

        var jsonConverterAttribute = type.GetCustomAttribute<JsonConverterAttribute>(inherit: true);
        if (jsonConverterAttribute is not null && jsonConverterAttribute.ConverterType is not null)
        {
            if (type.IsEnum && IsJsonStringEnumConverter(jsonConverterAttribute.ConverterType))
            {
                return new ConverterTomlTypeInfo(type, options, TomlStringEnumConverter.Instance);
            }

            var converter = CreateConverterFromAttribute(jsonConverterAttribute.ConverterType, type, options);
            return new ConverterTomlTypeInfo(type, options, converter);
        }

        return null;
    }

    private static bool IsJsonStringEnumConverter(Type converterType)
    {
        if (converterType.FullName == "System.Text.Json.Serialization.JsonStringEnumConverter")
        {
            return true;
        }

        return converterType.IsGenericType &&
            converterType.GetGenericTypeDefinition().FullName == "System.Text.Json.Serialization.JsonStringEnumConverter`1";
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlConverter CreateConverterFromAttribute(Type converterType, Type typeToConvert, TomlSerializerOptions options)
    {
        if (!typeof(TomlConverter).IsAssignableFrom(converterType))
        {
            throw new TomlException($"Converter type '{converterType.FullName}' must derive from '{typeof(TomlConverter).FullName}'.");
        }

        if (converterType.GetConstructor(Type.EmptyTypes) is null)
        {
            throw new TomlException($"Converter type '{converterType.FullName}' must declare a public parameterless constructor.");
        }

        TomlConverter converter;
        try
        {
            converter = (TomlConverter)Activator.CreateInstance(converterType)!;
        }
        catch (Exception ex)
        {
            throw new TomlException($"Failed to create converter '{converterType.FullName}'.", ex);
        }

        return ResolveAttributeConverter(converter, typeToConvert, options);
    }

    // Returns the converter to use for a converter declared with an attribute, creating it when it is a factory
    internal static TomlConverter ResolveAttributeConverter(TomlConverter converter, Type typeToConvert, TomlSerializerOptions options)
    {
        if (!converter.CanConvert(typeToConvert) && Nullable.GetUnderlyingType(typeToConvert) is { } underlyingType && converter.CanConvert(underlyingType))
        {
            return new TomlNullableConverter(ResolveAttributeConverter(converter, underlyingType, options), typeToConvert, underlyingType);
        }

        if (converter is TomlConverterFactory factory)
        {
            return CreateConverterFromFactory(factory, typeToConvert, options);
        }

        if (!converter.CanConvert(typeToConvert))
        {
            throw new TomlException($"Converter '{converter.GetType().FullName}' cannot convert '{typeToConvert.FullName}'.");
        }

        return converter;
    }

    private static TomlConverter CreateConverterFromFactory(TomlConverterFactory factory, Type typeToConvert, TomlSerializerOptions options)
    {
        var created = factory.CreateConverter(typeToConvert, options);
        if (created is null)
        {
            throw new TomlException($"The converter factory '{factory.GetType().FullName}' returned null.");
        }

        if (created is TomlConverterFactory)
        {
            throw new TomlException($"The converter factory '{factory.GetType().FullName}' returned another {nameof(TomlConverterFactory)}.");
        }

        if (!created.CanConvert(typeToConvert))
        {
            throw new TomlException(
                $"The converter factory '{factory.GetType().FullName}' returned a converter that cannot convert '{typeToConvert.FullName}'.");
        }

        return created;
    }

    internal static TomlTypeInfo? TryResolveFromConverters(TomlSerializerOptions options, Type type)
        => TryResolveFromConverters(options, type, ignoredConverterTypes: null);

    internal static TomlTypeInfo? TryResolveFromConverters(TomlSerializerOptions options, Type type, IReadOnlyList<Type>? ignoredConverterTypes)
    {
        var converters = options.Converters;
        if (converters.Count == 0)
        {
            return null;
        }

        for (var i = 0; i < converters.Count; i++)
        {
            var converter = converters[i];
            if (ShouldIgnoreConverter(converter, ignoredConverterTypes))
            {
                continue;
            }

            if (!converter.CanConvert(type))
            {
                continue;
            }

            if (converter is TomlConverterFactory factory)
            {
                converter = CreateConverterFromFactory(factory, type, options);
            }

            return new ConverterTomlTypeInfo(type, options, converter);
        }

        return null;
    }

    private static bool ShouldIgnoreConverter(TomlConverter converter, IReadOnlyList<Type>? ignoredConverterTypes)
    {
        if (ignoredConverterTypes is null || ignoredConverterTypes.Count == 0)
        {
            return false;
        }

        var converterType = converter.GetType();
        for (var i = 0; i < ignoredConverterTypes.Count; i++)
        {
            if (converterType == ignoredConverterTypes[i])
            {
                return true;
            }
        }

        return false;
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlTypeInfo ResolveFromReflection(TomlSerializerOptions options, Type type)
    {
        var typeInfo = TomlReflectionTypeInfoResolver.TryCreateTypeInfo(type, options);
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        var dispatch = TomlPolymorphicTypeInfo.TryCreate(type, options, baseTypeInfo: null);
        if (dispatch is not null)
        {
            return dispatch;
        }

        throw new TomlException(
            $"No TOML metadata is available for type '{type.FullName}'. " +
            $"Reflection-based metadata could not be generated for this type. " +
            $"Provide {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.TypeInfoResolver)} (source generation) or a custom resolver.");
    }

    private sealed class ConverterTomlTypeInfo : TomlTypeInfo
    {
        private readonly TomlConverter _converter;

        public ConverterTomlTypeInfo(Type type, TomlSerializerOptions options, TomlConverter converter)
            : base(type, options)
        {
            _converter = converter ?? throw new ArgumentNullException(nameof(converter));
        }

        public override void Write(TomlWriter writer, object? value)
        {
            ArgumentGuard.ThrowIfNull(writer, nameof(writer));
            _converter.Write(writer, value);
        }

        public override object? ReadAsObject(TomlReader reader)
        {
            ArgumentGuard.ThrowIfNull(reader, nameof(reader));
            return TomlConverterHelper.Read(reader, _converter, Type);
        }
    }
}
