using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A caret position of the navigation: a line and the index of a boundary between its units (see <see cref="CaretLine"/>).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CaretStop(int Line, int Index);

/// <summary>The granularities of the caret movements (https://w3c.github.io/selection-api/#dom-selection-modify).</summary>
internal enum CaretGranularity
{
    Character,
    Word,
    Sentence,
    Line,
    Paragraph,
    LineBoundary,
    SentenceBoundary,
    ParagraphBoundary,
    DocumentBoundary,
}

/// <summary>
/// The result of a caret movement: the new boundary point, the caret position, whether the caret is attached to the unit
/// before the position (its visual location at a bidi boundary, the caret affinity), and the inline coordinate kept by the
/// consecutive line movements.
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CaretMoveResult(CaretPoint Point, CaretStop Stop, bool AttachedUpstream, double? LineCoordinate);

/// <summary>
/// The caret movements of the user agent over the rendered content (https://w3c.github.io/selection-api/#dom-selection-modify,
/// "as if the user had requested to move or extend the selection"), computed from the line boxes of the layout as the browsers
/// do: the caret moves over the typographic character units (grapheme clusters) of the rendered text, the atomic inlines and
/// the forced line breaks; the collapsed white space, the generated content and the elements that are not rendered are
/// skipped. The left and right movements by characters are visual (bidi text), the movements by lines keep the inline
/// coordinate of the caret, and the movements stay in the navigation root (the editing host of the caret).
/// </summary>
internal sealed class CaretNavigator
{
    private readonly List<CaretLine> _lines;
    private readonly Dictionary<Node, List<(CaretStop Before, CaretUnit Unit)>> _unitsByNode = new(ReferenceEqualityComparer.Instance);

    // The text of the content for the word and sentence boundaries, and the caret positions at each index
    private string? _text;
    private CaretStop[]? _firstStops;
    private CaretStop[]? _lastStops;
    private Dictionary<CaretStop, int>? _textIndexes;

    private CaretNavigator(List<CaretLine> lines)
    {
        _lines = lines;
        for (var line = 0; line < lines.Count; line++)
        {
            var units = lines[line].Units;
            for (var i = 0; i < units.Count; i++)
            {
                if (!_unitsByNode.TryGetValue(units[i].Node, out var list))
                {
                    list = [];
                    _unitsByNode.Add(units[i].Node, list);
                }

                list.Add((new CaretStop(line, i), units[i]));
            }
        }
    }

    /// <summary>Creates the navigator of the content of a navigation root (see <see cref="CaretLineCollector.Collect"/>), or null when it has no caret position.</summary>
    public static CaretNavigator? Create(LayoutEngine engine, Node root, Element? editingHost)
    {
        var lines = CaretLineCollector.Collect(engine, root, editingHost);
        return lines.Count == 0 ? null : new CaretNavigator(lines);
    }

    public IReadOnlyList<CaretLine> Lines => _lines;

    private CaretStop First => new(0, 0);

    private CaretStop Last => new(_lines.Count - 1, _lines[^1].LastPosition);

    /// <summary>The boundary point after the unit before a caret position (the upstream position), or null.</summary>
    public CaretPoint? GetUpstream(CaretStop stop)
    {
        var line = _lines[stop.Line];
        if (stop.Index > 0)
            return line.Units[stop.Index - 1].End;

        // The position after a forced line break is the start of the next line of the same block container
        if (stop.Line > 0 && _lines[stop.Line - 1] is { EndsWithForcedBreak: true } previous && ReferenceEquals(previous.Container, line.Container))
            return previous.Units[^1].End;

        return null;
    }

    /// <summary>The boundary point before the unit after a caret position (the downstream position), or null.</summary>
    public CaretPoint? GetDownstream(CaretStop stop)
    {
        var units = _lines[stop.Line].Units;
        return stop.Index < units.Count ? units[stop.Index].Start : null;
    }

    /// <summary>
    /// Returns the boundary point of a caret position: the position in a text node, in the direction of the movement first (the
    /// position after the unit crossed by a forward movement), else the other one.
    /// </summary>
    public CaretPoint GetPoint(CaretStop stop, bool forward)
    {
        var first = forward ? GetDownstream(stop) : GetUpstream(stop);
        var second = forward ? GetUpstream(stop) : GetDownstream(stop);
        if (first is { IsInText: true })
            return first.Value;

        if (second is { IsInText: true })
            return second.Value;

        return (first ?? second)!.Value;
    }

    /// <summary>Whether two caret positions are the same position of the DOM (e.g. the end of a line and the start of the next line when a word is broken).</summary>
    private bool IsSamePoint(CaretStop a, CaretStop b)
    {
        var upA = GetUpstream(a);
        var downA = GetDownstream(a);
        var upB = GetUpstream(b);
        var downB = GetDownstream(b);
        return (upA is not null && (upA == upB || upA == downB)) || (downA is not null && (downA == upB || downA == downB));
    }

    /// <summary>
    /// Returns the caret position of a boundary point: the position whose upstream or downstream point it is, the position
    /// before the unit that contains it, or the closest position in the content (the position before the next unit, or after
    /// the previous unit when the point is in the element of the previous unit only). <paramref name="attachedUpstream"/>
    /// chooses between the end of a line and the start of the next line when they are the same point.
    /// </summary>
    public CaretStop Resolve(Node node, int offset, bool attachedUpstream)
    {
        if (node is Text && _unitsByNode.TryGetValue(node, out var textUnits))
        {
            CaretStop? upstream = null;
            CaretStop? downstream = null;
            foreach (var (before, unit) in textUnits)
            {
                if (unit.Start.Offset == offset)
                {
                    downstream ??= before;
                }
                else if (unit.Start.Offset < offset && offset < unit.End.Offset)
                {
                    // Inside a cluster (e.g. between the two code units of a surrogate pair)
                    return before;
                }

                if (unit.End.Offset == offset && upstream is null)
                {
                    if (unit.Kind != CaretUnitKind.ForcedBreak)
                    {
                        upstream = new CaretStop(before.Line, before.Index + 1);
                    }
                    else if (before.Line + 1 < _lines.Count)
                    {
                        upstream = new CaretStop(before.Line + 1, 0);
                    }
                }
            }

            if ((attachedUpstream ? upstream ?? downstream : downstream ?? upstream) is { } match)
                return match;
        }

        // The closest units before and after the point, in tree order
        var root = node.GetRoot();
        Node? next;
        Node? previous;
        if (node is Text or Comment or ProcessingInstruction)
        {
            next = node.GetNextInTreeOrderSkippingChildren(root);
            previous = node;
        }
        else
        {
            var child = GetChild(node, offset);
            next = child ?? node.GetNextInTreeOrderSkippingChildren(root);
            previous = child is not null ? child.GetPreviousInTreeOrder(root) : GetLastInclusiveDescendant(node);
            if (offset == 0 && child is null)
            {
                previous = node;
            }
        }

        // A text node whose characters are partly before and after the offset was handled above
        (CaretStop Stop, CaretUnit Unit)? nextUnit = null;
        for (var current = next; current is not null; current = current.GetNextInTreeOrder(root))
        {
            if (_unitsByNode.TryGetValue(current, out var units))
            {
                nextUnit = units[0];
                break;
            }
        }

        (CaretStop Stop, CaretUnit Unit)? previousUnit = null;
        for (var current = previous; current is not null; current = current.GetPreviousInTreeOrder(root))
        {
            if (_unitsByNode.TryGetValue(current, out var units))
            {
                previousUnit = units[^1];
                break;
            }
        }

        if (previousUnit is { } p && (nextUnit is null || (node.IsInclusiveAncestorOf(p.Unit.Node) && !node.IsInclusiveAncestorOf(nextUnit.Value.Unit.Node))))
        {
            var after = new CaretStop(p.Stop.Line, p.Stop.Index + 1);
            if (after.Index > _lines[after.Line].LastPosition)
            {
                after = after.Line + 1 < _lines.Count ? new CaretStop(after.Line + 1, 0) : new CaretStop(after.Line, _lines[after.Line].LastPosition);
            }

            return after;
        }

        if (nextUnit is { } n)
            return n.Stop;

        return node is Document ? First : Last;

        static Node? GetChild(Node parent, int index)
        {
            var child = parent.FirstChild;
            for (var i = 0; i < index && child is not null; i++)
            {
                child = child.NextSibling;
            }

            return child;
        }

        static Node GetLastInclusiveDescendant(Node node)
        {
            while (node.LastChild is { } last)
            {
                node = last;
            }

            return node;
        }
    }

    /// <summary>The logical movement by one character: the caret crosses the next (or previous) unit.</summary>
    public CaretMoveResult? MoveByCharacter(CaretStop stop, bool forward)
    {
        if (forward)
        {
            var line = _lines[stop.Line];
            if (stop.Index < line.Units.Count)
            {
                var unit = line.Units[stop.Index];
                if (unit.Kind == CaretUnitKind.ForcedBreak)
                {
                    if (stop.Line + 1 >= _lines.Count)
                        return null;

                    var next = new CaretStop(stop.Line + 1, 0);
                    return new CaretMoveResult(GetPoint(next, forward: true), next, AttachedUpstream: false, null);
                }

                return Crossed(new CaretStop(stop.Line, stop.Index + 1), unit, forward: true);
            }

            // The end of a line: the start of the next line, or after its first unit when it is the same point of the DOM
            if (stop.Line + 1 >= _lines.Count)
                return null;

            var start = new CaretStop(stop.Line + 1, 0);
            if (IsSamePoint(stop, start))
                return MoveByCharacter(start, forward: true);

            return new CaretMoveResult(GetPoint(start, forward: true), start, AttachedUpstream: false, null);
        }

        if (stop.Index > 0)
            return Crossed(new CaretStop(stop.Line, stop.Index - 1), _lines[stop.Line].Units[stop.Index - 1], forward: false);

        if (stop.Line == 0)
            return null;

        var previous = _lines[stop.Line - 1];
        if (previous.EndsWithForcedBreak)
        {
            var before = new CaretStop(stop.Line - 1, previous.Units.Count - 1);
            return new CaretMoveResult(GetPoint(before, forward: false), before, AttachedUpstream: true, null);
        }

        var end = new CaretStop(stop.Line - 1, previous.Units.Count);
        if (IsSamePoint(stop, end))
            return MoveByCharacter(end, forward: false);

        return new CaretMoveResult(GetPoint(end, forward: false), end, AttachedUpstream: true, null);
    }

    /// <summary>The result of a movement that crossed a unit: the caret is at its edge (in its text node), attached to it.</summary>
    private CaretMoveResult Crossed(CaretStop stop, CaretUnit unit, bool forward)
    {
        var point = forward ? unit.End : unit.Start;
        if (!point.IsInText)
        {
            point = GetPoint(stop, forward);
        }

        return new CaretMoveResult(point, stop, AttachedUpstream: forward, null);
    }

    /// <summary>
    /// The visual movement by one character to the left or to the right (the line-left or line-right direction): the caret
    /// crosses the unit next to it on the line, in the visual order of the bidi text, then moves to the next or previous line at
    /// the end of the line.
    /// </summary>
    public CaretMoveResult? MoveVisually(CaretStop stop, bool? attachedUpstream, bool toRight)
    {
        var line = _lines[stop.Line];
        var units = line.Units;

        // The unit the caret is attached to: at a bidi boundary (two units of different directions, whose edges are at
        // different places of the line), the unit of the last movement, else the unit whose direction is the direction of the line
        var before = stop.Index > 0 ? units[stop.Index - 1] : null;
        var after = stop.Index < units.Count && units[stop.Index].Kind != CaretUnitKind.ForcedBreak ? units[stop.Index] : null;
        var upstream = attachedUpstream ?? (before is not null && after is not null && before.IsRightToLeft != after.IsRightToLeft && before.IsRightToLeft == line.IsRightToLeft);
        CaretUnit? unit;
        bool atRightEdge;
        if ((upstream && before is not null) || after is null)
        {
            // The caret is at the logical end edge of the unit before it
            unit = before;
            atRightEdge = unit is not null && !unit.IsRightToLeft;
        }
        else
        {
            // The caret is at the logical start edge of the unit after it
            unit = after;
            atRightEdge = unit.IsRightToLeft;
        }

        if (unit is null)
            return MoveAcrossLineEdge(stop, toRight);

        // The units in visual order (the forced line break is at the end of the line)
        var visual = new List<CaretUnit>(units.Count);
        foreach (var candidate in units)
        {
            if (candidate.Kind != CaretUnitKind.ForcedBreak)
            {
                visual.Add(candidate);
            }
        }

        visual.Sort(static (a, b) => a.Low.CompareTo(b.Low));
        var visualIndex = visual.IndexOf(unit);
        CaretUnit crossed;
        if (toRight)
        {
            if (!atRightEdge)
            {
                crossed = unit;
            }
            else if (visualIndex + 1 < visual.Count)
            {
                crossed = visual[visualIndex + 1];
            }
            else
            {
                return MoveAcrossLineEdge(stop, toRight);
            }
        }
        else if (atRightEdge)
        {
            crossed = unit;
        }
        else if (visualIndex > 0)
        {
            crossed = visual[visualIndex - 1];
        }
        else
        {
            return MoveAcrossLineEdge(stop, toRight);
        }

        // Crossing the unit in its logical direction (a left-to-right unit crossed to the right) moves after it
        var logicalForward = toRight != crossed.IsRightToLeft;
        var index = units.IndexOf(crossed);
        var newStop = new CaretStop(stop.Line, logicalForward ? index + 1 : index);
        return Crossed(newStop, crossed, logicalForward);
    }

    /// <summary>A visual movement past the left or right edge of a line: the logical movement to the next or previous line.</summary>
    private CaretMoveResult? MoveAcrossLineEdge(CaretStop stop, bool toRight)
    {
        var line = _lines[stop.Line];
        var forward = toRight != line.IsRightToLeft;
        var edge = forward ? new CaretStop(stop.Line, line.LastPosition) : new CaretStop(stop.Line, 0);
        if (forward && line.EndsWithForcedBreak)
            return MoveByCharacter(edge, forward: true);

        if (forward ? stop.Line + 1 >= _lines.Count : stop.Line == 0)
            return null;

        return MoveByCharacter(edge, forward);
    }

    /// <summary>The coordinate of a caret position along the inline axis (the edge of the unit the caret is attached to).</summary>
    public double GetCoordinate(CaretStop stop, bool attachedUpstream)
    {
        var units = _lines[stop.Line].Units;
        if (stop.Index > 0 && (attachedUpstream || stop.Index >= units.Count))
            return units[stop.Index - 1].EndCoordinate;

        return units[Math.Min(stop.Index, units.Count - 1)].StartCoordinate;
    }

    /// <summary>
    /// The movement to the next or previous line (<paramref name="paragraph"/>: to the line of the next or previous paragraph),
    /// at the caret position of the line closest to the inline coordinate of the caret.
    /// </summary>
    public CaretMoveResult? MoveByLine(CaretStop stop, double coordinate, bool forward, bool paragraph)
    {
        var line = stop.Line;
        do
        {
            line += forward ? 1 : -1;
            if (line < 0 || line >= _lines.Count)
            {
                // There is no line after (or before) the caret: the end (or the start) of the content
                var edge = forward ? Last : First;
                return edge == stop ? null : new CaretMoveResult(GetPoint(edge, !forward), edge, forward, coordinate);
            }
        }
        while (paragraph && GetParagraphStart(line) == GetParagraphStart(stop.Line));

        var target = GetClosestStop(line, coordinate, out var attachedUpstream);
        return new CaretMoveResult(GetPoint(target, forward: !attachedUpstream), target, attachedUpstream, coordinate);
    }

    /// <summary>The caret position of a line closest to an inline coordinate.</summary>
    private CaretStop GetClosestStop(int line, double coordinate, out bool attachedUpstream)
    {
        var caretLine = _lines[line];
        var best = new CaretStop(line, 0);
        var bestDistance = double.PositiveInfinity;
        attachedUpstream = false;
        for (var index = 0; index <= caretLine.LastPosition; index++)
        {
            var stop = new CaretStop(line, index);
            if (index > 0)
            {
                var distance = Math.Abs(caretLine.Units[index - 1].EndCoordinate - coordinate);
                if (distance < bestDistance)
                {
                    (best, bestDistance, attachedUpstream) = (stop, distance, true);
                }
            }

            if (index < caretLine.Units.Count)
            {
                var distance = Math.Abs(caretLine.Units[index].StartCoordinate - coordinate);
                if (distance < bestDistance)
                {
                    (best, bestDistance, attachedUpstream) = (stop, distance, false);
                }
            }
        }

        return best;
    }

    /// <summary>The first line of the paragraph of a line: the paragraphs are separated by the forced line breaks and the block boundaries.</summary>
    private int GetParagraphStart(int line)
    {
        while (line > 0 && !_lines[line - 1].EndsWithForcedBreak && ReferenceEquals(_lines[line - 1].Container, _lines[line].Container))
        {
            line--;
        }

        return line;
    }

    /// <summary>The last line of the paragraph of a line.</summary>
    private int GetParagraphEnd(int line)
    {
        while (line + 1 < _lines.Count && !_lines[line].EndsWithForcedBreak && ReferenceEquals(_lines[line + 1].Container, _lines[line].Container))
        {
            line++;
        }

        return line;
    }

    /// <summary>The movement to the start or the end of a line, a paragraph or the content.</summary>
    public CaretMoveResult MoveToBoundary(CaretStop stop, CaretGranularity granularity, bool forward)
    {
        CaretStop target;
        if (granularity == CaretGranularity.DocumentBoundary)
        {
            target = forward ? Last : First;
        }
        else
        {
            var line = granularity == CaretGranularity.ParagraphBoundary ? (forward ? GetParagraphEnd(stop.Line) : GetParagraphStart(stop.Line)) : stop.Line;
            target = forward ? new CaretStop(line, _lines[line].LastPosition) : new CaretStop(line, 0);
        }

        return new CaretMoveResult(GetPoint(target, forward: !forward), target, forward, null);
    }

    /// <summary>
    /// The movement by words (https://www.unicode.org/reports/tr29/#Word_Boundaries): forward to the end of the word that
    /// contains the caret or of the next word, backward to the start of the word that contains the caret or of the previous
    /// word (the behavior of the browsers on macOS and Linux). The segments that are not words (spaces, punctuation) are
    /// crossed.
    /// </summary>
    public CaretMoveResult? MoveByWord(CaretStop stop, bool forward)
    {
        EnsureText();
        var text = _text!;
        if (!_textIndexes!.TryGetValue(stop, out var index))
            return null;

        var target = WordBoundaries.Move(text, index, forward);
        return MoveToTextIndex(stop, target, forward);
    }

    /// <summary>
    /// The movement by sentences: the sentences end after a terminal punctuation (. ! ? and their full-width forms) followed by
    /// white space, and at the paragraph boundaries (a simplification of https://www.unicode.org/reports/tr29/#Sentence_Boundaries).
    /// <paramref name="boundary"/>: the start or the end of the sentence of the caret, else the start of the next or previous sentence.
    /// </summary>
    public CaretMoveResult? MoveBySentence(CaretStop stop, bool forward, bool boundary)
    {
        EnsureText();
        var text = _text!;
        if (!_textIndexes!.TryGetValue(stop, out var index))
            return null;

        int target;
        if (forward)
        {
            target = text.Length;
            for (var i = index; i < text.Length; i++)
            {
                if (IsSentenceEnd(text, i, out var next) && next > index)
                {
                    target = boundary ? i : next;
                    if (target > index)
                        break;
                }
            }
        }
        else
        {
            target = 0;
            for (var i = Math.Min(index, text.Length) - 1; i >= 0; i--)
            {
                if (IsSentenceEnd(text, i, out var next) && (boundary ? next <= index : next < index))
                {
                    target = next;
                    break;
                }
            }
        }

        return MoveToTextIndex(stop, target, forward);

        // A sentence ends at a terminal punctuation followed by spaces (the next sentence starts after them), or at a paragraph separator
        static bool IsSentenceEnd(string text, int i, out int next)
        {
            next = i + 1;
            if (text[i] == '\n')
                return true;

            if (text[i] is not ('.' or '!' or '?' or '。' or '！' or '？'))
                return false;

            while (next < text.Length && text[next] is '.' or '!' or '?' or '"' or '\'' or ')' or '”' or '’')
            {
                next++;
            }

            if (next < text.Length && text[next] is not (' ' or '\t' or '\n'))
                return text[i] is '。' or '！' or '？';

            while (next < text.Length && text[next] is ' ' or '\t')
            {
                next++;
            }

            return true;
        }
    }

    private CaretMoveResult? MoveToTextIndex(CaretStop stop, int target, bool forward)
    {
        var targetStop = forward ? _firstStops![target] : _lastStops![target];
        if (targetStop == stop)
            return null;

        return new CaretMoveResult(GetPoint(targetStop, forward: !forward), targetStop, forward, null);
    }

    /// <summary>
    /// Builds the text of the content (the characters of the units; a line feed between the blocks) and the caret positions
    /// at each index of the text.
    /// </summary>
    private void EnsureText()
    {
        if (_text is not null)
            return;

        var builder = new StringBuilder();
        var firstAt = new Dictionary<int, CaretStop>();
        var lastAt = new Dictionary<int, CaretStop>();
        var indexes = new Dictionary<CaretStop, int>();
        void Add(CaretStop stop)
        {
            indexes.TryAdd(stop, builder.Length);
            firstAt.TryAdd(builder.Length, stop);
            lastAt[builder.Length] = stop;
        }

        for (var line = 0; line < _lines.Count; line++)
        {
            var caretLine = _lines[line];
            if (line > 0 && !_lines[line - 1].EndsWithForcedBreak && !ReferenceEquals(_lines[line - 1].Container, caretLine.Container))
            {
                builder.Append('\n');
            }

            for (var i = 0; i < caretLine.Units.Count; i++)
            {
                if (i <= caretLine.LastPosition)
                {
                    Add(new CaretStop(line, i));
                }

                builder.Append(caretLine.Units[i].Text);
            }

            if (!caretLine.EndsWithForcedBreak)
            {
                Add(new CaretStop(line, caretLine.Units.Count));
            }
        }

        // The indexes without caret position (inside a unit, after a final line break) have the position of the next index
        var firstStops = new CaretStop[builder.Length + 1];
        var lastStops = new CaretStop[builder.Length + 1];
        var nextFirst = Last;
        var nextLast = Last;
        for (var i = builder.Length; i >= 0; i--)
        {
            if (firstAt.TryGetValue(i, out var stop))
            {
                nextFirst = stop;
                nextLast = lastAt[i];
            }

            firstStops[i] = nextFirst;
            lastStops[i] = nextLast;
        }

        _text = builder.ToString();
        _firstStops = firstStops;
        _lastStops = lastStops;
        _textIndexes = indexes;
    }
}
