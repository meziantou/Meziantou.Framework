// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;

namespace Tomlyn.Serialization.Internal;

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
        catch (TomlException ex) when (ex.Diagnostics.Count == 0)
        {
            throw CreateReadException(reader, converter, typeToConvert, ex);
        }
        catch (Exception ex) when (ShouldWrapConverterException(ex))
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
        catch (TomlException ex) when (ex.Diagnostics.Count == 0)
        {
            throw CreateReadException(reader, converter, typeof(T), ex);
        }
        catch (Exception ex) when (ShouldWrapConverterException(ex))
        {
            throw CreateReadException(reader, converter, typeof(T), ex);
        }
    }

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
