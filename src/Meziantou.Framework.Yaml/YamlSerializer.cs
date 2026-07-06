using System.Buffers;
using Meziantou.Framework.Yaml.Serialization;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Serializes and deserializes YAML payloads, following a <c>System.Text.Json</c>-style API shape.
/// </summary>
public static class YamlSerializer
{
    private const string ReflectionSwitchName = YamlSerializerFeatureSwitches.ReflectionSwitchName;
    private static readonly bool ReflectionEnabledByDefault = YamlSerializerFeatureSwitches.IsReflectionEnabledByDefaultCalculated;
    private static readonly Encoding DefaultStreamEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    [ThreadStatic]
    private static StringBuilder? s_cachedStringBuilder;

    /// <summary>Gets a value indicating whether reflection-based serialization is enabled by default.</summary>
    public static bool IsReflectionEnabledByDefault => ReflectionEnabledByDefault;

    /// <summary>Serializes a value into YAML text.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>A YAML payload.</returns>
    public static string Serialize<T>(T value, YamlSerializerOptions? options = null)
    {
        return Serialize((object?)value, typeof(T), options);
    }

    /// <summary>Serializes a value into YAML text using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>A YAML payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static string Serialize<T>(T value, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Serialize(value, typeof(T), context);
    }

    /// <summary>Serializes a value into YAML text using an explicit input type.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>A YAML payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Reflection is disabled and no metadata is available from <see cref="YamlSerializerOptions.TypeInfoResolver"/>.
    /// </exception>
    public static string Serialize(object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, inputType);
        return SerializeCore(typeInfo, value);
    }

    /// <summary>Serializes a value into YAML text using generated metadata from a serializer context.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>A YAML payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputType"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="inputType"/> in <paramref name="context"/>.</exception>
    public static string Serialize(object? value, Type inputType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, inputType);
        return SerializeCore(typeInfo, value);
    }

    /// <summary>Serializes a value to a writer.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(TextWriter writer, T value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>Serializes a value to a writer using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static void Serialize<T>(TextWriter writer, T value, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>Serializes a value to a stream using UTF-8 encoding.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(Stream utf8Stream, T value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        using var writer = new StreamWriter(utf8Stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, options);
        writer.Flush();
    }

    /// <summary>Serializes a value to a stream using UTF-8 encoding and generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static void Serialize<T>(Stream utf8Stream, T value, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(context);

        using var writer = new StreamWriter(utf8Stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, context);
        writer.Flush();
    }

    /// <summary>Serializes a value to a writer using an explicit input type.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="inputType"/> is <see langword="null"/>.</exception>
    public static void Serialize(TextWriter writer, object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(inputType);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, inputType);
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>Serializes a value to a writer using generated metadata from a serializer context.</summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/>, <paramref name="inputType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="inputType"/> in <paramref name="context"/>.</exception>
    public static void Serialize(TextWriter writer, object? value, Type inputType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, inputType);
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>Serializes a value to a stream using UTF-8 encoding and an explicit input type.</summary>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="inputType"/> is <see langword="null"/>.</exception>
    public static void Serialize(Stream utf8Stream, object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        using var writer = new StreamWriter(utf8Stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, inputType, options);
        writer.Flush();
    }

    /// <summary>Serializes a value to a stream using UTF-8 encoding and generated metadata from a serializer context.</summary>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/>, <paramref name="inputType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="inputType"/> in <paramref name="context"/>.</exception>
    public static void Serialize(Stream utf8Stream, object? value, Type inputType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        using var writer = new StreamWriter(utf8Stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
        Serialize(writer, value, inputType, context);
        writer.Flush();
    }

    /// <summary>Deserializes a YAML payload from text.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(string yaml, YamlSerializerOptions? options = null)
    {
        return (T?)Deserialize(yaml, typeof(T), options);
    }

    /// <summary>Deserializes a YAML payload from text using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static T? Deserialize<T>(string yaml, YamlSerializerContext context)
    {
        return (T?)Deserialize(yaml, typeof(T), context);
    }

    /// <summary>Deserializes a YAML payload into an explicit destination type.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">
    /// Reflection is disabled and no metadata is available from <see cref="YamlSerializerOptions.TypeInfoResolver"/>.
    /// </exception>
    public static object? Deserialize(string yaml, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return DeserializeCore(typeInfo, yaml);
    }

    /// <summary>Deserializes a YAML payload into an explicit destination type using generated metadata from a serializer context.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="returnType"/> in <paramref name="context"/>.</exception>
    public static object? Deserialize(string yaml, Type returnType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, returnType);
        return DeserializeCore(typeInfo, yaml);
    }

    /// <summary>Attempts to deserialize a YAML payload from text.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(string yaml, out T? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        try
        {
            value = Deserialize<T>(yaml, options);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Attempts to deserialize a YAML payload from text using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(string yaml, YamlSerializerContext context, out T? value)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize<T>(yaml, context);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Attempts to deserialize a YAML payload into an explicit destination type.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(string yaml, Type returnType, out object? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);

        try
        {
            value = Deserialize(yaml, returnType, options);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Attempts to deserialize a YAML payload into an explicit destination type using generated metadata from a serializer context.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(string yaml, Type returnType, YamlSerializerContext context, out object? value)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize(yaml, returnType, context);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a text reader.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="reader">The source reader.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(TextReader reader, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return (T?)DeserializeCore(typeInfo, reader);
    }

    /// <summary>Attempts to deserialize YAML from a text reader.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="reader">The source reader.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(TextReader reader, out T? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        try
        {
            value = Deserialize<T>(reader, options);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a stream using UTF-8 encoding.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(Stream utf8Stream, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        using var reader = new StreamReader(utf8Stream, DefaultStreamEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return Deserialize<T>(reader, options);
    }

    /// <summary>Attempts to deserialize YAML from a stream using UTF-8 encoding.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(Stream utf8Stream, out T? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);

        try
        {
            value = Deserialize<T>(utf8Stream, options);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a text reader using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="reader">The source reader.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static T? Deserialize<T>(TextReader reader, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, typeof(T)) as YamlTypeInfo<T>;
        if (typeInfo is null)
        {
            throw new InvalidOperationException($"No generated metadata is available for type '{typeof(T)}' on context '{context.GetType()}'.");
        }

        return DeserializeCore(typeInfo, reader);
    }

    /// <summary>Attempts to deserialize YAML from a text reader using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="reader">The source reader.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(TextReader reader, YamlSerializerContext context, out T? value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize<T>(reader, context);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a stream using UTF-8 encoding and generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static T? Deserialize<T>(Stream utf8Stream, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(context);

        using var reader = new StreamReader(utf8Stream, DefaultStreamEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return Deserialize<T>(reader, context);
    }

    /// <summary>Attempts to deserialize YAML from a stream using UTF-8 encoding and generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(Stream utf8Stream, YamlSerializerContext context, out T? value)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize<T>(utf8Stream, context);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a text reader using an explicit destination type.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(TextReader reader, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(returnType);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return DeserializeCore(typeInfo, reader);
    }

    /// <summary>Attempts to deserialize YAML from a text reader using an explicit destination type.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(TextReader reader, Type returnType, out object? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(returnType);

        try
        {
            value = Deserialize(reader, returnType, options);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a stream using UTF-8 encoding.</summary>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(Stream utf8Stream, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(returnType);

        using var reader = new StreamReader(utf8Stream, DefaultStreamEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return Deserialize(reader, returnType, options);
    }

    /// <summary>Attempts to deserialize YAML from a stream using UTF-8 encoding and an explicit destination type.</summary>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(Stream utf8Stream, Type returnType, out object? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(returnType);

        try
        {
            value = Deserialize(utf8Stream, returnType, options);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a text reader using an explicit destination type and generated metadata from a serializer context.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="returnType"/> in <paramref name="context"/>.</exception>
    public static object? Deserialize(TextReader reader, Type returnType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, returnType);
        return DeserializeCore(typeInfo, reader);
    }

    /// <summary>Attempts to deserialize YAML from a text reader using an explicit destination type and generated metadata from a serializer context.</summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(TextReader reader, Type returnType, YamlSerializerContext context, out object? value)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize(reader, returnType, context);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }

    /// <summary>Deserializes YAML from a stream using UTF-8 encoding and generated metadata from a serializer context.</summary>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="returnType"/> in <paramref name="context"/>.</exception>
    public static object? Deserialize(Stream utf8Stream, Type returnType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        using var reader = new StreamReader(utf8Stream, DefaultStreamEncoding, detectEncodingFromByteOrderMarks: true, bufferSize: 1024, leaveOpen: true);
        return Deserialize(reader, returnType, context);
    }

    /// <summary>Attempts to deserialize YAML from a stream using UTF-8 encoding and an explicit destination type using generated metadata from a serializer context.</summary>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/>, <paramref name="returnType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(Stream utf8Stream, Type returnType, YamlSerializerContext context, out object? value)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize(utf8Stream, returnType, context);
            return true;
        }
        catch (YamlException)
        {
            value = null;
            return false;
        }
    }
    /// <summary>Deserializes YAML from a span of characters.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload as a span.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    public static T? Deserialize<T>(ReadOnlySpan<char> yaml, YamlSerializerOptions? options = null)
    {
        return Deserialize<T>(yaml.ToString(), options);
    }

    /// <summary>Deserializes YAML from a span of characters using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload as a span.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static T? Deserialize<T>(ReadOnlySpan<char> yaml, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Deserialize<T>(yaml.ToString(), context);
    }

    /// <summary>Deserializes YAML from a span of characters using an explicit destination type.</summary>
    /// <param name="yaml">The YAML payload as a span.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(ReadOnlySpan<char> yaml, Type returnType, YamlSerializerOptions? options = null)
    {
        return Deserialize(yaml.ToString(), returnType, options);
    }

    /// <summary>Deserializes YAML from a span of characters using an explicit destination type and generated metadata from a serializer context.</summary>
    /// <param name="yaml">The YAML payload as a span.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="returnType"/> in <paramref name="context"/>.</exception>
    public static object? Deserialize(ReadOnlySpan<char> yaml, Type returnType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);
        return Deserialize(yaml.ToString(), returnType, context);
    }

    /// <summary>Serializes a value using explicit type metadata.</summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <returns>A YAML payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static string Serialize<T>(T value, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return SerializeCore(typeInfo, value);
    }

    /// <summary>Deserializes a payload using explicit type metadata.</summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(string yaml, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, yaml);
    }

    /// <summary>Deserializes a payload using explicit type metadata from a character span.</summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="yaml">The YAML payload as a span.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(ReadOnlySpan<char> yaml, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, yaml.ToString());
    }

    /// <summary>
    /// Serializes a value to an <see cref="IBufferWriter{T}"/> destination.
    /// </summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="destination">The destination buffer writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(IBufferWriter<char> destination, T value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);

        var writer = new BufferWriterTextWriter(destination);
        Serialize(writer, value, options);
    }

    /// <summary>
    /// Serializes a value to an <see cref="IBufferWriter{T}"/> destination using generated metadata from a serializer context.
    /// </summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="destination">The destination buffer writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(IBufferWriter<char> destination, T value, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(context);

        var writer = new BufferWriterTextWriter(destination);
        Serialize(writer, value, context);
    }

    /// <summary>
    /// Serializes a value to an <see cref="IBufferWriter{T}"/> destination using an explicit input type.
    /// </summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/> or <paramref name="inputType"/> is <see langword="null"/>.</exception>
    public static void Serialize(IBufferWriter<char> destination, object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(inputType);

        var writer = new BufferWriterTextWriter(destination);
        Serialize(writer, value, inputType, options);
    }

    /// <summary>
    /// Serializes a value to an <see cref="IBufferWriter{T}"/> destination using an explicit input type and generated metadata from a serializer context.
    /// </summary>
    /// <param name="destination">The destination buffer writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <exception cref="ArgumentNullException"><paramref name="destination"/>, <paramref name="inputType"/>, or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static void Serialize(IBufferWriter<char> destination, object? value, Type inputType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(destination);
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(context);

        var writer = new BufferWriterTextWriter(destination);
        Serialize(writer, value, inputType, context);
    }

    private static void SerializeCore(YamlTypeInfo typeInfo, object? value, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(writer);

        var yamlWriter = new YamlWriter(writer, typeInfo.Options);
        typeInfo.Write(yamlWriter, value);
        if (!yamlWriter.EndsWithNewLine)
        {
            writer.Write('\n');
        }
    }

    private static string SerializeCore(YamlTypeInfo typeInfo, object? value)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var stringBuilder = AcquireStringBuilder(minimumCapacity: 1024);
        var writer = new YamlWriter(stringBuilder, typeInfo.Options);
        typeInfo.Write(writer, value);
        if (!writer.EndsWithNewLine)
        {
            stringBuilder.Append('\n');
        }

        return GetStringAndReleaseBuilder(stringBuilder);
    }

    private static string SerializeCore<T>(YamlTypeInfo<T> typeInfo, T value)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var stringBuilder = AcquireStringBuilder(minimumCapacity: 1024);
        var writer = new YamlWriter(stringBuilder, typeInfo.Options);
        typeInfo.Write(writer, value);
        if (!writer.EndsWithNewLine)
        {
            stringBuilder.Append('\n');
        }

        return GetStringAndReleaseBuilder(stringBuilder);
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, string yaml)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(yaml);

        var reader = YamlReader.Create(yaml, typeInfo.Options);
        if (!reader.Read())
        {
            return null;
        }

        return typeInfo.ReadAsObject(reader);
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, string yaml)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(yaml);

        var reader = YamlReader.Create(yaml, typeInfo.Options);
        if (!reader.Read())
        {
            return default;
        }

        return typeInfo.Read(reader);
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(reader);

        var yamlReader = YamlReader.Create(reader, typeInfo.Options);
        if (!yamlReader.Read())
        {
            return null;
        }

        return typeInfo.ReadAsObject(yamlReader);
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(reader);

        var yamlReader = YamlReader.Create(reader, typeInfo.Options);
        if (!yamlReader.Read())
        {
            return default;
        }

        return typeInfo.Read(yamlReader);
    }

    private static StringBuilder AcquireStringBuilder(int minimumCapacity)
    {
        var cached = s_cachedStringBuilder;
        if (cached is not null)
        {
            s_cachedStringBuilder = null;
            cached.Clear();
            if (cached.Capacity < minimumCapacity)
            {
                cached.EnsureCapacity(minimumCapacity);
            }

            return cached;
        }

        return new StringBuilder(minimumCapacity);
    }

    private static string GetStringAndReleaseBuilder(StringBuilder builder)
    {
        var result = builder.ToString();
        if (builder.Capacity <= 1024 * 1024)
        {
            builder.Clear();
            s_cachedStringBuilder = builder;
        }

        return result;
    }

    private static YamlTypeInfo ResolveTypeInfo(YamlSerializerContext context, Type requestedType)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requestedType);

        var typeInfo = context.GetTypeInfo(requestedType, context.Options);
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        throw new InvalidOperationException($"No generated metadata is available for '{requestedType}' on context '{context.GetType()}'.");
    }

    private static YamlTypeInfo ResolveTypeInfo(YamlSerializerOptions options, Type requestedType)
    {
        if (options.TypeInfoResolver is YamlSerializerContext context && !ReferenceEquals(options, context.Options))
        {
            // Options were created from context.CreateOptions(); resolve type info from the context
            // but use the caller's options for runtime behavior (SourceName, WriteIndented, etc.).
            var contextTypeInfo = context.GetTypeInfo(requestedType, context.Options);
            if (contextTypeInfo is not null)
            {
                return new YamlTypeInfoWithOptions(contextTypeInfo, options);
            }

            throw new InvalidOperationException($"No generated metadata is available for '{requestedType}' on context '{context.GetType()}'.");
        }

        var typeInfo = options.TypeInfoResolver?.GetTypeInfo(requestedType, options);
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        typeInfo = YamlBuiltInTypeInfoResolver.GetTypeInfo(requestedType, options);
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        if (IsReflectionEnabledByDefault)
        {
            typeInfo = ReflectionYamlTypeInfoResolver.Default.GetTypeInfo(requestedType, options);
            if (typeInfo is null)
            {
                throw new InvalidOperationException($"No metadata is available for '{requestedType}'.");
            }

            return typeInfo;
        }

        throw new InvalidOperationException(
            $"Reflection serialization is disabled and no metadata was found for '{requestedType}'. " +
            $"Provide metadata via {nameof(YamlSerializerOptions)}.{nameof(YamlSerializerOptions.TypeInfoResolver)} or enable the '{ReflectionSwitchName}' AppContext switch.");
    }
}
