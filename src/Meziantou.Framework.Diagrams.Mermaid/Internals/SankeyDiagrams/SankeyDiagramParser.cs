using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.SankeyDiagrams;

/// <summary>
/// Parses the <c>sankey</c> (or <c>sankey-beta</c>) diagrams of Mermaid: rows of comma-separated values
/// <c>source,target,value</c>, with the quoting of CSV (RFC 4180), as the grammar of Mermaid reads them.
/// </summary>
/// <remarks>
/// As Mermaid, the names are trimmed, a value is the number that starts the field (the <c>parseFloat</c> of JavaScript),
/// and the line feeds of a quoted name are kept, a run of them as one. Unlike Mermaid, the parser accepts the characters
/// that are not ASCII, the blank lines with spaces and the spaces around the quotes, and rejects the values that are not
/// positive numbers, and the cycles, which Mermaid fails to draw.
/// </remarks>
internal sealed class SankeyDiagramParser
{
    private readonly ParseContext _context;
    private readonly string _text;
    private readonly SankeyDiagram _diagram = new();
    private readonly List<int> _linkOffsets = [];
    private int _position;

    private SankeyDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _text = context.Text;
        _position = position;
    }

    public static SankeyDiagram Parse(ParseContext context, int position) => new SankeyDiagramParser(context, position).Parse();

    /// <summary>Indicates whether a character is white space for the <c>trim</c> of JavaScript.</summary>
    public static bool IsJavaScriptWhiteSpace(char c) => c is '\t' or '\n' or '\v' or '\f' or '\r' or ' ' or '\u00A0' or '\u1680' or (>= '\u2000' and <= '\u200A') or '\u2028' or '\u2029' or '\u202F' or '\u205F' or '\u3000' or '\uFEFF';

    /// <summary>Reads the number that starts a text, as the <c>parseFloat</c> of JavaScript: <see cref="double.NaN"/> when there is none.</summary>
    public static double ParseFloat(string text)
    {
        var i = 0;
        while (i < text.Length && IsJavaScriptWhiteSpace(text[i]))
        {
            i++;
        }

        var start = i;
        var negative = false;
        if (i < text.Length && text[i] is '+' or '-')
        {
            negative = text[i] == '-';
            i++;
        }

        if (string.CompareOrdinal(text, i, "Infinity", 0, 8) == 0)
            return negative ? double.NegativeInfinity : double.PositiveInfinity;

        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length && text[i] == '.')
        {
            var afterPoint = i + 1;
            var fraction = 0;
            while (afterPoint + fraction < text.Length && char.IsAsciiDigit(text[afterPoint + fraction]))
            {
                fraction++;
            }

            if (digits > 0 || fraction > 0)
            {
                i = afterPoint + fraction;
                digits += fraction;
            }
        }

        if (digits is 0)
            return double.NaN;

        // An exponent is read only when it has digits
        if (i < text.Length && text[i] is 'e' or 'E')
        {
            var j = i + 1;
            if (j < text.Length && text[j] is '+' or '-')
            {
                j++;
            }

            var exponentStart = j;
            while (j < text.Length && char.IsAsciiDigit(text[j]))
            {
                j++;
            }

            if (j > exponentStart)
            {
                i = j;
            }
        }

        return double.Parse(text.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    private SankeyDiagram Parse()
    {
        var keywordStart = _position;
        if (!TryConsume("sankey-beta") && !TryConsume("sankey"))
            throw _context.Error(keywordStart, "'sankey' is expected.");

        SkipSpaces();
        if (_position < _text.Length && _text[_position] != '\n')
            throw _context.Error(_position, $"Unexpected '{Describe()}': the rows start on the next line.");

        ApplyConfiguration();
        while (_position < _text.Length)
        {
            // The line feed that ends the previous line, then the blank lines
            _position++;
            var lineStart = _position;
            SkipSpaces();
            if (_position >= _text.Length)
                break;

            if (_text[_position] == '\n')
                continue;

            _position = lineStart;
            ParseRow();
        }

        CheckCycles();
        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "sankey");
        var configuration = _context.Configuration;
        if (configuration.GetNumber("sankey", "width") is { } width)
        {
            if (width is >= 1 and <= 10_000)
            {
                _diagram.Width = width;
            }
            else
            {
                _context.Warn(0, "The configuration 'sankey.width' must be between 1 and 10000: it is ignored.");
            }
        }

        if (configuration.GetNumber("sankey", "height") is { } height)
        {
            if (height is >= 1 and <= 10_000)
            {
                _diagram.Height = height;
            }
            else
            {
                _context.Warn(0, "The configuration 'sankey.height' must be between 1 and 10000: it is ignored.");
            }
        }

        if (configuration.GetNumber("sankey", "nodeWidth") is >= 0 and <= 1000 and var nodeWidth)
        {
            _diagram.NodeWidth = nodeWidth;
        }

        if (configuration.GetNumber("sankey", "nodePadding") is >= 0 and <= 1000 and var nodePadding)
        {
            _diagram.NodePadding = nodePadding;
        }

        if (configuration.GetString("sankey", "linkColor") is { } linkColor)
        {
            switch (linkColor)
            {
                case "gradient":
                    _diagram.LinkColoring = SankeyLinkColoring.Gradient;
                    break;
                case "source":
                    _diagram.LinkColoring = SankeyLinkColoring.Source;
                    break;
                case "target":
                    _diagram.LinkColoring = SankeyLinkColoring.Target;
                    break;
                default:
                    if (SceneColor.TryParse(linkColor, out var color))
                    {
                        _diagram.LinkColor = color;
                    }
                    else
                    {
                        _context.Warn(0, $"The link color '{linkColor}' is not supported: use 'gradient', 'source', 'target' or a color.");
                    }

                    break;
            }
        }

        if (configuration.GetString("sankey", "nodeAlignment") is { } alignment)
        {
            switch (alignment)
            {
                case "justify":
                    _diagram.NodeAlignment = SankeyNodeAlignment.Justify;
                    break;
                case "left":
                    _diagram.NodeAlignment = SankeyNodeAlignment.Left;
                    break;
                case "right":
                    _diagram.NodeAlignment = SankeyNodeAlignment.Right;
                    break;
                case "center":
                    _diagram.NodeAlignment = SankeyNodeAlignment.Center;
                    break;
                default:
                    _context.Warn(0, $"The node alignment '{alignment}' is not supported: use 'justify', 'left', 'right' or 'center'.");
                    break;
            }
        }

        if (configuration.GetBoolean("sankey", "showValues") is { } showValues)
        {
            _diagram.ShowValues = showValues;
        }

        if (configuration.GetString("sankey", "prefix") is { } prefix)
        {
            _diagram.ValuePrefix = prefix;
        }

        if (configuration.GetString("sankey", "suffix") is { } suffix)
        {
            _diagram.ValueSuffix = suffix;
        }

        if (configuration.GetString("sankey", "labelStyle") is { } labelStyle)
        {
            switch (labelStyle)
            {
                case "legacy":
                    _diagram.LabelStyle = SankeyLabelStyle.Legacy;
                    break;
                case "outlined":
                    _diagram.LabelStyle = SankeyLabelStyle.Outlined;
                    break;
                default:
                    _context.Warn(0, $"The label style '{labelStyle}' is not supported: use 'legacy' or 'outlined'.");
                    break;
            }
        }

        if (configuration.GetMap("sankey", "nodeColors") is { } nodeColors)
        {
            foreach (var (name, value) in nodeColors)
            {
                if (value is string text && SceneColor.TryParse(text, out var color))
                {
                    _diagram.NodeColors[name] = color;
                }
                else
                {
                    _context.Warn(0, $"The color of the node '{name}' is not a color: it is ignored.");
                }
            }
        }
    }

    private void ParseRow()
    {
        var rowStart = _position;
        var source = ReadField(out _);
        Expect(',');
        var target = ReadField(out _);
        Expect(',');
        var valueStart = _position;
        var valueText = ReadField(out var quoted);
        SkipSpaces();
        if (_position < _text.Length && _text[_position] != '\n')
        {
            if (_text[_position] == ',')
                throw _context.Error(_position, "A row has three columns: the source, the target and the value.");

            throw _context.Error(_position, $"Unexpected '{Describe()}'.");
        }

        var value = ParseFloat(valueText);
        if (double.IsNaN(value))
            throw _context.Error(valueStart, $"The value '{(quoted ? valueText : valueText.Trim())}' is not a number.");

        if (value < 0)
            throw _context.Error(valueStart, $"The value '{value.ToString("R", CultureInfo.InvariantCulture)}' is negative: the values of a sankey diagram are positive.");

        if (value > 1e300)
            throw _context.Error(valueStart, "The value is too large.");

        if (_diagram.Links.Count >= _context.Options.MaxNodes)
            throw _context.Error(rowStart, $"The diagram has more than {_context.Options.MaxNodes} links.");

        _diagram.Links.Add(new SankeyLink(Trim(source), Trim(target), value));
        _linkOffsets.Add(rowStart);
    }

    /// <summary>Reads a field: a quoted text, whose <c>""</c> is a quote, or the text up to the next comma or the end of the line.</summary>
    private string ReadField(out bool quoted)
    {
        SkipSpaces();
        quoted = false;
        if (_position < _text.Length && _text[_position] == '"')
        {
            quoted = true;
            var start = _position;
            _position++;
            var builder = new StringBuilder();
            while (true)
            {
                if (_position >= _text.Length)
                    throw _context.Error(start, "The quoted text is not closed.");

                var c = _text[_position];
                if (c == '"')
                {
                    if (_position + 1 < _text.Length && _text[_position + 1] == '"')
                    {
                        builder.Append('"');
                        _position += 2;
                        continue;
                    }

                    _position++;
                    break;
                }

                // Mermaid reads a run of line feeds as one
                if (c != '\n' || builder.Length is 0 || builder[^1] != '\n')
                {
                    builder.Append(c);
                }

                _position++;
            }

            SkipSpaces();
            if (_position < _text.Length && _text[_position] is not (',' or '\n'))
                throw _context.Error(_position, $"Unexpected '{Describe()}' after the quoted text.");

            return builder.ToString();
        }

        var fieldStart = _position;
        while (_position < _text.Length && _text[_position] is not (',' or '\n'))
        {
            if (_text[_position] == '"')
                throw _context.Error(_position, "A quote must be in a quoted field: write the field between quotes, and the quote as two quotes.");

            _position++;
        }

        return _text[fieldStart.._position];
    }

    /// <summary>Checks that the links do not form a cycle: d3-sankey, which lays the diagram out, rejects them.</summary>
    private void CheckCycles()
    {
        var indexes = new Dictionary<string, int>(StringComparer.Ordinal);
        var outgoing = new List<List<int>>();
        int GetIndex(string name)
        {
            if (!indexes.TryGetValue(name, out var index))
            {
                index = indexes.Count;
                indexes.Add(name, index);
                outgoing.Add([]);
            }

            return index;
        }

        var targets = new int[_diagram.Links.Count];
        for (var i = 0; i < _diagram.Links.Count; i++)
        {
            var source = GetIndex(_diagram.Links[i].Source);
            targets[i] = GetIndex(_diagram.Links[i].Target);
            outgoing[source].Add(i);
        }

        // 0: not visited, 1: on the path being explored, 2: done
        var state = new byte[indexes.Count];
        var stack = new Stack<(int Node, int Next)>();
        for (var start = 0; start < indexes.Count; start++)
        {
            if (state[start] != 0)
                continue;

            state[start] = 1;
            stack.Push((start, 0));
            while (stack.Count > 0)
            {
                var (node, next) = stack.Pop();
                if (next >= outgoing[node].Count)
                {
                    state[node] = 2;
                    continue;
                }

                stack.Push((node, next + 1));
                var link = outgoing[node][next];
                var target = targets[link];
                if (state[target] == 1)
                    throw _context.Error(_linkOffsets[link], $"The link from '{_diagram.Links[link].Source}' to '{_diagram.Links[link].Target}' closes a cycle: the links of a sankey diagram cannot form a cycle.");

                if (state[target] == 0)
                {
                    state[target] = 1;
                    stack.Push((target, 0));
                }
            }
        }
    }

    private static string Trim(string value)
    {
        var start = 0;
        var end = value.Length;
        while (start < end && IsJavaScriptWhiteSpace(value[start]))
        {
            start++;
        }

        while (end > start && IsJavaScriptWhiteSpace(value[end - 1]))
        {
            end--;
        }

        return value[start..end];
    }

    private void Expect(char c)
    {
        if (_position >= _text.Length || _text[_position] != c)
            throw _context.Error(_position, _position >= _text.Length || _text[_position] == '\n' ? "A row has three columns: the source, the target and the value." : $"'{c}' is expected.");

        _position++;
    }

    private bool TryConsume(string value)
    {
        if (string.CompareOrdinal(_text, _position, value, 0, value.Length) != 0)
            return false;

        _position += value.Length;
        return true;
    }

    private void SkipSpaces()
    {
        while (_position < _text.Length && _text[_position] is ' ' or '\t')
        {
            _position++;
        }
    }

    private string Describe()
    {
        var end = _position;
        while (end < _text.Length && end - _position < 20 && !char.IsWhiteSpace(_text[end]))
        {
            end++;
        }

        return end == _position ? _text[_position].ToString() : _text[_position..end];
    }
}
