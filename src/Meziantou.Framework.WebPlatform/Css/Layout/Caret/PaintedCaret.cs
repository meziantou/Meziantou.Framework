using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The shapes of the text insertion caret (https://drafts.csswg.org/css-ui-4/#caret-shape).</summary>
internal enum CaretShape : byte
{
    /// <summary>A thin bar at the insertion point (also the shape of <c>auto</c>).</summary>
    Bar,

    /// <summary>A rectangle over the character that follows the insertion point.</summary>
    Block,

    /// <summary>A thin line under the character that follows the insertion point.</summary>
    Underscore,
}

/// <summary>
/// The text insertion caret of a document as it is painted (https://drafts.csswg.org/css-ui-4/#insertion-caret): the caret of
/// the focused text control, or of the collapsed selection in editable content, computed from the fragments of the layout.
/// </summary>
/// <remarks>
/// <para>
/// The caret is displayed when the document contains the focused area of its top-level traversable, which has the system
/// focus, and <see cref="ViewportOptions.CaretVisible"/> is set: in the value of the focused text control (a <c>textarea</c>
/// or a text field that can be edited) whose selection is collapsed; else at the focus of the collapsed selection of the
/// document when it is in editable content and has the focus (<see cref="Selection.HasEditingFocus"/>: it was set, or its
/// editing host was focused, and the focus did not move since). A selection that is a range has no caret.
/// </para>
/// <para>
/// The geometry follows the browsers. The position along the line is the edge of the unit of the line the caret is attached
/// to (see <see cref="CaretNavigator"/>: bidirectional text, vertical writing modes), and its extent across the line is the
/// one of the fragment of that unit (the ascent and the descent of the font of a text, the box of an atomic inline). A block
/// container without line box (an empty editing host) has the caret of its first line: aligned by <c>text-align</c> and
/// <c>text-indent</c>, as tall as its font and centered in the height of a line. A <c>bar</c> is one CSS pixel thick; a
/// <c>block</c> covers the character after the insertion point, and an <c>underscore</c> is a line of one CSS pixel under it;
/// without such a character (the end of a line, an atomic inline), they are 1ch long, after the insertion point. The caret is
/// kept inside the padding box of its block container (or the extent of its line when it is longer), so the caret at the end
/// of a line that fills its container is not clipped.
/// </para>
/// </remarks>
internal sealed class PaintedCaret
{
    /// <summary>The thickness of a bar caret and of an underscore caret, in CSS pixels.</summary>
    private const double Thickness = 1;

    private PaintedCaret()
    {
    }

    /// <summary>The node the caret belongs to (the text control, or the node of the selection), for the display items.</summary>
    public required Node Source { get; init; }

    /// <summary>The line box of the caret, or null when its block container has no line box.</summary>
    public LineBoxFragment? Line { get; private init; }

    /// <summary>The fragment of the block container of the caret.</summary>
    public required BoxFragment Container { get; init; }

    /// <summary>
    /// The rectangle of the caret relative to the root of the fragment tree (see <see cref="Fragment.GetOffsetInRoot"/>), or null
    /// for a block caret that is the overlay of <see cref="CoveredText"/>.
    /// </summary>
    public PhysicalRect? Rect { get; private init; }

    /// <summary>The text fragment whose character is covered by a block caret (the caret is painted under its glyphs), or null.</summary>
    public TextFragment? CoveredText { get; private init; }

    /// <summary>The offset in the text node of the character covered by a block caret.</summary>
    public int CoveredStart { get; private init; }

    /// <summary>The offset after the character covered by a block caret.</summary>
    public int CoveredEnd { get; private init; }

    /// <summary>The color of the caret.</summary>
    public DisplayColor Color { get; private init; }

    /// <summary>The color of the character covered by a block caret (the second value of <c>caret-color</c>), or null for the color of the text.</summary>
    public DisplayColor? TextColor { get; private init; }

    /// <summary>Whether the caret blinks (<c>caret-animation: auto</c>): it is not painted in the hidden phase of the blinking.</summary>
    public bool IsAnimated { get; private init; }

    /// <summary>Returns the caret of a document, or null when no caret is displayed. The layout of the document must be up to date.</summary>
    public static PaintedCaret? Compute(LayoutEngine engine, Document document)
    {
        if (document.BrowsingContext is not { } browsingContext || !browsingContext.Top.Viewport.CaretVisible)
            return null;

        // The caret is in the document of the focused area of the top-level traversable, when it has the system focus
        if (!browsingContext.HasSystemFocus || FocusModel.GetCurrentlyFocusedAreaOf(document) is not { } area || !ReferenceEquals(area as Document ?? area.NodeDocument, document))
            return null;

        if (document.FocusedArea is { } focused && TextControlLayout.HasInnerEditor(focused))
            return ComputeForTextControl(engine, focused);

        // The selection in editable content, when it has the focus
        if (document.GetSelection() is not { HasEditingFocus: true } selection || selection.GetCaretPosition() is not var (node, offset, attachedUpstream) || Editing.GetEditingHost(node) is null)
            return null;

        return ComputeForEditableContent(engine, node, offset, attachedUpstream);
    }

    private static PaintedCaret? ComputeForTextControl(LayoutEngine engine, Element control)
    {
        // As in Chrome, a control that cannot be edited (read-only) has no caret
        if (control is not IEditableTextControl { IsEditMutable: true } || TextControlLayout.GetSelection(control) is not var (start, end) || start != end)
            return null;

        if (engine.GetFragments(control) is not [var fragment, ..] || TextControlLayout.GetInnerEditorFragment(fragment) is not { } editor
            || TextControlLayout.GetCaretFragment(control, editor, start) is not { Parent: LineBoxFragment line } text)
        {
            return null;
        }

        var style = engine.Document.GetStyleEngine().GetStyle(control);
        var horizontal = text.WritingDirection.IsHorizontal;
        var position = text.GetRangeRect(start, start).Translate(text.GetOffsetInRoot());
        var site = new Site(horizontal, horizontal ? position.X : position.Y, GetForward(editor.WritingDirection), horizontal ? position.Y : position.X, horizontal ? position.Height : position.Width);

        // The character after the insertion point, when it is rendered on the line (not the end of the value or of a line)
        var covered = text.Run.Kind == TextRunKind.Generated && start < text.End && GetInlineSize(text.GetRangeRect(start, start + 1), horizontal) > 0 ? (text, start, start + 1) : ((TextFragment, int, int)?)null;
        return Create(control, style, line, editor, site, covered, GetLineExtent(line, horizontal));
    }

    private static PaintedCaret? ComputeForEditableContent(LayoutEngine engine, Node node, int offset, bool attachedUpstream)
    {
        var styleEngine = engine.Document.GetStyleEngine();
        if ((node as Element ?? node.ParentElement) is not { } element || !ComputedStyleDeclarations.IsBeingRendered(element))
            return null;

        var style = styleEngine.GetStyle(element);
        var host = Selection.GetOutermostEditingHost(node);
        if (host is null)
            return null;

        if (CaretNavigator.Create(engine, host, host) is not { } navigator)
            return ComputeForEmptyBlock(engine, node, element, style);

        var stop = navigator.Resolve(node, offset, attachedUpstream);
        var line = navigator.Lines[stop.Line];
        var units = line.Units;
        var downstream = stop.Index < units.Count ? units[stop.Index] : null;
        var upstream = stop.Index > 0 ? units[stop.Index - 1] : null;

        // The caret is at the start edge of the unit after it, or at the end edge of the unit before it (at the end of the line,
        // or when a movement attached it to the content before it)
        var attachedToUpstream = upstream is not null && (downstream is null || attachedUpstream);
        var unit = attachedToUpstream ? upstream! : downstream!;
        var horizontal = line.IsHorizontal;
        var extent = (unit.Fragment ?? line.Fragment).GetRectInRoot();
        var site = new Site(horizontal, attachedToUpstream ? unit.EndCoordinate : unit.StartCoordinate, GetForward(line.Container.WritingDirection), horizontal ? extent.Y : extent.X, horizontal ? extent.Height : extent.Width);

        // The next visible character on the line
        var covered = downstream is { Kind: CaretUnitKind.Character, Fragment: TextFragment text } ? (text, downstream.Start.Offset, downstream.End.Offset) : ((TextFragment, int, int)?)null;
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        foreach (var lineUnit in units)
        {
            low = Math.Min(low, lineUnit.Low);
            high = Math.Max(high, lineUnit.High);
        }

        return Create(node, style, line.Fragment, line.Container, site, covered, (low, high));
    }

    /// <summary>
    /// The caret of editable content without line box (an empty editing host): the caret of the first line of the block
    /// container of the position, as in the browsers (the <c>LocalCaretRectForEmptyElement</c> of Blink).
    /// </summary>
    private static PaintedCaret? ComputeForEmptyBlock(LayoutEngine engine, Node node, Element element, ComputedStyle style)
    {
        if (engine.BoxTree is not { } boxTree)
            return null;

        BoxFragment? container = null;
        for (Node? current = element; current is not null && container is null; current = current.ParentNode)
        {
            if (current is Element ancestor && boxTree.GetPrincipalBox(ancestor) is { IsBlockContainer: true } box && engine.GetLaidOutFirstFragment(box) is BoxFragment fragment)
            {
                container = fragment;
                style = box.Style;
            }

            if (Editing.IsEditingHost(current))
                break;
        }

        if (container is null)
            return null;

        var layout = style.Layout;
        var writingDirection = container.WritingDirection;
        var content = writingDirection.ToLogical(container.ContentBoxRect, container.Size);
        var isRightToLeft = writingDirection.IsRightToLeft;
        // (text-indent: hanging does not indent the first line)
        var indent = layout.Text.TextIndentHanging ? 0 : layout.Text.TextIndent.Resolve(content.Size.Inline);
        var inlineOffset = layout.Text.TextAlign switch
        {
            TextAlign.Center => (content.Size.Inline + indent) / 2,
            TextAlign.Left => isRightToLeft ? content.Size.Inline : indent,
            TextAlign.Right => isRightToLeft ? indent : content.Size.Inline,
            TextAlign.End => content.Size.Inline,
            _ => indent,
        };

        var metrics = style.Fonts.PrimaryFont.GetMetrics(layout.FontSize);
        var fontHeight = metrics.Ascent + metrics.Descent;
        var blockOffset = (layout.UsedLineHeight - fontHeight) / 2;
        var origin = container.GetOffsetInRoot();
        var rect = writingDirection.ToPhysical(new LogicalRect(new LogicalOffset(content.Offset.Inline + inlineOffset, content.Offset.Block + blockOffset), new LogicalSize(0, fontHeight)), container.Size).Translate(origin);
        var horizontal = writingDirection.IsHorizontal;
        var site = new Site(horizontal, horizontal ? rect.X : rect.Y, GetForward(writingDirection), horizontal ? rect.Y : rect.X, horizontal ? rect.Height : rect.Width);

        // The caret stays in the content box
        var contentBox = container.ContentBoxRect.Translate(origin);
        return Create(node, style, line: null, container, site, covered: null, lineExtent: null, horizontal ? (contentBox.Left, contentBox.Right) : (contentBox.Top, contentBox.Bottom));
    }

    /// <summary>The position of the caret on its line, in the coordinates of the root of the fragment tree.</summary>
    /// <param name="Horizontal">Whether the inline axis of the line is horizontal.</param>
    /// <param name="Position">The coordinate of the insertion point along the inline axis.</param>
    /// <param name="Forward">The direction of the line along the inline axis: 1 when the content after the insertion point is at the greater coordinates, else -1.</param>
    /// <param name="CrossStart">The start of the caret in the other axis.</param>
    /// <param name="CrossSize">The size of the caret in the other axis.</param>
    private readonly record struct Site(bool Horizontal, double Position, int Forward, double CrossStart, double CrossSize);

    private static int GetForward(WritingDirection writingDirection) => writingDirection.InlineStart is PhysicalSide.Left or PhysicalSide.Top ? 1 : -1;

    private static double GetInlineSize(PhysicalRect rect, bool horizontal) => horizontal ? rect.Width : rect.Height;

    /// <summary>The extent along the inline axis of the content of a line box, relative to the root of the fragment tree.</summary>
    private static (double Low, double High)? GetLineExtent(LineBoxFragment line, bool horizontal)
    {
        var origin = line.GetOffsetInRoot();
        var low = double.PositiveInfinity;
        var high = double.NegativeInfinity;
        foreach (var child in line.ChildrenSpan)
        {
            var start = horizontal ? origin.X + child.Offset.X : origin.Y + child.Offset.Y;
            low = Math.Min(low, start);
            high = Math.Max(high, start + (horizontal ? child.Size.Width : child.Size.Height));
        }

        return high >= low ? (low, high) : null;
    }

    private static PaintedCaret? Create(Node source, ComputedStyle style, LineBoxFragment? line, BoxFragment container, Site site, (TextFragment Text, int Start, int End)? covered, (double Low, double High)? lineExtent, (double Low, double High)? bounds = null)
    {
        if (style.Layout.Visibility != Visibility.Visible)
            return null;

        var shape = style.GetComputedValue("caret-shape") switch
        {
            "block" => CaretShape.Block,
            "underscore" => CaretShape.Underscore,
            _ => CaretShape.Bar,
        };

        // https://drafts.csswg.org/css-ui-4/#caret-color: the color of the caret, then the color of the character that a block
        // caret covers (auto: the color of the text). The used value of auto is currentcolor: "user agents may automatically
        // adjust the color of the caret to ensure good visibility and contrast with the surrounding content": as in Chrome, an
        // auto block caret is half transparent when the character under it keeps its color, so that it stays legible
        var computed = CssValueComputer.SplitComponents(style.GetComputedValue("caret-color"));
        var used = CssValueComputer.SplitComponents(style.GetUsedValue("caret-color"));
        if (used.Length == 0 || DisplayColor.Parse(used[0]) is not { } color)
            return null;

        var textColor = shape == CaretShape.Block && used.Length > 1 && computed.Length > 1 && computed[1] != "auto" ? DisplayColor.Parse(used[1]) : null;
        if (shape == CaretShape.Block && textColor is null && computed is ["auto", ..])
        {
            color = color.MultiplyAlpha(0.5);
        }

        var isAnimated = style.GetComputedValue("caret-animation") != "manual";
        if (shape == CaretShape.Block && covered is var (text, start, end))
        {
            return new PaintedCaret
            {
                Source = source,
                Line = line,
                Container = container,
                CoveredText = text,
                CoveredStart = start,
                CoveredEnd = end,
                Color = color,
                TextColor = textColor,
                IsAnimated = isAnimated,
            };
        }

        // The extent of the caret along the line: a bar at the insertion point; else the character after it, or 1ch after it
        var zoom = style.Layout.EffectiveZoom;
        double low;
        double size;
        if (shape == CaretShape.Bar)
        {
            size = Thickness;
            low = site.Position;
        }
        else if (covered is var (coveredText, coveredStart, coveredEnd))
        {
            var rect = coveredText.GetRangeRect(coveredStart, coveredEnd).Translate(coveredText.GetOffsetInRoot());
            size = GetInlineSize(rect, site.Horizontal);
            low = site.Horizontal ? rect.X : rect.Y;
        }
        else
        {
            size = style.GetFontRelativeLength(FontRelativeUnit.Ch) * zoom;
            low = site.Forward > 0 ? site.Position : site.Position - size;
        }

        // The caret stays in its block container (or in its line when the line overflows the container)
        var (min, max) = bounds ?? GetBounds(container, site.Horizontal);
        if (lineExtent is var (lineLow, lineHigh))
        {
            min = Math.Min(min, lineLow);
            max = Math.Max(max, lineHigh);
        }

        low = Math.Max(min, Math.Min(low, max - size));

        var crossStart = site.CrossStart;
        var crossSize = site.CrossSize;
        if (shape == CaretShape.Underscore)
        {
            // https://drafts.csswg.org/css-writing-modes-4/#line-under
            if (container.WritingDirection.LineUnder is PhysicalSide.Bottom or PhysicalSide.Right)
            {
                crossStart += Math.Max(0, crossSize - Thickness);
            }

            crossSize = Math.Min(crossSize, Thickness);
        }

        return new PaintedCaret
        {
            Source = source,
            Line = line,
            Container = container,
            Rect = site.Horizontal ? new PhysicalRect(low, crossStart, size, crossSize) : new PhysicalRect(crossStart, low, crossSize, size),
            Color = color,
            IsAnimated = isAnimated,
        };
    }

    /// <summary>The extent along the inline axis of the padding box of a block container, relative to the root of the fragment tree.</summary>
    private static (double Low, double High) GetBounds(BoxFragment container, bool horizontal)
    {
        var box = container.PaddingBoxRect.Translate(container.GetOffsetInRoot());
        return horizontal ? (box.Left, box.Right) : (box.Top, box.Bottom);
    }
}
