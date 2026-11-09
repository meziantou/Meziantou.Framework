using System.Globalization;
using Meziantou.Framework.Diagrams.Mermaid.Internals.GitGraphDiagrams;

namespace Meziantou.Framework.Diagrams.Mermaid.Internals.PieDiagrams;

/// <summary>
/// Parses the <c>pie</c> diagrams of Mermaid: <c>pie</c>, an optional <c>showData</c>, then the title, the accessibility
/// statements and the slices, as <c>"Dogs" : 386</c>, a statement per line.
/// </summary>
internal sealed class PieDiagramParser
{
    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly PieDiagram _diagram = new();
    private readonly HashSet<string> _labels = new(StringComparer.Ordinal);

    private PieDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static PieDiagram Parse(ParseContext context, int position) => new PieDiagramParser(context, position).Parse();

    private PieDiagram Parse()
    {
        var keywordStart = _scanner.Position;
        if (!TryConsumeKeyword("pie"))
            throw _scanner.Error(keywordStart, "'pie' is expected.");

        ApplyConfiguration();

        // showData is on the line of the keyword; the first statement can follow on the same line
        _scanner.SkipSpaces();
        if (TryConsumeKeyword("showData"))
        {
            _diagram.ShowData = true;
        }

        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.IsEnd)
                break;

            if (_scanner.Current == '\n')
            {
                _scanner.Position++;
                continue;
            }

            if (MermaidCommon.TryParseAccessibility(_scanner, _diagram) || GitGraphLangium.TryParseTitle(_scanner, _diagram))
            {
                ExpectLineEnd();
                continue;
            }

            ParseSlice();
        }

        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "pie");
        var configuration = _context.Configuration;
        if (configuration.GetNumber("pie", "textPosition") is { } textPosition)
        {
            if (textPosition is >= 0 and <= 1)
            {
                _diagram.TextPosition = textPosition;
            }
            else
            {
                _context.Warn(0, "The configuration 'pie.textPosition' must be between 0 and 1: it is ignored.");
            }
        }

        // As Mermaid, a donut hole out of its range draws a full pie
        if (configuration.GetNumber("pie", "donutHole") is > 0 and <= 0.9 and var donutHole)
        {
            _diagram.DonutHole = donutHole;
        }

        if (configuration.GetString("pie", "legendPosition") is { } legendPosition)
        {
            _diagram.LegendPosition = legendPosition switch
            {
                "top" => PieLegendPosition.Top,
                "bottom" => PieLegendPosition.Bottom,
                "left" => PieLegendPosition.Left,
                "center" => PieLegendPosition.Center,
                "right" => PieLegendPosition.Right,
                _ => WarnLegendPosition(legendPosition),
            };
        }

        if (configuration.GetString("pie", "highlightSlice") is { Length: > 0 } highlightSlice)
        {
            if (highlightSlice is "hover")
            {
                _context.Warn(0, "The highlight of the slice under the mouse is not supported.");
            }
            else
            {
                _diagram.HighlightedSlice = highlightSlice;
            }
        }

        for (var i = 0; i < 12; i++)
        {
            var index = i;
            ReadColor("pie" + (i + 1).ToString(CultureInfo.InvariantCulture), color => _diagram.SliceColors[index] = color);
        }

        ReadColor("pieStrokeColor", color => _diagram.StrokeColor = color);
        ReadColor("pieOuterStrokeColor", color => _diagram.OuterStrokeColor = color);
        ReadColor("pieSectionTextColor", color => _diagram.SectionTextColor = color);
        ReadColor("pieLegendTextColor", color => _diagram.LegendTextColor = color);
        if (configuration.GetNumber("themeVariables", "pieStrokeWidth") is >= 0 and <= 1000 and var strokeWidth)
        {
            _diagram.StrokeWidth = strokeWidth;
        }

        if (configuration.GetNumber("themeVariables", "pieOuterStrokeWidth") is >= 0 and <= 1000 and var outerStrokeWidth)
        {
            _diagram.OuterStrokeWidth = outerStrokeWidth;
        }

        if (configuration.GetNumber("themeVariables", "pieOpacity") is >= 0 and <= 1 and var opacity)
        {
            _diagram.Opacity = opacity;
        }

        if (configuration.GetNumber("themeVariables", "pieSectionTextSize") is >= 1 and <= 200 and var sectionTextSize)
        {
            _diagram.SectionTextSize = sectionTextSize;
        }

        if (configuration.GetNumber("themeVariables", "pieLegendTextSize") is >= 1 and <= 200 and var legendTextSize)
        {
            _diagram.LegendTextSize = legendTextSize;
        }

        void ReadColor(string name, Action<SceneColor> set)
        {
            if (configuration.GetString("themeVariables", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
            {
                set(color);
            }
        }
    }

    private PieLegendPosition WarnLegendPosition(string legendPosition)
    {
        _context.Warn(0, $"The legend position '{legendPosition}' is not supported: use 'top', 'bottom', 'left', 'right' or 'center'.");
        return PieLegendPosition.Right;
    }

    private void ParseSlice()
    {
        var start = _scanner.Position;
        if (_scanner.Current is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted label is expected.");

        var label = GitGraphLangium.ReadString(_scanner);
        _scanner.SkipSpaces();
        _scanner.Expect(":");
        _scanner.SkipSpaces();
        var valueStart = _scanner.Position;
        var value = PieNumber.Read(_scanner, allowMinus: true);
        var valueText = _scanner.Text[valueStart.._scanner.Position];
        ExpectLineEnd();
        if (value < 0)
            throw _scanner.Error(valueStart, $"\"{label}\" has invalid value: {valueText}. Negative values are not allowed in pie charts. All slice values must be >= 0.");

        // As Mermaid, a label keeps its first value
        if (!_labels.Add(label))
        {
            _context.Warn(start, $"The slice \"{label}\" is already defined: its value is ignored.");
            return;
        }

        if (_diagram.Slices.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} slices.");

        // -0 is zero
        _diagram.Slices.Add(new PieSlice(label, value + 0.0));
    }

    private bool TryConsumeKeyword(string keyword)
    {
        if (!_scanner.StartsWith(keyword))
            return false;

        // As the keywords of the Langium grammars of Mermaid, the keyword is followed by a space, a comment or the end of the line
        var next = _scanner.Position + keyword.Length;
        if (next < _scanner.Text.Length && _scanner.Text[next] is not (' ' or '\t' or '\n') && !_scanner.IsCommentAt(next))
            return false;

        _scanner.Position = next;
        return true;
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }
}
