using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.UserJourneyDiagrams;

/// <summary>
/// Parses the <c>journey</c> diagrams of Mermaid: a statement per line (or between semicolons), <c>#</c> and <c>%%</c>
/// start comments, and a task is <c>name: score: actor, actor</c>.
/// </summary>
internal sealed class UserJourneyDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly UserJourneyDiagram _diagram = new();
    private UserJourneySection? _section;
    private int _taskCount;

    private UserJourneyDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static UserJourneyDiagram Parse(ParseContext context, int position) => new UserJourneyDiagramParser(context, position).Parse();

    private UserJourneyDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("journey"))
            throw _scanner.Error(keywordStart, "'journey' is expected.");

        ExpectStatementEnd();
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "journey");
        while (true)
        {
            SkipSeparators();
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
        if (StartsWithWord("title"))
        {
            _scanner.Position += "title".Length;
            var title = ReadText().Trim();
            _diagram.Title = title.Length is 0 ? null : title;
            ExpectStatementEnd();
            return;
        }

        if (StartsWithWord("section"))
        {
            _scanner.Position += "section".Length;
            var name = ReadText(stopAtColon: true).Trim();
            if (name.Length is 0)
                throw _scanner.Error(start, "The name of the section is expected.");

            if (_scanner.Current == ':')
                throw _scanner.Error("The name of a section cannot contain ':'.");

            _section = _diagram.AddSection(name);
            ExpectStatementEnd();
            return;
        }

        if (string.Compare(_scanner.Text, _scanner.Position, "journey", 0, 7, StringComparison.OrdinalIgnoreCase) == 0)
            throw _scanner.Error("'journey' starts the diagram and cannot start a task.");

        // A task: name: score: actors
        var taskName = ReadText(stopAtColon: true).Trim();
        if (_scanner.Current != ':')
            throw _scanner.Error(start, "A task is written 'name: score: actor, actor'.");

        _scanner.Position++;
        var dataStart = _scanner.Position;
        var data = ReadText();
        if (data.Length is 0)
            throw _scanner.Error(dataStart, "The score of the task is expected.");

        ExpectStatementEnd();
        if (taskName.Length is 0)
            throw _scanner.Error(start, "The name of the task is expected.");

        var pieces = data.Split(':');
        var scoreText = pieces[0].Trim();
        double score = 0;
        if (scoreText.Length > 0 && (!double.TryParse(scoreText, NumberStyles.Float, CultureInfo.InvariantCulture, out score) || !double.IsFinite(score)))
            throw _scanner.Error(dataStart, $"The score '{scoreText}' is not a number.");

        if (pieces.Length > 2)
        {
            _context.Warn(dataStart, "The text after the actors of the task is ignored.");
        }

        if (++_taskCount > _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} tasks.");

        // The tasks before the first section are in a section without name
        _section ??= _diagram.AddSection(string.Empty);
        var task = new UserJourneyTask(taskName, score);
        if (pieces.Length > 1)
        {
            foreach (var actor in pieces[1].Split(','))
            {
                var name = actor.Trim();
                if (name.Length > 0)
                {
                    task.Actors.Add(name);
                }
            }
        }

        _section.Tasks.Add(task);
    }

    /// <summary>Reads up to the end of the statement: a line feed, a semicolon, a <c>#</c> or a <c>%%</c> comment, or a colon.</summary>
    private string ReadText(bool stopAtColon = false)
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';' or '#') && !(stopAtColon && _scanner.Current == ':') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    /// <summary>Indicates whether a keyword starts here, ignoring the case, followed by a space or a tab.</summary>
    private bool StartsWithWord(string word)
    {
        var position = _scanner.Position;
        var text = _scanner.Text;
        return position + word.Length < text.Length && string.Compare(text, position, word, 0, word.Length, StringComparison.OrdinalIgnoreCase) == 0 && text[position + word.Length] is ' ' or '\t';
    }

    private void ExpectStatementEnd()
    {
        _scanner.SkipSpaces();
        if (_scanner.Current == '#')
        {
            SkipComment();
        }

        if (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';'))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }

    /// <summary>Skips a <c>#</c> or <c>%%</c> comment, up to the end of the line.</summary>
    private void SkipComment()
    {
        while (!_scanner.IsEnd && _scanner.Current != '\n')
        {
            _scanner.Position++;
        }
    }

    private void SkipSeparators()
    {
        while (!_scanner.IsEnd)
        {
            if (char.IsWhiteSpace(_scanner.Current) || _scanner.Current == ';')
            {
                _scanner.Position++;
            }
            else if (_scanner.Current == '#' || _scanner.IsCommentAt(_scanner.Position))
            {
                SkipComment();
            }
            else
            {
                return;
            }
        }
    }
}
