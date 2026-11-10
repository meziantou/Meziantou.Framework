using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// Draws the Git graphs as Mermaid does: a branch per row (or per column), the commits in order along it, and the
/// links between the commits and their parents, which turn with arcs. A bottom-to-top graph is the top-to-bottom
/// layout mirrored.
/// </summary>
internal static class GitGraphDiagramRenderer
{
    private const double LayoutOffset = 10;
    private const double CommitStep = 40;
    private const double DefaultPosition = 30;
    private const double LabelFontSize = 10;
    private const double PaddingX = 4;
    private const double PaddingY = 2;

    public static SceneBounds Render(GitGraphDiagram diagram, DiagramRenderContext context, SceneGroup group)
    {
        var history = GitGraphHistory.Create(diagram);
        var colors = new GitGraphColors(diagram, context.Theme);
        var state = new State(diagram, context, history, colors);
        state.Layout();
        if (diagram.ShowBranches)
        {
            state.DrawBranches(group.Add(new SceneGroup { CssClass = "branches" }));
        }

        state.DrawArrows(group.Add(new SceneGroup { CssClass = "commit-arrows" }));
        state.DrawCommits(group.Add(new SceneGroup { CssClass = "commit-bullets" }), group.Add(new SceneGroup { CssClass = "commit-labels" }));
        return state.Bounds.ToBounds();
    }

    private sealed class State(GitGraphDiagram diagram, DiagramRenderContext context, GitGraphHistory history, GitGraphColors colors)
    {
        private readonly Dictionary<string, (double Position, int Index)> _branchPositions = new(StringComparer.Ordinal);
        private readonly Dictionary<GitGraphHistoryCommit, LayoutPoint> _positions = new(ReferenceEqualityComparer.Instance);
        private readonly List<double> _lanes = [];
        private double _maxPosition;

        public BoundsBuilder Bounds { get; } = new();

        private bool IsVertical => diagram.Orientation is not GitGraphOrientation.LeftToRight;

        private bool IsBottomToTop => diagram.Orientation is GitGraphOrientation.BottomToTop;

        private SceneFont BranchFont => new(context.FontSize);

        private static SceneFont LabelFont => new(LabelFontSize);

        /// <summary>Places the branches and the commits; a vertical graph is laid out top to bottom, and mirrored for bottom to top.</summary>
        public void Layout()
        {
            var position = 0.0;
            for (var i = 0; i < history.Branches.Count; i++)
            {
                var name = history.Branches[i];
                _branchPositions[name] = (position, i);
                position += 50 + (diagram.RotateCommitLabels ? 40 : 0) + (IsVertical ? Measure(name, BranchFont).Width / 2 : 0);
            }

            position = IsVertical ? DefaultPosition : 0;
            foreach (var commit in history.Commits)
            {
                if (!_branchPositions.TryGetValue(commit.Branch, out var branch))
                    continue;

                if (diagram.ParallelCommits)
                {
                    position = ParallelPosition(commit);
                }

                var along = position + LayoutOffset;
                _positions[commit] = IsVertical ? new LayoutPoint(branch.Position, along) : new LayoutPoint(along, branch.Position - 2);
                position += CommitStep + LayoutOffset;
                _maxPosition = Math.Max(_maxPosition, position);
            }
        }

        private double ParallelPosition(GitGraphHistoryCommit commit)
        {
            // After the parent that is the furthest along the history
            var best = double.NegativeInfinity;
            foreach (var parent in commit.Parents)
            {
                if (_positions.TryGetValue(parent, out var point))
                {
                    best = Math.Max(best, IsVertical ? point.Y : point.X);
                }
            }

            if (double.IsNegativeInfinity(best))
                return IsVertical ? DefaultPosition : 0;

            return best + CommitStep;
        }

        /// <summary>Maps a point of the top-to-bottom layout to the bottom-to-top one.</summary>
        private LayoutPoint Map(LayoutPoint point) => IsBottomToTop ? new LayoutPoint(point.X, _maxPosition + DefaultPosition - point.Y) : point;

        private double MapY(double y) => IsBottomToTop ? _maxPosition + DefaultPosition - y : y;

        public void DrawBranches(SceneGroup target)
        {
            var theme = context.Theme;
            foreach (var name in history.Branches)
            {
                var (position, index) = _branchPositions[name];
                var branchGroup = target.Add(new SceneGroup { CssClass = "branch" });
                branchGroup.SetDataAttribute("branch", name);
                var spine = IsVertical ? position : position - 2;
                var dashes = new[] { 2.0 };
                if (IsVertical)
                {
                    var start = Map(new LayoutPoint(position, DefaultPosition));
                    var end = Map(new LayoutPoint(position, _maxPosition));
                    branchGroup.Add(new SceneLine(start.X, start.Y, end.X, end.Y) { Stroke = theme.LineColor, StrokeWidth = 1, StrokeDashArray = dashes });
                    Bounds.Add(start);
                    Bounds.Add(end);
                }
                else
                {
                    branchGroup.Add(new SceneLine(0, spine, _maxPosition, spine) { Stroke = theme.LineColor, StrokeWidth = 1, StrokeDashArray = dashes });
                    Bounds.Add(new LayoutPoint(0, spine));
                    Bounds.Add(new LayoutPoint(_maxPosition, spine));
                }

                _lanes.Add(spine);

                // The name, in a box of the color of the branch
                var metrics = Measure(name, BranchFont);
                var width = metrics.Width + 18;
                var height = metrics.Height + 4;
                SceneBounds box;
                if (IsVertical)
                {
                    var top = IsBottomToTop ? MapY(DefaultPosition) + 10 : 0;
                    box = new SceneBounds(position - (metrics.Width / 2) - 10, top, width, height);
                }
                else
                {
                    box = new SceneBounds(-metrics.Width - 23 - (diagram.RotateCommitLabels ? 30 : 0), spine - (height / 2), width, height);
                }

                branchGroup.Add(new SceneRectangle(box.X, box.Y, box.Width, box.Height) { Fill = colors.Branch(index), CornerRadius = 4, CssClass = "branch-label-background" });
                branchGroup.Add(new SceneText(box.CenterX, box.Y + 2 + metrics.Ascent, name, BranchFont, colors.BranchLabel(index)) { Anchor = SceneTextAnchor.Middle, MeasuredWidth = metrics.Width });
                Bounds.Add(box);
            }
        }

        public void DrawArrows(SceneGroup target)
        {
            foreach (var commit in history.Commits)
            {
                foreach (var parent in commit.Parents)
                {
                    DrawArrow(target, parent, commit);
                }
            }
        }

        private void DrawArrow(SceneGroup target, GitGraphHistoryCommit commitA, GitGraphHistoryCommit commitB)
        {
            if (!_positions.TryGetValue(commitA, out var p1) || !_positions.TryGetValue(commitB, out var p2))
                return;

            var isSecondParent = commitB.Kind is GitGraphHistoryCommitKind.Merge && commitB.Parents.Count > 0 && commitB.Parents[0] != commitA;
            var colorBranch = isSecondParent ? commitA.Branch : commitB.Branch;
            var path = new ArrowPath(p1);
            if (NeedsRerouting(commitA, commitB, p1, p2))
            {
                const double Radius = 10;
                var lineY = p1.Y < p2.Y ? FindLane(p1.Y, p2.Y) : FindLane(p2.Y, p1.Y);
                var lineX = p1.X < p2.X ? FindLane(p1.X, p2.X) : FindLane(p2.X, p1.X);
                if (IsVertical)
                {
                    if (p1.X < p2.X)
                    {
                        path.Line(lineX - Radius, p1.Y).Arc(Radius, sweep: true, lineX, p1.Y + Radius).Line(lineX, p2.Y - Radius).Arc(Radius, sweep: false, lineX + Radius, p2.Y);
                    }
                    else
                    {
                        colorBranch = commitA.Branch;
                        path.Line(lineX + Radius, p1.Y).Arc(Radius, sweep: false, lineX, p1.Y + Radius).Line(lineX, p2.Y - Radius).Arc(Radius, sweep: true, lineX - Radius, p2.Y);
                    }
                }
                else if (p1.Y < p2.Y)
                {
                    path.Line(p1.X, lineY - Radius).Arc(Radius, sweep: false, p1.X + Radius, lineY).Line(p2.X - Radius, lineY).Arc(Radius, sweep: true, p2.X, lineY + Radius);
                }
                else
                {
                    colorBranch = commitA.Branch;
                    path.Line(p1.X, lineY + Radius).Arc(Radius, sweep: true, p1.X + Radius, lineY).Line(p2.X - Radius, lineY).Arc(Radius, sweep: false, p2.X, lineY - Radius);
                }
            }
            else
            {
                const double Radius = 20;
                if (IsVertical)
                {
                    if (p1.X < p2.X)
                    {
                        if (isSecondParent)
                        {
                            path.Line(p1.X, p2.Y - Radius).Arc(Radius, sweep: false, p1.X + Radius, p2.Y);
                        }
                        else
                        {
                            path.Line(p2.X - Radius, p1.Y).Arc(Radius, sweep: true, p2.X, p1.Y + Radius);
                        }
                    }
                    else if (p1.X > p2.X)
                    {
                        if (isSecondParent)
                        {
                            path.Line(p1.X, p2.Y - Radius).Arc(Radius, sweep: true, p1.X - Radius, p2.Y);
                        }
                        else
                        {
                            path.Line(p2.X + Radius, p1.Y).Arc(Radius, sweep: false, p2.X, p1.Y + Radius);
                        }
                    }
                }
                else if (p1.Y < p2.Y)
                {
                    if (isSecondParent)
                    {
                        path.Line(p2.X - Radius, p1.Y).Arc(Radius, sweep: true, p2.X, p1.Y + Radius);
                    }
                    else
                    {
                        path.Line(p1.X, p2.Y - Radius).Arc(Radius, sweep: false, p1.X + Radius, p2.Y);
                    }
                }
                else if (p1.Y > p2.Y)
                {
                    if (isSecondParent)
                    {
                        path.Line(p2.X - Radius, p1.Y).Arc(Radius, sweep: false, p2.X, p1.Y - Radius);
                    }
                    else
                    {
                        path.Line(p1.X, p2.Y + Radius).Arc(Radius, sweep: true, p1.X + Radius, p2.Y);
                    }
                }
            }

            path.Line(p2.X, p2.Y);
            var index = _branchPositions.TryGetValue(colorBranch, out var branch) ? branch.Index : 0;
            target.Add(new ScenePath(path.Build(this)) { Stroke = colors.Branch(index), StrokeWidth = 8, LineCap = SceneLineCap.Round, CssClass = "arrow" });
            foreach (var point in path.Points)
            {
                Bounds.Add(Map(point));
            }
        }

        /// <summary>Indicates whether a commit of the branch the link turns on lies between its ends: the link then goes around it, in a lane of its own.</summary>
        private bool NeedsRerouting(GitGraphHistoryCommit commitA, GitGraphHistoryCommit commitB, LayoutPoint p1, LayoutPoint p2)
        {
            var commitBIsFurthest = IsVertical ? p1.X < p2.X : p1.Y < p2.Y;
            var branch = commitBIsFurthest ? commitB.Branch : commitA.Branch;
            foreach (var commit in history.Commits)
            {
                if (commit.Sequence > commitA.Sequence && commit.Sequence < commitB.Sequence && commit.Branch == branch)
                    return true;
            }

            return false;
        }

        private double FindLane(double y1, double y2, int depth = 0)
        {
            while (true)
            {
                var candidate = y1 + (Math.Abs(y1 - y2) / 2);
                if (depth > 5)
                    return candidate;

                if (_lanes.TrueForAll(lane => Math.Abs(lane - candidate) >= 10))
                {
                    _lanes.Add(candidate);
                    return candidate;
                }

                y2 -= Math.Abs(y1 - y2) / 5;
                depth++;
            }
        }

        public void DrawCommits(SceneGroup bullets, SceneGroup labels)
        {
            foreach (var commit in history.Commits)
            {
                if (!_positions.TryGetValue(commit, out var layoutPoint))
                    continue;

                var point = Map(layoutPoint);
                var index = _branchPositions[commit.Branch].Index;
                DrawBullet(bullets, commit, point, index);
                if (diagram.ShowCommitLabels && commit.Kind is not GitGraphHistoryCommitKind.CherryPick && (commit.Kind is not GitGraphHistoryCommitKind.Merge || commit.IsCustomId))
                {
                    DrawCommitLabel(labels, commit, point);
                }

                if (commit.Tags.Count > 0)
                {
                    DrawTags(labels, commit, point);
                }
            }
        }

        private void DrawBullet(SceneGroup target, GitGraphHistoryCommit commit, LayoutPoint point, int index)
        {
            var theme = context.Theme;
            var commitGroup = target.Add(new SceneGroup { CssClass = "commit" });
            commitGroup.SetDataAttribute("id", commit.Id);
            var (x, y) = (point.X, point.Y);
            var symbol = commit.Type ?? commit.Kind switch
            {
                GitGraphHistoryCommitKind.Merge => (GitGraphCommitType?)null,
                _ => GitGraphCommitType.Normal,
            };
            if (commit.Kind is GitGraphHistoryCommitKind.CherryPick)
            {
                var background = context.Background;
                commitGroup.Add(new SceneCircle(x, y, 10) { Fill = theme.LineColor, CssClass = "commit-cherry-pick" });
                commitGroup.Add(new SceneCircle(x - 3, y + 2, 2.75) { Fill = background });
                commitGroup.Add(new SceneCircle(x + 3, y + 2, 2.75) { Fill = background });
                commitGroup.Add(new SceneLine(x + 3, y + 1, x, y - 5) { Stroke = background });
                commitGroup.Add(new SceneLine(x - 3, y + 1, x, y - 5) { Stroke = background });
            }
            else if (symbol is GitGraphCommitType.Highlight)
            {
                commitGroup.Add(new SceneRectangle(x - 10, y - 10, 20, 20) { Fill = colors.Highlight(index), Stroke = colors.Highlight(index), CssClass = "commit-highlight" });
                commitGroup.Add(new SceneRectangle(x - 6, y - 6, 12, 12) { Fill = theme.PrimaryColor, Stroke = theme.PrimaryColor });
            }
            else
            {
                commitGroup.Add(new SceneCircle(x, y, 10) { Fill = colors.Branch(index), Stroke = colors.Branch(index), CssClass = symbol is null ? "commit-merge" : symbol is GitGraphCommitType.Reverse ? "commit-reverse" : "commit-normal" });
                if (symbol is null)
                {
                    commitGroup.Add(new SceneCircle(x, y, 6) { Fill = theme.PrimaryColor, Stroke = theme.PrimaryColor });
                }
                else if (symbol is GitGraphCommitType.Reverse)
                {
                    commitGroup.Add(new ScenePath(new PathBuilder().MoveTo(x - 5, y - 5).LineTo(x + 5, y + 5).MoveTo(x - 5, y + 5).LineTo(x + 5, y - 5).Build()) { Stroke = theme.PrimaryColor, StrokeWidth = 3 });
                }
            }

            Bounds.Add(new SceneBounds(x - 10, y - 10, 20, 20));
        }

        private void DrawCommitLabel(SceneGroup target, GitGraphHistoryCommit commit, LayoutPoint point)
        {
            var metrics = Measure(commit.Id, LabelFont);
            var wrapper = new SceneGroup { CssClass = "commit-label" };
            SceneBounds background;
            double textX;
            double baseline;
            if (IsVertical)
            {
                background = new SceneBounds(point.X - (metrics.Width + (4 * PaddingX) + 5), point.Y - 12, metrics.Width + (2 * PaddingY), metrics.Height + (2 * PaddingY));
                textX = point.X - (metrics.Width + (4 * PaddingX));
                baseline = background.Y + PaddingY + metrics.Ascent;
                if (diagram.RotateCommitLabels)
                {
                    wrapper.Transform = SceneTransform.Rotate(-45, point.X, point.Y);
                }
            }
            else
            {
                background = new SceneBounds(point.X - (metrics.Width / 2) - PaddingY, point.Y + 13.5, metrics.Width + (2 * PaddingY), metrics.Height + (2 * PaddingY));
                textX = point.X - (metrics.Width / 2);
                baseline = background.Y + PaddingY + metrics.Ascent;
                if (diagram.RotateCommitLabels)
                {
                    var rotation = SceneTransform.Rotate(-45, point.X - LayoutOffset, point.Y);
                    wrapper.Transform = rotation with
                    {
                        E = rotation.E - 7.5 - ((metrics.Width + 10) / 25 * 9.5),
                        F = rotation.F + 10 + (metrics.Width / 25 * 8.5),
                    };
                }
            }

            wrapper.Add(new SceneRectangle(background.X, background.Y, background.Width, background.Height) { Fill = colors.CommitLabelBackground, Opacity = 0.5 });
            wrapper.Add(new SceneText(textX, baseline, commit.Id, LabelFont, colors.CommitLabel) { MeasuredWidth = metrics.Width });
            target.Add(wrapper);
            AddBounds(wrapper.Transform, background);
        }

        private void DrawTags(SceneGroup target, GitGraphHistoryCommit commit, LayoutPoint point)
        {
            var tags = commit.Tags.AsEnumerable().Reverse().ToList();
            var metrics = tags.Select(tag => Measure(tag, LabelFont)).ToList();
            var maxWidth = metrics.Max(item => item.Width);
            var halfHeight = metrics.Max(item => item.Height) / 2;
            var position = (IsVertical ? point.Y : point.X) - LayoutOffset;
            for (var i = 0; i < tags.Count; i++)
            {
                var yOffset = i * 20.0;
                var tagGroup = target.Add(new SceneGroup { CssClass = "tag" });
                PathData outline;
                LayoutPoint hole;
                double textX;
                double baseline;
                if (IsVertical)
                {
                    var origin = position + yOffset;
                    var x = point.X;
                    outline = Geometry.Polygon((x, origin + 2), (x, origin - 2), (x + LayoutOffset, origin - halfHeight - 2), (x + LayoutOffset + maxWidth + 4, origin - halfHeight - 2), (x + LayoutOffset + maxWidth + 4, origin + halfHeight + 2), (x + LayoutOffset, origin + halfHeight + 2));
                    hole = new LayoutPoint(x + (PaddingX / 2), origin);
                    textX = x + 5;
                    baseline = origin + 3;
                    var rotation = SceneTransform.Rotate(45, x, position);
                    tagGroup.Transform = rotation with { E = rotation.E + 12, F = rotation.F + 12 };
                    var textGroup = tagGroup;
                    tagGroup.Add(new ScenePath(outline) { Fill = colors.TagLabelBackground, Stroke = colors.TagLabelBorder });
                    tagGroup.Add(new SceneCircle(hole.X, hole.Y, 1.5) { Fill = context.Theme.TextColor });

                    // The text is 2 pixels further than its box, as in Mermaid
                    var text = textGroup.Add(new SceneGroup { Transform = SceneTransform.Translate(2, 2) });
                    text.Add(new SceneText(textX, baseline, tags[i], LabelFont, colors.TagLabel) { MeasuredWidth = metrics[i].Width });
                    AddBounds(tagGroup.Transform, new SceneBounds(x, origin - halfHeight - 2, LayoutOffset + maxWidth + 6, (2 * halfHeight) + 4));
                }
                else
                {
                    var ly = point.Y - 19.2 - yOffset;
                    outline = Geometry.Polygon(
                        (position - (maxWidth / 2) - (PaddingX / 2), ly + PaddingY),
                        (position - (maxWidth / 2) - (PaddingX / 2), ly - PaddingY),
                        (point.X - (maxWidth / 2) - PaddingX, ly - halfHeight - PaddingY),
                        (point.X + (maxWidth / 2) + PaddingX, ly - halfHeight - PaddingY),
                        (point.X + (maxWidth / 2) + PaddingX, ly + halfHeight + PaddingY),
                        (point.X - (maxWidth / 2) - PaddingX, ly + halfHeight + PaddingY));
                    tagGroup.Add(new ScenePath(outline) { Fill = colors.TagLabelBackground, Stroke = colors.TagLabelBorder });
                    tagGroup.Add(new SceneCircle(position - (maxWidth / 2) + (PaddingX / 2), ly, 1.5) { Fill = context.Theme.TextColor });
                    tagGroup.Add(new SceneText(point.X - (metrics[i].Width / 2), point.Y - 16 - yOffset, tags[i], LabelFont, colors.TagLabel) { MeasuredWidth = metrics[i].Width });
                    Bounds.Add(new SceneBounds(position - (maxWidth / 2) - (PaddingX / 2), ly - halfHeight - PaddingY, maxWidth + (PaddingX * 1.5) + 10, (2 * halfHeight) + (2 * PaddingY)));
                }
            }
        }

        private void AddBounds(SceneTransform? transform, SceneBounds bounds)
        {
            if (transform is not { } value)
            {
                Bounds.Add(bounds);
                return;
            }

            foreach (var (x, y) in new[] { (bounds.X, bounds.Y), (bounds.Right, bounds.Y), (bounds.X, bounds.Bottom), (bounds.Right, bounds.Bottom) })
            {
                var (tx, ty) = value.Apply(x, y);
                Bounds.Add(new LayoutPoint(tx, ty));
            }
        }

        private TextMetrics Measure(string text, SceneFont font) => context.Measurer.Measure(text, font);

        /// <summary>A link with right angles and arcs, in the top-to-bottom layout, mirrored when drawn bottom to top.</summary>
        private sealed class ArrowPath(LayoutPoint start)
        {
            private readonly List<(LayoutPoint Point, double Radius, bool Sweep)> _segments = [(start, 0, false)];

            public IEnumerable<LayoutPoint> Points => _segments.Select(segment => segment.Point);

            public ArrowPath Line(double x, double y)
            {
                _segments.Add((new LayoutPoint(x, y), 0, false));
                return this;
            }

            public ArrowPath Arc(double radius, bool sweep, double x, double y)
            {
                _segments.Add((new LayoutPoint(x, y), radius, sweep));
                return this;
            }

            public PathData Build(State state)
            {
                var first = state.Map(_segments[0].Point);
                var builder = new PathBuilder().MoveTo(first.X, first.Y);
                for (var i = 1; i < _segments.Count; i++)
                {
                    var (point, radius, sweep) = _segments[i];
                    var mapped = state.Map(point);
                    if (radius > 0)
                    {
                        // A mirror turns the arcs the other way
                        builder.ArcTo(radius, radius, 0, largeArc: false, sweep: state.IsBottomToTop ? !sweep : sweep, mapped.X, mapped.Y);
                    }
                    else
                    {
                        builder.LineTo(mapped.X, mapped.Y);
                    }
                }

                return builder.Build();
            }
        }
    }
}
