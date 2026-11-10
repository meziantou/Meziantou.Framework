using Meziantou.Framework.Mermaid.Internals.GitGraphDiagrams;
using Meziantou.Framework.Mermaid.Internals.PieDiagrams;

namespace Meziantou.Framework.Mermaid.Internals.RadarDiagrams;

/// <summary>
/// Parses the <c>radar-beta</c> diagrams of Mermaid: <c>axis</c> and <c>curve</c> statements, the options
/// (<c>showLegend</c>, <c>ticks</c>, <c>max</c>, <c>min</c>, <c>graticule</c>), the title and the accessibility
/// statements. As the Langium grammar of Mermaid, the statements do not need to be on their own lines.
/// </summary>
internal sealed class RadarDiagramParser
{
    private const int MaxTicks = 32;

    private readonly ParseContext _context;
    private readonly Scanner _scanner;
    private readonly RadarChart _diagram = new();
    private readonly List<PendingCurve> _curves = [];
    private readonly Dictionary<string, (double Value, int Position)> _options = new(StringComparer.Ordinal);

    private RadarDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _scanner = new Scanner(context, position);
    }

    public static RadarChart Parse(ParseContext context, int position) => new RadarDiagramParser(context, position).Parse();

    private RadarChart Parse()
    {
        var keywordStart = _scanner.Position;
        if (!_scanner.TryConsume("radar-beta"))
            throw _scanner.Error(keywordStart, "'radar-beta' is expected.");

        if (!_scanner.TryConsume(":"))
        {
            if (!_scanner.IsEnd && _scanner.Current is not (' ' or '\t' or '\n') && !_scanner.IsCommentAt(_scanner.Position))
                throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");

            _scanner.SkipSpaces();
            _scanner.TryConsume(":");
        }

        ApplyConfiguration();
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

            var start = _scanner.Position;
            if (TryConsumeKeyword("axis"))
            {
                do
                {
                    ParseAxis();
                }
                while (TryConsumeComma());
            }
            else if (TryConsumeKeyword("curve"))
            {
                do
                {
                    ParseCurve();
                }
                while (TryConsumeComma());
            }
            else if (IsOptionKeyword())
            {
                do
                {
                    ParseOption();
                }
                while (TryConsumeComma());
            }
            else
            {
                throw _scanner.Error(start, $"Unexpected '{_scanner.Describe()}': 'axis', 'curve' or an option is expected.");
            }
        }

        ResolveCurves();
        ApplyOptions();
        return _diagram;
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "radar");
        var configuration = _context.Configuration;
        Read("width", 1, 10_000, value => _diagram.Width = value);
        Read("height", 1, 10_000, value => _diagram.Height = value);
        Read("marginTop", 0, 10_000, value => _diagram.MarginTop = value);
        Read("marginRight", 0, 10_000, value => _diagram.MarginRight = value);
        Read("marginBottom", 0, 10_000, value => _diagram.MarginBottom = value);
        Read("marginLeft", 0, 10_000, value => _diagram.MarginLeft = value);
        Read("axisScaleFactor", 0, 100, value => _diagram.AxisScaleFactor = value);
        Read("axisLabelFactor", 0, 100, value => _diagram.AxisLabelFactor = value);
        Read("curveTension", 0, 1, value => _diagram.CurveTension = value);

        // The style of the radar charts is in the theme variables, under 'radar'
        ReadColor("axisColor", color => _diagram.AxisColor = color);
        ReadColor("graticuleColor", color => _diagram.GraticuleColor = color);
        ReadStyle("axisStrokeWidth", 0, 1000, value => _diagram.AxisStrokeWidth = value);
        ReadStyle("axisLabelFontSize", 1, 200, value => _diagram.AxisLabelFontSize = value);
        ReadStyle("curveOpacity", 0, 1, value => _diagram.CurveOpacity = value);
        ReadStyle("curveStrokeWidth", 0, 1000, value => _diagram.CurveStrokeWidth = value);
        ReadStyle("graticuleStrokeWidth", 0, 1000, value => _diagram.GraticuleStrokeWidth = value);
        ReadStyle("graticuleOpacity", 0, 1, value => _diagram.GraticuleOpacity = value);
        ReadStyle("legendFontSize", 1, 200, value => _diagram.LegendFontSize = value);

        void Read(string name, double minimum, double maximum, Action<double> set)
        {
            if (configuration.GetNumber("radar", name) is { } value)
            {
                if (value >= minimum && value <= maximum)
                {
                    set(value);
                }
                else
                {
                    _context.Warn(0, $"The configuration 'radar.{name}' is out of range: it is ignored.");
                }
            }
        }

        void ReadStyle(string name, double minimum, double maximum, Action<double> set)
        {
            if (configuration.GetNumber("themeVariables", "radar", name) is { } value && value >= minimum && value <= maximum)
            {
                set(value);
            }
        }

        void ReadColor(string name, Action<SceneColor> set)
        {
            if (configuration.GetString("themeVariables", "radar", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
            {
                set(color);
            }
        }
    }

    private void ParseAxis()
    {
        var start = _scanner.Position;
        var name = ReadIdentifier("the name of an axis");
        var label = ReadLabel();
        CheckCount(start);
        _diagram.Axes.Add(new RadarAxis(name, label));
    }

    private void ParseCurve()
    {
        var start = _scanner.Position;
        var name = ReadIdentifier("the name of a curve");
        var label = ReadLabel();
        SkipHidden();
        _scanner.Expect("{");
        SkipHiddenAndLines();
        var curve = new PendingCurve(new RadarCurve(name, label), start);
        var byAxis = !char.IsAsciiDigit(_scanner.Current);
        while (true)
        {
            var entryStart = _scanner.Position;
            if (byAxis)
            {
                var axis = ReadIdentifier("the name of an axis");
                SkipHidden();
                _scanner.TryConsume(":");
                SkipHidden();
                curve.Entries.Add((axis, PieNumber.Read(_scanner, allowMinus: false)));
            }
            else
            {
                curve.Curve.Values.Add(PieNumber.Read(_scanner, allowMinus: false));
            }

            if (curve.Entries.Count + curve.Curve.Values.Count > _context.Options.MaxNodes)
                throw _scanner.Error(entryStart, $"The curve has more than {_context.Options.MaxNodes} values.");

            SkipHidden();
            if (!_scanner.TryConsume(","))
                break;

            SkipHiddenAndLines();
        }

        SkipHiddenAndLines();
        _scanner.Expect("}");
        CheckCount(start);
        _curves.Add(curve);
        _diagram.Curves.Add(curve.Curve);
    }

    private void ParseOption()
    {
        SkipHidden();
        var start = _scanner.Position;
        if (!IsOptionKeyword())
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': an option is expected.");

        var name = ReadWord();
        SkipHidden();
        double value;
        switch (name)
        {
            case "showLegend":
                if (TryConsumePrefix("true"))
                {
                    value = 1;
                }
                else if (TryConsumePrefix("false"))
                {
                    value = 0;
                }
                else
                {
                    throw _scanner.Error($"Unexpected '{_scanner.Describe()}': 'true' or 'false' is expected.");
                }

                break;
            case "graticule":
                if (TryConsumePrefix("circle"))
                {
                    value = (double)RadarGraticule.Circle;
                }
                else if (TryConsumePrefix("polygon"))
                {
                    value = (double)RadarGraticule.Polygon;
                }
                else
                {
                    throw _scanner.Error($"Unexpected '{_scanner.Describe()}': 'circle' or 'polygon' is expected.");
                }

                break;
            default:
                value = PieNumber.Read(_scanner, allowMinus: false);
                break;
        }

        // As Mermaid, the last value of an option wins
        _options[name] = (value, start);
    }

    private void ApplyOptions()
    {
        if (_options.TryGetValue("showLegend", out var showLegend))
        {
            _diagram.ShowLegend = showLegend.Value is 1;
        }

        if (_options.TryGetValue("graticule", out var graticule))
        {
            _diagram.Graticule = (RadarGraticule)(int)graticule.Value;
        }

        if (_options.TryGetValue("ticks", out var ticks))
        {
            var count = ticks.Value;
            if (count > MaxTicks)
            {
                _context.Warn(ticks.Position, $"The radar has {PieNumber.Format(count)} ticks, more than the maximum of {MaxTicks}: {MaxTicks} are drawn.");
                count = MaxTicks;
            }
            else if (count != Math.Floor(count))
            {
                _context.Warn(ticks.Position, $"The number of ticks {PieNumber.Format(count)} is not an integer: it is rounded up.");
                count = Math.Ceiling(count);
            }

            _diagram.Ticks = (int)count;
        }

        if (_options.TryGetValue("max", out var maximum))
        {
            _diagram.Maximum = maximum.Value;
        }

        if (_options.TryGetValue("min", out var minimum))
        {
            _diagram.Minimum = minimum.Value;
        }
    }

    /// <summary>Puts the values given by axis in the order of the axes, as Mermaid does once every axis is read.</summary>
    private void ResolveCurves()
    {
        foreach (var curve in _curves)
        {
            if (curve.Entries.Count is 0)
                continue;

            if (_diagram.Axes.Count is 0)
                throw _scanner.Error(curve.Position, "Axes must be populated before curves for reference entries.");

            foreach (var axis in _diagram.Axes)
            {
                var index = curve.Entries.FindIndex(entry => string.Equals(entry.Axis, axis.Name, StringComparison.Ordinal));
                if (index < 0)
                    throw _scanner.Error(curve.Position, $"Missing entry for axis {axis.Label ?? axis.Name}.");

                curve.Curve.Values.Add(curve.Entries[index].Value);
            }
        }
    }

    private void CheckCount(int start)
    {
        if (_diagram.Axes.Count + _diagram.Curves.Count >= _context.Options.MaxNodes)
            throw _scanner.Error(start, $"The diagram has more than {_context.Options.MaxNodes} axes and curves.");
    }

    private string? ReadLabel()
    {
        SkipHidden();
        if (!_scanner.TryConsume("["))
            return null;

        SkipHidden();
        if (_scanner.Current is not ('"' or '\''))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': a quoted label is expected.");

        var label = GitGraphLangium.ReadString(_scanner);
        SkipHidden();
        _scanner.Expect("]");
        return label;
    }

    /// <summary>Reads an identifier: a letter, a digit or an underscore, then dashes too, but not at the end.</summary>
    private string ReadIdentifier(string description)
    {
        SkipHidden();
        var start = _scanner.Position;
        var length = RadarIdentifier.GetLength(_scanner.Text, start);
        if (!RadarIdentifier.IsValidAt(_scanner.Text, start, length))
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}': {description} is expected.");

        _scanner.Position += length;
        return _scanner.Text.Substring(start, length);
    }

    private string ReadWord()
    {
        var start = _scanner.Position;
        _scanner.Position += RadarIdentifier.GetLength(_scanner.Text, start);
        return _scanner.Text[start.._scanner.Position];
    }

    private bool IsOptionKeyword()
    {
        var length = RadarIdentifier.GetLength(_scanner.Text, _scanner.Position);
        return _scanner.Text.AsSpan(_scanner.Position, length) is "showLegend" or "ticks" or "max" or "min" or "graticule";
    }

    /// <summary>Consumes a keyword: as Langium, a longer identifier that starts with the keyword is not the keyword.</summary>
    private bool TryConsumeKeyword(string keyword)
    {
        if (!_scanner.StartsWith(keyword) || RadarIdentifier.GetLength(_scanner.Text, _scanner.Position) != keyword.Length)
            return false;

        _scanner.Position += keyword.Length;
        return true;
    }

    /// <summary>Consumes the text of a terminal of Mermaid (true, false, circle, polygon): as Chevrotain, the terminal does not need to end there.</summary>
    private bool TryConsumePrefix(string value) => _scanner.TryConsume(value);

    private bool TryConsumeComma()
    {
        SkipHidden();
        return _scanner.TryConsume(",");
    }

    /// <summary>Skips the spaces, the tabs and a comment, which Langium hides; the line feeds are tokens.</summary>
    private void SkipHidden() => _scanner.SkipSpaces();

    private void SkipHiddenAndLines()
    {
        while (true)
        {
            _scanner.SkipSpaces();
            if (_scanner.Current != '\n')
                return;

            _scanner.Position++;
        }
    }

    private void ExpectLineEnd()
    {
        _scanner.SkipSpaces();
        if (!_scanner.IsEnd && _scanner.Current != '\n')
            throw _scanner.Error($"Unexpected '{_scanner.Describe()}'.");
    }

    private sealed class PendingCurve(RadarCurve curve, int position)
    {
        public RadarCurve Curve { get; } = curve;

        public int Position { get; } = position;

        public List<(string Axis, double Value)> Entries { get; } = [];
    }
}
