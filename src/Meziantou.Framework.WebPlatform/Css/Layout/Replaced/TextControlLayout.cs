using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The contents of the text controls (https://html.spec.whatwg.org/multipage/rendering.html#the-textarea-element-2,
/// https://html.spec.whatwg.org/multipage/rendering.html#the-input-element-as-a-text-entry-widget): a <c>textarea</c> and the
/// text fields (<c>input</c> elements whose type is text, search, tel, url, email, password or number) are replaced boxes
/// (their natural sizes are the ones of <see cref="FormControlSizing"/>) whose child is, as in the browsers, an anonymous block
/// container laid out by the inline layout: the inner editor, whose text is the value of the control; it is followed by the
/// box of the <c>::placeholder</c> pseudo-element, laid out over it, when the value is empty and the control has a placeholder.
/// </summary>
/// <remarks>
/// <para>
/// The boxes are built by <see cref="BoxTreeBuilder"/> (no DOM node is created: the text is a generated text run, whose
/// offsets are the offsets in the value) with the styles of <see cref="Cascade.StyleEngine.CreateInnerEditorStyle"/>. They are
/// laid out in the content box of the control by <see cref="ReplacedFormattingContext"/>: at its block-start edge for a
/// <c>textarea</c>, which is the scroll container of its inner editor (see <see cref="ScrollContainers.GetUsedOverflow"/>);
/// centered in the block axis for a text field, whose inner editor is a scroll container that does not wrap its line.
/// </para>
/// <para>
/// The fragments are children of the fragment of the control: they are painted and hit tested in the painting order as the
/// contents of the other boxes (the target of their text is the control). The selection of the control (see
/// <see cref="GetSelectedRange"/>), the caret positions and the hit testing of its value read the line boxes of the inner editor.
/// </para>
/// </remarks>
internal static class TextControlLayout
{
    /// <summary>The name of the pseudo-element of the box of the placeholder of a text control (https://drafts.csswg.org/css-pseudo-4/#placeholder-pseudo).</summary>
    public const string PlaceholderPseudoElement = "placeholder";

    /// <summary>Whether an element is a text control whose value is laid out in an inner editor: a <c>textarea</c>, or a text field.</summary>
    public static bool HasInnerEditor(Element? element)
        => element is HtmlTextAreaElement or HtmlInputElement { InputType: InputType.Text or InputType.Search or InputType.Tel or InputType.Url or InputType.Email or InputType.Password or InputType.Number };

    /// <summary>Whether a box is the replaced box of a <c>textarea</c>: the scroll container of its inner editor.</summary>
    public static bool IsMultiline(ContainerBox box) => box.Kind == ContainerBoxKind.Replaced && box.Element is HtmlTextAreaElement && box.PseudoElement is null;

    /// <summary>
    /// Returns the text of the inner editor of a text control: its API value (the line breaks of a <c>textarea</c> are line
    /// feeds; the input of the user when the value sanitization changed it, for number and email fields), a bullet for each
    /// code unit of the value of a password field.
    /// </summary>
    public static string GetValue(Element element) => element switch
    {
        HtmlTextAreaElement textArea => textArea.Value,
        HtmlInputElement { InputType: InputType.Password } password => new string((char)0x2022, password.Value.Length),
        HtmlInputElement input => input.VisibleValue,
        _ => "",
    };

    /// <summary>
    /// Returns the placeholder of a text control (https://html.spec.whatwg.org/multipage/input.html#attr-input-placeholder,
    /// https://html.spec.whatwg.org/multipage/form-elements.html#attr-textarea-placeholder), or null: the line breaks of the
    /// placeholder of a text field are stripped, the ones of a <c>textarea</c> are line feeds.
    /// </summary>
    public static string? GetPlaceholder(Element element)
    {
        if (element.GetAttributeValue("placeholder") is not { Length: > 0 } placeholder)
            return null;

        if (!placeholder.AsSpan().ContainsAny('\r', '\n'))
            return placeholder;

        return element is HtmlTextAreaElement
            ? placeholder.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n')
            : placeholder.Replace("\r", "", StringComparison.Ordinal).Replace("\n", "", StringComparison.Ordinal);
    }

    /// <summary>Returns the inner editor of the replaced box of a text control, or null.</summary>
    public static ContainerBox? GetInnerEditor(ContainerBox control) => control.FirstChild is ContainerBox { IsTextControlInnerEditor: true } editor ? editor : null;

    /// <summary>Returns the fragment of the inner editor of the fragment of a text control, or null.</summary>
    public static BoxFragment? GetInnerEditorFragment(BoxFragment control)
        => control.Box.Kind == ContainerBoxKind.Replaced && control.ChildrenSpan is [BoxFragment { Box.IsTextControlInnerEditor: true } editor, ..] ? editor : null;

    /// <summary>Returns the fragment of the box of the placeholder of the fragment of a text control, or null.</summary>
    public static BoxFragment? GetPlaceholderFragment(BoxFragment control)
        => control.Box.Kind == ContainerBoxKind.Replaced && control.ChildrenSpan is [BoxFragment { Box.IsTextControlInnerEditor: true }, BoxFragment { Box.PseudoElement: PlaceholderPseudoElement } placeholder, ..] ? placeholder : null;

    /// <summary>Returns the text control of a text run of its value (a run of its inner editor), or null.</summary>
    public static Element? GetControl(TextRun run)
        => run is { Kind: TextRunKind.Generated, Node: null, Parent.IsTextControlInnerEditor: true } ? run.Element : null;

    /// <summary>
    /// Lays out the inner editor of a text control, and the box of its placeholder, at the start of its content box, with the
    /// inline size of the content box as their available inline size. Their block size does not change the size of the
    /// control: it is a part of its scrollable overflow; except for a <c>textarea</c> with <c>field-sizing: content</c>, whose
    /// automatic block size is the block size of its lines laid out in its inline size, where they wrap
    /// (https://drafts.csswg.org/css-ui-4/#field-sizing, see <c>ReplacedFormattingContext.GetAutomaticBlockSize</c>).
    /// </summary>
    public static ContentLayoutResult LayoutContents(LayoutContext context, ContainerBox editor, FragmentBuilder builder, in ContentArea content)
    {
        var space = ConstraintSpace.ForContentBox(content.WritingDirection, content.InlineSize, blockSize: null) with { FixedInlineSize = content.InlineSize };
        var blockSize = 0d;
        for (LayoutBox? child = editor; child is ContainerBox box; child = child.NextSibling)
        {
            var fragment = context.LayoutBox(box, space);
            builder.AddChild(fragment, content.Offset);
            blockSize = Math.Max(blockSize, content.WritingDirection.ToLogical(fragment.Size).Block);
        }

        return new ContentLayoutResult(SizesToContents(editor.Parent) ? blockSize : 0) { InFlowBlockSize = blockSize };
    }

    /// <summary>Whether the box is the one of a <c>textarea</c> with <c>field-sizing: content</c>, whose block size is the one of its laid out lines.</summary>
    public static bool SizesToContents(ContainerBox? box) => box is not null && IsMultiline(box) && FormControlSizing.IsFieldSizingContent(box.Style);

    /// <summary>
    /// Positions the contents of a text control in its fragment and returns its baseline (from the block-start edge of its border
    /// box), or null when the contents have no line: the line of a text field is centered in the block axis of its content box
    /// (as in the browsers, also when it is taller than the content box).
    /// </summary>
    public static double? AlignContents(BoxFragment control, FormControlBaseline baseline)
    {
        if (GetInnerEditorFragment(control) is not { } editor)
            return null;

        var writingDirection = control.WritingDirection;
        var edges = writingDirection.ToLogical(control.Padding.Add(control.Border).Add(control.Scrollbars));
        var editorOffset = 0d;
        if (baseline == FormControlBaseline.CenteredLine)
        {
            // The line of the inner editor and the one of the placeholder are centered separately (their fonts may differ)
            var contentBlockSize = writingDirection.ToLogical(control.Size).Block - edges.BlockSum();
            foreach (var child in control.ChildrenSpan)
            {
                var blockOffset = (contentBlockSize - writingDirection.ToLogical(child.Size).Block) / 2;
                child.Offset += writingDirection.BlockStart switch
                {
                    PhysicalSide.Top => new PhysicalOffset(0, blockOffset),
                    PhysicalSide.Left => new PhysicalOffset(blockOffset, 0),
                    PhysicalSide.Right => new PhysicalOffset(-blockOffset, 0),
                    _ => new PhysicalOffset(0, -blockOffset),
                };
                if (ReferenceEquals(child, editor))
                {
                    editorOffset = blockOffset;
                }
            }

            // (the overflow of the fragment is computed again)
            control.MoveInFlowChildren(PhysicalOffset.Zero);
        }

        return editor.FirstBaseline is { } first ? edges.BlockStart + editorOffset + first : null;
    }

    /// <summary>
    /// Returns the selected characters of a text fragment of the value of a text control (the offsets in the value, see
    /// https://html.spec.whatwg.org/multipage/form-control-infrastructure.html#textFieldSelection), and the control, or null
    /// when the fragment has none.
    /// </summary>
    public static (Element Control, int Start, int End)? GetSelectedRange(TextFragment fragment)
    {
        if (GetControl(fragment.Run) is not { } control || GetSelection(control) is not var (selectionStart, selectionEnd))
            return null;

        var start = Math.Max(selectionStart, fragment.Start);
        var end = Math.Min(selectionEnd, fragment.End);
        return start < end ? (control, start, end) : null;
    }

    /// <summary>Returns the selection of a text control, or null for an element whose selection does not apply.</summary>
    public static (int Start, int End)? GetSelection(Element control) => control switch
    {
        HtmlTextAreaElement textArea => ((int)textArea.SelectionStart, (int)textArea.SelectionEnd),
        HtmlInputElement { SelectionStart: { } start, SelectionEnd: { } end } => ((int)start, (int)end),
        _ => null,
    };

    /// <summary>
    /// Returns the caret position of the value of a text control that is the closest to a point (relative to the root of the
    /// fragment tree), and the rectangle of the caret (relative to the root of the fragment tree), or null when the fragment is
    /// not the one of a text control with an inner editor: the position in the line box whose block range contains the point
    /// (else the closest one), at the boundary of the clusters that is the closest to the point along the line.
    /// </summary>
    public static (int Offset, PhysicalRect Caret)? GetCaretPosition(BoxFragment control, PhysicalOffset rootPoint)
    {
        if (!HasInnerEditor(control.Box.Element) || control.Box.PseudoElement is not null || control.Box.Kind != ContainerBoxKind.Replaced)
            return null;

        if (GetInnerEditorFragment(control) is not { } editor)
            return (0, GetEmptyCaretRect(control));

        TextFragment? best = null;
        var bestLocal = default(PhysicalOffset);
        var bestBlock = double.PositiveInfinity;
        var bestInline = double.PositiveInfinity;
        foreach (var text in EnumerateValueFragments(editor))
        {
            if (!LayoutEngine.TryMapFromRoot(text, rootPoint, out var local))
                continue;

            // The line box of the text gives its extent in the block axis: the point between two lines is in one of them
            var line = text.Parent as LineBoxFragment;
            var horizontal = text.WritingDirection.IsHorizontal;
            var lineOffset = line is null ? default : text.Offset;
            var block = horizontal
                ? Distance(local.Y + lineOffset.Y, line?.Size.Height ?? text.Size.Height)
                : Distance(local.X + lineOffset.X, line?.Size.Width ?? text.Size.Width);
            var inline = horizontal ? Distance(local.X, text.Size.Width) : Distance(local.Y, text.Size.Height);
            if (block < bestBlock || (block == bestBlock && inline < bestInline))
            {
                best = text;
                bestLocal = local;
                bestBlock = block;
                bestInline = inline;
            }
        }

        if (best is null)
            return (0, GetEmptyCaretRect(control));

        var distance = best.WritingDirection.InlineStart switch
        {
            PhysicalSide.Left => bestLocal.X,
            PhysicalSide.Right => best.Size.Width - bestLocal.X,
            PhysicalSide.Top => bestLocal.Y,
            _ => best.Size.Height - bestLocal.Y,
        };
        var offset = best.GetDomOffsetAt(distance);
        return (offset, GetCaretRect(best, offset));

        // The distance from a coordinate to the range [0, size]
        static double Distance(double value, double size) => value < 0 ? -value : value > size ? value - size : 0;
    }

    /// <summary>
    /// Returns the rectangle of the caret before a character of the value of a text control (relative to the root of the fragment
    /// tree), or null when the fragment is not the one of a text control with an inner editor: a rectangle without inline size,
    /// as tall as the line box of the character; at the end of the previous text when the character is not rendered (e.g. the
    /// end of the value).
    /// </summary>
    public static PhysicalRect? GetCaretRect(BoxFragment control, int offset)
    {
        if (!HasInnerEditor(control.Box.Element) || control.Box.PseudoElement is not null || control.Box.Kind != ContainerBoxKind.Replaced)
            return null;

        if (GetInnerEditorFragment(control) is not { } editor)
            return GetEmptyCaretRect(control);

        // The fragment of the character; else the one of the last character before it (the end of the value, or a character
        // that is not rendered: the position at the boundary of two lines is at the start of the second one)
        TextFragment? before = null;
        TextFragment? first = null;
        TextFragment? best = null;
        foreach (var text in EnumerateValueFragments(editor))
        {
            if (text.Start <= offset && offset < text.End)
            {
                best = text;
                break;
            }

            if (text.End <= offset && (before is null || text.End > before.End))
            {
                before = text;
            }

            if (first is null || text.Start < first.Start)
            {
                first = text;
            }
        }

        best ??= before ?? first;
        return best is null ? GetEmptyCaretRect(control) : GetCaretRect(best, offset);
    }

    /// <summary>
    /// Scrolls the scrolling box of a text control (the <c>textarea</c>, or the inner editor of a text field) by the smallest
    /// distance that reveals the caret before a character of its value: as in the browsers, a move of the caret by the user
    /// scrolls the value (an instant scroll, which fires <c>scroll</c> at the control).
    /// </summary>
    public static void RevealCaret(Element control, int offset)
    {
        var document = control.NodeDocument;
        if (document.BrowsingContext is null || !HasInnerEditor(control) || !Cascade.ComputedStyleDeclarations.IsBeingRendered(control))
            return;

        var engine = document.GetLayoutEngine();
        if (engine.GetScrollState(control) is not { Fragment: { } scroller } state || engine.GetFragments(control) is not [var fragment, ..] || GetCaretRect(fragment, offset) is not { } caret)
            return;

        // The caret and the scrollport at the current scroll position
        var scrollport = LayoutEngine.MapToRoot(scroller, scroller.PaddingBoxRect).Bounds;
        var x = caret.Left < scrollport.Left ? caret.Left - scrollport.Left : caret.Right > scrollport.Right ? caret.Right - scrollport.Right : 0;
        var y = caret.Top < scrollport.Top ? caret.Top - scrollport.Top : caret.Bottom > scrollport.Bottom ? caret.Bottom - scrollport.Bottom : 0;
        if (x != 0 || y != 0)
        {
            _ = CssomViewScrolling.ScrollElement(engine, state, state.Offset.X + x, state.Offset.Y + y, ScrollBehavior.Instant, control);
        }
    }

    /// <summary>
    /// Returns the text fragment of the inner editor of a text control that the caret before a character of its value is
    /// painted with, or null: the fragment of the line of the character; the line break of the empty last line of the control
    /// for the end of a value that is empty or ends with a line feed; else the fragment of the last character before it (the
    /// end of the value, or a character that is not rendered).
    /// </summary>
    public static TextFragment? GetCaretFragment(Element control, BoxFragment editor, int offset)
    {
        TextFragment? before = null;
        TextFragment? first = null;
        TextFragment? lastLineBreak = null;
        foreach (var text in EnumerateTextFragments(editor))
        {
            if (text.Run.Kind == TextRunKind.LineBreak)
            {
                lastLineBreak = text;
                continue;
            }

            if (text.Start <= offset && offset < text.End)
                return text;

            if (text.End <= offset && (before is null || text.End > before.End))
            {
                before = text;
            }

            if (first is null || text.Start < first.Start)
            {
                first = text;
            }
        }

        var value = GetValue(control);
        if (lastLineBreak is not null && (value.Length == 0 || (offset >= value.Length && value[^1] == '\n')))
            return lastLineBreak;

        return before ?? first ?? lastLineBreak;

        static IEnumerable<TextFragment> EnumerateTextFragments(Fragment parent)
        {
            foreach (var child in parent.Children)
            {
                if (child is TextFragment { IsHyphen: false, Run.Node: null } text)
                {
                    yield return text;
                }
                else if (child is not TextFragment)
                {
                    foreach (var nested in EnumerateTextFragments(child))
                    {
                        yield return nested;
                    }
                }
            }
        }
    }

    private static PhysicalRect GetCaretRect(TextFragment text, int offset)
    {
        var rect = text.GetRangeRect(offset, offset);

        // The caret is as tall as the line box
        if (text.Parent is LineBoxFragment line)
        {
            rect = text.WritingDirection.IsHorizontal
                ? new PhysicalRect(rect.X, -text.Offset.Y, 0, line.Size.Height)
                : new PhysicalRect(-text.Offset.X, rect.Y, line.Size.Width, 0);
        }

        return LayoutEngine.MapToRoot(text, rect).Bounds;
    }

    /// <summary>
    /// The caret of a text control without text: at the inline-start edge of its content box, with the height of a line (at the
    /// block-start edge of the content box of a <c>textarea</c>, centered in the one of a text field).
    /// </summary>
    private static PhysicalRect GetEmptyCaretRect(BoxFragment control)
    {
        var style = control.Box.Style;
        var writingDirection = control.WritingDirection;
        var content = writingDirection.ToLogical(control.ContentBoxRect, control.Size);
        var lineHeight = control.Box.Element is HtmlInputElement ? FormControlSizing.GetTextFieldLineHeight(style) : style.Layout.UsedLineHeight;
        var blockStart = control.Box.Element is HtmlInputElement ? content.Offset.Block + ((content.Size.Block - lineHeight) / 2) : content.Offset.Block;
        var rect = writingDirection.ToPhysical(new LogicalRect(new LogicalOffset(content.Offset.Inline, blockStart), new LogicalSize(0, lineHeight)), control.Size);
        return LayoutEngine.MapToRoot(control, rect).Bounds;
    }

    /// <summary>Enumerates the text fragments of the value of a text control in its inner editor, in the order of the lines.</summary>
    private static IEnumerable<TextFragment> EnumerateValueFragments(Fragment parent)
    {
        foreach (var child in parent.Children)
        {
            if (child is TextFragment { IsHyphen: false, Run: { Kind: TextRunKind.Generated, Node: null } } text)
            {
                yield return text;
            }
            else if (child is not TextFragment)
            {
                foreach (var nested in EnumerateValueFragments(child))
                {
                    yield return nested;
                }
            }
        }
    }
}
