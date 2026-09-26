using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using Tomlyn.Helpers;
using Tomlyn.Serialization;
using Tomlyn.Serialization.Internal;

namespace Tomlyn;

/// <summary>
/// Serializes and deserializes TOML payloads, following a <c>System.Text.Json</c>-style API shape.
/// </summary>
public static class TomlSerializer
{
    private const int InitialStringBuilderCapacity = 1024;

    [ThreadStatic]
    private static StringBuilder? s_cachedStringBuilder;

    private const string ReflectionBasedSerializationMessage =
        "Reflection-based TOML serialization is not compatible with trimming/NativeAOT. " +
        "Use a source-generated TomlSerializerContext or pass a TomlTypeInfo instance.";

    private static readonly Encoding DefaultStreamEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    /// <summary>
    /// Gets a value indicating whether reflection-based serialization is enabled by default.
    /// </summary>
    public static bool IsReflectionEnabledByDefault => TomlSerializerFeatureSwitches.IsReflectionEnabledByDefaultCalculated;

    /// <summary>
    /// Serializes a value into TOML text.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static string Serialize<T>(T value, TomlSerializerOptions? options = null)
        => Serialize((object?)value, typeof(T), options);

    /// <summary>
    /// Serializes a value into TOML text using generated metadata from a serializer context.
    /// </summary>
    public static string Serialize<T>(T value, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return Serialize(value, typeof(T), context);
    }

    /// <summary>
    /// Serializes a value into TOML text using an explicit input type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static string Serialize(object? value, Type inputType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var operationState = new TomlSerializationOperationState(effectiveOptions);
        var typeInfo = ResolveTypeInfo(operationState, inputType);
        return SerializeToString(value, typeInfo, operationState);
    }

    /// <summary>
    /// Serializes a value into TOML text using generated metadata from a serializer context.
    /// </summary>
    public static string Serialize(object? value, Type inputType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, inputType);
        return SerializeToString(value, typeInfo, new TomlSerializationOperationState(typeInfo.Options));
    }

    /// <summary>
    /// Serializes a value into TOML text using explicit metadata.
    /// </summary>
    public static string Serialize<T>(T value, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return SerializeToString(value, typeInfo, new TomlSerializationOperationState(typeInfo.Options));
    }

    /// <summary>
    /// Serializes a value to a writer using explicit metadata.
    /// </summary>
    public static void Serialize<T>(TextWriter writer, T value, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        Serialize(writer, (object?)value, (TomlTypeInfo)typeInfo);
    }

    /// <summary>
    /// Serializes a value into TOML text using explicit metadata.
    /// </summary>
    public static string Serialize(object? value, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return SerializeToString(value, typeInfo, new TomlSerializationOperationState(typeInfo.Options));
    }

    private static string SerializeToString(object? value, TomlTypeInfo typeInfo, TomlSerializationOperationState operationState)
    {
        var builder = s_cachedStringBuilder;
        if (builder is null)
        {
            builder = new StringBuilder(InitialStringBuilderCapacity);
            s_cachedStringBuilder = builder;
        }
        else
        {
            builder.Clear();
        }

        try
        {
            using (var writer = new StringWriter(builder, CultureInfo.InvariantCulture))
            {
                Serialize(writer, value, typeInfo, operationState);
            }

            return builder.ToString();
        }
        finally
        {
            builder.Clear();
        }
    }

    /// <summary>
    /// Serializes a value to a writer.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static void Serialize<T>(TextWriter writer, T value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        Serialize(writer, (object?)value, typeof(T), options);
    }

    /// <summary>
    /// Serializes a value to a writer using an explicit input type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static void Serialize(TextWriter writer, object? value, Type inputType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, inputType);
        Serialize(writer, value, typeInfo);
    }

    /// <summary>
    /// Serializes a value to a writer using generated metadata from a serializer context.
    /// </summary>
    public static void Serialize<T>(TextWriter writer, T value, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        Serialize(writer, (object?)value, typeof(T), context);
    }

    /// <summary>
    /// Serializes a value to a writer using an explicit input type and generated metadata from a serializer context.
    /// </summary>
    public static void Serialize(TextWriter writer, object? value, Type inputType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, inputType);
        Serialize(writer, value, typeInfo);
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static void Serialize<T>(Stream stream, T value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        Serialize(stream, (object?)value, typeof(T), options);
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and an explicit input type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static void Serialize(Stream stream, object? value, Type inputType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));

        using var writer = new StreamWriter(stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, inputType, options);
        writer.Flush();
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and generated metadata from a serializer context.
    /// </summary>
    public static void Serialize<T>(Stream stream, T value, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        Serialize(stream, (object?)value, typeof(T), context);
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding, an explicit input type, and generated metadata from a serializer context.
    /// </summary>
    public static void Serialize(Stream stream, object? value, Type inputType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        using var writer = new StreamWriter(stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, inputType, context);
        writer.Flush();
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static void Serialize(Stream stream, object? value, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        using var writer = new StreamWriter(stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, typeInfo);
        writer.Flush();
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static void Serialize<T>(Stream stream, T value, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        using var writer = new StreamWriter(stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, typeInfo);
        writer.Flush();
    }

    /// <summary>
    /// Deserializes a TOML payload from text.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static T? Deserialize<T>(string toml, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return (T?)Deserialize(toml, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from text using generated metadata from a serializer context.
    /// </summary>
    public static T? Deserialize<T>(string toml, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return (T?)Deserialize(toml, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from text using explicit metadata.
    /// </summary>
    public static T? Deserialize<T>(string toml, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return (T?)Deserialize(toml, (TomlTypeInfo)typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from text into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static object? Deserialize(string toml, Type returnType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));

        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return Deserialize(toml, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from text into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static object? Deserialize(string toml, Type returnType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, returnType);
        return Deserialize(toml, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static T? Deserialize<T>(TextReader reader, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return (T?)Deserialize(reader, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader using generated metadata from a serializer context.
    /// </summary>
    public static T? Deserialize<T>(TextReader reader, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return (T?)Deserialize(reader, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader using explicit metadata.
    /// </summary>
    public static T? Deserialize<T>(TextReader reader, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return (T?)Deserialize(reader, (TomlTypeInfo)typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static object? Deserialize(TextReader reader, Type returnType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));

        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return Deserialize(reader, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static object? Deserialize(TextReader reader, Type returnType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, returnType);
        return Deserialize(reader, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a text reader using explicit metadata.
    /// </summary>
    public static object? Deserialize(TextReader reader, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        var operationState = new TomlSerializationOperationState(typeInfo.Options);
        var tomlReader = TomlReader.Create(reader, typeInfo.Options, operationState);
        return DeserializeCore(tomlReader, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static T? Deserialize<T>(Stream stream, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return (T?)Deserialize(stream, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding and generated metadata from a serializer context.
    /// </summary>
    public static T? Deserialize<T>(Stream stream, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return (T?)Deserialize(stream, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static T? Deserialize<T>(Stream stream, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return (T?)Deserialize(stream, (TomlTypeInfo)typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static object? Deserialize(Stream stream, Type returnType, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));

        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return Deserialize(stream, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static object? Deserialize(Stream stream, Type returnType, TomlSerializerContext context)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));

        var typeInfo = ResolveTypeInfo(context, returnType);
        return Deserialize(stream, typeInfo);
    }

    /// <summary>
    /// Deserializes a TOML payload from a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static object? Deserialize(Stream stream, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        using var reader = new StreamReader(stream, DefaultStreamEncoding, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
        return Deserialize(reader, typeInfo);
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize<T>(string toml, [NotNullWhen(true)] out T? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        try
        {
            value = Deserialize<T>(toml, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize(string toml, Type returnType, [NotNullWhen(true)] out object? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        try
        {
            value = Deserialize(toml, returnType, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text using generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize<T>(string toml, TomlSerializerContext context, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize<T>(toml, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize(string toml, Type returnType, TomlSerializerContext context, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize(toml, returnType, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize<T>(TextReader reader, [NotNullWhen(true)] out T? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        try
        {
            value = Deserialize<T>(reader, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize(TextReader reader, Type returnType, [NotNullWhen(true)] out object? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        try
        {
            value = Deserialize(reader, returnType, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader using generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize<T>(TextReader reader, TomlSerializerContext context, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize<T>(reader, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize(TextReader reader, Type returnType, TomlSerializerContext context, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize(reader, returnType, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize<T>(Stream stream, [NotNullWhen(true)] out T? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        try
        {
            value = Deserialize<T>(stream, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static bool TryDeserialize(Stream stream, Type returnType, [NotNullWhen(true)] out object? value, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        try
        {
            value = Deserialize(stream, returnType, options)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding and generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize<T>(Stream stream, TomlSerializerContext context, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize<T>(stream, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static bool TryDeserialize(Stream stream, Type returnType, TomlSerializerContext context, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        try
        {
            value = Deserialize(stream, returnType, context)!;
            return true;
        }
        catch (TomlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Deserializes a TOML payload from text using explicit metadata.
    /// </summary>
    public static object? Deserialize(string toml, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        var operationState = new TomlSerializationOperationState(typeInfo.Options);
        var reader = TomlReader.Create(toml, typeInfo.Options, operationState);
        return DeserializeCore(reader, typeInfo);
    }

    private static object? DeserializeCore(TomlReader reader, TomlTypeInfo typeInfo)
    {
        object? value;
        try
        {
            reader.Read(); // StartDocument
            reader.Read(); // value start

            var options = typeInfo.Options;
            if (options.RootValueHandling == TomlRootValueHandling.WrapInRootKey)
            {
                if (reader.TokenType != TomlTokenType.StartTable)
                {
                    throw reader.CreateException($"Expected a TOML table at the document root but was {reader.TokenType}.");
                }

                reader.Read();
                while (reader.TokenType != TomlTokenType.EndTable)
                {
                    if (reader.TokenType != TomlTokenType.PropertyName)
                    {
                        throw reader.CreateException($"Expected {TomlTokenType.PropertyName} token but was {reader.TokenType}.");
                    }

                    var name = reader.PropertyName;
                    reader.Read();
                    if (string.Equals(name, options.RootValueKeyName, StringComparison.Ordinal))
                    {
                        value = typeInfo.ReadAsObject(reader);
                        ThrowIfDiagnostics(reader.OperationState);
                        return value;
                    }

                    reader.Skip();
                }

                throw reader.CreateException($"The root value key '{options.RootValueKeyName}' was not found.");
            }

            value = typeInfo.ReadAsObject(reader);
        }
        catch (TomlException ex) when (reader.OperationState.HasDiagnostics)
        {
            reader.OperationState.AddDiagnostics(ex);
            ThrowIfDiagnostics(reader.OperationState);
            throw;
        }

        ThrowIfDiagnostics(reader.OperationState);
        return value;
    }

    private static void ThrowIfDiagnostics(TomlSerializationOperationState operationState)
    {
        if (operationState.Diagnostics is { Count: > 0 } diagnostics)
        {
            throw new TomlException(diagnostics);
        }
    }

    /// <summary>
    /// Serializes a value to a writer using explicit metadata.
    /// </summary>
    public static void Serialize(TextWriter writer, object? value, TomlTypeInfo typeInfo)
        => Serialize(writer, value, typeInfo, new TomlSerializationOperationState(typeInfo.Options));

    private static void Serialize(TextWriter writer, object? value, TomlTypeInfo typeInfo, TomlSerializationOperationState operationState)
    {
        ArgumentGuard.ThrowIfNull(writer, nameof(writer));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        ArgumentGuard.ThrowIfNull(operationState, nameof(operationState));

        var options = typeInfo.Options;
        if (options.RootValueHandling != TomlRootValueHandling.WrapInRootKey &&
            options.MetadataStore is null &&
            typeInfo.Type == typeof(Tomlyn.Model.TomlTable) &&
            value is Tomlyn.Model.TomlTable rootTable)
        {
            Tomlyn.Serialization.Internal.TomlModelTextWriter.WriteDocument(writer, rootTable, options);
            return;
        }

        var tomlWriter = new TomlWriter(writer, options, operationState);
        tomlWriter.WriteStartDocument();

        if (options.RootValueHandling == TomlRootValueHandling.WrapInRootKey)
        {
            tomlWriter.WriteStartTable();
            tomlWriter.WritePropertyNameLiteral(options.RootValueKeyName);
            typeInfo.Write(tomlWriter, value);
            tomlWriter.WriteEndTable();
        }
        else
        {
            typeInfo.Write(tomlWriter, value);
        }

        tomlWriter.WriteEndDocument();
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlTypeInfo ResolveTypeInfo(TomlSerializerOptions options, Type type)
    {
        return ResolveTypeInfo(new TomlSerializationOperationState(options), type);
    }

    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    private static TomlTypeInfo ResolveTypeInfo(TomlSerializationOperationState operationState, Type type)
    {
        return operationState.ResolveTypeInfo(type);
    }

    private static TomlTypeInfo ResolveTypeInfo(TomlSerializerContext context, Type type)
    {
        var typeInfo = context.GetTypeInfo(type, context.Options);
        if (typeInfo is null)
        {
            throw new TomlException($"No generated metadata is available for type '{type.FullName}' in the provided context.");
        }

        return typeInfo;
    }
}
