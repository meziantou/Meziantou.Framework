namespace Meziantou.Framework.Charts;

/// <summary>Lays a <see cref="Plot"/> out into a <see cref="ChartScene"/>: resolves the scales, measures the titles, the legend and the axes, computes the plot area, then draws the grid, the marks, the axes and the legend.</summary>
internal sealed class PlotRenderer
{
    private const double Padding = 10;
    private const double TickLabelGap = 3;
    private const double AxisLabelGap = 6;

    private readonly Plot _plot;
    private readonly ITextMeasurer _measurer;
    private readonly ChartTheme _theme;
    private readonly List<(Mark Mark, MarkData Data)> _marks = [];
    private readonly MarkTraits _traits;
    private readonly ResolvedScale? _x;
    private readonly ResolvedScale? _y;
    private readonly ResolvedColorScale? _color;
    private readonly ResolvedSizeScale? _size;
    private readonly Coordinates _coordinates;
    private readonly ChartScene _scene;

    private PlotRenderer(Plot plot, ITextMeasurer measurer)
    {
        _plot = plot;
        _measurer = measurer;
        _theme = plot.Theme;
        foreach (var mark in plot.Marks)
        {
            var data = mark.Prepare();
            _marks.Add((mark, data));
            _traits |= mark.GetTraits(data);
        }

        _coordinates = plot.Coordinates ?? (_traits.HasFlag(MarkTraits.PreferPolar) ? new PolarCoordinates() : new CartesianCoordinates());
        var frameOnly = _marks.Count > 0 && _marks.All(mark => mark.Mark.GetTraits(mark.Data).HasFlag(MarkTraits.Frame));
        if (!frameOnly)
        {
            _x = ScaleResolver.ResolvePosition(plot.XScale, Collect(data => [data.X, data.X1, data.X2, .. data.ExtraX]), _traits.HasFlag(MarkTraits.BandX), _traits.HasFlag(MarkTraits.ZeroX), _traits.HasFlag(MarkTraits.ExactX));
            _y = ScaleResolver.ResolvePosition(plot.YScale, Collect(data => [data.Y, data.Y1, data.Y2, .. data.ExtraY]), _traits.HasFlag(MarkTraits.BandY), _traits.HasFlag(MarkTraits.ZeroY), _traits.HasFlag(MarkTraits.ExactY));
        }

        _color = ScaleResolver.ResolveColor(plot.ColorScale, Collect(data => [data.Fill, data.Stroke, .. data.ExtraColor]), _theme);
        _size = ScaleResolver.ResolveSize(plot.SizeScale, Collect(data => [data.Size]));
        _scene = new ChartScene(plot.Width, plot.Height, _theme.FontFamily ?? measurer.FontFamily)
        {
            Title = plot.Title,
            Description = plot.Description,
            Background = _theme.Background,
        };
    }

    private ChartFont TickFont => new(_theme.TickFontSize);

    private ChartFont LabelFont => new(_theme.FontSize);

    public static ChartScene Render(Plot plot, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        return new PlotRenderer(plot, measurer).Render();
    }

    private ChartScene Render()
    {
        var width = _plot.Width;
        var height = _plot.Height;
        var top = Padding;
        var bottom = Padding;
        var right = Padding;
        var titles = new List<SceneText>();
        top = AddTitle(titles, _plot.Title, new ChartFont(_theme.TitleFontSize, Bold: true), _theme.Foreground, top);
        top = AddTitle(titles, _plot.Subtitle, new ChartFont(_theme.SubtitleFontSize), _theme.MutedForeground, top);
        if (titles.Count > 0)
        {
            top += 6;
        }

        // The legend takes its space before the plot area
        LegendRenderer? legend = null;
        var legendPosition = (X: 0.0, Y: 0.0);
        if (_color is not null && (_plot.Legend.Visible ?? LegendRenderer.IsVisibleByDefault(_color)))
        {
            legend = new LegendRenderer(_color, _plot.Legend.Title, _theme, _measurer);
            switch (_plot.Legend.Position)
            {
                case LegendPosition.Right:
                    legend.Measure(Math.Max(60, Math.Min(180, width / 4)), vertical: true);
                    legendPosition = (width - Padding - legend.Width, top);
                    right += legend.Width + 12;
                    break;
                case LegendPosition.Bottom:
                    legend.Measure(width - (2 * Padding), vertical: false);
                    legendPosition = (Padding, height - Padding - legend.Height);
                    bottom += legend.Height + 10;
                    break;
                default:
                    legend.Measure(width - (2 * Padding), vertical: false);
                    legendPosition = (Padding, top);
                    top += legend.Height + 12;
                    break;
            }
        }

        var area = new ChartRectangle(Padding, top, Math.Max(1, width - Padding - right), Math.Max(1, height - top - bottom));
        var grid = new SceneGroup { CssClass = "grid" };
        var axes = new SceneGroup { CssClass = "axes" };
        var marks = new SceneGroup { CssClass = "marks" };
        var facets = FacetGrid.Create(_marks.Select(mark => mark.Data), _plot.Facets);

        // The scales are mapped to the frame of a chart of the grid before the chart is drawn
        void RenderCell(FacetCell cell, CoordinateSystem system, ChartRectangle frame)
        {
            var context = new RenderContext(_theme, _measurer, _x, _y, _color, _size, system, frame);
            var target = marks;
            if (facets.IsFaceted)
            {
                target = marks.Add(new SceneGroup { CssClass = "facet", Clip = _plot.Clip ? frame : null });
            }
            else
            {
                marks.Clip = _plot.Clip ? frame : null;
            }

            foreach (var (mark, data) in _marks)
            {
                var group = new SceneGroup { CssClass = mark.CssClass, Opacity = mark.Opacity };
                mark.Render(context, data.ForFacet(cell.XKey, cell.YKey), group);
                target.Add(group);
            }
        }

        if (_coordinates is PolarCoordinates polar)
        {
            LayoutPolarCells(polar, area, facets, grid, axes, RenderCell);
        }
        else
        {
            LayoutCartesianCells(area, facets, grid, axes, RenderCell);
        }

        if (grid.Children.Count > 0)
        {
            _scene.Root.Add(grid);
        }

        _scene.Root.Add(marks);
        if (axes.Children.Count > 0)
        {
            _scene.Root.Add(axes);
        }

        legend?.Render(_scene.Root, legendPosition.X, legendPosition.Y);
        foreach (var title in titles)
        {
            _scene.Root.Add(title);
        }

        return _scene;
    }

    private double AddTitle(List<SceneText> titles, string? text, ChartFont font, ChartColor color, double top)
    {
        if (string.IsNullOrEmpty(text))
            return top;

        var metrics = _measurer.Measure(text, font);
        titles.Add(new SceneText(Padding, top + metrics.Ascent, text, font, color) { MeasuredWidth = metrics.Width });
        return top + metrics.Height + 2;
    }

    private ChartRectangle FromMargin(ChartMargin margin) => new(margin.Left, margin.Top, Math.Max(1, _plot.Width - margin.Left - margin.Right), Math.Max(1, _plot.Height - margin.Top - margin.Bottom));

    private ChartFont HeaderFont => new(_theme.FontSize, Bold: true);

    private void LayoutCartesianCells(ChartRectangle area, FacetGrid facets, SceneGroup grid, SceneGroup axes, Action<FacetCell, CoordinateSystem, ChartRectangle> renderCell)
    {
        var transposed = _coordinates.Transposed;
        var vertical = transposed ? _x : _y;
        var horizontal = transposed ? _y : _x;
        var verticalAxis = transposed ? _plot.XAxis : _plot.YAxis;
        var horizontalAxis = transposed ? _plot.YAxis : _plot.XAxis;
        var verticalVisible = vertical is not null && (verticalAxis.Visible ?? !HidesAxis(isX: transposed));
        var horizontalVisible = horizontal is not null && (horizontalAxis.Visible ?? !HidesAxis(isX: !transposed));
        var gapX = facets.IsFaceted ? _plot.Facets.Gap : 0;
        var gapY = gapX;
        var headerHeight = facets.HasHeaders ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;
        var rowLabelWidth = facets.HasRowLabels ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;

        var verticalWidth = 0.0;
        var horizontalHeight = 0.0;
        var extraLeft = 0.0;
        var extraRight = 0.0;
        HorizontalAxisLayout? horizontalLayout = null;
        var gridArea = area;
        for (var iteration = 0; iteration < 3; iteration++)
        {
            gridArea = _plot.Margin is { } margin
                ? FromMargin(margin)
                : new ChartRectangle(area.X + verticalWidth + extraLeft, area.Y, Math.Max(1, area.Width - verticalWidth - extraLeft - extraRight - rowLabelWidth), Math.Max(1, area.Height - horizontalHeight));

            var frame = GetCellFrame(gridArea, facets, facets.Cells[0], gapX, gapY, headerHeight);
            Configure(vertical, frame.Bottom, frame.Y, frame.Height / 40);
            Configure(horizontal, frame.X, frame.Right, frame.Width / 80);

            verticalWidth = 0;
            if (verticalVisible)
            {
                var verticalLabels = MeasureVerticalLabels(vertical!);
                var labelsWidth = verticalLabels.Count is 0 ? 0 : verticalLabels.Max(label => label.Metrics.Width);
                verticalWidth = labelsWidth + verticalAxis.TickSize + TickLabelGap + 2;

                // The labels at the ends of two charts one above the other must not overlap
                if (facets.IsFaceted && verticalLabels.Count > 0)
                {
                    gapY = Math.Max(_plot.Facets.Gap, verticalLabels.Max(label => label.Metrics.Height) + 2);
                }
                if (verticalAxis.Label is { Length: > 0 } label)
                {
                    verticalWidth += _measurer.Measure(label, LabelFont).Height + AxisLabelGap;
                }
            }

            horizontalHeight = 0;
            extraLeft = 0;
            extraRight = 0;
            if (horizontalVisible)
            {
                horizontalLayout = CreateHorizontalLayout(horizontal!, horizontalAxis);
                horizontalHeight = horizontalAxis.TickSize + TickLabelGap + horizontalLayout.LabelBlockHeight + 2;
                if (horizontalAxis.Label is { Length: > 0 } label)
                {
                    horizontalHeight += _measurer.Measure(label, LabelFont).Height + AxisLabelGap;
                }

                if (horizontalLayout.Labels.Count > 0)
                {
                    // The labels at the ends must not go past the edges of the chart. Their offsets from the edges of
                    // the frame do not change from one iteration to the next.
                    var first = horizontalLayout.Labels.MinBy(label => label.Position);
                    var last = horizontalLayout.Labels.MaxBy(label => label.Position);
                    var leftExtent = horizontalLayout.Rotation == 0 ? first.Metrics.Width / 2 : first.Metrics.Width * Math.Cos(horizontalLayout.Rotation * Math.PI / 180);
                    var requiredLeft = 2 + leftExtent - (first.Position - frame.X);
                    extraLeft = Math.Max(0, requiredLeft - (area.X + verticalWidth));
                    var rightExtent = 0.0;
                    if (horizontalLayout.Rotation == 0)
                    {
                        var requiredRight = _plot.Width - 2 - rowLabelWidth - (last.Metrics.Width / 2) - (last.Position - frame.Right);
                        extraRight = Math.Max(0, area.Right - rowLabelWidth - requiredRight);
                        rightExtent = (last.Metrics.Width / 2) - (frame.Right - last.Position);
                    }

                    // The labels at the ends of two charts side by side must not overlap
                    if (facets.IsFaceted)
                    {
                        gapX = Math.Max(_plot.Facets.Gap, Math.Max(0, leftExtent - (first.Position - frame.X)) + Math.Max(0, rightExtent) + 6);
                    }
                }
            }
        }

        var cartesian = new CartesianSystem(transposed);
        var single = !facets.IsFaceted;
        foreach (var cell in facets.Cells)
        {
            var frame = GetCellFrame(gridArea, facets, cell, gapX, gapY, headerHeight);
            Configure(vertical, frame.Bottom, frame.Y, frame.Height / 40);
            Configure(horizontal, frame.X, frame.Right, frame.Width / 80);
            if (verticalVisible)
            {
                RenderVerticalAxis(vertical!, verticalAxis, MeasureVerticalLabels(vertical!), frame, grid, axes, isYScale: !transposed, drawLabels: cell.IsLeft, titleArea: single ? frame : null);
            }

            if (horizontalVisible)
            {
                horizontalLayout = CreateHorizontalLayout(horizontal!, horizontalAxis);
                RenderHorizontalAxis(horizontal!, horizontalAxis, horizontalLayout, frame, grid, axes, isYScale: transposed, drawLabels: cell.IsBottom, titleArea: single ? frame : null);
            }

            RenderFacetLabels(facets, cell, frame, axes);
            renderCell(cell, cartesian, frame);
        }

        if (!single)
        {
            // The titles of the axes, once for the grid
            var titleArea = new ChartRectangle(gridArea.X, gridArea.Y + headerHeight, gridArea.Width, gridArea.Height - headerHeight);
            if (verticalVisible)
            {
                RenderVerticalTitle(verticalAxis, titleArea, axes);
            }

            if (horizontalVisible)
            {
                RenderHorizontalTitle(horizontalAxis, horizontalLayout!, titleArea, axes);
            }
        }
    }

    private static ChartRectangle GetCellFrame(ChartRectangle gridArea, FacetGrid facets, FacetCell cell, double gapX, double gapY, double headerHeight)
    {
        var headers = facets.Wraps ? facets.Rows * headerHeight : headerHeight;
        var width = Math.Max(1, (gridArea.Width - (gapX * (facets.Columns - 1))) / facets.Columns);
        var height = Math.Max(1, (gridArea.Height - headers - (gapY * (facets.Rows - 1))) / facets.Rows);
        var x = gridArea.X + (cell.Column * (width + gapX));
        var y = facets.Wraps
            ? gridArea.Y + (cell.Row * (height + gapY + headerHeight)) + headerHeight
            : gridArea.Y + headerHeight + (cell.Row * (height + gapY));
        return new ChartRectangle(x, y, width, height);
    }

    private void RenderFacetLabels(FacetGrid facets, FacetCell cell, ChartRectangle frame, SceneGroup axes)
    {
        var font = HeaderFont;
        if (cell.HasHeader && cell.XKey is { } header)
        {
            var text = TextFitting.Truncate(_measurer, header, font, frame.Width) ?? TextFitting.Ellipsis;
            var metrics = _measurer.Measure(text, font);
            axes.Add(new SceneText(frame.CenterX, frame.Y - 6 - metrics.Descent, text, font, _theme.Foreground) { Anchor = SceneTextAnchor.Middle, MeasuredWidth = metrics.Width, CssClass = "facet-header" });
        }

        if (facets.HasRowLabels && cell.IsRight && cell.YKey is { } rowLabel)
        {
            var text = TextFitting.Truncate(_measurer, rowLabel, font, frame.Height) ?? TextFitting.Ellipsis;
            var metrics = _measurer.Measure(text, font);
            axes.Add(new SceneText(frame.Right + 6, frame.CenterY, text, font, _theme.Foreground) { Anchor = SceneTextAnchor.Middle, Rotation = 90, MeasuredWidth = metrics.Width, CssClass = "facet-header" });
        }
    }

    private List<AxisLabel> MeasureVerticalLabels(ResolvedScale scale)
    {
        var maxWidth = Math.Max(40, _plot.Width * 0.3);
        var labels = new List<AxisLabel>();
        foreach (var tick in scale.GetTicks())
        {
            var text = TextFitting.Truncate(_measurer, tick.Label, TickFont, maxWidth) ?? TextFitting.Ellipsis;
            labels.Add(new AxisLabel(tick.Position, text, _measurer.Measure(text, TickFont)));
        }

        return labels;
    }

    private HorizontalAxisLayout CreateHorizontalLayout(ResolvedScale scale, Axis axis) => HorizontalAxisLayout.Create(scale.GetTicks(), _measurer, TickFont, axis.TickRotation, Math.Max(40, Math.Min(150, _plot.Height * 0.3)));

    private static void Configure(ResolvedScale? scale, double start, double end, double tickCount)
    {
        if (scale is null)
            return;

        // Categories read from the top down on a vertical axis, and from left to right on a horizontal one
        if (scale.IsOrdinal && start > end)
        {
            (start, end) = (end, start);
        }

        scale.Configure(start, end, Math.Max(2, (int)Math.Round(tickCount, MidpointRounding.AwayFromZero)));
    }

    // The axis of the values (the y scale, wherever the coordinates draw it) has grid lines, unless it has categories
    private static bool HasGridByDefault(ResolvedScale scale, bool isYScale) => isYScale && !scale.IsOrdinal;

    private bool HidesAxis(bool isX) => _traits.HasFlag(isX ? MarkTraits.HideXAxis : MarkTraits.HideYAxis);

    private void RenderVerticalAxis(ResolvedScale scale, Axis axis, List<AxisLabel> labels, ChartRectangle frame, SceneGroup grid, SceneGroup axes, bool isYScale, bool drawLabels, ChartRectangle? titleArea)
    {
        var showGrid = axis.Grid ?? HasGridByDefault(scale, isYScale);
        var lastLabel = double.NaN;
        foreach (var label in labels)
        {
            if (showGrid)
            {
                grid.Add(new SceneLine(frame.X, label.Position, frame.Right, label.Position) { Stroke = _theme.GridColor });
            }

            if (!drawLabels)
                continue;

            if (axis.TickSize > 0)
            {
                axes.Add(new SceneLine(frame.X - axis.TickSize, label.Position, frame.X, label.Position) { Stroke = _theme.AxisColor });
            }

            // A label that would overlap the previous one is skipped
            if (!double.IsNaN(lastLabel) && Math.Abs(label.Position - lastLabel) < label.Metrics.Height)
                continue;

            lastLabel = label.Position;
            axes.Add(new SceneText(frame.X - axis.TickSize - TickLabelGap, label.Position + ((label.Metrics.Ascent - label.Metrics.Descent) / 2), label.Text, TickFont, _theme.MutedForeground)
            {
                Anchor = SceneTextAnchor.End,
                MeasuredWidth = label.Metrics.Width,
            });
        }

        if (axis.Line ?? false)
        {
            axes.Add(new SceneLine(frame.X, frame.Y, frame.X, frame.Bottom) { Stroke = _theme.AxisColor });
        }

        if (titleArea is { } area)
        {
            RenderVerticalTitle(axis, area, axes);
        }
    }

    private void RenderVerticalTitle(Axis axis, ChartRectangle area, SceneGroup axes)
    {
        if (axis.Label is { Length: > 0 } text)
        {
            var metrics = _measurer.Measure(text, LabelFont);
            axes.Add(new SceneText(Padding + metrics.Ascent, area.CenterY, text, LabelFont, _theme.Foreground)
            {
                Anchor = SceneTextAnchor.Middle,
                Rotation = -90,
                MeasuredWidth = metrics.Width,
            });
        }
    }

    private void RenderHorizontalAxis(ResolvedScale scale, Axis axis, HorizontalAxisLayout layout, ChartRectangle frame, SceneGroup grid, SceneGroup axes, bool isYScale, bool drawLabels, ChartRectangle? titleArea)
    {
        var showGrid = axis.Grid ?? HasGridByDefault(scale, isYScale);
        var baseline = frame.Bottom + axis.TickSize + TickLabelGap;
        for (var i = 0; i < layout.Labels.Count; i++)
        {
            var label = layout.Labels[i];
            if (showGrid)
            {
                grid.Add(new SceneLine(label.Position, frame.Y, label.Position, frame.Bottom) { Stroke = _theme.GridColor });
            }

            if (!drawLabels)
                continue;

            if (axis.TickSize > 0)
            {
                axes.Add(new SceneLine(label.Position, frame.Bottom, label.Position, frame.Bottom + axis.TickSize) { Stroke = _theme.AxisColor });
            }

            if (i % layout.Stride is not 0)
                continue;

            if (layout.Rotation == 0)
            {
                axes.Add(new SceneText(label.Position, baseline + label.Metrics.Ascent, label.Text, TickFont, _theme.MutedForeground)
                {
                    Anchor = SceneTextAnchor.Middle,
                    MeasuredWidth = label.Metrics.Width,
                });
            }
            else
            {
                // Rotated around its end, which is under the tick
                axes.Add(new SceneText(label.Position + (label.Metrics.Ascent / 2), baseline + (label.Metrics.Ascent / 2), label.Text, TickFont, _theme.MutedForeground)
                {
                    Anchor = SceneTextAnchor.End,
                    Rotation = layout.Rotation,
                    MeasuredWidth = label.Metrics.Width,
                });
            }
        }

        if (axis.Line ?? true)
        {
            axes.Add(new SceneLine(frame.X, frame.Bottom, frame.Right, frame.Bottom) { Stroke = _theme.AxisColor });
        }

        if (titleArea is { } area)
        {
            RenderHorizontalTitle(axis, layout, area, axes);
        }
    }

    private void RenderHorizontalTitle(Axis axis, HorizontalAxisLayout layout, ChartRectangle area, SceneGroup axes)
    {
        if (axis.Label is { Length: > 0 } text)
        {
            var metrics = _measurer.Measure(text, LabelFont);
            axes.Add(new SceneText(area.CenterX, area.Bottom + axis.TickSize + TickLabelGap + layout.LabelBlockHeight + AxisLabelGap + metrics.Ascent, text, LabelFont, _theme.Foreground)
            {
                Anchor = SceneTextAnchor.Middle,
                MeasuredWidth = metrics.Width,
            });
        }
    }

    private void LayoutPolarCells(PolarCoordinates polar, ChartRectangle area, FacetGrid facets, SceneGroup grid, SceneGroup axes, Action<FacetCell, CoordinateSystem, ChartRectangle> renderCell)
    {
        if (!facets.IsFaceted)
        {
            var system = LayoutPolar(polar, _plot.Margin is { } margin ? FromMargin(margin) : area);
            RenderPolarAxes(system, grid, axes);
            renderCell(facets.Cells[0], system, _plot.Margin is { } frameMargin ? FromMargin(frameMargin) : area);
            return;
        }

        var headerHeight = facets.HasHeaders ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;
        var rowLabelWidth = facets.HasRowLabels ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;
        var gridArea = new ChartRectangle(area.X, area.Y, Math.Max(1, area.Width - rowLabelWidth), area.Height);
        foreach (var cell in facets.Cells)
        {
            var frame = GetCellFrame(gridArea, facets, cell, _plot.Facets.Gap, _plot.Facets.Gap, headerHeight);
            var system = LayoutPolar(polar, frame);
            RenderPolarAxes(system, grid, axes);
            RenderFacetLabels(facets, cell, frame, axes);
            renderCell(cell, system, frame);
        }
    }

    private PolarSystem LayoutPolar(PolarCoordinates polar, ChartRectangle area)
    {

        var transposed = polar.Transposed;
        var angular = transposed ? _y : _x;
        var radial = transposed ? _x : _y;
        var angularAxis = transposed ? _plot.YAxis : _plot.XAxis;
        var startAngle = polar.StartAngle * Math.PI / 180;
        var endAngle = polar.EndAngle * Math.PI / 180;
        if (endAngle < startAngle)
        {
            (startAngle, endAngle) = (endAngle, startAngle);
        }

        var periodic = endAngle - startAngle >= (2 * Math.PI) - 1e-9;
        var innerFraction = Math.Clamp(polar.InnerRadius, 0, 0.99);
        angular?.Configure(startAngle, endAngle, Math.Max(2, (int)Math.Round((endAngle - startAngle) / (Math.PI / 6), MidpointRounding.AwayFromZero)), periodic);

        // The angular labels are around the circle: keep room for the widest one
        var horizontalSpace = 4.0;
        var verticalSpace = 4.0;
        if (angular is not null && (angularAxis.Visible ?? !HidesAxis(isX: !transposed)))
        {
            var ticks = angular.GetTicks();
            if (ticks.Count > 0)
            {
                horizontalSpace = ticks.Max(tick => _measurer.Measure(tick.Label, TickFont).Width) + 10;
                verticalSpace = _measurer.Measure("0", TickFont).Height + 10;
            }
        }

        var (minX, minY, maxX, maxY) = PolarGeometry.GetUnitBounds(innerFraction, startAngle, endAngle);
        var availableWidth = Math.Max(1, area.Width - (2 * horizontalSpace));
        var availableHeight = Math.Max(1, area.Height - (2 * verticalSpace));
        var radius = Math.Max(1, Math.Min(availableWidth / Math.Max(maxX - minX, 1e-9), availableHeight / Math.Max(maxY - minY, 1e-9)));
        var centerX = area.CenterX - (radius * (minX + maxX) / 2);
        var centerY = area.CenterY - (radius * (minY + maxY) / 2);
        radial?.Configure(innerFraction * radius, radius, Math.Max(2, (int)Math.Round(radius / 30, MidpointRounding.AwayFromZero)));
        return new PolarSystem(transposed, centerX, centerY, innerFraction * radius, radius, startAngle, endAngle);
    }

    private void RenderPolarAxes(PolarSystem system, SceneGroup grid, SceneGroup axes)
    {
        var transposed = system.Transposed;
        var angular = transposed ? _y : _x;
        var radial = transposed ? _x : _y;
        var angularAxis = transposed ? _plot.YAxis : _plot.XAxis;
        var radialAxis = transposed ? _plot.XAxis : _plot.YAxis;
        var angularVisible = angular is not null && (angularAxis.Visible ?? !HidesAxis(isX: !transposed));
        var radialVisible = radial is not null && (radialAxis.Visible ?? !HidesAxis(isX: transposed));
        var angularTicks = angular?.GetTicks() ?? [];
        if (system.IsFullCircle && angularTicks.Count > 1 && Math.Abs(angularTicks[^1].Position - angularTicks[0].Position - (2 * Math.PI)) < 1e-6)
        {
            // 0 and 360 degrees are the same spoke
            angularTicks = [.. angularTicks.Take(angularTicks.Count - 1)];
        }

        if (radialVisible && (radialAxis.Grid ?? true))
        {
            var polygon = system.IsFullCircle && angular is { IsOrdinal: true } && angularTicks.Count > 2 && ((PolarCoordinates)_coordinates).GridShape is not PolarGridShape.Circle;
            polygon |= ((PolarCoordinates)_coordinates).GridShape is PolarGridShape.Polygon && angularTicks.Count > 2;
            foreach (var tick in radial!.GetTicks())
            {
                if (tick.Position <= 0)
                    continue;

                PathData path;
                if (polygon)
                {
                    var builder = new PathBuilder();
                    for (var i = 0; i < angularTicks.Count; i++)
                    {
                        var (x, y) = PolarGeometry.Point(system.CenterX, system.CenterY, tick.Position, angularTicks[i].Position);
                        if (i is 0)
                        {
                            builder.MoveTo(x, y);
                        }
                        else
                        {
                            builder.LineTo(x, y);
                        }
                    }

                    path = builder.Close().Build();
                }
                else
                {
                    path = PolarGeometry.Arc(system.CenterX, system.CenterY, tick.Position, system.StartAngle, system.EndAngle);
                }

                grid.Add(new ScenePath(path) { Stroke = _theme.GridColor });
            }
        }

        if (angularVisible)
        {
            var spokes = angularAxis.Grid ?? true;
            var font = TickFont;
            foreach (var tick in angularTicks)
            {
                if (spokes)
                {
                    var (x1, y1) = PolarGeometry.Point(system.CenterX, system.CenterY, system.InnerRadius, tick.Position);
                    var (x2, y2) = PolarGeometry.Point(system.CenterX, system.CenterY, system.OuterRadius, tick.Position);
                    grid.Add(new SceneLine(x1, y1, x2, y2) { Stroke = _theme.GridColor });
                }

                var metrics = _measurer.Measure(tick.Label, font);
                var (x, y) = PolarGeometry.Point(system.CenterX, system.CenterY, system.OuterRadius + 6, tick.Position);
                var sin = Math.Sin(tick.Position);
                var cos = Math.Cos(tick.Position);
                var anchor = sin > 0.1 ? SceneTextAnchor.Start : sin < -0.1 ? SceneTextAnchor.End : SceneTextAnchor.Middle;
                var baseline = y + (((metrics.Ascent * (1 - cos)) - (metrics.Descent * (1 + cos))) / 2);
                axes.Add(new SceneText(x, baseline, tick.Label, font, _theme.MutedForeground) { Anchor = anchor, MeasuredWidth = metrics.Width });
            }
        }

        if (radialVisible)
        {
            var font = TickFont;
            foreach (var tick in radial!.GetTicks())
            {
                var metrics = _measurer.Measure(tick.Label, font);
                var (x, y) = PolarGeometry.Point(system.CenterX, system.CenterY, tick.Position, system.StartAngle);
                axes.Add(new SceneText(x + 4, y + ((metrics.Ascent - metrics.Descent) / 2), tick.Label, font, _theme.MutedForeground) { MeasuredWidth = metrics.Width });
            }
        }
    }

    private List<ChannelValues> Collect(Func<MarkData, ChannelValues?[]> selector)
    {
        var result = new List<ChannelValues>();
        foreach (var (_, data) in _marks)
        {
            foreach (var channel in selector(data))
            {
                if (channel is not null)
                {
                    result.Add(channel);
                }
            }
        }

        return result;
    }
}
