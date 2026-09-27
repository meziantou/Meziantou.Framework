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
        var diagnosticCount = reader.OperationState.DiagnosticCount;
        var converterValue = IsBuiltInConverter(converter) ? null : reader.BeginConverterValue();
        try
        {
            var value = converter.Read(reader, typeToConvert);
            reader.SkipIfStateUnchanged(state);
            if (converterValue is not null && !reader.EndConverterValue(converterValue))
            {
                converterValue = null;
                throw CreateReadTooMuchOrNotEnoughException(reader, converter, typeToConvert, state, diagnosticCount);
            }

            converterValue = null;
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
        finally
        {
            if (converterValue is not null)
            {
                reader.EndConverterValue(converterValue);
            }
        }
    }

    internal static T? Read<T>(TomlReader reader, TomlConverter<T> converter)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(converter, nameof(converter));

        var state = reader.CurrentState;
        var diagnosticCount = reader.OperationState.DiagnosticCount;
        var converterValue = IsBuiltInConverter(converter) ? null : reader.BeginConverterValue();
        try
        {
            var value = converter.Read(reader);
            reader.SkipIfStateUnchanged(state);
            if (converterValue is not null && !reader.EndConverterValue(converterValue))
            {
                converterValue = null;
                throw CreateReadTooMuchOrNotEnoughException(reader, converter, typeof(T), state, diagnosticCount);
            }

            converterValue = null;
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
        finally
        {
            if (converterValue is not null)
            {
                reader.EndConverterValue(converterValue);
            }
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

    // Like System.Text.Json, a converter that leaves the reader elsewhere than after its value is a bug: the parent would read
    // the rest of the value as its own keys, or miss the keys the converter read
    private static TomlException CreateReadTooMuchOrNotEnoughException(TomlReader reader, TomlConverter converter, Type typeToConvert, TomlReaderState state, int diagnosticCount)
    {
        // Without recovery, the error of a nested value stops its reading: a converter that catches it returns within the
        // value, whose error is reported instead. With recovery, the nested value is read to its end whatever its errors.
        var operationState = reader.OperationState;
        if (!operationState.RecoversValueErrors && operationState.DiagnosticCount > diagnosticCount)
        {
            return TomlException.CreateRecordedValueError(operationState.Diagnostics!, diagnosticCount, state.Span);
        }

        var location = state.Span is { } span ? $" at {span.ToStringSimple()}" : string.Empty;
        return TomlException.CreateConfigurationError(
            $"The converter '{converter.GetType().FullName}' read too much or not enough of the '{typeToConvert.FullName}' value{location}. A converter must read the whole value, then the token that follows it.");
    }

    private static bool ShouldWrapConverterException(Exception exception)
        => exception is not OperationCanceledException and not OutOfMemoryException;
}
