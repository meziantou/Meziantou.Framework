namespace Meziantou.Framework.Diagrams.Mermaid.Internals.KanbanDiagrams;

/// <summary>
/// Parses the <c>kanban</c> diagrams of Mermaid. The indentation makes the structure: the items as indented as the
/// first one are the columns, and the more indented ones are the cards of the column above them.
/// </summary>
internal sealed class KanbanDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly KanbanDiagram _diagram = new();
    private int? _columnLevel;
    private object? _last;
    private int _count;

    private KanbanDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static KanbanDiagram Parse(ParseContext context, int position) => new KanbanDiagramParser(context, position).Parse();

    private KanbanDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (string.Compare(_scanner.Text, keywordStart, "kanban", 0, 6, StringComparison.OrdinalIgnoreCase) != 0)
            throw _scanner.Error(keywordStart, "'kanban' is expected.");

        _scanner.Position += 6;
        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "kanban");
        if (_context.Configuration.GetString("kanban", "ticketBaseUrl") is { Length: > 0 } ticketBaseUrl)
        {
            _diagram.TicketLinkFormat = MermaidCommon.SanitizeUrl(ticketBaseUrl, _context, 0);
        }

        if (_context.Configuration.GetNumber("kanban", "sectionWidth") is >= 50 and <= 2000 and var width)
        {
            _diagram.ColumnWidth = width;
        }

        while (!_scanner.IsEnd)
        {
            if (_scanner.Current == '\n')
            {
                _scanner.Position++;
                continue;
            }

            ParseLine();
        }

        return _diagram;
    }

    private void ParseLine()
    {
        var lineStart = _scanner.Position;
        while (!_scanner.IsEnd && _scanner.Current is ' ' or '\t')
        {
            _scanner.Position++;
        }

        var level = _scanner.Position - lineStart;
        if (_scanner.IsEnd || _scanner.Current == '\n' || _scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.SkipSpaces();
            return;
        }

        if (TryParseDecoration())
        {
            ExpectLineEnd();
            return;
        }

        var start = _scanner.Position;
        var (id, label) = ParseNode();
        string? assigned = null;
        string? ticket = null;
        KanbanPriority? priority = null;
        _scanner.SkipSpaces();
        if (_scanner.StartsWith("@{"))
        {
            var metadataStart = _scanner.Position;
            _scanner.Position++;
            foreach (var (key, value) in MermaidCommon.ReadInlineMapping(_scanner))
            {
                var text = value switch
                {
                    null => null,
                    string item => item,
                    bool item => item ? "true" : "false",
                    double item => item.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.ToString(),
                };
                if (string.IsNullOrEmpty(text))
                    continue;

                switch (key)
                {
                    case "assigned":
                        assigned = text;
                        break;
                    case "ticket":
                        ticket = text;
                        break;
                    case "priority":
                        priority = text switch
                        {
                            "Very High" => KanbanPriority.VeryHigh,
                            "High" => KanbanPriority.High,
                            "Medium" => KanbanPriority.Medium,
                            "Low" => KanbanPriority.Low,
                            "Very Low" => KanbanPriority.VeryLow,
                            _ => null,
                        };
                        if (priority is null)
                        {
                            _context.Warn(metadataStart, $"The priority '{text}' is not supported: use 'Very High', 'High', 'Medium', 'Low' or 'Very Low'.");
                        }

                        break;
                    case "label":
                        label = MermaidText.DecodePlain(text);
                        break;
                    case "icon":
                        _context.Warn(metadataStart, "The icons of the cards are not supported: they are ignored.");
                        break;
                    case "shape":
                        if (text != text.ToLowerInvariant() || text.Contains('_', StringComparison.Ordinal))
                            throw _scanner.Error(metadataStart, $"No such shape: {text}. Shape names should be lowercase.");

                        break;
                    default:
                        _context.Warn(metadataStart, $"The metadata '{key}' is not supported: it is ignored.");
                        break;
                }
            }

            _scanner.SkipSpaces();
        }

        ExpectLineEnd();
        if (++_count > _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} items.");

        _columnLevel ??= level;
        if (level < _columnLevel)
            throw _scanner.Error(start, $"Items without column detected: '{label ?? id}' is less indented than the columns.");

        var text1 = label is null || label == id ? null : DiagramText.Markdown(label);
        if (level == _columnLevel)
        {
            _last = _diagram.AddColumn(id, text1);
        }
        else
        {
            var card = _diagram.Columns[^1].AddCard(id, text1);
            card.Assigned = assigned;
            card.Ticket = ticket;
            card.Priority = priority;
            _last = card;
        }
    }

    /// <summary>Reads a line that decorates the last item: <c>:::classes</c>, or an icon, which is ignored.</summary>
    private bool TryParseDecoration()
    {
        var start = _scanner.Position;
        if (_scanner.TryConsume(":::"))
        {
            var classes = ReadClasses();
            switch (_last)
            {
                case KanbanColumn column:
                    column.CssClasses.Clear();
                    AddClasses(column.CssClasses, classes);
                    break;
                case KanbanCard card:
                    card.CssClasses.Clear();
                    AddClasses(card.CssClasses, classes);
                    break;
                default:
                    throw _scanner.Error(start, "The classes decorate no item.");
            }

            return true;
        }

        if (_scanner.TryConsume("::icon("))
        {
            var end = _scanner.Text.IndexOfOrdinal(')', _scanner.Position);
            var lineEnd = _scanner.Text.IndexOfOrdinal('\n', _scanner.Position);
            if (end < 0 || (lineEnd >= 0 && lineEnd < end))
                throw _scanner.Error(start, "The icon is not closed by ')'.");

            if (_last is null)
                throw _scanner.Error(start, "The icon decorates no item.");

            _scanner.Position = end + 1;
            _context.Warn(start, "The icons of the items are not supported: they are ignored.");
            return true;
        }

        return false;
    }

    private static void AddClasses(IList<string> target, List<string> classes)
    {
        foreach (var name in classes)
        {
            target.Add(name);
        }
    }

    private List<string> ReadClasses()
    {
        var start = _scanner.Position;
        var text = _scanner.ReadRestOfLine();
        var classes = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).ToList();
        if (classes.Count is 0)
            throw _scanner.Error(start, "A class name is expected after ':::'.");

        return classes;
    }

    /// <summary>Reads an item: <c>id</c>, <c>id[text]</c>, <c>[text]</c>, with the delimiters of the shapes of the mind maps, which a kanban draws the same.</summary>
    private (string Id, string? Label) ParseNode()
    {
        var start = _scanner.Position;
        var idStart = _scanner.Position;
        while (!_scanner.IsEnd && IsIdCharacter(_scanner.Current) && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        var id = _scanner.Text[idStart.._scanner.Position].TrimEnd();

        // Mermaid reads 'kanban' as the keyword wherever a token starts
        if (id.StartsWith("kanban", StringComparison.OrdinalIgnoreCase))
            throw _scanner.Error(start, $"'{id}' cannot be an item: the identifiers cannot start with 'kanban'.");

        if (_scanner.IsEnd || _scanner.Current is '\n' or '@' || _scanner.IsCommentAt(_scanner.Position))
        {
            if (id.Length is 0)
                throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': an item is expected.");

            return (id, null);
        }

        // The shape: (( {{ ( [ ) )) (- -), which the kanban ignores
        var openStart = _scanner.Position;
        if (!(_scanner.TryConsume("((") || _scanner.TryConsume("{{") || _scanner.TryConsume("(-") || _scanner.TryConsume("-)") || _scanner.TryConsume("))") || _scanner.TryConsume("(") || _scanner.TryConsume("[") || _scanner.TryConsume(")")))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");

        string label;
        if (_scanner.StartsWith("\"`"))
        {
            var close = _scanner.Text.IndexOf("`\"", _scanner.Position + 2, StringComparison.Ordinal);
            if (close < 0)
                throw _scanner.Error(openStart, "The markdown text is not closed by '`\"'.");

            label = _scanner.Text[(_scanner.Position + 2)..close];
            if (label.Length is 0 || label.Contains('"', StringComparison.Ordinal))
                throw _scanner.Error(openStart, "A markdown text cannot be empty nor contain '\"'.");

            _scanner.Position = close + 2;
            label = MermaidText.DecodeEntities(string.Join('\n', label.Split('\n').Select(line => line.Trim())));
        }
        else if (_scanner.Current == '"')
        {
            var quoteStart = _scanner.Position;
            var text = _scanner.ReadQuoted();
            if (text.Length is 0)
                throw _scanner.Error(quoteStart, "The text cannot be empty.");

            label = MermaidText.DecodePlain(text);
        }
        else
        {
            var textStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current is not (')' or ']' or '(' or '}' or '\n'))
            {
                _scanner.Position++;
            }

            label = MermaidText.DecodePlain(_scanner.Text[textStart.._scanner.Position].Trim());
            if (label.Length is 0)
                throw _scanner.Error(textStart, "The text cannot be empty.");
        }

        if (!(_scanner.TryConsume("))") || _scanner.TryConsume("}}") || _scanner.TryConsume("(-") || _scanner.TryConsume("-)") || _scanner.TryConsume("((") || _scanner.TryConsume(")") || _scanner.TryConsume("]") || _scanner.TryConsume("(")))
            throw _scanner.Error(openStart, "The item is not closed.");

        return (id.Length is 0 ? label : id, label);
    }

    internal static bool IsIdCharacter(char c) => c is not ('(' or '[' or '\n' or ')' or '{' or '}' or '@');

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }
}
