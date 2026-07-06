using System.Diagnostics;
using Meziantou.Framework.Yaml.Tokens;
using AnchorAlias = Meziantou.Framework.Yaml.Tokens.AnchorAlias;
using DocumentEnd = Meziantou.Framework.Yaml.Tokens.DocumentEnd;
using DocumentStart = Meziantou.Framework.Yaml.Tokens.DocumentStart;
using Event = Meziantou.Framework.Yaml.Events.ParsingEvent;
using Scalar = Meziantou.Framework.Yaml.Tokens.Scalar;
using StreamEnd = Meziantou.Framework.Yaml.Tokens.StreamEnd;
using StreamStart = Meziantou.Framework.Yaml.Tokens.StreamStart;

namespace Meziantou.Framework.Yaml;

/// <summary>Represents the Parser.</summary>
public static class Parser
{
    /// <summary>Creates a YAML parser with the default maximum nesting depth.</summary>
    /// <param name="reader">The YAML reader.</param>
    /// <returns>The parser.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    public static IParser CreateParser(TextReader reader)
    {
        return CreateParser(reader, maxDepth: 0);
    }

    /// <summary>Creates a YAML parser with a configurable maximum nesting depth.</summary>
    /// <param name="reader">The YAML reader.</param>
    /// <param name="maxDepth">
    /// The maximum allowed nesting depth for mappings and sequences.
    /// A value of <c>0</c> uses the default limit of 64.
    /// </param>
    /// <returns>The parser.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDepth"/> is less than 0.</exception>
    public static IParser CreateParser(TextReader reader, int maxDepth)
    {
        return CreateParser(reader, maxDepth, sourceName: null);
    }

    /// <summary>Creates a YAML parser with a configurable maximum nesting depth and optional source name.</summary>
    /// <param name="reader">The YAML reader.</param>
    /// <param name="maxDepth">
    /// The maximum allowed nesting depth for mappings and sequences.
    /// A value of <c>0</c> uses the default limit of 64.
    /// </param>
    /// <param name="sourceName">An optional source name associated with the YAML payload, such as a file path, used when reporting depth-limit failures.</param>
    /// <returns>The parser.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxDepth"/> is less than 0.</exception>
    public static IParser CreateParser(TextReader reader, int maxDepth, string? sourceName)
    {
        ArgumentNullException.ThrowIfNull(reader);
        var effectiveMaxDepth = YamlDepthHelper.GetEffectiveMaxDepth(maxDepth, nameof(maxDepth));
        return CreateParserCore(reader, effectiveMaxDepth, sourceName);
    }

    private static IParser CreateParserCore(TextReader reader, int maxDepth, string? sourceName)
    {
        if (reader is StringReader stringReader)
            return new Parser<StringLookAheadBuffer>(new StringLookAheadBuffer(stringReader.ReadToEnd()), maxDepth, sourceName);

        else return new Parser<LookAheadBuffer>(new LookAheadBuffer(reader, Scanner<LookAheadBuffer>.MaxBufferLength), maxDepth, sourceName);
    }
}

/// <summary>Parses YAML streams.</summary>
public class Parser<TBuffer> : IParser where TBuffer : ILookAheadBuffer
{
    private readonly Stack<ParserState> _states = new Stack<ParserState>();
    private readonly TagDirectiveCollection _tagDirectives = new TagDirectiveCollection();
    private ParserState _state;

    private readonly Scanner<TBuffer> _scanner;
    private readonly int _maxDepth;
    private readonly string? _sourceName;
    private int _currentDepth;
    private Token? _currentToken;

    private Token GetCurrentToken()
    {
        if (_currentToken == null)
        {
            if (_scanner.InternalMoveNext())
            {
                _currentToken = _scanner.Current;
            }
        }
        return _currentToken!;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IParser"/> class.
    /// </summary>
    /// <param name="buffer">The input where the YAML stream is to be read.</param>
    /// <param name="maxDepth">The maximum allowed nesting depth for mappings and sequences.</param>
    /// <param name="sourceName">The optional source name used when reporting depth-limit failures.</param>
    public Parser(TBuffer buffer, int maxDepth = YamlDepthHelper.DefaultMaxDepth, string? sourceName = null)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        _maxDepth = YamlDepthHelper.GetEffectiveMaxDepth(maxDepth, nameof(maxDepth));
        _sourceName = sourceName;
        _scanner = new Scanner<TBuffer>(buffer);
    }

    /// <summary>Gets the current event.</summary>
    public Event? Current { get; private set; }

    /// <summary>Moves to the next event.</summary>
    /// <returns>Returns true if there are more events available, otherwise returns false.</returns>
    public bool MoveNext()
    {
        // No events after the end of the stream or error.
        if (_state == ParserState.YAML_PARSE_END_STATE)
        {
            Current = null;
            return false;
        }
        else
        {
            // Generate the next event.
            Current = StateMachine();
            EnforceMaxDepth(Current);
            return true;
        }
    }

    private void EnforceMaxDepth(Event current)
    {
        if (current is Events.SequenceStart or Events.MappingStart)
        {
            if (_currentDepth >= _maxDepth)
            {
                throw YamlDepthHelper.CreateMaxDepthExceededException(_maxDepth, current.Start, current.End, _sourceName);
            }

            _currentDepth++;
        }
        else if (current is Events.SequenceEnd or Events.MappingEnd)
        {
            _currentDepth--;
        }
    }

    private Event StateMachine()
    {
        switch (_state)
        {
            case ParserState.YAML_PARSE_STREAM_START_STATE:
                return ParseStreamStart();

            case ParserState.YAML_PARSE_IMPLICIT_DOCUMENT_START_STATE:
                return ParseDocumentStart(true);

            case ParserState.YAML_PARSE_DOCUMENT_START_STATE:
                return ParseDocumentStart(false);

            case ParserState.YAML_PARSE_DOCUMENT_CONTENT_STATE:
                return ParseDocumentContent();

            case ParserState.YAML_PARSE_DOCUMENT_END_STATE:
                return ParseDocumentEnd();

            case ParserState.YAML_PARSE_BLOCK_NODE_STATE:
                return ParseNode(true, false);

            case ParserState.YAML_PARSE_BLOCK_NODE_OR_INDENTLESS_SEQUENCE_STATE:
                return ParseNode(true, true);

            case ParserState.YAML_PARSE_FLOW_NODE_STATE:
                return ParseNode(false, false);

            case ParserState.YAML_PARSE_BLOCK_SEQUENCE_FIRST_ENTRY_STATE:
                return ParseBlockSequenceEntry(true);

            case ParserState.YAML_PARSE_BLOCK_SEQUENCE_ENTRY_STATE:
                return ParseBlockSequenceEntry(false);

            case ParserState.YAML_PARSE_INDENTLESS_SEQUENCE_ENTRY_STATE:
                return ParseIndentlessSequenceEntry();

            case ParserState.YAML_PARSE_BLOCK_MAPPING_FIRST_KEY_STATE:
                return ParseBlockMappingKey(true);

            case ParserState.YAML_PARSE_BLOCK_MAPPING_KEY_STATE:
                return ParseBlockMappingKey(false);

            case ParserState.YAML_PARSE_BLOCK_MAPPING_VALUE_STATE:
                return ParseBlockMappingValue();

            case ParserState.YAML_PARSE_FLOW_SEQUENCE_FIRST_ENTRY_STATE:
                return ParseFlowSequenceEntry(true);

            case ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_STATE:
                return ParseFlowSequenceEntry(false);

            case ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_KEY_STATE:
                return ParseFlowSequenceEntryMappingKey();

            case ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_VALUE_STATE:
                return ParseFlowSequenceEntryMappingValue();

            case ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_END_STATE:
                return ParseFlowSequenceEntryMappingEnd();

            case ParserState.YAML_PARSE_FLOW_MAPPING_FIRST_KEY_STATE:
                return ParseFlowMappingKey(true);

            case ParserState.YAML_PARSE_FLOW_MAPPING_KEY_STATE:
                return ParseFlowMappingKey(false);

            case ParserState.YAML_PARSE_FLOW_MAPPING_VALUE_STATE:
                return ParseFlowMappingValue(false);

            case ParserState.YAML_PARSE_FLOW_MAPPING_EMPTY_VALUE_STATE:
                return ParseFlowMappingValue(true);

            default:
                Debug.Assert(false, "Invalid state"); // Invalid state.
                throw new InvalidOperationException();
        }
    }

    private void Skip()
    {
        if (_currentToken != null)
        {
            _currentToken = null;
            _scanner.ConsumeCurrent();
        }
    }

    /// <summary>
    /// Parse the production:
    /// stream   ::= STREAM-START implicit_document? explicit_document* STREAM-END
    ///              ************
    /// </summary>
    private Events.StreamStart ParseStreamStart()
    {
        if (GetCurrentToken() is not StreamStart streamStart)
        {
            var current = GetCurrentToken();
            throw new SemanticErrorException(current.Start, current.End, "Did not find expected <stream-start>.");
        }
        Skip();

        _state = ParserState.YAML_PARSE_IMPLICIT_DOCUMENT_START_STATE;
        return new Events.StreamStart(streamStart.Start, streamStart.End);
    }

    /// <summary>
    /// Parse the productions:
    /// implicit_document    ::= block_node DOCUMENT-END*
    ///                          *
    /// explicit_document    ::= DIRECTIVE* DOCUMENT-START block_node? DOCUMENT-END*
    ///                          *************************
    /// </summary>
    private Event ParseDocumentStart(bool isImplicit)
    {
        // Parse extra document end indicators.

        if (!isImplicit)
        {
            while (GetCurrentToken() is DocumentEnd)
            {
                Skip();
            }
        }

        // Parse an isImplicit document.

        if (isImplicit && !(GetCurrentToken() is VersionDirective || GetCurrentToken() is TagDirective || GetCurrentToken() is DocumentStart || GetCurrentToken() is StreamEnd))
        {
            var directives = new TagDirectiveCollection();
            ProcessDirectives(directives);

            _states.Push(ParserState.YAML_PARSE_DOCUMENT_END_STATE);

            _state = ParserState.YAML_PARSE_BLOCK_NODE_STATE;

            return new Events.DocumentStart(null, directives, true, GetCurrentToken().Start, GetCurrentToken().End);
        }

        // Parse an explicit document.

        else if (GetCurrentToken() is not StreamEnd)
        {
            var start = GetCurrentToken().Start;
            var directives = new TagDirectiveCollection();
            var versionDirective = ProcessDirectives(directives);

            var current = GetCurrentToken();
            if (current is not DocumentStart)
            {
                throw new SemanticErrorException(current.Start, current.End, "Did not find expected <document start>.");
            }

            _states.Push(ParserState.YAML_PARSE_DOCUMENT_END_STATE);

            _state = ParserState.YAML_PARSE_DOCUMENT_CONTENT_STATE;

            Event evt = new Events.DocumentStart(versionDirective, directives, false, start, current.End);
            Skip();
            return evt;
        }

        // Parse the stream end.

        else
        {
            _state = ParserState.YAML_PARSE_END_STATE;

            Event evt = new Events.StreamEnd(GetCurrentToken().Start, GetCurrentToken().End);
            // Do not call skip here because that would throw an exception
            if (_scanner.InternalMoveNext())
            {
                throw new InvalidOperationException("The scanner should contain no more tokens.");
            }
            return evt;
        }
    }

    /// <summary>Parse directives.</summary>
    private VersionDirective? ProcessDirectives(TagDirectiveCollection tags)
    {
        VersionDirective? version = null;

        while (true)
        {
            if (GetCurrentToken() is VersionDirective currentVersion)
            {
                if (version != null)
                {
                    throw new SemanticErrorException(currentVersion.Start, currentVersion.End, "Found duplicate %YAML directive.");
                }

                if (!Constants.IsSupportedYamlVersion(currentVersion.Version))
                {
                    throw new SemanticErrorException(currentVersion.Start, currentVersion.End, "Found incompatible YAML document.");
                }

                version = currentVersion;
            }
            else if (GetCurrentToken() is TagDirective tag)
            {
                if (_tagDirectives.Contains(tag.Handle))
                {
                    throw new SemanticErrorException(tag.Start, tag.End, "Found duplicate %TAG directive.");
                }
                _tagDirectives.Add(tag);
                if (tags != null)
                {
                    tags.Add(tag);
                }
            }
            else
            {
                break;
            }

            Skip();
        }

        if (tags != null)
        {
            AddDefaultTagDirectives(tags);
        }
        AddDefaultTagDirectives(_tagDirectives);

        return version;
    }

    private static void AddDefaultTagDirectives(TagDirectiveCollection directives)
    {
        foreach (var directive in Constants.DefaultTagDirectives)
        {
            if (!directives.Contains(directive))
            {
                directives.Add(directive);
            }
        }
    }

    /// <summary>
    /// Parse the productions:
    /// explicit_document    ::= DIRECTIVE* DOCUMENT-START block_node? DOCUMENT-END*
    ///                                                    ***********
    /// </summary>
    private Event ParseDocumentContent()
    {
        if (
            GetCurrentToken() is VersionDirective ||
            GetCurrentToken() is TagDirective ||
            GetCurrentToken() is DocumentStart ||
            GetCurrentToken() is DocumentEnd ||
            GetCurrentToken() is StreamEnd
        )
        {
            _state = _states.Pop();
            return ProcessEmptyScalar(_scanner.CurrentPosition);
        }
        else
        {
            return ParseNode(true, false);
        }
    }

    /// <summary>Generate an empty scalar event.</summary>
    private static Events.Scalar ProcessEmptyScalar(Mark position)
    {
        return new Events.Scalar(null, null, string.Empty, ScalarStyle.Plain, true, false, position, position);
    }

    /// <summary>
    /// Parse the productions:
    /// block_node_or_indentless_sequence    ::=
    ///                          ALIAS
    ///                          *****
    ///                          | properties (block_content | indentless_block_sequence)?
    ///                            **********  *
    ///                          | block_content | indentless_block_sequence
    ///                            *
    /// block_node           ::= ALIAS
    ///                          *****
    ///                          | properties block_content?
    ///                            ********** *
    ///                          | block_content
    ///                            *
    /// flow_node            ::= ALIAS
    ///                          *****
    ///                          | properties flow_content?
    ///                            ********** *
    ///                          | flow_content
    ///                            *
    /// properties           ::= TAG ANCHOR? | ANCHOR TAG?
    ///                          *************************
    /// block_content        ::= block_collection | flow_collection | SCALAR
    ///                                                               ******
    /// flow_content         ::= flow_collection | SCALAR
    ///                                            ******
    /// </summary>
    private Event ParseNode(bool isBlock, bool isIndentlessSequence)
    {
        if (GetCurrentToken() is AnchorAlias alias)
        {
            _state = _states.Pop();
            Event evt = new Events.AnchorAlias(alias.Value, alias.Start, alias.End);
            Skip();
            return evt;
        }

        var start = GetCurrentToken().Start;

        Anchor? anchor = null;
        Tag? tag = null;

        // The anchor and the tag can be in any order. This loop repeats at most twice.
        while (true)
        {
            if (anchor == null && (anchor = GetCurrentToken() as Anchor) != null)
            {
                Skip();
            }
            else if (tag == null && (tag = GetCurrentToken() as Tag) != null)
            {
                Skip();
            }
            else
            {
                break;
            }
        }

        string? tagName = null;
        if (tag != null)
        {
            if (string.IsNullOrEmpty(tag.Handle))
            {
                tagName = tag.Suffix;
            }
            else if (_tagDirectives.Contains(tag.Handle))
            {
                tagName = string.Concat(_tagDirectives[tag.Handle].Prefix, tag.Suffix);
            }
            else
            {
                throw new SemanticErrorException(tag.Start, tag.End, "While parsing a node, find undefined tag handle.");
            }
        }
        if (string.IsNullOrEmpty(tagName))
        {
            tagName = null;
        }

        var anchorName = anchor != null ? string.IsNullOrEmpty(anchor.Value) ? null : anchor.Value : null;

        bool isImplicit = string.IsNullOrEmpty(tagName);

        if (isIndentlessSequence && GetCurrentToken() is BlockEntry)
        {
            _state = ParserState.YAML_PARSE_INDENTLESS_SEQUENCE_ENTRY_STATE;

            return new Events.SequenceStart(
                anchorName,
                tagName,
                isImplicit,
                YamlStyle.Block,
                start,
                GetCurrentToken().End
            );
        }
        else
        {
            if (GetCurrentToken() is Scalar scalar)
            {
                bool isPlainImplicit = false;
                bool isQuotedImplicit = false;
                if ((scalar.Style == ScalarStyle.Plain && tagName == null) || tagName == Constants.DefaultHandle)
                {
                    isPlainImplicit = true;
                }
                else if (tagName == null)
                {
                    isQuotedImplicit = true;
                }

                _state = _states.Pop();
                Event evt = new Events.Scalar(anchorName, tagName, scalar.Value, scalar.Style, isPlainImplicit, isQuotedImplicit, start, scalar.End);

                Skip();
                return evt;
            }

            if (GetCurrentToken() is FlowSequenceStart flowSequenceStart)
            {
                _state = ParserState.YAML_PARSE_FLOW_SEQUENCE_FIRST_ENTRY_STATE;
                return new Events.SequenceStart(anchorName, tagName, isImplicit, YamlStyle.Flow, start, flowSequenceStart.End);
            }

            if (GetCurrentToken() is FlowMappingStart flowMappingStart)
            {
                _state = ParserState.YAML_PARSE_FLOW_MAPPING_FIRST_KEY_STATE;
                return new Events.MappingStart(anchorName, tagName, isImplicit, YamlStyle.Flow, start, flowMappingStart.End);
            }

            if (isBlock)
            {
                if (GetCurrentToken() is BlockSequenceStart blockSequenceStart)
                {
                    _state = ParserState.YAML_PARSE_BLOCK_SEQUENCE_FIRST_ENTRY_STATE;
                    return new Events.SequenceStart(anchorName, tagName, isImplicit, YamlStyle.Block, start, blockSequenceStart.End);
                }

                if (GetCurrentToken() is BlockMappingStart)
                {
                    _state = ParserState.YAML_PARSE_BLOCK_MAPPING_FIRST_KEY_STATE;
                    return new Events.MappingStart(anchorName, tagName, isImplicit, YamlStyle.Block, start, GetCurrentToken().End);
                }
            }

            if (anchorName != null || tag != null)
            {
                _state = _states.Pop();
                return new Events.Scalar(anchorName, tagName, string.Empty, ScalarStyle.Plain, isImplicit, false, start, GetCurrentToken().End);
            }

            var current = GetCurrentToken();
            throw new SemanticErrorException(current.Start, current.End, "While parsing a node, did not find expected node content.");
        }
    }

    /// <summary>
    /// Parse the productions:
    /// implicit_document    ::= block_node DOCUMENT-END*
    ///                                     *************
    /// explicit_document    ::= DIRECTIVE* DOCUMENT-START block_node? DOCUMENT-END*
    ///                                                                *************
    /// </summary>
    private Events.DocumentEnd ParseDocumentEnd()
    {
        bool isImplicit = true;
        var start = GetCurrentToken().Start;
        var end = start;

        if (GetCurrentToken() is DocumentEnd)
        {
            end = GetCurrentToken().End;
            Skip();
            isImplicit = false;
        }

        _tagDirectives.Clear();

        _state = ParserState.YAML_PARSE_DOCUMENT_START_STATE;
        return new Events.DocumentEnd(isImplicit, start, end);
    }

    /// <summary>
    /// Parse the productions:
    /// block_sequence ::= BLOCK-SEQUENCE-START (BLOCK-ENTRY block_node?)* BLOCK-END
    ///                    ********************  *********** *             *********
    /// </summary>
    private Event ParseBlockSequenceEntry(bool isFirst)
    {
        if (isFirst)
        {
            GetCurrentToken();
            Skip();
        }

        if (GetCurrentToken() is BlockEntry)
        {
            var mark = GetCurrentToken().End;

            Skip();
            if (!(GetCurrentToken() is BlockEntry || GetCurrentToken() is BlockEnd))
            {
                _states.Push(ParserState.YAML_PARSE_BLOCK_SEQUENCE_ENTRY_STATE);
                return ParseNode(true, false);
            }
            else
            {
                _state = ParserState.YAML_PARSE_BLOCK_SEQUENCE_ENTRY_STATE;
                return ProcessEmptyScalar(mark);
            }
        }

        else if (GetCurrentToken() is BlockEnd)
        {
            _state = _states.Pop();
            Event evt = new Events.SequenceEnd(GetCurrentToken().Start, GetCurrentToken().End);
            Skip();
            return evt;
        }

        else
        {
            var current = GetCurrentToken();
            throw new SemanticErrorException(current.Start, current.End, "While parsing a block collection, did not find expected '-' indicator.");
        }
    }

    /// <summary>
    /// Parse the productions:
    /// indentless_sequence  ::= (BLOCK-ENTRY block_node?)+
    ///                           *********** *
    /// </summary>
    private Event ParseIndentlessSequenceEntry()
    {
        if (GetCurrentToken() is BlockEntry)
        {
            var mark = GetCurrentToken().End;
            Skip();

            if (!(GetCurrentToken() is BlockEntry || GetCurrentToken() is Key || GetCurrentToken() is Value || GetCurrentToken() is BlockEnd))
            {
                _states.Push(ParserState.YAML_PARSE_INDENTLESS_SEQUENCE_ENTRY_STATE);
                return ParseNode(true, false);
            }
            else
            {
                _state = ParserState.YAML_PARSE_INDENTLESS_SEQUENCE_ENTRY_STATE;
                return ProcessEmptyScalar(mark);
            }
        }
        else
        {
            _state = _states.Pop();
            return new Events.SequenceEnd(GetCurrentToken().Start, GetCurrentToken().End);
        }
    }

    /// <summary>
    /// Parse the productions:
    /// block_mapping        ::= BLOCK-MAPPING_START
    ///                          *******************
    ///                          ((KEY block_node_or_indentless_sequence?)?
    ///                            *** *
    ///                          (VALUE block_node_or_indentless_sequence?)?)*
    ///
    ///                          BLOCK-END
    ///                          *********
    /// </summary>
    private Event ParseBlockMappingKey(bool isFirst)
    {
        if (isFirst)
        {
            GetCurrentToken();
            Skip();
        }

        if (GetCurrentToken() is Key)
        {
            var mark = GetCurrentToken().End;
            Skip();
            if (!(GetCurrentToken() is Key || GetCurrentToken() is Value || GetCurrentToken() is BlockEnd))
            {
                _states.Push(ParserState.YAML_PARSE_BLOCK_MAPPING_VALUE_STATE);
                return ParseNode(true, true);
            }
            else
            {
                _state = ParserState.YAML_PARSE_BLOCK_MAPPING_VALUE_STATE;
                return ProcessEmptyScalar(mark);
            }
        }

        else if (GetCurrentToken() is BlockEnd)
        {
            _state = _states.Pop();
            Event evt = new Events.MappingEnd(GetCurrentToken().Start, GetCurrentToken().End);
            Skip();
            return evt;
        }

        else
        {
            var current = GetCurrentToken();
            throw new SemanticErrorException(current.Start, current.End, "While parsing a block mapping, did not find expected key.");
        }
    }

    /// <summary>
    /// Parse the productions:
    /// block_mapping        ::= BLOCK-MAPPING_START
    ///
    ///                          ((KEY block_node_or_indentless_sequence?)?
    ///
    ///                          (VALUE block_node_or_indentless_sequence?)?)*
    ///                           ***** *
    ///                          BLOCK-END
    ///
    /// </summary>
    private Event ParseBlockMappingValue()
    {
        if (GetCurrentToken() is Value)
        {
            var mark = GetCurrentToken().End;
            Skip();

            if (!(GetCurrentToken() is Key || GetCurrentToken() is Value || GetCurrentToken() is BlockEnd))
            {
                _states.Push(ParserState.YAML_PARSE_BLOCK_MAPPING_KEY_STATE);
                return ParseNode(true, true);
            }
            else
            {
                _state = ParserState.YAML_PARSE_BLOCK_MAPPING_KEY_STATE;
                return ProcessEmptyScalar(mark);
            }
        }

        else
        {
            _state = ParserState.YAML_PARSE_BLOCK_MAPPING_KEY_STATE;
            return ProcessEmptyScalar(GetCurrentToken().Start);
        }
    }

    /// <summary>
    /// Parse the productions:
    /// flow_sequence        ::= FLOW-SEQUENCE-START
    ///                          *******************
    ///                          (flow_sequence_entry FLOW-ENTRY)*
    ///                           *                   **********
    ///                          flow_sequence_entry?
    ///                          *
    ///                          FLOW-SEQUENCE-END
    ///                          *****************
    /// flow_sequence_entry  ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                          *
    /// </summary>
    private Event ParseFlowSequenceEntry(bool isFirst)
    {
        if (isFirst)
        {
            GetCurrentToken();
            Skip();
        }

        Event evt;
        if (GetCurrentToken() is not FlowSequenceEnd)
        {
            if (!isFirst)
            {
                if (GetCurrentToken() is FlowEntry)
                {
                    Skip();
                }
                else
                {
                    var current = GetCurrentToken();
                    throw new SemanticErrorException(current.Start, current.End, "While parsing a flow sequence, did not find expected ',' or ']'.");
                }
            }

            if (GetCurrentToken() is Key)
            {
                _state = ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_KEY_STATE;
                evt = new Events.MappingStart(null, null, true, YamlStyle.Flow);
                Skip();
                return evt;
            }
            else if (GetCurrentToken() is not FlowSequenceEnd)
            {
                _states.Push(ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_STATE);
                return ParseNode(false, false);
            }
        }

        _state = _states.Pop();
        evt = new Events.SequenceEnd(GetCurrentToken().Start, GetCurrentToken().End);
        Skip();
        return evt;
    }

    /// <summary>
    /// Parse the productions:
    /// flow_sequence_entry  ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                                      *** *
    /// </summary>
    private Event ParseFlowSequenceEntryMappingKey()
    {
        if (!(GetCurrentToken() is Value || GetCurrentToken() is FlowEntry || GetCurrentToken() is FlowSequenceEnd))
        {
            _states.Push(ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_VALUE_STATE);
            return ParseNode(false, false);
        }
        else
        {
            var mark = GetCurrentToken().End;
            Skip();
            _state = ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_VALUE_STATE;
            return ProcessEmptyScalar(mark);
        }
    }

    /// <summary>
    /// Parse the productions:
    /// flow_sequence_entry  ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                                                      ***** *
    /// </summary>
    private Event ParseFlowSequenceEntryMappingValue()
    {
        if (GetCurrentToken() is Value)
        {
            Skip();
            if (!(GetCurrentToken() is FlowEntry || GetCurrentToken() is FlowSequenceEnd))
            {
                _states.Push(ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_END_STATE);
                return ParseNode(false, false);
            }
        }
        _state = ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_MAPPING_END_STATE;
        return ProcessEmptyScalar(GetCurrentToken().Start);
    }

    /// <summary>
    /// Parse the productions:
    /// flow_sequence_entry  ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                                                                      *
    /// </summary>
    private Events.MappingEnd ParseFlowSequenceEntryMappingEnd()
    {
        _state = ParserState.YAML_PARSE_FLOW_SEQUENCE_ENTRY_STATE;
        return new Events.MappingEnd(GetCurrentToken().Start, GetCurrentToken().End);
    }

    /// <summary>
    /// Parse the productions:
    /// flow_mapping         ::= FLOW-MAPPING-START
    ///                          ******************
    ///                          (flow_mapping_entry FLOW-ENTRY)*
    ///                           *                  **********
    ///                          flow_mapping_entry?
    ///                          ******************
    ///                          FLOW-MAPPING-END
    ///                          ****************
    /// flow_mapping_entry   ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                          *           *** *
    /// </summary>
    private Event ParseFlowMappingKey(bool isFirst)
    {
        if (isFirst)
        {
            GetCurrentToken();
            Skip();
        }

        if (GetCurrentToken() is not FlowMappingEnd)
        {
            if (!isFirst)
            {
                if (GetCurrentToken() is FlowEntry)
                {
                    Skip();
                }
                else
                {
                    var current = GetCurrentToken();
                    throw new SemanticErrorException(current.Start, current.End, "While parsing a flow mapping,  did not find expected ',' or '}'.");
                }
            }

            if (GetCurrentToken() is Key)
            {
                Skip();

                if (!(GetCurrentToken() is Value || GetCurrentToken() is FlowEntry || GetCurrentToken() is FlowMappingEnd))
                {
                    _states.Push(ParserState.YAML_PARSE_FLOW_MAPPING_VALUE_STATE);
                    return ParseNode(false, false);
                }
                else
                {
                    _state = ParserState.YAML_PARSE_FLOW_MAPPING_VALUE_STATE;
                    return ProcessEmptyScalar(GetCurrentToken().Start);
                }
            }
            else if (GetCurrentToken() is not FlowMappingEnd)
            {
                _states.Push(ParserState.YAML_PARSE_FLOW_MAPPING_EMPTY_VALUE_STATE);
                return ParseNode(false, false);
            }
        }

        _state = _states.Pop();
        Event evt = new Events.MappingEnd(GetCurrentToken().Start, GetCurrentToken().End);
        Skip();
        return evt;
    }

    /// <summary>
    /// Parse the productions:
    /// flow_mapping_entry   ::= flow_node | KEY flow_node? (VALUE flow_node?)?
    ///                                   *                  ***** *
    /// </summary>
    private Event ParseFlowMappingValue(bool isEmpty)
    {
        if (isEmpty)
        {
            _state = ParserState.YAML_PARSE_FLOW_MAPPING_KEY_STATE;
            return ProcessEmptyScalar(GetCurrentToken().Start);
        }

        if (GetCurrentToken() is Value)
        {
            Skip();
            if (!(GetCurrentToken() is FlowEntry || GetCurrentToken() is FlowMappingEnd))
            {
                _states.Push(ParserState.YAML_PARSE_FLOW_MAPPING_KEY_STATE);
                return ParseNode(false, false);
            }
        }

        _state = ParserState.YAML_PARSE_FLOW_MAPPING_KEY_STATE;
        return ProcessEmptyScalar(GetCurrentToken().Start);
    }
}
