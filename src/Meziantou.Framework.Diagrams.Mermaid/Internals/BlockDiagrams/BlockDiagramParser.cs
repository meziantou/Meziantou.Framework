using System.Globalization;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.BlockDiagrams;

/// <summary>
/// Parses the <c>block</c> and <c>block-beta</c> diagrams of Mermaid. The statements are separated by white space, so a
/// line can hold several blocks; a block takes its place where it is first named, in the group being read.
/// </summary>
internal sealed class BlockDiagramParser
{
    // The delimiters of the shapes, the longest first
    internal static readonly (string Open, string Close, FlowchartNodeShape Shape)[] ShapeDelimiters =
    [
        ("(((", ")))", FlowchartNodeShape.DoubleCircle),
        ("((", "))", FlowchartNodeShape.Circle),
        ("([", "])", FlowchartNodeShape.Stadium),
        ("[[", "]]", FlowchartNodeShape.FramedRectangle),
        ("[(", ")]", FlowchartNodeShape.Cylinder),
        ("{{", "}}", FlowchartNodeShape.Hexagon),
        ("[/", "/]", FlowchartNodeShape.LeanRight),
        ("[\\", "\\]", FlowchartNodeShape.LeanLeft),
        ("[/", "\\]", FlowchartNodeShape.TrapezoidBaseBottom),
        ("[\\", "/]", FlowchartNodeShape.TrapezoidBaseTop),
        ("(", ")", FlowchartNodeShape.RoundedRectangle),
        ("{", "}", FlowchartNodeShape.Diamond),
        (">", "]", FlowchartNodeShape.Odd),
        ("[", "]", FlowchartNodeShape.Rectangle),
    ];

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly BlockDiagram _diagram = new();
    private readonly Dictionary<string, BlockDiagramBlock> _blocks = new(StringComparer.Ordinal);
    private readonly Stack<BlockDiagramComposite> _scopes = new();
    private readonly List<(List<string> Ids, List<string> Classes, int Offset)> _classAssignments = [];
    private readonly List<(List<string> Ids, DiagramStyle Style, int Offset)> _styleAssignments = [];
    private int _anonymousCount;
    private int _itemCount;

    private BlockDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static BlockDiagram Parse(ParseContext context, int position) => new BlockDiagramParser(context, position).Parse();

    private IList<BlockDiagramItem> CurrentItems => _scopes.Count > 0 ? _scopes.Peek().Items : _diagram.Items;

    private BlockDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("block-beta") && !_scanner.TryConsume("block"))
            throw _scanner.Error(keywordStart, "'block' is expected.");

        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "block");
        while (true)
        {
            SkipWhiteSpace();
            if (_scanner.IsEnd)
                break;

            ParseStatement();
        }

        if (_scopes.Count > 0)
            throw _scanner.Error(_scanner.Text.Length, $"The composite block '{_scopes.Peek().Id}' is not closed by 'end'.");

        foreach (var (ids, classes, offset) in _classAssignments)
        {
            foreach (var id in ids)
            {
                if (_blocks.TryGetValue(id, out var block))
                {
                    foreach (var className in classes)
                    {
                        block.CssClasses.Add(className);
                    }
                }
                else
                {
                    _context.Warn(offset, $"The block '{id}' is not defined: its classes are ignored.");
                }
            }
        }

        foreach (var (ids, style, offset) in _styleAssignments)
        {
            foreach (var id in ids)
            {
                if (_blocks.TryGetValue(id, out var block))
                {
                    block.Style = block.Style is null ? style : block.Style.Merge(style);
                }
                else
                {
                    _context.Warn(offset, $"The block '{id}' is not defined: its style is ignored.");
                }
            }
        }

        return _diagram;
    }

    private void ParseStatement()
    {
        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram))
            return;

        var start = _scanner.Position;
        if (StartsWithWord("columns"))
        {
            _scanner.Position += "columns".Length;
            SkipWhiteSpace();
            if (StartsWithWord("auto"))
            {
                _scanner.Position += 4;
                SetColumns(null);
                return;
            }

            var digits = ReadDigits();
            if (digits.Length is 0 || !int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var columns) || columns < 1 || columns > 1000)
                throw _scanner.Error(start, "'columns' is followed by a number of columns from 1 to 1000, or 'auto'.");

            SetColumns(columns);
            return;
        }

        if (StartsWithWord("space") || _scanner.StartsWith("space:"))
        {
            _scanner.Position += "space".Length;
            var space = new BlockDiagramSpace { Span = ReadSpan(start) ?? 1 };
            AddItem(space, start);
            return;
        }

        if (_scanner.StartsWith("block:"))
        {
            _scanner.Position += "block:".Length;
            var idStart = _scanner.Position;
            var id = ReadId() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the identifier of the composite block is expected.");
            var (label, shape) = ReadShape();
            if (shape is not (null or FlowchartNodeShape.Rectangle))
            {
                _context.Warn(idStart, "The shape of a composite block is ignored: it is drawn as a frame.");
            }

            var span = ReadSpan(start);
            if (_blocks.ContainsKey(id))
                throw _scanner.Error(idStart, $"The block '{id}' is already defined.");

            OpenComposite(new BlockDiagramComposite(id, label) { Span = span ?? 1 }, start);
            return;
        }

        if (StartsWithWord("block"))
        {
            _scanner.Position += "block".Length;
            _anonymousCount++;
            OpenComposite(new BlockDiagramComposite("block-" + _anonymousCount.ToString(CultureInfo.InvariantCulture)), start);
            return;
        }

        if (StartsWithWord("end"))
        {
            if (_scopes.Count is 0)
                throw _scanner.Error("'end' closes no composite block.");

            _scanner.Position += "end".Length;
            _scopes.Pop();
            return;
        }

        if (StartsWithWord("classDef"))
        {
            _scanner.Position += "classDef".Length;
            SkipSpaces();
            var name = ReadWord();
            if (name.Length is 0)
                throw _scanner.Error(start, "The name of the class is expected.");

            var style = MermaidStyle.Parse(ReadCss(), _context, start);
            _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
            return;
        }

        if (StartsWithWord("class"))
        {
            _scanner.Position += "class".Length;
            SkipSpaces();
            var ids = ReadWordList();
            var classes = _scanner.ReadLine().Split([' ', '\t', ','], StringSplitOptions.RemoveEmptyEntries).ToList();
            if (ids.Count is 0 || classes.Count is 0)
                throw _scanner.Error(start, "The blocks and the class names are expected.");

            foreach (var className in classes)
            {
                if (!IsWord(className))
                    throw _scanner.Error(start, $"'{className}' is not a valid class name.");
            }

            _classAssignments.Add((ids, classes, start));
            return;
        }

        if (StartsWithWord("style"))
        {
            _scanner.Position += "style".Length;
            SkipSpaces();
            var ids = ReadWordList();
            if (ids.Count is 0)
                throw _scanner.Error(start, "The blocks are expected.");

            _styleAssignments.Add((ids, MermaidStyle.Parse(ReadCss(), _context, start), start));
            return;
        }

        ParseNodeStatement();
    }

    /// <summary>Parses a block, or a chain of edges: <c>a --&gt; b -- "label" --&gt; c</c>.</summary>
    private void ParseNodeStatement()
    {
        var from = ParseNode(allowSpan: true);
        while (true)
        {
            var save = _scanner.Position;
            SkipWhiteSpace();
            var linkStart = _scanner.Position;
            if (!TryReadLink(out var lineStyle, out var startHead, out var endHead, out var label))
            {
                _scanner.Position = save;
                return;
            }

            SkipWhiteSpace();
            var to = ParseNode(allowSpan: false);
            if (_diagram.Edges.Count >= _context.Options.MaxEdges)
                throw _scanner.Error(linkStart, $"The diagram has more than {_context.Options.MaxEdges} edges.");

            _diagram.Edges.Add(new BlockDiagramEdge(from, to) { LineStyle = lineStyle, StartArrowHead = startHead, EndArrowHead = endHead, Label = label });
            from = to;
        }
    }

    /// <summary>Parses a block: its identifier, its shape and its text, or a block arrow, and its span.</summary>
    private BlockDiagramBlock ParseNode(bool allowSpan)
    {
        var start = _scanner.Position;
        var id = ReadId() ?? throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a block is expected.");
        var save = _scanner.Position;
        SkipWhiteSpace();
        BlockDiagramBlock block;
        if (_scanner.StartsWith("<["))
        {
            _scanner.Position += 2;
            var label = ReadLabel(start);
            if (!_scanner.TryConsume("]>"))
                throw _scanner.Error("']>' is expected after the text of the block arrow.");

            SkipSpaces();
            _scanner.Expect("(");
            var directions = BlockArrowDirections.None;
            while (true)
            {
                SkipSpaces();
                var word = ReadDigitsOrLetters();
                directions |= word switch
                {
                    "right" => BlockArrowDirections.Right,
                    "left" => BlockArrowDirections.Left,
                    "up" => BlockArrowDirections.Up,
                    "down" => BlockArrowDirections.Down,
                    "x" => BlockArrowDirections.Left | BlockArrowDirections.Right,
                    "y" => BlockArrowDirections.Up | BlockArrowDirections.Down,
                    _ => throw _scanner.Error($"'{word}' is not a direction: use right, left, up, down, x or y."),
                };
                SkipSpaces();
                if (_scanner.TryConsume(")"))
                    break;

                _scanner.TryConsume(",");
            }

            block = Declare(id, start, () => new BlockDiagramArrow(id, label, directions), existing =>
            {
                if (existing is not BlockDiagramArrow arrow)
                    throw _scanner.Error(start, $"The block '{id}' is already defined as another kind of block.");

                arrow.Label = label;
                arrow.Directions = directions;
            });
        }
        else
        {
            _scanner.Position = save;
            var (label, shape) = ReadShape();
            block = Declare(id, start, () => new BlockDiagramBlock(id, label) { Shape = shape ?? FlowchartNodeShape.Rectangle }, existing =>
            {
                if (shape is null)
                    return;

                if (existing is BlockDiagramComposite or BlockDiagramArrow)
                    throw _scanner.Error(start, $"The block '{id}' is already defined as another kind of block.");

                existing.Label = label;
                existing.Shape = shape.Value;
            });
        }

        if (allowSpan)
        {
            var span = ReadSpan(start);
            if (span is not null)
            {
                block.Span = span.Value;
            }
        }

        return block;
    }

    /// <summary>Returns the block of an identifier; a new one takes its place in the current group.</summary>
    private BlockDiagramBlock Declare(string id, int offset, Func<BlockDiagramBlock> create, Action<BlockDiagramBlock> update)
    {
        if (_blocks.TryGetValue(id, out var existing))
        {
            update(existing);
            return existing;
        }

        var block = create();
        _blocks[id] = block;
        AddItem(block, offset);
        return block;
    }

    private void OpenComposite(BlockDiagramComposite composite, int offset)
    {
        if (_scopes.Count >= _context.Options.MaxDepth)
            throw _scanner.Error(offset, $"The composite blocks are nested deeper than {_context.Options.MaxDepth} levels.");

        _blocks[composite.Id] = composite;
        AddItem(composite, offset);
        _scopes.Push(composite);
    }

    private void AddItem(BlockDiagramItem item, int offset)
    {
        if (++_itemCount > _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} blocks.");

        CurrentItems.Add(item);
    }

    private void SetColumns(int? columns)
    {
        if (_scopes.Count > 0)
        {
            _scopes.Peek().Columns = columns;
        }
        else
        {
            _diagram.Columns = columns;
        }
    }

    /// <summary>Reads the shape and the text of a block: a quoted or a markdown text between delimiters, or nothing.</summary>
    private (DiagramText? Label, FlowchartNodeShape? Shape) ReadShape()
    {
        var save = _scanner.Position;
        SkipWhiteSpace();
        var start = _scanner.Position;
        foreach (var (open, _, _) in ShapeDelimiters)
        {
            if (!_scanner.StartsWith(open))
                continue;

            _scanner.Position += open.Length;
            var label = ReadLabel(start);
            foreach (var (candidateOpen, close, shape) in ShapeDelimiters)
            {
                if (candidateOpen == open && _scanner.TryConsume(close))
                    return (label, shape);
            }

            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the shape is not closed.");
        }

        _scanner.Position = save;
        return (null, null);
    }

    /// <summary>Reads a quoted text (<c>"text"</c>) or a markdown text (<c>"`text`"</c>), which cannot be empty.</summary>
    private DiagramText ReadLabel(int offset)
    {
        var start = _scanner.Position;
        if (_scanner.Current != '"')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the text of a block is quoted.");

        if (_scanner.Peek() == '`')
        {
            var close = _scanner.Text.IndexOf("`\"", start + 2, StringComparison.Ordinal);
            if (close < 0)
                throw _scanner.Error(start, "The markdown text is not closed by '`\"'.");

            var raw = _scanner.Text[(start + 2)..close];
            if (raw.Length is 0 || raw.Contains('"', StringComparison.Ordinal))
                throw _scanner.Error(start, "A markdown text cannot be empty nor contain '\"'.");

            _scanner.Position = close + 2;
            return DiagramText.Markdown(MermaidText.DecodeEntities(string.Join('\n', raw.Split('\n').Select(line => line.Trim()))));
        }

        var text = _scanner.ReadQuoted();
        if (text.Length is 0)
            throw _scanner.Error(offset, "The text of a block cannot be empty.");

        return new DiagramText(MermaidText.DecodePlain(text));
    }

    /// <summary>Reads a link: <c>--&gt;</c>, <c>---</c>, <c>==&gt;</c>, <c>-.-&gt;</c>, <c>~~~</c>, <c>&lt;--&gt;</c>... or a link with a text, <c>-- "text" --&gt;</c>.</summary>
    private bool TryReadLink(out FlowchartLineStyle lineStyle, out FlowchartArrowHead startHead, out FlowchartArrowHead endHead, out DiagramText? label)
    {
        var start = _scanner.Position;
        label = null;
        startHead = FlowchartArrowHead.None;
        endHead = FlowchartArrowHead.None;
        lineStyle = FlowchartLineStyle.Solid;
        if (_scanner.TryConsume("~~~"))
        {
            while (_scanner.Current == '~')
            {
                _scanner.Position++;
            }

            lineStyle = FlowchartLineStyle.Invisible;
            return true;
        }

        if (_scanner.Current == '<' && _scanner.Peek() is '-' or '=')
        {
            startHead = FlowchartArrowHead.Arrow;
            _scanner.Position++;
        }

        var bodyStart = _scanner.Position;
        if (TryReadLine(out lineStyle, out endHead))
            return true;

        // A link with a text: an opening line, the quoted text, and a closing line
        _scanner.Position = bodyStart;
        if (_scanner.TryConsume("--") || _scanner.TryConsume("==") || _scanner.TryConsume("-."))
        {
            SkipSpaces();
            if (_scanner.Current == '"')
            {
                var text = _scanner.ReadQuoted();
                label = text.Length is 0 ? null : new DiagramText(MermaidText.DecodePlain(text));
                SkipSpaces();
                if (TryReadLine(out lineStyle, out endHead))
                    return true;

                throw _scanner.Error($"Unexpected '{_scanner.Describe()}': the link is not closed.");
            }
        }

        _scanner.Position = start;
        return false;
    }

    private bool TryReadLine(out FlowchartLineStyle lineStyle, out FlowchartArrowHead endHead)
    {
        var start = _scanner.Position;
        lineStyle = FlowchartLineStyle.Solid;
        endHead = FlowchartArrowHead.None;
        var c = _scanner.Current;
        if (c is '-' && _scanner.Peek() == '-')
        {
            var count = CountRun('-');
            if (count >= 3 && !IsHeadChar(_scanner.Current))
            {
                return true;
            }

            if (count >= 2 && IsHeadChar(_scanner.Current))
            {
                endHead = ToHead(_scanner.Current);
                _scanner.Position++;
                return true;
            }
        }
        else if (c is '=' && _scanner.Peek() == '=')
        {
            lineStyle = FlowchartLineStyle.Thick;
            var count = CountRun('=');
            if (count >= 3 && !IsHeadChar(_scanner.Current))
                return true;

            if (count >= 2 && IsHeadChar(_scanner.Current))
            {
                endHead = ToHead(_scanner.Current);
                _scanner.Position++;
                return true;
            }
        }
        else if (c is '-' or '.')
        {
            // -.- -.-> .-> -..-
            lineStyle = FlowchartLineStyle.Dotted;
            _scanner.TryConsume("-");
            if (CountRun('.') > 0 && _scanner.TryConsume("-"))
            {
                if (IsHeadChar(_scanner.Current))
                {
                    endHead = ToHead(_scanner.Current);
                    _scanner.Position++;
                }

                return true;
            }
        }

        _scanner.Position = start;
        return false;

        static bool IsHeadChar(char c) => c is '>' or 'x' or 'o';

        static FlowchartArrowHead ToHead(char c) => c switch
        {
            'x' => FlowchartArrowHead.Cross,
            'o' => FlowchartArrowHead.Circle,
            _ => FlowchartArrowHead.Arrow,
        };
    }

    private int CountRun(char c)
    {
        var count = 0;
        while (_scanner.Current == c)
        {
            _scanner.Position++;
            count++;
        }

        return count;
    }

    private int? ReadSpan(int offset)
    {
        var save = _scanner.Position;
        SkipSpaces();
        if (_scanner.Current != ':' || !char.IsAsciiDigit(_scanner.Peek()))
        {
            _scanner.Position = save;
            return null;
        }

        _scanner.Position++;
        var digits = ReadDigits();
        if (!int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var span) || span < 1 || span > 1000)
            throw _scanner.Error(offset, "A block spans 1 to 1000 columns.");

        return span;
    }

    /// <summary>Reads an identifier: characters that are not white space nor <c>()[]{}&lt;&gt;:=-"%</c>, which is not a keyword.</summary>
    private string? ReadId()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && IsIdCharacter(_scanner.Current))
        {
            _scanner.Position++;
        }

        if (_scanner.Position == start)
            return null;

        var id = _scanner.Text[start.._scanner.Position];
        if (IsKeyword(id))
            throw _scanner.Error(start, $"'{id}' is a keyword and cannot be a block.");

        return id;
    }

    internal static bool IsIdCharacter(char c) => !char.IsWhiteSpace(c) && c is not ('(' or ')' or '[' or ']' or '{' or '}' or '<' or '>' or ':' or '=' or '-' or '"' or '%' or '`' or '~');

    internal static bool IsValidId(string id) => id.Length > 0 && id.All(IsIdCharacter) && !IsKeyword(id);

    internal static bool IsKeyword(string id) => id is "block" or "end" or "space" or "columns" or "classDef" or "class" or "style" or "default" or "linkStyle" or "interpolate" || id.StartsWith("accTitle", StringComparison.Ordinal) || id.StartsWith("accDescr", StringComparison.Ordinal);

    internal static bool IsWord(string value) => value.Length > 0 && value.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');

    private bool StartsWithWord(string word)
    {
        if (!_scanner.StartsWith(word))
            return false;

        var end = _scanner.Position + word.Length;
        return end >= _scanner.Text.Length || !IsIdCharacter(_scanner.Text[end]) || _scanner.Text[end] == '%';
    }

    private List<string> ReadWordList()
    {
        var result = new List<string>();
        while (true)
        {
            SkipSpaces();
            var start = _scanner.Position;
            while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
            {
                _scanner.Position++;
            }

            if (_scanner.Position == start)
                return result;

            result.Add(_scanner.Text[start.._scanner.Position]);
            SkipSpaces();
            if (!_scanner.TryConsume(","))
                return result;
        }
    }

    private string ReadWord()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    /// <summary>Reads the declarations of a style up to the end of the line, without the semicolon that may end it.</summary>
    private string ReadCss()
    {
        SkipSpaces();
        var css = _scanner.ReadLine().Trim();
        return css.EndsWith(';', StringComparison.Ordinal) ? css[..^1] : css;
    }

    private string ReadDigits()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiDigit(_scanner.Current) && _scanner.Position - start < 9)
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private string ReadDigitsOrLetters()
    {
        var start = _scanner.Position;
        while (!_scanner.IsEnd && char.IsAsciiLetterOrDigit(_scanner.Current))
        {
            _scanner.Position++;
        }

        return _scanner.Text[start.._scanner.Position];
    }

    private void SkipSpaces()
    {
        while (!_scanner.IsEnd && _scanner.Current is ' ' or '\t')
        {
            _scanner.Position++;
        }
    }

    /// <summary>Skips the white space, the line feeds and the <c>%%</c> comments.</summary>
    private void SkipWhiteSpace()
    {
        while (!_scanner.IsEnd)
        {
            if (char.IsWhiteSpace(_scanner.Current))
            {
                _scanner.Position++;
            }
            else if (_scanner.IsCommentAt(_scanner.Position))
            {
                _scanner.ReadLine();
            }
            else
            {
                return;
            }
        }
    }
}
