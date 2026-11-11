using System.Globalization;
using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>The state of the drawing of a sequence diagram: the positions of the participants and the vertical cursor.</summary>
internal sealed class SequenceLayout
{
    private const double HeadWidth = 150;
    private const double HeadHeight = 65;
    private const double ActorMargin = 50;
    private const double BoxMargin = 10;
    private const double MessageGap = 10;
    private const double ActivationWidth = 10;
    private const double NoteMargin = 10;
    private const double NoteMinWidth = 100;
    private const double SymbolHeight = 44;
    private const double MaxDepth = 64;

    private readonly SequenceDiagram _diagram;
    private readonly DiagramRenderContext _context;
    private readonly DiagramTheme _theme;
    private readonly SceneGroup _root;
    private readonly List<SequenceParticipant> _participants = [];
    private readonly Dictionary<SequenceParticipant, int> _index = new(ReferenceEqualityComparer.Instance);
    private readonly List<TextBlock> _labels = [];
    private readonly List<double> _widths = [];
    private readonly List<double> _heights = [];
    private readonly List<double> _centers = [];
    private readonly Dictionary<SequenceParticipant, Stack<double>> _activations = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SequenceParticipant, double> _lifelineStarts = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<SequenceParticipant, double> _lifelineEnds = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<SequenceParticipant> _created = new(ReferenceEqualityComparer.Instance);
    private readonly BoundsBuilder _bounds = new();
    private SceneGroup _boxesLayer = null!;
    private SceneGroup _backgroundsLayer = null!;
    private SceneGroup _lifelinesLayer = null!;
    private SceneGroup _activationsLayer = null!;
    private SceneGroup _framesLayer = null!;
    private SceneGroup _messagesLayer = null!;
    private SceneGroup _notesLayer = null!;
    private SceneGroup _headsLayer = null!;
    private double _y;
    private double _top;
    private double _rowHeight;
    private bool _numbering;
    private decimal _number = 1;
    private decimal _increment = 1;

    public SequenceLayout(SequenceDiagram diagram, DiagramRenderContext context, SceneGroup root)
    {
        _diagram = diagram;
        _context = context;
        _theme = context.Theme;
        _root = root;
    }

    private double ActorFontSize => _theme.FontSize * 0.875;

    public SceneBounds Run()
    {
        _boxesLayer = _root.Add(new SceneGroup { CssClass = "boxes" });
        _backgroundsLayer = _root.Add(new SceneGroup { CssClass = "rects" });
        _lifelinesLayer = _root.Add(new SceneGroup { CssClass = "lifelines" });
        _activationsLayer = _root.Add(new SceneGroup { CssClass = "activations" });
        _framesLayer = _root.Add(new SceneGroup { CssClass = "blocks" });
        _messagesLayer = _root.Add(new SceneGroup { CssClass = "messages" });
        _notesLayer = _root.Add(new SceneGroup { CssClass = "notes" });
        _headsLayer = _root.Add(new SceneGroup { CssClass = "participants" });

        CollectParticipants();
        PlaceParticipants();

        // The boxes of participants with a title push the participants down
        var boxTitles = _diagram.Boxes.Select(box => box.Title is { IsEmpty: false } title ? _context.Text(title, ActorFontSize, bold: true) : null).ToList();
        var titleHeight = boxTitles.Max(title => title?.Height) ?? 0;
        _top = _diagram.Boxes.Count > 0 ? BoxMargin + (titleHeight > 0 ? titleHeight + 5 : 0) : 0;
        _rowHeight = _heights.Count is 0 ? 0 : _heights.Max();
        for (var i = 0; i < _participants.Count; i++)
        {
            var participant = _participants[i];
            if (_created.Contains(participant))
                continue;

            DrawHead(i, _top, isBottom: false);
            _lifelineStarts[participant] = _top + _heights[i];
        }

        _y = _top + _rowHeight + 10;
        DrawSteps(_diagram.Steps, depth: 0);

        // The activations that are not closed end at the bottom
        foreach (var (participant, starts) in _activations)
        {
            while (starts.Count > 0)
            {
                EndActivation(participant, _y);
            }
        }

        _y += 10;
        var bottom = _y;
        for (var i = 0; i < _participants.Count; i++)
        {
            var participant = _participants[i];
            var start = _lifelineStarts.GetValueOrDefault(participant, _top + _heights[i]);
            var end = _lifelineEnds.TryGetValue(participant, out var destroyed) ? destroyed : bottom;
            _lifelinesLayer.Add(new SceneLine(_centers[i], start, _centers[i], end) { Stroke = _theme.LineColor, StrokeWidth = 0.5, CssClass = "lifeline" });
            if (_diagram.MirrorParticipants && !_lifelineEnds.ContainsKey(participant))
            {
                DrawHead(i, bottom, isBottom: true);
            }
        }

        var contentBottom = _diagram.MirrorParticipants ? bottom + _rowHeight : bottom;
        DrawBoxes(boxTitles, contentBottom);
        return _bounds.ToBounds();
    }

    private void CollectParticipants()
    {
        void Add(SequenceParticipant participant)
        {
            if (_index.ContainsKey(participant))
                return;

            _index[participant] = _participants.Count;
            _participants.Add(participant);
            var label = _context.Text(participant.Label ?? participant.Id, ActorFontSize);
            _labels.Add(label);
            _widths.Add(Math.Max(HeadWidth, label.Width + 20));
            _heights.Add(IsSymbol(participant.Kind) ? SymbolHeight + 4 + label.Height : Math.Max(HeadHeight, label.Height + 20));
        }

        foreach (var participant in _diagram.Participants)
        {
            Add(participant);
        }

        foreach (var box in _diagram.Boxes)
        {
            foreach (var participant in box.Participants)
            {
                Add(participant);
            }
        }

        foreach (var step in _diagram.EnumerateSteps())
        {
            switch (step)
            {
                case SequenceMessage message:
                    Add(message.From);
                    Add(message.To);
                    if (message.CreatesReceiver)
                    {
                        _created.Add(message.To);
                    }

                    break;
                case SequenceNote note:
                    foreach (var participant in note.Participants)
                    {
                        Add(participant);
                    }

                    break;
                case SequenceActivation activation:
                    Add(activation.Participant);
                    break;
            }
        }
    }

    private static bool IsSymbol(SequenceParticipantKind kind) => kind is SequenceParticipantKind.Actor or SequenceParticipantKind.Boundary or SequenceParticipantKind.Control or SequenceParticipantKind.Entity or SequenceParticipantKind.Database;

    /// <summary>Places the participants from the left: each one as far from the previous ones as their messages and notes need.</summary>
    private void PlaceParticipants()
    {
        var count = _participants.Count;
        var needs = new Dictionary<(int, int), double>();
        void Need(int a, int b, double distance)
        {
            if (a == b || a < 0 || b >= count || !double.IsFinite(distance))
                return;

            var key = a < b ? (a, b) : (b, a);
            needs[key] = Math.Max(needs.GetValueOrDefault(key), distance);
        }

        var numbering = false;
        foreach (var step in _diagram.EnumerateSteps())
        {
            switch (step)
            {
                case SequenceAutonumber autonumber:
                    numbering = autonumber.IsEnabled;
                    break;
                case SequenceMessage message:
                    {
                        var width = MessageText(message).Width + (numbering ? 20 : 0);
                        var from = _index[message.From];
                        var to = _index[message.To];
                        if (from == to)
                        {
                            Need(from, from + 1, width + 50 + (_widths[from + 1 < count ? from + 1 : from] / 2));
                        }
                        else
                        {
                            Need(from, to, width + 40);
                        }

                        break;
                    }

                case SequenceNote note when note.Participants.Count > 0:
                    {
                        var width = NoteWidth(note);
                        var first = _index[note.Participants[0]];
                        var last = _index[note.Participants[^1]];
                        switch (note.Position)
                        {
                            case SequenceNotePosition.RightOf:
                                Need(first, first + 1, 25 + width + 25 + (first + 1 < count ? _widths[first + 1] / 2 : 0));
                                break;
                            case SequenceNotePosition.LeftOf:
                                Need(first - 1, first, 25 + width + 25 + (first > 0 ? _widths[first - 1] / 2 : 0));
                                break;
                            default:
                                Need(first, last, width - 50);
                                break;
                        }

                        break;
                    }
            }
        }

        for (var i = 0; i < count; i++)
        {
            var center = i == 0 ? _widths[0] / 2 : _centers[i - 1] + (_widths[i - 1] / 2) + ActorMargin + (_widths[i] / 2);
            for (var j = 0; j < i; j++)
            {
                if (needs.TryGetValue((j, i), out var distance))
                {
                    center = Math.Max(center, _centers[j] + distance);
                }
            }

            _centers.Add(center);
        }
    }

    private TextBlock MessageText(SequenceMessage message) => _context.Text(message.Text);

    private TextBlock NoteText(SequenceNote note) => _context.Text(note.Text, ActorFontSize);

    private double NoteWidth(SequenceNote note) => Math.Max(NoteText(note).Width + (2 * NoteMargin), NoteMinWidth);

    private void DrawSteps(IList<SequenceStep> steps, int depth)
    {
        foreach (var step in steps)
        {
            switch (step)
            {
                case SequenceMessage message:
                    DrawMessage(message);
                    break;
                case SequenceNote note:
                    DrawNote(note);
                    break;
                case SequenceActivation activation when activation.IsActivation:
                    StartActivation(activation.Participant, _y);
                    break;
                case SequenceActivation activation:
                    EndActivation(activation.Participant, _y);
                    break;
                case SequenceAutonumber autonumber:
                    _numbering = autonumber.IsEnabled;
                    _number = autonumber.Start ?? _number;
                    _increment = autonumber.Increment ?? _increment;
                    break;
                case SequenceBlock block when depth < MaxDepth:
                    DrawBlock(block, depth);
                    break;
            }
        }
    }

    private int Depth(SequenceParticipant participant) => _activations.TryGetValue(participant, out var starts) ? starts.Count : 0;

    private void StartActivation(SequenceParticipant participant, double y)
    {
        if (!_activations.TryGetValue(participant, out var starts))
        {
            starts = new Stack<double>();
            _activations[participant] = starts;
        }

        starts.Push(y);
    }

    private void EndActivation(SequenceParticipant participant, double y)
    {
        if (!_activations.TryGetValue(participant, out var starts) || starts.Count is 0 || !_index.TryGetValue(participant, out var index))
            return;

        var level = starts.Count - 1;
        var start = starts.Pop();
        var end = Math.Max(y, start + 10);
        var x = _centers[index] - (ActivationWidth / 2) + (level * ActivationWidth / 2);
        _activationsLayer.Add(new SceneRectangle(x, start, ActivationWidth, end - start) { Fill = _theme.AlternateBackground, Stroke = _theme.NodeBorderColor, StrokeWidth = 1, CssClass = "activation" });
        _bounds.Add(new SceneBounds(x, start, ActivationWidth, end - start));
    }

    /// <summary>Returns where a message leaves or reaches a lifeline: the edge of the activation bar on the side of the other end.</summary>
    private double LifelineX(SequenceParticipant participant, bool towardRight)
    {
        var center = _centers[_index[participant]];
        var depth = Depth(participant);
        if (depth is 0)
            return center;

        return towardRight ? center + (ActivationWidth / 2) + ((depth - 1) * ActivationWidth / 2) : center - (ActivationWidth / 2);
    }

    private void DrawMessage(SequenceMessage message)
    {
        var text = MessageText(message);
        _y += MessageGap;
        var textTop = _y;
        _y += text.Height;
        var lineY = _y + 8;
        var fromIndex = _index[message.From];
        var toIndex = _index[message.To];
        var color = _theme.LineColor;
        var dashes = message.LineStyle is SequenceLineStyle.Dotted ? new double[] { 3, 3 } : null;
        var messageGroup = _messagesLayer.Add(new SceneGroup { CssClass = message.LineStyle is SequenceLineStyle.Dotted ? "message dotted" : "message" });
        LayoutPoint start;
        LayoutPoint end;
        if (fromIndex == toIndex)
        {
            start = new LayoutPoint(LifelineX(message.From, towardRight: true), lineY);
            end = new LayoutPoint(start.X, lineY + 20);
            messageGroup.Add(new ScenePath(new PathBuilder().MoveTo(start.X, start.Y).CubicTo(start.X + 60, start.Y - 10, start.X + 60, start.Y + 30, end.X, end.Y).Build()) { Stroke = color, StrokeWidth = 1.5, StrokeDashArray = dashes });
            DrawHead(messageGroup, message.EndArrowHead, end, new LayoutPoint(end.X + 30, end.Y + 2), color);
            DrawHead(messageGroup, message.StartArrowHead, start, new LayoutPoint(start.X + 30, start.Y - 2), color);
            text.Draw(messageGroup, start.X + 5 + (text.Width / 2), textTop + (text.Height / 2), _theme.TextColor);
            _bounds.Add(new SceneBounds(start.X, textTop, Math.Max(text.Width + 5, 50), lineY + 20 - textTop));
            _y = lineY + 20 + 12;
        }
        else
        {
            var towardRight = toIndex > fromIndex;
            start = new LayoutPoint(LifelineX(message.From, towardRight), lineY);
            var toX = LifelineX(message.To, !towardRight);
            if (message.CreatesReceiver && !_lifelineStarts.ContainsKey(message.To))
            {
                // A created participant appears at its message, which reaches the side of its box
                var headTop = lineY - (_heights[toIndex] / 2);
                DrawHead(toIndex, headTop, isBottom: false);
                _lifelineStarts[message.To] = headTop + _heights[toIndex];
                var halfWidth = IsSymbol(message.To.Kind) ? 20 : _widths[toIndex] / 2;
                toX = _centers[toIndex] + (towardRight ? -halfWidth : halfWidth);
            }

            end = new LayoutPoint(toX, lineY);
            var lineStart = start;
            var lineEnd = end;
            if (message.SenderConnection)
            {
                DrawConnection(messageGroup, start, color);
                lineStart = new LayoutPoint(start.X + (towardRight ? 4 : -4), lineY);
            }

            if (message.ReceiverConnection)
            {
                DrawConnection(messageGroup, end, color);
                lineEnd = new LayoutPoint(end.X + (towardRight ? -4 : 4), lineY);
            }

            messageGroup.Add(new SceneLine(lineStart.X, lineY, lineEnd.X, lineY) { Stroke = color, StrokeWidth = 1.5, StrokeDashArray = dashes });
            DrawHead(messageGroup, message.EndArrowHead, lineEnd, lineStart, color);
            DrawHead(messageGroup, message.StartArrowHead, lineStart, lineEnd, color);
            text.Draw(messageGroup, (start.X + end.X) / 2, textTop + (text.Height / 2), _theme.TextColor);
            _bounds.Add(new SceneBounds(((start.X + end.X) / 2) - (text.Width / 2), textTop, text.Width, text.Height));
            _bounds.Add(start);
            _bounds.Add(end);
            _y = Math.Max(lineY + 12, _lifelineStarts.TryGetValue(message.To, out var lifelineStart) && message.CreatesReceiver ? lifelineStart + 10 : 0);
        }

        if (_numbering)
        {
            var number = _context.Text(_number.ToString("0.##", CultureInfo.InvariantCulture), _theme.FontSize * 0.75, bold: true);
            var radius = Math.Max(9, (number.Width / 2) + 3);
            messageGroup.Add(new SceneCircle(start.X, start.Y, radius) { Fill = color, CssClass = "sequence-number" });
            number.Draw(messageGroup, start.X, start.Y, _context.Background);
            _bounds.Add(new SceneBounds(start.X - radius, start.Y - radius, 2 * radius, 2 * radius));
            _number += _increment;
        }

        if (message.ActivatesReceiver)
        {
            StartActivation(message.To, lineY);
        }

        if (message.DeactivatesSender)
        {
            EndActivation(message.From, lineY);
        }

        if (message.DestroyedParticipant is { } destroyed && _index.TryGetValue(destroyed, out var destroyedIndex))
        {
            // The lifeline of a destroyed participant ends at a cross
            var x = _centers[destroyedIndex];
            var crossY = lineY + (fromIndex == toIndex ? 20 : 0);
            _lifelineEnds[destroyed] = crossY;
            messageGroup.Add(new ScenePath(new PathBuilder().MoveTo(x - 9, crossY - 9).LineTo(x + 9, crossY + 9).MoveTo(x - 9, crossY + 9).LineTo(x + 9, crossY - 9).Build()) { Stroke = color, StrokeWidth = 2, CssClass = "destroy" });
            _bounds.Add(new SceneBounds(x - 9, crossY - 9, 18, 18));
        }
    }

    private static void DrawConnection(SceneGroup group, LayoutPoint point, SceneColor color) => group.Add(new SceneCircle(point.X, point.Y, 4) { Fill = color, CssClass = "connection" });

    /// <summary>Draws the decoration of an end of a message, its tip on <paramref name="tip"/>, the line coming from <paramref name="from"/>.</summary>
    private static void DrawHead(SceneGroup group, SequenceArrowHead head, LayoutPoint tip, LayoutPoint from, SceneColor color)
    {
        if (head is SequenceArrowHead.None)
            return;

        var length = Geometry.Distance(tip, from);
        if (length < 1e-9)
            return;

        var ux = (tip.X - from.X) / length;
        var uy = (tip.Y - from.Y) / length;

        // "Top" is on the left of the direction of the line, as the markers of Mermaid that turn with it
        LayoutPoint At(double back, double up) => new(tip.X - (ux * back) + (uy * up), tip.Y - (uy * back) - (ux * up));
        var top = At(10, 5);
        var bottom = At(10, -5);
        switch (head)
        {
            case SequenceArrowHead.Arrow:
                group.Add(new ScenePath(Polygon(tip, top, bottom)) { Fill = color, CssClass = "arrowhead" });
                break;
            case SequenceArrowHead.OpenArrow:
                group.Add(new ScenePath(new PathBuilder().MoveTo(top.X, top.Y).LineTo(tip.X, tip.Y).LineTo(bottom.X, bottom.Y).Build()) { Stroke = color, StrokeWidth = 1.5, CssClass = "arrowhead" });
                break;
            case SequenceArrowHead.Cross:
                {
                    var a = At(9, 4.5);
                    var b = At(0, -4.5);
                    var c = At(9, -4.5);
                    var d = At(0, 4.5);
                    group.Add(new ScenePath(new PathBuilder().MoveTo(a.X, a.Y).LineTo(b.X, b.Y).MoveTo(c.X, c.Y).LineTo(d.X, d.Y).Build()) { Stroke = color, StrokeWidth = 1.5, CssClass = "arrowhead" });
                    break;
                }

            case SequenceArrowHead.HalfArrowTop:
                group.Add(new ScenePath(Polygon(tip, top, At(10, 0))) { Fill = color, CssClass = "arrowhead" });
                break;
            case SequenceArrowHead.HalfArrowBottom:
                group.Add(new ScenePath(Polygon(tip, bottom, At(10, 0))) { Fill = color, CssClass = "arrowhead" });
                break;
            case SequenceArrowHead.StickTop:
                group.Add(new SceneLine(tip.X, tip.Y, top.X, top.Y) { Stroke = color, StrokeWidth = 1.5, CssClass = "arrowhead" });
                break;
            case SequenceArrowHead.StickBottom:
                group.Add(new SceneLine(tip.X, tip.Y, bottom.X, bottom.Y) { Stroke = color, StrokeWidth = 1.5, CssClass = "arrowhead" });
                break;
        }
    }

    private static PathData Polygon(LayoutPoint a, LayoutPoint b, LayoutPoint c) => Geometry.Polygon((a.X, a.Y), (b.X, b.Y), (c.X, c.Y));

    private void DrawNote(SequenceNote note)
    {
        if (note.Participants.Count is 0)
            return;

        var text = NoteText(note);
        var width = NoteWidth(note);
        var height = text.Height + (2 * NoteMargin);
        var first = _centers[_index[note.Participants[0]]];
        var last = _centers[_index[note.Participants[^1]]];
        double left;
        switch (note.Position)
        {
            case SequenceNotePosition.RightOf:
                left = first + 25;
                break;
            case SequenceNotePosition.LeftOf:
                left = first - 25 - width;
                break;
            default:
                var from = Math.Min(first, last);
                var to = Math.Max(first, last);
                if (from == to)
                {
                    left = from - (width / 2);
                }
                else
                {
                    width = Math.Max(width, to - from + 50);
                    left = ((from + to) / 2) - (width / 2);
                }

                break;
        }

        _y += NoteMargin;
        var noteGroup = _notesLayer.Add(new SceneGroup { CssClass = "note" });
        noteGroup.Add(new SceneRectangle(left, _y, width, height) { Fill = _theme.NoteBackground, Stroke = _theme.NoteBorderColor, StrokeWidth = 1 });
        text.Draw(noteGroup, left + (width / 2), _y + (height / 2), _theme.NoteTextColor);
        _bounds.Add(new SceneBounds(left, _y, width, height));
        _y += height + NoteMargin;
    }

    private void DrawBlock(SequenceBlock block, int depth)
    {
        var (left, right) = Extent(block, 0);
        if (block.Kind is SequenceBlockKind.Rectangle)
        {
            _y += BoxMargin;
            var rectTop = _y;
            foreach (var section in block.Sections)
            {
                DrawSteps(section.Steps, depth + 1);
            }

            _y += BoxMargin;
            var fill = block.Color ?? SceneColor.Parse("rgba(128, 128, 128, 0.25)");
            _backgroundsLayer.Add(new SceneRectangle(left, rectTop, right - left, _y - rectTop) { Fill = fill, CssClass = "rect" });
            _bounds.Add(new SceneBounds(left, rectTop, right - left, _y - rectTop));
            return;
        }

        var keyword = block.Kind switch
        {
            SequenceBlockKind.Alternative => "alt",
            SequenceBlockKind.Optional => "opt",
            SequenceBlockKind.Parallel or SequenceBlockKind.ParallelOverlap => "par",
            SequenceBlockKind.Critical => "critical",
            SequenceBlockKind.Break => "break",
            _ => "loop",
        };
        var tagText = _context.Text(keyword, ActorFontSize, bold: true);
        var tagWidth = Math.Max(50, tagText.Width + 16);
        var tagHeight = Math.Max(20, tagText.Height + 4);
        var labels = block.Sections.Select(section => section.Label is { IsEmpty: false } label ? _context.Text(new DiagramText("[" + label.Text + "]", label.Format), ActorFontSize) : null).ToList();
        right = Math.Max(right, left + tagWidth + (labels.Count > 0 && labels[0] is { } firstLabel ? firstLabel.Width + 20 : 0));
        for (var i = 1; i < labels.Count; i++)
        {
            right = Math.Max(right, left + (labels[i]?.Width ?? 0) + 20);
        }

        _y += BoxMargin;
        var top = _y;
        var frameGroup = _framesLayer.Add(new SceneGroup { CssClass = "block " + keyword });
        var stroke = _theme.NodeBorderColor;
        frameGroup.Add(new ScenePath(new PathBuilder().MoveTo(left, top).LineTo(left + tagWidth, top).LineTo(left + tagWidth, top + tagHeight - 6).LineTo(left + tagWidth - 6, top + tagHeight).LineTo(left, top + tagHeight).Close().Build()) { Fill = _theme.NodeBackground, Stroke = stroke, StrokeWidth = 1 });
        tagText.Draw(frameGroup, left + (tagWidth / 2), top + (tagHeight / 2), _theme.NodeTextColor);
        var firstHeight = tagHeight;
        if (labels.Count > 0 && labels[0] is { } title)
        {
            title.Draw(frameGroup, left + tagWidth + ((right - left - tagWidth) / 2), top + 4 + (title.Height / 2), _theme.TextColor);
            firstHeight = Math.Max(firstHeight, title.Height + 4);
        }

        _y += firstHeight;
        var separators = new List<double>();
        for (var i = 0; i < block.Sections.Count; i++)
        {
            if (i > 0)
            {
                _y += BoxMargin;
                separators.Add(_y);
                if (labels[i] is { } label)
                {
                    label.Draw(frameGroup, (left + right) / 2, _y + 4 + (label.Height / 2), _theme.TextColor);
                    _y += label.Height + 4;
                }
            }

            DrawSteps(block.Sections[i].Steps, depth + 1);
        }

        _y += BoxMargin;
        var bottom = _y;
        frameGroup.Add(new SceneRectangle(left, top, right - left, bottom - top) { Stroke = stroke, StrokeWidth = 2, StrokeDashArray = [2, 2] });
        foreach (var separator in separators)
        {
            frameGroup.Add(new SceneLine(left, separator, right, separator) { Stroke = stroke, StrokeWidth = 2, StrokeDashArray = [3, 3] });
        }

        _bounds.Add(new SceneBounds(left, top, right - left, bottom - top));
    }

    /// <summary>Returns the horizontal extent of a block: the boxes of the participants of its steps, around its nested blocks.</summary>
    private (double Left, double Right) Extent(SequenceBlock block, int depth)
    {
        var left = double.PositiveInfinity;
        var right = double.NegativeInfinity;
        void AddParticipant(SequenceParticipant participant)
        {
            if (_index.TryGetValue(participant, out var index))
            {
                left = Math.Min(left, _centers[index] - (_widths[index] / 2));
                right = Math.Max(right, _centers[index] + (_widths[index] / 2));
            }
        }

        foreach (var step in block.Sections.SelectMany(section => section.Steps))
        {
            switch (step)
            {
                case SequenceMessage message:
                    AddParticipant(message.From);
                    AddParticipant(message.To);
                    if (message.From == message.To && _index.TryGetValue(message.From, out var selfIndex))
                    {
                        // The text of a message to oneself is on the right of the lifeline
                        right = Math.Max(right, _centers[selfIndex] + 20 + MessageText(message).Width + BoxMargin);
                    }

                    break;
                case SequenceNote note:
                    foreach (var participant in note.Participants)
                    {
                        AddParticipant(participant);
                    }

                    if (note.Participants.Count > 0 && _index.TryGetValue(note.Participants[0], out var noteIndex))
                    {
                        var noteWidth = NoteWidth(note);
                        if (note.Position is SequenceNotePosition.RightOf)
                        {
                            right = Math.Max(right, _centers[noteIndex] + 25 + noteWidth + BoxMargin);
                        }
                        else if (note.Position is SequenceNotePosition.LeftOf)
                        {
                            left = Math.Min(left, _centers[noteIndex] - 25 - noteWidth - BoxMargin);
                        }
                    }

                    break;
                case SequenceActivation activation:
                    AddParticipant(activation.Participant);
                    break;
                case SequenceBlock nested when depth < MaxDepth:
                    var (nestedLeft, nestedRight) = Extent(nested, depth + 1);
                    if (double.IsFinite(nestedLeft))
                    {
                        left = Math.Min(left, nestedLeft - BoxMargin);
                        right = Math.Max(right, nestedRight + BoxMargin);
                    }

                    break;
            }
        }

        if (!double.IsFinite(left))
        {
            // A block without participants spans all of them
            left = _participants.Count is 0 ? 0 : _centers[0] - (_widths[0] / 2);
            right = _participants.Count is 0 ? 100 : _centers[^1] + (_widths[^1] / 2);
        }

        return (left, right);
    }

    private void DrawHead(int index, double top, bool isBottom)
    {
        var participant = _participants[index];
        var label = _labels[index];
        var width = _widths[index];
        var height = _heights[index];
        var x = _centers[index];
        var headGroup = _headsLayer.Add(new SceneGroup { CssClass = isBottom ? "participant participant-bottom" : "participant participant-top" });
        headGroup.SetDataAttribute("id", participant.Id);
        var fill = _theme.NodeBackground;
        var stroke = _theme.NodeBorderColor;
        var textColor = _theme.NodeTextColor;
        switch (participant.Kind)
        {
            case SequenceParticipantKind.Actor:
                headGroup.Add(new SceneCircle(x, top + 9, 8) { Fill = fill, Stroke = stroke, StrokeWidth = 1.5 });
                headGroup.Add(new ScenePath(new PathBuilder()
                    .MoveTo(x, top + 17).LineTo(x, top + 32)
                    .MoveTo(x - 14, top + 22).LineTo(x + 14, top + 22)
                    .MoveTo(x - 11, top + SymbolHeight).LineTo(x, top + 32).LineTo(x + 11, top + SymbolHeight)
                    .Build()) { Stroke = stroke, StrokeWidth = 1.5 });
                break;
            case SequenceParticipantKind.Boundary:
                headGroup.Add(new SceneLine(x - 24, top + 6, x - 24, top + 38) { Stroke = stroke, StrokeWidth = 1.5 });
                headGroup.Add(new SceneLine(x - 24, top + 22, x - 10, top + 22) { Stroke = stroke, StrokeWidth = 1.5 });
                headGroup.Add(new SceneCircle(x + 5, top + 22, 15) { Fill = fill, Stroke = stroke, StrokeWidth = 1.5 });
                break;
            case SequenceParticipantKind.Control:
                headGroup.Add(new SceneCircle(x, top + 24, 15) { Fill = fill, Stroke = stroke, StrokeWidth = 1.5 });
                headGroup.Add(new ScenePath(new PathBuilder().MoveTo(x + 5, top + 4).LineTo(x - 3, top + 9).LineTo(x + 5, top + 14).Build()) { Stroke = stroke, StrokeWidth = 1.5 });
                break;
            case SequenceParticipantKind.Entity:
                headGroup.Add(new SceneCircle(x, top + 20, 15) { Fill = fill, Stroke = stroke, StrokeWidth = 1.5 });
                headGroup.Add(new SceneLine(x - 15, top + 38, x + 15, top + 38) { Stroke = stroke, StrokeWidth = 1.5 });
                break;
            case SequenceParticipantKind.Database:
                {
                    var cylinder = Shapes.Create(FlowchartNodeShape.Cylinder, 10, SymbolHeight - (2 * Shapes.PaddingY) - 10);
                    StyleHelpers.DrawShape(headGroup, cylinder, x, top + (SymbolHeight / 2), fill, stroke, 1.5, dashes: null);
                    break;
                }

            case SequenceParticipantKind.Collections:
                headGroup.Add(new SceneRectangle(x - (width / 2) + 6, top - 6, width, height) { Fill = fill, Stroke = stroke, StrokeWidth = 1 });
                headGroup.Add(new SceneRectangle(x - (width / 2), top, width, height) { Fill = fill, Stroke = stroke, StrokeWidth = 1 });
                break;
            case SequenceParticipantKind.Queue:
                {
                    var queue = Shapes.Create(FlowchartNodeShape.HorizontalCylinder, width - (2 * Shapes.PaddingX) - 20, height - (2 * Shapes.PaddingY));
                    StyleHelpers.DrawShape(headGroup, queue, x, top + (height / 2), fill, stroke, 1, dashes: null);
                    break;
                }

            default:
                {
                    var box = headGroup.Add(new SceneGroup { Transform = SceneTransform.Translate(x, top + (height / 2)) });
                    box.Add(new ScenePath(Geometry.RoundedRectangle(width, height, 3)) { Fill = fill, Stroke = stroke, StrokeWidth = 1 });
                    break;
                }
        }

        if (IsSymbol(participant.Kind))
        {
            label.Draw(headGroup, x, top + SymbolHeight + 4 + (label.Height / 2), _theme.TextColor);
        }
        else
        {
            label.Draw(headGroup, x, top + (height / 2), textColor);
        }

        _bounds.Add(new SceneBounds(x - (width / 2), top - (participant.Kind is SequenceParticipantKind.Collections ? 6 : 0), width + (participant.Kind is SequenceParticipantKind.Collections ? 6 : 0), height + (participant.Kind is SequenceParticipantKind.Collections ? 6 : 0)));
    }

    private void DrawBoxes(List<TextBlock?> titles, double bottom)
    {
        for (var i = 0; i < _diagram.Boxes.Count; i++)
        {
            var box = _diagram.Boxes[i];
            var left = double.PositiveInfinity;
            var right = double.NegativeInfinity;
            foreach (var participant in box.Participants)
            {
                if (_index.TryGetValue(participant, out var index))
                {
                    left = Math.Min(left, _centers[index] - (_widths[index] / 2) - BoxMargin);
                    right = Math.Max(right, _centers[index] + (_widths[index] / 2) + BoxMargin);
                }
            }

            if (!double.IsFinite(left))
                continue;

            var title = titles[i];
            if (title is not null)
            {
                right = Math.Max(right, left + title.Width + (2 * BoxMargin));
            }

            var boxGroup = _boxesLayer.Add(new SceneGroup { CssClass = "box" });
            boxGroup.Add(new SceneRectangle(left, 0, right - left, bottom + BoxMargin) { Fill = box.Color, Stroke = _theme.NodeBorderColor.WithOpacity(0.5), StrokeWidth = 1 });
            title?.Draw(boxGroup, (left + right) / 2, 5 + (title.Height / 2), _theme.TextColor);
            _bounds.Add(new SceneBounds(left, 0, right - left, bottom + BoxMargin));
        }
    }
}
