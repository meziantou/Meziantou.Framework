using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using Tomlyn.Helpers;
using Tomlyn.Model;
using Tomlyn.Parsing;
using Tomlyn.Serialization.Internal;
using Tomlyn.Syntax;
using Tomlyn.Text;

namespace Tomlyn.Serialization;

/// <summary>
/// Reads TOML tokens for use by <see cref="TomlConverter"/> implementations.
/// </summary>
public sealed class TomlReader
{
    private readonly TomlSerializerOptions _options;
    private readonly TomlSerializationOperationState _operationState;
    private readonly TomlParser? _parser;
    private readonly TomlReaderToken[]? _buffer;
    private readonly string? _filteredPropertyName;
    private int _bufferIndex;
    private int _bufferDepth;
    private string? _currentPropertyName;
    private string? _currentString;
    private ulong _currentData;
    private TokenKind _currentPropertyNameTokenKind;
    private TokenKind _currentStringTokenKind;
    private TomlDateTime _currentDateTime;
    private bool _hasDateTime;
    private TomlSourceSpan? _currentSpan;
    private string? _currentRawText;
    private TomlSyntaxTriviaMetadata[]? _currentLeadingTrivia;
    private TomlSyntaxTriviaMetadata[]? _currentTrailingTrivia;
    private TomlTokenType _tokenType;

    private TomlReader(TomlParser parser, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        _parser = parser;
        _buffer = null;
        _filteredPropertyName = null;
        _bufferIndex = 0;
        _bufferDepth = 0;
        _options = options ?? TomlSerializerOptions.Default;
        _operationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
        _tokenType = TomlTokenType.None;
    }

    private TomlReader(TomlReaderToken[] buffer, TomlSerializerOptions options, string? filteredPropertyName, TomlSerializationOperationState operationState)
    {
        _parser = null;
        _buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _filteredPropertyName = filteredPropertyName;
        _bufferIndex = 0;
        _bufferDepth = 0;
        _options = options ?? TomlSerializerOptions.Default;
        _operationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
        _tokenType = TomlTokenType.None;
    }

    /// <summary>
    /// Creates a TOML reader over a string payload.
    /// </summary>
    public static TomlReader Create(string toml, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var operationState = new TomlSerializationOperationState(effectiveOptions);
        var parserOptions = new Tomlyn.Parsing.TomlParserOptions
        {
            CaptureTrivia = effectiveOptions.MetadataStore is not null,
            EagerStringValues = true,
        };
        var parser = TomlParser.Create(toml, parserOptions, effectiveOptions);
        return new TomlReader(parser, effectiveOptions, operationState);
    }

    /// <summary>
    /// Creates a TOML reader over a text reader.
    /// </summary>
    public static TomlReader Create(TextReader reader, TomlSerializerOptions? options = null)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var operationState = new TomlSerializationOperationState(effectiveOptions);
        var parserOptions = new Tomlyn.Parsing.TomlParserOptions
        {
            CaptureTrivia = effectiveOptions.MetadataStore is not null,
            EagerStringValues = true,
        };
        var parser = TomlParser.Create(reader, parserOptions, effectiveOptions);
        return new TomlReader(parser, effectiveOptions, operationState);
    }

    internal static TomlReader Create(string toml, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(operationState, nameof(operationState));

        var parserOptions = new Tomlyn.Parsing.TomlParserOptions
        {
            CaptureTrivia = options.MetadataStore is not null,
            EagerStringValues = true,
        };
        var parser = TomlParser.Create(toml, parserOptions, options);
        return new TomlReader(parser, options, operationState);
    }

    internal static TomlReader Create(TextReader reader, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(operationState, nameof(operationState));

        var parserOptions = new Tomlyn.Parsing.TomlParserOptions
        {
            CaptureTrivia = options.MetadataStore is not null,
            EagerStringValues = true,
        };
        var parser = TomlParser.Create(reader, parserOptions, options);
        return new TomlReader(parser, options, operationState);
    }

    internal static TomlReader Create(TomlReaderBuffer buffer, string? filteredPropertyName = null)
    {
        ArgumentGuard.ThrowIfNull(buffer, nameof(buffer));
        return new TomlReader(buffer.Tokens, buffer.Options, filteredPropertyName, buffer.OperationState);
    }

    /// <summary>
    /// Gets the current token type.
    /// </summary>
    public TomlTokenType TokenType => _tokenType;

    /// <summary>
    /// Gets the current property name when <see cref="TokenType"/> is <see cref="TomlTokenType.PropertyName"/>.
    /// </summary>
    public string? PropertyName
    {
        get
        {
            if (_tokenType != TomlTokenType.PropertyName)
            {
                return null;
            }

            if (_currentPropertyName is not null)
            {
                return _currentPropertyName;
            }

            if (_parser is null)
            {
                return null;
            }

            _currentPropertyName = _parser.GetPropertyName();
            return _currentPropertyName;
        }
    }

    /// <summary>
    /// Gets the optional source name associated with the TOML payload (for example, a file path).
    /// </summary>
    public string? SourceName => _options.SourceName;

    /// <summary>
    /// Gets the serializer options associated with this reader instance.
    /// </summary>
    public TomlSerializerOptions Options => _options;

    internal TomlSerializationOperationState OperationState => _operationState;

    internal TomlReaderState CurrentState => new(_tokenType, _currentSpan, _bufferIndex);

    /// <summary>
    /// Gets the current line number (1-based).
    /// </summary>
    public int Line => _currentSpan?.Start.Line + 1 ?? 0;

    /// <summary>
    /// Gets the current column number (1-based).
    /// </summary>
    public int Column => _currentSpan?.Start.Column + 1 ?? 0;

    /// <summary>
    /// Gets the optional source span associated with the current token.
    /// </summary>
    public TomlSourceSpan? CurrentSpan => _currentSpan;

    internal TomlSyntaxTriviaMetadata[]? CurrentLeadingTrivia => _currentLeadingTrivia;

    internal TomlSyntaxTriviaMetadata[]? CurrentTrailingTrivia => _currentTrailingTrivia;

    internal TokenKind CurrentStringTokenKind => _currentStringTokenKind;

    /// <summary>
    /// Advances the reader to the next token.
    /// </summary>
    public bool Read()
    {
        if (_buffer is not null)
        {
            while (true)
            {
                if (_bufferIndex >= _buffer.Length)
                {
                    _tokenType = TomlTokenType.EndDocument;
                    _currentSpan = null;
                    _currentLeadingTrivia = null;
                    _currentTrailingTrivia = null;
                    return false;
                }

                var token = _buffer[_bufferIndex++];

                if (token.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
                {
                    _bufferDepth++;
                }
                else if (token.TokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
                {
                    _bufferDepth--;
                }

                if (_filteredPropertyName is not null &&
                    token.TokenType == TomlTokenType.PropertyName &&
                    _bufferDepth == 1 &&
                    string.Equals(token.PropertyName, _filteredPropertyName, StringComparison.Ordinal))
                {
                    SkipBufferedValue();
                    continue;
                }

                ApplyBufferedToken(token);
                return true;
            }
        }

        if (_parser is null || !_parser.MoveNext())
        {
            _tokenType = TomlTokenType.EndDocument;
            _currentSpan = null;
            _currentLeadingTrivia = null;
            _currentTrailingTrivia = null;
            return false;
        }

        ref readonly var parseEvent = ref _parser.Current;
        _currentSpan = parseEvent.Span;
        _currentLeadingTrivia = _parser.CurrentLeadingTrivia;
        _currentTrailingTrivia = _parser.CurrentTrailingTrivia;
        switch (parseEvent.Kind)
        {
            case TomlParseEventKind.StartDocument:
                _tokenType = TomlTokenType.StartDocument;
                break;
            case TomlParseEventKind.EndDocument:
                _tokenType = TomlTokenType.EndDocument;
                break;
            case TomlParseEventKind.StartTable:
                _tokenType = TomlTokenType.StartTable;
                break;
            case TomlParseEventKind.EndTable:
                _tokenType = TomlTokenType.EndTable;
                break;
            case TomlParseEventKind.PropertyName:
                _tokenType = TomlTokenType.PropertyName;
                _currentPropertyName = parseEvent.PropertyName;
                _currentPropertyNameTokenKind = TomlParseEventData.UnpackPropertyNameTokenKind(parseEvent.Data);
                _currentData = TomlParseEventData.UnpackPropertyNameHash(parseEvent.Data);
                break;
            case TomlParseEventKind.StartArray:
                _tokenType = TomlTokenType.StartArray;
                break;
            case TomlParseEventKind.EndArray:
                _tokenType = TomlTokenType.EndArray;
                break;
            case TomlParseEventKind.String:
                _tokenType = TomlTokenType.String;
                _currentStringTokenKind = (TokenKind)parseEvent.Data;
                _currentString = parseEvent.StringValue;
                break;
            case TomlParseEventKind.Integer:
                _tokenType = TomlTokenType.Integer;
                _currentData = parseEvent.Data;
                break;
            case TomlParseEventKind.Float:
                _tokenType = TomlTokenType.Float;
                _currentData = parseEvent.Data;
                break;
            case TomlParseEventKind.Boolean:
                _tokenType = TomlTokenType.Boolean;
                _currentData = parseEvent.Data;
                break;
            case TomlParseEventKind.DateTime:
                _tokenType = TomlTokenType.DateTime;
                _currentDateTime = parseEvent.GetTomlDateTime();
                _hasDateTime = true;
                break;
            default:
                throw CreateException($"Unsupported TOML parse event `{parseEvent.Kind}`.");
        }

        return true;
    }

    /// <summary>
    /// Checks whether the current property name matches the expected value, using an ordinal, case-sensitive comparison.
    /// </summary>
    /// <param name="expected">The expected property name.</param>
    /// <returns><c>true</c> when the property name matches; otherwise <c>false</c>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="expected"/> is <c>null</c>.</exception>
    /// <exception cref="TomlException">The current token does not have a source span.</exception>
    public bool PropertyNameEquals(string expected)
    {
        ArgumentGuard.ThrowIfNull(expected, nameof(expected));

        if (_tokenType != TomlTokenType.PropertyName)
        {
            return false;
        }

        if (_currentPropertyName is not null)
        {
            return string.Equals(_currentPropertyName, expected, StringComparison.Ordinal);
        }

        if (_currentSpan is not { } span || _parser is null)
        {
            throw CreateException("The current token does not have a source span.");
        }

        var raw = _parser.GetSpan(span);
        if (raw.IsEmpty)
        {
            return expected.Length == 0;
        }

        var expectedSpan = expected.AsSpan();
        switch (_currentPropertyNameTokenKind)
        {
            case TokenKind.BasicKey:
                return raw.SequenceEqual(expectedSpan);
            case TokenKind.StringLiteral:
                if (raw.Length >= 2 && raw[0] == '\'' && raw[raw.Length - 1] == '\'')
                {
                    return raw.Slice(1, raw.Length - 2).SequenceEqual(expectedSpan);
                }

                return false;
            case TokenKind.String:
                if (raw.Length >= 2 && raw[0] == '"' && raw[raw.Length - 1] == '"')
                {
                    var content = raw.Slice(1, raw.Length - 2);
                    if (content.IndexOf('\\') < 0)
                    {
                        return content.SequenceEqual(expectedSpan);
                    }
                }

                _currentPropertyName = _parser.GetPropertyName();
                return string.Equals(_currentPropertyName, expected, StringComparison.Ordinal);
            default:
                _currentPropertyName = _parser.GetPropertyName();
                return string.Equals(_currentPropertyName, expected, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Attempts to get a stable hash for the current property name without materializing it.
    /// </summary>
    /// <param name="hash">
    /// When this method returns <c>true</c>, contains the property-name hash (case-sensitive) computed by the parser.
    /// </param>
    /// <returns><c>true</c> when the current token is a property name; otherwise <c>false</c>.</returns>
    public bool TryGetPropertyNameHash(out ulong hash)
    {
        if (_tokenType != TomlTokenType.PropertyName)
        {
            hash = 0;
            return false;
        }

        hash = _currentData;
        return true;
    }

    private void ApplyBufferedToken(TomlReaderToken token)
    {
        _tokenType = token.TokenType;
        _currentSpan = token.Span;
        _currentRawText = token.RawText;
        _currentLeadingTrivia = token.LeadingTrivia;
        _currentTrailingTrivia = token.TrailingTrivia;
        _currentPropertyName = token.PropertyName;
        _currentString = token.StringValue;
        _currentData = token.Data;
        _currentStringTokenKind = token.StringTokenKind;
        _currentDateTime = token.DateTime;
        _hasDateTime = token.HasDateTime;
    }

    private void SkipBufferedValue()
    {
        if (_buffer is null)
        {
            return;
        }

        if (_bufferIndex >= _buffer.Length)
        {
            return;
        }

        var valueToken = _buffer[_bufferIndex++];
        if (valueToken.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            var depth = 1;
            while (_bufferIndex < _buffer.Length && depth > 0)
            {
                var next = _buffer[_bufferIndex++];
                if (next.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
                {
                    depth++;
                }
                else if (next.TokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
                {
                    depth--;
                }
            }
        }
    }

    /// <summary>
    /// Skips the current value.
    /// </summary>
    public void Skip()
    {
        if (_tokenType is not TomlTokenType.StartArray and not TomlTokenType.StartTable)
        {
            Read();
            return;
        }

        var depth = 0;
        while (true)
        {
            if (_tokenType is TomlTokenType.StartArray or TomlTokenType.StartTable) depth++;
            else if (_tokenType is TomlTokenType.EndArray or TomlTokenType.EndTable) depth--;

            if (depth == 0)
            {
                Read();
                return;
            }

            if (!Read())
            {
                return;
            }
        }
    }

    /// <summary>
    /// Gets the current string value.
    /// </summary>
    public string GetString()
    {
        if (_tokenType != TomlTokenType.String)
        {
            throw CreateException($"Expected token {TomlTokenType.String} but was {_tokenType}.");
        }

        if (_currentString is not null)
        {
            return _currentString;
        }

        if (_buffer is not null)
        {
            _currentString = string.Empty;
            return _currentString;
        }

        if (_currentSpan is not { } span)
        {
            throw CreateException("The current token does not have a source span.");
        }

        if (_parser is null)
        {
            _currentString = string.Empty;
            return _currentString;
        }

        var raw = _parser.GetSpan(span);
        if (raw.IsEmpty)
        {
            _currentString = string.Empty;
            return _currentString;
        }

        try
        {
            _currentString = TomlStringDecoder.Decode(raw, _currentStringTokenKind);
            return _currentString;
        }
        catch (FormatException ex)
        {
            throw CreateException(ex.Message);
        }
    }

    /// <summary>
    /// Gets the raw source text represented by the current token.
    /// </summary>
    /// <remarks>
    /// This API is intended for custom converters that need to preserve the original literal (for example, to implement exact parsing).
    /// </remarks>
    /// <exception cref="TomlException">The current token does not have a source span.</exception>
    public string GetRawText()
    {
        if (_currentSpan is not { } span)
        {
            throw CreateException("The current token does not have a source span.");
        }

        if (_buffer is not null)
        {
            return _currentRawText ?? string.Empty;
        }

        if (_parser is null)
        {
            return string.Empty;
        }

        return _parser.GetText(span) ?? string.Empty;
    }

    /// <summary>
    /// Gets the current integer value.
    /// </summary>
    public long GetInt64()
    {
        if (_tokenType != TomlTokenType.Integer)
        {
            throw CreateException($"Expected token {TomlTokenType.Integer} but was {_tokenType}.");
        }

        return unchecked((long)_currentData);
    }

    /// <summary>
    /// Gets the current floating value.
    /// </summary>
    public double GetDouble()
    {
        if (_tokenType != TomlTokenType.Float && _tokenType != TomlTokenType.Integer)
        {
            throw CreateException($"Expected token {TomlTokenType.Float} but was {_tokenType}.");
        }

        return _tokenType == TomlTokenType.Float
            ? BitConverter.Int64BitsToDouble(unchecked((long)_currentData))
            : GetInt64();
    }

    /// <summary>
    /// Gets the current floating value as decimal.
    /// </summary>
    public decimal GetDecimal()
    {
        if (_tokenType != TomlTokenType.Float && _tokenType != TomlTokenType.Integer)
        {
            throw CreateException($"Expected token {TomlTokenType.Float} but was {_tokenType}.");
        }

        if (_tokenType == TomlTokenType.Integer)
        {
            return GetInt64();
        }

        return (decimal)GetDouble();
    }

    /// <summary>
    /// Gets the current boolean value.
    /// </summary>
    public bool GetBoolean()
    {
        if (_tokenType != TomlTokenType.Boolean)
        {
            throw CreateException($"Expected token {TomlTokenType.Boolean} but was {_tokenType}.");
        }

        return _currentData != 0;
    }

    /// <summary>
    /// Gets the current TOML datetime value.
    /// </summary>
    public TomlDateTime GetTomlDateTime()
    {
        if (_tokenType != TomlTokenType.DateTime)
        {
            throw CreateException($"Expected token {TomlTokenType.DateTime} but was {_tokenType}.");
        }

        if (!_hasDateTime)
        {
            throw CreateException("The current datetime token does not have a parsed value.");
        }

        return _currentDateTime;
    }

    /// <summary>
    /// Creates a <see cref="TomlException"/> with the current source location (when available).
    /// </summary>
    /// <param name="message">The exception message.</param>
    public TomlException CreateException(string message)
    {
        if (_currentSpan is { } span)
        {
            return new TomlException(span, message);
        }

        return new TomlException(message);
    }

    internal bool SkipIfStateUnchanged(TomlReaderState state)
    {
        if (!IsStateUnchanged(state))
        {
            return false;
        }

        Skip();
        return true;
    }

    internal bool IsStateUnchanged(TomlReaderState state)
        => _tokenType == state.TokenType &&
            Nullable.Equals(_currentSpan, state.Span) &&
            _bufferIndex == state.BufferIndex;

    [RequiresUnreferencedCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    [RequiresDynamicCode(TomlTypeInfoResolverPipeline.ReflectionBasedSerializationMessage)]
    internal TomlTypeInfo ResolveTypeInfo(Type type)
    {
        return _operationState.ResolveTypeInfo(type);
    }

    internal TomlReaderBuffer CaptureCurrentValueToBuffer()
    {
        if (_buffer is not null)
        {
            return CaptureCurrentBufferedValueToBuffer();
        }

        var tokens = new List<TomlReaderToken>();
        tokens.Add(new TomlReaderToken(TomlTokenType.StartDocument, span: null, rawText: null, leadingTrivia: null, trailingTrivia: null, propertyName: null, stringValue: null, data: 0, stringTokenKind: default, dateTime: default, hasDateTime: false));

        AddCurrentToken(tokens);

        if (_tokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            var depth = 1;
            while (depth > 0)
            {
                if (!Read())
                {
                    break;
                }

                AddCurrentToken(tokens);

                if (_tokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
                {
                    depth++;
                }
                else if (_tokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
                {
                    depth--;
                }
            }

            Read(); // advance past container end
        }
        else
        {
            Read();
        }

        tokens.Add(new TomlReaderToken(TomlTokenType.EndDocument, span: null, rawText: null, leadingTrivia: null, trailingTrivia: null, propertyName: null, stringValue: null, data: 0, stringTokenKind: default, dateTime: default, hasDateTime: false));

        return new TomlReaderBuffer(tokens.ToArray(), _options, _operationState);
    }

    private TomlReaderBuffer CaptureCurrentBufferedValueToBuffer()
    {
        if (_buffer is null)
        {
            throw new InvalidOperationException("Cannot capture a non-buffered reader with the buffered path.");
        }

        var currentIndex = _bufferIndex - 1;
        if (currentIndex < 0 || currentIndex >= _buffer.Length)
        {
            throw new InvalidOperationException("Cannot capture before the buffered reader is positioned on a value.");
        }

        var endExclusive = currentIndex + 1;
        var currentToken = _buffer[currentIndex];
        if (currentToken.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            var depth = 1;
            while (endExclusive < _buffer.Length && depth > 0)
            {
                var next = _buffer[endExclusive++];
                if (next.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
                {
                    depth++;
                }
                else if (next.TokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
                {
                    depth--;
                }
            }

            // The current StartTable/StartArray token has already increased the live reader depth.
            // The copied EndTable/EndArray token is not applied through Read(), so restore the parent depth.
            _bufferDepth--;
        }

        var tokens = new TomlReaderToken[endExclusive - currentIndex + 2];
        tokens[0] = new TomlReaderToken(TomlTokenType.StartDocument, span: null, rawText: null, leadingTrivia: null, trailingTrivia: null, propertyName: null, stringValue: null, data: 0, stringTokenKind: default, dateTime: default, hasDateTime: false);
        Array.Copy(_buffer, currentIndex, tokens, 1, endExclusive - currentIndex);
        tokens[^1] = new TomlReaderToken(TomlTokenType.EndDocument, span: null, rawText: null, leadingTrivia: null, trailingTrivia: null, propertyName: null, stringValue: null, data: 0, stringTokenKind: default, dateTime: default, hasDateTime: false);

        _bufferIndex = endExclusive;
        Read(); // advance past the captured value

        return new TomlReaderBuffer(tokens, _options, _operationState);
    }

    private void AddCurrentToken(List<TomlReaderToken> tokens)
    {
        var rawText = _buffer is not null
            ? _currentRawText
            : _currentSpan is { } span && _parser is not null
                ? _parser.GetText(span) ?? string.Empty
                : null;

        var stringValue = _tokenType == TomlTokenType.String ? GetString() : null;
        var propertyName = _tokenType == TomlTokenType.PropertyName ? PropertyName : _currentPropertyName;

        tokens.Add(new TomlReaderToken(
            _tokenType,
            _currentSpan,
            rawText,
            _currentLeadingTrivia,
            _currentTrailingTrivia,
            propertyName,
            stringValue,
            _currentData,
            _currentStringTokenKind,
            _currentDateTime,
            _hasDateTime));
    }
}
