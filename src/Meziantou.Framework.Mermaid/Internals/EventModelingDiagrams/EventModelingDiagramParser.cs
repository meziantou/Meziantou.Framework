namespace Meziantou.Framework.Mermaid.Internals.EventModelingDiagrams;

/// <summary>
/// Parses the <c>eventmodeling</c> diagrams of Mermaid. The white space and the line feeds separate the tokens only, so
/// a line can hold several statements; <c>//</c> and <c>/* */</c> are comments. The references to the frames and to the
/// data blocks can come before their declaration.
/// </summary>
internal sealed class EventModelingDiagramParser
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "eventmodeling", "tf", "timeframe", "rf", "resetframe", "entity", "data", "note", "gwt", "given", "when", "then",
        "rmo", "readmodel", "ui", "cmd", "command", "evt", "event", "pcr", "processor",
        "json", "jsobj", "figma", "salt", "uri", "md", "html", "text",
    };

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly EventModelingDiagram _diagram = new();
    private readonly Dictionary<string, EventModelingFrame> _frames = new(StringComparer.Ordinal);
    private readonly List<(EventModelingFrame Frame, List<(string Id, int Offset)> Sources, (string Name, int Offset)? DataReference)> _pendingFrames = [];
    private readonly List<(string FrameId, int Offset, Action<EventModelingFrame> Create)> _pendingFrameReferences = [];

    private EventModelingDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static EventModelingDiagram Parse(ParseContext context, int position) => new EventModelingDiagramParser(context, position).Parse();

    private EventModelingDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (ReadWord() != "eventmodeling")
            throw _scanner.Error(keywordStart, "'eventmodeling' is expected.");

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "eventmodeling");
        while (true)
        {
            SkipTrivia();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        Resolve();
        return _diagram;
    }

    private void ParseStatement()
    {
        var start = _scanner.Position;
        var word = ReadWord();
        switch (word)
        {
            case "tf" or "timeframe" or "rf" or "resetframe":
                ParseFrame(start, isReset: word is "rf" or "resetframe");
                break;
            case "entity":
                SkipTrivia();
                _diagram.Entities.Add(ReadQualifiedName());
                break;
            case "data":
                {
                    SkipTrivia();
                    var name = ReadIdentifier();
                    _diagram.DataBlocks.Add(new EventModelingDataBlock(name, ReadData(block: true)!));
                    break;
                }

            case "note":
                {
                    SkipTrivia();
                    var frameStart = _scanner.Position;
                    var frameId = ReadFrameId();
                    var data = ReadData(block: true)!;
                    _pendingFrameReferences.Add((frameId, frameStart, frame => _diagram.Notes.Add(new EventModelingNote(frame, data))));
                    break;
                }

            case "gwt":
                ParseScenario();
                break;
            default:
                throw _scanner.Error(start, $"Unexpected '{(word.Length is 0 ? _scanner.Describe() : word)}': a statement is expected: tf, rf, entity, data, note or gwt.");
        }
    }

    /// <summary>Parses a frame: <c>tf 01 cmd AddItem -&gt;&gt; 02 [[data]] `json`{ ... }</c>.</summary>
    private void ParseFrame(int start, bool isReset)
    {
        SkipTrivia();
        var idStart = _scanner.Position;
        var id = ReadFrameId();
        if (_frames.ContainsKey(id))
            throw _scanner.Error(idStart, $"The frame '{id}' is already defined: each frame has its own number.");

        SkipTrivia();
        var kind = ReadKind();
        SkipTrivia();
        var entity = ReadQualifiedName();
        var frame = new EventModelingFrame(id, kind, entity) { IsReset = isReset };
        var sources = new List<(string Id, int Offset)>();
        while (true)
        {
            SkipTrivia();
            if (!_scanner.TryConsume("->>"))
                break;

            SkipTrivia();
            var sourceStart = _scanner.Position;
            sources.Add((ReadFrameId(), sourceStart));
        }

        (string Name, int Offset)? reference = null;
        if (_scanner.TryConsume("[["))
        {
            SkipTrivia();
            var referenceStart = _scanner.Position;
            reference = (ReadIdentifier(), referenceStart);
            SkipTrivia();
            _scanner.Expect("]]");
            SkipTrivia();
        }

        frame.Data = ReadData(block: false);
        if (_frames.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} frames.");

        _frames[id] = frame;
        _diagram.Frames.Add(frame);
        _pendingFrames.Add((frame, sources, reference));
    }

    /// <summary>Parses a scenario: <c>gwt 01 given evt A when cmd B then evt C</c>.</summary>
    private void ParseScenario()
    {
        SkipTrivia();
        var frameStart = _scanner.Position;
        var frameId = ReadFrameId();
        SkipTrivia();
        if (ReadWord() != "given")
            throw _scanner.Error("'given' is expected.");

        var given = ReadSteps();
        if (given.Count is 0)
            throw _scanner.Error("A step is expected after 'given'.");

        var when = new List<EventModelingStep>();
        SkipTrivia();
        var save = _scanner.Position;
        var word = ReadWord();
        if (word == "when")
        {
            when = ReadSteps();
            if (when.Count is 0)
                throw _scanner.Error("A step is expected after 'when'.");

            SkipTrivia();
            save = _scanner.Position;
            word = ReadWord();
        }

        if (word != "then")
        {
            _scanner.Position = save;
            throw _scanner.Error("'then' is expected.");
        }

        var then = ReadSteps();
        if (then.Count is 0)
            throw _scanner.Error("A step is expected after 'then'.");

        _pendingFrameReferences.Add((frameId, frameStart, frame =>
        {
            var scenario = new EventModelingScenario(frame);
            foreach (var step in given)
            {
                scenario.Given.Add(step);
            }

            foreach (var step in when)
            {
                scenario.When.Add(step);
            }

            foreach (var step in then)
            {
                scenario.Then.Add(step);
            }

            _diagram.Scenarios.Add(scenario);
        }));
    }

    private List<EventModelingStep> ReadSteps()
    {
        var result = new List<EventModelingStep>();
        while (true)
        {
            SkipTrivia();
            var save = _scanner.Position;
            var word = ReadWord();
            var kind = ToKind(word);
            if (kind is null)
            {
                _scanner.Position = save;
                return result;
            }

            SkipTrivia();
            result.Add(new EventModelingStep(kind.Value, ReadIdentifier()));
            if (result.Count > _context.Options.MaxNodes)
                throw _scanner.Error(save, $"The scenario has more than {_context.Options.MaxNodes} steps.");
        }
    }

    /// <summary>Resolves the references to the frames and to the data blocks, which can come later in the text.</summary>
    private void Resolve()
    {
        var blocks = new Dictionary<string, EventModelingDataBlock>(StringComparer.Ordinal);
        foreach (var block in _diagram.DataBlocks)
        {
            blocks.TryAdd(block.Name, block);
        }

        foreach (var (frame, sources, reference) in _pendingFrames)
        {
            foreach (var (id, offset) in sources)
            {
                if (!_frames.TryGetValue(id, out var source))
                {
                    _context.Warn(offset, $"The frame '{id}' is not defined: the relation is ignored.");
                    continue;
                }

                if (!frame.Sources.Contains(source))
                {
                    frame.Sources.Add(source);
                }

                if (!IsAllowedSource(frame.Kind, source.Kind))
                {
                    _context.Warn(offset, $"A {Describe(frame.Kind)} receives input from {DescribeAllowedSources(frame.Kind)}, not from '{Describe(source.Kind)}'.");
                }
            }

            if (reference is { } dataReference)
            {
                if (blocks.TryGetValue(dataReference.Name, out var block))
                {
                    frame.DataBlock = block;
                }
                else
                {
                    _context.Warn(dataReference.Offset, $"The data block '{dataReference.Name}' is not defined: the reference is ignored.");
                }
            }
        }

        foreach (var (frameId, offset, create) in _pendingFrameReferences)
        {
            if (_frames.TryGetValue(frameId, out var frame))
            {
                create(frame);
            }
            else
            {
                _context.Warn(offset, $"The frame '{frameId}' is not defined: the statement is ignored.");
            }
        }
    }

    internal static bool IsAllowedSource(EventModelingEntityKind target, EventModelingEntityKind source) => target switch
    {
        EventModelingEntityKind.Command => source is EventModelingEntityKind.Ui or EventModelingEntityKind.Processor,
        EventModelingEntityKind.Event => source is EventModelingEntityKind.Command,
        EventModelingEntityKind.ReadModel => source is EventModelingEntityKind.Event,
        _ => source is EventModelingEntityKind.ReadModel,
    };

    private static string Describe(EventModelingEntityKind kind) => kind switch
    {
        EventModelingEntityKind.Ui => "ui",
        EventModelingEntityKind.Processor => "processor",
        EventModelingEntityKind.Command => "command",
        EventModelingEntityKind.ReadModel => "read model",
        _ => "event",
    };

    private static string DescribeAllowedSources(EventModelingEntityKind kind) => kind switch
    {
        EventModelingEntityKind.Command => "a ui or a processor",
        EventModelingEntityKind.Event => "a command",
        EventModelingEntityKind.ReadModel => "an event",
        _ => "a read model",
    };

    /// <summary>
    /// Reads data, after an optional format between backticks: inline, <c>{ ... }</c> or a quoted text on one line; or a
    /// block, <c>{</c> alone at the end of its line, up to a line that starts with <c>}</c>.
    /// </summary>
    private EventModelingData? ReadData(bool block)
    {
        SkipTrivia();
        EventModelingDataFormat? format = null;
        var start = _scanner.Position;
        if (_scanner.TryConsume("`"))
        {
            SkipTrivia();
            var formatStart = _scanner.Position;
            format = ReadWord() switch
            {
                "json" => EventModelingDataFormat.Json,
                "jsobj" => EventModelingDataFormat.JavaScriptObject,
                "figma" => EventModelingDataFormat.Figma,
                "salt" => EventModelingDataFormat.Salt,
                "uri" => EventModelingDataFormat.Uri,
                "md" => EventModelingDataFormat.Markdown,
                "html" => EventModelingDataFormat.Html,
                "text" => EventModelingDataFormat.Text,
                _ => throw _scanner.Error(formatStart, "A data format is expected: json, jsobj, figma, salt, uri, md, html or text."),
            };
            SkipTrivia();
            _scanner.Expect("`");
            SkipTrivia();
        }

        var text = _scanner.Text;
        var dataStart = _scanner.Position;
        var lineEnd = text.IndexOfOrdinal('\n', dataStart);
        if (lineEnd < 0)
        {
            lineEnd = text.Length;
        }

        if (block)
        {
            if (_scanner.Current != '{')
                throw _scanner.Error("The data block is expected: '{' at the end of a line, the data, and '}' at the start of a line.");

            var afterBrace = dataStart + 1;
            if (!string.IsNullOrWhiteSpace(text[afterBrace..lineEnd]) || lineEnd >= text.Length)
                throw _scanner.Error(dataStart, "A data block starts with '{' alone at the end of its line.");

            // The block ends at the first line that starts with } followed by white space or the end of the text
            var contentStart = lineEnd + 1;
            var position = contentStart - 1;
            while (true)
            {
                if (position + 1 < text.Length && text[position + 1] == '}' && (position + 2 >= text.Length || char.IsWhiteSpace(text[position + 2])))
                    break;

                position = text.IndexOfOrdinal('\n', position + 1);
                if (position < 0)
                    throw _scanner.Error(dataStart, "The data block is not closed by '}' at the start of a line.");
            }

            var content = position < contentStart ? string.Empty : text[contentStart..position];
            _scanner.Position = position + 2;
            return new EventModelingData(content, format);
        }

        var close = _scanner.Current switch
        {
            '{' => '}',
            '"' => '"',
            '\'' => '\'',
            _ => '\0',
        };
        if (close == '\0')
        {
            if (format is not null)
                throw _scanner.Error("The data is expected after its format.");

            _scanner.Position = start;
            return null;
        }

        // As Mermaid, the inline data goes to the last closing character of the line
        var last = text.LastIndexOf(close.ToString(), lineEnd - 1, lineEnd - dataStart, StringComparison.Ordinal);
        if (last <= dataStart)
            throw _scanner.Error(dataStart, "The inline data is not closed on its line.");

        _scanner.Position = last + 1;
        return new EventModelingData(text[(dataStart + 1)..last].Trim(), format);
    }

    private EventModelingEntityKind ReadKind()
    {
        var start = _scanner.Position;
        var word = ReadWord();
        return ToKind(word) ?? throw _scanner.Error(start, $"Unexpected '{(word.Length is 0 ? _scanner.Describe() : word)}': a kind is expected: ui, pcr, cmd, rmo or evt.");
    }

    internal static EventModelingEntityKind? ToKind(string word) => word switch
    {
        "ui" => EventModelingEntityKind.Ui,
        "pcr" or "processor" => EventModelingEntityKind.Processor,
        "cmd" or "command" => EventModelingEntityKind.Command,
        "rmo" or "readmodel" => EventModelingEntityKind.ReadModel,
        "evt" or "event" => EventModelingEntityKind.Event,
        _ => null,
    };

    /// <summary>Reads a frame number: one to three digits.</summary>
    private string ReadFrameId()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiDigit(_scanner.Current))
        {
            _scanner.Position++;
        }

        var length = _scanner.Position - start;
        if (length is 0 or > 3)
            throw _scanner.Error(start, "A frame number of one to three digits is expected.");

        return _scanner.Text[start.._scanner.Position];
    }

    /// <summary>Reads a name made of identifiers separated by dots: <c>Cart.ItemAdded</c>.</summary>
    private string ReadQualifiedName()
    {
        var name = ReadIdentifier();
        while (true)
        {
            var save = _scanner.Position;
            SkipTrivia();
            if (!_scanner.TryConsume("."))
            {
                _scanner.Position = save;
                return name;
            }

            SkipTrivia();
            name += "." + ReadIdentifier();
        }
    }

    private string ReadIdentifier()
    {
        var start = _scanner.Position;
        var word = ReadWord();
        if (word.Length is 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an identifier is expected.");

        if (Keywords.Contains(word))
            throw _scanner.Error(start, $"'{word}' is a keyword and cannot be an identifier.");

        return word;
    }

    internal static bool IsIdentifier(string value) => value.Length > 0 && (char.IsAsciiLetter(value[0]) || value[0] == '_') && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_') && !Keywords.Contains(value);

    /// <summary>Reads a word: a letter or <c>_</c>, then letters, digits and <c>_</c>.</summary>
    private string ReadWord()
    {
        var start = _scanner.Position;
        if (_scanner.IsEnd || !(char.IsAsciiLetter(_scanner.Current) || _scanner.Current == '_'))
            return string.Empty;

        while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    /// <summary>Skips the white space, the line feeds, and the <c>//</c>, <c>/* */</c> and <c>%%</c> comments.</summary>
    private void SkipTrivia()
    {
        var text = _scanner.Text;
        while (!_scanner.IsEnd)
        {
            var c = _scanner.Current;
            if (char.IsWhiteSpace(c))
            {
                _scanner.Position++;
            }
            else if ((c == '/' && _scanner.Peek() == '/') || _scanner.IsCommentAt(_scanner.Position))
            {
                _scanner.ReadLine();
            }
            else if (c == '/' && _scanner.Peek() == '*')
            {
                var end = text.IndexOf("*/", _scanner.Position + 2, StringComparison.Ordinal);
                if (end < 0)
                    throw _scanner.Error("The comment is not closed by '*/'.");

                _scanner.Position = end + 2;
            }
            else
            {
                return;
            }
        }
    }
}
