using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Mermaid.Internals.XyChartDiagrams;

/// <summary>
/// Parses the <c>xychart</c> diagrams of Mermaid with the grammar of <c>xychart.jison</c>: the title, the axes, and the
/// <c>bar</c> and <c>line</c> series, in any order. A text is a word, or several pieces that are joined without their
/// spaces, or a quoted text.
/// </summary>
internal sealed class XyChartDiagramParser
{
    private readonly ParseContext _context;
    private readonly XyChartLexer _lexer;
    private readonly XyChart _diagram = new();
    private XyChartToken _token;
    private bool _warnedBarLabels;

    private XyChartDiagramParser(ParseContext context, int position)
    {
        _context = context;
        _lexer = new XyChartLexer(context, position);
    }

    public static XyChart Parse(ParseContext context, int position) => new XyChartDiagramParser(context, position).Parse();

    private XyChart Parse()
    {
        ApplyConfiguration();
        Advance();

        // start: the line feeds, and the keyword with its orientation, can repeat before the statements
        while (true)
        {
            if (IsEndOfStatement(_token.Kind))
            {
                Advance();
                continue;
            }

            if (_token.Kind is XyChartTokenKind.XyChart)
            {
                Advance();
                if (_token.Kind is XyChartTokenKind.Orientation)
                {
                    _diagram.Orientation = _token.Text.Equals("horizontal", StringComparison.OrdinalIgnoreCase) ? XyChartOrientation.Horizontal : XyChartOrientation.Vertical;
                    Advance();
                }

                continue;
            }

            break;
        }

        while (_token.Kind is not XyChartTokenKind.EndOfFile)
        {
            ParseStatement();
            while (IsEndOfStatement(_token.Kind))
            {
                Advance();
            }
        }

        return _diagram;
    }

    private static bool IsEndOfStatement(XyChartTokenKind kind) => kind is XyChartTokenKind.NewLine or XyChartTokenKind.Semicolon;

    private void ParseStatement()
    {
        var token = _token;
        switch (token.Kind)
        {
            case XyChartTokenKind.Title:
                Advance();
                var title = ParseText(required: true)!;
                _diagram.Title = title.Length is 0 ? null : title;
                break;
            case XyChartTokenKind.XAxis:
                Advance();
                ParseAxis(_diagram.XAxis, allowCategories: true);
                break;
            case XyChartTokenKind.YAxis:
                Advance();
                ParseAxis(_diagram.YAxis, allowCategories: false);
                break;
            case XyChartTokenKind.Line or XyChartTokenKind.Bar:
                Advance();
                ParseSeries(token);
                break;
            case XyChartTokenKind.AccessibleTitle:
                Advance();
                _diagram.AccessibleTitle = XyChartStrings.Trim(token.Text);
                break;
            case XyChartTokenKind.AccessibleDescription:
                Advance();

                // As the other diagrams read them, the lines of a block are trimmed and its %% comments removed
                _diagram.Description = string.Join('\n', XyChartStrings.Trim(token.Text).Split('\n').Select(XyChartStrings.Trim).Where(line => !line.StartsWith("%%", StringComparison.Ordinal)));
                break;
            default:
                throw Unexpected("a statement");
        }
    }

    /// <summary>Reads <c>x-axis</c> or <c>y-axis</c>: a title, values, or both.</summary>
    private void ParseAxis(XyChartAxis axis, bool allowCategories)
    {
        var title = IsTextStart(_token.Kind) ? ParseText(required: true) : null;
        if (allowCategories && _token.Kind is XyChartTokenKind.OpenBracket)
        {
            var start = _token.Start;
            Advance();
            var categories = new List<string> { ParseText(required: true)! };
            while (_token.Kind is XyChartTokenKind.Comma)
            {
                Advance();
                categories.Add(ParseText(required: true)!);
                if (categories.Count > _context.Options.MaxNodes)
                    throw _context.Error(start, $"The axis has more than {_context.Options.MaxNodes} categories.");
            }

            Expect(XyChartTokenKind.CloseBracket, "',' or ']'");
            axis.Categories.Clear();
            foreach (var category in categories)
            {
                axis.Categories.Add(category);
            }

            axis.Range = null;
        }
        else if (_token.Kind is XyChartTokenKind.Number)
        {
            var rangeStart = ParseNumber();
            Expect(XyChartTokenKind.Arrow, "'-->'");
            if (_token.Kind is not XyChartTokenKind.Number)
                throw Unexpected("a number");

            var rangeEnd = ParseNumber();
            axis.Categories.Clear();
            axis.Range = new XyChartAxisRange(rangeStart, rangeEnd);
        }
        else if (title is null)
        {
            throw Unexpected(allowCategories ? "a title, '[' or a number" : "a title or a number");
        }

        // A statement without title removes the title
        axis.Title = string.IsNullOrEmpty(title) ? null : title;
    }

    /// <summary>Reads <c>bar</c> or <c>line</c>: an optional name, then the values in brackets.</summary>
    private void ParseSeries(XyChartToken keyword)
    {
        var name = IsTextStart(_token.Kind) ? ParseText(required: true) : null;
        XyChartSeries series = keyword.Kind is XyChartTokenKind.Line ? new XyChartLineSeries() : new XyChartBarSeries();
        series.Name = string.IsNullOrEmpty(name) ? null : name;
        Expect(XyChartTokenKind.OpenBracket, "'['");
        var labels = new List<string?>();
        while (true)
        {
            if (_token.Kind is not XyChartTokenKind.Number)
                throw Unexpected("a number");

            series.Values.Add(ParseNumber());
            string? label = null;
            if (_token.Kind is XyChartTokenKind.String)
            {
                label = XyChartStrings.Decode(XyChartStrings.Trim(_token.Text));
                Advance();
            }

            labels.Add(label?.Length is 0 ? null : label);
            if (series.Values.Count > _context.Options.MaxNodes)
                throw _context.Error(keyword.Start, $"The series has more than {_context.Options.MaxNodes} values.");

            if (_token.Kind is not XyChartTokenKind.Comma)
                break;

            Advance();
        }

        Expect(XyChartTokenKind.CloseBracket, "',' or ']'");
        if (labels.Exists(label => label is not null))
        {
            if (series is XyChartLineSeries line)
            {
                foreach (var label in labels)
                {
                    line.PointLabels.Add(label);
                }
            }
            else if (!_warnedBarLabels)
            {
                // Mermaid reads the labels of the bars, but draws only those of the lines
                _warnedBarLabels = true;
                _context.Warn(keyword.Start, "The labels of the values of a bar series are not drawn: they are ignored.");
            }
        }

        if (_diagram.Series.Count >= _context.Options.MaxNodes)
            throw _context.Error(keyword.Start, $"The chart has more than {_context.Options.MaxNodes} series.");

        _diagram.Series.Add(series);
    }

    private static bool IsTextStart(XyChartTokenKind kind) => kind is XyChartTokenKind.String or XyChartTokenKind.MarkdownString || IsWordPart(kind);

    private static bool IsWordPart(XyChartTokenKind kind) => kind is XyChartTokenKind.Alpha or XyChartTokenKind.Digits or XyChartTokenKind.Ampersand or XyChartTokenKind.Plus
        or XyChartTokenKind.Equals or XyChartTokenKind.Multiply or XyChartTokenKind.Dot or XyChartTokenKind.Hash or XyChartTokenKind.Minus or XyChartTokenKind.Underscore;

    /// <summary>Reads a text: a quoted text, a markdown text, or the pieces of a word, joined without the spaces between them.</summary>
    private string? ParseText(bool required)
    {
        if (_token.Kind is XyChartTokenKind.String or XyChartTokenKind.MarkdownString)
        {
            var value = XyChartStrings.Decode(XyChartStrings.Trim(_token.Text));
            Advance();
            return value;
        }

        if (!IsWordPart(_token.Kind))
            return required ? throw Unexpected("a text") : null;

        var builder = new StringBuilder();
        while (IsWordPart(_token.Kind))
        {
            builder.Append(_token.Text);
            Advance();
        }

        return XyChartStrings.Trim(builder.ToString());
    }

    private double ParseNumber()
    {
        var token = _token;
        if (!double.TryParse(token.Text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            throw _context.Error(token.Start, $"The number '{token.Text}' is too large.");

        Advance();
        return value;
    }

    private void Expect(XyChartTokenKind kind, string expected)
    {
        if (_token.Kind != kind)
            throw Unexpected(expected);

        Advance();
    }

    private void Advance() => _token = _lexer.Next();

    private MermaidParseException Unexpected(string expected)
    {
        var found = _token.Kind switch
        {
            XyChartTokenKind.EndOfFile => "the end of the text",
            XyChartTokenKind.NewLine => "the end of the line",
            XyChartTokenKind.String or XyChartTokenKind.MarkdownString => "a quoted text",
            _ => "'" + _token.Text + "'",
        };

        return _context.Error(_token.Start, $"Unexpected {found}: {expected} is expected.");
    }

    private void ApplyConfiguration()
    {
        MermaidCommon.ApplyCommonConfiguration(_diagram, _context, "xyChart");
        var configuration = _context.Configuration;
        if (configuration.GetString("xyChart", "chartOrientation") is { } orientation)
        {
            _diagram.Orientation = orientation.Trim() is "horizontal" ? XyChartOrientation.Horizontal : XyChartOrientation.Vertical;
        }

        ReadNumber("width", 1, 100_000, value => _diagram.Width = value);
        ReadNumber("height", 1, 100_000, value => _diagram.Height = value);
        ReadNumber("titleFontSize", 1, 1000, value => _diagram.TitleFontSize = value);
        ReadNumber("titlePadding", 0, 1000, value => _diagram.TitlePadding = value);
        ReadNumber("legendFontSize", 1, 1000, value => _diagram.LegendFontSize = value);
        ReadNumber("legendPadding", 0, 1000, value => _diagram.LegendPadding = value);
        ReadNumber("plotReservedSpacePercent", 1, 100, value => _diagram.PlotReservedSpacePercent = value);
        ReadBoolean(["xyChart", "showTitle"], value => _diagram.ShowTitle = value);
        ReadBoolean(["xyChart", "showLegend"], value => _diagram.ShowLegend = value);
        ReadBoolean(["xyChart", "showDataLabel"], value => _diagram.ShowDataLabels = value);
        ReadBoolean(["xyChart", "showDataLabelOutsideBar"], value => _diagram.DataLabelsOutsideBars = value);
        ReadAxisConfiguration("xAxis", _diagram.XAxis);
        ReadAxisConfiguration("yAxis", _diagram.YAxis);

        ReadColor("backgroundColor", value => _diagram.BackgroundColor = value);
        ReadColor("titleColor", value => _diagram.TitleColor = value);
        ReadColor("dataLabelColor", value => _diagram.DataLabelColor = value);
        ReadColor("legendTextColor", value => _diagram.LegendTextColor = value);
        foreach (var (prefix, axis) in new[] { ("xAxis", _diagram.XAxis), ("yAxis", _diagram.YAxis) })
        {
            ReadColor(prefix + "TitleColor", value => axis.TitleColor = value);
            ReadColor(prefix + "LabelColor", value => axis.LabelColor = value);
            ReadColor(prefix + "TickColor", value => axis.TickColor = value);
            ReadColor(prefix + "LineColor", value => axis.LineColor = value);
        }

        if (configuration.GetString("themeVariables", "xyChart", "plotColorPalette") is { } palette)
        {
            var colors = new List<SceneColor>();
            foreach (var item in palette.Split(','))
            {
                if (!SceneColor.TryParse(item.Trim(), out var color))
                {
                    _context.Warn(0, $"The color '{item.Trim()}' of 'plotColorPalette' is not valid: the palette is ignored.");
                    colors.Clear();
                    break;
                }

                colors.Add(color);
            }

            foreach (var color in colors)
            {
                _diagram.PlotColors.Add(color);
            }
        }
    }

    private void ReadAxisConfiguration(string name, XyChartAxis axis)
    {
        ReadNumber([name, "labelFontSize"], 1, 1000, value => axis.LabelFontSize = value);
        ReadNumber([name, "labelPadding"], 0, 1000, value => axis.LabelPadding = value);
        ReadNumber([name, "titleFontSize"], 1, 1000, value => axis.TitleFontSize = value);
        ReadNumber([name, "titlePadding"], 0, 1000, value => axis.TitlePadding = value);
        ReadNumber([name, "tickLength"], 0, 1000, value => axis.TickLength = value);
        ReadNumber([name, "tickWidth"], 0, 1000, value => axis.TickWidth = value);
        ReadNumber([name, "axisLineWidth"], 0, 1000, value => axis.LineWidth = value);

        // Mermaid draws the labels straight when their rotation is out of range
        ReadNumber([name, "labelRotation"], -90, 90, value => axis.LabelRotation = value, warn: false);
        ReadBoolean(["xyChart", name, "showLabel"], value => axis.ShowLabels = value);
        ReadBoolean(["xyChart", name, "showTitle"], value => axis.ShowTitle = value);
        ReadBoolean(["xyChart", name, "showTick"], value => axis.ShowTicks = value);
        ReadBoolean(["xyChart", name, "showAxisLine"], value => axis.ShowLine = value);
    }

    private void ReadNumber(string name, double minimum, double maximum, Action<double> set) => ReadNumber([name], minimum, maximum, set);

    private void ReadNumber(string[] path, double minimum, double maximum, Action<double> set, bool warn = true)
    {
        string[] fullPath = ["xyChart", .. path];
        if (_context.Configuration.Get(fullPath) is null)
            return;

        if (_context.Configuration.GetNumber(fullPath) is { } value && value >= minimum && value <= maximum)
        {
            set(value);
        }
        else if (warn)
        {
            _context.Warn(0, $"The configuration 'xyChart.{string.Join('.', path)}' must be a number between {minimum.ToString(CultureInfo.InvariantCulture)} and {maximum.ToString(CultureInfo.InvariantCulture)}: it is ignored.");
        }
    }

    private void ReadBoolean(string[] path, Action<bool> set)
    {
        if (_context.Configuration.GetBoolean(path) is { } value)
        {
            set(value);
        }
    }

    private void ReadColor(string name, Action<SceneColor> set)
    {
        if (_context.Configuration.GetString("themeVariables", "xyChart", name) is { } value && SceneColor.TryParse(value.Trim(), out var color))
        {
            set(color);
        }
    }
}
