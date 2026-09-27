using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Meziantou.Framework.Toml.Helpers;
using Meziantou.Framework.Toml.Model;
using Meziantou.Framework.Toml.Serialization;
using Meziantou.Framework.Toml.Syntax;
using Meziantou.Framework.Toml.Text;

namespace Meziantou.Framework.Toml.Parsing;

/// <summary>
/// Parses TOML into an incremental event stream.
/// </summary>
public sealed partial class TomlParser
{
    private readonly TomlSerializerOptions _options;
    private readonly TomlParserOptions _parserOptions;
    private readonly IParserCore _core;
    private readonly int _effectiveMaxDepth;
    private DiagnosticsBag? _diagnostics;
    private TomlParseEvent _current;
    private int _depth;
    private int _modelDepth;
    private List<(int Depth, int ExtraDepth)>? _extraDepths;
    private bool _stoppedAtMaxDepth;

    private TomlParser(IParserCore core, TomlSerializerOptions options, TomlParserOptions parserOptions, DiagnosticsBag? diagnostics)
    {
        _core = core;
        _options = options ?? TomlSerializerOptions.Default;
        _parserOptions = parserOptions ?? throw new ArgumentNullException(nameof(parserOptions));
        _effectiveMaxDepth = TomlDepthHelper.GetEffectiveMaxDepth(_options.MaxDepth);
        _diagnostics = diagnostics;
        _current = default;
        _depth = 0;
    }

    internal static TomlParser Create(ReadOnlyMemory<char> toml, TomlParserOptions parserOptions, TomlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(parserOptions);

        var effectiveOptions = options ?? TomlSerializerOptions.Default;
        var sourcePath = effectiveOptions.SourceName ?? string.Empty;
        DiagnosticsBag? diagnostics = null;
        if (parserOptions.Mode == TomlParserMode.Tolerant)
        {
            diagnostics = new DiagnosticsBag();
        }

        var core = new ParserCore(toml, sourcePath, parserOptions, effectiveOptions.DuplicateKeyHandling == TomlDuplicateKeyHandling.LastWins, diagnostics);
        return new TomlParser(core, effectiveOptions, parserOptions, diagnostics);
    }

    /// <summary>
    /// Creates a parser over a TOML string.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="options">Optional parser/serializer options.</param>
    /// <returns>A parser instance positioned before the first event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    public static TomlParser Create(string toml, TomlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var memory = toml.AsMemory();
        if (!memory.IsEmpty && memory.Span[0] == '\uFEFF')
        {
            memory = memory.Slice(1);
        }

        var parserOptions = new TomlParserOptions();
        return Create(memory, parserOptions, options);
    }

    /// <summary>
    /// Creates a parser over a TOML string.
    /// </summary>
    /// <param name="toml">The TOML payload.</param>
    /// <param name="parserOptions">Parser options controlling error handling.</param>
    /// <param name="options">Optional parser/serializer options.</param>
    /// <returns>A parser instance positioned before the first event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="toml"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="parserOptions"/> is <c>null</c>.</exception>
    public static TomlParser Create(string toml, TomlParserOptions parserOptions, TomlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(toml);

        var memory = toml.AsMemory();
        if (!memory.IsEmpty && memory.Span[0] == '\uFEFF')
        {
            memory = memory.Slice(1);
        }

        return Create(memory, parserOptions, options);
    }

    /// <summary>
    /// Creates a parser over TOML text from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="options">Optional parser/serializer options.</param>
    /// <returns>A parser instance positioned before the first event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    public static TomlParser Create(TextReader reader, TomlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        return Create(reader.ReadToEnd(), options);
    }

    /// <summary>
    /// Creates a parser over TOML text from a <see cref="TextReader"/>.
    /// </summary>
    /// <param name="reader">The text reader.</param>
    /// <param name="parserOptions">Parser options controlling error handling.</param>
    /// <param name="options">Optional parser/serializer options.</param>
    /// <returns>A parser instance positioned before the first event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <c>null</c>.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="parserOptions"/> is <c>null</c>.</exception>
    public static TomlParser Create(TextReader reader, TomlParserOptions parserOptions, TomlSerializerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(reader);

        ArgumentNullException.ThrowIfNull(parserOptions);
        return Create(reader.ReadToEnd(), parserOptions, options);
    }

    /// <summary>
    /// Gets the current parse event.
    /// </summary>
    public ref readonly TomlParseEvent Current => ref _current;

    /// <summary>
    /// Gets the current string scalar value by decoding it from the original TOML input.
    /// </summary>
    /// <returns>The decoded string value.</returns>
    /// <exception cref="InvalidOperationException">The current event is not a string scalar.</exception>
    public string GetString()
    {
        if (_current.Kind != TomlParseEventKind.String)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.String} but was {_current.Kind}.");
        }

        return DecodeString(_current);
    }

    internal string DecodeString(in TomlParseEvent parseEvent)
    {
        if (parseEvent.Span is not { } span)
        {
            return string.Empty;
        }

        var raw = GetSpan(span);
        if (raw.IsEmpty)
        {
            return string.Empty;
        }

        try
        {
            return TomlStringDecoder.Decode(raw, (TokenKind)parseEvent.Data);
        }
        catch (FormatException ex)
        {
            throw new TomlException(span, ex.Message, ex);
        }
    }

    /// <summary>
    /// Gets the current property name by decoding it from the original TOML input.
    /// </summary>
    /// <returns>The decoded property name.</returns>
    /// <exception cref="InvalidOperationException">The current event is not a property name.</exception>
    public string GetPropertyName()
    {
        if (_current.Kind != TomlParseEventKind.PropertyName)
        {
            throw new InvalidOperationException($"Expected {TomlParseEventKind.PropertyName} but was {_current.Kind}.");
        }

        return DecodePropertyName(_current);
    }

    internal string DecodePropertyName(in TomlParseEvent parseEvent)
    {
        if (parseEvent.PropertyName is not null)
        {
            return parseEvent.PropertyName;
        }

        if (parseEvent.Span is not { } span)
        {
            return string.Empty;
        }

        var raw = GetSpan(span);
        if (raw.IsEmpty)
        {
            return string.Empty;
        }

        var tokenKind = TomlParseEventData.UnpackPropertyNameTokenKind(parseEvent.Data);
        if (tokenKind == TokenKind.BasicKey)
        {
            return raw.ToString();
        }

        try
        {
            return TomlStringDecoder.Decode(raw, tokenKind);
        }
        catch (FormatException ex)
        {
            throw new TomlException(span, ex.Message, ex);
        }
    }

    /// <summary>
    /// Gets the parser options.
    /// </summary>
    public TomlParserOptions ParserOptions => _parserOptions;

    /// <summary>
    /// Gets diagnostics produced by the parser.
    /// </summary>
    public DiagnosticsBag Diagnostics => _diagnostics ??= new DiagnosticsBag();

    /// <summary>
    /// Gets a value indicating whether diagnostics include errors.
    /// </summary>
    public bool HasErrors => _diagnostics?.HasErrors ?? false;

    /// <summary>
    /// Gets the optional source name associated with the parsed payload.
    /// </summary>
    public string? SourceName => _options.SourceName;

    /// <summary>
    /// Gets the current event depth.
    /// </summary>
    public int Depth => _depth;

    /// <summary>
    /// Advances to the next parse event.
    /// </summary>
    /// <returns><c>true</c> when a parse event is available; otherwise <c>false</c>.</returns>
    /// <exception cref="TomlException">The TOML payload is invalid and <see cref="TomlParserOptions.Mode"/> is <see cref="TomlParserMode.Strict"/>.</exception>
    public bool MoveNext()
    {
        if (_stoppedAtMaxDepth || !_core.MoveNext(out _current))
        {
            return false;
        }

        switch (_current.Kind)
        {
            case TomlParseEventKind.StartTable:
            case TomlParseEventKind.StartArray:
                // MaxDepth limits the depth of the data model, which a table can exceed by one level when it stands for an
                // element of an array of tables
                _depth++;
                _modelDepth++;
                if (_core.ExtraDepth > 0)
                {
                    _modelDepth += _core.ExtraDepth;
                    (_extraDepths ??= []).Add((_depth, _core.ExtraDepth));
                }

                if (_modelDepth > _effectiveMaxDepth)
                {
                    // Tolerant mode records the error and ends the event stream instead of throwing
                    if (_parserOptions.Mode == TomlParserMode.Tolerant && _diagnostics is not null)
                    {
                        var message = TomlDepthHelper.GetMaxDepthExceededMessage(_effectiveMaxDepth);
                        if (_current.Span is { } span)
                        {
                            _diagnostics.Error(new SourceSpan(span.SourceName, new TextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TextPosition(span.End.Offset, span.End.Line, span.End.Column)), message);
                        }
                        else
                        {
                            _diagnostics.Error(new SourceSpan(SourceName ?? string.Empty, default, default), message);
                        }

                        _stoppedAtMaxDepth = true;
                        return false;
                    }

                    TomlDepthHelper.ThrowDepthExceeded(_effectiveMaxDepth, _current.Span);
                }
                break;
            case TomlParseEventKind.EndTable:
            case TomlParseEventKind.EndArray:
                if (_extraDepths is { Count: > 0 } extraDepths && extraDepths[^1].Depth == _depth)
                {
                    _modelDepth -= extraDepths[^1].ExtraDepth;
                    extraDepths.RemoveAt(extraDepths.Count - 1);
                }

                _depth = Math.Max(0, _depth - 1);
                _modelDepth = Math.Max(0, _modelDepth - 1);
                break;
        }

        return true;
    }

    internal string? GetText(TomlSourceSpan span) => _core.GetText(span);

    internal ReadOnlySpan<char> GetSpan(TomlSourceSpan span) => _core.GetSpan(span);

    internal TomlSyntaxTriviaMetadata[]? CurrentLeadingTrivia => _core.LeadingTrivia;

    internal TomlSyntaxTriviaMetadata[]? CurrentTrailingTrivia => _core.TrailingTrivia;

    private interface IParserCore
    {
        bool MoveNext(out TomlParseEvent parseEvent);

        ReadOnlySpan<char> GetSpan(TomlSourceSpan span);

        string? GetText(TomlSourceSpan span);

        TomlSyntaxTriviaMetadata[]? LeadingTrivia { get; }

        TomlSyntaxTriviaMetadata[]? TrailingTrivia { get; }

        // The levels the current StartTable event adds to the data model in addition to its own
        int ExtraDepth { get; }
    }

    private sealed partial class ParserCore : IParserCore
    {
        private readonly TomlParserMode _mode;
        private readonly bool _decodeScalars;
        private readonly bool _eagerStringValues;
        private readonly bool _captureTrivia;
        private readonly DiagnosticsBag? _diagnostics;
        private readonly Lexer _lexer;
        private SyntaxTokenValue _token;
        private bool _initialized;
        private bool _terminatedDueToError;

        private readonly List<TomlSyntaxTriviaMetadata>? _pendingTrivia;
        private string? _pendingWhitespace;
        private TomlSyntaxTriviaMetadata[]? _leadingTrivia;
        private TomlSyntaxTriviaMetadata[]? _trailingTrivia;

        private TomlParseEvent _pendingEvent;
        private bool _hasPendingEvent;
        private TomlSyntaxTriviaMetadata[]? _pendingLeadingTrivia;
        private TomlSyntaxTriviaMetadata[]? _pendingTrailingTrivia;
        private int _pendingExtraDepth;

        private PendingOperationKind _pendingOperation;

        // Pending key/value entry emission (EnsureImplicitPrefix + leaf PropertyName + consume `=`).
        private int _pendingKeyValueImplicitBaseIndex;
        private int _pendingKeyValuePrefixLength;
        private int _pendingKeyValueCommonPrefixLength;
        private int _pendingKeyValueOpenIndex;
        private bool _pendingKeyValueOpenEmitPropertyName;
        private bool _pendingKeyValueLeafEmitted;
        private bool _pendingKeyValueClosingsCompleted;
        private KeySegment _pendingKeyValueLeafSegment;
        private bool _pendingKeyValueHasLeafSegment;

        // Pending table-header emission (Close implicit frames, transition explicit frames).
        private int _pendingTableHeaderExplicitPrefixLength;
        private int _pendingTableHeaderOpenIndex;
        private bool _pendingTableHeaderOpenEmitPropertyName;
        private ExplicitFrame _pendingTableHeaderOpenFrame;
        private bool _pendingTableHeaderHasOpenFrame;
        private bool _pendingTableHeaderClosingsCompleted;

        // The comments before a header and on its line; the comments on the lines after it belong to the first key
        private TomlSyntaxTriviaMetadata[]? _pendingTableHeaderLeadingTrivia;
        private TomlSyntaxTriviaMetadata[]? _pendingTableHeaderTrailingTrivia;
        private int _pendingTableHeaderFrameIndex;

        // Pending document termination.
        private DocumentEndStage _pendingDocumentEndStage;
        private bool _pendingDocumentEndRootTableClosed;
        private bool _pendingDocumentEndDocumentClosed;
        private int _pendingDocumentStartStage;

        private readonly List<KeySegment> _implicitFrames;
        private readonly List<ExplicitFrame> _explicitFrames;
        private readonly List<KeySegment> _pathSegments;
        private readonly List<ExplicitFrame> _targetExplicitFrames;
        private readonly List<ContainerFrame> _containers;

        private DocumentState _state;

        public ParserCore(ReadOnlyMemory<char> text, string sourcePath, TomlParserOptions parserOptions, bool allowDuplicateKeys, DiagnosticsBag? diagnostics)
        {
            _mode = parserOptions.Mode;
            _allowDuplicateKeys = allowDuplicateKeys;
            _decodeScalars = parserOptions.DecodeScalars;
            _eagerStringValues = parserOptions.EagerStringValues;
            _captureTrivia = parserOptions.CaptureTrivia;
            _diagnostics = diagnostics;
            _lexer = new Lexer(text, sourcePath)
            {
                State = LexerState.Key,
                DecodeScalars = _decodeScalars,
                EmitHiddenTokens = _captureTrivia,
                EagerStringValues = _eagerStringValues,
                MaxErrorCount = _mode == TomlParserMode.Strict ? 1 : int.MaxValue,
            };
            _token = default;
            _initialized = false;
            _terminatedDueToError = false;
            _pendingTrivia = _captureTrivia ? new List<TomlSyntaxTriviaMetadata>(capacity: 8) : null;
            _pendingWhitespace = null;
            _leadingTrivia = null;
            _trailingTrivia = null;
            _pendingEvent = default;
            _hasPendingEvent = false;
            _pendingLeadingTrivia = null;
            _pendingTrailingTrivia = null;
            _pendingOperation = PendingOperationKind.None;
            _pendingKeyValueImplicitBaseIndex = 0;
            _pendingKeyValuePrefixLength = 0;
            _pendingKeyValueCommonPrefixLength = 0;
            _pendingKeyValueOpenIndex = 0;
            _pendingKeyValueOpenEmitPropertyName = true;
            _pendingKeyValueLeafEmitted = false;
            _pendingKeyValueClosingsCompleted = false;
            _pendingKeyValueLeafSegment = default;
            _pendingKeyValueHasLeafSegment = false;
            _pendingTableHeaderExplicitPrefixLength = 0;
            _pendingTableHeaderOpenIndex = 0;
            _pendingTableHeaderOpenEmitPropertyName = true;
            _pendingTableHeaderOpenFrame = default;
            _pendingTableHeaderHasOpenFrame = false;
            _pendingTableHeaderClosingsCompleted = false;
            _pendingDocumentEndStage = DocumentEndStage.Containers;
            _pendingDocumentEndRootTableClosed = false;
            _pendingDocumentEndDocumentClosed = false;
            _pendingDocumentStartStage = 0;

            _implicitFrames = new List<KeySegment>(capacity: 8);
            _explicitFrames = new List<ExplicitFrame>(capacity: 16);
            _pathSegments = new List<KeySegment>(capacity: 8);
            _targetExplicitFrames = new List<ExplicitFrame>(capacity: 16);
            _containers = new List<ContainerFrame>(capacity: 8);

            _state = DocumentState.NotStarted;
        }

        public string? GetText(TomlSourceSpan span) => _lexer.GetString(span.Offset, span.Length);

        public ReadOnlySpan<char> GetSpan(TomlSourceSpan span) => _lexer.GetSpanUnchecked(span.Offset, span.Length);

        public TomlSyntaxTriviaMetadata[]? LeadingTrivia => _leadingTrivia;

        public TomlSyntaxTriviaMetadata[]? TrailingTrivia => _trailingTrivia;

        public int ExtraDepth { get; private set; }

        public bool MoveNext(out TomlParseEvent parseEvent)
        {
            if (_hasPendingEvent)
            {
                parseEvent = _pendingEvent;
                _leadingTrivia = _pendingLeadingTrivia;
                _trailingTrivia = _pendingTrailingTrivia;
                ExtraDepth = _pendingExtraDepth;
                _hasPendingEvent = false;
                _pendingLeadingTrivia = null;
                _pendingTrailingTrivia = null;
                _pendingExtraDepth = 0;
                return true;
            }

            while (true)
            {
                bool producedNext;
                try
                {
                    producedNext = ProduceNext();
                }
                catch (TomlException ex) when (_mode == TomlParserMode.Tolerant)
                {
                    if (!_terminatedDueToError)
                    {
                        _terminatedDueToError = true;
                        RecordDiagnostics(ex);
                        BeginDocumentEnd();
                    }

                    producedNext = true;
                }

                if (!producedNext)
                {
                    break;
                }

                if (_hasPendingEvent)
                {
                    parseEvent = _pendingEvent;
                    _leadingTrivia = _pendingLeadingTrivia;
                    _trailingTrivia = _pendingTrailingTrivia;
                    ExtraDepth = _pendingExtraDepth;
                    _hasPendingEvent = false;
                    _pendingLeadingTrivia = null;
                    _pendingTrailingTrivia = null;
                    _pendingExtraDepth = 0;
                    return true;
                }
            }

            parseEvent = default;
            _leadingTrivia = null;
            _trailingTrivia = null;
            return false;
        }

        private bool ProduceNext()
        {
            if (_state == DocumentState.Ended)
            {
                return false;
            }

            if (_state == DocumentState.NotStarted)
            {
                _state = DocumentState.Statement;
                _pendingOperation = PendingOperationKind.DocumentStart;
                _pendingDocumentStartStage = 1;
                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartDocument, span: null, propertyName: null, stringValue: null, data: 0));
                return true;
            }

            // The first token is read once the document and its root table are started, so that an error in it closes them
            // like an error in any other token
            if (_pendingOperation != PendingOperationKind.DocumentStart)
            {
                EnsureInitialized();
            }

            if (_pendingOperation != PendingOperationKind.None)
            {
                if (ProducePendingOperation())
                {
                    return true;
                }
            }

            if (_containers.Count > 0)
            {
                return ProduceContainer();
            }

            return _state switch
            {
                DocumentState.Statement => ProduceStatement(),
                DocumentState.ExpectValue => ProduceKeyValueValue(),
                DocumentState.AfterValue => ProduceAfterValue(),
                _ => throw new InvalidOperationException($"Unexpected parser state {_state}."),
            };
        }

        private void EnsureInitialized()
        {
            if (_initialized)
            {
                return;
            }

            _initialized = true;
            NextToken();
        }

        private void NextToken()
        {
            if (!_captureTrivia)
            {
                var hasToken = _lexer.MoveNext();
                _token = hasToken ? _lexer.Token : new SyntaxTokenValue(TokenKind.Eof, new TextPosition(), new TextPosition());

                if (_lexer.HasErrors)
                {
                    throw new TomlException(new DiagnosticsBag(_lexer.Errors));
                }

                return;
            }

            bool result;
            while (true)
            {
                result = _lexer.MoveNext();
                if (!result)
                {
                    break;
                }

                ref readonly var token = ref _lexer.Token;
                if (!token.Kind.IsHidden(hideNewLine: false))
                {
                    _token = token;
                    goto TokenReady;
                }

                if (!_captureTrivia || _pendingTrivia is null)
                {
                    continue;
                }

                if (token.Kind == TokenKind.Whitespaces)
                {
                    var text = _lexer.GetString(token.Start.Offset, token.End.Offset - token.Start.Offset + 1) ?? string.Empty;
                    _pendingWhitespace = _pendingWhitespace is null ? text : string.Concat(_pendingWhitespace, text);
                    continue;
                }

                if (token.Kind == TokenKind.Comment)
                {
                    if (_pendingWhitespace is not null)
                    {
                        _pendingTrivia.Add(new TomlSyntaxTriviaMetadata(TokenKind.Whitespaces, _pendingWhitespace));
                        _pendingWhitespace = null;
                    }

                    _pendingTrivia.Add(new TomlSyntaxTriviaMetadata(TokenKind.Comment, token.GetText(_lexer.Text.Span) ?? string.Empty));
                    continue;
                }

                _pendingWhitespace = null;
            }

            _pendingWhitespace = null;
            _token = new SyntaxTokenValue(TokenKind.Eof, new TextPosition(), new TextPosition());

        TokenReady:
            _pendingWhitespace = null;

            if (_lexer.HasErrors)
            {
                throw new TomlException(new DiagnosticsBag(_lexer.Errors));
            }
        }

        private void Consume(TokenKind expected, LexerState nextState)
        {
            if (_token.Kind != expected)
            {
                throw ParserCore.CreateException(CurrentSpan(), $"Expected `{expected.ToText() ?? expected.ToString()}` but was `{ToPrintable(_token)}`.");
            }

            if (_captureTrivia && expected == TokenKind.NewLine && _pendingTrivia is { Count: > 0 })
            {
                var last = _pendingTrivia[_pendingTrivia.Count - 1];
                if (last.Kind == TokenKind.Comment)
                {
                    _pendingTrivia.Add(new TomlSyntaxTriviaMetadata(TokenKind.NewLine, _lexer.GetString(_token.Start.Offset, _token.End.Offset - _token.Start.Offset + 1) ?? "\n"));
                }
            }

            _lexer.State = nextState;
            NextToken();
        }

        private void SetPendingEvent(in TomlParseEvent parseEvent, TomlSyntaxTriviaMetadata[]? leadingTrivia = null, TomlSyntaxTriviaMetadata[]? trailingTrivia = null, int extraDepth = 0)
        {
            if (_hasPendingEvent)
            {
                throw new InvalidOperationException("The parser already has a pending event to return.");
            }

            _pendingEvent = parseEvent;
            _pendingLeadingTrivia = leadingTrivia;
            _pendingTrailingTrivia = trailingTrivia;
            _pendingExtraDepth = extraDepth;
            _hasPendingEvent = true;
        }

        private TomlSyntaxTriviaMetadata[]? ExtractPendingTrivia()
        {
            if (!_captureTrivia || _pendingTrivia is null || _pendingTrivia.Count == 0)
            {
                return null;
            }

            var array = _pendingTrivia.ToArray();
            _pendingTrivia.Clear();
            return array;
        }

        private bool ProducePendingOperation()
        {
            return _pendingOperation switch
            {
                PendingOperationKind.DocumentStart => ProducePendingDocumentStart(),
                PendingOperationKind.DocumentEnd => ProducePendingDocumentEnd(),
                PendingOperationKind.TableHeaderTransition => ProducePendingTableHeaderTransition(),
                PendingOperationKind.KeyValueEntry => ProducePendingKeyValueEntry(),
                PendingOperationKind.None => false,
                _ => throw new InvalidOperationException($"Unsupported pending operation kind {_pendingOperation}."),
            };
        }

        private bool ProducePendingDocumentStart()
        {
            if (_pendingDocumentStartStage == 1)
            {
                _pendingDocumentStartStage = 0;
                _pendingOperation = PendingOperationKind.None;
                // The root table starts at the start of the document, where its errors, such as a missing required key, are reported
                var documentStart = new TomlSourceSpan(_lexer.SourcePath, new TomlTextPosition(0, 0, 0), new TomlTextPosition(0, 0, 0));
                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartTable, span: documentStart, propertyName: null, stringValue: null, data: 0));
                return true;
            }

            _pendingOperation = PendingOperationKind.None;
            return false;
        }

        private void BeginDocumentEnd()
        {
            if (_pendingOperation == PendingOperationKind.DocumentEnd || _state == DocumentState.Ended)
            {
                return;
            }

            _pendingOperation = PendingOperationKind.DocumentEnd;
            _pendingDocumentEndStage = DocumentEndStage.Containers;
            _pendingDocumentEndRootTableClosed = false;
            _pendingDocumentEndDocumentClosed = false;
            _state = DocumentState.Terminating;
        }

        // The comments after the last key of the document are the leading trivia of the first event that ends it: the end of
        // the last table, whose last key they follow
        private bool ProducePendingDocumentEnd()
        {
            while (true)
            {
                switch (_pendingDocumentEndStage)
                {
                    case DocumentEndStage.Containers:
                        if (_containers.Count > 0)
                        {
                            var frame = _containers[_containers.Count - 1];
                            if (frame.Kind == ContainerKind.InlineTable && _implicitFrames.Count > frame.InlineImplicitBase)
                            {
                                _implicitFrames.RemoveAt(_implicitFrames.Count - 1);
                                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                                return true;
                            }

                            _containers.RemoveAt(_containers.Count - 1);
                            PopStructureValueScope();
                            SetPendingEvent(frame.Kind == ContainerKind.Array
                                ? new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0)
                                : new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                            return true;
                        }

                        _pendingDocumentEndStage = DocumentEndStage.ImplicitFrames;
                        continue;

                    case DocumentEndStage.ImplicitFrames:
                        if (_implicitFrames.Count > 0)
                        {
                            _implicitFrames.RemoveAt(_implicitFrames.Count - 1);
                            SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                            return true;
                        }

                        _pendingDocumentEndStage = DocumentEndStage.ExplicitFrames;
                        continue;

                    case DocumentEndStage.ExplicitFrames:
                        if (_explicitFrames.Count > 0)
                        {
                            var frame = _explicitFrames[_explicitFrames.Count - 1];
                            _explicitFrames.RemoveAt(_explicitFrames.Count - 1);
                            SetPendingEvent(frame.Kind == ExplicitFrameKind.Array
                                ? new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0)
                                : new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                            return true;
                        }

                        _pendingDocumentEndStage = DocumentEndStage.RootTable;
                        continue;

                    case DocumentEndStage.RootTable:
                        if (!_pendingDocumentEndRootTableClosed)
                        {
                            _pendingDocumentEndRootTableClosed = true;
                            SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                            return true;
                        }

                        _pendingDocumentEndStage = DocumentEndStage.Document;
                        continue;

                    case DocumentEndStage.Document:
                        if (!_pendingDocumentEndDocumentClosed)
                        {
                            _pendingDocumentEndDocumentClosed = true;
                            SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndDocument, span: null, propertyName: null, stringValue: null, data: 0), leadingTrivia: ExtractPendingTrivia());
                            _pendingOperation = PendingOperationKind.None;
                            _state = DocumentState.Ended;
                            ReleaseStructure();
                            return true;
                        }

                        _pendingOperation = PendingOperationKind.None;
                        _state = DocumentState.Ended;
                        return false;

                    default:
                        throw new InvalidOperationException($"Unsupported document end stage {_pendingDocumentEndStage}.");
                }
            }
        }

        private bool ProducePendingTableHeaderTransition()
        {
            if (!_pendingTableHeaderClosingsCompleted)
            {
                if (_implicitFrames.Count > 0)
                {
                    _implicitFrames.RemoveAt(_implicitFrames.Count - 1);
                    SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0));
                    return true;
                }

                if (_explicitFrames.Count > _pendingTableHeaderExplicitPrefixLength)
                {
                    var frame = _explicitFrames[_explicitFrames.Count - 1];
                    _explicitFrames.RemoveAt(_explicitFrames.Count - 1);
                    SetPendingEvent(frame.Kind == ExplicitFrameKind.Array
                        ? new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0)
                        : new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0));
                    return true;
                }

                _pendingTableHeaderClosingsCompleted = true;
            }

            if (_pendingTableHeaderOpenIndex < _targetExplicitFrames.Count)
            {
                if (_pendingTableHeaderHasOpenFrame)
                {
                    var openFrame = _pendingTableHeaderOpenFrame;
                    _pendingTableHeaderHasOpenFrame = false;
                    _pendingTableHeaderOpenEmitPropertyName = true;
                    _pendingTableHeaderOpenIndex++;

                    // The span of the key segment that opens the container, so errors about its type have a location
                    var openSpan = openFrame.Name?.Span;
                    var trailingTrivia = _pendingTableHeaderOpenIndex - 1 == _pendingTableHeaderFrameIndex ? TakeTableHeaderTrivia(ref _pendingTableHeaderTrailingTrivia) : null;
                    SetPendingEvent(openFrame.Kind == ExplicitFrameKind.Array
                        ? new TomlParseEvent(TomlParseEventKind.StartArray, span: openSpan, propertyName: null, stringValue: null, data: 0)
                        : new TomlParseEvent(TomlParseEventKind.StartTable, span: openSpan, propertyName: null, stringValue: null, data: 0),
                        trailingTrivia: trailingTrivia,
                        extraDepth: openFrame.IsTableArrayElement ? 1 : 0);
                    _explicitFrames.Add(openFrame);
                    return true;
                }

                var frame = _targetExplicitFrames[_pendingTableHeaderOpenIndex];
                if (frame.Kind == ExplicitFrameKind.TableArrayElement)
                {
                    // An element follows the frame of its array, which has the name
                    var elementSpan = _pendingTableHeaderOpenIndex > 0 ? _targetExplicitFrames[_pendingTableHeaderOpenIndex - 1].Name?.Span : null;
                    _pendingTableHeaderOpenIndex++;
                    SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartTable, span: elementSpan, propertyName: null, stringValue: null, data: 0));
                    _explicitFrames.Add(frame);
                    return true;
                }

                if (_pendingTableHeaderOpenEmitPropertyName)
                {
                    if (frame.Name is not { } name)
                    {
                        throw ParserCore.CreateException(CurrentSpan(), "Invalid explicit frame without a name.");
                    }

                    _pendingTableHeaderOpenFrame = frame;
                    _pendingTableHeaderHasOpenFrame = true;
                    _pendingTableHeaderOpenEmitPropertyName = false;

                    var leadingTrivia = _pendingTableHeaderOpenIndex == _pendingTableHeaderFrameIndex ? TakeTableHeaderTrivia(ref _pendingTableHeaderLeadingTrivia) : null;
                    SetPendingEvent(new TomlParseEvent(
                        TomlParseEventKind.PropertyName,
                        span: name.Span,
                        propertyName: _decodeScalars ? name.Value : null,
                        stringValue: null,
                        data: TomlParseEventData.PackPropertyName(name.TokenKind, name.Hash)),
                        leadingTrivia: leadingTrivia);
                    return true;
                }

                throw ParserCore.CreateException(CurrentSpan(), "Invalid table header open state.");
            }

            RequeueTableHeaderTrivia();
            _pendingOperation = PendingOperationKind.None;
            return false;
        }

        private static TomlSyntaxTriviaMetadata[]? TakeTableHeaderTrivia(ref TomlSyntaxTriviaMetadata[]? trivia)
        {
            var result = trivia;
            trivia = null;
            return result;
        }

        // A header that reopens a table or adds an element to an array of tables has no key to hold its comments, so they
        // go to the first key of the table instead of being lost
        private void RequeueTableHeaderTrivia()
        {
            if (_pendingTrivia is null || (_pendingTableHeaderLeadingTrivia is null && _pendingTableHeaderTrailingTrivia is null))
            {
                return;
            }

            var trivia = new List<TomlSyntaxTriviaMetadata>();
            trivia.AddRange(TakeTableHeaderTrivia(ref _pendingTableHeaderLeadingTrivia) ?? []);
            if (TakeTableHeaderTrivia(ref _pendingTableHeaderTrailingTrivia) is { } trailingTrivia)
            {
                foreach (var item in trailingTrivia)
                {
                    if (item.Kind != TokenKind.Whitespaces)
                    {
                        trivia.Add(item);
                    }
                }

                trivia.Add(new TomlSyntaxTriviaMetadata(TokenKind.NewLine, "\n"));
            }

            _pendingTrivia.InsertRange(0, trivia);
        }

        private bool ProducePendingKeyValueEntry()
        {
            if (!_pendingKeyValueClosingsCompleted)
            {
                var targetCount = _pendingKeyValueImplicitBaseIndex + _pendingKeyValueCommonPrefixLength;
                if (_implicitFrames.Count > targetCount)
                {
                    _implicitFrames.RemoveAt(_implicitFrames.Count - 1);
                    SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0));
                    return true;
                }

                _pendingKeyValueClosingsCompleted = true;
            }

            if (_pendingKeyValueOpenIndex < _pendingKeyValuePrefixLength)
            {
                var segment = _pathSegments[_pendingKeyValueOpenIndex];
                if (_pendingKeyValueOpenEmitPropertyName)
                {
                    _pendingKeyValueOpenEmitPropertyName = false;
                    SetPendingEvent(new TomlParseEvent(
                        TomlParseEventKind.PropertyName,
                        span: segment.Span,
                        propertyName: _decodeScalars ? segment.Value : null,
                        stringValue: null,
                        data: TomlParseEventData.PackPropertyName(segment.TokenKind, segment.Hash)));
                    return true;
                }

                _pendingKeyValueOpenEmitPropertyName = true;
                _pendingKeyValueOpenIndex++;
                _implicitFrames.Add(segment);
                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartTable, span: segment.Span, propertyName: null, stringValue: null, data: 0));
                return true;
            }

            if (!_pendingKeyValueLeafEmitted)
            {
                _pendingKeyValueLeafEmitted = true;
                var leaf = _pendingKeyValueHasLeafSegment ? _pendingKeyValueLeafSegment : _pathSegments[_pathSegments.Count - 1];
                var leadingTrivia = ExtractPendingTrivia();
                SetPendingEvent(new TomlParseEvent(
                    TomlParseEventKind.PropertyName,
                    span: leaf.Span,
                    propertyName: _decodeScalars ? leaf.Value : null,
                    stringValue: null,
                    data: TomlParseEventData.PackPropertyName(leaf.TokenKind, leaf.Hash)),
                    leadingTrivia: leadingTrivia);
                return true;
            }

            Consume(TokenKind.Equal, LexerState.Value);
            _pendingOperation = PendingOperationKind.None;
            _pendingKeyValueHasLeafSegment = false;
            return false;
        }

        private void SkipNewLines(LexerState lexerState)
        {
            while (_token.Kind == TokenKind.NewLine)
            {
                Consume(TokenKind.NewLine, lexerState);
            }
        }

        private bool ProduceContainer()
        {
            var frame = _containers[_containers.Count - 1];
            return frame.Kind switch
            {
                ContainerKind.Array => ProduceArray(ref frame),
                ContainerKind.InlineTable => ProduceInlineTable(ref frame),
                _ => throw new InvalidOperationException($"Unsupported container kind {frame.Kind}."),
            };
        }

        private bool ProduceStatement()
        {
            while (true)
            {
                SkipNewLines(LexerState.Key);

                if (_token.Kind == TokenKind.Eof)
                {
                    BeginDocumentEnd();
                    return true;
                }

                if (_token.Kind == TokenKind.OpenBracket || _token.Kind == TokenKind.OpenBracketDouble)
                {
                    ParseTableHeader(isTableArray: _token.Kind == TokenKind.OpenBracketDouble);
                    return true;
                }

                if (_token.Kind == TokenKind.BasicKey || _token.Kind.IsString())
                {
                    BeginKeyValueEntry(implicitBaseIndex: 0);
                    _state = DocumentState.ExpectValue;
                    return true;
                }

                throw ParserCore.CreateException(CurrentSpan(), $"Unexpected token `{ToPrintable(_token)}` while parsing TOML document.");
            }
        }

        private bool ProduceKeyValueValue()
        {
            if (_token.Kind == TokenKind.NewLine || _token.Kind == TokenKind.Eof)
            {
                throw ParserCore.CreateException(CurrentSpan(), "Missing value after `=`.");
            }

            ProduceValueEvent(_token.Start, nextStateAfterScalar: LexerState.Key);
            _state = DocumentState.AfterValue;
            return true;
        }

        private bool ProduceAfterValue()
        {
            if (_token.Kind == TokenKind.NewLine)
            {
                Consume(TokenKind.NewLine, LexerState.Key);
                _state = DocumentState.Statement;
                return true;
            }

            if (_token.Kind == TokenKind.Eof)
            {
                _state = DocumentState.Statement;
                return true;
            }

            throw ParserCore.CreateException(CurrentSpan(), $"Expected end-of-line after value but was `{ToPrintable(_token)}`.");
        }

        private bool ProduceArray(ref ContainerFrame frame)
        {
            var frameIndex = _containers.Count - 1;
            while (true)
            {
                SkipNewLines(LexerState.Value);

                if (_token.Kind == TokenKind.Eof)
                {
                    throw ParserCore.CreateException(CurrentSpan(), "Unexpected end-of-file while parsing array.");
                }

                if (frame.ArrayState == ArrayState.ExpectValueOrEnd)
                {
                    if (_token.Kind == TokenKind.CloseBracket)
                    {
                        var trailingTrivia = ConsumeContainerClose(TokenKind.CloseBracket);
                        _containers.RemoveAt(_containers.Count - 1);
                        PopStructureValueScope();
                        SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), trailingTrivia: trailingTrivia);
                        return true;
                    }

                    if (_token.Kind == TokenKind.Comma)
                    {
                        throw ParserCore.CreateException(CurrentSpan(), "Missing value in array.");
                    }

                    ProduceValueEvent(_token.Start, nextStateAfterScalar: LexerState.Value);
                    frame.ArrayState = ArrayState.AfterValue;
                    _containers[frameIndex] = frame;
                    return true;
                }

                // AfterValue
                SkipNewLines(LexerState.Value);

                if (_token.Kind == TokenKind.Comma)
                {
                    Consume(TokenKind.Comma, LexerState.Value);
                    SkipNewLines(LexerState.Value);
                    if (_token.Kind == TokenKind.CloseBracket)
                    {
                        var trailingTrivia = ConsumeContainerClose(TokenKind.CloseBracket);
                        _containers.RemoveAt(_containers.Count - 1);
                        PopStructureValueScope();
                        SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), trailingTrivia: trailingTrivia);
                        return true;
                    }

                    frame.ArrayState = ArrayState.ExpectValueOrEnd;
                    _containers[frameIndex] = frame;
                    continue;
                }

                if (_token.Kind == TokenKind.CloseBracket)
                {
                    var trailingTrivia = ConsumeContainerClose(TokenKind.CloseBracket);
                    _containers.RemoveAt(_containers.Count - 1);
                    PopStructureValueScope();
                    SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndArray, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), trailingTrivia: trailingTrivia);
                    return true;
                }

                throw ParserCore.CreateException(CurrentSpan(), $"Expected `,` or `]` while parsing array but was `{ToPrintable(_token)}`.");
            }
        }

        private bool ProduceInlineTable(ref ContainerFrame frame)
        {
            var frameIndex = _containers.Count - 1;
            while (true)
            {
                // A key/value pair is on a single line: newlines are allowed between the pairs, not after `=`
                if (frame.InlineTableState != InlineTableState.ExpectValue)
                {
                    SkipNewLines(LexerState.Key);
                }

                if (_token.Kind == TokenKind.Eof)
                {
                    throw ParserCore.CreateException(CurrentSpan(), "Unexpected end-of-file while parsing inline table.");
                }

                if (frame.InlineTableState == InlineTableState.ExpectKeyOrEnd)
                {
                    if (_token.Kind == TokenKind.CloseBrace)
                    {
                        if (_implicitFrames.Count > frame.InlineImplicitBase)
                        {
                            _implicitFrames.RemoveAt(_implicitFrames.Count - 1);
                            SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0));
                            return true;
                        }

                        var trailingTrivia = ConsumeContainerClose(TokenKind.CloseBrace);
                        _containers.RemoveAt(_containers.Count - 1);
                        PopStructureValueScope();
                        SetPendingEvent(new TomlParseEvent(TomlParseEventKind.EndTable, span: CurrentSpan(), propertyName: null, stringValue: null, data: 0), trailingTrivia: trailingTrivia);
                        return true;
                    }

                    if (_token.Kind != TokenKind.BasicKey && !_token.Kind.IsString())
                    {
                        throw ParserCore.CreateException(CurrentSpan(), $"Expected a key or `}}` while parsing inline table but was `{ToPrintable(_token)}`.");
                    }

                    BeginKeyValueEntry(implicitBaseIndex: frame.InlineImplicitBase);
                    frame.InlineTableState = InlineTableState.ExpectValue;
                    _containers[frameIndex] = frame;
                    return true;
                }

                if (frame.InlineTableState == InlineTableState.ExpectValue)
                {
                    if (_token.Kind is TokenKind.Comma or TokenKind.CloseBrace or TokenKind.NewLine)
                    {
                        throw ParserCore.CreateException(CurrentSpan(), "Missing value after `=` in inline table.");
                    }

                    ProduceValueEvent(_token.Start, nextStateAfterScalar: LexerState.Key);
                    frame.InlineTableState = InlineTableState.AfterValue;
                    _containers[frameIndex] = frame;
                    return true;
                }

                // AfterValue
                SkipNewLines(LexerState.Key);
                if (_token.Kind == TokenKind.Comma)
                {
                    Consume(TokenKind.Comma, LexerState.Key);
                    frame.InlineTableState = InlineTableState.ExpectKeyOrEnd;
                    _containers[frameIndex] = frame;
                    continue;
                }

                if (_token.Kind == TokenKind.CloseBrace)
                {
                    frame.InlineTableState = InlineTableState.ExpectKeyOrEnd;
                    _containers[frameIndex] = frame;
                    continue;
                }

                throw ParserCore.CreateException(CurrentSpan(), $"Expected `,` or `}}` while parsing inline table but was `{ToPrintable(_token)}`.");
            }
        }

        // The trivia before the closing token is inside the container, where the model cannot keep it. The trivia after it is the
        // trailing comment of the value, as for a scalar.
        private TomlSyntaxTriviaMetadata[]? ConsumeContainerClose(TokenKind closeTokenKind)
        {
            _pendingTrivia?.Clear();
            Consume(closeTokenKind, DetermineLexerStateAfterContainerClose());
            return ExtractPendingTrivia();
        }

        private LexerState DetermineLexerStateAfterContainerClose()
        {
            if (_containers.Count < 2)
            {
                return LexerState.Key;
            }

            var parent = _containers[_containers.Count - 2];
            return parent.Kind == ContainerKind.Array ? LexerState.Value : LexerState.Key;
        }

        private void ParseTableHeader(bool isTableArray)
        {
            _pendingTableHeaderLeadingTrivia = ExtractPendingTrivia();
            Consume(isTableArray ? TokenKind.OpenBracketDouble : TokenKind.OpenBracket, LexerState.Key);

            _pathSegments.Clear();
            ParseKeyPath(_pathSegments, out _);
            if (_pathSegments.Count == 0)
            {
                throw ParserCore.CreateException(CurrentSpan(), "Invalid table header.");
            }

            Consume(isTableArray ? TokenKind.CloseBracketDouble : TokenKind.CloseBracket, LexerState.Key);
            _pendingTableHeaderTrailingTrivia = ExtractPendingTrivia();

            if (_token.Kind == TokenKind.NewLine)
            {
                Consume(TokenKind.NewLine, LexerState.Key);
            }
            else if (_token.Kind != TokenKind.Eof)
            {
                throw ParserCore.CreateException(CurrentSpan(), $"Expected end-of-line after table header but was `{ToPrintable(_token)}`.");
            }

            DefineStructureTableHeader(_pathSegments, isTableArray);

            _targetExplicitFrames.Clear();
            BuildExplicitTargetFrames(_pathSegments, isTableArray, _explicitFrames, _targetExplicitFrames);
            var prefixLength = GetExplicitCommonPrefixLength(_targetExplicitFrames, isTableArray);

            _pendingTableHeaderExplicitPrefixLength = prefixLength;
            _pendingTableHeaderOpenIndex = prefixLength;
            _pendingTableHeaderFrameIndex = _targetExplicitFrames.Count - (isTableArray ? 2 : 1);
            _pendingTableHeaderOpenEmitPropertyName = true;
            _pendingTableHeaderHasOpenFrame = false;
            _pendingTableHeaderClosingsCompleted = false;
            _pendingOperation = PendingOperationKind.TableHeaderTransition;
        }

        private void BeginKeyValueEntry(int implicitBaseIndex)
        {
            _pendingKeyValueHasLeafSegment = false;

            // Parse the first key segment without going through the list-based path (fast-path for non-dotted keys).
            ParseKeySegment(out var firstSegment);
            if (_token.Kind != TokenKind.Dot)
            {
                if (_token.Kind != TokenKind.Equal)
                {
                    throw ParserCore.CreateException(CurrentSpan(), $"Expected `=` after key but was `{ToPrintable(_token)}`.");
                }

                DefineStructureKey(path: null, firstSegment);

                // Common case: a single-segment key/value entry at the current implicit frame depth.
                if (_implicitFrames.Count == implicitBaseIndex)
                {
                    _pendingKeyValueImplicitBaseIndex = implicitBaseIndex;
                    _pendingKeyValuePrefixLength = 0;
                    _pendingKeyValueCommonPrefixLength = 0;
                    _pendingKeyValueOpenIndex = 0;
                    _pendingKeyValueOpenEmitPropertyName = false;
                    _pendingKeyValueLeafEmitted = true;
                    _pendingKeyValueClosingsCompleted = true;
                    _pendingKeyValueLeafSegment = firstSegment;
                    _pendingKeyValueHasLeafSegment = true;
                    _pendingOperation = PendingOperationKind.KeyValueEntry;

                    var leadingTrivia = ExtractPendingTrivia();
                    SetPendingEvent(new TomlParseEvent(
                        TomlParseEventKind.PropertyName,
                        span: firstSegment.Span,
                        propertyName: _decodeScalars ? firstSegment.Value : null,
                        stringValue: null,
                        data: TomlParseEventData.PackPropertyName(firstSegment.TokenKind, firstSegment.Hash)),
                        leadingTrivia: leadingTrivia);
                    return;
                }

                _pendingKeyValueImplicitBaseIndex = implicitBaseIndex;
                _pendingKeyValuePrefixLength = 0;
                _pendingKeyValueCommonPrefixLength = 0;
                _pendingKeyValueOpenIndex = 0;
                _pendingKeyValueOpenEmitPropertyName = true;
                _pendingKeyValueLeafEmitted = false;
                _pendingKeyValueClosingsCompleted = false;
                _pendingKeyValueLeafSegment = firstSegment;
                _pendingKeyValueHasLeafSegment = true;
                _pendingOperation = PendingOperationKind.KeyValueEntry;
                return;
            }

            _pathSegments.Clear();
            _pathSegments.Add(firstSegment);
            while (_token.Kind == TokenKind.Dot)
            {
                Consume(TokenKind.Dot, LexerState.Key);
                ParseKeySegment(out var segment);
                _pathSegments.Add(segment);
            }

            if (_pathSegments.Count == 0)
            {
                throw ParserCore.CreateException(CurrentSpan(), "Invalid key/value entry.");
            }

            if (_token.Kind != TokenKind.Equal)
            {
                throw ParserCore.CreateException(CurrentSpan(), $"Expected `=` after key but was `{ToPrintable(_token)}`.");
            }

            DefineStructureKey(_pathSegments, default);

            var prefixLength = _pathSegments.Count - 1;
            var common = 0;
            var currentCount = _implicitFrames.Count - implicitBaseIndex;
            while (common < currentCount && common < prefixLength &&
                   KeySegmentsEqual(_implicitFrames[implicitBaseIndex + common], _pathSegments[common]))
            {
                common++;
            }

            _pendingKeyValueImplicitBaseIndex = implicitBaseIndex;
            _pendingKeyValuePrefixLength = prefixLength;
            _pendingKeyValueCommonPrefixLength = common;
            _pendingKeyValueOpenIndex = common;
            _pendingKeyValueOpenEmitPropertyName = true;
            _pendingKeyValueLeafEmitted = false;
            _pendingKeyValueClosingsCompleted = false;
            _pendingOperation = PendingOperationKind.KeyValueEntry;
        }

        private void BuildExplicitTargetFrames(List<KeySegment> path, bool isTableArray, List<ExplicitFrame> currentFrames, List<ExplicitFrame> frames)
        {
            var explicitIndex = 0;
            var reuse = true;

            for (var i = 0; i < path.Count - 1; i++)
            {
                var segment = path[i];
                if (reuse && TryConsumeCurrentFrame(currentFrames, ref explicitIndex, segment, frames))
                {
                    continue;
                }

                // A table in the path that is an array of tables that is not open is written as a table, which the reader merges
                // into the last element: the array and the element are two levels of the data model
                reuse = false;
                frames.Add(new ExplicitFrame(ExplicitFrameKind.Table, segment, isTableArrayElement: _headerPrefixIsTableArray[i]));
            }

            var last = path[path.Count - 1];
            if (isTableArray)
            {
                frames.Add(new ExplicitFrame(ExplicitFrameKind.Array, last));
                frames.Add(new ExplicitFrame(ExplicitFrameKind.TableArrayElement, name: null));
                return;
            }

            frames.Add(new ExplicitFrame(ExplicitFrameKind.Table, last));
        }

        private bool TryConsumeCurrentFrame(List<ExplicitFrame> currentFrames, ref int explicitIndex, KeySegment segment, List<ExplicitFrame> targetFrames)
        {
            if (explicitIndex >= currentFrames.Count)
            {
                return false;
            }

            var current = currentFrames[explicitIndex];
            if (current.Kind == ExplicitFrameKind.Table && current.Name is { } tableName && KeySegmentsEqual(tableName, segment))
            {
                targetFrames.Add(current);
                explicitIndex++;
                return true;
            }

            if (current.Kind == ExplicitFrameKind.Array && current.Name is { } arrayName && KeySegmentsEqual(arrayName, segment))
            {
                var elementIndex = explicitIndex + 1;
                if (elementIndex < currentFrames.Count && currentFrames[elementIndex].Kind == ExplicitFrameKind.TableArrayElement)
                {
                    targetFrames.Add(current);
                    targetFrames.Add(currentFrames[elementIndex]);
                    explicitIndex += 2;
                    return true;
                }
            }

            return false;
        }

        private int GetExplicitCommonPrefixLength(List<ExplicitFrame> targetFrames, bool isTableArray)
        {
            var max = Math.Min(_explicitFrames.Count, targetFrames.Count);
            var common = 0;
            while (common < max && FramesEqual(_explicitFrames[common], targetFrames[common]))
            {
                common++;
            }

            if (isTableArray && targetFrames.Count >= 2)
            {
                if (common == targetFrames.Count && targetFrames[targetFrames.Count - 1].Kind == ExplicitFrameKind.TableArrayElement)
                {
                    common = targetFrames.Count - 1;
                }
                else if (common >= targetFrames.Count - 1 && FramesEqualSequence(_explicitFrames, targetFrames, targetFrames.Count - 1))
                {
                    common = targetFrames.Count - 1;
                }
            }

            return common;
        }

        private bool FramesEqual(ExplicitFrame left, ExplicitFrame right)
        {
            if (left.Kind != right.Kind)
            {
                return false;
            }

            if (left.Kind == ExplicitFrameKind.TableArrayElement)
            {
                return true;
            }

            if (left.Name is not { } leftName || right.Name is not { } rightName)
            {
                return false;
            }

            return KeySegmentsEqual(leftName, rightName);
        }

        // The hash is only a fast filter: two different keys can have the same hash
        private bool KeySegmentsEqual(in KeySegment left, in KeySegment right)
        {
            if (left.Hash != right.Hash)
            {
                return false;
            }

            if (left.Value is not null && right.Value is not null)
            {
                return string.Equals(left.Value, right.Value, StringComparison.Ordinal);
            }

            return GetDecodedKey(left).SequenceEqual(GetDecodedKey(right));
        }

        private ReadOnlySpan<char> GetDecodedKey(in KeySegment segment) => GetDecodedKey(segment.TokenKind, segment.Span.Offset, segment.Span.Length, segment.Value);

        private ReadOnlySpan<char> GetDecodedKey(TokenKind tokenKind, int offset, int length, string? value)
        {
            if (value is not null)
            {
                return value;
            }

            var raw = _lexer.GetSpanUnchecked(offset, length);
            switch (tokenKind)
            {
                case TokenKind.BasicKey:
                    return raw;
                case TokenKind.StringLiteral:
                    return raw.Slice(1, raw.Length - 2);
                default:
                    var content = raw.Slice(1, raw.Length - 2);
                    if (!content.Contains('\\'))
                    {
                        return content;
                    }

                    return TomlStringDecoder.Decode(raw, tokenKind);
            }
        }

        private bool FramesEqualSequence(List<ExplicitFrame> current, List<ExplicitFrame> target, int length)
        {
            if (current.Count < length || target.Count < length)
            {
                return false;
            }

            for (var i = 0; i < length; i++)
            {
                if (!FramesEqual(current[i], target[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private void ProduceValueEvent(TextPosition spanStart, LexerState nextStateAfterScalar)
        {
            if (_token.Kind == TokenKind.OpenBracket)
            {
                var span = new TomlSourceSpan(_lexer.SourcePath,
                    new TomlTextPosition(spanStart.Offset, spanStart.Line, spanStart.Column),
                    new TomlTextPosition(_token.End.Offset, _token.End.Line, _token.End.Column));
                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartArray, span: span, propertyName: null, stringValue: null, data: TomlParseEventData.InlineContainer));
                Consume(TokenKind.OpenBracket, LexerState.Value);
                _containers.Add(new ContainerFrame(ContainerKind.Array, inlineImplicitBase: 0));
                PushStructureValueScope(isInlineTable: false);
                return;
            }

            if (_token.Kind == TokenKind.OpenBrace)
            {
                var span = new TomlSourceSpan(_lexer.SourcePath,
                    new TomlTextPosition(spanStart.Offset, spanStart.Line, spanStart.Column),
                    new TomlTextPosition(_token.End.Offset, _token.End.Line, _token.End.Column));
                SetPendingEvent(new TomlParseEvent(TomlParseEventKind.StartTable, span: span, propertyName: null, stringValue: null, data: TomlParseEventData.InlineContainer));
                Consume(TokenKind.OpenBrace, LexerState.Key);
                _containers.Add(new ContainerFrame(ContainerKind.InlineTable, inlineImplicitBase: _implicitFrames.Count));
                PushStructureValueScope(isInlineTable: true);
                return;
            }

            _pendingStructureValue = -1;
            var eventSpan = new TomlSourceSpan(_lexer.SourcePath,
                new TomlTextPosition(spanStart.Offset, spanStart.Line, spanStart.Column),
                new TomlTextPosition(_token.End.Offset, _token.End.Line, _token.End.Column));

            if (_token.Kind.IsString())
            {
                var tokenKind = _token.Kind;
                var text = _token.StringValue;
                var parseEvent = new TomlParseEvent(TomlParseEventKind.String, span: eventSpan, propertyName: null, stringValue: text, data: (ulong)tokenKind);
                Consume(tokenKind, nextStateAfterScalar);
                var trailingTrivia = ExtractPendingTrivia();
                SetPendingEvent(parseEvent, trailingTrivia: trailingTrivia);
                return;
            }

            if (_token.Kind.IsInteger())
            {
                var tokenKind = _token.Kind;
                var parseEvent = new TomlParseEvent(TomlParseEventKind.Integer, span: eventSpan, propertyName: null, stringValue: null, data: _token.Data);
                Consume(tokenKind, nextStateAfterScalar);
                var trailingTrivia = ExtractPendingTrivia();
                SetPendingEvent(parseEvent, trailingTrivia: trailingTrivia);
                return;
            }

            if (_token.Kind.IsFloat())
            {
                var tokenKind = _token.Kind;
                var parseEvent = new TomlParseEvent(TomlParseEventKind.Float, span: eventSpan, propertyName: null, stringValue: null, data: _token.Data);
                Consume(tokenKind, nextStateAfterScalar);
                var trailingTrivia = ExtractPendingTrivia();
                SetPendingEvent(parseEvent, trailingTrivia: trailingTrivia);
                return;
            }

            if (_token.Kind == TokenKind.True || _token.Kind == TokenKind.False)
            {
                var tokenKind = _token.Kind;
                var parseEvent = new TomlParseEvent(TomlParseEventKind.Boolean, span: eventSpan, propertyName: null, stringValue: null, data: _token.Data);
                Consume(tokenKind, nextStateAfterScalar);
                var trailingTrivia = ExtractPendingTrivia();
                SetPendingEvent(parseEvent, trailingTrivia: trailingTrivia);
                return;
            }

            if (_token.Kind.IsDateTime())
            {
                var tokenKind = _token.Kind;
                var literal = _token.StringValue ?? _lexer.GetString(_token.Start.Offset, _token.End.Offset - _token.Start.Offset + 1) ?? string.Empty;
                var parseEvent = new TomlParseEvent(TomlParseEventKind.DateTime, span: eventSpan, propertyName: null, stringValue: literal, data: 0);
                Consume(tokenKind, nextStateAfterScalar);
                var trailingTrivia = ExtractPendingTrivia();
                SetPendingEvent(parseEvent, trailingTrivia: trailingTrivia);
                return;
            }

            throw ParserCore.CreateException(CurrentSpan(), $"Unexpected token `{ToPrintable(_token)}` while parsing a value.");
        }

        private void ParseKeyPath(List<KeySegment> segments, out TomlSourceSpan leafSpan)
        {
            leafSpan = ParseKeySegment(out var firstSegment);
            segments.Add(firstSegment);
            while (_token.Kind == TokenKind.Dot)
            {
                Consume(TokenKind.Dot, LexerState.Key);
                leafSpan = ParseKeySegment(out var segment);
                segments.Add(segment);
            }
        }

        private TomlSourceSpan ParseKeySegment(out KeySegment segment)
        {
            if (_token.Kind == TokenKind.BasicKey)
            {
                var span = CurrentSpan();
                var hash = _token.Data;
                var text = _decodeScalars ? _lexer.GetString(_token.Start.Offset, _token.End.Offset - _token.Start.Offset + 1) : null;
                Consume(TokenKind.BasicKey, LexerState.Key);
                segment = new KeySegment(TokenKind.BasicKey, span, hash, text);
                return span;
            }

            if (_token.Kind.IsString())
            {
                var span = CurrentSpan();
                var tokenKind = _token.Kind;
                var hash = ComputeKeySegmentHash(tokenKind, _token.Start, _token.End);
                var text = _decodeScalars ? (_token.StringValue ?? string.Empty) : null;
                Consume(_token.Kind, LexerState.Key);
                segment = new KeySegment(tokenKind, span, hash, text);
                return span;
            }

            throw ParserCore.CreateException(CurrentSpan(), $"Unexpected token `{ToPrintable(_token)}` while parsing a key.");
        }

        private ulong ComputeKeySegmentHash(TokenKind tokenKind, TextPosition start, TextPosition end)
        {
            var startOffset = start.Offset;
            var endExclusive = end.Offset + 1;

            if (tokenKind == TokenKind.BasicKey)
            {
                return HashRaw(_lexer.GetSpanUnchecked(startOffset, endExclusive - startOffset));
            }

            if (tokenKind == TokenKind.StringLiteral)
            {
                // Skip the surrounding apostrophes.
                return HashRaw(_lexer.GetSpanUnchecked(startOffset + 1, end.Offset - (startOffset + 1)));
            }

            if (tokenKind == TokenKind.String)
            {
                // Skip the surrounding quotation marks and decode escapes.
                return HashBasicString(_lexer.GetSpanUnchecked(startOffset + 1, end.Offset - (startOffset + 1)));
            }

            throw ParserCore.CreateException(CurrentSpan(), $"Unexpected key token kind `{tokenKind}`.");
        }

        private static bool TryReadNextCodePoint(ReadOnlySpan<char> span, ref int position, out int codePoint)
        {
            if ((uint)position >= (uint)span.Length)
            {
                codePoint = 0;
                return false;
            }

            ref var start = ref unsafe(MemoryMarshal.GetReference(span));
            var c1 = unsafe(Unsafe.Add(ref start, position++));

            // Fast surrogate checks (avoid char.IsHighSurrogate/IsLowSurrogate)
            if (((uint)c1 & 0xFC00u) == 0xD800u)
            {
                // High surrogate; requires a following low surrogate.
                if ((uint)position < (uint)span.Length)
                {
                    var c2 = unsafe(Unsafe.Add(ref start, position));
                    if (((uint)c2 & 0xFC00u) == 0xDC00u)
                    {
                        position++;
                        codePoint = char.ConvertToUtf32(c1, c2);
                        return true;
                    }

                    // Invalid pair: the next code unit is another character
                    codePoint = 0xFFFD;
                    return true;
                }

                // Unexpected EOF after a high surrogate.
                position = span.Length;
                codePoint = 0xFFFD;
                return true;
            }

            // Lone low surrogate is invalid.
            if (((uint)c1 & 0xFC00u) == 0xDC00u)
            {
                codePoint = 0xFFFD;
                return true;
            }

            codePoint = c1;
            return true;
        }

        private static ulong HashRaw(ReadOnlySpan<char> span)
        {
            var hash = 14695981039346656037UL;
            var position = 0;
            while (TryReadNextCodePoint(span, ref position, out var codePoint))
            {
                hash = HashAdd(hash, codePoint);
            }

            return hash;
        }

        private ulong HashBasicString(ReadOnlySpan<char> span)
        {
            var hash = 14695981039346656037UL;
            var position = 0;
            while (TryReadNextCodePoint(span, ref position, out var codePoint))
            {
                if (codePoint != '\\')
                {
                    hash = HashAdd(hash, codePoint);
                    continue;
                }

                if ((uint)position >= (uint)span.Length)
                {
                    throw ParserCore.CreateException(CurrentSpan(), "Invalid escape sequence in key.");
                }

                var escapeCode = span[position++];
                hash = HashAdd(hash, DecodeEscape(span, ref position, escapeCode));
            }

            return hash;
        }

        private int DecodeEscape(ReadOnlySpan<char> span, ref int position, int escapeCode)
        {
            switch (escapeCode)
            {
                case '"': return '"';
                case '\\': return '\\';
                case 'b': return '\b';
                case 'e': return 0x1B;
                case 'f': return '\f';
                case 'n': return '\n';
                case 'r': return '\r';
                case 't': return '\t';
                case 'x':
                    return ReadHex(span, ref position, digits: 2);
                case 'u':
                    return ReadHex(span, ref position, digits: 4);
                case 'U':
                    return ReadHex(span, ref position, digits: 8);
                default:
                    throw ParserCore.CreateException(CurrentSpan(), $"Unexpected escape character `{(char)escapeCode}` in key.");
            }
        }

        private int ReadHex(ReadOnlySpan<char> span, ref int position, int digits)
        {
            var value = 0;
            for (var i = 0; i < digits; i++)
            {
                if ((uint)position >= (uint)span.Length)
                {
                    throw ParserCore.CreateException(CurrentSpan(), "Incomplete hexadecimal escape sequence in key.");
                }

                var c = span[position++];
                var digit = c switch
                {
                    >= '0' and <= '9' => c - '0',
                    >= 'a' and <= 'f' => 10 + (c - 'a'),
                    >= 'A' and <= 'F' => 10 + (c - 'A'),
                    _ => -1,
                };

                if (digit < 0)
                {
                    throw ParserCore.CreateException(CurrentSpan(), $"Invalid hex digit `{(char)c}` in key escape sequence.");
                }

                value = (value << 4) | digit;
            }

            return value;
        }

        private static ulong HashAdd(ulong hash, int codePoint)
        {
            hash ^= unchecked((uint)codePoint);
            return hash * 1099511628211UL;
        }

        private static TomlException CreateException(TomlSourceSpan span, string message) => new TomlException(span, message);

        private TomlSourceSpan CurrentSpan()
            => new TomlSourceSpan(_lexer.SourcePath,
                new TomlTextPosition(_token.Start.Offset, _token.Start.Line, _token.Start.Column),
                new TomlTextPosition(_token.End.Offset, _token.End.Line, _token.End.Column));

        private string ToPrintable(SyntaxTokenValue token)
        {
            var span = new TomlSourceSpan(_lexer.SourcePath,
                new TomlTextPosition(token.Start.Offset, token.Start.Line, token.Start.Column),
                new TomlTextPosition(token.End.Offset, token.End.Line, token.End.Column));
            return _lexer.GetString(span.Offset, span.Length).ToPrintableInputText() ?? token.Kind.ToString();
        }

        private void RecordDiagnostics(TomlException exception)
        {
            if (_diagnostics is null)
            {
                return;
            }

            if (exception.Diagnostics.Count > 0)
            {
                _diagnostics.AddRange(exception.Diagnostics);
                return;
            }

            if (exception.Span is { } span)
            {
                _diagnostics.Error(new SourceSpan(span.SourceName, new TextPosition(span.Start.Offset, span.Start.Line, span.Start.Column), new TextPosition(span.End.Offset, span.End.Line, span.End.Column)), exception.Message);
            }
        }

        private enum PendingOperationKind
        {
            None = 0,
            DocumentStart = 1,
            KeyValueEntry = 2,
            TableHeaderTransition = 3,
            DocumentEnd = 4,
        }

        private enum DocumentEndStage
        {
            Containers = 0,
            ImplicitFrames = 1,
            ExplicitFrames = 2,
            RootTable = 3,
            Document = 4,
        }

        private enum DocumentState
        {
            NotStarted = 0,
            Statement = 1,
            ExpectValue = 2,
            AfterValue = 3,
            Terminating = 4,
            Ended = 5,
        }

        private enum ContainerKind
        {
            Array = 0,
            InlineTable = 1,
        }

        private enum ArrayState
        {
            ExpectValueOrEnd = 0,
            AfterValue = 1,
        }

        private enum InlineTableState
        {
            ExpectKeyOrEnd = 0,
            ExpectValue = 1,
            AfterValue = 2,
        }

        private readonly struct KeySegment
        {
            public KeySegment(TokenKind tokenKind, TomlSourceSpan span, ulong hash, string? value)
            {
                TokenKind = tokenKind;
                Span = span;
                Hash = hash;
                Value = value;
            }

            public TokenKind TokenKind { get; }

            public TomlSourceSpan Span { get; }

            public ulong Hash { get; }

            public string? Value { get; }
        }

        [StructLayout(LayoutKind.Auto)]
        private struct ContainerFrame
        {
            public ContainerFrame(ContainerKind kind, int inlineImplicitBase)
            {
                Kind = kind;
                InlineImplicitBase = inlineImplicitBase;
                ArrayState = ArrayState.ExpectValueOrEnd;
                InlineTableState = InlineTableState.ExpectKeyOrEnd;
            }

            public ContainerKind Kind { get; }

            public int InlineImplicitBase { get; }

            public ArrayState ArrayState { get; set; }

            public InlineTableState InlineTableState { get; set; }
        }

        private enum ExplicitFrameKind
        {
            Table = 0,
            Array = 1,
            TableArrayElement = 2,
        }

        private readonly struct ExplicitFrame
        {
            public ExplicitFrame(ExplicitFrameKind kind, KeySegment? name, bool isTableArrayElement = false)
            {
                Kind = kind;
                Name = name;
                IsTableArrayElement = isTableArrayElement;
            }

            public ExplicitFrameKind Kind { get; }

            public KeySegment? Name { get; }

            // A table that stands for the last element of an array of tables that is not open
            public bool IsTableArrayElement { get; }
        }
    }
}
