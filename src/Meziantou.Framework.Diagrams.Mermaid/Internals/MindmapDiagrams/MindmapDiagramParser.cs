namespace Meziantou.Framework.Diagrams.Mermaid.Internals.MindmapDiagrams;

/// <summary>
/// Parses the <c>mindmap</c> diagrams of Mermaid. The indentation gives the tree: a node is the child of the last node
/// that is less indented. <c>::icon(...)</c> and <c>:::classes</c> decorate the last node.
/// </summary>
internal sealed class MindmapDiagramParser
{
    // The delimiters that open a shape, the longest first, and those that close it
    private static readonly string[] Openers = ["(-", "-)", "((", "(", "))", ")", "{{", "["];
    private static readonly string[] Closers = ["))", ")", "]", "}}", "(-", "-)", "((", "("];

    private readonly ParseContext _context;
    private readonly string _text;
    private readonly MindmapDiagram _diagram = new();
    private readonly List<(int Level, int Depth, MindmapNode Node)> _nodes = [];
    private int _position;
    private int? _baseLevel;

    private MindmapDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _text = context.Text;
        _position = position;
    }

    public static MindmapDiagram Parse(ParseContext context, int position) => new MindmapDiagramParser(context, position).Parse();

    private MindmapDiagram Parse()
    {
        _position += "mindmap".Length;
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "mindmap");
        var configuration = _context.Configuration;
        if (configuration.GetString("mindmap", "colorBy")?.Trim() is "depth")
        {
            _diagram.ColorBy = MindmapColorMode.Depth;
        }

        if (configuration.GetNumber("mindmap", "padding") is >= 0 and <= 1000 and var padding)
        {
            _diagram.NodePadding = padding;
        }

        if (configuration.GetNumber("mindmap", "maxNodeWidth") is >= 1 and <= 10_000 and var maxNodeWidth)
        {
            _diagram.MaxNodeWidth = maxNodeWidth;
        }

        // The root can be on the line of the keyword
        var first = true;
        while (_position < _text.Length)
        {
            if (!first)
            {
                if (_text[_position] != '\n')
                    throw Error(_position, $"Unexpected '{Describe(_position)}': a statement is alone on its line.");

                _position++;
            }

            first = false;
            var lineStart = _position;
            while (_position < _text.Length && IsSpace(_text[_position]))
            {
                _position++;
            }

            if (_position >= _text.Length)
                break;

            if (_text[_position] == '\n' || IsComment(_position))
            {
                SkipToLineEnd();
                continue;
            }

            ParseStatement(level: _position - lineStart);

            // A comment can end the line
            while (_position < _text.Length && IsSpace(_text[_position]))
            {
                _position++;
            }

            if (IsComment(_position))
            {
                SkipToLineEnd();
            }
        }

        return _diagram;
    }

    private void ParseStatement(int level)
    {
        var start = _position;
        if (StartsWith("::icon("))
        {
            _position += "::icon(".Length;
            var end = _text.IndexOfOrdinal(')', _position);
            var lineEnd = LineEnd(_position);
            if (end < 0 || end > lineEnd)
                throw Error(start, "The icon is not closed by ')'.");

            var icon = _text[_position..end];
            _position = end + 1;
            var node = LastNode(start, "An icon");
            if (icon.Length > 0)
            {
                node.Icon = icon;
                _context.Warn(start, "The icons of the mindmaps are not drawn.");
            }

            return;
        }

        if (StartsWith(":::"))
        {
            _position += 3;
            var end = LineEnd(_position);
            var classes = _text[_position..end].Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            _position = end;
            var node = LastNode(start, "A class");
            foreach (var className in classes)
            {
                node.CssClasses.Add(className);
            }

            return;
        }

        AddNode(ParseNode(), level, start);
    }

    // The whitespace of JavaScript (\s), except the line feeds
    internal static bool IsSpace(char c) => c is not '\n' && (char.IsWhiteSpace(c) || c is '\uFEFF');

    private MindmapNode LastNode(int offset, string what)
    {
        if (_nodes.Count is 0)
            throw Error(offset, $"{what} decorates the node before it: there is none.");

        return _nodes[^1].Node;
    }

    /// <summary>Parses a node: a text, or an optional identifier and a shape around a text.</summary>
    private MindmapNode ParseNode()
    {
        var start = _position;
        string? id = null;
        if (FindOpener() is null)
        {
            // The identifier, or the whole text of a node without shape
            while (_position < _text.Length && _text[_position] is not ('(' or '[' or '\n' or ')' or '{' or '}'))
            {
                _position++;
            }

            var raw = _text[start.._position].TrimEnd();
            if (FindOpener() is null)
            {
                if (_position < _text.Length && _text[_position] is '{' or '}')
                    throw Error(_position, $"Unexpected '{_text[_position]}': a hexagon opens with '{{{{' and closes with '}}}}'.");

                CheckWord(raw, start);
                return new MindmapNode(new DiagramText(MermaidText.DecodePlain(raw)));
            }

            CheckWord(raw, start);
            id = raw.Trim().Length is 0 ? null : raw.Trim();
        }

        var opener = FindOpener()!;
        var shapeStart = _position;
        _position += opener.Length;
        var label = ReadLabel(shapeStart);
        var closer = Array.Find(Closers, StartsWith) ?? throw Error(_position, $"Unexpected '{Describe(_position)}': the shape is not closed.");
        _position += closer.Length;
        return new MindmapNode(label, GetShape(opener, closer)) { Id = id };
    }

    /// <summary>Returns the shape of a pair of delimiters, as Mermaid: the opening one decides, but <c>(</c> that a <c>)</c> does not close is a cloud.</summary>
    internal static MindmapNodeShape GetShape(string opener, string closer) => opener switch
    {
        "[" => MindmapNodeShape.Square,
        "(" => closer == ")" ? MindmapNodeShape.RoundedSquare : MindmapNodeShape.Cloud,
        "((" => MindmapNodeShape.Circle,
        ")" => MindmapNodeShape.Cloud,
        "))" => MindmapNodeShape.Bang,
        "{{" => MindmapNodeShape.Hexagon,
        _ => MindmapNodeShape.Default,
    };

    /// <summary>Reads the text of a shape: a markdown string, a quoted string, or the characters up to a delimiter.</summary>
    private DiagramText ReadLabel(int shapeStart)
    {
        var start = _position;
        if (StartsWith("\"`"))
        {
            var end = _text.IndexOf("`\"", start + 2, StringComparison.Ordinal);
            if (end < 0)
                throw Error(start, "The markdown string is not closed by '`\"'.");

            var raw = _text[(start + 2)..end];
            if (raw.Length is 0 || raw.AsSpan().IndexOfAny('"', '`') >= 0)
                throw Error(start, "A markdown string cannot be empty nor contain '\"' or '`'.");

            _position = end + 2;
            return DiagramText.Markdown(MermaidText.DecodeEntities(string.Join('\n', raw.Split('\n').Select(line => line.Trim()))));
        }

        if (StartsWith("\""))
        {
            var end = _text.IndexOfOrdinal('"', start + 1);
            if (end < 0)
                throw Error(start, "The string is not closed.");

            var raw = _text[(start + 1)..end];
            if (raw.Length is 0)
                throw Error(start, "The text of the node is empty.");

            _position = end + 1;
            return new DiagramText(MermaidText.DecodePlain(raw));
        }

        while (_position < _text.Length && _text[_position] is not (')' or ']' or '(' or '}'))
        {
            _position++;
        }

        if (_position == start)
            throw Error(shapeStart, "The text of the node is empty.");

        return new DiagramText(MermaidText.DecodePlain(_text[start.._position]));
    }

    private void AddNode(MindmapNode node, int level, int offset)
    {
        if (_nodes.Count >= _context.Options.MaxNodes)
            throw Error(offset, $"The mindmap has more than {_context.Options.MaxNodes} nodes.");

        if (_baseLevel is null)
        {
            _baseLevel = level;
            _diagram.Root = node;
            _nodes.Add((0, 0, node));
            return;
        }

        level -= _baseLevel.Value;
        for (var i = _nodes.Count - 1; i >= 0; i--)
        {
            var (parentLevel, parentDepth, parent) = _nodes[i];
            if (parentLevel < level)
            {
                if (parentDepth + 1 > _context.Options.MaxDepth)
                    throw Error(offset, $"The mindmap is deeper than {_context.Options.MaxDepth} levels.");

                parent.Children.Add(node);
                _nodes.Add((level, parentDepth + 1, node));
                return;
            }
        }

        throw Error(offset, $"There can be only one root: no parent is found for '{node.Label.Text}'.");
    }

    /// <summary>Throws when a text starts with the keyword <c>mindmap</c>, which Mermaid reads as the keyword.</summary>
    private void CheckWord(string raw, int offset)
    {
        if (StartsWithKeyword(raw))
            throw Error(offset, "A node cannot start with the keyword 'mindmap'.");
    }

    internal static bool StartsWithKeyword(string text) => text.StartsWith("mindmap", StringComparison.OrdinalIgnoreCase) && (text.Length == 7 || !(char.IsAsciiLetterOrDigit(text[7]) || text[7] == '_'));

    private string? FindOpener() => Array.Find(Openers, StartsWith);

    private bool StartsWith(string value) => string.CompareOrdinal(_text, _position, value, 0, value.Length) == 0 && _position + value.Length <= _text.Length;

    private bool IsComment(int position) => position + 1 < _text.Length && _text[position] == '%' && _text[position + 1] == '%';

    private int LineEnd(int position)
    {
        var end = _text.IndexOfOrdinal('\n', position);
        return end < 0 ? _text.Length : end;
    }

    private void SkipToLineEnd() => _position = LineEnd(_position);

    private string Describe(int position)
    {
        if (position >= _text.Length)
            return "end of text";

        var end = position;
        while (end < _text.Length && end - position < 20 && !char.IsWhiteSpace(_text[end]))
        {
            end++;
        }

        return end == position ? _text[position].ToString() : _text[position..end];
    }

    private MermaidParseException Error(int offset, string message) => _context.Error(offset, message);
}
