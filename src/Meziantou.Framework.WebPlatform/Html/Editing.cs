using System.Collections.Frozen;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Html;

/// <summary>
/// The editing hosts and the editable nodes (https://html.spec.whatwg.org/multipage/interaction.html#editing-host,
/// https://w3c.github.io/editing/docs/execCommand/#editable), and the editing methods of the documents
/// (https://w3c.github.io/editing/docs/execCommand/#methods-to-query-and-execute-commands): the commands of the editing
/// specification, whose algorithms are in <see cref="EditingAlgorithms"/>, and the clipboard commands copy, cut and paste
/// (https://w3c.github.io/clipboard-apis/#integration-with-rich-text-editing).
/// </summary>
internal static class Editing
{
    /// <summary>The commands that are supported, in lowercase.</summary>
    private static readonly FrozenSet<string> SupportedCommands = new[]
    {
        "backcolor", "bold", "copy", "createlink", "cut", "defaultparagraphseparator", "delete", "fontname", "fontsize", "forecolor",
        "formatblock", "forwarddelete", "hilitecolor", "indent", "inserthorizontalrule", "inserthtml", "insertimage", "insertlinebreak",
        "insertorderedlist", "insertparagraph", "inserttext", "insertunorderedlist", "italic", "justifycenter", "justifyfull",
        "justifyleft", "justifyright", "outdent", "paste", "redo", "removeformat", "selectall", "strikethrough", "stylewithcss", "subscript",
        "superscript", "underline", "undo", "unlink", "usecss",
    }.ToFrozenSet(StringComparer.Ordinal);

    /// <summary>
    /// The input type of the input event of a command (https://w3c.github.io/input-events/#interface-InputEvent-Attributes),
    /// the empty string for the commands that have none.
    /// </summary>
    private static string GetInputType(string command) => command switch
    {
        "backcolor" or "hilitecolor" => "formatBackColor",
        "bold" => "formatBold",
        "createlink" => "insertLink",
        "delete" => "deleteContentBackward",
        "fontname" => "formatFontName",
        "forecolor" => "formatFontColor",
        "forwarddelete" => "deleteContentForward",
        "indent" => "formatIndent",
        "inserthorizontalrule" => "insertHorizontalRule",
        "insertlinebreak" => "insertLineBreak",
        "insertorderedlist" => "insertOrderedList",
        "insertparagraph" => "insertParagraph",
        "inserttext" => "insertText",
        "insertunorderedlist" => "insertUnorderedList",
        "italic" => "formatItalic",
        "justifycenter" => "formatJustifyCenter",
        "justifyfull" => "formatJustifyFull",
        "justifyleft" => "formatJustifyLeft",
        "justifyright" => "formatJustifyRight",
        "outdent" => "formatOutdent",
        "removeformat" => "formatRemove",
        "strikethrough" => "formatStrikeThrough",
        "subscript" => "formatSubscript",
        "superscript" => "formatSuperscript",
        "underline" => "formatUnderline",
        _ => "",
    };

    /// <summary>The miscellaneous commands (https://w3c.github.io/editing/docs/execCommand/#miscellaneous-commands): they are always enabled, and fire no event.</summary>
    private static bool IsMiscellaneousCommand(string command) => command is "defaultparagraphseparator" or "selectall" or "stylewithcss" or "usecss";

    /// <summary>The miscellaneous commands that set a state of the document: they do not depend on the focused text control.</summary>
    private static bool IsStateCommand(string command) => command is "defaultparagraphseparator" or "stylewithcss" or "usecss";

    /// <summary>
    /// The undo and redo commands, and the undo and redo of the user (e.g. a keyboard shortcut) in the editable content:
    /// the last transaction of the history of the document is reverted or applied again (see <see cref="EditingHistory"/>),
    /// and input is fired at its editing host with the historyUndo or historyRedo input type, after beforeinput (cancelable)
    /// when the user triggers it. Returns false when the history has no such transaction.
    /// </summary>
    public static bool UndoOrRedo(Document document, bool undo, bool userTriggered)
    {
        var history = document.EditingState.History;
        var inputType = undo ? "historyUndo" : "historyRedo";
        if ((undo ? history.PeekUndo() : history.PeekRedo()) is not { } next)
            return false;

        var view = document.DefaultView;
        if (userTriggered && next.EditingHost is { IsConnected: true } target
            && !target.FireEvent(new InputEvent("beforeinput", new InputEventInit { Bubbles = true, Cancelable = true, Composed = true, InputType = inputType, View = view })))
        {
            return false;
        }

        if ((undo ? history.Undo(document) : history.Redo(document)) is not { } transaction)
            return false;

        if (transaction.EditingHost is { IsConnected: true } editingHost && ReferenceEquals(editingHost.NodeDocument, document))
        {
            FireInputEvent(editingHost, inputType, data: null);
        }

        return true;
    }

    /// <summary>
    /// Whether the node is an editing host: an HTML element whose contenteditable attribute is in the True or the
    /// Plaintext-Only state, or the child HTML element of a document whose design mode is enabled.
    /// </summary>
    public static bool IsEditingHost(Node node)
    {
        if (node is not HtmlElement element)
            return false;

        if (element.ParentNode is Document { IsDesignModeEnabled: true })
            return true;

        var table = EnumeratedAttributes.Contenteditable;
        var state = table.GetState(element.GetAttributeValue("contenteditable"));
        return state == table.Keywords["true"] || state == table.Keywords["plaintext-only"];
    }

    /// <summary>
    /// Whether the node is editable (https://w3c.github.io/editing/docs/execCommand/#editable): it is not an editing host,
    /// it is not in the False state of contenteditable, it has no inert attribute, its parent is an editing host or
    /// editable, and it is an HTML, SVG or MathML element or a node that is not an element whose parent is an HTML element.
    /// </summary>
    public static bool IsEditable(Node node)
    {
        if (IsEditingHost(node) || node.ParentNode is not { } parent)
            return false;

        if (node is HtmlElement element)
        {
            var table = EnumeratedAttributes.Contenteditable;
            if (table.GetState(element.GetAttributeValue("contenteditable")) == table.Keywords["false"])
                return false;

            // https://html.spec.whatwg.org/multipage/interaction.html#inert: an inert node that is editable behaves as if it
            // were non-editable (the subtree of an element with the inert attribute is not editable)
            if (element.HasAttributeWithLocalName("inert"))
                return false;
        }
        else if (node is Element other)
        {
            if (!ReferenceEquals(other.NamespaceUri, Namespaces.Svg) && !ReferenceEquals(other.NamespaceUri, Namespaces.MathML))
                return false;
        }
        else if (parent is not HtmlElement)
        {
            return false;
        }

        return IsEditingHost(parent) || IsEditable(parent);
    }

    /// <summary>The editing host of a node (https://w3c.github.io/editing/docs/execCommand/#editing-host-of): the node when it is an editing host, the editing host of its parent when it is editable, else null.</summary>
    public static Element? GetEditingHost(Node node)
    {
        for (var current = node; current is not null; current = current.ParentNode)
        {
            if (IsEditingHost(current))
                return (Element)current;

            if (!IsEditable(current))
                return null;
        }

        return null;
    }

    /// <summary>
    /// Whether the content of an editing host is plain text (the Plaintext-Only state of contenteditable,
    /// https://html.spec.whatwg.org/multipage/interaction.html#attr-contenteditable-plaintextonly-state: the rich formatting
    /// is disabled). As the tests of the browsers expect (WPT editing/plaintext-only), the state is the one of the outermost
    /// editing host of the editable content that contains the host: an editing host that is nested in editable content
    /// behaves as the editing host that contains it, and the document element in design mode is never plain text.
    /// </summary>
    public static bool IsPlaintextOnly(Element editingHost)
    {
        var outermost = editingHost;
        for (var parent = editingHost.ParentNode; parent is not null; parent = parent.ParentNode)
        {
            if (IsEditingHost(parent))
            {
                outermost = (Element)parent;
            }
            else if (!IsEditable(parent))
            {
                break;
            }
        }

        if (outermost.ParentNode is Document { IsDesignModeEnabled: true })
            return false;

        var table = EnumeratedAttributes.Contenteditable;
        return table.GetState(outermost.GetAttributeValue("contenteditable")) == table.Keywords["plaintext-only"];
    }

    /// <summary>
    /// The commands that are not enabled in an editing host whose content is plain text (see <see cref="IsPlaintextOnly"/>):
    /// the commands that format the content or insert elements. The commands that edit text (delete, forwardDelete,
    /// insertHTML, insertLineBreak, insertParagraph, insertText) stay enabled.
    /// </summary>
    private static bool IsRichTextCommand(string command)
        => !IsMiscellaneousCommand(command) && command is not ("copy" or "cut" or "paste" or "delete" or "forwarddelete" or "inserthtml" or "insertlinebreak" or "insertparagraph" or "inserttext");

    /// <summary>
    /// Collapses the selection at the first caret position of an editing host (the start of its first rendered character, or
    /// before its first atomic inline, line break or non-editable element; the start of the host when it has no rendered
    /// content), unless the selection is already in it: the selection of the browsers when an editing host gets the focus.
    /// </summary>
    public static void PlaceCaretAtStart(Element editingHost)
    {
        var document = editingHost.NodeDocument;
        if (document.GetSelection() is not { } selection)
            return;

        if (selection.RangeOrNull is { } range && editingHost.IsShadowIncludingInclusiveAncestorOf(range.StartContainer) && editingHost.IsShadowIncludingInclusiveAncestorOf(range.EndContainer))
            return;

        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is not null && Css.Layout.CaretNavigator.Create(engine, editingHost, editingHost) is { } navigator)
        {
            var point = navigator.GetPoint(new Css.Layout.CaretStop(0, 0), forward: true);
            var (node, offset) = (point.Node, (uint)point.Offset);

            // As in the browsers, the caret does not go past an element that is not editable or an hr element: when one
            // precedes the first caret position (only empty inline elements are before it), the caret is at the start of
            // its parent
            if (FindFirstUneditableElement(editingHost) is { ParentNode: { } parent } stopper
                && AbstractRange.ComparePoints(parent, (uint)stopper.GetIndex(), node, offset) != AbstractRange.BoundaryPointPosition.After)
            {
                (node, offset) = (parent, 0);
            }

            selection.Collapse(node, offset);
            return;
        }

        selection.Collapse(editingHost, 0);
    }

    /// <summary>The first element of an editing host, in tree order, that is not editable (e.g. <c>contenteditable="false"</c>) or is an hr element.</summary>
    private static Element? FindFirstUneditableElement(Element editingHost)
    {
        for (var node = editingHost.FirstChild; node is not null; node = node.FirstChild ?? GetNextSkippingChildren(node, editingHost))
        {
            if (node is not Element element)
                continue;

            if (element.IsHtml(TagId.Hr) || !IsEditable(element))
                return element;
        }

        return null;

        static Node? GetNextSkippingChildren(Node node, Node root)
        {
            for (var current = node; current is not null && !ReferenceEquals(current, root); current = current.ParentNode)
            {
                if (current.NextSibling is { } next)
                    return next;
            }

            return null;
        }
    }

    /// <summary>Whether the command is supported (https://w3c.github.io/editing/docs/execCommand/#supported).</summary>
    public static bool IsSupported(string command) => SupportedCommands.Contains(Infra.InfraAscii.ToAsciiLowercase(command));

    /// <summary>
    /// Whether the command is enabled (https://w3c.github.io/editing/docs/execCommand/#enabled): the miscellaneous commands
    /// always are, the clipboard commands are enabled when the document may access the clipboard (the
    /// script-may-access-clipboard flag of https://w3c.github.io/clipboard-apis/#clipboard-actions), undo and redo when the
    /// history has a transaction to revert or to apply again, the other commands are enabled when the boundary points of the
    /// active range are editable or editing hosts, in an editing host that is not inert. While a text control is focused,
    /// the commands apply to its value (see <see cref="TextControlEditing"/>).
    /// </summary>
    public static bool IsEnabled(Document document, string command)
    {
        command = Infra.InfraAscii.ToAsciiLowercase(command);
        if (!SupportedCommands.Contains(command))
            return false;

        if (!IsStateCommand(command) && TextControlEditing.GetFocusedTextControl(document) is { } textControl)
            return TextControlEditing.IsEnabled(textControl, command);

        // As the other commands, undo and redo need the active range to be in an editing host
        if (command is "undo" or "redo" && !(command == "undo" ? document.EditingState.History.CanUndo : document.EditingState.History.CanRedo))
            return false;

        if (command is "copy" or "cut" or "paste")
            return document.RelevantWindow is { } window && ReferenceEquals(window.Document, document) && ClipboardPermissions.IsAllowed(window, command == "paste" ? ClipboardAccess.Read : ClipboardAccess.Write);

        // An inert editing host behaves as if it were non-editable (https://html.spec.whatwg.org/multipage/interaction.html#inert)
        if (IsMiscellaneousCommand(command))
            return true;

        // The formatting commands are not enabled in plain text content (contenteditable=plaintext-only)
        return GetActiveRangeEditingHost(document) is { } editingHost && !FocusModel.IsInert(editingHost) && !(IsRichTextCommand(command) && IsPlaintextOnly(editingHost));
    }

    /// <summary>Runs a command (https://w3c.github.io/editing/docs/execCommand/#execcommand()). Returns false when the command is not supported or not enabled, or when its value is not valid.</summary>
    public static bool Execute(Document document, string command, string value)
    {
        // As in Chromium and Gecko, a command is not run while another command of the document runs (e.g. from a listener
        // of its input event): https://github.com/w3c/editing/issues/200
        var state = document.EditingState;
        if (state.IsExecutingCommand)
            return false;

        state.IsExecutingCommand = true;
        try
        {
            return ExecuteCore(document, command, value);
        }
        finally
        {
            state.IsExecutingCommand = false;
        }
    }

    private static bool ExecuteCore(Document document, string command, string value)
    {
        command = Infra.InfraAscii.ToAsciiLowercase(command);
        if (!IsEnabled(document, command))
            return false;

        if (!IsStateCommand(command) && TextControlEditing.GetFocusedTextControl(document) is { } textControl)
            return TextControlEditing.Execute(textControl, command, value);

        // https://w3c.github.io/clipboard-apis/#integration-with-rich-text-editing: the action runs synchronously, and its
        // result is the result of the command
        switch (command)
        {
            case "undo" or "redo":
                return UndoOrRedo(document, undo: command == "undo", userTriggered: false);
            case "copy":
                return ClipboardActions.Copy(document, scriptTriggered: true);
            case "cut":
                return ClipboardActions.Cut(document, scriptTriggered: true);
            case "paste":
                return ClipboardActions.Paste(document, scriptTriggered: true);
            case "selectall":
                SelectAll(document);
                return true;
        }

        var algorithms = new EditingAlgorithms(document);
        if (IsMiscellaneousCommand(command))
            return algorithms.RunAction(command, value);

        var editingHost = GetActiveRangeEditingHost(document)!;
        var transaction = EditingHistory.Begin(document);
        bool result;
        try
        {
            result = algorithms.RunAction(command, value);
        }
        finally
        {
            document.EditingState.History.Commit(document, transaction, editingHost);
        }

        if (!result)
            return false;

        FireInputEvent(editingHost, GetInputType(command), command is "inserttext" or "fontname" or "forecolor" or "backcolor" or "hilitecolor" or "createlink" ? value : null);
        return true;
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandindeterm()</summary>
    public static bool QueryIndeterm(Document document, string command)
    {
        command = Infra.InfraAscii.ToAsciiLowercase(command);
        return SupportedCommands.Contains(command) && new EditingAlgorithms(document).QueryIndeterm(command);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandstate()</summary>
    public static bool QueryState(Document document, string command)
    {
        command = Infra.InfraAscii.ToAsciiLowercase(command);
        return SupportedCommands.Contains(command) && new EditingAlgorithms(document).QueryState(command);
    }

    /// <summary>https://w3c.github.io/editing/docs/execCommand/#querycommandvalue()</summary>
    public static string QueryValue(Document document, string command)
    {
        command = Infra.InfraAscii.ToAsciiLowercase(command);
        return SupportedCommands.Contains(command) ? new EditingAlgorithms(document).QueryValue(command) : "";
    }

    /// <summary>
    /// The insertion or deletion of text by the user (e.g. a key press) when <paramref name="target"/> (the focused element)
    /// is editable: beforeinput is fired at the editing host of the active range, then the active range is edited, when it
    /// is in the editing host of the target (e.g. in a contenteditable element of a document in design mode). The content of
    /// a paste (<c>insertFromPaste</c>) is its plain text (<paramref name="data"/>) and its HTML (<paramref name="html"/>),
    /// which are the <c>text/plain</c> and <c>text/html</c> data of the dataTransfer of the events.
    /// </summary>
    /// <param name="fireBeforeInput">False for the cut and paste actions that a script triggers (<c>document.execCommand()</c>): as the commands, they fire no beforeinput event.</param>
    public static void UserEdit(Element target, string inputType, string? data, string? html = null, bool fireBeforeInput = true)
    {
        if (GetEditingHost(target) is not { } targetHost || GetActiveRangeEditingHost(target.NodeDocument) is not { } editingHost || !targetHost.IsShadowIncludingInclusiveAncestorOf(editingHost))
            return;

        // Plain text content has no paragraphs: the Enter key inserts a line break (contenteditable=plaintext-only)
        var plaintextOnly = IsPlaintextOnly(editingHost);
        if (plaintextOnly && inputType == "insertParagraph")
        {
            inputType = "insertLineBreak";
        }

        // https://w3c.github.io/input-events/#overview: the deletion of a selection that is not collapsed is a deletion of
        // content, whatever the granularity of the key
        var document = target.NodeDocument;
        if (inputType is "deleteWordBackward" or "deleteWordForward" && document.GetSelection()?.RangeOrNull is { Collapsed: false })
        {
            inputType = inputType == "deleteWordBackward" ? "deleteContentBackward" : "deleteContentForward";
        }

        // https://w3c.github.io/input-events/#overview: the data of a paste in editable content is in the dataTransfer
        // attribute of the events (their data attribute is null)
        DataTransfer? dataTransfer = null;
        var eventData = data;
        if (inputType == "insertFromPaste")
        {
            // The data of the events cannot be changed by their listeners: the drag data store is in the read-only mode
            var store = new DragDataStore { Mode = DragDataStoreMode.ReadOnly };
            if (data is not null || html is null)
            {
                store.Items.Add(new DragDataItem(DragDataItemKind.Text, "text/plain", data ?? ""));
            }

            if (html is not null)
            {
                store.Items.Add(new DragDataItem(DragDataItemKind.Text, "text/html", html));
            }

            dataTransfer = new DataTransfer(store, dropEffect: "none", effectAllowed: "none");
            eventData = null;
        }

        var view = document.DefaultView;
        if (fireBeforeInput)
        {
            var beforeInput = new InputEvent("beforeinput", new InputEventInit { Bubbles = true, Cancelable = true, Composed = true, Data = eventData, DataTransfer = dataTransfer, InputType = inputType, View = view, TargetRanges = GetTargetRanges(document, inputType) });
            var canceled = !editingHost.FireEvent(beforeInput);

            // The target ranges are only available while the event is dispatched
            beforeInput.ClearTargetRanges();
            if (canceled)
                return;

            // The listeners may have changed the selection
            if (GetActiveRangeEditingHost(document) is not { } host || !ReferenceEquals(host, editingHost))
                return;
        }

        var transaction = EditingHistory.Begin(document);
        try
        {
            RunUserEdit(document, inputType, data, html, plaintextOnly);
        }
        finally
        {
            document.EditingState.History.Commit(document, transaction, editingHost);
        }

        editingHost.FireEvent(new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = eventData, DataTransfer = dataTransfer, InputType = inputType, View = view }));
    }

    /// <summary>Edits the active range for an edit of the user (see <see cref="UserEdit"/>).</summary>
    private static void RunUserEdit(Document document, string inputType, string? data, string? html, bool plaintextOnly)
    {
        var algorithms = new EditingAlgorithms(document);
        switch (inputType)
        {
            case "deleteContentBackward":
                algorithms.RunAction("delete", "");
                break;

            case "deleteContentForward":
                algorithms.RunAction("forwarddelete", "");
                break;

            case "deleteWordBackward" or "deleteWordForward":
                // The word before or after the caret is selected, then deleted (the algorithms edit the new active range)
                document.GetSelection()?.Modify("extend", inputType == "deleteWordBackward" ? "backward" : "forward", "word");
                if (GetActiveRangeEditingHost(document) is null)
                    return;

                new EditingAlgorithms(document).RunAction(inputType == "deleteWordBackward" ? "delete" : "forwarddelete", "");
                break;

            case "deleteByCut":
                algorithms.DeleteActiveRange();
                break;

            case "insertLineBreak":
                algorithms.RunAction("insertlinebreak", "");
                break;

            case "insertParagraph":
                algorithms.RunAction("insertparagraph", "");
                break;

            case "insertFromPaste":
                algorithms.InsertPastedContent(html, data, plaintextOnly);
                break;

            default:
                algorithms.RunAction("inserttext", data ?? "");
                break;
        }
    }

    /// <summary>
    /// The default action of the editing shortcuts of a trusted keydown event (Control or Meta with a key): A selects the
    /// whole value of the focused text control, or the content of the editing host of the selection (else of the body); Z
    /// undoes and Shift+Z or Y redo the last edit of the text control or of the editable content; B, I and U run the bold,
    /// italic and underline commands in the editable content, with the beforeinput and input events of their input types.
    /// Returns whether the key is such a shortcut.
    /// </summary>
    public static bool ProcessShortcut(Node target, KeyboardEvent keyboardEvent)
    {
        if (keyboardEvent.AltKey || !(keyboardEvent.CtrlKey || keyboardEvent.MetaKey) || keyboardEvent.Key.Length != 1 || target is not Element element)
            return false;

        var document = element.NodeDocument;
        var textControl = element is IEditableTextControl { IsTextEditor: true } ? (HtmlElement)element : null;
        var key = char.ToLowerInvariant(keyboardEvent.Key[0]);
        switch (key)
        {
            case 'a' when !keyboardEvent.ShiftKey:
                if (textControl is not null)
                {
                    TextControlEditing.SelectAll(textControl);
                }
                else if (document.GetSelection() is not { IsCollapsed: true } selection || Selection.FireSelectStart(selection.AnchorNode ?? (Node?)document.Body ?? document))
                {
                    // selectstart is fired when the selection is extended from a caret, or created
                    SelectAll(document);
                }

                return true;

            case 'z' or 'y':
                var undo = key == 'z' && !keyboardEvent.ShiftKey;
                if (textControl is not null)
                {
                    TextControlEditing.UserEdit(textControl, undo ? "historyUndo" : "historyRedo", data: null, fireBeforeInput: true);
                }
                else if (GetEditingHost(element) is not null)
                {
                    UndoOrRedo(document, undo, userTriggered: true);
                }

                return true;

            case 'b' or 'i' or 'u' when !keyboardEvent.ShiftKey && textControl is null:
                if (GetEditingHost(element) is null)
                    return false;

                UserFormat(element, key == 'b' ? "bold" : key == 'i' ? "italic" : "underline");
                return true;

            default:
                return false;
        }
    }

    /// <summary>A formatting command run by the user (e.g. a keyboard shortcut) in the editable content: beforeinput (cancelable) is fired at the editing host of the active range, then the command runs as <see cref="Execute"/> does.</summary>
    private static void UserFormat(Element target, string command)
    {
        var document = target.NodeDocument;
        if (GetEditingHost(target) is not { } targetHost || GetActiveRangeEditingHost(document) is not { } editingHost || !targetHost.IsShadowIncludingInclusiveAncestorOf(editingHost)
            || FocusModel.IsInert(editingHost))
        {
            return;
        }

        var beforeInput = new InputEvent("beforeinput", new InputEventInit { Bubbles = true, Cancelable = true, Composed = true, InputType = GetInputType(command), View = document.DefaultView, TargetRanges = GetTargetRanges(document, GetInputType(command)) });
        var canceled = !editingHost.FireEvent(beforeInput);
        beforeInput.ClearTargetRanges();
        if (!canceled && ReferenceEquals(GetActiveRangeEditingHost(document), editingHost))
        {
            Execute(document, command, "");
        }
    }

    /// <summary>
    /// The target ranges of the beforeinput event of a user edit of editable content (https://w3c.github.io/input-events/#dom-inputevent-gettargetranges):
    /// the range of the selection, or, for a deletion at a caret, the caret extended by a character or by a word in the
    /// direction of the deletion (as <c>Selection.modify()</c> extends it), adjusted as the browsers do (see
    /// <see cref="EditingAlgorithms.GetTargetRange"/>).
    /// </summary>
    private static StaticRange[] GetTargetRanges(Document document, string inputType)
    {
        if (document.GetSelection() is not { RangeOrNull: { } range } selection)
            return [];

        var (startContainer, startOffset, endContainer, endOffset) = (range.StartContainer, range.StartOffset, range.EndContainer, range.EndOffset);
        var deletion = inputType is "deleteContentBackward" or "deleteContentForward" or "deleteWordBackward" or "deleteWordForward";
        var backward = inputType is "deleteContentBackward" or "deleteWordBackward";
        var character = inputType is "deleteContentBackward" or "deleteContentForward";
        var algorithms = new EditingAlgorithms(document);
        if (range.Collapsed && deletion)
        {
            selection.Modify("extend", backward ? "backward" : "forward", character ? "character" : "word");
            if (selection.RangeOrNull is { } extended)
            {
                var target = algorithms.GetTargetRange(extended.StartContainer, (int)extended.StartOffset, extended.EndContainer, (int)extended.EndOffset, startContainer, (int)startOffset, deletion, backward, character);
                selection.Collapse(startContainer, startOffset);
                return [new StaticRange(new StaticRangeInit { StartContainer = target.StartNode, StartOffset = (uint)target.StartOffset, EndContainer = target.EndNode, EndOffset = (uint)target.EndOffset })];
            }
        }

        if (!range.Collapsed)
        {
            var target = algorithms.GetTargetRange(startContainer, (int)startOffset, endContainer, (int)endOffset, caretNode: null, 0, deletion, backward, character);
            return [new StaticRange(new StaticRangeInit { StartContainer = target.StartNode, StartOffset = (uint)target.StartOffset, EndContainer = target.EndNode, EndOffset = (uint)target.EndOffset })];
        }

        return [new StaticRange(new StaticRangeInit { StartContainer = startContainer, StartOffset = startOffset, EndContainer = endContainer, EndOffset = endOffset })];
    }

    /// <summary>An edit of the value of a text control by the user (see <see cref="TextControlEditing.UserEdit"/>). Returns false when the element is not a text control or nothing is edited.</summary>
    public static bool UserEditTextControl(Element element, string inputType, string? data) => TextControlEditing.UserEdit(element, inputType, data, fireBeforeInput: true);

    /// <summary>
    /// The replacement of the selection of a text control by the user (the paste and cut actions): the selected text is
    /// replaced with <paramref name="data"/>, the selection is collapsed after it, and beforeinput (unless a script
    /// triggered the action) and input are fired.
    /// </summary>
    public static void UserReplaceTextControlSelection(Element control, string inputType, string data, bool fireBeforeInput = true) => TextControlEditing.UserEdit(control, inputType, data, fireBeforeInput);

    /// <summary>
    /// The editing host of the active range: the closest editing host that is an inclusive ancestor of its start and end
    /// nodes, when these nodes are editable or editing hosts; else null.
    /// </summary>
    private static Element? GetActiveRangeEditingHost(Document document)
    {
        if (document.GetSelection()?.RangeOrNull is not { } range)
            return null;

        var start = range.StartContainer;
        var end = range.EndContainer;
        if (start is not (Text or Element) || end is not (Text or Element) || !document.IsInclusiveAncestorOf(start) || !document.IsInclusiveAncestorOf(end))
            return null;

        if ((!IsEditable(start) && !IsEditingHost(start)) || (!IsEditable(end) && !IsEditingHost(end)))
            return null;

        for (var ancestor = range.CommonAncestorContainer; ancestor is not null; ancestor = ancestor.ParentNode)
        {
            if (IsEditingHost(ancestor))
                return (Element)ancestor;
        }

        return null;
    }

    /// <summary>The selectAll command: selects the content of the editing host of the selection, or of the body element (or the document element).</summary>
    internal static void SelectAll(Document document)
    {
        if (document.GetSelection() is not { } selection)
            return;

        Node? target = GetActiveRangeEditingHost(document);
        target ??= (Node?)document.Body ?? document.DocumentElement;
        if (target is not null)
        {
            selection.SelectAllChildren(target);
        }
    }

    private static void FireInputEvent(Element editingHost, string inputType, string? data)
        => editingHost.FireEvent(new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = data, InputType = inputType, View = editingHost.NodeDocument.DefaultView }));
}
