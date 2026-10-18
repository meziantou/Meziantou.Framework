using System.Globalization;
using System.Text;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The box tree of a document (https://drafts.csswg.org/css-display/#box-tree), built by <see cref="BoxTreeBuilder"/> from
/// the flat tree and the computed styles. Its root is the viewport box (the initial containing block), whose child is the
/// principal box of the root element. The tree reflects the DOM and the styles when it was built: it is patched with the
/// boxes of the elements that changed (see <see cref="TryPatch"/>), or built again.
/// </summary>
internal sealed class BoxTree
{
    private readonly Dictionary<(Node Node, string? PseudoElement), LayoutBox> _boxes;
    private readonly Dictionary<ContainerBox, int>? _containerQuoteDepths;

    internal BoxTree(ContainerBox? viewport, Dictionary<(Node Node, string? PseudoElement), LayoutBox> boxes, Dictionary<ContainerBox, int>? containerQuoteDepths)
    {
        Viewport = viewport;
        _boxes = boxes;
        PrunedBoxCount = boxes.Count;
        _containerQuoteDepths = containerQuoteDepths;
    }

    /// <summary>Builds the box tree of a document.</summary>
    public static BoxTree Build(Document document) => BoxTreeBuilder.Build(document);

    /// <summary>
    /// Builds again the boxes of the elements that changed (see <see cref="BoxTreeChanges"/>) and replaces their previous boxes
    /// (see <see cref="BoxTreeBuilder.TryPatch"/>). Returns false when the tree must be built again: it is then unchanged.
    /// </summary>
    public bool TryPatch(Document document, BoxTreeChanges changes) => BoxTreeBuilder.TryPatch(this, document, changes);

    /// <summary>The boxes by node and pseudo-element (the principal box of an element, the first text run of a text node).</summary>
    internal Dictionary<(Node Node, string? PseudoElement), LayoutBox> Boxes => _boxes;

    /// <summary>
    /// Builds again the children of the principal box of a size container whose descendants were styled again with its size
    /// (see <see cref="BoxTreeBuilder.RebuildContents"/>).
    /// </summary>
    public void RebuildContents(ContainerBox box) => BoxTreeBuilder.RebuildContents(this, box);

    /// <summary>Builds again the boxes of the cues rendered over a video element, the children of its replaced box (see <see cref="BoxTreeBuilder.RebuildCueBoxes"/>).</summary>
    public void RebuildCueBoxes(ContainerBox videoBox, IReadOnlyList<WebVtt.WebVttDisplayItem> items) => BoxTreeBuilder.RebuildCueBoxes(this, videoBox, items);

    /// <summary>The depth of nesting of the quotes at the start of the contents of a size container.</summary>
    internal int GetContainerQuoteDepth(ContainerBox box) => _containerQuoteDepths is not null && _containerQuoteDepths.TryGetValue(box, out var depth) ? depth : 0;

    /// <summary>
    /// The principal boxes of the elements with <c>content-visibility: auto</c> (when it applies to them), in tree order: their
    /// <see cref="ContainerBox.SkipsContents"/> depends on the relevance to the user (see <see cref="ContentRelevancy"/>). The
    /// boxes replaced by <see cref="RebuildContents"/> may stay in the list: check that a box is still the principal box of its element.
    /// </summary>
    public List<ContainerBox> ContentVisibilityAutoBoxes { get; init; } = [];

    /// <summary>The principal boxes whose <c>contain-intrinsic-size</c> may have the <c>auto</c> keyword (see <see cref="LastRememberedSizes"/>), with the same caveat.</summary>
    public List<ContainerBox> ContainIntrinsicSizeAutoBoxes { get; init; } = [];

    /// <summary>The boxes of the scroll navigation controls (the <c>::scroll-marker-group</c>, <c>::scroll-marker</c> and <c>::scroll-button()</c> pseudo-elements), or null when there is none.</summary>
    public ScrollControlBoxes? ScrollControls { get; internal set; }

    /// <summary>
    /// The counters of the document computed by the build of the tree (null when no box reads them): a patch of the tree reads
    /// them for the boxes it builds again, which is valid while the counter operations of the boxes do not change (see
    /// <see cref="BoxTreeBuilder.TryPatch"/>).
    /// </summary>
    internal Cascade.CssCounters? Counters { get; set; }

    /// <summary>
    /// Whether the tree can be patched (see <see cref="BoxTreeBuilder.TryPatch"/>): it has no box whose generation depends on
    /// boxes that a patch may not build again (the quotes, the run-in boxes, the ::first-letter boxes, the scroll navigation
    /// controls, the boxes of the top layer, the counter operations of the pseudo-elements without box).
    /// </summary>
    internal bool IsPatchable { get; set; } = true;

    /// <summary>The number of boxes by node when the tree was built or the boxes of the removed nodes were last pruned (see <see cref="BoxTreeBuilder.TryPatch"/>).</summary>
    internal int PrunedBoxCount { get; set; }

    /// <summary>The root of the tree, or null when the root element of the document is not rendered (e.g. the document has no browsing context).</summary>
    public ContainerBox? Viewport { get; }

    /// <summary>
    /// Returns the principal box of an element (https://drafts.csswg.org/css-display/#principal-box): the table wrapper box of
    /// a table, the first part of a split inline box (see <see cref="ContainerBox.Continuation"/>); or null when the element
    /// does not generate a box.
    /// </summary>
    public ContainerBox? GetPrincipalBox(Element element) => _boxes.GetValueOrDefault((element, null)) as ContainerBox;

    /// <summary>Returns the box of a pseudo-element (<c>before</c>, <c>after</c>, <c>marker</c>, <c>first-letter</c>), or null when it is not generated (or its display is contents).</summary>
    public ContainerBox? GetPseudoElementBox(Element element, string pseudoElement) => _boxes.GetValueOrDefault((element, pseudoElement)) as ContainerBox;

    /// <summary>Returns the first text run of a text node (see <see cref="TextRun.NextRunOfNode"/>), or null when the text node is not rendered.</summary>
    public TextRun? GetFirstTextRun(Text text) => _boxes.GetValueOrDefault((text, null)) as TextRun;

    /// <summary>
    /// Returns a text representation of the tree, one box per line indented by its depth (for the tests and the debugging):
    /// the kind and the display type of the box, its element or pseudo-element (or <c>anonymous</c>), its flags and, for
    /// a text run, its text and its range in the text node.
    /// </summary>
    public string Dump() => Viewport is null ? "" : Dump(Viewport);

    /// <summary>Returns a text representation of a box and of its descendants (see <see cref="Dump()"/>).</summary>
    public static string Dump(LayoutBox box)
    {
        var builder = new StringBuilder();
        Dump(builder, box, 0);
        return builder.ToString();
    }

    private static void Dump(StringBuilder builder, LayoutBox box, int depth)
    {
        builder.Append(' ', depth * 2);
        if (box is TextRun run)
        {
            builder.Append(run.Kind switch
            {
                TextRunKind.LineBreak => "line-break",
                TextRunKind.WordBreakOpportunity => "word-break-opportunity",
                TextRunKind.Generated => "generated-text",
                _ => "text",
            });
            if (run.Kind is TextRunKind.Text or TextRunKind.Generated)
            {
                builder.Append(' ');
                AppendQuoted(builder, run.Text);
            }

            if (run.Node is not null && (run.Start != 0 || run.End != run.Data.Length))
            {
                builder.Append(CultureInfo.InvariantCulture, $" [{run.Start}, {run.End})");
            }

            if (run.PseudoElement is not null && run.Parent?.PseudoElement != run.PseudoElement)
            {
                builder.Append(" ::").Append(run.PseudoElement);
            }

            builder.Append('\n');
            return;
        }

        var container = (ContainerBox)box;
        builder.Append(container.Kind switch
        {
            ContainerBoxKind.Viewport => "viewport",
            ContainerBoxKind.TableWrapper => "table-wrapper " + DescribeOuter(container.Display),
            ContainerBoxKind.TableGrid => "table",
            ContainerBoxKind.Replaced => "replaced " + DescribeOuter(container.Display),
            _ => Describe(container.Display),
        });

        if (container.IsAnonymous)
        {
            builder.Append(" anonymous");
        }
        else if (container.Element is { } element)
        {
            builder.Append(" <").Append(element.LocalName);
            if (element.GetAttributeValue("id") is { Length: > 0 } id)
            {
                builder.Append('#').Append(id);
            }

            builder.Append('>');
            if (container.PseudoElement is { } pseudoElement)
            {
                builder.Append("::").Append(pseudoElement);
            }
        }

        if (container.Kind != ContainerBoxKind.Viewport && container.IsBlockContainer && container.Kind != ContainerBoxKind.TableWrapper && !container.IsEmpty)
        {
            builder.Append(container.ChildrenAreInline ? " inline-children" : " block-children");
        }

        if (container.PreviousContinuation is not null)
        {
            builder.Append(" continuation");
        }

        if (container.IsOutOfFlow)
        {
            builder.Append(container.Style.Layout.IsAbsolutelyPositioned ? " absolute" : " float");
        }

        if (container.IsOutsideMarker)
        {
            builder.Append(" outside");
        }

        if (container.SkipsContents)
        {
            builder.Append(" skips-contents");
        }

        if (container.GeneratedImage is { } image)
        {
            builder.Append(' ').Append(image);
        }

        builder.Append('\n');
        foreach (var child in container.Children)
        {
            Dump(builder, child, depth + 1);
        }
    }

    private static void AppendQuoted(StringBuilder builder, ReadOnlySpan<char> text)
    {
        builder.Append('"');
        foreach (var c in text)
        {
            switch (c)
            {
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                case '"':
                    builder.Append("\\\"");
                    break;
                case < ' ' or > '~':
                    builder.Append(CultureInfo.InvariantCulture, $"\\{(int)c:X}");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        builder.Append('"');
    }

    private static string DescribeOuter(Display display) => display.Outside == DisplayOutside.Inline ? "inline" : "block";

    /// <summary>The display type as a keyword (the short display keywords of https://drafts.csswg.org/css-display/#display-value-summary).</summary>
    private static string Describe(Display display)
    {
        var result = display.Internal switch
        {
            DisplayInternal.TableRowGroup => "table-row-group",
            DisplayInternal.TableHeaderGroup => "table-header-group",
            DisplayInternal.TableFooterGroup => "table-footer-group",
            DisplayInternal.TableRow => "table-row",
            DisplayInternal.TableCell => "table-cell",
            DisplayInternal.TableColumnGroup => "table-column-group",
            DisplayInternal.TableColumn => "table-column",
            DisplayInternal.TableCaption => "table-caption",
            DisplayInternal.RubyBase => "ruby-base",
            DisplayInternal.RubyText => "ruby-text",
            DisplayInternal.RubyBaseContainer => "ruby-base-container",
            DisplayInternal.RubyTextContainer => "ruby-text-container",
            _ => (display.Outside, display.Inside) switch
            {
                (DisplayOutside.Block, DisplayInside.Flow) => "block",
                (DisplayOutside.Inline, DisplayInside.Flow) => "inline",
                (DisplayOutside.RunIn, DisplayInside.Flow) => "run-in",
                (DisplayOutside.Block, DisplayInside.FlowRoot) => "flow-root",
                (DisplayOutside.Inline, DisplayInside.FlowRoot) => "inline-block",
                (DisplayOutside.Block, DisplayInside.Flex) => "flex",
                (DisplayOutside.Inline, DisplayInside.Flex) => "inline-flex",
                (DisplayOutside.Block, DisplayInside.Grid) => "grid",
                (DisplayOutside.Inline, DisplayInside.Grid) => "inline-grid",
                (DisplayOutside.Inline, DisplayInside.Ruby) => "ruby",
                (DisplayOutside.Inline, DisplayInside.Math) => "math",
                (DisplayOutside.Block, DisplayInside.WebkitBox) => "-webkit-box",
                (DisplayOutside.Inline, DisplayInside.WebkitBox) => "-webkit-inline-box",
                var (outside, inside) => (outside == DisplayOutside.RunIn ? "run-in" : DescribeOuter(display)) + " " + inside switch
                {
                    DisplayInside.Flow => "flow",
                    DisplayInside.FlowRoot => "flow-root",
                    DisplayInside.Table => "table",
                    DisplayInside.Flex => "flex",
                    DisplayInside.Grid => "grid",
                    DisplayInside.GridLanes => "grid-lanes",
                    DisplayInside.Ruby => "ruby",
                    DisplayInside.Math => "math",
                    _ => "-webkit-box",
                },
            },
        };

        return display.IsListItem ? result + " list-item" : result;
    }
}
