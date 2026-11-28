using System.Buffers;
using Meziantou.Framework.Yaml.Serialization;
using Meziantou.Framework.Yaml.Serialization.References;

namespace Meziantou.Framework.Yaml;

/// <summary>
/// Serializes and deserializes YAML payloads, following a <c>System.Text.Json</c>-style API shape.
/// </summary>
public static class YamlSerializer
{
    private const string ReflectionSwitchName = YamlSerializerFeatureSwitches.ReflectionSwitchName;
    private static readonly bool ReflectionEnabledByDefault = YamlSerializerFeatureSwitches.IsReflectionEnabledByDefaultCalculated;
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

        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, options);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
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

        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, context);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
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

        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, inputType, options);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
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

        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, inputType, context);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Deserializes a YAML payload from text.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(string yaml, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(yaml);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return DeserializeAs<T>(typeInfo, yaml);
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
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return DeserializeAs<T>(typeInfo, yaml);
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
        return DeserializeAs<T>(typeInfo, reader);
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

    /// <summary>Deserializes a YAML payload from UTF-8 bytes.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <remarks>The bytes are read as is, without being decoded to a string first.</remarks>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Yaml, YamlSerializerOptions? options = null)
    {
        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return DeserializeAs<T>(typeInfo, utf8Yaml);
    }

    /// <summary>Attempts to deserialize a YAML payload from UTF-8 bytes.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    public static bool TryDeserialize<T>(ReadOnlySpan<byte> utf8Yaml, out T? value, YamlSerializerOptions? options = null)
    {
        try
        {
            value = Deserialize<T>(utf8Yaml, options);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes a YAML payload from UTF-8 bytes using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Yaml, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return DeserializeAs<T>(typeInfo, utf8Yaml);
    }

    /// <summary>Attempts to deserialize a YAML payload from UTF-8 bytes using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="default"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize<T>(ReadOnlySpan<byte> utf8Yaml, YamlSerializerContext context, out T? value)
    {
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize<T>(utf8Yaml, context);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes a YAML payload from UTF-8 bytes into an explicit destination type.</summary>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(ReadOnlySpan<byte> utf8Yaml, Type returnType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(returnType);

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return DeserializeCore(typeInfo, utf8Yaml);
    }

    /// <summary>Attempts to deserialize a YAML payload from UTF-8 bytes into an explicit destination type.</summary>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(ReadOnlySpan<byte> utf8Yaml, Type returnType, out object? value, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(returnType);

        try
        {
            value = Deserialize(utf8Yaml, returnType, options);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes a YAML payload from UTF-8 bytes into an explicit destination type using generated metadata from a serializer context.</summary>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="returnType"/> in <paramref name="context"/>.</exception>
    public static object? Deserialize(ReadOnlySpan<byte> utf8Yaml, Type returnType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        var typeInfo = ResolveTypeInfo(context, returnType);
        return DeserializeCore(typeInfo, utf8Yaml);
    }

    /// <summary>Attempts to deserialize a YAML payload from UTF-8 bytes into an explicit destination type using generated metadata from a serializer context.</summary>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="returnType">The destination CLR type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <param name="value">The deserialized value when successful; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when deserialization succeeded; otherwise <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="returnType"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    public static bool TryDeserialize(ReadOnlySpan<byte> utf8Yaml, Type returnType, YamlSerializerContext context, out object? value)
    {
        ArgumentNullException.ThrowIfNull(returnType);
        ArgumentNullException.ThrowIfNull(context);

        try
        {
            value = Deserialize(utf8Yaml, returnType, context);
            return true;
        }
        catch (YamlException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>Deserializes a YAML payload from UTF-8 bytes using explicit type metadata.</summary>
    /// <typeparam name="T">The destination CLR type.</typeparam>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="typeInfo">The metadata for <typeparamref name="T"/>.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(ReadOnlySpan<byte> utf8Yaml, YamlTypeInfo<T> typeInfo)
    {
        return DeserializeCore(typeInfo, utf8Yaml);
    }

    /// <summary>Deserializes a YAML payload from UTF-8 bytes using explicit type metadata.</summary>
    /// <param name="utf8Yaml">The YAML payload, encoded in UTF-8. A leading byte order mark is allowed.</param>
    /// <param name="typeInfo">The metadata for the destination type.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(ReadOnlySpan<byte> utf8Yaml, YamlTypeInfo typeInfo)
    {
        return DeserializeCore(typeInfo, utf8Yaml);
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

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, typeof(T));
        return DeserializeAs<T>(typeInfo, utf8Stream);
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

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return DeserializeAs<T>(typeInfo, reader);
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

        var typeInfo = ResolveTypeInfo(context, typeof(T));
        return DeserializeAs<T>(typeInfo, utf8Stream);
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

        var effectiveOptions = options ?? YamlSerializerOptions.Default;
        var typeInfo = ResolveTypeInfo(effectiveOptions, returnType);
        return DeserializeCore(typeInfo, utf8Stream);
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

        var typeInfo = ResolveTypeInfo(context, returnType);
        return DeserializeCore(typeInfo, utf8Stream);
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

    /// <summary>
    /// Serializes a value using explicit type metadata.
    /// </summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <returns>A YAML payload.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static string Serialize(object? value, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return SerializeCore(typeInfo, value);
    }

    /// <summary>
    /// Serializes a value to a writer using explicit type metadata.
    /// </summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(TextWriter writer, T value, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(typeInfo);
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>
    /// Serializes a value to a writer using explicit type metadata.
    /// </summary>
    /// <param name="writer">The destination writer.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <exception cref="ArgumentNullException"><paramref name="writer"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static void Serialize(TextWriter writer, object? value, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(typeInfo);
        SerializeCore(typeInfo, value, writer);
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit type metadata.
    /// </summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static void Serialize<T>(Stream utf8Stream, T value, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit type metadata.
    /// </summary>
    /// <param name="utf8Stream">The destination stream.</param>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static void Serialize(Stream utf8Stream, object? value, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        var writer = Utf8TextWriter.Rent(utf8Stream);
        try
        {
            Serialize(writer, value, typeInfo);
            writer.Flush();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <remarks>
    /// The bytes are the ones that <see cref="Serialize{T}(Stream, T, YamlSerializerOptions?)"/> writes. The text is encoded
    /// in pooled buffers: the returned array is the only allocation of the encoding, and there is no intermediate string.
    /// </remarks>
    /// <example>
    /// <code xmlns:ref="urn:meziantou:file-referencer" ref:source="../../tests/Meziantou.Framework.Yaml.Tests/Serialization/YamlSerializerApiTests.cs" ref:region="YamlSerializer_SerializeToUtf8Bytes" ref:dedent="true">
    /// var utf8Yaml = YamlSerializer.SerializeToUtf8Bytes(new Product
    /// {
    ///     Id = 1,
    ///     DisplayName = "Café",
    ///     Tags = ["new"],
    /// });
    ///
    /// Assert.Equal("""
    ///     Id: 1
    ///     name: Café
    ///     Tags:
    ///       - new
    ///
    ///     """u8.ToArray(), utf8Yaml);
    ///
    /// var product = YamlSerializer.Deserialize&lt;Product&gt;(utf8Yaml);
    /// Assert.Equal("Café", product!.DisplayName);
    /// </code>
    /// </example>
    public static byte[] SerializeToUtf8Bytes<T>(T value, YamlSerializerOptions? options = null)
    {
        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, options);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes using generated metadata from a serializer context.</summary>
    /// <typeparam name="T">The CLR type to serialize.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <typeparamref name="T"/> in <paramref name="context"/>.</exception>
    public static byte[] SerializeToUtf8Bytes<T>(T value, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, context);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes using an explicit input type.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="options">The serializer options. If <see langword="null"/>, <see cref="YamlSerializerOptions.Default"/> is used.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputType"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeToUtf8Bytes(object? value, Type inputType, YamlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(inputType);

        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, inputType, options);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes using an explicit input type and generated metadata from a serializer context.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="inputType">The declared input type.</param>
    /// <param name="context">The source-generated serializer context.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="inputType"/> or <paramref name="context"/> is <see langword="null"/>.</exception>
    /// <exception cref="InvalidOperationException">No generated metadata is available for <paramref name="inputType"/> in <paramref name="context"/>.</exception>
    public static byte[] SerializeToUtf8Bytes(object? value, Type inputType, YamlSerializerContext context)
    {
        ArgumentNullException.ThrowIfNull(inputType);
        ArgumentNullException.ThrowIfNull(context);

        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, inputType, context);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes using explicit type metadata.</summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeToUtf8Bytes<T>(T value, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, typeInfo);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Serializes a value to UTF-8 bytes using explicit type metadata.</summary>
    /// <param name="value">The value to serialize.</param>
    /// <param name="typeInfo">The metadata used for serialization.</param>
    /// <returns>The YAML document encoded in UTF-8, without byte order mark.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static byte[] SerializeToUtf8Bytes(object? value, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);

        var writer = Utf8TextWriter.RentForBytes();
        try
        {
            Serialize(writer, value, typeInfo);
            writer.Flush();
            return writer.ToArray();
        }
        finally
        {
            Utf8TextWriter.Return(writer);
        }
    }

    /// <summary>Deserializes a payload using explicit type metadata.</summary>
    /// <param name="yaml">The YAML payload.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="yaml"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(string yaml, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(yaml);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, yaml);
    }

    /// <summary>
    /// Deserializes a payload from a text reader using explicit type metadata.
    /// </summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="reader">The source reader.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(TextReader reader, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, reader);
    }

    /// <summary>
    /// Deserializes a payload from a text reader using explicit type metadata.
    /// </summary>
    /// <param name="reader">The source reader.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(TextReader reader, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, reader);
    }

    /// <summary>
    /// Deserializes a payload from a stream using UTF-8 encoding and explicit type metadata.
    /// </summary>
    /// <typeparam name="T">The represented CLR type.</typeparam>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static T? Deserialize<T>(Stream utf8Stream, YamlTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, utf8Stream);
    }

    /// <summary>
    /// Deserializes a payload from a stream using UTF-8 encoding and explicit type metadata.
    /// </summary>
    /// <param name="utf8Stream">The source stream.</param>
    /// <param name="typeInfo">The metadata used for deserialization.</param>
    /// <returns>The deserialized value.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="utf8Stream"/> or <paramref name="typeInfo"/> is <see langword="null"/>.</exception>
    public static object? Deserialize(Stream utf8Stream, YamlTypeInfo typeInfo)
    {
        ArgumentNullException.ThrowIfNull(utf8Stream);
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, utf8Stream);
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

        var writer = BufferWriterTextWriterCache.RentWriter(destination);
        try
        {
            Serialize(writer, value, options);
        }
        finally
        {
            BufferWriterTextWriterCache.ReturnWriter(writer);
        }
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

        var writer = BufferWriterTextWriterCache.RentWriter(destination);
        try
        {
            Serialize(writer, value, context);
        }
        finally
        {
            BufferWriterTextWriterCache.ReturnWriter(writer);
        }
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

        var writer = BufferWriterTextWriterCache.RentWriter(destination);
        try
        {
            Serialize(writer, value, inputType, options);
        }
        finally
        {
            BufferWriterTextWriterCache.ReturnWriter(writer);
        }
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

        var writer = BufferWriterTextWriterCache.RentWriter(destination);
        try
        {
            Serialize(writer, value, inputType, context);
        }
        finally
        {
            BufferWriterTextWriterCache.ReturnWriter(writer);
        }
    }

    private static void SerializeCore(YamlTypeInfo typeInfo, object? value, TextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(writer);

        var yamlWriter = CreateWriter(typeInfo, value, writer);
        typeInfo.Write(yamlWriter, value);
        if (!yamlWriter.EndsWithNewLine)
        {
            writer.Write('\n');
        }
    }

    private static YamlWriter CreateWriter(YamlTypeInfo typeInfo, object? value, TextWriter writer)
    {
        if (typeInfo.Options.ReferenceHandling != YamlReferenceHandling.PreserveMinimal)
        {
            return new YamlWriter(writer, typeInfo.Options);
        }

        var referenceWriter = new YamlReferenceWriter(collectReferences: true);
        var collectingWriter = new YamlWriter(TextWriter.Null, typeInfo.Options, referenceWriter);
        typeInfo.Write(collectingWriter, value);
        referenceWriter.CompleteReferenceCollection();
        return new YamlWriter(writer, typeInfo.Options, referenceWriter);
    }

    private static string SerializeCore(YamlTypeInfo typeInfo, object? value)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        var stringBuilder = AcquireStringBuilder(minimumCapacity: 1024);
        var writer = typeInfo.Options.ReferenceHandling == YamlReferenceHandling.PreserveMinimal
            ? CreateWriter(typeInfo, value, new StringWriter(stringBuilder, System.Globalization.CultureInfo.InvariantCulture))
            : new YamlWriter(stringBuilder, typeInfo.Options);
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
        var writer = typeInfo.Options.ReferenceHandling == YamlReferenceHandling.PreserveMinimal
            ? CreateWriter(typeInfo, value, new StringWriter(stringBuilder, System.Globalization.CultureInfo.InvariantCulture))
            : new YamlWriter(stringBuilder, typeInfo.Options);
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
        return DeserializeCore(typeInfo, new Utf8YamlTokenSource(yaml, typeInfo.Options.EffectiveMaxDepth, typeInfo.Options.SourceName));
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, string yaml)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(yaml);
        return DeserializeCore(typeInfo, new Utf8YamlTokenSource(yaml, typeInfo.Options.EffectiveMaxDepth, typeInfo.Options.SourceName));
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, ReadOnlySpan<byte> utf8Yaml)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, new Utf8YamlTokenSource(utf8Yaml, typeInfo.Options.EffectiveMaxDepth, typeInfo.Options.SourceName));
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, ReadOnlySpan<byte> utf8Yaml)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        return DeserializeCore(typeInfo, new Utf8YamlTokenSource(utf8Yaml, typeInfo.Options.EffectiveMaxDepth, typeInfo.Options.SourceName));
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(reader);
        return DeserializeCore(typeInfo, CreateSource(reader, typeInfo.Options));
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, TextReader reader)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(reader);
        return DeserializeCore(typeInfo, CreateSource(reader, typeInfo.Options));
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, Stream utf8Stream)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(utf8Stream);
        return DeserializeCore(typeInfo, CreateSource(utf8Stream, typeInfo.Options));
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, Stream utf8Stream)
    {
        ArgumentNullException.ThrowIfNull(typeInfo);
        ArgumentNullException.ThrowIfNull(utf8Stream);
        return DeserializeCore(typeInfo, CreateSource(utf8Stream, typeInfo.Options));
    }

    private static object? DeserializeCore(YamlTypeInfo typeInfo, Utf8YamlTokenSource source)
    {
        try
        {
            var yamlReader = YamlReader.Create(source, typeInfo.Options);
            if (!yamlReader.Read())
            {
                return null;
            }

            return typeInfo.ReadAsObject(yamlReader);
        }
        catch (DecoderFallbackException exception)
        {
            throw CreateInvalidEncodingException(typeInfo.Options.SourceName, exception);
        }
        finally
        {
            source.ReturnPooledBuffer();
        }
    }

    private static T? DeserializeCore<T>(YamlTypeInfo<T> typeInfo, Utf8YamlTokenSource source)
    {
        try
        {
            var yamlReader = YamlReader.Create(source, typeInfo.Options);
            if (!yamlReader.Read())
            {
                return default;
            }

            return typeInfo.Read(yamlReader);
        }
        catch (DecoderFallbackException exception)
        {
            throw CreateInvalidEncodingException(typeInfo.Options.SourceName, exception);
        }
        finally
        {
            source.ReturnPooledBuffer();
        }
    }

    // The first block of the text is read and decoded when the source is created
    private static Utf8YamlTokenSource CreateSource(TextReader reader, YamlSerializerOptions options)
    {
        try
        {
            return new Utf8YamlTokenSource(reader, options.EffectiveMaxDepth, options.SourceName);
        }
        catch (DecoderFallbackException exception)
        {
            throw CreateInvalidEncodingException(options.SourceName, exception);
        }
    }

    private static Utf8YamlTokenSource CreateSource(Stream utf8Stream, YamlSerializerOptions options)
    {
        try
        {
            return new Utf8YamlTokenSource(utf8Stream, options.EffectiveMaxDepth, options.SourceName);
        }
        catch (DecoderFallbackException exception)
        {
            throw CreateInvalidEncodingException(options.SourceName, exception);
        }
    }

    // The text is decoded while it is parsed, so invalid bytes surface in the middle of deserialization. They make the
    // payload invalid like any syntax error, which the TryDeserialize overloads report by returning false.
    private static YamlException CreateInvalidEncodingException(string? sourceName, DecoderFallbackException exception)
        => new(sourceName, Mark.Empty, Mark.Empty, "The YAML payload contains bytes that are not valid in its text encoding.", exception);

    // An empty payload has no value, which reads as null. Unboxing that null into a non-nullable value type would
    // throw a NullReferenceException, so it is returned as default(T), like the YamlTypeInfo<T> overloads do.
    private static T? DeserializeAs<T>(YamlTypeInfo typeInfo, string yaml)
    {
        if (typeInfo is YamlTypeInfo<T> typedTypeInfo)
        {
            return DeserializeCore(typedTypeInfo, yaml);
        }

        return DeserializeCore(typeInfo, yaml) is { } value ? (T)value : default;
    }

    private static T? DeserializeAs<T>(YamlTypeInfo typeInfo, ReadOnlySpan<byte> utf8Yaml)
    {
        if (typeInfo is YamlTypeInfo<T> typedTypeInfo)
        {
            return DeserializeCore(typedTypeInfo, utf8Yaml);
        }

        return DeserializeCore(typeInfo, utf8Yaml) is { } value ? (T)value : default;
    }

    private static T? DeserializeAs<T>(YamlTypeInfo typeInfo, TextReader reader)
    {
        if (typeInfo is YamlTypeInfo<T> typedTypeInfo)
        {
            return DeserializeCore(typedTypeInfo, reader);
        }

        return DeserializeCore(typeInfo, reader) is { } value ? (T)value : default;
    }

    private static T? DeserializeAs<T>(YamlTypeInfo typeInfo, Stream utf8Stream)
    {
        if (typeInfo is YamlTypeInfo<T> typedTypeInfo)
        {
            return DeserializeCore(typedTypeInfo, utf8Stream);
        }

        return DeserializeCore(typeInfo, utf8Stream) is { } value ? (T)value : default;
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

    internal static YamlTypeInfo ResolveTypeInfo(YamlSerializerOptions options, Type requestedType)
    {
        var typeInfo = TryResolveTypeInfo(options, requestedType);
        if (typeInfo is not null)
        {
            return typeInfo;
        }

        if (options.TypeInfoResolver is YamlSerializerContext context && !ReferenceEquals(options, context.Options))
        {
            throw new InvalidOperationException($"No generated metadata is available for '{requestedType}' on context '{context.GetType()}'.");
        }

        if (IsReflectionEnabledByDefault)
        {
            throw new InvalidOperationException($"No metadata is available for '{requestedType}'.");
        }

        throw new InvalidOperationException(
            $"Reflection serialization is disabled and no metadata was found for '{requestedType}'. " +
            $"Provide metadata via {nameof(YamlSerializerOptions)}.{nameof(YamlSerializerOptions.TypeInfoResolver)} or enable the '{ReflectionSwitchName}' AppContext switch.");
    }

    internal static YamlTypeInfo? TryResolveTypeInfo(YamlSerializerOptions options, Type requestedType)
    {
        if (options.TypeInfoResolver is YamlSerializerContext context && !ReferenceEquals(options, context.Options))
        {
            // Resolve type info from the context with the caller's options so generated contexts can
            // initialize any option-dependent metadata before serialization/deserialization starts.
            return context.GetTypeInfo(requestedType, options);
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
            return ReflectionYamlTypeInfoResolver.Default.GetTypeInfo(requestedType, options);
        }

        return null;
    }
}
