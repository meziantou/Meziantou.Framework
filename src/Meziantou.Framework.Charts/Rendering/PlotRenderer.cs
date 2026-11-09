namespace Meziantou.Framework.Charts;

/// <summary>Lays a <see cref="Plot"/> out into a <see cref="Scene"/>: resolves the scales, measures the titles, the legend and the axes, computes the plot area, then draws the grid, the marks, the axes and the legend.</summary>
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
    private readonly ResolvedScale? _y2;
    private readonly ResolvedColorScale? _color;
    private readonly ResolvedSizeScale? _size;
    private readonly Coordinates _coordinates;
    private readonly Scene _scene;

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

        _coordinates = plot.Coordinates ?? (_traits.HasFlag(MarkTraits.PreferGeo) ? new GeoCoordinates() : _traits.HasFlag(MarkTraits.PreferPolar) ? new PolarCoordinates() : new CartesianCoordinates());
        var frameOnly = _marks.Count > 0 && _marks.All(mark => mark.Mark.GetTraits(mark.Data).HasFlag(MarkTraits.Frame));
        if (_coordinates is GeoCoordinates)
        {
            // On a map, x and y are longitudes and latitudes: the scales give them unchanged to the projection
            _x = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, -180, 180, options: null, parameter: 0, nice: false);
            _y = new ResolvedContinuousScale(ContinuousTransformKind.Linear, ChannelKind.Number, -90, 90, options: null, parameter: 0, nice: false);
        }
        else if (!frameOnly)
        {
            _x = ScaleResolver.ResolvePosition(plot.XScale, Collect(data => [data.X, data.X1, data.X2, .. data.ExtraX]), _traits.HasFlag(MarkTraits.BandX), _traits.HasFlag(MarkTraits.ZeroX), _traits.HasFlag(MarkTraits.ExactX));
            var primary = _marks.Where(mark => !mark.Mark.UseSecondaryYScale).ToList();
            var secondary = _marks.Where(mark => mark.Mark.UseSecondaryYScale).ToList();
            var primaryTraits = GetTraits(primary);
            var secondaryTraits = GetTraits(secondary);
            _y = primary.Count is 0 && plot.YScale is null ? null : ScaleResolver.ResolvePosition(plot.YScale, Collect(primary, data => [data.Y, data.Y1, data.Y2, .. data.ExtraY]), primaryTraits.HasFlag(MarkTraits.BandY), primaryTraits.HasFlag(MarkTraits.ZeroY), primaryTraits.HasFlag(MarkTraits.ExactY), primary.Count is 0 ? 0 : primary.Max(mark => mark.Mark.GetYHeadroom()));
            _y2 = secondary.Count is 0 && plot.SecondaryYScale is null ? null : ScaleResolver.ResolvePosition(plot.SecondaryYScale, Collect(secondary, data => [data.Y, data.Y1, data.Y2, .. data.ExtraY]), secondaryTraits.HasFlag(MarkTraits.BandY), secondaryTraits.HasFlag(MarkTraits.ZeroY), secondaryTraits.HasFlag(MarkTraits.ExactY));
        }

        _color = ScaleResolver.ResolveColor(plot.ColorScale, Collect(data => [data.Fill, data.Stroke, .. data.ExtraColor]), _theme);
        _size = ScaleResolver.ResolveSize(plot.SizeScale, Collect(data => [data.Size]));
        _scene = new Scene(plot.Width, plot.Height, _theme.FontFamily ?? measurer.FontFamily)
        {
            Title = plot.Title,
            Description = plot.Description,
            Background = _theme.Background,
        };
    }

    private SceneFont TickFont => new(_theme.TickFontSize);

    private SceneFont LabelFont => new(_theme.FontSize);

    public static Scene Render(Plot plot, ITextMeasurer measurer)
    {
        ArgumentNullException.ThrowIfNull(measurer);
        return new PlotRenderer(plot, measurer).Render();
    }

    private Scene Render()
    {
        var width = _plot.Width;
        var height = _plot.Height;
        var top = Padding;
        var bottom = Padding;
        var right = Padding;
        var titles = new List<SceneText>();
        top = AddTitle(titles, _plot.Title, new SceneFont(_theme.TitleFontSize, Bold: true), _theme.Foreground, top);
        top = AddTitle(titles, _plot.Subtitle, new SceneFont(_theme.SubtitleFontSize), _theme.MutedForeground, top);
        if (titles.Count > 0)
        {
            top += 6;
        }

        // The legends take their space before the plot area: side by side above or under it, stacked on its right
        var legends = new List<ILegendBlock>();
        if (_color is not null && (_plot.Legend.Visible ?? LegendRenderer.IsVisibleByDefault(_color)))
        {
            legends.Add(new LegendRenderer(_color, _plot.Legend.Title, _theme, _measurer));
        }

        if (_size is not null && (_plot.SizeLegend.Visible ?? true))
        {
            legends.Add(new SizeLegendRenderer(_size, _plot.SizeLegend.Values, _plot.SizeLegend.Title, _theme, _measurer));
        }

        var legendPositions = new List<(ILegendBlock Legend, double X, double Y)>();
        if (legends.Count > 0)
        {
            if (_plot.Legend.Position is LegendPosition.Right)
            {
                var maxWidth = Math.Max(60, Math.Min(180, width / 4));
                var legendsWidth = 0.0;
                foreach (var legend in legends)
                {
                    legend.Measure(maxWidth, vertical: true);
                    legendsWidth = Math.Max(legendsWidth, legend.Width);
                }

                var y = top;
                foreach (var legend in legends)
                {
                    legendPositions.Add((legend, width - Padding - legendsWidth, y));
                    y += legend.Height + 12;
                }

                right += legendsWidth + 12;
            }
            else
            {
                // In rows that wrap, the legends of a row aligned on their tops
                var maxWidth = width - (2 * Padding);
                var x = 0.0;
                var y = 0.0;
                var rowHeight = 0.0;
                foreach (var legend in legends)
                {
                    legend.Measure(maxWidth, vertical: false);
                    if (x > 0 && x + legend.Width > maxWidth)
                    {
                        x = 0;
                        y += rowHeight + 12;
                        rowHeight = 0;
                    }

                    legendPositions.Add((legend, Padding + x, y));
                    x += legend.Width + 24;
                    rowHeight = Math.Max(rowHeight, legend.Height);
                }

                var legendsHeight = y + rowHeight;
                var offset = _plot.Legend.Position is LegendPosition.Bottom ? height - Padding - legendsHeight : top;
                for (var i = 0; i < legendPositions.Count; i++)
                {
                    legendPositions[i] = legendPositions[i] with { Y = legendPositions[i].Y + offset };
                }

                if (_plot.Legend.Position is LegendPosition.Bottom)
                {
                    bottom += legendsHeight + 10;
                }
                else
                {
                    top += legendsHeight + 12;
                }
            }
        }

        var area = new SceneBounds(Padding, top, Math.Max(1, width - Padding - right), Math.Max(1, height - top - bottom));
        var grid = new SceneGroup { CssClass = "grid" };
        var axes = new SceneGroup { CssClass = "axes" };
        var marks = new SceneGroup { CssClass = "marks" };
        var facets = FacetGrid.Create(_marks.Select(mark => mark.Data), _plot.Facets);

        // The scales are mapped to the frame of a chart of the grid before the chart is drawn
        var lateColorsResolved = false;
        void RenderCell(FacetCell cell, CoordinateSystem system, SceneBounds frame)
        {
            if (!lateColorsResolved)
            {
                // Before the first chart is drawn, the continuous color scale includes the values the marks compute from
                // positions. The charts of a grid have the same size, so the frame of the first one serves for all.
                lateColorsResolved = true;
                if (_color is ResolvedContinuousColorScale continuous)
                {
                    foreach (var (mark, data) in _marks)
                    {
                        var context = new RenderContext(_theme, _measurer, _x, mark.UseSecondaryYScale ? _y2 : _y, _color, _size, system, frame);
                        foreach (var facetCell in facets.Cells)
                        {
                            foreach (var value in mark.GetLateColorValues(context, data.ForFacet(facetCell.XKey, facetCell.YKey)))
                            {
                                continuous.Include(value);
                            }
                        }
                    }
                }
            }

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
                var context = new RenderContext(_theme, _measurer, _x, mark.UseSecondaryYScale ? _y2 : _y, _color, _size, system, frame);
                var group = new SceneGroup { CssClass = mark.CssClass, Opacity = mark.Opacity };
                mark.Render(context, data.ForFacet(cell.XKey, cell.YKey), group);
                target.Add(group);
            }
        }

        if (_coordinates is GeoCoordinates geo)
        {
            LayoutGeoCells(geo, area, facets, grid, axes, RenderCell);
        }
        else if (_coordinates is PolarCoordinates polar)
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

        foreach (var (legend, x, y) in legendPositions)
        {
            legend.Render(_scene.Root, x, y);
        }
        foreach (var title in titles)
        {
            _scene.Root.Add(title);
        }

        return _scene;
    }

    private double AddTitle(List<SceneText> titles, string? text, SceneFont font, SceneColor color, double top)
    {
        if (string.IsNullOrEmpty(text))
            return top;

        var metrics = _measurer.Measure(text, font);
        titles.Add(new SceneText(Padding, top + metrics.Ascent, text, font, color) { MeasuredWidth = metrics.Width });
        return top + metrics.Height + 2;
    }

    private SceneBounds FromMargin(ChartMargin margin) => new(margin.Left, margin.Top, Math.Max(1, _plot.Width - margin.Left - margin.Right), Math.Max(1, _plot.Height - margin.Top - margin.Bottom));

    private SceneFont HeaderFont => new(_theme.FontSize, Bold: true);

    private void LayoutCartesianCells(SceneBounds area, FacetGrid facets, SceneGroup grid, SceneGroup axes, Action<FacetCell, CoordinateSystem, SceneBounds> renderCell)
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
        var secondary = transposed ? null : _y2;
        var secondaryAxis = _plot.SecondaryYAxis;
        var secondaryVisible = secondary is not null && (secondaryAxis.Visible ?? true);
        var secondaryWidth = 0.0;

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
                : new SceneBounds(area.X + verticalWidth + extraLeft, area.Y, Math.Max(1, area.Width - verticalWidth - extraLeft - extraRight - rowLabelWidth - secondaryWidth), Math.Max(1, area.Height - horizontalHeight));

            var frame = GetCellFrame(gridArea, facets, facets.Cells[0], gapX, gapY, headerHeight);
            Configure(vertical, frame.Bottom, frame.Y, frame.Height / 40);
            Configure(horizontal, frame.X, frame.Right, frame.Width / 80);
            ConfigureSecondary(frame, transposed);
            secondaryWidth = 0;
            if (secondaryVisible)
            {
                var labels = MeasureVerticalLabels(secondary!);
                secondaryWidth = (labels.Count is 0 ? 0 : labels.Max(label => label.Metrics.Width)) + secondaryAxis.TickSize + TickLabelGap + 2;
                if (secondaryAxis.Label is { Length: > 0 } label)
                {
                    secondaryWidth += _measurer.Measure(label, LabelFont).Height + AxisLabelGap;
                }
            }

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
                        var requiredRight = _plot.Width - 2 - rowLabelWidth - secondaryWidth - (last.Metrics.Width / 2) - (last.Position - frame.Right);
                        extraRight = Math.Max(0, area.Right - rowLabelWidth - secondaryWidth - requiredRight);
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
            ConfigureSecondary(frame, transposed);
            if (verticalVisible)
            {
                RenderVerticalAxis(vertical!, verticalAxis, MeasureVerticalLabels(vertical!), frame, grid, axes, isYScale: !transposed, drawLabels: cell.IsLeft, titleArea: single ? frame : null);
            }

            if (horizontalVisible)
            {
                horizontalLayout = CreateHorizontalLayout(horizontal!, horizontalAxis);
                RenderHorizontalAxis(horizontal!, horizontalAxis, horizontalLayout, frame, grid, axes, isYScale: transposed, drawLabels: cell.IsBottom, titleArea: single ? frame : null);
            }

            if (secondaryVisible)
            {
                RenderSecondaryAxis(secondary!, secondaryAxis, frame, grid, axes, drawLabels: cell.IsRight, titleArea: single ? frame : null, secondaryWidth);
            }

            RenderFacetLabels(facets, cell, frame, axes, secondaryVisible && cell.IsRight ? secondaryWidth : 0);
            renderCell(cell, cartesian, frame);
        }

        if (!single)
        {
            // The titles of the axes, once for the grid
            var titleArea = new SceneBounds(gridArea.X, gridArea.Y + headerHeight, gridArea.Width, gridArea.Height - headerHeight);
            if (verticalVisible)
            {
                RenderVerticalTitle(verticalAxis, titleArea, axes);
            }

            if (horizontalVisible)
            {
                RenderHorizontalTitle(horizontalAxis, horizontalLayout!, titleArea, axes);
            }

            if (secondaryVisible)
            {
                RenderSecondaryTitle(secondaryAxis, titleArea, axes, secondaryWidth);
            }
        }
    }

    // The secondary scale has the range of the y scale: vertical in Cartesian coordinates, horizontal when transposed
    private void ConfigureSecondary(SceneBounds frame, bool transposed)
    {
        if (transposed)
        {
            Configure(_y2, frame.X, frame.Right, frame.Width / 80);
        }
        else
        {
            Configure(_y2, frame.Bottom, frame.Y, frame.Height / 40);
        }
    }

    private void RenderSecondaryAxis(ResolvedScale scale, Axis axis, SceneBounds frame, SceneGroup grid, SceneGroup axes, bool drawLabels, SceneBounds? titleArea, double axisWidth)
    {
        var showGrid = axis.Grid ?? false;
        var lastLabel = double.NaN;
        foreach (var label in MeasureVerticalLabels(scale))
        {
            if (showGrid)
            {
                grid.Add(new SceneLine(frame.X, label.Position, frame.Right, label.Position) { Stroke = _theme.GridColor });
            }

            if (!drawLabels)
                continue;

            if (axis.TickSize > 0)
            {
                axes.Add(new SceneLine(frame.Right, label.Position, frame.Right + axis.TickSize, label.Position) { Stroke = _theme.AxisColor });
            }

            if (!double.IsNaN(lastLabel) && Math.Abs(label.Position - lastLabel) < label.Metrics.Height)
                continue;

            lastLabel = label.Position;
            axes.Add(new SceneText(frame.Right + axis.TickSize + TickLabelGap, label.Position + ((label.Metrics.Ascent - label.Metrics.Descent) / 2), label.Text, TickFont, _theme.MutedForeground)
            {
                MeasuredWidth = label.Metrics.Width,
            });
        }

        if (axis.Line ?? false)
        {
            axes.Add(new SceneLine(frame.Right, frame.Y, frame.Right, frame.Bottom) { Stroke = _theme.AxisColor });
        }

        if (titleArea is { } area)
        {
            RenderSecondaryTitle(axis, area, axes, axisWidth);
        }
    }

    private void RenderSecondaryTitle(Axis axis, SceneBounds area, SceneGroup axes, double axisWidth)
    {
        if (axis.Label is { Length: > 0 } text)
        {
            // Rotated by 90 degrees, at the right edge of the space of the axis: the top of the glyphs faces right
            var metrics = _measurer.Measure(text, LabelFont);
            axes.Add(new SceneText(area.Right + axisWidth - metrics.Height + metrics.Descent, area.CenterY, text, LabelFont, _theme.Foreground)
            {
                Anchor = SceneTextAnchor.Middle,
                Rotation = 90,
                MeasuredWidth = metrics.Width,
            });
        }
    }

    private static SceneBounds GetCellFrame(SceneBounds gridArea, FacetGrid facets, FacetCell cell, double gapX, double gapY, double headerHeight)
    {
        var headers = facets.Wraps ? facets.Rows * headerHeight : headerHeight;
        var width = Math.Max(1, (gridArea.Width - (gapX * (facets.Columns - 1))) / facets.Columns);
        var height = Math.Max(1, (gridArea.Height - headers - (gapY * (facets.Rows - 1))) / facets.Rows);
        var x = gridArea.X + (cell.Column * (width + gapX));
        var y = facets.Wraps
            ? gridArea.Y + (cell.Row * (height + gapY + headerHeight)) + headerHeight
            : gridArea.Y + headerHeight + (cell.Row * (height + gapY));
        return new SceneBounds(x, y, width, height);
    }

    private void RenderFacetLabels(FacetGrid facets, FacetCell cell, SceneBounds frame, SceneGroup axes, double rightOffset = 0)
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
            axes.Add(new SceneText(frame.Right + rightOffset + 6, frame.CenterY, text, font, _theme.Foreground) { Anchor = SceneTextAnchor.Middle, Rotation = 90, MeasuredWidth = metrics.Width, CssClass = "facet-header" });
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

    private void RenderVerticalAxis(ResolvedScale scale, Axis axis, List<AxisLabel> labels, SceneBounds frame, SceneGroup grid, SceneGroup axes, bool isYScale, bool drawLabels, SceneBounds? titleArea)
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

    private void RenderVerticalTitle(Axis axis, SceneBounds area, SceneGroup axes)
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

    private void RenderHorizontalAxis(ResolvedScale scale, Axis axis, HorizontalAxisLayout layout, SceneBounds frame, SceneGroup grid, SceneGroup axes, bool isYScale, bool drawLabels, SceneBounds? titleArea)
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

    private void RenderHorizontalTitle(Axis axis, HorizontalAxisLayout layout, SceneBounds area, SceneGroup axes)
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

    private void LayoutPolarCells(PolarCoordinates polar, SceneBounds area, FacetGrid facets, SceneGroup grid, SceneGroup axes, Action<FacetCell, CoordinateSystem, SceneBounds> renderCell)
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
        var gridArea = new SceneBounds(area.X, area.Y, Math.Max(1, area.Width - rowLabelWidth), area.Height);
        foreach (var cell in facets.Cells)
        {
            var frame = GetCellFrame(gridArea, facets, cell, _plot.Facets.Gap, _plot.Facets.Gap, headerHeight);
            var system = LayoutPolar(polar, frame);
            RenderPolarAxes(system, grid, axes);
            RenderFacetLabels(facets, cell, frame, axes);
            renderCell(cell, system, frame);
        }
    }

    private void LayoutGeoCells(GeoCoordinates geo, SceneBounds area, FacetGrid facets, SceneGroup grid, SceneGroup axes, Action<FacetCell, CoordinateSystem, SceneBounds> renderCell)
    {
        // The map fits the extent, else the shapes and the positions of the marks (and the outline of the globe when it
        // is drawn), else the whole world
        var projection = geo.Projection;
        var positions = geo.Extent is { } extent
            ? GeoSystem.SampleBox(projection, extent.West, extent.South, extent.East, extent.North)
            : [.. _marks.SelectMany(mark => mark.Mark.GetGeoPositions(mark.Data))];
        if (geo.Extent is null && geo.Outline)
        {
            positions.AddRange(SampleWorld(projection));
        }

        var bounds = GeoSystem.GetProjectedBounds(projection, positions);
        if (double.IsNaN(bounds.MinX))
        {
            positions = SampleWorld(projection);
            bounds = GeoSystem.GetProjectedBounds(projection, positions);
        }

        var region = (West: positions.Min(position => position.Longitude), South: positions.Min(position => position.Latitude), East: positions.Max(position => position.Longitude), North: positions.Max(position => position.Latitude));
        _x!.Configure(-180, 180, 2);
        _y!.Configure(-90, 90, 2);
        if (!facets.IsFaceted)
        {
            var frame = _plot.Margin is { } margin ? FromMargin(margin) : area;
            var system = GeoSystem.Fit(projection, bounds, frame);
            RenderGeoGrid(geo, system, frame, region, grid);
            renderCell(facets.Cells[0], system, frame);
            return;
        }

        var headerHeight = facets.HasHeaders ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;
        var rowLabelWidth = facets.HasRowLabels ? _measurer.Measure("Xg", HeaderFont).Height + 6 : 0;
        var gridArea = new SceneBounds(area.X, area.Y, Math.Max(1, area.Width - rowLabelWidth), area.Height);
        foreach (var cell in facets.Cells)
        {
            var frame = GetCellFrame(gridArea, facets, cell, _plot.Facets.Gap, _plot.Facets.Gap, headerHeight);
            var system = GeoSystem.Fit(projection, bounds, frame);
            RenderGeoGrid(geo, system, frame, region, grid);
            RenderFacetLabels(facets, cell, frame, axes);
            renderCell(cell, system, frame);
        }
    }

    // The edge of the map of the whole world
    private static List<GeoPosition> SampleWorld(GeoProjection projection) => GeoSystem.SampleBox(projection, projection.CenterLongitude - 180, -projection.MaxLatitude, projection.CenterLongitude + 180, projection.MaxLatitude);

    /// <summary>Draws the graticule around a region (with a margin of one step) and the outline of the globe, clipped to the frame.</summary>
    private void RenderGeoGrid(GeoCoordinates geo, GeoSystem system, SceneBounds frame, (double West, double South, double East, double North) region, SceneGroup grid)
    {
        var projection = geo.Projection;
        var group = new SceneGroup { CssClass = "graticule", Clip = frame };
        if (geo.GraticuleStep is { } step)
        {
            var minLongitude = projection.CenterLongitude - 180;
            var maxLongitude = projection.CenterLongitude + 180;
            var maxLatitude = projection.MaxLatitude;
            var west = Math.Max(minLongitude, (Math.Floor(region.West / step) - 1) * step);
            var east = Math.Min(maxLongitude, (Math.Ceiling(region.East / step) + 1) * step);
            var south = Math.Max(-maxLatitude, (Math.Floor(region.South / step) - 1) * step);
            var north = Math.Min(maxLatitude, (Math.Ceiling(region.North / step) + 1) * step);
            var builder = new PathBuilder();
            for (var longitude = Math.Ceiling(west / step) * step; longitude <= east + 1e-9; longitude += step)
            {
                var line = new List<GeoPosition>();
                GeoSystem.AddSegment(projection, line, longitude, south, longitude, north);
                system.AddPolyline(builder, line, close: false);
            }

            for (var latitude = Math.Ceiling(south / step) * step; latitude <= north + 1e-9; latitude += step)
            {
                if (Math.Abs(latitude) >= 90)
                    continue;

                var line = new List<GeoPosition>();
                GeoSystem.AddSegment(projection, line, west, latitude, east, latitude);
                system.AddPolyline(builder, line, close: false);
            }

            if (!builder.IsEmpty)
            {
                group.Add(new ScenePath(builder.Build()) { Stroke = _theme.GridColor, StrokeWidth = 0.5 });
            }
        }

        if (geo.Outline)
        {
            // On the map of a pole, the west and east edges of the world are the same line, inside the map: the outline is the opposite pole
            var west = projection.CenterLongitude - 180;
            var east = projection.CenterLongitude + 180;
            PathData outline;
            if (system.JoinsMeridians(west, east))
            {
                var builder = new PathBuilder();
                system.AddRingOutline(builder, SampleWorld(projection), west, east);
                outline = builder.Build();
            }
            else
            {
                outline = system.Polyline(SampleWorld(projection), close: true);
            }

            group.Add(new ScenePath(outline) { Stroke = _theme.AxisColor, LineJoin = SceneLineJoin.Round });
        }

        if (group.Children.Count > 0)
        {
            grid.Add(group);
        }
    }

    private PolarSystem LayoutPolar(PolarCoordinates polar, SceneBounds area)
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
        if (transposed)
        {
            _y2?.Configure(startAngle, endAngle, Math.Max(2, (int)Math.Round((endAngle - startAngle) / (Math.PI / 6), MidpointRounding.AwayFromZero)), periodic);
        }
        else
        {
            _y2?.Configure(innerFraction * radius, radius, Math.Max(2, (int)Math.Round(radius / 30, MidpointRounding.AwayFromZero)));
        }
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

    private List<ChannelValues> Collect(Func<MarkData, ChannelValues?[]> selector) => Collect(_marks, selector);

    private static List<ChannelValues> Collect(List<(Mark Mark, MarkData Data)> marks, Func<MarkData, ChannelValues?[]> selector)
    {
        var result = new List<ChannelValues>();
        foreach (var (_, data) in marks)
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

    private static MarkTraits GetTraits(List<(Mark Mark, MarkData Data)> marks) => marks.Aggregate(MarkTraits.None, (traits, mark) => traits | mark.Mark.GetTraits(mark.Data));
}
