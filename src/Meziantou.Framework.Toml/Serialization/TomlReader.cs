using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Parsing;
using Meziantou.Framework.Toml.Serialization.Internal;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Serialization;

/// <summary>
/// Reads TOML tokens for use by <see cref="TomlConverter"/> implementations.
/// </summary>
/// <remarks>
/// The reader parses the whole document on the first <see cref="Read"/>, and returns the keys of a table defined in
/// several places (for example an array of tables reopened after another table) as a single table.
/// </remarks>
public sealed class TomlReader
{
    private readonly TomlSerializerOptions _options;
    private List<(int Depth, TomlPropertiesMetadata Metadata, string? InlineValueName)>? _metadataCaptures;
    private int _depth;
    private readonly TomlSerializationOperationState _operationState;
    private readonly TomlParser? _parser;
    private readonly string _sourceName;

    // The parser events of the whole document, in the order they are read (see TomlParseEventMerger)
    private TomlBufferedParseEvent[]? _events;
    private TomlDateTime[]? _eventDateTimes;
    private TomlSyntaxTriviaMetadata[]?[]? _eventLeadingTrivia;
    private TomlSyntaxTriviaMetadata[]?[]? _eventTrailingTrivia;
    private int[]? _eventOrder;
    private int _eventCount;
    private bool _eventsLoaded;
    private int _eventPosition;
    private int _currentEventIndex;
    private readonly TomlReaderToken[]? _buffer;
    private readonly int[]? _bufferContainerEnds;

    // The buffered tokens are _buffer[_bufferStart.._bufferEnd]: a captured value shares the array of the reader it
    // was captured from. The reader returns a StartDocument token before them and an EndDocument token after them.
    private readonly int _bufferStart;
    private readonly int _bufferEnd;
    private bool _bufferStarted;
    private bool _bufferEnded;
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
    private TomlSyntaxTriviaMetadata[]? _previousTrailingTrivia;
    private TomlTokenType _tokenType;

    private TomlReader(TomlParser parser, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        _parser = parser;
        _sourceName = options?.SourceName ?? string.Empty;
        _buffer = null;
        _filteredPropertyName = null;
        _bufferIndex = 0;
        _bufferDepth = 0;
        _options = options ?? TomlSerializerOptions.Default;
        _operationState = operationState ?? throw new ArgumentNullException(nameof(operationState));
        _tokenType = TomlTokenType.None;
    }

    private TomlReader(TomlReaderBuffer buffer, string? filteredPropertyName)
    {
        _parser = null;
        _sourceName = buffer.Options.SourceName ?? string.Empty;
        _buffer = buffer.Tokens;
        _bufferContainerEnds = buffer.ContainerEnds;
        _bufferStart = buffer.Start;
        _bufferEnd = buffer.Start + buffer.Count;
        _filteredPropertyName = filteredPropertyName;
        _bufferIndex = buffer.Start;
        _bufferDepth = 0;
        _options = buffer.Options;
        _operationState = buffer.OperationState;
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
        var parserOptions = new Meziantou.Framework.Toml.Parsing.TomlParserOptions
        {
            CaptureTrivia = effectiveOptions.MetadataStore is not null,
            EagerStringValues = false,
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
        var parserOptions = new Meziantou.Framework.Toml.Parsing.TomlParserOptions
        {
            CaptureTrivia = effectiveOptions.MetadataStore is not null,
            EagerStringValues = false,
        };
        var parser = TomlParser.Create(reader, parserOptions, effectiveOptions);
        return new TomlReader(parser, effectiveOptions, operationState);
    }

    internal static TomlReader Create(string toml, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        ArgumentGuard.ThrowIfNull(toml, nameof(toml));
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(operationState, nameof(operationState));

        var parserOptions = new Meziantou.Framework.Toml.Parsing.TomlParserOptions
        {
            CaptureTrivia = options.MetadataStore is not null,
            EagerStringValues = false,
        };
        var parser = TomlParser.Create(toml, parserOptions, options);
        return new TomlReader(parser, options, operationState);
    }

    internal static TomlReader Create(TextReader reader, TomlSerializerOptions options, TomlSerializationOperationState operationState)
    {
        ArgumentGuard.ThrowIfNull(reader, nameof(reader));
        ArgumentGuard.ThrowIfNull(options, nameof(options));
        ArgumentGuard.ThrowIfNull(operationState, nameof(operationState));

        var parserOptions = new Meziantou.Framework.Toml.Parsing.TomlParserOptions
        {
            CaptureTrivia = options.MetadataStore is not null,
            EagerStringValues = false,
        };
        var parser = TomlParser.Create(reader, parserOptions, options);
        return new TomlReader(parser, options, operationState);
    }

    internal static TomlReader Create(TomlReaderBuffer buffer, string? filteredPropertyName = null)
    {
        ArgumentGuard.ThrowIfNull(buffer, nameof(buffer));
        return new TomlReader(buffer, filteredPropertyName);
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

            _currentPropertyName = _parser.DecodePropertyName(GetCurrentParseEvent());
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

    internal TomlReaderState CurrentState => new(_tokenType, _currentSpan, CurrentPosition);

    private int CurrentPosition => _buffer is not null ? _bufferIndex : _eventPosition;

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

    /// <summary>
    /// Gets a value indicating whether the current <see cref="TomlTokenType.StartTable"/> or <see cref="TomlTokenType.StartArray"/>
    /// token starts an inline table (<c>{</c>) or an inline array (<c>[</c>).
    /// </summary>
    /// <remarks>
    /// It is <see langword="false"/> for a table or an array of tables opened by a header or a dotted key, which a later
    /// header or dotted key can extend, and for any other token.
    /// </remarks>
    public bool IsInlineContainer => _tokenType is TomlTokenType.StartTable or TomlTokenType.StartArray && TomlParseEventData.IsInlineContainer(_currentData);

    internal TomlSyntaxTriviaMetadata[]? CurrentLeadingTrivia => _currentLeadingTrivia;

    internal TomlSyntaxTriviaMetadata[]? CurrentTrailingTrivia => _currentTrailingTrivia;

    // The trailing trivia of the token before the current one, such as the end of the inline array or table just read
    internal TomlSyntaxTriviaMetadata[]? PreviousTrailingTrivia => _previousTrailingTrivia;

    internal TokenKind CurrentStringTokenKind => _currentStringTokenKind;

    /// <summary>
    /// Advances the reader to the next token.
    /// </summary>
    /// <exception cref="TomlException">The TOML document is invalid. The first call reports errors anywhere in the document.</exception>
    public bool Read()
    {
        if (_metadataCaptures is not { Count: > 0 } captures)
        {
            var hasToken = ReadCore();
            UpdateDepth();
            return hasToken;
        }

        // The property of a table whose metadata is captured: its name is known before the read, and its value after it
        var capture = captures[^1];
        var isCapturedProperty = _tokenType == TomlTokenType.PropertyName && capture.Depth == _depth;
        var name = isCapturedProperty ? PropertyName : null;
        var nameSpan = _currentSpan;
        var leadingTrivia = _currentLeadingTrivia;
        var result = ReadCore();
        UpdateDepth();
        if (isCapturedProperty)
        {
            TomlPropertyMetadataCapture.Capture(capture.Metadata, name!, nameSpan, leadingTrivia, _currentTrailingTrivia, TomlPropertyMetadataCapture.GetDisplayKind(this));

            // The trailing comment of an inline array or table follows its closing token
            captures[^1] = capture with { InlineValueName = IsInlineContainer ? name : null };
        }

        // A table left without EndPropertiesMetadataCapture, because an error was recovered from, stops capturing. So does the
        // capture of an inline table value when its end is read.
        while (captures.Count > 0 && captures[^1].Depth > _depth)
        {
            captures.RemoveAt(captures.Count - 1);
        }

        if (captures.Count > 0 && captures[^1] is { InlineValueName: { } inlineValueName } current && current.Depth == _depth &&
            _tokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
        {
            TomlPropertyMetadataCapture.AppendTrailingTrivia(current.Metadata, inlineValueName, _currentTrailingTrivia);
            captures[^1] = current with { InlineValueName = null };
        }

        return result;
    }

    // The depth of the current token: a table and its properties have the same depth
    private void UpdateDepth()
    {
        switch (_tokenType)
        {
            case TomlTokenType.StartTable or TomlTokenType.StartArray:
                _depth++;
                break;
            case TomlTokenType.EndTable or TomlTokenType.EndArray:
                _depth--;
                break;
        }
    }

    // Generated metadata captures how the properties of a table are written, like the reflection-based metadata does. The
    // reader records them, as the generated code reads property names in many places.
    internal TomlPropertiesMetadata? BeginPropertiesMetadataCapture()
    {
        if (_options.MetadataStore is null || _tokenType != TomlTokenType.StartTable)
        {
            return null;
        }

        var metadata = new TomlPropertiesMetadata();
        (_metadataCaptures ??= []).Add((_depth, metadata, null));
        return metadata;
    }

    internal void EndPropertiesMetadataCapture(TomlPropertiesMetadata metadata, object instance)
    {
        if (_metadataCaptures is { } captures)
        {
            for (var i = captures.Count - 1; i >= 0; i--)
            {
                if (ReferenceEquals(captures[i].Metadata, metadata))
                {
                    captures.RemoveAt(i);
                    break;
                }
            }
        }

        _options.MetadataStore!.SetProperties(instance, metadata);
    }

    private bool ReadCore()
    {
        _previousTrailingTrivia = _currentTrailingTrivia;
        if (_buffer is not null)
        {
            if (!_bufferStarted)
            {
                _bufferStarted = true;
                ApplyBufferedToken(DocumentToken(TomlTokenType.StartDocument));
                return true;
            }

            while (true)
            {
                if (_bufferIndex >= _bufferEnd)
                {
                    if (!_bufferEnded)
                    {
                        _bufferEnded = true;
                        ApplyBufferedToken(DocumentToken(TomlTokenType.EndDocument));
                        return true;
                    }

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

        if (_parser is not null && !_eventsLoaded)
        {
            _eventsLoaded = true;
            ReadAllEvents(_parser);
        }

        if (_events is null || _eventPosition >= _eventCount)
        {
            ReleaseEvents();
            _tokenType = TomlTokenType.EndDocument;
            _currentSpan = null;
            _currentLeadingTrivia = null;
            _currentTrailingTrivia = null;
            return false;
        }

        _currentEventIndex = _eventOrder is null ? _eventPosition : _eventOrder[_eventPosition];
        _eventPosition++;
        ref readonly var parseEvent = ref _events[_currentEventIndex];
        _currentSpan = parseEvent.GetSpan(_sourceName);
        _currentLeadingTrivia = _eventLeadingTrivia?[_currentEventIndex];
        _currentTrailingTrivia = _eventTrailingTrivia?[_currentEventIndex];
        switch (parseEvent.Kind)
        {
            case TomlParseEventKind.StartDocument:
                _tokenType = TomlTokenType.StartDocument;
                break;
            case TomlParseEventKind.EndDocument:
                _tokenType = TomlTokenType.EndDocument;
                break;
            case TomlParseEventKind.StartTable:
                TomlDepthHelper.EnsureSufficientExecutionStack(_currentSpan);
                _tokenType = TomlTokenType.StartTable;
                _currentData = parseEvent.Data;
                break;
            case TomlParseEventKind.EndTable:
                _tokenType = TomlTokenType.EndTable;
                break;
            case TomlParseEventKind.PropertyName:
                _tokenType = TomlTokenType.PropertyName;
                _currentPropertyName = null;
                _currentPropertyNameTokenKind = TomlParseEventData.UnpackPropertyNameTokenKind(parseEvent.Data);
                _currentData = TomlParseEventData.UnpackPropertyNameHash(parseEvent.Data);
                break;
            case TomlParseEventKind.StartArray:
                TomlDepthHelper.EnsureSufficientExecutionStack(_currentSpan);
                _tokenType = TomlTokenType.StartArray;
                _currentData = parseEvent.Data;
                break;
            case TomlParseEventKind.EndArray:
                _tokenType = TomlTokenType.EndArray;
                break;
            case TomlParseEventKind.String:
                _tokenType = TomlTokenType.String;
                _currentStringTokenKind = (TokenKind)parseEvent.Data;
                _currentString = null;
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
                _currentDateTime = _eventDateTimes![(int)parseEvent.Data];
                _hasDateTime = true;
                break;
            default:
                throw CreateException($"Unsupported TOML parse event `{parseEvent.Kind}`.");
        }

        // The last event is EndDocument, which does not need the buffer
        if (_eventPosition >= _eventCount)
        {
            ReleaseEvents();
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

                _currentPropertyName = _parser.DecodePropertyName(GetCurrentParseEvent());
                return string.Equals(_currentPropertyName, expected, StringComparison.Ordinal);
            default:
                _currentPropertyName = _parser.DecodePropertyName(GetCurrentParseEvent());
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

    // A table can be split across the document, so the reader cannot stream the parser events: consumers expect each key
    // of a table once. Reading the whole document first also reports every syntax error before any value is created.
    private void ReadAllEvents(TomlParser parser)
    {
        var captureTrivia = parser.ParserOptions.CaptureTrivia;
        var events = ArrayPool<TomlBufferedParseEvent>.Shared.Rent(256);
        var leadingTrivia = captureTrivia ? new TomlSyntaxTriviaMetadata[]?[events.Length] : null;
        var trailingTrivia = captureTrivia ? new TomlSyntaxTriviaMetadata[]?[events.Length] : null;
        List<TomlDateTime>? dateTimes = null;
        var count = 0;
        try
        {
            while (parser.MoveNext())
            {
                if (count == events.Length)
                {
                    var larger = ArrayPool<TomlBufferedParseEvent>.Shared.Rent(events.Length * 2);
                    Array.Copy(events, larger, count);
                    ArrayPool<TomlBufferedParseEvent>.Shared.Return(events);
                    events = larger;
                    if (captureTrivia)
                    {
                        Array.Resize(ref leadingTrivia, events.Length);
                        Array.Resize(ref trailingTrivia, events.Length);
                    }
                }

                ref readonly var parseEvent = ref parser.Current;
                var data = parseEvent.Data;
                if (parseEvent.Kind == TomlParseEventKind.DateTime)
                {
                    dateTimes ??= [];
                    data = (ulong)dateTimes.Count;
                    dateTimes.Add(parseEvent.GetTomlDateTime());
                }

                events[count] = new TomlBufferedParseEvent(parseEvent.Kind, parseEvent.Span, data);
                if (captureTrivia)
                {
                    leadingTrivia![count] = parser.CurrentLeadingTrivia;
                    trailingTrivia![count] = parser.CurrentTrailingTrivia;
                }

                count++;
            }

            _eventOrder = TomlParseEventMerger.GetMergedOrder(events, count, parser, _sourceName);
        }
        catch
        {
            ArrayPool<TomlBufferedParseEvent>.Shared.Return(events);
            throw;
        }

        _eventDateTimes = dateTimes?.ToArray();
        _eventLeadingTrivia = leadingTrivia;
        _eventTrailingTrivia = trailingTrivia;
        _eventCount = _eventOrder?.Length ?? count;
        _events = events;
    }

    private TomlParseEvent GetCurrentParseEvent() => _events![_currentEventIndex].ToParseEvent(_sourceName);

    private void ReleaseEvents()
    {
        if (_events is null)
        {
            return;
        }

        ArrayPool<TomlBufferedParseEvent>.Shared.Return(_events);
        _events = null;
        _eventDateTimes = null;
        _eventOrder = null;
        _eventLeadingTrivia = null;
        _eventTrailingTrivia = null;
        _eventCount = 0;
    }

    private static TomlReaderToken DocumentToken(TomlTokenType tokenType)
        => new(tokenType, span: null, rawText: null, leadingTrivia: null, trailingTrivia: null, propertyName: null, stringValue: null, data: 0, stringTokenKind: default, dateTime: default, hasDateTime: false);

    private void ApplyBufferedToken(TomlReaderToken token)
    {
        if (token.TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            TomlDepthHelper.EnsureSufficientExecutionStack(token.Span);
        }

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

        if (_bufferIndex >= _bufferEnd)
        {
            return;
        }

        _bufferIndex = GetBufferedValueEnd(_bufferIndex);
    }

    // The index after the buffered value that starts at the index
    private int GetBufferedValueEnd(int index)
    {
        if (_buffer![index].TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            return Math.Min(_bufferContainerEnds![index] + 1, _bufferEnd);
        }

        return index + 1;
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

        // The end token of a buffered container is read like any other token, so the depth and the metadata captures are
        // updated as if the tokens in between were read
        if (_buffer is not null && _bufferIndex > _bufferStart && _bufferContainerEnds![_bufferIndex - 1] is var endIndex &&
            endIndex >= _bufferIndex && endIndex < _bufferEnd && _buffer[endIndex].TokenType is TomlTokenType.EndArray or TomlTokenType.EndTable)
        {
            _bufferIndex = endIndex;
            Read();
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
    /// <exception cref="TomlException">The value is not a number, or is outside the range of <see cref="decimal"/>.</exception>
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

        // Parse the literal rather than the double, which would lose digits
        var text = GetRawText();
        if (decimal.TryParse(text.Replace("_", "", StringComparison.Ordinal), NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        throw CreateException($"TOML float literal `{text}` cannot be converted to decimal.");
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

    // Records the error of a value and continues with the next one: the value is skipped when the error happened on its first
    // token, and was already read when its errors were recorded
    internal bool TryRecoverValue(TomlException exception, TomlReaderState startState)
    {
        if (_operationState.IsRecordedValueError(exception))
        {
            return _operationState.CanContinueAfterRecordedValueError(exception, startState.TokenType, startState.Span);
        }

        if (!_operationState.CanAddDiagnostics(exception) || !IsStateUnchanged(startState))
        {
            return false;
        }

        _operationState.AddDiagnostics(exception);
        if (!_operationState.RecoversValueErrors)
        {
            return false;
        }

        Skip();
        return true;
    }

    internal bool IsStateUnchanged(TomlReaderState state)
        => _tokenType == state.TokenType &&
            Nullable.Equals(_currentSpan, state.Span) &&
            CurrentPosition == state.BufferIndex;

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

        return new TomlReaderBuffer(tokens.ToArray(), 0, tokens.Count, _options, _operationState);
    }

    private TomlReaderBuffer CaptureCurrentBufferedValueToBuffer()
    {
        if (_buffer is null)
        {
            throw new InvalidOperationException("Cannot capture a non-buffered reader with the buffered path.");
        }

        var currentIndex = _bufferIndex - 1;
        if (currentIndex < _bufferStart || currentIndex >= _bufferEnd)
        {
            throw new InvalidOperationException("Cannot capture before the buffered reader is positioned on a value.");
        }

        var endExclusive = GetBufferedValueEnd(currentIndex);

        // The captured value shares the tokens: copying them at every nested level would be quadratic
        var buffer = new TomlReaderBuffer(_buffer, currentIndex, endExclusive - currentIndex, _options, _operationState, _bufferContainerEnds);

        if (_buffer[currentIndex].TokenType is TomlTokenType.StartTable or TomlTokenType.StartArray)
        {
            if (endExclusive - 1 > currentIndex && _buffer[endExclusive - 1].TokenType is TomlTokenType.EndTable or TomlTokenType.EndArray)
            {
                // The end token is read like any other, so the depths and the metadata captures are updated as if the tokens
                // of the value were read
                _bufferIndex = endExclusive - 1;
                Read(); // the end of the value
                Read(); // advance past the captured value
                return buffer;
            }

            // The value has no end token: restore the depth of its parent, which the start token increased
            _bufferDepth--;
            _depth--;
        }

        _bufferIndex = endExclusive;
        Read(); // advance past the captured value

        return buffer;
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
