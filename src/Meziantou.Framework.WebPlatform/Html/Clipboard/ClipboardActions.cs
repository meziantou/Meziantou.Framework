using System.Text;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;
using File = Meziantou.Framework.WebPlatform.Dom.FileApi.File;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The copy, cut and paste actions (https://w3c.github.io/clipboard-apis/#clipboard-actions): triggered by the user (e.g. a
/// keyboard shortcut), or by a script (<c>document.execCommand()</c>, the script-triggered flag), when
/// <see cref="BrowsingContextOptions.ClipboardPermission"/> allows the access to the clipboard. The selected content is the
/// selection of the focused text control, or the selection of the document; the content is copied as <c>text/plain</c>
/// (and <c>text/html</c> for the selection of the document). The plain text of the clipboard is pasted in a text control;
/// its HTML, or else its plain text, is pasted in editable content.
/// </summary>
internal static class ClipboardActions
{
    /// <summary>The copy action (https://w3c.github.io/clipboard-apis/#copy-action). Returns false when it is not allowed.</summary>
    public static bool Copy(Document document, bool scriptTriggered)
    {
        if (document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document) || (scriptTriggered && !ClipboardPermissions.IsAllowed(window, ClipboardAccess.Write)))
            return false;

        var clipboard = window.Options.EffectiveClipboard;
        var context = GetContext(document);
        var (canceled, store) = FireClipboardEvent(document, "copy", context, clipboard);
        if (!canceled)
        {
            // The listeners may have changed the selection or the focus. As in the browsers, the command of a script copies
            // nothing and returns false when nothing is selected or when the document is not the focused document.
            if (GetSelectedContent(GetContext(document), copy: true) is { } content && (!scriptTriggered || IsFocusedDocument(document)))
            {
                clipboard.Write([content]);
            }
            else if (scriptTriggered)
            {
                return false;
            }
        }
        else
        {
            WriteContentToClipboard(clipboard, store);
        }

        return true;
    }

    /// <summary>Whether the currently focused area of the top-level browsing context is the viewport or an element of the document (not of a nested document).</summary>
    private static bool IsFocusedDocument(Document document)
        => FocusModel.GetCurrentlyFocusedAreaOf(document) is { } focused && ReferenceEquals(focused as Document ?? focused.NodeDocument, document);

    /// <summary>The cut action (https://w3c.github.io/clipboard-apis/#cut-action). Returns false when it is not allowed, or when there is no selection in an editable context.</summary>
    public static bool Cut(Document document, bool scriptTriggered)
    {
        if (document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document) || (scriptTriggered && !ClipboardPermissions.IsAllowed(window, ClipboardAccess.Write)))
            return false;

        var clipboard = window.Options.EffectiveClipboard;
        var (canceled, store) = FireClipboardEvent(document, "cut", GetContext(document), clipboard);
        if (canceled)
        {
            WriteContentToClipboard(clipboard, store);
            return true;
        }

        var context = GetContext(document);
        if (!context.IsEditable || GetSelectedContent(context) is not { } content)
            return false;

        clipboard.Write([content]);
        RemoveSelectedContent(context, scriptTriggered);
        return true;
    }

    /// <summary>The paste action (https://w3c.github.io/clipboard-apis/#paste-action). Returns false when it is not allowed, canceled, or when the context is not editable.</summary>
    public static bool Paste(Document document, bool scriptTriggered) => Paste(document, scriptTriggered, asPlainText: false);

    /// <summary>
    /// The paste action; <paramref name="asPlainText"/> is the "paste as plain text" action of the browsers (e.g.
    /// Control+Shift+V): only the plain text of the clipboard is available to the paste event and pasted.
    /// </summary>
    public static bool Paste(Document document, bool scriptTriggered, bool asPlainText)
    {
        if (document.RelevantWindow is not { } window || !ReferenceEquals(window.Document, document) || (scriptTriggered && !ClipboardPermissions.IsAllowed(window, ClipboardAccess.Read)))
            return false;

        var clipboard = window.Options.EffectiveClipboard;
        var (canceled, _) = FireClipboardEvent(document, "paste", GetContext(document), clipboard, asPlainText);
        if (canceled || !document.IsFullyActive)
            return false;

        var context = GetContext(document);
        if (!context.IsEditable)
            return false;

        // Editable content: the HTML of the clipboard is pasted, or its plain text
        if (context.TextControl is null)
        {
            PasteInEditableContent(context, clipboard, asPlainText, scriptTriggered);
            return true;
        }

        // The most suitable content: the plain text
        if (clipboard.ReadText() is { Length: > 0 } text)
        {
            InsertText(context, text, scriptTriggered);
        }

        return true;
    }

    /// <summary>
    /// Fires a clipboard event (https://w3c.github.io/clipboard-apis/#fire-a-clipboard-event): a trusted and composed event,
    /// whose clipboardData is associated with a new drag data store (read/write for copy and cut, read-only for paste,
    /// filled with the data of the system clipboard) while it is dispatched. Returns whether the event was canceled.
    /// </summary>
    private static (bool Canceled, DragDataStore Store) FireClipboardEvent(Document document, string type, ClipboardContext context, SystemClipboard clipboard, bool plainTextOnly = false)
    {
        var store = new DragDataStore
        {
            Mode = type == "paste" ? DragDataStoreMode.ReadOnly : DragDataStoreMode.ReadWrite,
            ClipboardClear = new ClipboardClearState(),
        };

        if (type == "paste")
        {
            AddClipboardData(store, clipboard);
            if (plainTextOnly)
            {
                store.Items.RemoveAll(static item => item.Type != "text/plain");
            }
        }

        var dataTransfer = new DataTransfer(store, dropEffect: "none", effectAllowed: "uninitialized");
        var @event = new ClipboardEvent(type, new ClipboardEventInit { Bubbles = true, Cancelable = true, Composed = true, ClipboardData = dataTransfer });
        var target = (Node?)context.Target ?? (Node?)document.Body ?? (Node?)document.DocumentElement ?? document;
        var notCanceled = target.FireEvent(@event);

        // The data of the clipboard is not available outside of the event handlers (https://w3c.github.io/clipboard-apis/#privacy-event-api)
        dataTransfer.Disassociate();
        return (!notCanceled, store);
    }

    /// <summary>The items of the drag data store of a paste event: the representations of the system clipboard.</summary>
    private static void AddClipboardData(DragDataStore store, SystemClipboard clipboard)
    {
        foreach (var item in clipboard.Items)
        {
            foreach (var representation in item.Representations)
            {
                var name = representation.Name;
                if (name.StartsWith("application/web;", StringComparison.Ordinal) || MimeType.Parse(name) is not { } mimeType)
                    continue;

                if (store.Items.Exists(i => i.Type == mimeType.Essence))
                    continue;

                if (mimeType.Type == "image")
                {
                    var file = new File([representation.Data.ToArray()], "image." + mimeType.Subtype.Split('+')[0], new FilePropertyBag { Type = mimeType.Essence });
                    store.Items.Add(new DragDataItem(DragDataItemKind.File, mimeType.Essence, file));
                }
                else
                {
                    store.Items.Add(new DragDataItem(DragDataItemKind.Text, mimeType.Essence, Encoding.UTF8.GetString(representation.Data.Span)));
                }
            }
        }
    }

    /// <summary>
    /// Writes the data set by the listeners of a canceled copy or cut event to the system clipboard
    /// (https://w3c.github.io/clipboard-apis/#write-content-to-the-clipboard): the text items, the image files, and the
    /// web custom formats (the types with the <c>web </c> prefix).
    /// </summary>
    private static void WriteContentToClipboard(SystemClipboard clipboard, DragDataStore store)
    {
        if (store.Items.Count > 0)
        {
            var representations = new List<SystemClipboardRepresentation>();
            var webCustomFormats = new List<(string MimeType, byte[] Data)>();
            foreach (var item in store.Items)
            {
                var bytes = item.Data is File file ? file.Data.ToArray() : Encoding.UTF8.GetBytes(InfraString.ToScalarValueString((string)item.Data));
                var type = item.Type;
                if (ClipboardFormats.RemoveWebPrefix(ref type))
                {
                    if (MimeType.Parse(type) is { } customType)
                    {
                        webCustomFormats.Add((customType.ToString(), bytes));
                    }
                }
                else if (representations.TrueForAll(r => r.Name != type))
                {
                    representations.Add(new SystemClipboardRepresentation(type, bytes));
                }
            }

            ClipboardFormats.AddWebCustomFormats(representations, webCustomFormats);
            clipboard.Write([new SystemClipboardItem(representations)]);
            return;
        }

        if (store.ClipboardClear is not { ClearWasCalled: true } clearState)
            return;

        if (clearState.TypesToClear.Count == 0)
        {
            clipboard.Clear();
            return;
        }

        // The types are removed from the clipboard
        var items = new List<SystemClipboardItem>();
        foreach (var item in clipboard.Items)
        {
            var representations = item.Representations.Where(r => !clearState.TypesToClear.Contains(r.Name, StringComparer.Ordinal)).ToList();
            if (representations.Count > 0)
            {
                items.Add(new SystemClipboardItem(representations, item.PresentationStyle));
            }
        }

        clipboard.Write(items);
    }

    /// <summary>
    /// The context of a clipboard action and the target of its event (https://w3c.github.io/clipboard-apis/#fire-a-clipboard-event):
    /// the focused text control, the element that contains the start of the selection of an editing host, or the focused
    /// element (the body element when it is null).
    /// </summary>
    private static ClipboardContext GetContext(Document document)
    {
        var focused = FocusModel.GetFocusedElement(document);
        if (focused is not null && !ReferenceEquals(focused.NodeDocument, document))
        {
            focused = null;
        }

        if (focused is HtmlTextAreaElement || (focused is HtmlInputElement input && IsEditableInputType(input.InputType)))
            return new ClipboardContext(focused, focused, Range: null, IsEditable: !IsReadOnly(focused));

        Dom.Range? range = null;
        if (document.GetSelection() is { RangeCount: > 0 } selection)
        {
            range = selection.GetRangeAt(0);
            if (Editing.GetEditingHost(range.StartContainer) is not null)
            {
                var start = range.StartContainer as Element ?? range.StartContainer.ParentElement;
                return new ClipboardContext(start, TextControl: null, range, IsEditable: true);
            }
        }

        return new ClipboardContext(focused, TextControl: null, range, IsEditable: false);
    }

    /// <summary>The input types of the editable contexts (https://w3c.github.io/clipboard-apis/#editable-context).</summary>
    private static bool IsEditableInputType(InputType type) => type is InputType.Text or InputType.Search or InputType.Tel or InputType.Url or InputType.Email or InputType.Password or InputType.Number;

    private static bool IsReadOnly(Element control) => control.HasAttribute("readonly") || control.HasAttribute("disabled");

    /// <summary>
    /// The selected content as a system clipboard item, or null when nothing is selected: the selected text of the text
    /// control, or the text and the HTML of the selection of the document.
    /// </summary>
    /// <param name="context">The context of the action.</param>
    /// <param name="copy">
    /// Whether the content is copied, not cut: as in the browsers, the selection of the document is copied when nothing is
    /// selected in the focused text control (e.g. a script selected a range of the document after the control was focused).
    /// </param>
    private static SystemClipboardItem? GetSelectedContent(ClipboardContext context, bool copy = false)
    {
        var range = context.Range;
        if (context.TextControl is { } control)
        {
            var (value, start, end) = GetTextControlSelection(control);
            if (start < end)
                return new SystemClipboardItem([new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes(value[start..end]))]);

            if (!copy)
                return null;

            range = control.NodeDocument.SelectionOrNull?.RangeOrNull;
        }

        if (range is not { Collapsed: false })
            return null;

        // The text of the selection is its rendered text, as in the browsers
        var text = RenderedText.GetRenderedText(range) ?? range.ToString();
        var html = HtmlSerializer.SerializeChildren(range.CloneContents());
        return new SystemClipboardItem(
        [
            new SystemClipboardRepresentation("text/plain", Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(text))),
            new SystemClipboardRepresentation("text/html", Encoding.UTF8.GetBytes(InfraString.ToScalarValueString(html))),
        ]);
    }

    /// <summary>The value and the selection of a text control (the selection is empty when it does not apply to the type of the input).</summary>
    private static (string Value, int Start, int End) GetTextControlSelection(Element control)
    {
        return control switch
        {
            HtmlTextAreaElement textArea => (textArea.Value, (int)Math.Min(textArea.SelectionStart, (uint)textArea.Value.Length), (int)Math.Min(textArea.SelectionEnd, (uint)textArea.Value.Length)),
            HtmlInputElement { SelectionStart: { } start, SelectionEnd: { } end } input => (input.Value, (int)Math.Min(start, (uint)input.Value.Length), (int)Math.Min(end, (uint)input.Value.Length)),
            HtmlInputElement input => (input.Value, 0, 0),
            _ => ("", 0, 0),
        };
    }

    /// <summary>Removes the selected content of the editable context and collapses the selection (the cut action).</summary>
    private static void RemoveSelectedContent(ClipboardContext context, bool scriptTriggered)
    {
        if (context.TextControl is { } control)
        {
            Editing.UserReplaceTextControlSelection(control, "deleteByCut", "", fireBeforeInput: !scriptTriggered);
        }
        else if (context.Target is { } target)
        {
            Editing.UserEdit(target, "deleteByCut", data: null, fireBeforeInput: !scriptTriggered);
        }
    }

    /// <summary>
    /// Pastes the content of the clipboard at the selection of editable content: its HTML (<c>text/html</c>) and its plain
    /// text (<c>text/plain</c>) are the data of the beforeinput and input events, and the HTML is inserted when there is
    /// one (see <see cref="Editing.UserEdit"/>).
    /// </summary>
    private static void PasteInEditableContent(ClipboardContext context, SystemClipboard clipboard, bool asPlainText, bool scriptTriggered)
    {
        var text = clipboard.ReadText();
        string? html = null;
        if (!asPlainText)
        {
            foreach (var item in clipboard.Items)
            {
                if (item.GetRepresentation("text/html") is { } representation)
                {
                    html = Encoding.UTF8.GetString(representation.Data.Span);
                    break;
                }
            }
        }

        if (context.Target is { } target && (text is { Length: > 0 } || html is { Length: > 0 }))
        {
            Editing.UserEdit(target, "insertFromPaste", text, html, fireBeforeInput: !scriptTriggered);
        }
    }

    /// <summary>Inserts the pasted text at the selection of the editable context.</summary>
    private static void InsertText(ClipboardContext context, string text, bool scriptTriggered)
    {
        if (context.TextControl is { } control)
        {
            Editing.UserReplaceTextControlSelection(control, "insertFromPaste", text, fireBeforeInput: !scriptTriggered);
        }
        else if (context.Target is { } target)
        {
            Editing.UserEdit(target, "insertFromPaste", text, html: null, fireBeforeInput: !scriptTriggered);
        }
    }

    private sealed record ClipboardContext(Element? Target, Element? TextControl, Dom.Range? Range, bool IsEditable);
}
