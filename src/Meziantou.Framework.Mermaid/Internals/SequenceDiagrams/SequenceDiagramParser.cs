using System.Globalization;

namespace Meziantou.Framework.Mermaid.Internals.SequenceDiagrams;

/// <summary>
/// Parses the <c>sequenceDiagram</c> diagrams of Mermaid. The keywords ignore the case, and a semicolon separates the
/// statements as a line feed does. As in Mermaid, a participant that a message names is declared there.
/// </summary>
internal sealed class SequenceDiagramParser
{
    /// <summary>The arrows of the messages, the longest first: the line style and the decorations of the start and the end.</summary>
    internal static readonly (string Text, SequenceLineStyle LineStyle, SequenceArrowHead Start, SequenceArrowHead End)[] Arrows =
    [
        ("<<-->>", SequenceLineStyle.Dotted, SequenceArrowHead.Arrow, SequenceArrowHead.Arrow),
        ("<<->>", SequenceLineStyle.Solid, SequenceArrowHead.Arrow, SequenceArrowHead.Arrow),
        ("-->>", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.Arrow),
        ("--|\\", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.HalfArrowTop),
        ("--|/", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.HalfArrowBottom),
        ("--\\\\", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.StickTop),
        ("--//", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.StickBottom),
        ("/|--", SequenceLineStyle.Dotted, SequenceArrowHead.HalfArrowTop, SequenceArrowHead.None),
        ("\\|--", SequenceLineStyle.Dotted, SequenceArrowHead.HalfArrowBottom, SequenceArrowHead.None),
        ("//--", SequenceLineStyle.Dotted, SequenceArrowHead.StickTop, SequenceArrowHead.None),
        ("\\\\--", SequenceLineStyle.Dotted, SequenceArrowHead.StickBottom, SequenceArrowHead.None),
        ("->>", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.Arrow),
        ("-->", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.None),
        ("--x", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.Cross),
        ("--)", SequenceLineStyle.Dotted, SequenceArrowHead.None, SequenceArrowHead.OpenArrow),
        ("-|\\", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.HalfArrowTop),
        ("-|/", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.HalfArrowBottom),
        ("-\\\\", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.StickTop),
        ("-//", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.StickBottom),
        ("/|-", SequenceLineStyle.Solid, SequenceArrowHead.HalfArrowTop, SequenceArrowHead.None),
        ("\\|-", SequenceLineStyle.Solid, SequenceArrowHead.HalfArrowBottom, SequenceArrowHead.None),
        ("//-", SequenceLineStyle.Solid, SequenceArrowHead.StickTop, SequenceArrowHead.None),
        ("\\\\-", SequenceLineStyle.Solid, SequenceArrowHead.StickBottom, SequenceArrowHead.None),
        ("->", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.None),
        ("-x", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.Cross),
        ("-)", SequenceLineStyle.Solid, SequenceArrowHead.None, SequenceArrowHead.OpenArrow),
    ];

    private static readonly HashSet<string> Keywords = new(StringComparer.OrdinalIgnoreCase)
    {
        "participant", "actor", "create", "destroy", "box", "end", "loop", "rect", "opt", "alt", "else", "par", "par_over", "and", "critical",
        "option", "break", "note", "over", "activate", "deactivate", "title", "autonumber", "off", "links", "link", "properties", "details",
        "sequenceDiagram", "accTitle", "accDescr",
    };

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly SequenceDiagram _diagram = new();
    private readonly Dictionary<string, SequenceParticipant> _participants = new(StringComparer.Ordinal);
    private readonly Dictionary<SequenceParticipant, SequenceBox> _boxOf = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SequenceParticipant, int> _activations = new(ReferenceEqualityComparer.Instance);
    private readonly Stack<(SequenceBlock Block, int Offset)> _blocks = new();
    private SequenceBox? _box;
    private int _boxOffset;
    private SequenceParticipant? _pendingCreate;
    private SequenceParticipant? _pendingDestroy;
    private int _pendingOffset;
    private int _stepCount;

    private SequenceDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static SequenceDiagram Parse(ParseContext context, int position) => new SequenceDiagramParser(context, position).Parse();

    private IList<SequenceStep> CurrentSteps => _blocks.Count > 0 ? _blocks.Peek().Block.Sections[^1].Steps : _diagram.Steps;

    private SequenceDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!TryConsumeKeyword("sequenceDiagram"))
            throw _scanner.Error(keywordStart, "'sequenceDiagram' is expected.");

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "sequence");
        if (_context.Configuration.GetBoolean("sequence", "mirrorActors") is { } mirror)
        {
            _diagram.MirrorParticipants = mirror;
        }

        if (_context.Configuration.GetBoolean("sequence", "showSequenceNumbers") is true)
        {
            _diagram.Steps.Add(new SequenceAutonumber());
        }

        while (true)
        {
            _scanner.SkipSeparators();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_box is not null)
            throw _scanner.Error(_boxOffset, "The box is not closed by 'end'.");

        if (_blocks.Count > 0)
            throw _scanner.Error(_blocks.Peek().Offset, "The block is not closed by 'end'.");

        if (_pendingCreate is not null)
            throw _scanner.Error(_pendingOffset, $"The created participant '{_pendingCreate.Id}' has no creating message after its declaration.");

        if (_pendingDestroy is not null)
            throw _scanner.Error(_pendingOffset, $"The destroyed participant '{_pendingDestroy.Id}' has no destroying message after its declaration.");

        return _diagram;
    }

    private void ParseStatement()
    {
        var start = _scanner.Position;
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        if (_box is not null)
        {
            if (TryConsumeKeyword("end"))
            {
                _box = null;
                EndStatement();
                return;
            }

            if (StartsWithKeyword("participant") || StartsWithKeyword("actor"))
            {
                ParseDeclaration(start, isCreated: false);
                return;
            }

            throw _scanner.Error("A box holds only participant and actor declarations.");
        }

        if (StartsWithKeyword("participant") || StartsWithKeyword("actor"))
        {
            ParseDeclaration(start, isCreated: false);
            return;
        }

        if (TryConsumeKeyword("create"))
        {
            SkipSpaces();
            if (!StartsWithKeyword("participant") && !StartsWithKeyword("actor"))
                throw _scanner.Error("'participant' or 'actor' is expected after 'create'.");

            ParseDeclaration(start, isCreated: true);
            return;
        }

        if (TryConsumeKeyword("destroy"))
        {
            SkipSpaces();
            var participant = GetParticipant(ReadDeclaredId(), start);
            EndStatement();
            _pendingDestroy = participant;
            _pendingOffset = start;
            return;
        }

        if (TryConsumeKeyword("box"))
        {
            ParseBox(start);
            return;
        }

        if (TryConsumeKeyword("end"))
        {
            if (_blocks.Count is 0)
                throw _scanner.Error(start, "'end' closes no block.");

            _blocks.Pop();
            EndStatement();
            return;
        }

        foreach (var (keyword, kind) in new[] { ("loop", SequenceBlockKind.Loop), ("rect", SequenceBlockKind.Rectangle), ("opt", SequenceBlockKind.Optional), ("alt", SequenceBlockKind.Alternative), ("par_over", SequenceBlockKind.ParallelOverlap), ("par", SequenceBlockKind.Parallel), ("critical", SequenceBlockKind.Critical), ("break", SequenceBlockKind.Break) })
        {
            if (TryConsumeKeyword(keyword))
            {
                ParseBlock(start, kind);
                return;
            }
        }

        foreach (var (keyword, kinds) in new[] { ("else", new[] { SequenceBlockKind.Alternative }), ("and", new[] { SequenceBlockKind.Parallel, SequenceBlockKind.ParallelOverlap }), ("option", new[] { SequenceBlockKind.Critical }) })
        {
            if (TryConsumeKeyword(keyword))
            {
                if (_blocks.Count is 0 || !kinds.Contains(_blocks.Peek().Block.Kind))
                    throw _scanner.Error(start, $"'{keyword}' is not in a block that accepts it.");

                _blocks.Peek().Block.Sections.Add(new SequenceBlockSection(ReadLabel()));
                return;
            }
        }

        if (TryConsumeKeyword("note"))
        {
            ParseNote(start);
            return;
        }

        if (TryConsumeKeyword("activate") || TryConsumeKeyword("deactivate"))
        {
            var isActivation = !_scanner.Text.AsSpan(start, 2).Equals("de", StringComparison.OrdinalIgnoreCase);
            SkipSpaces();
            var participant = GetParticipant(ReadDeclaredId(), start);
            EndStatement();
            if (isActivation)
            {
                _activations[participant] = _activations.GetValueOrDefault(participant) + 1;
            }
            else
            {
                Deactivate(participant, start);
            }

            AddStep(new SequenceActivation(participant, isActivation), start);
            return;
        }

        if (TryConsumeKeyword("autonumber"))
        {
            ParseAutonumber(start);
            return;
        }

        if (StartsWithKeyword("title"))
        {
            _scanner.Position += "title".Length;
            _scanner.TryConsume(":");
            var title = ReadText().Trim();
            _diagram.Title = title.Length is 0 ? null : title;
            return;
        }

        foreach (var keyword in new[] { "links", "link", "properties", "details" })
        {
            if (StartsWithKeyword(keyword) && IsMenuStatement(keyword.Length))
            {
                _scanner.Position += keyword.Length;
                SkipSpaces();
                GetParticipant(ReadName(), start);
                SkipSpaces();
                _scanner.Expect(":");
                ReadText();
                _context.Warn(start, $"The '{keyword}' statements (actor menus) are not supported: they are ignored.");
                return;
            }
        }

        ParseMessage(start);
    }

    /// <summary>Indicates whether a menu keyword starts a statement: it is followed by spaces and a name, otherwise it is the name of a participant.</summary>
    private bool IsMenuStatement(int length)
    {
        var position = _scanner.Position + length;
        var spaces = position;
        while (spaces < _scanner.Text.Length && _scanner.Text[spaces] is ' ' or '\t')
        {
            spaces++;
        }

        return spaces > position && spaces < _scanner.Text.Length && _scanner.Text[spaces] is not ('/' or '\\' or '+' or '(' or ')' or '<' or '>' or ':' or '\n' or ',' or ';' or '-' or ' ' or '\t');
    }

    private void ParseDeclaration(int start, bool isCreated)
    {
        var isActor = StartsWithKeyword("actor");
        _scanner.Position += isActor ? "actor".Length : "participant".Length;
        SkipSpaces();
        var idStart = _scanner.Position;
        var text = _scanner.Text;

        // An identifier before a configuration or an alias has no spaces; otherwise, it is the rest of the line
        var end = idStart;
        while (end < text.Length && text[end] is not ('<' or '>' or ':' or '\n' or ',' or ';' or '@' or ' ' or '\t'))
        {
            end++;
        }

        var after = end;
        while (after < text.Length && text[after] is ' ' or '\t')
        {
            after++;
        }

        string id;
        Dictionary<string, object?>? configuration = null;
        var hasAlias = false;
        if (end > idStart && after + 1 < text.Length && text[after] == '@' && text[after + 1] == '{')
        {
            id = text[idStart..end];
            _scanner.Position = after + 1;
            configuration = MermaidCommon.ReadInlineMapping(_scanner);
            SkipSpaces();
            hasAlias = TryConsumeAs();
        }
        else if (end > idStart && after > end && IsAs(after))
        {
            id = text[idStart..end];
            _scanner.Position = after;
            hasAlias = TryConsumeAs();
        }
        else
        {
            id = ReadDeclaredId();
        }

        DiagramText? alias = null;
        if (hasAlias)
        {
            alias = ReadLabel();
        }
        else
        {
            EndStatement();
        }

        ValidateId(id, idStart);
        var kind = isActor ? SequenceParticipantKind.Actor : SequenceParticipantKind.Participant;
        if (configuration is not null)
        {
            foreach (var (key, value) in configuration)
            {
                switch (key)
                {
                    case "type":
                        kind = (value as string) switch
                        {
                            "participant" => SequenceParticipantKind.Participant,
                            "actor" => SequenceParticipantKind.Actor,
                            "boundary" => SequenceParticipantKind.Boundary,
                            "control" => SequenceParticipantKind.Control,
                            "entity" => SequenceParticipantKind.Entity,
                            "database" => SequenceParticipantKind.Database,
                            "collections" => SequenceParticipantKind.Collections,
                            "queue" => SequenceParticipantKind.Queue,
                            _ => throw _scanner.Error(idStart, $"'{value}' is not a type of participant: use participant, actor, boundary, control, entity, database, collections or queue."),
                        };
                        break;
                    case "alias":
                        if (alias is null && value is string inlineAlias && inlineAlias.Trim().Length > 0)
                        {
                            alias = new DiagramText(MermaidText.DecodePlain(inlineAlias.Trim()));
                        }

                        break;
                    default:
                        _context.Warn(idStart, $"The property '{key}' of a participant is not supported: it is ignored.");
                        break;
                }
            }
        }

        if (_participants.TryGetValue(id, out var participant))
        {
            if (isCreated)
                throw _scanner.Error(start, $"The participant '{id}' already exists: a participant is created once, before any use.");

            if (_box is not null && _boxOf.TryGetValue(participant, out var otherBox) && otherBox != _box)
                throw _scanner.Error(start, $"The participant '{id}' is already in another box.");

            if (alias is not null || configuration is not null || participant.Kind != kind)
            {
                participant.Label = alias ?? participant.Label;
                participant.Kind = kind;
            }
        }
        else
        {
            participant = AddParticipant(id, start);
            participant.Label = alias;
            participant.Kind = kind;
        }

        if (_box is not null && !_boxOf.ContainsKey(participant))
        {
            _boxOf[participant] = _box;
            _box.Participants.Add(participant);
        }

        if (isCreated)
        {
            if (_pendingCreate is not null)
                throw _scanner.Error(start, $"The created participant '{_pendingCreate.Id}' has no creating message after its declaration.");

            _pendingCreate = participant;
            _pendingOffset = start;
        }
    }

    private bool IsAs(int position)
    {
        var text = _scanner.Text;
        return position + 2 < text.Length && (text[position] is 'a' or 'A') && (text[position + 1] is 's' or 'S') && text[position + 2] is ' ' or '\t';
    }

    private bool TryConsumeAs()
    {
        if (!IsAs(_scanner.Position))
            return false;

        _scanner.Position += 2;
        return true;
    }

    private void ParseBox(int start)
    {
        var rest = ReadText().Trim();
        var box = new SequenceBox();
        string? title;

        // A color first, as a function (rgb, hsl...) or a word, then the title; otherwise, the whole text is the title
        var colorEnd = 0;
        if (rest.StartsWith("rgb", StringComparison.OrdinalIgnoreCase) || rest.StartsWith("hsl", StringComparison.OrdinalIgnoreCase))
        {
            var close = rest.IndexOf(')', StringComparison.Ordinal);
            colorEnd = close < 0 ? 0 : close + 1;
        }
        else
        {
            while (colorEnd < rest.Length && (char.IsAsciiLetterOrDigit(rest[colorEnd]) || rest[colorEnd] == '_'))
            {
                colorEnd++;
            }
        }

        var colorText = rest[..colorEnd];
        if (string.Equals(colorText, "transparent", StringComparison.OrdinalIgnoreCase))
        {
            title = rest[colorEnd..];
        }
        else if (colorEnd > 0 && SceneColor.TryParse(colorText, out var color))
        {
            box.Color = color;
            title = rest[colorEnd..];
        }
        else
        {
            title = rest;
        }

        title = MermaidText.DecodePlain(StripWrap(title.Trim())).Trim();
        box.Title = title.Length is 0 ? null : new DiagramText(title);
        _box = box;
        _boxOffset = start;
        _diagram.Boxes.Add(box);
    }

    private void ParseBlock(int start, SequenceBlockKind kind)
    {
        if (_blocks.Count >= _context.Options.MaxDepth)
            throw _scanner.Error(start, $"The blocks are nested deeper than {_context.Options.MaxDepth} levels.");

        var block = new SequenceBlock(kind);
        var label = ReadLabel();
        if (kind is SequenceBlockKind.Rectangle)
        {
            if (label is not null)
            {
                if (SceneColor.TryParse(label.Text, out var color))
                {
                    block.Color = color;
                }
                else
                {
                    _context.Warn(start, $"'{label.Text}' is not a color: the default background is used.");
                }
            }

            block.Sections.Add(new SequenceBlockSection());
        }
        else
        {
            block.Sections.Add(new SequenceBlockSection(label));
        }

        AddStep(block, start);
        _blocks.Push((block, start));
    }

    private void ParseNote(int start)
    {
        SkipSpaces();
        SequenceNotePosition position;
        if (TryConsumeWords("left of"))
        {
            position = SequenceNotePosition.LeftOf;
        }
        else if (TryConsumeWords("right of"))
        {
            position = SequenceNotePosition.RightOf;
        }
        else if (TryConsumeKeyword("over"))
        {
            position = SequenceNotePosition.Over;
        }
        else
        {
            throw _scanner.Error("'left of', 'right of' or 'over' is expected.");
        }

        SkipSpaces();
        var note = new SequenceNote(new DiagramText(string.Empty), position);
        note.Participants.Add(GetParticipant(ReadName(), start));
        SkipSpaces();
        if (position is SequenceNotePosition.Over && _scanner.TryConsume(","))
        {
            SkipSpaces();
            var second = GetParticipant(ReadName(), start);
            if (second != note.Participants[0])
            {
                note.Participants.Add(second);
            }

            SkipSpaces();
        }

        if (_scanner.Current != ':')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': ':' and the text of the note are expected.");

        _scanner.Position++;
        note.Text = DecodeText(ReadText());
        AddStep(note, start);
    }

    private void ParseAutonumber(int start)
    {
        SkipSpaces();
        var autonumber = new SequenceAutonumber();
        if (TryConsumeKeyword("off"))
        {
            autonumber.IsEnabled = false;
        }
        else if (TryReadNumber(out var number))
        {
            autonumber.Start = number;
            SkipSpaces();
            if (TryReadNumber(out var increment))
            {
                autonumber.Increment = increment;
            }
        }

        EndStatement();
        AddStep(autonumber, start);
    }

    /// <summary>Reads a number with two decimals at most, followed by a space or the end of the statement.</summary>
    private bool TryReadNumber(out decimal value)
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var end = start;
        while (end < text.Length && char.IsAsciiDigit(text[end]) && end - start < 18)
        {
            end++;
        }

        if (end < text.Length && text[end] == '.')
        {
            var fraction = end + 1;
            while (fraction < text.Length && char.IsAsciiDigit(text[fraction]) && fraction - end <= 2)
            {
                fraction++;
            }

            if (fraction > end + 1)
            {
                end = fraction;
            }
        }

        if (end == start || (end < text.Length && text[end] is not (' ' or '\t' or '\n' or ';')) || !decimal.TryParse(text.AsSpan(start, end - start), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out value))
        {
            value = 0;
            return false;
        }

        _scanner.Position = end;
        return true;
    }

    /// <summary>Parses a message: <c>A-&gt;&gt;+B: text</c>, <c>A()-&gt;&gt;()B: text</c>.</summary>
    private void ParseMessage(int start)
    {
        var from = GetParticipant(ReadName(), start);
        SkipSpaces();
        var senderConnection = TryConsumeConnection();
        var arrowStart = _scanner.Position;
        var arrow = Array.FindIndex(Arrows, item => _scanner.StartsWith(item.Text));
        if (arrow < 0)
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an arrow such as '->>' is expected.");

        _scanner.Position += Arrows[arrow].Text.Length;
        SkipSpaces();
        var activates = false;
        var deactivates = false;
        var receiverConnection = TryConsumeConnection();
        if (!receiverConnection && !senderConnection)
        {
            if (_scanner.TryConsume("+"))
            {
                activates = true;
            }
            else if (_scanner.Current == '-' && Array.FindIndex(Arrows, item => _scanner.StartsWith(item.Text)) < 0)
            {
                _scanner.Position++;
                deactivates = true;
            }
        }

        SkipSpaces();
        var to = GetParticipant(ReadName(), arrowStart);
        SkipSpaces();
        if (_scanner.Current != ':')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': ':' and the text of the message are expected.");

        _scanner.Position++;
        var text = DecodeText(ReadText());
        var message = new SequenceMessage(from, to)
        {
            Text = text.IsEmpty ? null : text,
            LineStyle = Arrows[arrow].LineStyle,
            StartArrowHead = Arrows[arrow].Start,
            EndArrowHead = Arrows[arrow].End,
            ActivatesReceiver = activates,
            DeactivatesSender = deactivates,
            SenderConnection = senderConnection,
            ReceiverConnection = receiverConnection,
        };

        // As in Mermaid, a created participant is the receiver of the next message, a destroyed one an end of it
        if (_pendingCreate is not null)
        {
            if (to != _pendingCreate)
                throw _scanner.Error(start, $"The created participant '{_pendingCreate.Id}' has no creating message after its declaration.");

            message.CreatesReceiver = true;
            _pendingCreate = null;
        }
        else if (_pendingDestroy is not null)
        {
            if (to != _pendingDestroy && from != _pendingDestroy)
                throw _scanner.Error(start, $"The destroyed participant '{_pendingDestroy.Id}' has no destroying message after its declaration.");

            message.DestroyedParticipant = _pendingDestroy;
            _pendingDestroy = null;
        }

        if (activates)
        {
            _activations[to] = _activations.GetValueOrDefault(to) + 1;
        }

        if (deactivates)
        {
            Deactivate(from, start);
        }

        AddStep(message, start);
    }

    private bool TryConsumeConnection()
    {
        if (!_scanner.StartsWith("()"))
            return false;

        _scanner.Position += 2;
        SkipSpaces();
        return true;
    }

    private void Deactivate(SequenceParticipant participant, int offset)
    {
        var count = _activations.GetValueOrDefault(participant);
        if (count < 1)
            throw _scanner.Error(offset, $"The participant '{participant.Id}' is not active: it cannot be deactivated.");

        _activations[participant] = count - 1;
    }

    private void AddStep(SequenceStep step, int offset)
    {
        if (++_stepCount > _context.Options.MaxEdges * 4)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxEdges * 4} steps.");

        CurrentSteps.Add(step);
    }

    private SequenceParticipant GetParticipant(string id, int offset)
    {
        if (_participants.TryGetValue(id, out var participant))
            return participant;

        ValidateId(id, offset);
        return AddParticipant(id, offset);
    }

    private SequenceParticipant AddParticipant(string id, int offset)
    {
        if (_participants.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} participants.");

        var participant = new SequenceParticipant(id);
        _participants[id] = participant;
        _diagram.Participants.Add(participant);
        return participant;
    }

    private void ValidateId(string id, int offset)
    {
        if (!IsValidId(id, allowSpaces: true))
            throw _scanner.Error(offset, $"'{id}' is not a valid participant: it is empty, starts with a keyword or a number, or contains a reserved character.");
    }

    /// <summary>Indicates whether an identifier reads back as one participant, in a message and in a declaration.</summary>
    internal static bool IsValidId(string id, bool allowSpaces)
    {
        if (id.Length is 0 || id.Trim().Length != id.Length || id.Contains("%%", StringComparison.Ordinal))
            return false;

        foreach (var c in id)
        {
            if (c is '<' or '>' or ':' or ',' or ';' or '@' or '#' or '+' or '(' or ')' or '/' or '\\' or '"' or '\n' or '\r' || char.IsControl(c) || (char.IsWhiteSpace(c) && (!allowSpaces || c is not ' ')))
                return false;
        }

        if (id[0] == '-' || id[^1] == '-' || id.Contains("--", StringComparison.Ordinal) || id.Contains("-x", StringComparison.Ordinal) || id.Contains("-|", StringComparison.Ordinal) || id.Contains("-.", StringComparison.Ordinal))
            return false;

        var words = id.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (Keywords.Contains(words[0]) || (words.Length > 1 && string.Equals(words[1], "of", StringComparison.OrdinalIgnoreCase) && words[0] is "left" or "right" or "Left" or "Right"))
            return false;

        // A number followed by a space is a number for Mermaid; "as" separates an alias in a declaration
        if (words.Length > 1 && words[0].All(c => char.IsAsciiDigit(c) || c == '.'))
            return false;

        return !words.Skip(1).Any(word => string.Equals(word, "as", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Reads the name of a participant in a message or a note: characters up to an arrow, a colon, a comma or the end of the statement.</summary>
    private string ReadName()
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var i = start;
        while (i < text.Length && text[i] is not ('\n' or ';' or ':' or ',' or '+' or '#') && !_scanner.IsCommentAt(i) && !(text[i] == '(' && i + 1 < text.Length && text[i + 1] == ')'))
        {
            var position = i;
            if (Array.Exists(Arrows, item => string.CompareOrdinal(text, position, item.Text, 0, item.Text.Length) == 0))
                break;

            i++;
        }

        _scanner.Position = i;
        var name = text[start..i].Trim();
        if (name.Length is 0)
            throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': a participant is expected.");

        return name;
    }

    /// <summary>Reads the identifier of a declaration, <c>destroy</c> or <c>activate</c>: the rest of the statement.</summary>
    private string ReadDeclaredId()
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var i = start;
        while (i < text.Length && text[i] is not ('\n' or ';' or '#') && !_scanner.IsCommentAt(i))
        {
            i++;
        }

        _scanner.Position = i;
        var id = text[start..i].Trim();
        if (id.Length is 0)
            throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': a participant is expected.");

        return id;
    }

    /// <summary>Reads a label up to the end of the statement, without its wrap prefix: <see langword="null"/> when it is empty.</summary>
    private DiagramText? ReadLabel()
    {
        var text = DecodeText(ReadText());
        return text.IsEmpty ? null : text;
    }

    private static DiagramText DecodeText(string raw) => new(MermaidText.DecodePlain(StripWrap(raw.Trim())).Trim());

    /// <summary>Removes the <c>wrap:</c> and <c>nowrap:</c> prefixes, which set the wrapping of a text in Mermaid.</summary>
    private static string StripWrap(string text)
    {
        foreach (var prefix in new[] { ":wrap:", ":nowrap:", "wrap:", "nowrap:" })
        {
            if (text.StartsWith(prefix, StringComparison.Ordinal))
                return text[prefix.Length..];
        }

        return text;
    }

    /// <summary>Reads a text up to a line feed, a semicolon, a <c>#</c> comment or a <c>%%</c> comment; an entity code such as <c>#59;</c> is part of the text.</summary>
    private string ReadText()
    {
        var text = _scanner.Text;
        var start = _scanner.Position;
        var i = start;
        while (i < text.Length && text[i] is not ('\n' or ';') && !_scanner.IsCommentAt(i))
        {
            if (text[i] == '#')
            {
                var entityEnd = EntityEnd(text, i);
                if (entityEnd < 0)
                    break;

                i = entityEnd;
                continue;
            }

            i++;
        }

        _scanner.Position = i;

        // A # comment goes up to the end of the line
        if (i < text.Length && text[i] == '#')
        {
            _scanner.ReadLine();
        }

        return text[start..i];
    }

    /// <summary>Returns the position after the entity code at a position, such as <c>#59;</c>, or -1 when there is none.</summary>
    private static int EntityEnd(string text, int index)
    {
        var end = index + 1;
        while (end < text.Length && end - index < 40 && char.IsAsciiLetterOrDigit(text[end]))
        {
            end++;
        }

        return end > index + 1 && end < text.Length && text[end] == ';' ? end + 1 : -1;
    }

    private void EndStatement()
    {
        SkipSpaces();
        if (_scanner.Current == '#')
        {
            _scanner.ReadLine();
        }

        if (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';') && !_scanner.IsCommentAt(_scanner.Position))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }

    private void SkipSpaces() => _scanner.SkipSpaces();

    private bool StartsWithKeyword(string keyword)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + keyword.Length > text.Length || string.Compare(text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) != 0)
            return false;

        var end = position + keyword.Length;
        return end >= text.Length || !(char.IsAsciiLetterOrDigit(text[end]) || text[end] == '_');
    }

    private bool TryConsumeKeyword(string keyword)
    {
        if (!StartsWithKeyword(keyword))
            return false;

        _scanner.Position += keyword.Length;
        return true;
    }

    private bool TryConsumeWords(string words)
    {
        var parts = words.Split(' ');
        var save = _scanner.Position;
        foreach (var part in parts)
        {
            SkipSpaces();
            if (!TryConsumeKeyword(part))
            {
                _scanner.Position = save;
                return false;
            }
        }

        return true;
    }
}
