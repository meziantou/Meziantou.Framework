namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The layout of an axis of an XY chart, ported from the axes of Mermaid: a band axis places categories at regular
/// intervals, a linear axis maps numbers. It measures the space of its line, ticks, labels and title, then draws them.
/// </summary>
internal sealed class XyChartAxisLayout
{
    private const double BarWidthToTickWidthRatio = 0.7;
    private const double MaxOuterPaddingPercentForLabel = 0.2;
    private const int TickCount = 10;

    private readonly XyChartAxis _options;
    private readonly string? _title;
    private readonly XyChartText _text;
    private readonly List<string>? _categories;
    private readonly Dictionary<string, int>? _categoryIndexes;
    private readonly double _domainStart;
    private readonly double _domainEnd;
    private readonly double _rotation;
    private double _rangeStart;
    private double _rangeEnd = 10;
    private double _x;
    private double _y;
    private double _width;
    private double _height;
    private double _outerPadding;
    private double _titleTextHeight;
    private bool _showTitle;
    private bool _showLabel;
    private bool _showTick;
    private bool _showAxisLine;

    // The scale: the position of the first category and the step between two categories, or the linear mapping
    private double _scaleStart;
    private double _scaleStep;
    private double _domainFrom;
    private double _domainTo;

    private XyChartAxisLayout(XyChartAxis options, string? title, XyChartText text, List<string>? categories, double domainStart, double domainEnd)
    {
        _options = options;
        _title = title;
        _text = text;
        _categories = categories;
        if (categories is not null)
        {
            // The scale of d3 keeps the first of the duplicate categories
            _categoryIndexes = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var category in categories)
            {
                _categoryIndexes.TryAdd(category, _categoryIndexes.Count);
            }
        }

        _domainStart = domainStart;
        _domainEnd = domainEnd;
        _rotation = options.LabelRotation * Math.PI / 180;
        Position = XyChartAxisPosition.Left;
        RecalculateScale();
    }

    public XyChartAxisPosition Position { get; private set; }

    public bool IsBand => _categories is not null;

    public static XyChartAxisLayout CreateBand(XyChartAxis options, string? title, XyChartText text, List<string> categories) => new(options, title, text, categories, 0, 0);

    public static XyChartAxisLayout CreateLinear(XyChartAxis options, string? title, XyChartText text, double start, double end) => new(options, title, text, categories: null, start, end);

    public void SetRange(double start, double end)
    {
        _rangeStart = start;
        _rangeEnd = end;
        if (Position is XyChartAxisPosition.Left)
        {
            _height = end - start;
        }
        else
        {
            _width = end - start;
        }

        RecalculateScale();
    }

    public void SetPosition(XyChartAxisPosition position)
    {
        Position = position;
        SetRange(_rangeStart, _rangeEnd);
    }

    public void SetLocation(double x, double y)
    {
        _x = x;
        _y = y;
    }

    public double OuterPadding => _outerPadding;

    private (double Start, double End) Range => (_rangeStart + _outerPadding, _rangeEnd - _outerPadding);

    public double GetTickDistance()
    {
        var (start, end) = Range;
        return Math.Abs(start - end) / GetTickCount();
    }

    private int GetTickCount() => _categories?.Count ?? GetLinearTicks().Count;

    public void RecalculateOuterPaddingToDrawBar()
    {
        if (BarWidthToTickWidthRatio * GetTickDistance() > _outerPadding * 2)
        {
            _outerPadding = Math.Floor(BarWidthToTickWidthRatio * GetTickDistance() / 2);
        }

        RecalculateScale();
    }

    /// <summary>Returns the position of a category of a band axis, or the start of the axis for an unknown one.</summary>
    public double GetPosition(string category)
    {
        if (_categoryIndexes is not null && _categoryIndexes.TryGetValue(category, out var index))
            return _scaleStart + (_scaleStep * index);

        return Range.Start;
    }

    /// <summary>Returns the position of a value of a linear axis.</summary>
    public double GetPosition(double value)
    {
        var (start, end) = Range;
        var span = _domainTo - _domainFrom;

        // d3 maps a domain of one value to the middle of the range
        var t = span != 0 ? (value - _domainFrom) / span : double.IsNaN(span) ? double.NaN : 0.5;
        return start + (t * (end - start));
    }

    private void RecalculateScale()
    {
        var (start, end) = Range;
        if (_categoryIndexes is not null)
        {
            // scaleBand().paddingInner(1).paddingOuter(0).align(0.5): the categories go from one end to the other
            var count = _categoryIndexes.Count;
            _scaleStep = (end - start) / Math.Max(1, count - 1);
            _scaleStart = start + ((end - start - (_scaleStep * (count - 1))) * 0.5);
            return;
        }

        // The y axis of SVG goes down: a vertical axis goes up from its first value
        (_domainFrom, _domainTo) = Position is XyChartAxisPosition.Left ? (_domainEnd, _domainStart) : (_domainStart, _domainEnd);
    }

    private List<double> GetLinearTicks() => XyChartNumbers.Ticks(_domainFrom, _domainTo, TickCount);

    /// <summary>Returns the labels of the ticks and their positions.</summary>
    private List<(string Label, double Position)> GetTicks()
    {
        if (_categories is not null)
            return _categories.ConvertAll(category => (category, GetPosition(category)));

        return GetLinearTicks().ConvertAll(tick => (XyChartNumbers.ToJavaScriptString(tick), GetPosition(tick)));
    }

    private (double Width, double Height) GetLabelDimension() => _text.MeasureMax(GetTicks().Select(tick => tick.Label), _options.LabelFontSize);

    public (double Width, double Height) CalculateSpace(double availableWidth, double availableHeight)
    {
        if (Position is XyChartAxisPosition.Left)
        {
            CalculateSpaceIfDrawnVertically(availableWidth, availableHeight);
        }
        else
        {
            CalculateSpaceIfDrawnHorizontally(availableWidth, availableHeight);
        }

        RecalculateScale();
        return (_width, _height);
    }

    private void CalculateSpaceIfDrawnHorizontally(double availableWidth, double availableSpaceHeight)
    {
        var availableHeight = availableSpaceHeight;
        if (_options.ShowLine && availableHeight > _options.LineWidth)
        {
            availableHeight -= _options.LineWidth;
            _showAxisLine = true;
        }

        if (_options.ShowLabels)
        {
            var (labelWidth, labelHeight) = GetLabelDimension();
            var maxPadding = MaxOuterPaddingPercentForLabel * availableWidth;
            _outerPadding = Math.Min(labelWidth / 2, maxPadding);
            var heightRequired = labelHeight;
            if (Position is XyChartAxisPosition.Bottom && _rotation != 0)
            {
                heightRequired = Math.Max(heightRequired, Math.Abs(Math.Sin(_rotation) * labelWidth) + Math.Abs(Math.Cos(_rotation) * labelHeight));
            }

            heightRequired += _options.LabelPadding * 2;
            if (heightRequired <= availableHeight)
            {
                availableHeight -= heightRequired;
                _showLabel = true;
            }
        }

        if (_options.ShowTicks && availableHeight >= _options.TickLength)
        {
            _showTick = true;
            availableHeight -= _options.TickLength;
        }

        if (_options.ShowTitle && !string.IsNullOrEmpty(_title))
        {
            var (_, titleHeight) = _text.MeasureMax([_title], _options.TitleFontSize);
            var heightRequired = titleHeight + (_options.TitlePadding * 2);
            _titleTextHeight = titleHeight;
            if (heightRequired <= availableHeight)
            {
                availableHeight -= heightRequired;
                _showTitle = true;
            }
        }

        _width = availableWidth;
        _height = availableSpaceHeight - availableHeight;
    }

    private void CalculateSpaceIfDrawnVertically(double availableSpaceWidth, double availableHeight)
    {
        var availableWidth = availableSpaceWidth;
        if (_options.ShowLine && availableWidth > _options.LineWidth)
        {
            availableWidth -= _options.LineWidth;
            _showAxisLine = true;
        }

        if (_options.ShowLabels)
        {
            var (labelWidth, labelHeight) = GetLabelDimension();
            var maxPadding = MaxOuterPaddingPercentForLabel * availableHeight;
            _outerPadding = Math.Min(labelHeight / 2, maxPadding);
            var widthRequired = labelWidth + (_options.LabelPadding * 2);
            if (widthRequired <= availableWidth)
            {
                availableWidth -= widthRequired;
                _showLabel = true;
            }
        }

        if (_options.ShowTicks && availableWidth >= _options.TickLength)
        {
            _showTick = true;
            availableWidth -= _options.TickLength;
        }

        if (_options.ShowTitle && !string.IsNullOrEmpty(_title))
        {
            var (_, titleHeight) = _text.MeasureMax([_title], _options.TitleFontSize);
            var widthRequired = titleHeight + (_options.TitlePadding * 2);
            _titleTextHeight = titleHeight;
            if (widthRequired <= availableWidth)
            {
                availableWidth -= widthRequired;
                _showTitle = true;
            }
        }

        _width = availableSpaceWidth - availableWidth;
        _height = availableHeight;
    }

    public void Draw(SceneGroup group, XyChartAxisColors colors)
    {
        switch (Position)
        {
            case XyChartAxisPosition.Left:
                DrawLeft(group.Add(new SceneGroup { CssClass = "left-axis" }), colors);
                break;
            case XyChartAxisPosition.Bottom:
                DrawBottom(group.Add(new SceneGroup { CssClass = "bottom-axis" }), colors);
                break;
            default:
                DrawTop(group.Add(new SceneGroup { CssClass = "top-axis" }), colors);
                break;
        }
    }

    private double LineWidth => _showAxisLine ? _options.LineWidth : 0;

    private double TickLength => _showTick ? _options.TickLength : 0;

    private void DrawLeft(SceneGroup group, XyChartAxisColors colors)
    {
        if (_showAxisLine)
        {
            var x = _x + _width - (_options.LineWidth / 2);
            group.Add(Line(x, _y, x, _y + _height, colors.Line, _options.LineWidth, "axis-line"));
        }

        var ticks = GetTicks();
        if (_showLabel)
        {
            var labels = group.Add(new SceneGroup { CssClass = "label" });
            var x = _x + _width - _options.LabelPadding - TickLength - LineWidth;
            foreach (var (label, position) in ticks)
            {
                _text.Draw(labels, label, x, position, _options.LabelFontSize, colors.Label, XyChartTextBaseline.Middle, SceneTextAnchor.End, rotation: 0);
            }
        }

        if (_showTick)
        {
            var tickGroup = group.Add(new SceneGroup { CssClass = "ticks" });
            var x = _x + _width - LineWidth;
            foreach (var (_, position) in ticks)
            {
                tickGroup.Add(Line(x, position, x - _options.TickLength, position, colors.Tick, _options.TickWidth, cssClass: null));
            }
        }

        if (_showTitle)
        {
            var title = group.Add(new SceneGroup { CssClass = "title" });
            _text.Draw(title, _title!, _x + _options.TitlePadding, _y + (_height / 2), _options.TitleFontSize, colors.Title, XyChartTextBaseline.Top, SceneTextAnchor.Middle, rotation: 270);
        }
    }

    private void DrawBottom(SceneGroup group, XyChartAxisColors colors)
    {
        if (_showAxisLine)
        {
            var y = _y + (_options.LineWidth / 2);
            group.Add(Line(_x, y, _x + _width, y, colors.Line, _options.LineWidth, "axis-line"));
        }

        var ticks = GetTicks();
        if (_showLabel)
        {
            var labels = group.Add(new SceneGroup { CssClass = "label" });
            var (labelWidth, labelHeight) = _rotation != 0 ? GetLabelDimension() : default;
            var offsetX = Math.Sin(_rotation) * labelHeight / 2;
            var offsetY = Math.Abs(Math.Sin(_rotation) * labelWidth / 2);
            var y = _y + _options.LabelPadding + TickLength + LineWidth + offsetY;
            foreach (var (label, position) in ticks)
            {
                _text.Draw(labels, label, position + offsetX, y, _options.LabelFontSize, colors.Label, XyChartTextBaseline.Top, SceneTextAnchor.Middle, _options.LabelRotation);
            }
        }

        if (_showTick)
        {
            var tickGroup = group.Add(new SceneGroup { CssClass = "ticks" });
            var y = _y + LineWidth;
            foreach (var (_, position) in ticks)
            {
                tickGroup.Add(Line(position, y, position, y + _options.TickLength, colors.Tick, _options.TickWidth, cssClass: null));
            }
        }

        if (_showTitle)
        {
            var title = group.Add(new SceneGroup { CssClass = "title" });
            var y = _y + _height - _options.TitlePadding - _titleTextHeight;
            _text.Draw(title, _title!, _rangeStart + ((_rangeEnd - _rangeStart) / 2), y, _options.TitleFontSize, colors.Title, XyChartTextBaseline.Top, SceneTextAnchor.Middle, rotation: 0);
        }
    }

    private void DrawTop(SceneGroup group, XyChartAxisColors colors)
    {
        if (_showAxisLine)
        {
            var y = _y + _height - (_options.LineWidth / 2);
            group.Add(Line(_x, y, _x + _width, y, colors.Line, _options.LineWidth, "axis-line"));
        }

        var ticks = GetTicks();
        if (_showLabel)
        {
            var labels = group.Add(new SceneGroup { CssClass = "label" });
            var y = _y + (_showTitle ? _titleTextHeight + (_options.TitlePadding * 2) : 0) + _options.LabelPadding;
            foreach (var (label, position) in ticks)
            {
                _text.Draw(labels, label, position, y, _options.LabelFontSize, colors.Label, XyChartTextBaseline.Top, SceneTextAnchor.Middle, rotation: 0);
            }
        }

        if (_showTick)
        {
            var tickGroup = group.Add(new SceneGroup { CssClass = "ticks" });
            var bottom = _y + _height - LineWidth;
            foreach (var (_, position) in ticks)
            {
                tickGroup.Add(Line(position, bottom, position, bottom - _options.TickLength, colors.Tick, _options.TickWidth, cssClass: null));
            }
        }

        if (_showTitle)
        {
            var title = group.Add(new SceneGroup { CssClass = "title" });
            _text.Draw(title, _title!, _x + (_width / 2), _y + _options.TitlePadding, _options.TitleFontSize, colors.Title, XyChartTextBaseline.Top, SceneTextAnchor.Middle, rotation: 0);
        }
    }

    private static SceneLine Line(double x1, double y1, double x2, double y2, SceneColor color, double width, string? cssClass) => new(x1, y1, x2, y2) { Stroke = color, StrokeWidth = width, CssClass = cssClass };
}
