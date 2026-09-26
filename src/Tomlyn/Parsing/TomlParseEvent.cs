// Copyright (c) Alexandre Mutel. All rights reserved.
// Licensed under the BSD-Clause 2 license.
// See license.txt file in the project root for full license information.

using System;
using Tomlyn.Helpers;
using Tomlyn.Text;

namespace Tomlyn.Parsing;

/// <summary>
/// Represents a semantic parse event produced by <see cref="TomlParser"/>.
/// </summary>
public readonly struct TomlParseEvent
{
    internal TomlParseEvent(TomlParseEventKind kind, TomlSourceSpan? span, string? propertyName, string? stringValue, ulong data)
    {
        Kind = kind;
        Span = span;
        PropertyName = propertyName;
        _stringValue = stringValue;
        _data = data;
    }

    private readonly string? _stringValue;
    private readonly ulong _data;

    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public TomlParseEventKind Kind { get; }

    /// <summary>
    /// Gets the source span associated with this event, when available.
    /// </summary>
    public TomlSourceSpan? Span { get; }

    /// <summary>
    /// Gets the property name when <see cref="Kind"/> is <see cref="TomlParseEventKind.PropertyName"/>.
    /// This value is only populated when the parser was configured to decode scalars eagerly; otherwise use
    /// <see cref="TomlParser.GetPropertyName"/> to decode it from the original input.
    /// </summary>
    public string? PropertyName { get; }

    /// <summary>
    /// Gets the string scalar value when <see cref="Kind"/> is <see cref="TomlParseEventKind.String"/> or <see cref="TomlParseEventKind.DateTime"/>.
    /// For <see cref="TomlParseEventKind.String"/>, this value is only populated when the parser was configured to decode scalars eagerly;
    /// otherwise use <see cref="TomlParser.GetString"/>.
    /// When <see cref="TomlParserOptions.EagerStringValues"/> is enabled, this value may also be populated for simple basic strings
    /// (single-line, no escape sequences) even when <see cref="TomlParserOptions.DecodeScalars"/> is <c>false</c>.
    /// For <see cref="TomlParseEventKind.DateTime"/>, this value contains the raw literal text so that consumers can parse it without
    /// requiring <see cref="TomlParserOptions.DecodeScalars"/>.
    /// </summary>
    public string? StringValue => _stringValue;

    /// <summary>
    /// Gets the raw scalar payload for numeric and boolean scalars.
    /// When <see cref="Kind"/> is <see cref="TomlParseEventKind.String"/>, this contains the underlying <see cref="Tomlyn.Syntax.TokenKind"/> for the string literal.
    /// When <see cref="Kind"/> is <see cref="TomlParseEventKind.PropertyName"/>, this contains an internal packed payload that includes the <see cref="Tomlyn.Syntax.TokenKind"/>
    /// of the key literal (in the low 8 bits) as well as a case-sensitive hash of the decoded property name (in the remaining high bits).
    /// </summary>
    public ulong Data => _data;

    /// <summary>
    /// Gets the 1-based line number associated with this event, or 0 when unknown.
    /// </summary>
    public int Line => Span?.Start.Line + 1 ?? 0;

    /// <summary>
    /// Gets the 1-based column number associated with this event, or 0 when unknown.
    /// </summary>
    public int Column => Span?.Start.Column + 1 ?? 0;

    /// <summary>
    /// Gets the current scalar value as a string.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current event is not a string scalar.</exception>
    public string GetString()
    {
        if (Kind != TomlParseEventKind.String)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.String} but was {Kind}.");
        }

        if (_stringValue is null)
        {
            throw new InvalidOperationException("The current string scalar is not materialized. Use TomlParser.GetString() to decode it from the input.");
        }

        return _stringValue;
    }

    /// <summary>
    /// Gets the current scalar value as a 64-bit integer.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current event is not an integer scalar.</exception>
    public long GetInt64()
    {
        if (Kind != TomlParseEventKind.Integer)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.Integer} but was {Kind}.");
        }

        return unchecked((long)_data);
    }

    /// <summary>
    /// Gets the current scalar value as a double-precision number.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current event is not a numeric scalar.</exception>
    public double GetDouble()
    {
        if (Kind != TomlParseEventKind.Float && Kind != TomlParseEventKind.Integer)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.Float} or {TomlParseEventKind.Integer} but was {Kind}.");
        }

        return Kind == TomlParseEventKind.Float
            ? BitConverter.Int64BitsToDouble(unchecked((long)_data))
            : GetInt64();
    }

    /// <summary>
    /// Gets the current scalar value as a boolean.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current event is not a boolean scalar.</exception>
    public bool GetBoolean()
    {
        if (Kind != TomlParseEventKind.Boolean)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.Boolean} but was {Kind}.");
        }

        return _data != 0;
    }

    /// <summary>
    /// Gets the current scalar value as a <see cref="Tomlyn.TomlDateTime"/>.
    /// </summary>
    /// <exception cref="InvalidOperationException">The current event is not a date/time scalar.</exception>
    public TomlDateTime GetTomlDateTime()
    {
        if (Kind != TomlParseEventKind.DateTime)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.DateTime} but was {Kind}.");
        }

        if (_stringValue is null)
        {
            throw new InvalidOperationException("The current datetime scalar does not have a value.");
        }

        if (DateTimeRFC3339.TryParseOffsetDateTime(_stringValue, out var time) ||
            DateTimeRFC3339.TryParseLocalDateTime(_stringValue, out time) ||
            DateTimeRFC3339.TryParseLocalDate(_stringValue, out time) ||
            DateTimeRFC3339.TryParseLocalTime(_stringValue, out time))
        {
            return time;
        }

        var message = $"Invalid TOML datetime literal `{_stringValue}`.";
        if (Span is { } span)
        {
            throw new TomlException(span, message);
        }

        throw new TomlException(message);
    }
}
