using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.QuadrantChartDiagrams;

/// <summary>
/// Parses the <c>quadrantChart</c> diagrams of Mermaid with its jison grammar: a statement per line or per <c>;</c>,
/// <c>title</c>, <c>x-axis left --> right</c>, <c>y-axis bottom --> top</c>, <c>quadrant-1 text</c>, the points
/// <c>text:::class: [x, y] radius: 10, color: #ff0000</c>, and <c>classDef name styles</c>.
/// </summary>
internal sealed class QuadrantChartDiagramParser
{
    private const string AxisArrow = " \u27F6 ";

    private readonly ParseContext _context;
    private readonly List<QuadrantChartToken> _tokens;
    private readonly QuadrantChartDiagram _diagram = new();
    private int _index;

    private QuadrantChartDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _tokens = QuadrantChartLexer.Tokenize(context.Text, position, context.Text.Length);
    }

    private QuadrantChartToken Current => _tokens[_index];

    public static QuadrantChartDiagram Parse(ParseContext context, int position) => new QuadrantChartDiagramParser(context, position).Parse();

    private QuadrantChartDiagram Parse()
    {
        if (Current.Kind is not QuadrantChartTokenKind.Keyword)
            throw Error("'quadrantChart' is expected.");

        _index++;
        ApplyConfiguration();
        while (true)
        {
            ParseStatement();
            if (Current.Kind is QuadrantChartTokenKind.End)
                return _diagram;

            _index++;
        }
    }

    private void ParseStatement()
    {
        while (Current.Kind is QuadrantChartTokenKind.Space)
        {
            _index++;
        }

        var token = Current;
        switch (token.Kind)
        {
            case QuadrantChartTokenKind.NewLine or QuadrantChartTokenKind.Semicolon or QuadrantChartTokenKind.End:
                return;
            case QuadrantChartTokenKind.Title:
                _index++;
                var title = QuadrantChartLexer.Trim(token.Value);
                _diagram.Title = title.Length is 0 ? null : title;
                break;
            case QuadrantChartTokenKind.AccTitle:
                _index++;
                _diagram.AccessibleTitle = QuadrantChartLexer.Trim(token.Value);
                break;
            case QuadrantChartTokenKind.AccDescr:
                _index++;
                _diagram.Description = QuadrantChartLexer.Trim(token.Value);
                break;
            case QuadrantChartTokenKind.AccDescrMultiline:
                _index++;
                if (token.Value.Length > 0)
                {
                    _diagram.Description = string.Join('\n', token.Value.Split('\n').Select(QuadrantChartLexer.Trim)).Trim('\n');
                }

                break;
            case QuadrantChartTokenKind.XAxis or QuadrantChartTokenKind.YAxis:
                ParseAxis(token.Kind is QuadrantChartTokenKind.XAxis);
                break;
            case QuadrantChartTokenKind.Quadrant1 or QuadrantChartTokenKind.Quadrant2 or QuadrantChartTokenKind.Quadrant3 or QuadrantChartTokenKind.Quadrant4:
                _index++;
                var label = ToLabel(ParseText());
                switch (token.Kind)
                {
                    case QuadrantChartTokenKind.Quadrant1:
                        _diagram.Quadrant1Label = label;
                        break;
                    case QuadrantChartTokenKind.Quadrant2:
                        _diagram.Quadrant2Label = label;
                        break;
                    case QuadrantChartTokenKind.Quadrant3:
                        _diagram.Quadrant3Label = label;
                        break;
                    default:
                        _diagram.Quadrant4Label = label;
                        break;
                }

                break;
            case QuadrantChartTokenKind.ClassDef:
                ParseClassDefinition();
                break;
            default:
                if (token.IsAlphaNumeric || token.Kind is QuadrantChartTokenKind.String or QuadrantChartTokenKind.MarkdownString)
                {
                    ParsePoint();
                    break;
                }

                throw Unexpected();
        }

        if (!Current.IsStatementEnd)
            throw Unexpected();
    }

    private void ParseAxis(bool isX)
    {
        _index++;
        var first = ParseText();
        DiagramText? second = null;
        if (Current.Kind is QuadrantChartTokenKind.Delimiter)
        {
            _index++;
            if (Current.IsStatementEnd)
            {
                // A delimiter without a second text adds an arrow to the first one
                first = new DiagramText(QuadrantChartLexer.Trim(first.Text + AxisArrow), first.Format);
            }
            else
            {
                second = ParseText();
            }
        }

        if (isX)
        {
            _diagram.XAxisLeftLabel = ToLabel(first);
            if (second is not null)
            {
                _diagram.XAxisRightLabel = ToLabel(second);
            }
        }
        else
        {
            _diagram.YAxisBottomLabel = ToLabel(first);
            if (second is not null)
            {
                _diagram.YAxisTopLabel = ToLabel(second);
            }
        }
    }

    private void ParsePoint()
    {
        var start = Current.Start;
        var label = ParseText();
        string? className = null;
        if (Current.Kind is QuadrantChartTokenKind.ClassName)
        {
            className = Current.Value;
            _index++;
        }

        if (Current.Kind is not QuadrantChartTokenKind.Point)
            throw Error("The position of the point is expected: ': [x, y]'.");

        var position = Current;
        _index++;
        if (_diagram.Points.Count >= _context.Options.MaxNodes)
            throw _context.Error(start, $"The diagram has more than {_context.Options.MaxNodes} points.");

        var point = new QuadrantChartPoint(label, position.X, position.Y) { ClassName = className };
        if (!Current.IsStatementEnd)
        {
            ParseStyles(point.Style);
        }

        _diagram.Points.Add(point);
    }

    private void ParseClassDefinition()
    {
        _index++;
        ExpectSingleSpace();
        var name = new StringBuilder();
        while (Current.IsClassNamePart)
        {
            name.Append(Current.Value);
            _index++;
        }

        if (name.Length is 0)
            throw Error("The name of the class is expected.");

        ExpectSingleSpace();
        var style = new QuadrantChartPointStyle();
        ParseStyles(style);
        _diagram.ClassDefinitions[name.ToString()] = style;
    }

    private void ExpectSingleSpace()
    {
        if (Current.Kind is not QuadrantChartTokenKind.Space)
            throw Error("A space is expected.");

        _index++;
    }

    /// <summary>Reads the styles separated by commas, each <c>name: value</c>; the names and the values are those Mermaid validates.</summary>
    private void ParseStyles(QuadrantChartPointStyle style)
    {
        while (true)
        {
            var start = Current.Start;
            var builder = new StringBuilder();
            while (Current.IsStylePart)
            {
                builder.Append(Current.Value);
                _index++;
            }

            if (builder.Length is 0)
                throw Unexpected();

            ApplyStyle(style, QuadrantChartLexer.Trim(builder.ToString()), start);
            if (Current.Kind is not QuadrantChartTokenKind.Comma)
                return;

            _index++;
        }
    }

    private void ApplyStyle(QuadrantChartPointStyle style, string text, int offset)
    {
        var parts = SplitStyle(text);
        var key = parts[0];
        var value = parts.Count > 1 ? parts[1] : null;
        switch (key)
        {
            case "radius":
                style.Radius = ParseInteger(value, suffix: null, key, "number", offset);
                break;
            case "color":
                style.Color = ParseColor(value, key, offset);
                break;
            case "stroke-color":
                style.StrokeColor = ParseColor(value, key, offset);
                break;
            case "stroke-width":
                style.StrokeWidth = ParseInteger(value, suffix: "px", key, "number of pixels (eg. 10px)", offset);
                break;
            default:
                throw _context.Error(offset, $"style named {key} is not supported.");
        }
    }

    /// <summary>Splits a style on its colons and the white space around them, as <c>split(/\s*:\s*/)</c> in JavaScript.</summary>
    private static List<string> SplitStyle(string text)
    {
        var parts = new List<string>();
        var partStart = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] != ':')
                continue;

            var before = i;
            while (before > partStart && QuadrantChartLexer.IsWhiteSpace(text[before - 1]))
            {
                before--;
            }

            parts.Add(text[partStart..before]);
            var after = i + 1;
            while (after < text.Length && QuadrantChartLexer.IsWhiteSpace(text[after]))
            {
                after++;
            }

            partStart = after;
            i = after - 1;
        }

        parts.Add(text[partStart..]);
        return parts;
    }

    private int ParseInteger(string? value, string? suffix, string key, string type, int offset)
    {
        var digits = value;
        if (suffix is not null && digits is not null)
        {
            digits = digits.EndsWith(suffix, StringComparison.Ordinal) ? digits[..^suffix.Length] : null;
        }

        if (string.IsNullOrEmpty(digits) || !digits.All(char.IsAsciiDigit))
            throw InvalidStyle(key, value, type, offset);

        var trimmed = digits.TrimStart('0');
        if (trimmed.Length > 7 || (trimmed.Length > 0 && int.Parse(trimmed, CultureInfo.InvariantCulture) > 1_000_000))
            throw _context.Error(offset, $"The value of {key} {value} is too large: the maximum is 1000000.");

        return trimmed.Length is 0 ? 0 : int.Parse(trimmed, CultureInfo.InvariantCulture);
    }

    private SceneColor ParseColor(string? value, string key, int offset)
    {
        var hex = value is not null && value.StartsWith('#', StringComparison.Ordinal) ? value[1..] : value;
        if (hex is null || hex.Length is not (3 or 6) || !hex.All(char.IsAsciiHexDigit))
            throw InvalidStyle(key, value, "hex code", offset);

        // Mermaid also reads a color without '#', which the browsers do not draw: it is read as a hexadecimal color
        return SceneColor.Parse("#" + hex);
    }

    private MermaidParseException InvalidStyle(string key, string? value, string type, int offset) => _context.Error(offset, $"value for {key} {value ?? "undefined"} is invalid, please use a valid {type}");

    /// <summary>Reads a text: a string, a markdown string or a word, followed by words, spaces and dashes; the text is trimmed.</summary>
    private DiagramText ParseText()
    {
        var token = Current;
        var format = DiagramTextFormat.Plain;
        var builder = new StringBuilder();
        switch (token.Kind)
        {
            case QuadrantChartTokenKind.String:
                builder.Append(token.Value);
                break;
            case QuadrantChartTokenKind.MarkdownString:
                builder.Append(token.Value);
                format = DiagramTextFormat.Markdown;
                break;
            default:
                if (!token.IsAlphaNumeric)
                    throw Error($"A text is expected, not '{Describe(token)}'.");

                builder.Append(token.Value);
                break;
        }

        _index++;
        while (Current.IsText)
        {
            builder.Append(Current.Value);
            _index++;
        }

        return new DiagramText(QuadrantChartLexer.Trim(builder.ToString()), format);
    }

    private static DiagramText? ToLabel(DiagramText text) => text.IsEmpty ? null : text;

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "quadrantChart");
        var configuration = _context.Configuration;
        ReadNumber("chartWidth", 10_000, value => _diagram.Width = value);
        ReadNumber("chartHeight", 10_000, value => _diagram.Height = value);
        ReadNumber("quadrantPadding", 10_000, value => _diagram.QuadrantPadding = value);
        ReadNumber("quadrantTextTopPadding", 10_000, value => _diagram.QuadrantTextTopPadding = value);
        ReadNumber("quadrantLabelFontSize", 1_000, value => _diagram.QuadrantLabelFontSize = value);
        ReadNumber("quadrantInternalBorderStrokeWidth", 1_000, value => _diagram.InternalBorderWidth = value);
        ReadNumber("quadrantExternalBorderStrokeWidth", 1_000, value => _diagram.ExternalBorderWidth = value);
        ReadNumber("xAxisLabelPadding", 10_000, value => _diagram.XAxisLabelPadding = value);
        ReadNumber("xAxisLabelFontSize", 1_000, value => _diagram.XAxisLabelFontSize = value);
        ReadNumber("yAxisLabelPadding", 10_000, value => _diagram.YAxisLabelPadding = value);
        ReadNumber("yAxisLabelFontSize", 1_000, value => _diagram.YAxisLabelFontSize = value);
        ReadNumber("pointTextPadding", 10_000, value => _diagram.PointTextPadding = value);
        ReadNumber("pointLabelFontSize", 1_000, value => _diagram.PointLabelFontSize = value);
        ReadNumber("pointRadius", 10_000, value => _diagram.PointRadius = value);
        switch (configuration.GetString("quadrantChart", "xAxisPosition")?.Trim())
        {
            case null:
                break;
            case "top":
                _diagram.XAxisPosition = QuadrantChartXAxisPosition.Top;
                break;
            case "bottom":
                _diagram.XAxisPosition = QuadrantChartXAxisPosition.Bottom;
                break;
            case var position:
                _context.Warn(0, $"The x axis position '{position}' is not supported: use 'top' or 'bottom'.");
                break;
        }

        switch (configuration.GetString("quadrantChart", "yAxisPosition")?.Trim())
        {
            case null:
                break;
            case "left":
                _diagram.YAxisPosition = QuadrantChartYAxisPosition.Left;
                break;
            case "right":
                _diagram.YAxisPosition = QuadrantChartYAxisPosition.Right;
                break;
            case var position:
                _context.Warn(0, $"The y axis position '{position}' is not supported: use 'left' or 'right'.");
                break;
        }

        var colors = _diagram.Colors;
        ReadColor("quadrant1Fill", color => colors.Quadrant1Fill = color);
        ReadColor("quadrant2Fill", color => colors.Quadrant2Fill = color);
        ReadColor("quadrant3Fill", color => colors.Quadrant3Fill = color);
        ReadColor("quadrant4Fill", color => colors.Quadrant4Fill = color);
        ReadColor("quadrant1TextFill", color => colors.Quadrant1TextFill = color);
        ReadColor("quadrant2TextFill", color => colors.Quadrant2TextFill = color);
        ReadColor("quadrant3TextFill", color => colors.Quadrant3TextFill = color);
        ReadColor("quadrant4TextFill", color => colors.Quadrant4TextFill = color);
        ReadColor("quadrantPointFill", color => colors.PointFill = color);
        ReadColor("quadrantPointTextFill", color => colors.PointTextFill = color);
        ReadColor("quadrantXAxisTextFill", color => colors.XAxisTextFill = color);
        ReadColor("quadrantYAxisTextFill", color => colors.YAxisTextFill = color);
        ReadColor("quadrantInternalBorderStrokeFill", color => colors.InternalBorderStrokeFill = color);
        ReadColor("quadrantExternalBorderStrokeFill", color => colors.ExternalBorderStrokeFill = color);

        void ReadNumber(string name, double maximum, Action<double> set)
        {
            if (configuration.GetNumber("quadrantChart", name) is not { } value)
                return;

            if (value >= 0 && value <= maximum)
            {
                set(value);
            }
            else
            {
                _context.Warn(0, $"The configuration 'quadrantChart.{name}' must be between 0 and {maximum.ToString(CultureInfo.InvariantCulture)}: it is ignored.");
            }
        }

        void ReadColor(string name, Action<SceneColor> set)
        {
            if (configuration.GetString("themeVariables", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
            {
                set(color);
            }
        }
    }

    private MermaidParseException Unexpected() => Current.Kind is QuadrantChartTokenKind.Error ? Error(Current.Value) : Error($"Unexpected '{Describe(Current)}'.");

    private MermaidParseException Error(string message) => _context.Error(Current.Start, Current.Kind is QuadrantChartTokenKind.Error ? Current.Value : message);

    private static string Describe(QuadrantChartToken token) => token.Kind switch
    {
        QuadrantChartTokenKind.End => "end of text",
        QuadrantChartTokenKind.NewLine => "end of line",
        _ => token.Value.Length > 20 ? token.Value[..20] : token.Value,
    };
}
