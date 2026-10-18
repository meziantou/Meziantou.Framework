using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of the pointing device for the drag-and-drop events: its position in the viewport and its pressed buttons.</summary>
/// <param name="X">The horizontal position of the pointer in the viewport.</param>
/// <param name="Y">The vertical position of the pointer in the viewport.</param>
/// <param name="Buttons">The pressed buttons (<see cref="MouseEvent.Buttons"/>).</param>
[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
public readonly record struct DragPointerState(double X, double Y, ushort Buttons);

/// <summary>
/// A drag-and-drop operation (https://html.spec.whatwg.org/multipage/dnd.html#drag-and-drop-processing-model), driven by the
/// user interactions of a host (e.g. the test driver of the web-platform-tests): <see cref="TryStart"/> when the pointer
/// moves with a pressed button, <see cref="Update"/> each time it moves, and <see cref="End"/> when the button is released.
/// The iterations of the processing model run on each move instead of every 350 ms.
/// </summary>
/// <remarks>
/// As in the browsers, the element under the pointer (the immediate user selection) is the current target element whether
/// or not its dragenter event is canceled: dragenter and dragleave are fired when the pointer enters and leaves elements,
/// dragover is fired at the element under the pointer, and the drop is accepted when the dragover event is canceled (the
/// specification instead makes the body element the current target element when the dragenter event is not canceled).
/// A drop event that is not canceled keeps the current drag operation (the dropEffect of the dragend event), instead of
/// resetting it to "none". There is no feedback (drag image) and the drop of files from outside the user agent is not supported.
/// </remarks>
public sealed class DragAndDropOperation
{
    private readonly DragDataStore _store;
    private readonly DragSourceKind _sourceKind;
    private Element? _currentTarget;
    private string _currentDragOperation = "none";

    private DragAndDropOperation(Node sourceNode, DragDataStore store, DragSourceKind sourceKind)
    {
        SourceNode = sourceNode;
        _store = store;
        _sourceKind = sourceKind;
    }

    private enum DragSourceKind
    {
        Element,
        Link,
        Selection,
    }

    /// <summary>The source node: the dragged element, or the first Text node of the dragged selection.</summary>
    public Node SourceNode { get; }

    /// <summary>Whether the operation ended (the button was released, or the drag event was canceled).</summary>
    public bool IsEnded { get; private set; }

    /// <summary>
    /// The steps run when the user attempts to begin a drag operation on <paramref name="node"/> (the node under the
    /// pointer when the button was pressed). Returns null when nothing is dragged or when the dragstart event is canceled.
    /// </summary>
    /// <param name="dragSelection">Whether the selection can be dragged: the press was in the selection (the selection that the moves of the pointer are making is not dragged).</param>
    public static DragAndDropOperation? TryStart(Node node, DragPointerState pointerState, bool dragSelection = true)
    {
        ArgumentNullException.ThrowIfNull(node);
        if (!node.IsConnected)
            return null;

        // Determine what is being dragged: the selection when the node is selected, else the first draggable inclusive ancestor
        Node? sourceNode = null;
        var sourceKind = DragSourceKind.Element;
        var store = new DragDataStore();
        if (dragSelection && node.NodeDocument.GetSelection() is { IsCollapsed: false } selection && selection.ContainsNode(node) && GetFirstSelectedText(selection) is { } text)
        {
            sourceNode = text;
            sourceKind = DragSourceKind.Selection;
            store.Items.Add(new DragDataItem(DragDataItemKind.Text, "text/plain", selection.ToString()));
        }
        else
        {
            // As in the browsers, a press in a text control, a select element or editable content selects text or an
            // option instead of dragging the element or an ancestor
            for (var current = node as Element ?? node.ParentElement; current is not null; current = current.ParentElement)
            {
                if (IsTextControl(current) || current is HtmlSelectElement || current is HtmlElement { IsContentEditable: true })
                    return null;

                if (current is HtmlElement { Draggable: true })
                {
                    sourceNode = current;
                    break;
                }
            }

            if (sourceNode is not Element element)
                return null;

            // The URLs of the dragged nodes (links and images)
            string? url = null;
            if (element.IsHtml(TagId.A) && element.GetAttributeValue("href") is { } href)
            {
                url = Reflection.EncodingParseAndSerializeUrl(element.NodeDocument, href);
                sourceKind = DragSourceKind.Link;
            }
            else if (element.IsHtml(TagId.Img) && element.GetAttributeValue("src") is { } src)
            {
                url = Reflection.EncodingParseAndSerializeUrl(element.NodeDocument, src);
            }

            if (url is not null)
            {
                store.Items.Add(new DragDataItem(DragDataItemKind.Text, "text/uri-list", url));
            }
        }

        var operation = new DragAndDropOperation(sourceNode, store, sourceKind);
        if (!operation.FireDndEvent("dragstart", sourceNode, relatedTarget: null, pointerState, out _))
            return null;

        // The pointer event stream is suppressed (https://w3c.github.io/pointerevents/#suppressing-a-pointer-event-stream)
        FirePointerEvent("pointercancel", sourceNode, pointerState);
        FirePointerEvent("pointerout", sourceNode, pointerState);
        FirePointerEvent("pointerleave", sourceNode, pointerState);
        return operation;
    }

    /// <summary>An iteration of the drag-and-drop operation while the pointer is over <paramref name="immediateUserSelection"/>.</summary>
    public void Update(Element? immediateUserSelection, DragPointerState pointerState)
    {
        if (IsEnded)
            return;

        if (!FireDndEvent("drag", SourceNode, relatedTarget: null, pointerState, out _))
        {
            _currentDragOperation = "none";
            Finish(pointerState);
            return;
        }

        if (!ReferenceEquals(immediateUserSelection, _currentTarget))
        {
            var previousTarget = _currentTarget;
            if (immediateUserSelection is not null && immediateUserSelection.IsConnected)
            {
                FireDndEvent("dragenter", immediateUserSelection, previousTarget, pointerState, out _);
                _currentTarget = immediateUserSelection;
            }
            else
            {
                _currentTarget = null;
            }

            if (previousTarget is not null)
            {
                FireDndEvent("dragleave", previousTarget, _currentTarget, pointerState, out _);
            }
        }

        if (_currentTarget is not null)
        {
            if (FireDndEvent("dragover", _currentTarget, relatedTarget: null, pointerState, out var dataTransfer))
            {
                _currentDragOperation = AcceptsText(_currentTarget) ? GetTextDropOperation() : "none";
            }
            else
            {
                _currentDragOperation = GetDragOperation(dataTransfer.EffectAllowed, dataTransfer.DropEffect);
            }
        }
        else
        {
            _currentDragOperation = "none";
        }
    }

    /// <summary>The last iteration, when the user releases the button (or cancels the operation, e.g. with the Escape key).</summary>
    public void End(DragPointerState pointerState, bool canceled = false)
    {
        if (IsEnded)
            return;

        if (!FireDndEvent("drag", SourceNode, relatedTarget: null, pointerState, out _) || canceled)
        {
            _currentDragOperation = "none";
        }

        Finish(pointerState);
    }

    private void Finish(DragPointerState pointerState)
    {
        IsEnded = true;
        pointerState = pointerState with { Buttons = 0 };
        var target = _currentTarget is { IsConnected: true } ? _currentTarget : null;
        if (_currentDragOperation == "none" || target is null)
        {
            if (target is not null)
            {
                FireDndEvent("dragleave", target, relatedTarget: null, pointerState, out _);
            }

            _currentDragOperation = "none";
        }
        else
        {
            if (!FireDndEvent("drop", target, relatedTarget: null, pointerState, out var dataTransfer))
            {
                _currentDragOperation = dataTransfer.DropEffect;
            }
            else if (AcceptsText(target))
            {
                InsertText(target, (string)_store.Items.Find(i => i.Kind == DragDataItemKind.Text && i.Type == "text/plain")!.Data);
            }
        }

        if (SourceNode.IsConnected)
        {
            FireDndEvent("dragend", SourceNode, relatedTarget: null, pointerState, out _);
        }
    }

    /// <summary>
    /// Fires a DND event (https://html.spec.whatwg.org/multipage/dnd.html#fire-a-dnd-event). Returns false when the event
    /// is canceled; <paramref name="dataTransfer"/> is the DataTransfer object of the event (no longer associated with
    /// the drag data store, with the values of dropEffect and effectAllowed after the dispatch).
    /// </summary>
    private bool FireDndEvent(string type, Node target, EventTarget? relatedTarget, DragPointerState pointerState, out DataTransfer dataTransfer)
    {
        var window = target.NodeDocument.DefaultView;
        var previousMode = _store.Mode;
        if (type == "dragstart")
        {
            _store.Mode = DragDataStoreMode.ReadWrite;
        }
        else if (type == "drop")
        {
            _store.Mode = DragDataStoreMode.ReadOnly;
        }

        var dropEffect = type switch
        {
            "dragstart" or "drag" or "dragleave" => "none",
            "drop" or "dragend" => _currentDragOperation,
            _ => GetInitialDropEffect(_store.AllowedEffectsState),
        };

        dataTransfer = new DataTransfer(_store, dropEffect, _store.AllowedEffectsState);
        var @event = new DragEvent(type, new DragEventInit
        {
            Bubbles = true,
            Cancelable = type is not ("dragleave" or "dragend"),
            Composed = true,
            View = window,
            RelatedTarget = relatedTarget,
            DataTransfer = dataTransfer,
            ClientX = Round(pointerState.X),
            ClientY = Round(pointerState.Y),
            ScreenX = Round(pointerState.X),
            ScreenY = Round(pointerState.Y),
            Buttons = pointerState.Buttons,
        })
        {
            IsTrusted = true,
        };

        var notCanceled = EventDispatcher.Dispatch(@event, target);
        _store.AllowedEffectsState = dataTransfer.EffectAllowed;

        // The drag data store is in the protected mode, except during the dragstart and drop events
        _store.Mode = previousMode;
        dataTransfer.Disassociate();
        return notCanceled;
    }

    private static void FirePointerEvent(string type, Node target, DragPointerState pointerState)
    {
        var isBoundaryEvent = type == "pointerleave";
        var @event = new PointerEvent(type, new PointerEventInit
        {
            Bubbles = !isBoundaryEvent,
            Cancelable = type == "pointerout",
            Composed = true,
            View = target.NodeDocument.DefaultView,
            ClientX = Round(pointerState.X),
            ClientY = Round(pointerState.Y),
            ScreenX = Round(pointerState.X),
            ScreenY = Round(pointerState.Y),
            Button = -1,
            PointerId = 1,
            PointerType = "mouse",
            IsPrimary = true,
        })
        {
            IsTrusted = true,
        };

        EventDispatcher.Dispatch(@event, target);
    }

    private static int Round(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>The dropEffect of the dragenter and dragover events, based on effectAllowed and on what is dragged.</summary>
    private string GetInitialDropEffect(string effectAllowed) => effectAllowed switch
    {
        "none" => "none",
        "copy" or "copyLink" or "copyMove" or "all" => "copy",
        "link" or "linkMove" => "link",
        "move" => "move",
        _ => _sourceKind == DragSourceKind.Link ? "link" : "copy",
    };

    /// <summary>The current drag operation after a canceled dragover event, given effectAllowed and dropEffect.</summary>
    private static string GetDragOperation(string effectAllowed, string dropEffect) => dropEffect switch
    {
        "copy" when effectAllowed is "uninitialized" or "copy" or "copyLink" or "copyMove" or "all" => "copy",
        "link" when effectAllowed is "uninitialized" or "link" or "copyLink" or "linkMove" or "all" => "link",
        "move" when effectAllowed is "uninitialized" or "move" or "copyMove" or "linkMove" or "all" => "move",
        _ => "none",
    };

    /// <summary>The drag operation when text is dropped in a text control or in editable content: a copy, or a move when only moving is allowed.</summary>
    private string GetTextDropOperation() => _store.AllowedEffectsState switch
    {
        "none" or "link" => "none",
        "move" or "linkMove" => "move",
        _ => "copy",
    };

    /// <summary>Whether the target is a text control or editable content and the drag data store has a text/plain item.</summary>
    private bool AcceptsText(Element target)
        => (IsTextControl(target) || target is HtmlElement { IsContentEditable: true })
           && _store.Items.Exists(i => i.Kind == DragDataItemKind.Text && i.Type == "text/plain");

    /// <summary>A textarea element, or an input element in the Text, Search, Tel, URL, Email, Password or Number state.</summary>
    private static bool IsTextControl(Element element) => element switch
    {
        HtmlTextAreaElement => true,
        HtmlInputElement input => input.InputType is InputType.Text or InputType.Search or InputType.Tel or InputType.Url or InputType.Email or InputType.Password or InputType.Number,
        _ => false,
    };

    /// <summary>The default action of the drop event in a text control or in editable content: the text is inserted at the end.</summary>
    private static void InsertText(Element target, string text)
    {
        switch (target)
        {
            case HtmlTextAreaElement textArea:
                textArea.Value += text;
                break;
            case HtmlInputElement input:
                input.Value += text;
                break;
            default:
                target.AppendChild(target.NodeDocument.CreateTextNode(text));
                break;
        }

        var @event = new InputEvent("input", new InputEventInit { Bubbles = true, Composed = true, Data = text, InputType = "insertFromDrop", View = target.NodeDocument.DefaultView })
        {
            IsTrusted = true,
        };

        EventDispatcher.Dispatch(@event, target);
    }

    private static Text? GetFirstSelectedText(Selection selection)
    {
        var range = selection.GetRangeAt(0);
        var root = range.CommonAncestorContainer;
        for (Node? node = root; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is Text text && selection.ContainsNode(text, allowPartialContainment: true))
                return text;
        }

        return null;
    }
}
