using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Text;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Serialization.Internal;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml;

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

    private const int StreamReadBufferSize = 16 * 1024;

    private static void ThrowIfInputTooLong(long length, TomlSerializerOptions options, string unit)
    {
        if (options.MaxInputLength > 0 && length > options.MaxInputLength)
        {
            throw new TomlException($"The TOML input is longer than {options.MaxInputLength} {unit}. Change {nameof(TomlSerializerOptions)}.{nameof(TomlSerializerOptions.MaxInputLength)} to allow longer input.");
        }
    }

    private static string ReadText(TextReader reader, TomlSerializerOptions options)
    {
        if (options.MaxInputLength == 0)
        {
            return reader.ReadToEnd();
        }

        // Read no further than the limit, so a very long input is not loaded in memory
        var builder = new StringBuilder();
        var buffer = new char[Math.Min(StreamReadBufferSize, options.MaxInputLength) + 1];
        int read;
        while ((read = reader.Read(buffer, 0, buffer.Length)) > 0)
        {
            builder.Append(buffer, 0, read);
            ThrowIfInputTooLong(builder.Length, options, "characters");
        }

        return builder.ToString();
    }

    private static string ReadStream(Stream stream, TomlSerializerOptions options)
    {
        using var buffer = new MemoryStream();
        if (options.MaxInputLength == 0)
        {
            stream.CopyTo(buffer);
        }
        else
        {
            var chunk = new byte[Math.Min(StreamReadBufferSize, options.MaxInputLength) + 1];
            int read;
            while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
            {
                buffer.Write(chunk, 0, read);
                ThrowIfInputTooLong(buffer.Length, options, "bytes");
            }
        }

        return DecodeStream(buffer, options);
    }

    private static async Task<string> ReadStreamAsync(Stream stream, TomlSerializerOptions options, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        if (options.MaxInputLength == 0)
        {
            await stream.CopyToAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var chunk = new byte[Math.Min(StreamReadBufferSize, options.MaxInputLength) + 1];
            int read;
            while ((read = await stream.ReadAsync(chunk.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                buffer.Write(chunk, 0, read);
                ThrowIfInputTooLong(buffer.Length, options, "bytes");
            }
        }

        return DecodeStream(buffer, options);
    }

    // Invalid UTF-8 is reported as a TomlException with its location, so TryDeserialize returns false
    private static string DecodeStream(MemoryStream buffer, TomlSerializerOptions options)
    {
        var bytes = buffer.GetBuffer();
        var length = (int)buffer.Length;
        try
        {
            return DefaultStreamEncoding.GetString(bytes, 0, length);
        }
        catch (DecoderFallbackException ex)
        {
            var index = Math.Clamp(ex.Index, 0, length);
            var lineStart = index == 0 ? 0 : bytes.AsSpan(0, index).LastIndexOf((byte)'\n') + 1;
            var line = bytes.AsSpan(0, index).Count((byte)'\n');
            var column = DefaultStreamEncoding.GetCharCount(bytes, lineStart, index - lineStart);
            var position = new TomlTextPosition(DefaultStreamEncoding.GetCharCount(bytes, 0, index), line, column);
            throw new TomlException(new TomlSourceSpan(options.SourceName ?? string.Empty, position, position), $"Invalid UTF-8 byte sequence at byte offset {index}.", ex);
        }
    }

    private static async Task WriteToStreamAsync(Stream stream, Action<TextWriter> write, CancellationToken cancellationToken)
    {
        // The document is built in memory anyway, so only the copy to the stream is asynchronous
        using var buffer = new MemoryStream();
        WriteToStream(buffer, write);
        buffer.Position = 0;
        await buffer.CopyToAsync(stream, cancellationToken).ConfigureAwait(false);
        await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void WriteToStream(Stream stream, Action<TextWriter> write)
    {
        try
        {
            using var writer = new StreamWriter(stream, DefaultStreamEncoding, bufferSize: 1024, leaveOpen: true);
            write(writer);
            writer.Flush();
        }
        catch (EncoderFallbackException ex)
        {
            throw new TomlException("The TOML output contains an unpaired surrogate, which cannot be encoded as UTF-8.", ex);
        }
    }

    /// <summary>
    /// Gets a value indicating whether reflection-based serialization is enabled by default.
    /// </summary>
    /// <remarks>
    /// This property is the trimming feature switch: every reflection-based code path is guarded by it, so the trimmer
    /// removes them when the switch is disabled.
    /// </remarks>
    [FeatureSwitchDefinition(TomlSerializerFeatureSwitches.ReflectionSwitchName)]
    public static bool IsReflectionEnabledByDefault { get; } = TomlSerializerFeatureSwitches.IsReflectionEnabledByDefault;

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

        WriteToStream(stream, writer => Serialize(writer, value, inputType, options));
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

        WriteToStream(stream, writer => Serialize(writer, value, inputType, context));
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static void Serialize(Stream stream, object? value, TomlTypeInfo typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));

        WriteToStream(stream, writer => Serialize(writer, value, typeInfo));
    }

    /// <summary>
    /// Serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static void Serialize<T>(Stream stream, T value, TomlTypeInfo<T> typeInfo)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        WriteToStream(stream, writer => Serialize(writer, value, typeInfo));
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

        return Deserialize(ReadText(reader, typeInfo.Options), typeInfo);
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

        return Deserialize(ReadStream(stream, typeInfo.Options), typeInfo);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, TomlSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        var typeInfo = ResolveTypeInfo(options ?? TomlSerializerOptions.Default, typeof(T));
        return DeserializeAsyncCore<T>(stream, typeInfo, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding and generated metadata from a serializer context.
    /// </summary>
    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, TomlSerializerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return DeserializeAsyncCore<T>(stream, ResolveTypeInfo(context, typeof(T)), cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static ValueTask<T?> DeserializeAsync<T>(Stream stream, TomlTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return DeserializeAsyncCore<T>(stream, typeInfo, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding into an explicit destination type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static ValueTask<object?> DeserializeAsync(Stream stream, Type returnType, TomlSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        var typeInfo = ResolveTypeInfo(options ?? TomlSerializerOptions.Default, returnType);
        return DeserializeAsyncCore<object>(stream, typeInfo, cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding into an explicit destination type using generated metadata from a serializer context.
    /// </summary>
    public static ValueTask<object?> DeserializeAsync(Stream stream, Type returnType, TomlSerializerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(returnType, nameof(returnType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return DeserializeAsyncCore<object>(stream, ResolveTypeInfo(context, returnType), cancellationToken);
    }

    /// <summary>
    /// Asynchronously deserializes a TOML payload from a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static ValueTask<object?> DeserializeAsync(Stream stream, TomlTypeInfo typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return DeserializeAsyncCore<object>(stream, typeInfo, cancellationToken);
    }

    private static async ValueTask<T?> DeserializeAsyncCore<T>(Stream stream, TomlTypeInfo typeInfo, CancellationToken cancellationToken)
    {
        var toml = await ReadStreamAsync(stream, typeInfo.Options, cancellationToken).ConfigureAwait(false);
        return (T?)Deserialize(toml, typeInfo);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static Task SerializeAsync<T>(Stream stream, T value, TomlSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        return SerializeAsync(stream, value, typeof(T), options, cancellationToken);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding and an explicit input type.
    /// </summary>
    [RequiresUnreferencedCode(ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(ReflectionBasedSerializationMessage)]
    public static Task SerializeAsync(Stream stream, object? value, Type inputType, TomlSerializerOptions? options = null, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        return WriteToStreamAsync(stream, writer => Serialize(writer, value, inputType, options), cancellationToken);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding and generated metadata from a serializer context.
    /// </summary>
    public static Task SerializeAsync<T>(Stream stream, T value, TomlSerializerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return SerializeAsync(stream, value, typeof(T), context, cancellationToken);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding, an explicit input type, and generated metadata from a serializer context.
    /// </summary>
    public static Task SerializeAsync(Stream stream, object? value, Type inputType, TomlSerializerContext context, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(inputType, nameof(inputType));
        ArgumentGuard.ThrowIfNull(context, nameof(context));
        return WriteToStreamAsync(stream, writer => Serialize(writer, value, inputType, context), cancellationToken);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static Task SerializeAsync(Stream stream, object? value, TomlTypeInfo typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return WriteToStreamAsync(stream, writer => Serialize(writer, value, typeInfo), cancellationToken);
    }

    /// <summary>
    /// Asynchronously serializes a value to a stream using UTF-8 encoding and explicit metadata.
    /// </summary>
    public static Task SerializeAsync<T>(Stream stream, T value, TomlTypeInfo<T> typeInfo, CancellationToken cancellationToken = default)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        return WriteToStreamAsync(stream, writer => Serialize(writer, value, typeInfo), cancellationToken);
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
            value = Deserialize<T>(toml, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(toml, returnType, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize<T>(toml, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(toml, returnType, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize<T>(reader, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(reader, returnType, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize<T>(reader, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(reader, returnType, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize<T>(stream, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(stream, returnType, options);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize<T>(stream, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
            value = Deserialize(stream, returnType, context);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text using explicit metadata.
    /// </summary>
    public static bool TryDeserialize<T>(string toml, TomlTypeInfo<T> typeInfo, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(toml, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from text using explicit metadata.
    /// </summary>
    public static bool TryDeserialize(string toml, TomlTypeInfo typeInfo, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(toml, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader using explicit metadata.
    /// </summary>
    public static bool TryDeserialize<T>(TextReader reader, TomlTypeInfo<T> typeInfo, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(reader, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a text reader using explicit metadata.
    /// </summary>
    public static bool TryDeserialize(TextReader reader, TomlTypeInfo typeInfo, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(reader, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = null;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding using explicit metadata.
    /// </summary>
    public static bool TryDeserialize<T>(Stream stream, TomlTypeInfo<T> typeInfo, [NotNullWhen(true)] out T? value)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(stream, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    /// Attempts to deserialize a TOML payload from a stream using UTF-8 encoding using explicit metadata.
    /// </summary>
    public static bool TryDeserialize(Stream stream, TomlTypeInfo typeInfo, [NotNullWhen(true)] out object? value)
    {
        ArgumentGuard.ThrowIfNull(stream, nameof(stream));
        ArgumentGuard.ThrowIfNull(typeInfo, nameof(typeInfo));
        try
        {
            value = Deserialize(stream, typeInfo);
            return value is not null;
        }
        catch (TomlException ex) when (!ex.IsConfigurationError)
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
        ThrowIfInputTooLong(toml.Length, typeInfo.Options, "characters");

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
        catch (TomlException ex) when (reader.OperationState.HasDiagnostics && !ex.IsConfigurationError)
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
        // The table is written directly when the converters would not change any value
        if (options.RootValueHandling != TomlRootValueHandling.WrapInRootKey &&
            options.MetadataStore is null &&
            options.Converters.Count == 0 &&
            typeInfo.Type == typeof(Meziantou.Framework.Toml.Model.TomlTable) &&
            value is Meziantou.Framework.Toml.Model.TomlTable rootTable &&
            Meziantou.Framework.Toml.Serialization.Internal.TomlModelTextWriter.CanWriteDirectly(rootTable, options))
        {
            Meziantou.Framework.Toml.Serialization.Internal.TomlModelTextWriter.WriteDocument(writer, rootTable, options);
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
            throw TomlException.CreateConfigurationError($"No generated metadata is available for type '{type.FullName}' in the provided context.");
        }

        return typeInfo;
    }
}
