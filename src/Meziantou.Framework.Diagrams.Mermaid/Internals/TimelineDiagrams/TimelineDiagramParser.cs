namespace Meziantou.Framework.Diagrams.Mermaid.Internals.TimelineDiagrams;

/// <summary>
/// Parses the <c>timeline</c> diagrams of Mermaid: <c>title</c>, <c>section</c>, and the periods followed by their events,
/// each introduced by <c>": "</c>, on the line of the period or on the next lines. <c>#</c> starts a comment outside the events.
/// </summary>
internal sealed class TimelineDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly TimelineDiagram _diagram = new();
    private TimelineSection? _section;
    private TimelinePeriod? _period;
    private int _count;

    private TimelineDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static TimelineDiagram Parse(ParseContext context, int position) => new TimelineDiagramParser(context, position).Parse();

    private TimelineDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("timeline"))
            throw _scanner.Error(keywordStart, "'timeline' is expected.");

        _scanner.SkipSpaces();
        if (TryWord("LR"))
        {
            _diagram.Direction = TimelineDirection.LeftToRight;
        }
        else if (TryWord("TD"))
        {
            _diagram.Direction = TimelineDirection.TopToBottom;
        }

        ExpectLineEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "timeline");
        if (_context.Configuration.GetBoolean("timeline", "disableMulticolor") is true)
        {
            _diagram.DisableMulticolor = true;
        }

        while (true)
        {
            SkipBlankLines();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (_scanner.Current == ':')
        {
            if (_period is null)
                throw _scanner.Error("An event follows a period.");

            ParseEvents();
            return;
        }

        if (StartsWithIgnoreCase("timeline"))
            throw _scanner.Error("The diagram has one 'timeline' keyword, on its first line.");

        // Mermaid reads these keywords ignoring the case
        if (StartsWithIgnoreCase("accTitle") || StartsWithIgnoreCase("accDescr"))
            throw _scanner.Error("'accTitle' is followed by a colon, and 'accDescr' by a colon or a brace.");

        if (TryKeyword("title"))
        {
            var title = _scanner.ReadRestOfLine().Trim();
            _diagram.Title = title.Length is 0 ? null : title;
            ExpectLineEnd();
            return;
        }

        if (TryKeyword("section"))
        {
            var titleStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ':') && !_scanner.IsCommentAt(_scanner.Position))
            {
                _scanner.Position++;
            }

            var raw = _scanner.Text[titleStart.._scanner.Position].Trim();
            if (raw.Length is 0)
                throw _scanner.Error(start, "The title of the section is expected.");

            ExpectLineEnd();
            _section = _diagram.AddSection(new DiagramText(MermaidText.DecodePlain(raw)));
            _period = null;
            return;
        }

        // A period: up to a colon, a '#' comment or the end of the line
        while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ':' or '#') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        var label = _scanner.Text[start.._scanner.Position].Trim();
        if (++_count > _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} periods and events.");

        _period = new TimelinePeriod(new DiagramText(MermaidText.DecodePlain(label)));
        (_section?.Periods ?? _diagram.Periods).Add(_period);
        if (_scanner.Current == '#')
        {
            SkipToLineEnd();
            return;
        }

        if (_scanner.Current == ':')
        {
            ParseEvents();
            return;
        }

        ExpectLineEnd();
    }

    /// <summary>Reads the events of the line: each starts with a colon and white space, and ends at the next colon followed by white space.</summary>
    private void ParseEvents()
    {
        while (_scanner.Current == ':')
        {
            var start = _scanner.Position;
            _scanner.Position++;
            if (_scanner.Current is not (' ' or '\t'))
                throw _scanner.Error(start, "An event starts with a colon and a space: ': event'.");

            var textStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current != '\n' && !_scanner.IsCommentAt(_scanner.Position)
                && !(_scanner.Current == ':' && (_scanner.Position + 1 >= _scanner.Text.Length || char.IsWhiteSpace(_scanner.Text[_scanner.Position + 1]))))
            {
                _scanner.Position++;
            }

            var raw = _scanner.Text[textStart.._scanner.Position].Trim();
            if (raw.Length is 0)
                throw _scanner.Error(start, "The text of the event is expected.");

            if (++_count > _context.Options.MaxNodes)
                throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} periods and events.");

            _period!.Events.Add(new DiagramText(MermaidText.DecodePlain(raw)));
        }

        ExpectLineEnd();
    }

    private bool StartsWithIgnoreCase(string keyword)
    {
        var position = _scanner.Position;
        return position + keyword.Length <= _scanner.Text.Length && string.Compare(_scanner.Text, position, keyword, 0, keyword.Length, StringComparison.OrdinalIgnoreCase) == 0;
    }

    /// <summary>Consumes a keyword, ignoring the case, followed by white space.</summary>
    private bool TryKeyword(string keyword)
    {
        var text = _scanner.Text;
        var position = _scanner.Position;
        if (position + keyword.Length >= text.Length || !StartsWithIgnoreCase(keyword) || !IsKeywordSeparator(text[position + keyword.Length]))
            return false;

        _scanner.Position += keyword.Length;
        _scanner.SkipSpaces();
        return true;
    }

    /// <summary>Indicates whether a character ends a keyword: white space other than a line feed, as the <c>\s</c> of Mermaid.</summary>
    internal static bool IsKeywordSeparator(char c) => c != '\n' && char.IsWhiteSpace(c);

    /// <summary>Consumes a word, ignoring the case, followed by the end of the line.</summary>
    private bool TryWord(string word)
    {
        var end = _scanner.Position + word.Length;
        if (!StartsWithIgnoreCase(word) || (end < _scanner.Text.Length && !char.IsWhiteSpace(_scanner.Text[end]) && !_scanner.IsCommentAt(end)))
            return false;

        _scanner.Position = end;
        return true;
    }

    private void SkipToLineEnd()
    {
        while (!_scanner.IsEnd && _scanner.Current != '\n')
        {
            _scanner.Position++;
        }
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current == '#')
        {
            SkipToLineEnd();
        }

        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a statement is alone on its line.");
    }

    private void SkipBlankLines()
    {
        while (!_scanner.IsEnd)
        {
            if (char.IsWhiteSpace(_scanner.Current))
            {
                _scanner.Position++;
            }
            else if (_scanner.Current == '#' || _scanner.IsCommentAt(_scanner.Position))
            {
                SkipToLineEnd();
            }
            else
            {
                break;
            }
        }
    }
}
