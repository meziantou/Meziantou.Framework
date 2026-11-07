using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A boundary point of the DOM (a node and an offset in it), see https://dom.spec.whatwg.org/#concept-range-bp.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct CaretPoint(Node Node, int Offset)
{
    public bool IsInText => Node is Text;
}

/// <summary>The kinds of <see cref="CaretUnit"/>.</summary>
internal enum CaretUnitKind : byte
{
    /// <summary>A typographic character unit (an extended grapheme cluster, https://drafts.csswg.org/css-text-4/#characters) of a text.</summary>
    Character,

    /// <summary>
    /// An atomic inline (e.g. an image, an inline-block) or, in an editing host, a non-editable element: the caret moves
    /// around it, not into it.
    /// </summary>
    Atomic,

    /// <summary>A forced line break (a <c>br</c> element or a preserved segment break): the position after it is the start of the next line.</summary>
    ForcedBreak,
}

/// <summary>
/// A unit of a line that the caret moves over (https://drafts.csswg.org/css-text-4/#characters): its range in the DOM, and its
/// extent along the inline axis of the line in the coordinates of the root of the fragment tree.
/// </summary>
internal sealed class CaretUnit
{
    public CaretUnit(CaretUnitKind kind, Node node, CaretPoint start, CaretPoint end, double startCoordinate, double endCoordinate, bool isRightToLeft, string text)
    {
        Kind = kind;
        Node = node;
        Start = start;
        End = end;
        StartCoordinate = startCoordinate;
        EndCoordinate = endCoordinate;
        IsRightToLeft = isRightToLeft;
        Text = text;
    }

    public CaretUnitKind Kind { get; }

    /// <summary>The node of the unit: the text node of a character, the element of an atomic inline or of a line break.</summary>
    public Node Node { get; }

    /// <summary>The boundary point before the unit (in logical order).</summary>
    public CaretPoint Start { get; }

    /// <summary>The boundary point after the unit (in logical order).</summary>
    public CaretPoint End { get; private set; }

    /// <summary>The coordinate of the logical start edge of the unit along the inline axis (x in horizontal writing modes, y in vertical ones).</summary>
    public double StartCoordinate { get; }

    /// <summary>The coordinate of the logical end edge of the unit along the inline axis.</summary>
    public double EndCoordinate { get; private set; }

    /// <summary>Whether the unit is right-to-left (its bidi level is odd): its logical start edge is its line-right edge.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>The characters of the unit, for the word and sentence boundaries (U+FFFC for an atomic unit, U+000A for a line break).</summary>
    public string Text { get; }

    /// <summary>The line-left coordinate of the unit.</summary>
    public double Low => Math.Min(StartCoordinate, EndCoordinate);

    /// <summary>The line-right coordinate of the unit.</summary>
    public double High => Math.Max(StartCoordinate, EndCoordinate);

    /// <summary>The order of the unit in the line (logical order), set when the units of the line are sorted.</summary>
    internal (int Box, int Offset) LogicalKey { get; set; }

    /// <summary>
    /// The fragment of the unit (the text fragment of a character or of a line break, the fragment of an atomic inline), or null
    /// for the units without fragment (a preserved segment break at the end of a line, the non-editable content merged into a
    /// unit). Used by the painting of the caret.
    /// </summary>
    internal Fragment? Fragment { get; init; }

    /// <summary>Extends a non-editable atomic unit to the next fragment of the same element on the line.</summary>
    internal void Extend(CaretPoint end, double endCoordinate)
    {
        End = end;
        EndCoordinate = IsRightToLeft ? Math.Min(EndCoordinate, endCoordinate) : Math.Max(EndCoordinate, endCoordinate);
    }
}

/// <summary>
/// A line box seen by the caret navigation: its units in logical order. The caret positions of the line are the boundaries
/// between its units, from the position before the first unit to the position after the last one, except after a forced
/// line break (the position after it is the first position of the next line).
/// </summary>
internal sealed class CaretLine
{
    public CaretLine(LineBoxFragment fragment, BoxFragment container, List<CaretUnit> units, bool isRightToLeft, bool isHorizontal)
    {
        Fragment = fragment;
        Container = container;
        Units = units;
        IsRightToLeft = isRightToLeft;
        IsHorizontal = isHorizontal;
    }

    public LineBoxFragment Fragment { get; }

    /// <summary>The fragment of the block container of the line.</summary>
    public BoxFragment Container { get; }

    /// <summary>The units of the line, in logical order.</summary>
    public List<CaretUnit> Units { get; }

    /// <summary>Whether the inline base direction of the line is right-to-left.</summary>
    public bool IsRightToLeft { get; }

    /// <summary>Whether the inline axis of the line is horizontal.</summary>
    public bool IsHorizontal { get; }

    /// <summary>Whether the line ends with a forced line break.</summary>
    public bool EndsWithForcedBreak => Units[^1].Kind == CaretUnitKind.ForcedBreak;

    /// <summary>The index of the last caret position of the line.</summary>
    public int LastPosition => EndsWithForcedBreak ? Units.Count - 1 : Units.Count;
}

/// <summary>Collects the lines of the caret navigation from the fragment tree.</summary>
internal sealed class CaretLineCollector
{
    private readonly LayoutEngine _engine;
    private readonly Node? _filter;
    private readonly Element? _editingHost;
    private readonly List<CaretLine> _lines = [];
    private readonly List<(Fragment Fragment, PhysicalRect Rect)> _leaves = [];
    private readonly Dictionary<ContainerBox, Dictionary<LayoutBox, int>> _boxOrders = [];
    private readonly Dictionary<Node, Element?> _islands = new(ReferenceEqualityComparer.Instance);

    private CaretLineCollector(LayoutEngine engine, Node? filter, Element? editingHost)
    {
        _engine = engine;
        _filter = filter;
        _editingHost = editingHost;
    }

    /// <summary>
    /// Returns the lines of the content of a navigation root, in the order of the content: the document, an editing host (the
    /// caret does not leave it, and its non-editable descendants are atomic units), or an atomic inline (e.g. an inline-block)
    /// that contains the focus. The lines of an inline element are the lines of its block container, restricted to its content.
    /// </summary>
    public static List<CaretLine> Collect(LayoutEngine engine, Node root, Element? editingHost)
    {
        if (engine.EnsureLayout() is not { } rootFragment || engine.BoxTree is not { } boxTree)
            return [];

        if (root is not Element element)
        {
            var documentCollector = new CaretLineCollector(engine, filter: null, editingHost);
            documentCollector.Visit(rootFragment);
            return documentCollector.Sort();
        }

        // The block container that contains the lines of the element
        LayoutBox? box = boxTree.GetPrincipalBox(element);
        while (box is ContainerBox { IsInlineBox: true } or TextRun)
        {
            box = box.Parent;
        }

        if (box is null)
            return [];

        var collector = new CaretLineCollector(engine, element, editingHost);
        for (var fragment = engine.GetLaidOutFirstFragment(box); fragment is not null; fragment = fragment.NextFragmentOfBox)
        {
            collector.Visit(fragment);
        }

        return collector.Sort();
    }

    /// <summary>Sorts the lines in the order of their content (e.g. the order property of flex items changes the order of the fragments).</summary>
    private List<CaretLine> Sort()
    {
        var keys = new Dictionary<CaretLine, int>(_lines.Count);
        var index = 0;
        foreach (var line in _lines)
        {
            keys[line] = index++;
        }

        _lines.Sort((a, b) =>
        {
            if (ReferenceEquals(a, b))
                return 0;

            var x = a.Units[0].Start;
            var y = b.Units[0].Start;
            if (ReferenceEquals(x.Node.GetRoot(), y.Node.GetRoot()))
            {
                var position = Dom.Range.ComparePoints(x.Node, (uint)x.Offset, y.Node, (uint)y.Offset);
                if (position != AbstractRange.BoundaryPointPosition.Equal)
                    return position == AbstractRange.BoundaryPointPosition.Before ? -1 : 1;
            }

            return keys[a].CompareTo(keys[b]);
        });
        return _lines;
    }

    private void Visit(Fragment fragment)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            if (child is LineBoxFragment line)
            {
                AddLine(line, (BoxFragment)fragment);
            }
            else if (child is BoxFragment { Box.Kind: not ContainerBoxKind.Replaced, IsForceHidden: false } box)
            {
                Visit(box);
            }
        }
    }

    private void AddLine(LineBoxFragment line, BoxFragment container)
    {
        _leaves.Clear();
        CollectLeaves(line);
        if (_leaves.Count == 0 && line.ForcedBreakRun is null)
            return;

        var units = new List<CaretUnit>();
        var boxOrder = GetBoxOrder(container.Box);
        foreach (var (leaf, rect) in _leaves)
        {
            var key = boxOrder.TryGetValue(leaf.Box!, out var order) ? order : int.MaxValue;
            if (leaf is TextFragment text)
            {
                AddTextUnits(units, text, rect, key);
            }
            else if (leaf.Box!.Element is { } element && leaf.Box.PseudoElement is null && element.ParentNode is { } parent && IsInFilter(element))
            {
                var index = element.GetIndex();
                var unit = CreateAtomicUnit(CaretUnitKind.Atomic, element, new CaretPoint(parent, index), new CaretPoint(parent, index + 1), rect, container.WritingDirection, fragment: leaf);
                unit.LogicalKey = (key, 0);
                units.Add(unit);
            }
        }

        units.Sort(static (a, b) => a.LogicalKey.CompareTo(b.LogicalKey));

        // The preserved segment break that ends the line has no fragment: it is at the logical end of the line
        if (line.ForcedBreakRun is { Node: { } breakNode } breakRun && IsInFilter(breakNode))
        {
            var writingDirection = container.WritingDirection;
            double coordinate;
            if (units.Count > 0)
            {
                coordinate = units[^1].EndCoordinate;
            }
            else
            {
                var rect = line.GetRectInRoot();
                coordinate = GetCoordinate(rect, writingDirection, 0);
            }

            var (start, end) = line.ForcedBreakRange;
            units.Add(new CaretUnit(CaretUnitKind.ForcedBreak, breakNode, new CaretPoint(breakNode, start), new CaretPoint(breakNode, end), coordinate, coordinate, writingDirection.IsRightToLeft, "\n")
            {
                LogicalKey = (boxOrder.TryGetValue(breakRun, out var order) ? order : int.MaxValue, start),
            });
        }

        if (units.Count == 0)
            return;
        if (_editingHost is not null)
        {
            MergeNonEditableUnits(units);
        }

        // A forced line break can only be the last unit of a line
        _lines.Add(new CaretLine(line, container, units, container.WritingDirection.IsRightToLeft, container.WritingDirection.IsHorizontal));
    }

    /// <summary>Collects the text fragments and the atomic inlines of a line (the content of the inline boxes).</summary>
    private void CollectLeaves(Fragment fragment)
    {
        foreach (var child in fragment.ChildrenSpan)
        {
            switch (child)
            {
                case TextFragment text:
                    _leaves.Add((text, text.GetRectInRoot()));
                    break;

                case BoxFragment { Box.IsInlineBox: true } inlineBox:
                    CollectLeaves(inlineBox);
                    break;

                case BoxFragment { IsForceHidden: false } atomic:
                    _leaves.Add((atomic, atomic.GetRectInRoot()));
                    break;
            }
        }
    }

    /// <summary>The order of the inline-level boxes of a block container (pre-order), the logical order of the leaves of its lines.</summary>
    private Dictionary<LayoutBox, int> GetBoxOrder(ContainerBox container)
    {
        if (_boxOrders.TryGetValue(container, out var order))
            return order;

        order = new Dictionary<LayoutBox, int>(ReferenceEqualityComparer.Instance);
        var index = 0;
        AddBoxes(container);
        _boxOrders.Add(container, order);
        return order;

        void AddBoxes(ContainerBox parent)
        {
            for (var child = parent.FirstChild; child is not null; child = child.NextSibling)
            {
                order[child] = index++;
                if (child is ContainerBox { IsInlineBox: true } inlineBox)
                {
                    AddBoxes(inlineBox);
                }
                else if (child is ContainerBox { Display.IsInlineLevel: false, IsBlockContainer: true, IsOutOfFlow: false } block && block.Parent is { IsInlineBox: true })
                {
                    // A block inside an inline box: the inline box is split, and the block has its own lines
                    AddBoxes(block);
                }
            }
        }
    }

    private bool IsInFilter(Node node) => _filter is null || _filter.IsShadowIncludingInclusiveAncestorOf(node);

    /// <summary>Adds the units of a text fragment: its typographic character units, or a forced line break.</summary>
    private void AddTextUnits(List<CaretUnit> units, TextFragment fragment, PhysicalRect rect, int key)
    {
        if (fragment.IsHyphen)
            return;

        var run = fragment.Run;
        if (run.Kind == TextRunKind.LineBreak)
        {
            // A br element
            if (run.Element is { ParentNode: { } parent } br && IsInFilter(br))
            {
                var index = br.GetIndex();
                var unit = CreateAtomicUnit(CaretUnitKind.ForcedBreak, br, new CaretPoint(parent, index), new CaretPoint(parent, index + 1), rect, fragment.WritingDirection, fragment: fragment);
                unit.LogicalKey = (key, 0);
                units.Add(unit);
            }

            return;
        }

        // The generated content has no caret position
        if (run.Node is not { } node || !IsInFilter(node))
            return;

        var data = run.Data;
        if (fragment.ShapedText is not { } shaped || fragment.GlyphCount == 0)
        {
            // A preserved segment break
            if (fragment.End > fragment.Start && data[fragment.Start] is '\n' or '\r')
            {
                var unit = CreateAtomicUnit(CaretUnitKind.ForcedBreak, node, new CaretPoint(node, fragment.Start), new CaretPoint(node, fragment.End), rect, fragment.WritingDirection, "\n", fragment);
                unit.LogicalKey = (key, fragment.Start);
                units.Add(unit);
            }

            return;
        }

        var (shapedStart, shapedEnd) = fragment.GetShapedRange(shaped);
        if (shapedEnd <= shapedStart)
            return;

        // The grapheme cluster boundaries of the text of the fragment, in the text node
        var domStart = fragment.Start;
        var domEnd = Math.Min(fragment.End, data.Length);
        Span<bool> graphemeBoundaries = domEnd - domStart <= 512 ? stackalloc bool[domEnd - domStart] : new bool[domEnd - domStart];
        GraphemeClusters.GetBoundaries(data.AsSpan(domStart, domEnd - domStart), graphemeBoundaries);

        var writingDirection = fragment.WritingDirection;
        var origin = shaped.GetOffset(shapedStart);
        var unitStart = shapedStart;
        for (var index = shapedStart + 1; index <= shapedEnd; index++)
        {
            if (index < shapedEnd)
            {
                // A unit ends at a boundary of the clusters of the glyphs and of the grapheme clusters, where the offset in the
                // text node increases (a text transformation may map several characters to the same offset)
                var offset = GetDomOffset(fragment, shapedStart, index);
                if (!shaped.IsCaretPosition(index) || offset <= GetDomOffset(fragment, shapedStart, unitStart) || offset >= domEnd || !graphemeBoundaries[offset - domStart])
                    continue;
            }

            var start = GetDomOffset(fragment, shapedStart, unitStart);
            var last = GetDomOffset(fragment, shapedStart, index - 1);
            var end = last + (char.IsHighSurrogate(data[last]) && last + 1 < data.Length && char.IsLowSurrogate(data[last + 1]) ? 2 : 1);
            if (index < shapedEnd)
            {
                end = Math.Min(end, GetDomOffset(fragment, shapedStart, index));
            }

            end = Math.Clamp(end, start + 1, Math.Max(start + 1, fragment.End));
            var startCoordinate = GetCoordinate(rect, writingDirection, shaped.GetOffset(unitStart) - origin);
            var endCoordinate = GetCoordinate(rect, writingDirection, shaped.GetOffset(index) - origin);

            // The collapsed white space is a single space
            var text = data.Substring(start, Math.Min(end, data.Length) - start);
            if (text.Length > 1 && TextRun.IsWhiteSpace(text))
            {
                text = " ";
            }

            var unit = new CaretUnit(CaretUnitKind.Character, node, new CaretPoint(node, start), new CaretPoint(node, end), startCoordinate, endCoordinate, writingDirection.IsRightToLeft, text)
            {
                LogicalKey = (key, start),
                Fragment = fragment,
            };
            units.Add(unit);
            unitStart = index;
        }

        static int GetDomOffset(TextFragment fragment, int shapedStart, int index)
            => fragment.DomOffsets is { } map ? map[Math.Min(index - shapedStart, map.Length - 1)] : fragment.Start + (index - shapedStart);
    }

    private static CaretUnit CreateAtomicUnit(CaretUnitKind kind, Node node, CaretPoint start, CaretPoint end, PhysicalRect rect, WritingDirection writingDirection, string? text = null, Fragment? fragment = null)
    {
        var size = writingDirection.IsHorizontal ? rect.Width : rect.Height;
        return new CaretUnit(kind, node, start, end, GetCoordinate(rect, writingDirection, 0), GetCoordinate(rect, writingDirection, size), writingDirection.IsRightToLeft, text ?? (kind == CaretUnitKind.ForcedBreak ? "\n" : "￼"))
        {
            Fragment = fragment,
        };
    }

    /// <summary>The coordinate along the inline axis of a distance from the inline-start edge of a rectangle.</summary>
    private static double GetCoordinate(PhysicalRect rect, WritingDirection writingDirection, double distance) => writingDirection.InlineStart switch
    {
        PhysicalSide.Left => rect.X + distance,
        PhysicalSide.Right => rect.X + rect.Width - distance,
        PhysicalSide.Top => rect.Y + distance,
        _ => rect.Y + rect.Height - distance,
    };

    /// <summary>
    /// Replaces the units of the non-editable content of the editing host (e.g. a <c>contenteditable=false</c> element) with
    /// an atomic unit of the outermost non-editable element: the caret moves around it, as in the browsers.
    /// </summary>
    private void MergeNonEditableUnits(List<CaretUnit> units)
    {
        CaretUnit? previous = null;
        Element? previousIsland = null;
        var write = 0;
        for (var read = 0; read < units.Count; read++)
        {
            var unit = units[read];
            var island = GetNonEditableIsland(unit.Node);
            if (island is null)
            {
                units[write++] = unit;
                previous = null;
                previousIsland = null;
                continue;
            }

            if (ReferenceEquals(island, previousIsland) && previous is not null)
            {
                previous.Extend(previous.End, unit.EndCoordinate);
                continue;
            }

            var parent = island.ParentNode!;
            var index = island.GetIndex();
            previous = new CaretUnit(CaretUnitKind.Atomic, island, new CaretPoint(parent, index), new CaretPoint(parent, index + 1), unit.StartCoordinate, unit.EndCoordinate, unit.IsRightToLeft, "￼")
            {
                LogicalKey = unit.LogicalKey,
            };
            previousIsland = island;
            units[write++] = previous;
        }

        units.RemoveRange(write, units.Count - write);
    }

    /// <summary>The outermost element of the editing host that contains the node and is neither editable nor an editing host, or null.</summary>
    private Element? GetNonEditableIsland(Node node)
    {
        var parent = node is Element ? node : node.ParentNode;
        if (parent is null)
            return null;

        if (_islands.TryGetValue(parent, out var island))
            return island;

        island = null;
        for (var current = parent; current is not null && !ReferenceEquals(current, _editingHost); current = current.ParentNode)
        {
            if (current is Element element && !Editing.IsEditable(element) && !Editing.IsEditingHost(element))
            {
                island = element;
            }
        }

        _islands[parent] = island;
        return island;
    }
}
