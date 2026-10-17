using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

// The plain text content (contenteditable=plaintext-only) and the content that is pasted. The editing specification has
// no algorithm for them: the behavior is the one that the browsers share (WPT editing/plaintext-only, editing/other/paste-*)
internal sealed partial class EditingAlgorithms
{
    /// <summary>Whether the active range starts in plain text content (see <see cref="Editing.IsPlaintextOnly"/>).</summary>
    private bool IsInPlaintextOnlyContent() => _activeRange is { } range && GetEditingHostOf(range.StartContainer) is { } host && Editing.IsPlaintextOnly(host);

    /// <summary>
    /// Inserts the content that the user pastes (or drops) at the active range: the HTML as a fragment
    /// (<see cref="InsertPastedHtml"/>), unless the content is plain text (<paramref name="plaintextOnly"/>) or there is no
    /// HTML: then the text is inserted (<see cref="InsertPlainText"/>; the rendered text of the HTML when there is no text).
    /// </summary>
    public void InsertPastedContent(string? html, string? text, bool plaintextOnly)
    {
        if (_activeRange is null)
            return;

        if (html is not null && !plaintextOnly && _document.IsHtmlDocument)
        {
            InsertPastedHtml(html);
            return;
        }

        DeleteSelection(stripWrappers: false);
        if (!IsEditableOrEditingHost(ActiveRange.StartContainer))
            return;

        text ??= html is not null && _document.IsHtmlDocument ? GetRenderedTextOfHtml(html) : "";
        InsertPlainText(text);
    }

    /// <summary>
    /// The insertHTML command in plain text content: the selection is deleted, then the rendered text of the HTML is
    /// inserted (the formatting and the elements are dropped; the blocks and the br elements are line breaks).
    /// </summary>
    private bool InsertHtmlAsPlainText(string value)
    {
        DeleteSelection(stripWrappers: false);
        if (!IsEditableOrEditingHost(ActiveRange.StartContainer))
            return true;

        InsertPlainText(GetRenderedTextOfHtml(value, forCommand: true));
        return true;
    }

    /// <summary>
    /// The rendered text of HTML (its innerText): as in Blink and WebKit, the fragment is inserted in the editing host for
    /// the time of the computation, in an element whose style does not depend on the host (<c>all: initial</c>), so that
    /// the white space of the HTML collapses unless the HTML preserves it (e.g. a pre element), whatever the white-space of
    /// the host.
    /// </summary>
    private string GetRenderedTextOfHtml(string html, bool forCommand = false)
    {
        var range = ActiveRange;
        var context = range.StartContainer as Element ?? range.StartContainer.ParentElement;
        var host = GetEditingHostOf(range.StartContainer);
        if (context is null || host is null)
            return "";

        // The fragment of the command is the one of insertHTML (its sink is checked by Trusted Types); the pasted HTML is
        // sanitized
        var fragment = forCommand ? range.CreateContextualFragmentForEditing(html) : DynamicMarkup.ParsePastedFragment(context, html);
        if (!fragment.HasChildNodes())
            return "";

        var holder = CreateElement("div");
        holder.GetInlineStyle().SetProperty("all", "initial");
        holder.AppendChild(fragment);
        host.AppendChild(holder);
        try
        {
            return RenderedText.GetInnerText(holder) ?? holder.TextContent ?? "";
        }
        finally
        {
            RemoveNode(holder);
        }
    }

    /// <summary>
    /// Inserts plain text at the active range, which is collapsed: the lines are inserted as the insertText command inserts
    /// them (the white space is canonicalized where it collapses), and the line breaks
    /// as the insertLineBreak command inserts them (a line feed where white-space preserves it, else a br element).
    /// </summary>
    private void InsertPlainText(string text)
    {
        text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var start = 0;
        while (IsEditableOrEditingHost(ActiveRange.StartContainer))
        {
            var index = text.IndexOf('\n', start, StringComparison.Ordinal);
            var end = index < 0 ? text.Length : index;
            if (end > start)
            {
                InsertText(text[start..end]);
            }

            if (index < 0)
                break;

            InsertLineBreak();
            start = index + 1;
        }
    }

    /// <summary>
    /// Whether the active range is a caret in a Text node that is before (or after, for a backward deletion) a line feed:
    /// the deletion of a character at the caret deletes the line break, so the line is removed (the last line is not kept
    /// visible by <see cref="PadLastLineFeed"/>).
    /// </summary>
    private bool IsCaretAtLineFeed(bool forward)
    {
        if (_activeRange is not { Collapsed: true, StartContainer: Text text } range)
            return false;

        var index = (int)range.StartOffset - (forward ? 0 : 1);
        return index >= 0 && index < text.Data.Length && text.Data[index] == '\n';
    }

    /// <summary>
    /// Keeps the empty last line of a block visible, when the caret is on it after a line feed that white-space preserves
    /// (the line feed that insertLineBreak inserts at the end of a block, or the one that precedes the last line when a
    /// deletion removes its content): a line feed at the end of a block is not rendered, so a br element is inserted after
    /// it, as the padding line break of an empty block (a second line feed would be in the text content of the block).
    /// </summary>
    private void PadLastLineFeed()
    {
        if (_activeRange is not { Collapsed: true, StartContainer: Text text } range || !IsEditable(text))
            return;

        var offset = (int)range.StartOffset;
        if (offset == 0 || offset != text.Data.Length || text.Data[offset - 1] != '\n' || !PreservesLineFeeds(GetWhiteSpace(text)))
            return;

        if (FindLineItem(text, forward: true) == LineItem.Boundary && text.ParentNode is { } parent && IsAllowedChild("br", parent))
        {
            InsertAfter(CreateElement("br"), text);
        }
    }

    /// <summary>
    /// Removes the line break that rendered the empty last line of a block, after text is inserted on this line (the active
    /// range is the inserted text): the br element that follows the text, when nothing follows it in its block and the text
    /// follows a line break (a br element, or a line feed that white-space preserves); or the line feed that white-space
    /// preserves, when it was the only content of the block. The specification only removes the br that is the sole child
    /// of its parent; the browsers do not leave a line break that has no effect after the text.
    /// </summary>
    private void RemovePaddingLineBreakAfterInsertedText()
    {
        var range = ActiveRange;
        if (range.StartContainer is not Text text || !ReferenceEquals(range.EndContainer, text))
            return;

        var start = (int)range.StartOffset;
        var end = (int)range.EndOffset;
        var data = text.Data;
        if (end == data.Length)
        {
            if (text.NextSibling is not { } next || !IsHtmlElement(next, "br") || !IsEditable(next) || !IsCollapsedLineBreak(next))
                return;

            var followsLineBreak = start > 0
                ? data[start - 1] == '\n' && PreservesLineFeeds(GetWhiteSpace(text))
                : text.PreviousSibling is Text previousText
                    ? previousText.Data.EndsWith('\n', StringComparison.Ordinal) && PreservesLineFeeds(GetWhiteSpace(previousText))
                    : IsHtmlElement(text.PreviousSibling, "br");
            if (followsLineBreak)
            {
                RemoveNode(next);
            }
        }
        else if (start == 0 && end == data.Length - 1 && data[end] == '\n' && PreservesLineFeeds(GetWhiteSpace(text))
            && FindLineItem(text, forward: false) == LineItem.Boundary && FindLineItem(text, forward: true) == LineItem.Boundary)
        {
            text.DeleteData((uint)end, 1);
        }
    }

    /// <summary>
    /// Inserts the HTML that the user pastes: the selection is deleted, the HTML is parsed in the context of the insertion
    /// point and sanitized (the scripts, the event handlers, the comments and the elements of the head are removed), and the
    /// fragment is inserted as the insertHTML command inserts it. The Text nodes at the boundaries of the fragment are then
    /// merged with the adjacent Text nodes (pasting text in the middle of a Text node leaves one Text node), and the white
    /// space at the boundaries is canonicalized (a space that would collapse becomes a no-break space).
    /// </summary>
    private void InsertPastedHtml(string html)
    {
        DeleteSelection();
        var range = ActiveRange;
        if (!IsEditableOrEditingHost(range.StartContainer) || (range.StartContainer as Element ?? range.StartContainer.ParentElement) is not { } context)
            return;

        var fragment = DynamicMarkup.ParsePastedFragment(context, html);
        var first = fragment.FirstChild;
        var last = fragment.LastChild;
        if (first is null || last is null)
            return;

        InsertHtml("", fragment);

        // The caret is after the last node of the fragment: in it when it is a Text node
        Text? caretText = null;
        var caretOffset = 0;
        if (last is Text { ParentNode: not null } lastText)
        {
            caretText = lastText;
            caretOffset = lastText.Data.Length;
        }

        if (first is Text { ParentNode: not null, PreviousSibling: Text previous } firstText && IsEditable(previous) && IsEditable(firstText))
        {
            var length = previous.Data.Length;
            previous.AppendData(firstText.Data);
            RemoveNode(firstText);
            CanonicalizeWhitespace(previous, length, fixCollapsedSpace: false);
            if (ReferenceEquals(first, last))
            {
                caretText = previous;
                caretOffset = previous.Data.Length;
            }
        }

        if (caretText is null)
            return;

        if (caretText.NextSibling is Text next && IsEditable(next) && IsEditable(caretText))
        {
            caretText.AppendData(next.Data);
            RemoveNode(next);
        }

        CollapseTo(caretText, caretOffset);
        CanonicalizeWhitespace(caretText, caretOffset, fixCollapsedSpace: false);
    }
}
