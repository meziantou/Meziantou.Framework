using System.Diagnostics;
using System.Text.RegularExpressions;
using Meziantou.Framework.Yaml.Events;
using TagDirective = Meziantou.Framework.Yaml.Tokens.TagDirective;
using VersionDirective = Meziantou.Framework.Yaml.Tokens.VersionDirective;

namespace Meziantou.Framework.Yaml;

/// <summary>Emits YAML streams.</summary>
public partial class Emitter : IEmitter
{
    private readonly TextWriter _output;

    private readonly bool _isCanonical;
    private readonly int _bestIndent;
    private readonly int _bestWidth;
    private EmitterState _state;

    private readonly Stack<EmitterState> _states = new Stack<EmitterState>();
    private readonly Queue<ParsingEvent> _events = new Queue<ParsingEvent>();
    private readonly Stack<int> _indents = new Stack<int>();
    private readonly TagDirectiveCollection _tagDirectives = new TagDirectiveCollection();
    private int _indent;
    private int _flowLevel;
    private bool _isMappingContext;
    private bool _isSimpleKeyContext;
    private bool _isRootContext;

    private int _column;
    private bool _isWhitespace;
    private bool _isIndentation;
    private readonly bool _emitKeyQuoted;

    private bool _isOpenEnded;

    private readonly MutableStringLookAheadBuffer _buffer = new MutableStringLookAheadBuffer();


    private class MutableStringLookAheadBuffer : ILookAheadBuffer
    {
        private string? _value;

        public string? Value
        {
            get { return _value; }
            set
            {
                _value = value;
                Position = 0;
            }
        }

        public int Length { get { return _value?.Length ?? 0; } }

        public int Position { get; private set; }

        public bool IsOutside(int index)
        {
            return _value == null || index >= _value.Length;
        }

        public bool EndOfInput { get { return IsOutside(Position); } }

        public MutableStringLookAheadBuffer() { }

        public char Peek(int offset)
        {
            Debug.Assert(_value is not null);
            int index = Position + offset;
            return _value[index];
        }

        public void Skip(int length)
        {
            if (length < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(length), "The length must be positive.");
            }
            Position += length;
        }

        public void Cache(int length) { }
    }

    private struct AnchorData
    {
        public string? Anchor;
        public bool IsAlias;
    }

    private AnchorData _anchorData;

    private struct TagData
    {
        public string? Handle;
        public string? Suffix;
    }

    private TagData _tagData;

    private struct ScalarData
    {
        public string Value;
        public bool IsMultiline;
        public bool IsFlowPlainAllowed;
        public bool IsBlockPlainAllowed;
        public bool IsSingleQuotedAllowed;
        public bool IsBlockAllowed;
        public ScalarStyle Style;
    }

    private readonly bool _isUnicode;

    private ScalarData _scalarData;

    internal const int MinBestIndent = 2;
    internal const int MaxBestIndent = 9;

    /// <summary>
    /// Initializes a new instance of the <see cref="IEmitter" /> class.
    /// </summary>
    /// <param name="output">The <see cref="TextWriter" /> where the emitter will write.</param>
    /// <param name="bestIndent">The preferred indentation.</param>
    /// <param name="bestWidth">The preferred text width.</param>
    /// <param name="isCanonical">If true, write the output in canonical form.</param>
    /// <param name="forceIndentLess">if set to <c>true</c> [always indent].</param>
    /// <param name="emitKeyQuoted">if set to <c>true</c> always emit keys double quoted.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// bestIndent
    /// or
    /// bestWidth;The bestWidth parameter must be greater than bestIndent * 2.
    /// </exception>
    public Emitter(TextWriter output, int bestIndent = MinBestIndent, int bestWidth = int.MaxValue, bool isCanonical = false, bool forceIndentLess = false, bool emitKeyQuoted = false)
    {
        if (bestIndent < MinBestIndent || bestIndent > MaxBestIndent)
        {
            throw new ArgumentOutOfRangeException(nameof(bestIndent), FormattableString.Invariant($"The bestIndent parameter must be between {MinBestIndent} and {MaxBestIndent}."));
        }

        this._bestIndent = bestIndent;

        if (bestWidth <= bestIndent * 2)
        {
            throw new ArgumentOutOfRangeException(nameof(bestWidth), "The bestWidth parameter must be greater than bestIndent * 2.");
        }

        this._bestWidth = bestWidth;

        this._isCanonical = isCanonical;
        ForceIndentLess = forceIndentLess;
        this._emitKeyQuoted = emitKeyQuoted;

        this._output = output;
        _isUnicode = output.Encoding.WebName.StartsWith("utf", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Gets or sets a value indicating whether [always indent].</summary>
    /// <value><c>true</c> if [always indent]; otherwise, <c>false</c>.</value>
    public bool ForceIndentLess { get; set; }


    private void Write(char value)
    {
        _output.Write(value);
        ++_column;
    }

    private void Write(string value)
    {
        _output.Write(value);
        _column += value.Length;
    }

    private void WriteBreak()
    {
        _output.WriteLine();
        _column = 0;
    }

    /// <summary>Emit an evt.</summary>
    public void Emit(ParsingEvent @event)
    {
        _events.Enqueue(@event);

        while (!NeedMoreEvents())
        {
            var current = _events.Peek();
            AnalyzeEvent(current);
            StateMachine(current);

            // Only dequeue after calling state_machine because it checks how many events are in the queue.
            _events.Dequeue();
        }
    }

    /// <summary>
    /// Check if we need to accumulate more events before emitting.
    ///
    /// We accumulate extra
    ///  - 1 event for DOCUMENT-START
    ///  - 2 events for SEQUENCE-START
    ///  - 3 events for MAPPING-START
    /// </summary>
    private bool NeedMoreEvents()
    {
        if (_events.Count == 0)
        {
            return true;
        }

        int accumulate;
        switch (_events.Peek().Type)
        {
            case EventType.YAML_DOCUMENT_START_EVENT:
                accumulate = 1;
                break;

            case EventType.YAML_SEQUENCE_START_EVENT:
                accumulate = 2;
                break;

            case EventType.YAML_MAPPING_START_EVENT:
                accumulate = 3;
                break;

            default:
                return false;
        }

        if (_events.Count > accumulate)
        {
            return false;
        }

        int level = 0;
        foreach (var evt in _events)
        {
            switch (evt.Type)
            {
                case EventType.YAML_DOCUMENT_START_EVENT:
                case EventType.YAML_SEQUENCE_START_EVENT:
                case EventType.YAML_MAPPING_START_EVENT:
                    ++level;
                    break;

                case EventType.YAML_DOCUMENT_END_EVENT:
                case EventType.YAML_SEQUENCE_END_EVENT:
                case EventType.YAML_MAPPING_END_EVENT:
                    --level;
                    break;
            }
            if (level == 0)
            {
                return false;
            }
        }

        return true;
    }

    private void AnalyzeAnchor(string? anchor, bool isAlias)
    {
        _anchorData.Anchor = anchor;
        _anchorData.IsAlias = isAlias;
    }

    /// <summary>Check if the evt data is valid.</summary>
    private void AnalyzeEvent(ParsingEvent evt)
    {
        _anchorData.Anchor = null;
        _tagData.Handle = null;
        _tagData.Suffix = null;

        if (evt is AnchorAlias alias)
        {
            AnalyzeAnchor(alias.Value, true);
            return;
        }

        if (evt is NodeEvent nodeEvent)
        {
            if (evt is Scalar scalar)
            {
                AnalyzeScalar(scalar.Value);
            }

            AnalyzeAnchor(nodeEvent.Anchor, false);

            if (!string.IsNullOrEmpty(nodeEvent.Tag) && (_isCanonical || nodeEvent.IsCanonical))
            {
                AnalyzeTag(nodeEvent.Tag);
            }
            return;
        }
    }

    /// <summary>Check if a scalar is valid.</summary>
    private void AnalyzeScalar(string value)
    {
        bool block_indicators = false;
        bool flow_indicators = false;
        bool line_breaks = false;
        bool special_characters = false;

        bool leading_space = false;
        bool leading_break = false;
        bool trailing_space = false;
        bool trailing_break = false;
        bool break_space = false;
        bool space_break = false;

        bool previous_space = false;
        bool previous_break = false;

        _scalarData.Value = value;

        if (value.Length == 0)
        {
            _scalarData.IsMultiline = false;
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = true;
            _scalarData.IsSingleQuotedAllowed = true;
            _scalarData.IsBlockAllowed = false;
            return;
        }

        if (value.StartsWith("---", StringComparison.Ordinal) || value.StartsWith("...", StringComparison.Ordinal))
        {
            block_indicators = true;
            flow_indicators = true;
        }

        bool preceeded_by_whitespace = true;

        _buffer.Value = value;
        bool followed_by_whitespace = _buffer.IsOutside(_buffer.Position + 1) || _buffer.IsBlankOrBreakOrZero(1);

        // If the output is not detected as unicode, check if the value to encode contains
        // special characters that would require special encoding
        if (!_isUnicode)
        {
            try
            {
                var encodedBytes = _output.Encoding.GetBytes(value);
                var decodedString = _output.Encoding.GetString(encodedBytes, 0, encodedBytes.Length);
                special_characters = decodedString != value;
            }
            catch (EncoderFallbackException)
            {
                special_characters = true;
            }
        }

        bool isFirst = true;
        while (!_buffer.EndOfInput)
        {
            if (isFirst)
            {
                if (_buffer.Check(@"#,[]{}&*!|>\""%@`"))
                {
                    flow_indicators = true;
                    block_indicators = true;
                }

                if (_buffer.Check("?:"))
                {
                    flow_indicators = true;
                    if (followed_by_whitespace)
                    {
                        block_indicators = true;
                    }
                }

                if (_buffer.Check('-') && followed_by_whitespace)
                {
                    flow_indicators = true;
                    block_indicators = true;
                }
            }
            else
            {
                if (_buffer.Check(",?[]{}"))
                {
                    flow_indicators = true;
                }

                if (_buffer.Check(':'))
                {
                    flow_indicators = true;
                    if (followed_by_whitespace)
                    {
                        block_indicators = true;
                    }
                }

                if (_buffer.Check('#') && preceeded_by_whitespace)
                {
                    flow_indicators = true;
                    block_indicators = true;
                }
            }

            if (!special_characters && !_buffer.IsPrintable())
            {
                special_characters = true;
            }

            if (_buffer.IsBreak())
            {
                line_breaks = true;
            }

            if (_buffer.IsSpace())
            {
                if (isFirst)
                {
                    leading_space = true;
                }
                if (_buffer.Position >= _buffer.Length - 1)
                {
                    trailing_space = true;
                }
                if (previous_break)
                {
                    break_space = true;
                }

                previous_space = true;
                previous_break = false;
            }

            else if (_buffer.IsBreak())
            {
                if (isFirst)
                {
                    leading_break = true;
                }
                if (_buffer.Position >= _buffer.Length - 1)
                {
                    trailing_break = true;
                }

                if (previous_space)
                {
                    space_break = true;
                }
                previous_space = false;
                previous_break = true;
            }
            else
            {
                previous_space = false;
                previous_break = false;
            }

            preceeded_by_whitespace = _buffer.IsBlankOrBreakOrZero();
            _buffer.Skip(1);
            if (!_buffer.EndOfInput)
            {
                followed_by_whitespace = _buffer.IsOutside(_buffer.Position + 1) || _buffer.IsBlankOrBreakOrZero(1);
            }
            isFirst = false;
        }

        _scalarData.IsMultiline = line_breaks;

        _scalarData.IsFlowPlainAllowed = true;
        _scalarData.IsBlockPlainAllowed = true;
        _scalarData.IsSingleQuotedAllowed = true;
        _scalarData.IsBlockAllowed = true;

        if (leading_space || leading_break || trailing_space || trailing_break)
        {
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = false;
        }

        if (trailing_space)
        {
            _scalarData.IsBlockAllowed = false;
        }

        if (break_space)
        {
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = false;
            _scalarData.IsSingleQuotedAllowed = false;
        }

        if (space_break)
        {
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = false;
            _scalarData.IsSingleQuotedAllowed = false;
            _scalarData.IsBlockAllowed = false;
        }

        if (special_characters)
        {
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = false;
            _scalarData.IsSingleQuotedAllowed = false;
            // Don't disable block scalars for line breaks - they're the point of folded/literal scalars
            // However, disable block scalars for single-character strings containing only special characters
            // as they're better represented with quoted styles
            if (!line_breaks || (line_breaks && value.Length == 1))
            {
                _scalarData.IsBlockAllowed = false;
            }
        }

        if (line_breaks)
        {
            _scalarData.IsFlowPlainAllowed = false;
            _scalarData.IsBlockPlainAllowed = false;
        }

        if (flow_indicators)
        {
            _scalarData.IsFlowPlainAllowed = false;
        }

        if (block_indicators)
        {
            _scalarData.IsBlockPlainAllowed = false;
        }
    }

    /// <summary>Check if a tag is valid.</summary>
    private void AnalyzeTag(string tag)
    {
        _tagData.Handle = tag;
        foreach (var tagDirective in _tagDirectives)
        {
            if (tag.StartsWith(tagDirective.Prefix, StringComparison.Ordinal))
            {
                _tagData.Handle = tagDirective.Handle;
                _tagData.Suffix = tag.Substring(tagDirective.Prefix.Length);
                break;
            }
        }
    }

    /// <summary>State dispatcher.</summary>
    private void StateMachine(ParsingEvent evt)
    {
        switch (_state)
        {
            case EmitterState.YAML_EMIT_STREAM_START_STATE:
                EmitStreamStart(evt);
                break;

            case EmitterState.YAML_EMIT_FIRST_DOCUMENT_START_STATE:
                EmitDocumentStart(evt, true);
                break;

            case EmitterState.YAML_EMIT_DOCUMENT_START_STATE:
                EmitDocumentStart(evt, false);
                break;

            case EmitterState.YAML_EMIT_DOCUMENT_CONTENT_STATE:
                EmitDocumentContent(evt);
                break;

            case EmitterState.YAML_EMIT_DOCUMENT_END_STATE:
                EmitDocumentEnd(evt);
                break;

            case EmitterState.YAML_EMIT_FLOW_SEQUENCE_FIRST_ITEM_STATE:
                EmitFlowSequenceItem(evt, true);
                break;

            case EmitterState.YAML_EMIT_FLOW_SEQUENCE_ITEM_STATE:
                EmitFlowSequenceItem(evt, false);
                break;

            case EmitterState.YAML_EMIT_FLOW_MAPPING_FIRST_KEY_STATE:
                EmitFlowMappingKey(evt, true);
                break;

            case EmitterState.YAML_EMIT_FLOW_MAPPING_KEY_STATE:
                EmitFlowMappingKey(evt, false);
                break;

            case EmitterState.YAML_EMIT_FLOW_MAPPING_SIMPLE_VALUE_STATE:
                EmitFlowMappingValue(evt, true);
                break;

            case EmitterState.YAML_EMIT_FLOW_MAPPING_VALUE_STATE:
                EmitFlowMappingValue(evt, false);
                break;

            case EmitterState.YAML_EMIT_BLOCK_SEQUENCE_FIRST_ITEM_STATE:
                EmitBlockSequenceItem(evt, true);
                break;

            case EmitterState.YAML_EMIT_BLOCK_SEQUENCE_ITEM_STATE:
                EmitBlockSequenceItem(evt, false);
                break;

            case EmitterState.YAML_EMIT_BLOCK_MAPPING_FIRST_KEY_STATE:
                EmitBlockMappingKey(evt, true);
                break;

            case EmitterState.YAML_EMIT_BLOCK_MAPPING_KEY_STATE:
                EmitBlockMappingKey(evt, false);
                break;

            case EmitterState.YAML_EMIT_BLOCK_MAPPING_SIMPLE_VALUE_STATE:
                EmitBlockMappingValue(evt, true);
                break;

            case EmitterState.YAML_EMIT_BLOCK_MAPPING_VALUE_STATE:
                EmitBlockMappingValue(evt, false);
                break;

            case EmitterState.YAML_EMIT_END_STATE:
                throw new YamlException("Expected nothing after STREAM-END");

            default:
                Debug.Assert(false, "Invalid state.");
                throw new InvalidOperationException("Invalid state");
        }
    }

    /// <summary>Expect STREAM-START.</summary>
    private void EmitStreamStart(ParsingEvent evt)
    {
        if (evt is not StreamStart)
        {
            throw new ArgumentException("Expected STREAM-START.", nameof(evt));
        }

        _indent = -1;
        _column = 0;
        _isWhitespace = true;
        _isIndentation = true;

        _state = EmitterState.YAML_EMIT_FIRST_DOCUMENT_START_STATE;
    }

    /// <summary>Expect DOCUMENT-START or STREAM-END.</summary>
    private void EmitDocumentStart(ParsingEvent evt, bool isFirst)
    {
        if (evt is DocumentStart documentStart)
        {
            bool isImplicit = documentStart.IsImplicit && isFirst && !_isCanonical;


            if (documentStart.Version != null && _isOpenEnded)
            {
                WriteIndicator("...", true, false, false);
                WriteIndent();
            }

            if (documentStart.Version != null)
            {
                AnalyzeVersionDirective(documentStart.Version);

                isImplicit = false;
                WriteIndicator("%YAML", true, false, false);
                WriteIndicator(FormattableString.Invariant($"{documentStart.Version.Version.Major}.{documentStart.Version.Version.Minor}"), true, false, false);
                WriteIndent();
            }

            if (documentStart.Tags != null)
            {
                foreach (var tagDirective in documentStart.Tags)
                {
                    AppendTagDirective(tagDirective, false);
                }
            }

            foreach (var tagDirective in Constants.DefaultTagDirectives)
            {
                AppendTagDirective(tagDirective, true);
            }

            if (documentStart.Tags != null && documentStart.Tags.Count != 0)
            {
                isImplicit = false;
                foreach (var tagDirective in documentStart.Tags)
                {
                    WriteIndicator("%TAG", true, false, false);
                    WriteTagHandle(tagDirective.Handle);
                    WriteTagContent(tagDirective.Prefix, true);
                    WriteIndent();
                }
            }

            if (CheckEmptyDocument())
            {
                isImplicit = false;
            }

            if (!isImplicit)
            {
                WriteIndent();
                WriteIndicator("---", true, false, false);
                if (_isCanonical)
                {
                    WriteIndent();
                }
            }

            _state = EmitterState.YAML_EMIT_DOCUMENT_CONTENT_STATE;
        }

        else if (evt is StreamEnd)
        {
            if (_isOpenEnded)
            {
                WriteIndicator("...", true, false, false);
                WriteIndent();
            }

            _state = EmitterState.YAML_EMIT_END_STATE;
        }
        else
        {
            throw new YamlException("Expected DOCUMENT-START or STREAM-END");
        }
    }

    /// <summary>Check if the document content is an empty scalar.</summary>
    private bool CheckEmptyDocument()
    {
        int index = 0;
        foreach (var parsingEvent in _events)
        {
            if (++index == 2)
            {
                if (parsingEvent is Scalar scalar)
                {
                    return string.IsNullOrEmpty(scalar.Value);
                }
                break;
            }
        }

        return false;
    }

    private void WriteTagHandle(string value)
    {
        if (!_isWhitespace)
        {
            Write(' ');
        }

        Write(value);

        _isWhitespace = false;
        _isIndentation = false;
    }

    [GeneratedRegex(@"[^0-9A-Za-z_\-;?@=$~\\\)\]/:&+,\.\*\(\[!]", RegexOptions.Singleline, matchTimeoutMilliseconds: -1)]
    private static partial Regex UriReplacer { get; }

    private static string UrlEncode(string text)
    {
        return UriReplacer.Replace(text, delegate (Match match)
        {
            var buffer = new StringBuilder();
            foreach (var toEncode in Encoding.UTF8.GetBytes(match.Value))
            {
                buffer.AppendFormat("%{0:X02}", toEncode);
            }
            return buffer.ToString();
        });
    }

    private void WriteTagContent(string value, bool needsWhitespace)
    {
        if (needsWhitespace && !_isWhitespace)
        {
            Write(' ');
        }

        Write(UrlEncode(value));

        _isWhitespace = false;
        _isIndentation = false;
    }

    /// <summary>Append a directive to the directives stack.</summary>
    private void AppendTagDirective(TagDirective value, bool allowDuplicates)
    {
        if (_tagDirectives.Contains(value))
        {
            if (allowDuplicates)
            {
                return;
            }
            else
            {
                throw new YamlException("Duplicate %TAG directive.");
            }
        }
        else
        {
            _tagDirectives.Add(value);
        }
    }

    /// <summary>Check if a %YAML directive is valid.</summary>
    private static void AnalyzeVersionDirective(VersionDirective versionDirective)
    {
        if (!Constants.IsSupportedYamlVersion(versionDirective.Version))
        {
            throw new YamlException("Incompatible %YAML directive");
        }
    }

    private void WriteIndicator(string indicator, bool needWhitespace, bool whitespace, bool indentation)
    {
        if (needWhitespace && !_isWhitespace)
        {
            Write(' ');
        }

        Write(indicator);

        _isWhitespace = whitespace;
        _isIndentation &= indentation;
        _isOpenEnded = false;
    }

    private void WriteIndent()
    {
        int currentIndent = Math.Max(_indent, 0);

        if (!_isIndentation || _column > currentIndent || (_column == currentIndent && !_isWhitespace))
        {
            WriteBreak();
        }

        while (_column < currentIndent)
        {
            Write(' ');
        }

        _isWhitespace = true;
        _isIndentation = true;
    }

    /// <summary>Expect the root node.</summary>
    private void EmitDocumentContent(ParsingEvent evt)
    {
        _states.Push(EmitterState.YAML_EMIT_DOCUMENT_END_STATE);
        EmitNode(evt, true, false, false);
    }

    /// <summary>Expect a node.</summary>
    private void EmitNode(ParsingEvent evt, bool isRoot, bool isMapping, bool isSimpleKey)
    {
        _isRootContext = isRoot;
        _isMappingContext = isMapping;
        _isSimpleKeyContext = isSimpleKey;

        var eventType = evt.Type;
        switch (eventType)
        {
            case EventType.YAML_ALIAS_EVENT:
                EmitAlias();
                break;

            case EventType.YAML_SCALAR_EVENT:
                EmitScalar(evt);
                break;

            case EventType.YAML_SEQUENCE_START_EVENT:
                EmitSequenceStart(evt);
                break;

            case EventType.YAML_MAPPING_START_EVENT:
                EmitMappingStart(evt);
                break;

            default:
                throw new YamlException($"Expected SCALAR, SEQUENCE-START, MAPPING-START, or ALIAS, got {eventType}");
        }
    }

    /// <summary>Expect SEQUENCE-START.</summary>
    private void EmitSequenceStart(ParsingEvent evt)
    {
        ProcessAnchor();
        ProcessTag();

        var sequenceStart = (SequenceStart)evt;

        if (_flowLevel != 0 || _isCanonical || sequenceStart.Style == YamlStyle.Flow || CheckEmptySequence())
        {
            _state = EmitterState.YAML_EMIT_FLOW_SEQUENCE_FIRST_ITEM_STATE;
        }
        else
        {
            _state = EmitterState.YAML_EMIT_BLOCK_SEQUENCE_FIRST_ITEM_STATE;
        }
    }

    /// <summary>Check if the next events represent an empty sequence.</summary>
    private bool CheckEmptySequence()
    {
        if (_events.Count < 2)
        {
            return false;
        }

        var eventList = new FakeList<ParsingEvent>(_events);
        return eventList[0] is SequenceStart && eventList[1] is SequenceEnd;
    }

    /// <summary>Check if the next events represent an empty mapping.</summary>
    private bool CheckEmptyMapping()
    {
        if (_events.Count < 2)
        {
            return false;
        }

        var eventList = new FakeList<ParsingEvent>(_events);
        return eventList[0] is MappingStart && eventList[1] is MappingEnd;
    }

    /// <summary>Write a tag.</summary>
    private void ProcessTag()
    {
        if (_tagData.Handle == null && _tagData.Suffix == null)
        {
            return;
        }

        if (_tagData.Handle != null)
        {
            WriteTagHandle(_tagData.Handle);
            if (_tagData.Suffix != null)
            {
                WriteTagContent(_tagData.Suffix, false);
            }
        }
        else
        {
            Debug.Assert(_tagData.Suffix is not null);
            WriteIndicator("!<", true, false, false);
            WriteTagContent(_tagData.Suffix, false);
            WriteIndicator(">", false, false, false);
        }
    }

    /// <summary>Expect MAPPING-START.</summary>
    private void EmitMappingStart(ParsingEvent evt)
    {
        ProcessAnchor();
        ProcessTag();

        var mappingStart = (MappingStart)evt;

        if (_flowLevel != 0 || _isCanonical || mappingStart.Style == YamlStyle.Flow || CheckEmptyMapping())
        {
            _state = EmitterState.YAML_EMIT_FLOW_MAPPING_FIRST_KEY_STATE;
        }
        else
        {
            _state = EmitterState.YAML_EMIT_BLOCK_MAPPING_FIRST_KEY_STATE;
        }
    }

    /// <summary>Expect SCALAR.</summary>
    private void EmitScalar(ParsingEvent evt)
    {
        SelectScalarStyle(evt);
        ProcessAnchor();
        ProcessTag();
        IncreaseIndent(true, false);
        ProcessScalar();

        _indent = _indents.Pop();
        _state = _states.Pop();
    }

    /// <summary>Write a scalar.</summary>
    private void ProcessScalar()
    {
        switch (_scalarData.Style)
        {
            case ScalarStyle.Plain:
                WritePlainScalar(_scalarData.Value, !_isSimpleKeyContext);
                break;

            case ScalarStyle.SingleQuoted:
                WriteSingleQuotedScalar(_scalarData.Value, !_isSimpleKeyContext);
                break;

            case ScalarStyle.DoubleQuoted:
                WriteDoubleQuotedScalar(_scalarData.Value, !_isSimpleKeyContext);
                break;

            case ScalarStyle.Literal:
                WriteLiteralScalar(_scalarData.Value);
                break;

            case ScalarStyle.Folded:
                WriteFoldedScalar(_scalarData.Value);
                break;

            default:
                // Impossible.
                throw new InvalidOperationException();
        }
    }

    private static bool IsBreak(char character)
    {
        return character == '\r' || character == '\n' || character == '\x85' || character == '\x2028' || character == '\x2029';
    }

    private static bool IsBlank(char character)
    {
        return character == ' ' || character == '\t';
    }

    /// <summary>Check if the specified character is a space.</summary>
    private static bool IsSpace(char character)
    {
        return character == ' ';
    }

    internal static bool IsPrintable(char character)
    {
        return
            (character >= '\x20' && character <= '\x7E') ||
            character == '\x85' ||
            (character >= '\xA0' && character <= '\xD7FF') ||
            (character >= '\xE000' && character <= '\xFFFD');
    }

    private void WriteFoldedScalar(string value)
    {
        bool previous_break = true;
        bool leading_spaces = true;

        WriteIndicator(">", true, false, false);
        WriteBlockScalarHints(value);
        WriteBreak();

        _isIndentation = true;
        _isWhitespace = true;

        for (int i = 0; i < value.Length; ++i)
        {
            char character = value[i];
            if (IsBreak(character))
            {
                if (!previous_break && !leading_spaces && character == '\n')
                {
                    int k = 0;
                    while (i + k < value.Length && IsBreak(value[i + k]))
                    {
                        ++k;
                    }
                    if (i + k < value.Length && !(IsBlank(value[i + k]) || IsBreak(value[i + k])))
                    {
                        WriteBreak();
                    }
                }
                WriteBreak();
                _isIndentation = true;
                previous_break = true;
            }
            else
            {
                if (previous_break)
                {
                    WriteIndent();
                    leading_spaces = IsBlank(character);
                }
                if (!previous_break && character == ' ' && i + 1 < value.Length && value[i + 1] != ' ' && _column > _bestWidth)
                {
                    WriteIndent();
                }
                else
                {
                    Write(character);
                }
                _isIndentation = false;
                previous_break = false;
            }
        }
    }

    private void WriteLiteralScalar(string value)
    {
        bool previous_break = true;

        WriteIndicator("|", true, false, false);
        WriteBlockScalarHints(value);
        WriteBreak();

        _isIndentation = true;
        _isWhitespace = true;

        foreach (var character in value)
        {
            if (IsBreak(character))
            {
                WriteBreak();
                _isIndentation = true;
                previous_break = true;
            }
            else
            {
                if (previous_break)
                {
                    WriteIndent();
                }
                Write(character);
                _isIndentation = false;
                previous_break = false;
            }
        }
    }

    private void WriteDoubleQuotedScalar(string value, bool allowBreaks)
    {
        WriteIndicator("\"", true, false, false);

        bool previous_space = false;
        for (int index = 0; index < value.Length; ++index)
        {
            char character = value[index];


            if (!IsPrintable(character) || IsBreak(character) || character == '"' || character == '\\')
            {
                Write('\\');

                switch (character)
                {
                    case '\0':
                        Write('0');
                        break;

                    case '\x7':
                        Write('a');
                        break;

                    case '\x8':
                        Write('b');
                        break;

                    case '\x9':
                        Write('t');
                        break;

                    case '\xA':
                        Write('n');
                        break;

                    case '\xB':
                        Write('v');
                        break;

                    case '\xC':
                        Write('f');
                        break;

                    case '\xD':
                        Write('r');
                        break;

                    case '\x1B':
                        Write('e');
                        break;

                    case '\x22':
                        Write('"');
                        break;

                    case '\x5C':
                        Write('\\');
                        break;

                    case '\x85':
                        Write('N');
                        break;

                    case '\xA0':
                        Write('_');
                        break;

                    case '\x2028':
                        Write('L');
                        break;

                    case '\x2029':
                        Write('P');
                        break;

                    default:
                        short code = (short)character;
                        if (code <= 0xFF)
                        {
                            Write('x');
                            Write(code.ToString("X02", CultureInfo.InvariantCulture));
                        }
                        else if (CharHelper.IsHighSurrogate(character))
                        {
                            char nextChar;
                            if (index + 1 < value.Length && CharHelper.IsLowSurrogate(nextChar = value[index + 1]))
                            {
                                Write('U');
                                Write(CharHelper.ConvertToUtf32(character, nextChar).ToString("X08", CultureInfo.InvariantCulture));
                                index++;
                            }
                            else
                            {
                                throw new YamlException($"Unable to encode character low surrogate after high surrogate [{character}] at position {index + 1} of text `{value}`");
                            }
                        }
                        else
                        {
                            Write('u');
                            Write(code.ToString("X04", CultureInfo.InvariantCulture));
                        }
                        break;
                }
                previous_space = false;
            }
            else if (character == ' ')
            {
                if (allowBreaks && !previous_space && _column > _bestWidth && index > 0 && index + 1 < value.Length)
                {
                    WriteIndent();
                    if (value[index + 1] == ' ')
                    {
                        Write('\\');
                    }
                }
                else
                {
                    Write(character);
                }
                previous_space = true;
            }
            else
            {
                Write(character);
                previous_space = false;
            }
        }

        WriteIndicator("\"", false, false, false);

        _isWhitespace = false;
        _isIndentation = false;
    }

    private void WriteSingleQuotedScalar(string value, bool allowBreaks)
    {
        WriteIndicator("'", true, false, false);

        bool previous_space = false;
        bool previous_break = false;

        for (int index = 0; index < value.Length; ++index)
        {
            char character = value[index];

            if (character == ' ')
            {
                if (allowBreaks && !previous_space && _column > _bestWidth && index != 0 && index + 1 < value.Length && value[index + 1] != ' ')
                {
                    WriteIndent();
                }
                else
                {
                    Write(character);
                }
                previous_space = true;
            }
            else if (IsBreak(character))
            {
                if (!previous_break && character == '\n')
                {
                    WriteBreak();
                }
                WriteBreak();
                _isIndentation = true;
                previous_break = true;
            }
            else
            {
                if (previous_break)
                {
                    WriteIndent();
                }
                if (character == '\'')
                {
                    Write(character);
                }
                Write(character);
                _isIndentation = false;
                previous_space = false;
                previous_break = false;
            }
        }

        WriteIndicator("'", false, false, false);

        _isWhitespace = false;
        _isIndentation = false;
    }

    private void WritePlainScalar(string value, bool allowBreaks)
    {
        if (!_isWhitespace)
        {
            Write(' ');
        }

        bool previous_space = false;
        bool previous_break = false;
        for (int index = 0; index < value.Length; ++index)
        {
            char character = value[index];

            if (IsSpace(character))
            {
                if (allowBreaks && !previous_space && _column > _bestWidth && index + 1 < value.Length && value[index + 1] != ' ')
                {
                    WriteIndent();
                }
                else
                {
                    Write(character);
                }
                previous_space = true;
            }
            else if (IsBreak(character))
            {
                if (!previous_break && character == '\n')
                {
                    WriteBreak();
                }
                WriteBreak();
                _isIndentation = true;
                previous_break = true;
            }
            else
            {
                if (previous_break)
                {
                    WriteIndent();
                }
                Write(character);
                _isIndentation = false;
                previous_space = false;
                previous_break = false;
            }
        }

        _isWhitespace = false;
        _isIndentation = false;

        if (_isRootContext)
        {
            _isOpenEnded = true;
        }
    }

    /// <summary>Increase the indentation level.</summary>
    private void IncreaseIndent(bool isFlow, bool isIndentless)
    {
        _indents.Push(_indent);

        if (_indent < 0)
        {
            _indent = isFlow ? _bestIndent : 0;
        }
        else if (!isIndentless || !ForceIndentLess)
        {
            _indent += _bestIndent;
        }
    }

    /// <summary>Determine an acceptable scalar style.</summary>
    private void SelectScalarStyle(ParsingEvent evt)
    {
        var scalar = (Scalar)evt;

        var style = scalar.Style;
        bool noTag = _tagData.Handle == null && _tagData.Suffix == null;

        if (noTag && !scalar.IsPlainImplicit && !scalar.IsQuotedImplicit)
        {
            throw new YamlException("Neither tag nor isImplicit flags are specified.");
        }

        if (style == ScalarStyle.Any)
        {
            style = _scalarData.IsMultiline ? ScalarStyle.Folded : ScalarStyle.Plain;
        }

        if (_isCanonical)
        {
            style = ScalarStyle.DoubleQuoted;
        }

        if (_isSimpleKeyContext && (_scalarData.IsMultiline || _emitKeyQuoted))
        {
            style = ScalarStyle.DoubleQuoted;
        }

        if (style == ScalarStyle.Plain)
        {
            if ((_flowLevel != 0 && !_scalarData.IsFlowPlainAllowed) || (_flowLevel == 0 && !_scalarData.IsBlockPlainAllowed))
            {
                style = ScalarStyle.SingleQuoted;
            }
            if (string.IsNullOrEmpty(_scalarData.Value) && (_flowLevel != 0 || _isSimpleKeyContext))
            {
                style = ScalarStyle.SingleQuoted;
            }
            if (noTag && !scalar.IsPlainImplicit)
            {
                style = ScalarStyle.SingleQuoted;
            }
        }

        if (style == ScalarStyle.SingleQuoted)
        {
            if (!_scalarData.IsSingleQuotedAllowed)
            {
                style = ScalarStyle.DoubleQuoted;
            }
        }

        if (style == ScalarStyle.Literal || style == ScalarStyle.Folded)
        {
            // Only override block styles if they're truly not possible to emit
            // Don't override if the user explicitly requested Folded/Literal style
            // unless we're in a context where block scalars are impossible (flow context, simple key)
            if (_flowLevel != 0 || _isSimpleKeyContext)
            {
                style = ScalarStyle.DoubleQuoted;
            }
            // For both literal and folded scalars, fall back to double quotes if block scalars aren't allowed
            else if (!_scalarData.IsBlockAllowed)
            {
                style = ScalarStyle.DoubleQuoted;
            }
        }

        // Final fallback: if no style is allowed, always use double quoted
        if (!_scalarData.IsFlowPlainAllowed && !_scalarData.IsBlockPlainAllowed &&
            !_scalarData.IsSingleQuotedAllowed && !_scalarData.IsBlockAllowed)
        {
            style = ScalarStyle.DoubleQuoted;
        }

        _scalarData.Style = style;
    }

    /// <summary>Expect ALIAS.</summary>
    private void EmitAlias()
    {
        ProcessAnchor();
        _state = _states.Pop();
    }

    /// <summary>Write an anchor.</summary>
    private void ProcessAnchor()
    {
        if (_anchorData.Anchor != null)
        {
            WriteIndicator(_anchorData.IsAlias ? "*" : "&", true, false, false);
            WriteAnchor(_anchorData.Anchor);
        }
    }

    private void WriteAnchor(string value)
    {
        Write(value);

        _isWhitespace = false;
        _isIndentation = false;
    }

    /// <summary>Expect DOCUMENT-END.</summary>
    private void EmitDocumentEnd(ParsingEvent evt)
    {
        if (evt is DocumentEnd documentEnd)
        {
            WriteIndent();
            if (!documentEnd.IsImplicit)
            {
                WriteIndicator("...", true, false, false);
                WriteIndent();
            }

            _state = EmitterState.YAML_EMIT_DOCUMENT_START_STATE;

            _tagDirectives.Clear();
        }
        else
        {
            throw new YamlException("Expected DOCUMENT-END.");
        }
    }

    /// <summary>Expect a flow item node.</summary>
    private void EmitFlowSequenceItem(ParsingEvent evt, bool isFirst)
    {
        if (isFirst)
        {
            WriteIndicator("[", true, true, false);
            IncreaseIndent(true, false);
            ++_flowLevel;
        }

        if (evt is SequenceEnd)
        {
            --_flowLevel;
            _indent = _indents.Pop();
            if (_isCanonical && !isFirst)
            {
                WriteIndicator(",", false, false, false);
                WriteIndent();
            }
            WriteIndicator("]", false, false, false);
            _state = _states.Pop();
            return;
        }

        if (!isFirst)
        {
            WriteIndicator(",", false, false, false);
        }

        if (_isCanonical || _column > _bestWidth)
        {
            WriteIndent();
        }

        _states.Push(EmitterState.YAML_EMIT_FLOW_SEQUENCE_ITEM_STATE);

        EmitNode(evt, false, false, false);
    }

    /// <summary>Expect a flow key node.</summary>
    private void EmitFlowMappingKey(ParsingEvent evt, bool isFirst)
    {
        if (isFirst)
        {
            WriteIndicator("{", true, true, false);
            IncreaseIndent(true, false);
            ++_flowLevel;
        }

        if (evt is MappingEnd)
        {
            --_flowLevel;
            _indent = _indents.Pop();
            if (_isCanonical && !isFirst)
            {
                WriteIndicator(",", false, false, false);
                WriteIndent();
            }
            WriteIndicator("}", false, false, false);
            _state = _states.Pop();
            return;
        }

        if (!isFirst)
        {
            WriteIndicator(",", false, false, false);
        }
        if (_isCanonical || _column > _bestWidth)
        {
            WriteIndent();
        }

        if (!_isCanonical && CheckSimpleKey())
        {
            _states.Push(EmitterState.YAML_EMIT_FLOW_MAPPING_SIMPLE_VALUE_STATE);
            EmitNode(evt, false, true, true);
        }
        else
        {
            WriteIndicator("?", true, false, false);
            _states.Push(EmitterState.YAML_EMIT_FLOW_MAPPING_VALUE_STATE);
            EmitNode(evt, false, true, false);
        }
    }

    private const int MaxAliasLength = 128;

    private static int SafeStringLength(string? value)
    {
        return value != null ? value.Length : 0;
    }

    /// <summary>Check if the next node can be expressed as a simple key.</summary>
    private bool CheckSimpleKey()
    {
        if (_events.Count < 1)
        {
            return false;
        }

        int length;
        switch (_events.Peek().Type)
        {
            case EventType.YAML_ALIAS_EVENT:
                length = SafeStringLength(_anchorData.Anchor);
                break;

            case EventType.YAML_SCALAR_EVENT:
                if (_scalarData.IsMultiline)
                {
                    return false;
                }

                length =
                    SafeStringLength(_anchorData.Anchor) +
                    SafeStringLength(_tagData.Handle) +
                    SafeStringLength(_tagData.Suffix) +
                    SafeStringLength(_scalarData.Value);
                break;

            case EventType.YAML_SEQUENCE_START_EVENT:
                if (!CheckEmptySequence())
                {
                    return false;
                }
                length =
                    SafeStringLength(_anchorData.Anchor) +
                    SafeStringLength(_tagData.Handle) +
                    SafeStringLength(_tagData.Suffix);
                break;

            case EventType.YAML_MAPPING_START_EVENT:
                if (!CheckEmptySequence())
                {
                    return false;
                }
                length =
                    SafeStringLength(_anchorData.Anchor) +
                    SafeStringLength(_tagData.Handle) +
                    SafeStringLength(_tagData.Suffix);
                break;

            default:
                return false;
        }

        return length <= MaxAliasLength;
    }

    /// <summary>Expect a flow value node.</summary>
    private void EmitFlowMappingValue(ParsingEvent evt, bool isSimple)
    {
        if (isSimple)
        {
            WriteIndicator(":", false, false, false);
        }
        else
        {
            if (_isCanonical || _column > _bestWidth)
            {
                WriteIndent();
            }
            WriteIndicator(":", true, false, false);
        }
        _states.Push(EmitterState.YAML_EMIT_FLOW_MAPPING_KEY_STATE);
        EmitNode(evt, false, true, false);
    }

    /// <summary>Expect a block item node.</summary>
    private void EmitBlockSequenceItem(ParsingEvent evt, bool isFirst)
    {
        if (isFirst)
        {
            IncreaseIndent(false, (_isMappingContext && !_isIndentation));
        }

        if (evt is SequenceEnd)
        {
            _indent = _indents.Pop();
            _state = _states.Pop();
            return;
        }

        WriteIndent();
        WriteIndicator("-", true, false, true);
        _states.Push(EmitterState.YAML_EMIT_BLOCK_SEQUENCE_ITEM_STATE);

        EmitNode(evt, false, false, false);
    }

    /// <summary>Expect a block key node.</summary>
    private void EmitBlockMappingKey(ParsingEvent evt, bool isFirst)
    {
        if (isFirst)
        {
            IncreaseIndent(false, false);
        }

        if (evt is MappingEnd)
        {
            _indent = _indents.Pop();
            _state = _states.Pop();
            return;
        }

        WriteIndent();

        if (CheckSimpleKey())
        {
            _states.Push(EmitterState.YAML_EMIT_BLOCK_MAPPING_SIMPLE_VALUE_STATE);
            EmitNode(evt, false, true, true);
        }
        else
        {
            WriteIndicator("?", true, false, true);
            _states.Push(EmitterState.YAML_EMIT_BLOCK_MAPPING_VALUE_STATE);
            EmitNode(evt, false, true, false);
        }
    }

    /// <summary>Expect a block value node.</summary>
    private void EmitBlockMappingValue(ParsingEvent evt, bool isSimple)
    {
        if (isSimple)
        {
            WriteIndicator(":", false, false, false);
        }
        else
        {
            WriteIndent();
            WriteIndicator(":", true, false, true);
        }
        _states.Push(EmitterState.YAML_EMIT_BLOCK_MAPPING_KEY_STATE);
        EmitNode(evt, false, true, false);
    }

    private void WriteBlockScalarHints(string value)
    {
        var analyzer = new StringLookAheadBuffer(value);

        if (analyzer.IsSpace() || analyzer.IsBreak())
        {
            var indent_hint = FormattableString.Invariant($"{_bestIndent}");
            WriteIndicator(indent_hint, false, false, false);
        }

        _isOpenEnded = false;

        string? chomp_hint = null;
        if (value.Length == 0 || !analyzer.IsBreak(value.Length - 1))
        {
            chomp_hint = "-";
        }
        else if (value.Length >= 2 && analyzer.IsBreak(value.Length - 2))
        {
            chomp_hint = "+";
            _isOpenEnded = true;
        }

        if (chomp_hint != null)
        {
            WriteIndicator(chomp_hint, false, false, false);
        }
    }
}
