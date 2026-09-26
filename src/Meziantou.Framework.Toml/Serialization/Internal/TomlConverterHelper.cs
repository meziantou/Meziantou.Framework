using System;
using Meziantou.Framework.Toml.Helpers;

namespace Meziantou.Framework.Toml.Serialization.Internal;

internal static class TomlConverterHelper
{
    private const string ReadExceptionMessagePrefix = "Exception while trying to convert TOML value";

    internal static object? Read(TomlReader reader, TomlConverter converter, Type typeToConvert)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(converter, nameof(converter));
        ArgumentGuard.ThrowIfNull(typeToConvert, nameof(typeToConvert));

        var state = reader.CurrentState;
        try
        {
            var value = converter.Read(reader, typeToConvert);
            reader.SkipIfStateUnchanged(state);
            return value;
        }
        catch (TomlException ex) when (ex.Diagnostics.Count == 0 && !ex.IsConfigurationError && !IsBuiltInConverter(converter))
        {
            throw CreateReadException(reader, converter, typeToConvert, ex);
        }
        catch (Exception ex) when (ex is not TomlException && ShouldWrapConverterException(ex))
        {
            throw CreateReadException(reader, converter, typeToConvert, ex);
        }
    }

    internal static T? Read<T>(TomlReader reader, TomlConverter<T> converter)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(converter, nameof(converter));

        var state = reader.CurrentState;
        try
        {
            var value = converter.Read(reader);
            reader.SkipIfStateUnchanged(state);
            return value;
        }
        catch (TomlException ex) when (ex.Diagnostics.Count == 0 && !ex.IsConfigurationError && !IsBuiltInConverter(converter))
        {
            throw CreateReadException(reader, converter, typeof(T), ex);
        }
        catch (Exception ex) when (ex is not TomlException && ShouldWrapConverterException(ex))
        {
            throw CreateReadException(reader, converter, typeof(T), ex);
        }
    }

    // The errors of the built-in converters already describe the TOML value, and the reflection-based metadata does not wrap
    // them either, so only the errors of user converters get the converter name
    private static bool IsBuiltInConverter(TomlConverter converter) => converter.GetType().Assembly == typeof(TomlConverterHelper).Assembly;

    private static TomlException CreateReadException(TomlReader reader, TomlConverter converter, Type typeToConvert, Exception innerException)
    {
        var message = $"{ReadExceptionMessagePrefix} to type '{typeToConvert.FullName}' using converter '{converter.GetType().FullName}'. Reason: {innerException.Message}";
        return reader.CurrentSpan is { } span
            ? new TomlException(span, message, innerException)
            : new TomlException(message, innerException);
    }

    private static bool ShouldWrapConverterException(Exception exception)
        => exception is not OperationCanceledException and not OutOfMemoryException;
}
