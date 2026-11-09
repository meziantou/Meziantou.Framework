using System.Globalization;
using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.TreemapDiagrams;

/// <summary>
/// Parses the <c>treemap</c> (or <c>treemap-beta</c>) diagrams of Mermaid: a quoted name per line, a section, or a leaf
/// with <c>: value</c>, nested by their indentation as Mermaid nests them, with <c>:::class</c> and <c>classDef</c>.
/// </summary>
/// <remarks>
/// As Mermaid, a row is a child of the last section less indented than it (a leaf has no children), and a value is the
/// number the digits, the points, the commas and the underscores make once the commas are removed (<c>1,000.5</c> is
/// 1000.5). Unlike Mermaid, a row is a line, the spaces at the end of a line and around <c>:::</c> are accepted, and the
/// values that are not numbers are rejected.
/// </remarks>
internal sealed class TreemapDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly TreemapDiagram _diagram = new();
    private readonly List<(int Level, TreemapNode Node)> _stack = [];
    private int _count;

    private TreemapDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static TreemapDiagram Parse(ParseContext context, int position) => new TreemapDiagramParser(context, position).Parse();

    /// <summary>Indicates whether a text is a class name of the <c>:::class</c> of the rows.</summary>
    public static bool IsClassName(string name, int minimumLength = 1)
    {
        if (name.Length < minimumLength || !(char.IsAsciiLetter(name[0]) || name[0] == '_'))
            return false;

        foreach (var c in name)
        {
            if (!char.IsAsciiLetterOrDigit(c) && c != '_')
                return false;
        }

        return true;
    }

    private TreemapDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("treemap-beta") && !_scanner.TryConsume("treemap"))
            throw _scanner.Error(keywordStart, "'treemap' is expected.");

        ExpectLineEnd();
        ApplyConfiguration();
        while (!_scanner.IsEnd)
        {
            // The scanner is on the line feed that ends the previous line
            _scanner.Position++;
            var lineStart = _scanner.Position;
            while (!_scanner.IsEnd && _scanner.Current is ' ' or '\t')
            {
                _scanner.Position++;
            }

            if (_scanner.IsEnd)
                break;

            // A blank line: the next iteration skips its line feed
            if (_scanner.Current == '\n')
                continue;

            if (_scanner.IsCommentAt(_scanner.Position))
            {
                _scanner.ReadLine();
                continue;
            }

            ParseLine(_scanner.Position - lineStart);
            ExpectLineEnd();
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "treemap");
        var configuration = _context.Configuration;
        if (configuration.GetNumber("treemap", "padding") is { } padding)
        {
            if (padding is >= 0 and <= 1000)
            {
                _diagram.InnerPadding = padding;
            }
            else
            {
                _context.Warn(0, "The configuration 'treemap.padding' must be between 0 and 1000: it is ignored.");
            }
        }

        // Mermaid draws a treemap 960 by 500 pixels when nodeWidth or nodeHeight is 0
        if (configuration.GetNumber("treemap", "nodeWidth") is { } nodeWidth)
        {
            if (nodeWidth is 0)
            {
                _diagram.NodeWidth = 96;
            }
            else if (nodeWidth is >= 1 and <= 1000)
            {
                _diagram.NodeWidth = nodeWidth;
            }
            else
            {
                _context.Warn(0, "The configuration 'treemap.nodeWidth' must be between 1 and 1000: it is ignored.");
            }
        }

        if (configuration.GetNumber("treemap", "nodeHeight") is { } nodeHeight)
        {
            if (nodeHeight is 0)
            {
                _diagram.NodeHeight = 50;
            }
            else if (nodeHeight is >= 1 and <= 1000)
            {
                _diagram.NodeHeight = nodeHeight;
            }
            else
            {
                _context.Warn(0, "The configuration 'treemap.nodeHeight' must be between 1 and 1000: it is ignored.");
            }
        }

        if (configuration.GetBoolean("treemap", "showValues") is { } showValues)
        {
            _diagram.ShowValues = showValues;
        }

        if (configuration.GetString("treemap", "valueFormat") is { } valueFormat)
        {
            _diagram.ValueFormat = valueFormat;
        }
    }

    private void ParseLine(int indentation)
    {
        if (_scanner.StartsWith("classDef") && _scanner.Peek("classDef".Length) is ' ' or '\t')
        {
            ParseClassDefinition();
            return;
        }

        if (MermaidCommon.TryParseAccessibility(_scanner, _diagram) || GitGraphLangium.TryParseTitle(_scanner, _diagram))
            return;

        if (_scanner.Current is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted name is expected.");

        var start = _scanner.Position;
        var name = _scanner.ReadQuoted();
        _scanner.SkipSpaces();
        double? value = null;
        if (!_scanner.StartsWith(":::") && _scanner.Current is ':' or ',')
        {
            _scanner.Position++;
            _scanner.SkipSpaces();
            value = ReadValue();
            _scanner.SkipSpaces();
        }

        string? cssClass = null;
        if (_scanner.TryConsume(":::"))
        {
            var classStart = _scanner.Position;
            while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
            {
                _scanner.Position++;
            }

            cssClass = _scanner.Text[classStart.._scanner.Position];
            if (!IsClassName(cssClass))
                throw _scanner.Error(classStart, "A class name is expected after ':::'.");
        }

        AddNode(indentation, new TreemapNode(name, value) { CssClass = cssClass }, start);
    }

    private void AddNode(int level, TreemapNode node, int offset)
    {
        if (++_count > _context.Options.MaxNodes)
            throw _scanner.Error(offset, $"The diagram has more than {_context.Options.MaxNodes} nodes.");

        while (_stack.Count > 0 && _stack[^1].Level >= level)
        {
            _stack.RemoveAt(_stack.Count - 1);
        }

        if (_stack.Count is 0)
        {
            _diagram.Nodes.Add(node);
        }
        else
        {
            _stack[^1].Node.Children.Add(node);
        }

        // Only a section has children
        if (node.Value is null)
        {
            if (_stack.Count >= _context.Options.MaxDepth)
                throw _scanner.Error(offset, $"The sections are nested deeper than {_context.Options.MaxDepth} levels.");

            _stack.Add((level, node));
        }
    }

    /// <summary>Reads a value: digits, points, commas and underscores, as the <c>parseFloat</c> of JavaScript reads them once the commas are removed.</summary>
    private double ReadValue()
    {
        var start = _scanner.Position;
        if (_scanner.IsEnd || !(char.IsAsciiDigit(_scanner.Current) || _scanner.Current == '.'))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a value is expected.");

        while (!_scanner.IsEnd && (char.IsAsciiDigit(_scanner.Current) || _scanner.Current is '.' or ',' or '_'))
        {
            _scanner.Position++;
        }

        var token = _scanner.Text[start.._scanner.Position];
        var text = token.Replace(",", "", StringComparison.Ordinal);

        // parseFloat stops at the first character that does not continue the number: an underscore or a second point
        var end = 0;
        while (end < text.Length && char.IsAsciiDigit(text[end]))
        {
            end++;
        }

        var digits = end;
        if (end < text.Length && text[end] == '.')
        {
            var fraction = end + 1;
            while (fraction < text.Length && char.IsAsciiDigit(text[fraction]))
            {
                fraction++;
            }

            digits += fraction - end - 1;
            end = fraction;
        }

        if (digits is 0)
            throw _scanner.Error(start, $"The value '{token}' is not a number.");

        var value = double.Parse(text.AsSpan(0, end), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture);
        if (value > 1e300)
            throw _scanner.Error(start, "The value is too large.");

        return value;
    }

    private void ParseClassDefinition()
    {
        var start = _scanner.Position;
        _scanner.Position += "classDef".Length;
        _scanner.SkipSpaces();
        var nameStart = _scanner.Position;
        while (!_scanner.IsEnd && (char.IsAsciiLetterOrDigit(_scanner.Current) || _scanner.Current == '_'))
        {
            _scanner.Position++;
        }

        var name = _scanner.Text[nameStart.._scanner.Position];
        if (!IsClassName(name, minimumLength: 2))
            throw _scanner.Error(nameStart, "A class name of at least two letters, digits or underscores is expected after 'classDef'.");

        if (!_scanner.IsEnd && _scanner.Current is not (' ' or '\t' or '\n' or ';'))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}' in the class name.");

        _scanner.SkipSpaces();
        var stylesStart = _scanner.Position;
        while (!_scanner.IsEnd && _scanner.Current is not ('\n' or ';') && !_scanner.IsCommentAt(_scanner.Position))
        {
            _scanner.Position++;
        }

        // Mermaid reads "\," as a comma in a value, as rgb(1\,2\,3)
        var styles = _scanner.Text[stylesStart.._scanner.Position].Replace("\\,", ",", StringComparison.Ordinal).Trim();
        _scanner.TryConsume(";");
        var style = MermaidStyle.Parse(styles, _context, start);
        _diagram.ClassDefinitions[name] = _diagram.ClassDefinitions.TryGetValue(name, out var existing) ? existing.Merge(style) : style;
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }
}
