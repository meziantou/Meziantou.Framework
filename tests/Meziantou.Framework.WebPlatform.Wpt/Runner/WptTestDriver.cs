using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

/// <summary>
/// The user interactions of the test driver (https://web-platform-tests.org/writing-tests/testdriver.html), called by
/// <c>testdriver-vendor.js</c>. There is no WebDriver: the events a browser would dispatch are dispatched to the element
/// (as trusted events). The library runs the default actions of the trusted events: a press of the primary button focuses
/// the clicked element, the Tab key moves the focus, and the arrow keys, Home and End move the caret of the editable content
/// and of the text controls. The keyboard events are dispatched to the focused element.
/// </summary>
internal static class WptTestDriver
{
    /// <summary>The pressed modifier keys of the keyboard of a top-level document (https://w3c.github.io/webdriver/#dfn-key-input-source).</summary>
    private static readonly ConditionalWeakTable<Document, HashSet<string>> PressedModifiers = new();

    /// <summary>The pointers of the pointer input sources of the action sequences (https://w3c.github.io/webdriver/#dfn-pointer-input-source), by id.</summary>
    private static readonly ConditionalWeakTable<UserInput, Dictionary<string, PointerInput>> PointerSources = new();

    /// <summary>
    /// The element click of WebDriver (https://w3c.github.io/webdriver/#element-click): the mouse moves to the in-view center
    /// point of the element (computed by testdriver.js), then its primary button is pressed and released. When the hit testing
    /// finds an element outside of the element at that point (the interactability checks of WebDriver are not implemented), the
    /// pointer and mouse events of the click are dispatched to the element instead.
    /// </summary>
    public static void Click(Element element, double x, double y)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is { } browsingContext && HitTest(document, x, y) is { } hit && element.IsShadowIncludingInclusiveAncestorOf(hit))
        {
            // The element at the point of the top-level browsing context is the element hit in the document, or an element of
            // the document of its nested browsing context (the click of a navigable container clicks in its document)
            var input = browsingContext.Input;
            var (topX, topY) = UserInput.MapToTopLevel(document, x, y);
            for (Node? node = input.HitTest(topX, topY); node is not null; node = node.NodeDocument.BrowsingContext is { Parent: not null, Container: { } container } ? container : null)
            {
                if (ReferenceEquals(node, hit))
                {
                    input.Mouse.Move(topX, topY);
                    input.Mouse.Down();
                    input.Mouse.Up();
                    return;
                }
            }
        }

        DispatchPointerEvent("pointerdown", element, x, y, button: 0, buttons: 1);
        DispatchPointerEvent("mousedown", element, x, y, button: 0, buttons: 1);
        DispatchPointerEvent("pointerup", element, x, y, button: 0, buttons: 0);
        DispatchPointerEvent("mouseup", element, x, y, button: 0, buttons: 0);
        if (element.IsConnected)
        {
            DispatchPointerEvent("click", element, x, y, button: 0, buttons: 0);
        }
    }

    /// <summary>
    /// Returns the element at a point of the viewport of a document, as the hit testing of the browser finds it
    /// (https://drafts.csswg.org/cssom-view/#hit-testing): not retargeted out of the shadow trees (unlike
    /// <c>document.elementFromPoint()</c>). A point outside of the viewport hits nothing.
    /// </summary>
    public static Element? HitTest(Document document, double x, double y)
        => CssomViewGeometry.IsInViewport(document, x, y) ? UserInput.HitTestDocument(document, x, y) : null;

    private static void DispatchPointerEvent(string type, Element target, double x, double y, short button, ushort buttons)
    {
        var isPointerEvent = type.StartsWith("pointer", StringComparison.Ordinal) || type == "click";
        var init = new PointerEventInit
        {
            Bubbles = true,
            Cancelable = true,
            Composed = true,
            View = target.NodeDocument.DefaultView,
            Detail = type is "click" or "mousedown" or "mouseup" ? 1 : 0,
            ClientX = Math.Floor(x),
            ClientY = Math.Floor(y),
            ScreenX = Math.Floor(x),
            ScreenY = Math.Floor(y),
            Button = button,
            Buttons = buttons,
            PointerId = 1,
            PointerType = "mouse",
            IsPrimary = true,
            Width = 1,
            Height = 1,
            Pressure = buttons == 0 ? 0 : 0.5f,
        };

        Dispatch(target, isPointerEvent ? new PointerEvent(type, init) : new MouseEvent(type, init));
    }

    /// <summary>The keys pressed by the key actions of the action sequences of a top-level document (https://w3c.github.io/webdriver/#dfn-key-input-source), in the order they were pressed.</summary>
    private static readonly ConditionalWeakTable<Document, List<string>> PressedKeys = new();

    /// <summary>The start of an action sequence: the click count is counted within an action sequence.</summary>
    public static void BeginActions(Window context) => context.Document.BrowsingContext?.Input.ResetClickCounts();

    /// <summary>
    /// The release actions of WebDriver (https://w3c.github.io/webdriver/#release-actions), which the WPT runner performs before
    /// each action sequence of a test but the first one: the keys and the buttons of the pointers that the previous action
    /// sequences left pressed are released, in the reverse order.
    /// </summary>
    public static void ReleaseActions(Window context)
    {
        if (context.Document.BrowsingContext is not { } browsingContext)
            return;

        if (PressedKeys.TryGetValue(browsingContext.Top.Document, out var keys) && keys.Count > 0)
        {
            var pressed = keys.ToArray();
            keys.Clear();
            if ((context.Document.Body ?? context.Document.DocumentElement) is { } element)
            {
                for (var i = pressed.Length - 1; i >= 0; i--)
                {
                    KeyUp(element, pressed[i]);
                }
            }
        }

        if (PointerSources.TryGetValue(browsingContext.Input, out var sources))
        {
            foreach (var pointer in sources.Values)
            {
                // The buttons of MouseEvent.buttons: the primary button (0) is 1, the secondary one (2) is 2, the auxiliary one (1) is 4
                foreach (var (button, mask) in ((int Button, int Mask)[])[(0, 1), (2, 2), (1, 4), (3, 8), (4, 16)])
                {
                    if ((pointer.Buttons & mask) != 0)
                    {
                        pointer.Up(button);
                    }
                }
            }
        }
    }

    /// <summary>
    /// A key action of an action sequence (https://w3c.github.io/webdriver/#keyboard-actions) in the browsing context of
    /// <paramref name="context"/>: the keys are dispatched to the focused element. The release of a key that is not pressed
    /// does nothing (https://w3c.github.io/webdriver/#dfn-dispatch-a-keyup-action).
    /// </summary>
    public static void KeyAction(Window context, string type, string value)
    {
        var document = context.Document;
        if ((document.Body ?? document.DocumentElement) is not { } element)
            return;

        var keys = PressedKeys.GetValue(document.BrowsingContext?.Top.Document ?? document, _ => []);
        if (type == "keyDown")
        {
            if (!keys.Contains(value))
            {
                keys.Add(value);
            }

            KeyDown(element, value);
        }
        else if (keys.Remove(value))
        {
            KeyUp(element, value);
        }
    }

    /// <summary>
    /// A pointer action of an action sequence (https://w3c.github.io/webdriver/#pointer-actions) in the browsing context of
    /// <paramref name="context"/>: the action is injected in the pointer of the input source (see <see cref="UserInput"/>). The
    /// mouse sources are the mouse of the browser; each pen and touch source is a pointer.
    /// </summary>
    /// <param name="origin">The origin of a move: null for the viewport, an element, or a string for the position of the pointer.</param>
    /// <param name="interpolate">Registers the steps of a move that has a duration (called with the ratio of the elapsed duration), or null for an immediate move.</param>
    public static void PointerAction(Window context, string sourceId, string pointerType, string type, object? origin, double x, double y, int button, PointerInputProperties? properties, Action<Action<double>>? interpolate = null)
    {
        if (context.Document.BrowsingContext is not { } browsingContext)
            return;

        var input = browsingContext.Input;
        var sources = PointerSources.GetValue(input, _ => new(StringComparer.Ordinal));
        if (!sources.TryGetValue(sourceId, out var pointer) || pointer.PointerType != pointerType)
        {
            pointer = input.CreatePointer(pointerType);
            sources[sourceId] = pointer;
        }

        switch (type)
        {
            case "pointerMove":
                // The position of the input source is the one of its last move: the position of the device, which is not
                // the position of the pointer while the pointer is locked
                var (targetX, targetY) = ResolveOrigin(context, origin, x, y, pointer.DeviceX, pointer.DeviceY);
                if (interpolate is not null && (targetX != pointer.DeviceX || targetY != pointer.DeviceY))
                {
                    // The move lasts its duration: the pointer moves along the line to the target (https://w3c.github.io/webdriver/#dfn-perform-a-pointer-move)
                    var (startX, startY) = (pointer.DeviceX, pointer.DeviceY);
                    interpolate(ratio => pointer.Move(startX + ((targetX - startX) * ratio), startY + ((targetY - startY) * ratio), properties));
                }
                else
                {
                    pointer.Move(targetX, targetY, properties);
                }

                break;

            case "pointerDown":
                pointer.Down(button, properties);
                break;

            case "pointerUp":
                pointer.Up(button);
                break;

            case "pointerCancel":
                pointer.Cancel();
                break;
        }
    }

    /// <summary>The properties of the contact of a pointer action (https://w3c.github.io/webdriver/#dfn-process-pointer-parameters), from the JSON of the action; null when there is none.</summary>
    public static PointerInputProperties? ParsePointerProperties(string json)
    {
        using var document = System.Text.Json.JsonDocument.Parse(json);
        var root = document.RootElement;
        if (root.ValueKind != System.Text.Json.JsonValueKind.Object || !root.EnumerateObject().Any())
            return null;

        double? Get(string name) => root.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.Number ? value.GetDouble() : null;
        return new PointerInputProperties
        {
            Width = Get("width") ?? 1,
            Height = Get("height") ?? 1,
            Pressure = Get("pressure"),
            TangentialPressure = Get("tangentialPressure") ?? 0,
            TiltX = (int?)Get("tiltX"),
            TiltY = (int?)Get("tiltY"),
            Twist = (int)(Get("twist") ?? 0),
            AltitudeAngle = Get("altitudeAngle"),
            AzimuthAngle = Get("azimuthAngle"),
        };
    }

    /// <summary>A scroll action of a wheel input source (https://w3c.github.io/webdriver/#wheel-actions).</summary>
    public static void WheelAction(Window context, object? origin, double x, double y, double deltaX, double deltaY)
    {
        if (context.Document.BrowsingContext is not { } browsingContext)
            return;

        var (targetX, targetY) = ResolveOrigin(context, origin, x, y, 0, 0);
        browsingContext.Input.Wheel(targetX, targetY, deltaX, deltaY);
    }

    /// <summary>
    /// Returns the position, in the viewport of the top-level browsing context, of an offset from an origin
    /// (https://w3c.github.io/webdriver/#dfn-origin): the viewport of the browsing context of the actions, the position of the
    /// pointer, or the in-view center point of an element (https://w3c.github.io/webdriver/#dfn-in-view-center-point: the center
    /// of the part of its first box that is in the viewport of its document).
    /// </summary>
    private static (double X, double Y) ResolveOrigin(Window context, object? origin, double x, double y, double pointerX, double pointerY)
    {
        switch (origin)
        {
            case Element element:
                var rects = element.GetClientRects();
                var rect = rects.Count > 0 ? rects[0] : element.GetBoundingClientRect();
                var window = element.NodeDocument.DefaultView;

                // An element that is out of the viewport is scrolled into view first (WebDriver fails with "move target out of
                // bounds"; the tests rely on the size of the window of the browsers)
                if (window is not null && (rect.Bottom <= 0 || rect.Right <= 0 || rect.Top >= window.InnerHeight || rect.Left >= window.InnerWidth))
                {
                    _ = element.ScrollIntoView(new ScrollIntoViewOptions { Behavior = ScrollBehavior.Instant, Block = ScrollLogicalPosition.Nearest, Inline = ScrollLogicalPosition.Nearest });
                    rects = element.GetClientRects();
                    rect = rects.Count > 0 ? rects[0] : element.GetBoundingClientRect();
                }

                var left = Math.Max(0, rect.Left);
                var right = Math.Min(window?.InnerWidth ?? double.PositiveInfinity, rect.Right);
                var top = Math.Max(0, rect.Top);
                var bottom = Math.Min(window?.InnerHeight ?? double.PositiveInfinity, rect.Bottom);
                return UserInput.MapToTopLevel(element.NodeDocument, ((left + right) / 2) + x, ((top + bottom) / 2) + y);

            case string:
                return (pointerX + x, pointerY + y);

            default:
                return UserInput.MapToTopLevel(context.Document, x, y);
        }
    }

    /// <summary>
    /// The element send keys of WebDriver (https://w3c.github.io/webdriver/#element-send-keys): the keyboard events of each
    /// key, and the insertion of the characters in a text control (input, textarea).
    /// </summary>
    public static void SendKeys(Element element, string keys)
    {
        // The element is focused (unless it is the focused element), then the keys are dispatched to the focused element (or
        // to the element when it cannot be focused)
        if (!ReferenceEquals(FocusModel.GetFocusedElement(element.NodeDocument), element))
        {
            // The caret that a script has put in the editing host is kept (the browsers focus the editing host that the selection enters)
            var selectionInElement = element.NodeDocument.GetSelection() is { RangeCount: > 0, AnchorNode: { } anchorNode } && element.IsShadowIncludingInclusiveAncestorOf(anchorNode);
            FocusModel.Focus(element);

            // "Set the text insertion caret using set selection range using current text length for both the start and end
            // parameters": the keys are typed at the end of the value of a text control that was not focused
            if (ReferenceEquals(FocusModel.GetFocusedElement(element.NodeDocument), element) && element is HtmlElement control)
            {
                Html.TextControlEditing.MoveCaretToEnd(control);

                // "Set the text insertion caret after any child content" of an editing host that was not focused
                if (!selectionInElement && Html.Editing.IsEditingHost(control) && control is not (HtmlInputElement or HtmlTextAreaElement) && control.NodeDocument.GetSelection() is { } selection)
                {
                    Node last = control;
                    while (last.LastChild is { } child && (child is Text || (child is HtmlElement { IsContentEditable: true } && child.HasChildNodes())))
                    {
                        last = child;
                    }

                    selection.Collapse(last, last is Text text ? text.Length : (uint)last.ChildNodes.Length);
                }
            }
        }

        var modifiers = new List<string>();
        foreach (var rune in keys.EnumerateRunes())
        {
            var text = rune.ToString();
            if (text == "\U0000E000")
            {
                // The null key releases the modifier keys
                foreach (var modifier in modifiers)
                {
                    KeyUp(element, modifier);
                }

                modifiers.Clear();
                continue;
            }

            KeyDown(element, text);
            if (IsModifier(GetKey(text).Key))
            {
                // A modifier key stays pressed until the end of the keys
                modifiers.Add(text);
            }
            else
            {
                KeyUp(element, text);
            }
        }

        foreach (var modifier in modifiers)
        {
            KeyUp(element, modifier);
        }
    }

    private static bool IsModifier(string key) => key is "Shift" or "Control" or "Alt" or "Meta";

    /// <summary>
    /// The target of the keyboard events (https://html.spec.whatwg.org/multipage/interaction.html#focus-processing-model):
    /// the focused element of the top-level browsing context, else <paramref name="element"/> when it is in the focused
    /// document, else the body element of the focused document.
    /// </summary>
    private static Element GetKeyTarget(Element element)
    {
        if (element.NodeDocument.BrowsingContext?.Top.Document is not { } topDocument)
            return element;

        var focused = FocusModel.GetCurrentlyFocusedArea(topDocument);
        if (focused is Element focusedElement)
            return focusedElement;

        // The viewport is focused: the element when it is in the focused document (it cannot be focused, e.g. the body), else the body
        var document = (Document)focused;
        if (element.IsConnected && ReferenceEquals(element.NodeDocument, document))
            return element;

        return document.Body ?? document.DocumentElement ?? element;
    }

    private static HashSet<string> GetPressedModifiers(Element element) => PressedModifiers.GetValue(element.NodeDocument.BrowsingContext?.Top.Document ?? element.NodeDocument, _ => []);

    /// <summary>The pressed modifier keys are the modifiers of the pointer and wheel events (<see cref="UserInput.Modifiers"/>).</summary>
    private static void UpdateInputModifiers(Element element, HashSet<string> modifiers)
    {
        if (element.NodeDocument.BrowsingContext is not { } browsingContext)
            return;

        browsingContext.Input.Modifiers =
            (modifiers.Contains("Shift") ? InputModifiers.Shift : InputModifiers.None)
            | (modifiers.Contains("Control") ? InputModifiers.Control : InputModifiers.None)
            | (modifiers.Contains("Alt") ? InputModifiers.Alt : InputModifiers.None)
            | (modifiers.Contains("Meta") ? InputModifiers.Meta : InputModifiers.None);
    }

    /// <summary>The key down action (https://w3c.github.io/webdriver/#dfn-dispatch-a-keydown-action): keydown, then keypress and the insertion of a character.</summary>
    public static void KeyDown(Element element, string text)
    {
        element = GetKeyTarget(element);
        var view = element.NodeDocument.DefaultView;
        var (key, code, keyCode) = GetKey(text);
        var modifiers = GetPressedModifiers(element);
        if (IsModifier(key))
        {
            modifiers.Add(key);
            UpdateInputModifiers(element, modifiers);
        }

        key = ApplyShift(key, modifiers);

        if (!Dispatch(element, new KeyboardEvent("keydown", CreateKeyboardEventInit(view, key, code, keyCode, charCode: 0, modifiers))))
            return;

        // The default action of the key (e.g. Tab) may have moved the focus
        element = GetKeyTarget(element);

        // The clipboard shortcuts (Control or Meta with C, X or V) run the copy, cut and paste actions of the focused document
        if ((modifiers.Contains("Control") || modifiers.Contains("Meta")) && !modifiers.Contains("Alt") && key.ToLowerInvariant() is "c" or "x" or "v")
        {
            var document = element.NodeDocument;
            _ = key.ToLowerInvariant() switch
            {
                "c" => ClipboardActions.Copy(document, scriptTriggered: false),
                "x" => ClipboardActions.Cut(document, scriptTriggered: false),
                _ => ClipboardActions.Paste(document, scriptTriggered: false, asPlainText: modifiers.Contains("Shift")),
            };
            return;
        }

        // A key pressed with Control or Meta is a shortcut (e.g. Select All, Undo: the default actions of keydown): it types no character
        var isShortcut = (modifiers.Contains("Control") || modifiers.Contains("Meta")) && !modifiers.Contains("Alt") && key != "Enter";
        var isCharacter = !isShortcut && (key.Length == 1 || (key.Length == 2 && char.IsSurrogatePair(key, 0)) || key == "Enter");
        var charCode = key == "Enter" ? 13u : isCharacter ? (uint)char.ConvertToUtf32(key, 0) : 0u;
        if (isCharacter && Dispatch(element, new KeyboardEvent("keypress", CreateKeyboardEventInit(view, key, code, charCode, charCode, modifiers))))
        {
            InsertText(element, key, modifiers.Contains("Shift"));
        }
        else if (key == "Backspace")
        {
            DeleteBackward(element, word: modifiers.Contains("Control") || modifiers.Contains("Alt"));
        }
        else if (key == "Delete")
        {
            // Control (Alt on macOS) deletes a word
            var inputType = modifiers.Contains("Control") || modifiers.Contains("Alt") ? "deleteWordForward" : "deleteContentForward";
            if (element is HtmlInputElement or HtmlTextAreaElement)
            {
                Html.Editing.UserEditTextControl(element, inputType, data: null);
            }
            else if (element is HtmlElement { IsContentEditable: true })
            {
                Html.Editing.UserEdit(element, inputType, data: null);
            }
        }
    }

    /// <summary>The key up action (https://w3c.github.io/webdriver/#dfn-dispatch-a-keyup-action).</summary>
    public static void KeyUp(Element element, string text)
    {
        element = GetKeyTarget(element);
        var (key, code, keyCode) = GetKey(text);
        var modifiers = GetPressedModifiers(element);
        if (modifiers.Remove(key))
        {
            UpdateInputModifiers(element, modifiers);
        }

        key = ApplyShift(key, modifiers);

        Dispatch(element, new KeyboardEvent("keyup", CreateKeyboardEventInit(element.NodeDocument.DefaultView, key, code, keyCode, charCode: 0, modifiers)));
    }

    /// <summary>The key value of a letter key pressed while the Shift key is down is the uppercase letter, as the keyboards (and the drivers of the browsers) produce.</summary>
    private static string ApplyShift(string key, HashSet<string> modifiers)
        => key.Length == 1 && key[0] is >= 'a' and <= 'z' && modifiers.Contains("Shift") ? key.ToUpperInvariant() : key;

    private static KeyboardEventInit CreateKeyboardEventInit(Window? view, string key, string code, uint keyCode, uint charCode, HashSet<string> modifiers) => new()
    {
        Bubbles = true,
        Cancelable = true,
        Composed = true,
        View = view,
        Key = key,
        Code = code,
        KeyCode = keyCode,
        CharCode = charCode,
        ShiftKey = modifiers.Contains("Shift"),
        CtrlKey = modifiers.Contains("Control"),
        AltKey = modifiers.Contains("Alt"),
        MetaKey = modifiers.Contains("Meta"),
    };

    /// <summary>The key values of the characters and of the special keys of WebDriver (https://w3c.github.io/webdriver/#keyboard-actions).</summary>
    private static (string Key, string Code, uint KeyCode) GetKey(string text)
    {
        if (text.Length == 1 && text[0] is >= '\U0000E000' and <= '\U0000E05D')
        {
            return text[0] switch
            {
                '\U0000E003' => ("Backspace", "Backspace", 8),
                '\U0000E004' => ("Tab", "Tab", 9),
                '\U0000E006' => ("Enter", "NumpadEnter", 13),
                '\U0000E007' => ("Enter", "Enter", 13),
                '\U0000E008' => ("Shift", "ShiftLeft", 16),
                '\U0000E009' => ("Control", "ControlLeft", 17),
                '\U0000E00A' => ("Alt", "AltLeft", 18),
                '\U0000E00C' => ("Escape", "Escape", 27),
                '\U0000E00D' => (" ", "Space", 32),
                '\U0000E00E' => ("PageUp", "PageUp", 33),
                '\U0000E00F' => ("PageDown", "PageDown", 34),
                '\U0000E010' => ("End", "End", 35),
                '\U0000E011' => ("Home", "Home", 36),
                '\U0000E012' => ("ArrowLeft", "ArrowLeft", 37),
                '\U0000E013' => ("ArrowUp", "ArrowUp", 38),
                '\U0000E014' => ("ArrowRight", "ArrowRight", 39),
                '\U0000E015' => ("ArrowDown", "ArrowDown", 40),
                '\U0000E017' => ("Delete", "Delete", 46),
                '\U0000E03D' => ("Meta", "MetaLeft", 91),
                '\U0000E050' => ("Shift", "ShiftRight", 16),
                '\U0000E051' => ("Control", "ControlRight", 17),
                '\U0000E052' => ("Alt", "AltRight", 18),
                '\U0000E053' => ("Meta", "MetaRight", 92),
                _ => ("Unidentified", "", 0),
            };
        }

        if (text.Length == 1)
        {
            var c = text[0];
            if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
                return (text, "Key" + char.ToUpperInvariant(c), char.ToUpperInvariant(c));

            if (c is >= '0' and <= '9')
                return (text, "Digit" + c, c);

            if (c == ' ')
                return (text, "Space", 32);

            if (c is '\n' or '\r')
                return ("Enter", "Enter", 13);
        }

        return (text, "", 0);
    }

    private static void InsertText(Element element, string key, bool shift)
    {
        // The text is inserted in the value of a text control, or at the selection of editable content: the Enter key inserts
        // a paragraph, or a line break with the Shift key
        var data = key == "Enter" ? "\n" : key;

        // The space key activates a focused button or summary element, even when it is editable: it types no character
        if (data == " " && element is not HtmlInputElement && UserAgentDefaultActions.IsActivatedBySpace(element))
            return;

        if (element is HtmlInputElement or HtmlTextAreaElement)
        {
            Html.Editing.UserEditTextControl(element, data == "\n" ? "insertLineBreak" : "insertText", data);
        }
        else if (element is HtmlElement { IsContentEditable: true })
        {
            if (data == "\n")
            {
                Html.Editing.UserEdit(element, shift ? "insertLineBreak" : "insertParagraph", data: null);
            }
            else
            {
                Html.Editing.UserEdit(element, "insertText", data);
            }
        }
    }

    private static void DeleteBackward(Element element, bool word)
    {
        if (element is HtmlInputElement or HtmlTextAreaElement)
        {
            Html.Editing.UserEditTextControl(element, word ? "deleteWordBackward" : "deleteContentBackward", data: null);
        }
        else if (element is HtmlElement { IsContentEditable: true })
        {
            // Control (Alt on macOS) deletes a word
            Html.Editing.UserEdit(element, word ? "deleteWordBackward" : "deleteContentBackward", data: null);
        }
    }

    /// <summary>Dispatches a trusted event (the events of the user interactions are trusted). Returns false when the event is canceled.</summary>
    private static bool Dispatch(Element element, Event @event)
    {
        @event.IsTrusted = true;
        return EventDispatcher.Dispatch(@event, element);
    }

    /// <summary>
    /// The WebDriver extension commands of the virtual authenticators (https://w3c.github.io/webauthn/#sctn-automation), on the
    /// <see cref="VirtualAuthenticatorEnvironment"/> of the runner: <paramref name="argumentsJson"/> is the JSON array of the
    /// arguments of the testdriver function. Returns the JSON of the result (or null for no result); throws a
    /// <see cref="WptWebDriverException"/> for a WebDriver error.
    /// </summary>
    public static string? ExecuteWebAuthnCommand(VirtualAuthenticatorEnvironment environment, string command, string argumentsJson)
    {
        var arguments = JsonNode.Parse(argumentsJson)?.AsArray() ?? throw WptWebDriverException.InvalidArgument("the arguments are not an array");
        switch (command)
        {
            case "add_virtual_authenticator":
                return JsonValue.Create(AddVirtualAuthenticator(environment, arguments[0] as JsonObject ?? throw WptWebDriverException.InvalidArgument("the configuration is not an object"))).ToJsonString();

            case "remove_virtual_authenticator":
                if (!environment.RemoveAuthenticator(GetString(arguments[0], "authenticatorId")))
                    throw WptWebDriverException.InvalidArgument("there is no virtual authenticator with this id");

                return null;

            case "add_credential":
                AddCredential(GetAuthenticator(environment, arguments[0]), arguments[1] as JsonObject ?? throw WptWebDriverException.InvalidArgument("the credential parameters are not an object"));
                return null;

            case "get_credentials":
                return new JsonArray([.. GetAuthenticator(environment, arguments[0]).GetCredentials().Select(ToCredentialParameters)]).ToJsonString();

            case "remove_credential":
                if (!GetAuthenticator(environment, arguments[0]).RemoveCredential(DecodeBase64Url(arguments[1], "credentialId")))
                    throw WptWebDriverException.InvalidArgument("there is no credential with this id");

                return null;

            case "remove_all_credentials":
                GetAuthenticator(environment, arguments[0]).RemoveAllCredentials();
                return null;

            case "set_user_verified":
            {
                // The parameters of the command ({ isUserVerified }), or the flag itself
                var authenticator = GetAuthenticator(environment, arguments[0]);
                var value = arguments[1] is JsonObject parameters ? parameters["isUserVerified"] : arguments[1];
                authenticator.IsUserVerified = value?.GetValueKind() is JsonValueKind.True or JsonValueKind.False
                    ? value.GetValue<bool>()
                    : throw WptWebDriverException.InvalidArgument("isUserVerified is not a boolean");
                return null;
            }

            case "set_credential_properties":
            {
                var authenticator = GetAuthenticator(environment, arguments[0]);
                var credentialId = DecodeBase64Url(arguments[1], "credentialId");
                var properties = arguments[2] as JsonObject ?? throw WptWebDriverException.InvalidArgument("the properties are not an object");
                if (!authenticator.SetCredentialProperties(credentialId, GetOptionalBoolean(properties, "backupEligibility"), GetOptionalBoolean(properties, "backupState")))
                    throw WptWebDriverException.InvalidArgument("there is no credential with this id");

                if (properties.TryGetPropertyValue("signCount", out var signCount))
                {
                    authenticator.SetSignCount(credentialId, GetSignCount(signCount));
                }

                return null;
            }

            default:
                throw new WptWebDriverException($"unknown command: {command}");
        }
    }

    /// <summary>https://w3c.github.io/webauthn/#add-virtual-authenticator</summary>
    private static string AddVirtualAuthenticator(VirtualAuthenticatorEnvironment environment, JsonObject parameters)
    {
        string[] keys = ["protocol", "transport", "hasResidentKey", "hasUserVerification", "isUserConsenting", "isUserVerified", "extensions", "defaultBackupEligibility", "defaultBackupState"];
        foreach (var (key, _) in parameters)
        {
            if (!keys.Contains(key, StringComparer.Ordinal))
                throw WptWebDriverException.InvalidArgument($"'{key}' is not a property of the authenticator configuration");
        }

        var protocol = GetString(parameters["protocol"], "protocol") switch
        {
            "ctap1/u2f" => VirtualAuthenticatorProtocol.Ctap1U2f,
            "ctap2" => VirtualAuthenticatorProtocol.Ctap2,
            "ctap2_1" => VirtualAuthenticatorProtocol.Ctap21,
            var value => throw WptWebDriverException.InvalidArgument($"'{value}' is not a protocol"),
        };
        var transport = GetString(parameters["transport"], "transport");
        if (transport is not ("usb" or "nfc" or "ble" or "smart-card" or "hybrid" or "internal"))
            throw WptWebDriverException.InvalidArgument($"'{transport}' is not a transport");

        var extensions = new List<string>();
        if (parameters["extensions"] is { } extensionsValue)
        {
            foreach (var extension in extensionsValue as JsonArray ?? throw WptWebDriverException.InvalidArgument("extensions is not an array"))
            {
                var name = GetString(extension, "extensions");
                if (!VirtualAuthenticatorOptions.SupportedExtensions.Contains(name, StringComparer.Ordinal))
                    throw new WptWebDriverException($"unsupported operation: the '{name}' extension is not supported");

                extensions.Add(name);
            }
        }

        var authenticator = environment.AddAuthenticator(new VirtualAuthenticatorOptions
        {
            Protocol = protocol,
            Transport = transport,
            HasResidentKey = GetOptionalBoolean(parameters, "hasResidentKey") ?? false,
            HasUserVerification = GetOptionalBoolean(parameters, "hasUserVerification") ?? false,
            IsUserConsenting = GetOptionalBoolean(parameters, "isUserConsenting") ?? true,
            IsUserVerified = GetOptionalBoolean(parameters, "isUserVerified") ?? false,
            Extensions = extensions,
            DefaultBackupEligibility = GetOptionalBoolean(parameters, "defaultBackupEligibility") ?? false,
            DefaultBackupState = GetOptionalBoolean(parameters, "defaultBackupState") ?? false,
        });
        return authenticator.Id;
    }

    /// <summary>https://w3c.github.io/webauthn/#add-credential</summary>
    private static void AddCredential(VirtualAuthenticator authenticator, JsonObject parameters)
    {
        var credentialId = DecodeBase64Url(parameters["credentialId"], "credentialId");
        var isResidentCredential = GetOptionalBoolean(parameters, "isResidentCredential") ?? throw WptWebDriverException.InvalidArgument("isResidentCredential is not defined");
        var rpId = GetString(parameters["rpId"], "rpId");
        if (rpId.Length == 0)
            throw WptWebDriverException.InvalidArgument("rpId is not a valid RP ID");

        var privateKey = DecodeBase64Url(parameters["privateKey"], "privateKey");
        byte[]? userHandle = parameters.ContainsKey("userHandle") ? DecodeBase64Url(parameters["userHandle"], "userHandle") : null;
        if (!parameters.TryGetPropertyValue("signCount", out var signCountValue))
            throw WptWebDriverException.InvalidArgument("signCount is not defined");

        byte[]? largeBlob = parameters.ContainsKey("largeBlob") ? DecodeBase64Url(parameters["largeBlob"], "largeBlob") : null;
        try
        {
            authenticator.AddCredential(new VirtualCredential
            {
                CredentialId = credentialId,
                IsResidentCredential = isResidentCredential,
                RpId = rpId,
                PrivateKey = privateKey,
                UserHandle = userHandle is null ? null : (ReadOnlyMemory<byte>?)userHandle,
                SignCount = GetSignCount(signCountValue),
                LargeBlob = largeBlob is null ? null : (ReadOnlyMemory<byte>?)largeBlob,
                BackupEligibility = GetOptionalBoolean(parameters, "backupEligibility"),
                BackupState = GetOptionalBoolean(parameters, "backupState"),
                UserName = parameters["userName"] is { } userName ? GetString(userName, "userName") : "",
                UserDisplayName = parameters["userDisplayName"] is { } userDisplayName ? GetString(userDisplayName, "userDisplayName") : "",
            });
        }
        catch (ArgumentException ex)
        {
            throw WptWebDriverException.InvalidArgument(ex.Message);
        }
    }

    /// <summary>The credential parameters of a credential (https://w3c.github.io/webauthn/#credential-parameters).</summary>
    private static JsonObject ToCredentialParameters(VirtualCredential credential)
    {
        var result = new JsonObject
        {
            ["credentialId"] = Convert.ToBase64String(credential.CredentialId.Span).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["isResidentCredential"] = credential.IsResidentCredential,
            ["rpId"] = credential.RpId,
            ["privateKey"] = Convert.ToBase64String(credential.PrivateKey.Span).TrimEnd('=').Replace('+', '-').Replace('/', '_'),
            ["signCount"] = credential.SignCount,
            ["backupEligibility"] = credential.BackupEligibility,
            ["backupState"] = credential.BackupState,
            ["userName"] = credential.UserName,
            ["userDisplayName"] = credential.UserDisplayName,
        };
        if (credential.UserHandle is { } userHandle)
        {
            result["userHandle"] = Convert.ToBase64String(userHandle.Span).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        if (credential.LargeBlob is { } largeBlob)
        {
            result["largeBlob"] = Convert.ToBase64String(largeBlob.Span).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        return result;
    }

    private static VirtualAuthenticator GetAuthenticator(VirtualAuthenticatorEnvironment environment, JsonNode? id)
        => environment.GetAuthenticator(GetString(id, "authenticatorId")) ?? throw WptWebDriverException.InvalidArgument("there is no virtual authenticator with this id");

    private static string GetString(JsonNode? value, string name)
        => value?.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : throw WptWebDriverException.InvalidArgument($"{name} is not a string");

    private static bool? GetOptionalBoolean(JsonObject parameters, string name) => parameters[name] switch
    {
        null => null,
        var value when value.GetValueKind() is JsonValueKind.True or JsonValueKind.False => value.GetValue<bool>(),
        _ => throw WptWebDriverException.InvalidArgument($"{name} is not a boolean"),
    };

    /// <summary>A signature counter: an unsigned 32-bit integer, or null (no counter).</summary>
    private static uint? GetSignCount(JsonNode? value)
    {
        if (value is null)
            return null;

        if (value.GetValueKind() == JsonValueKind.Number && value.GetValue<double>() is var number && number >= 0 && number <= uint.MaxValue && Math.Floor(number) == number)
            return (uint)number;

        throw WptWebDriverException.InvalidArgument("signCount is not an unsigned 32-bit integer");
    }

    /// <summary>
    /// Decodes a base64url string of the WebDriver commands; the padding and the characters of the base64 alphabet are
    /// accepted (the tests create some values with <c>btoa()</c>).
    /// </summary>
    private static byte[] DecodeBase64Url(JsonNode? value, string name)
    {
        var text = GetString(value, name).Replace('-', '+').Replace('_', '/');
        text = text.PadRight(text.Length + ((4 - (text.Length % 4)) % 4), '=');
        try
        {
            return Convert.FromBase64String(text);
        }
        catch (FormatException)
        {
            throw WptWebDriverException.InvalidArgument($"{name} is not a valid base64url encoding");
        }
    }
}

/// <summary>A WebDriver error of a command of the test driver (https://w3c.github.io/webdriver/#errors): its message starts with the error code.</summary>
internal sealed class WptWebDriverException : Exception
{
    public WptWebDriverException()
    {
    }

    public WptWebDriverException(string message)
        : base(message)
    {
    }

    public WptWebDriverException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public static WptWebDriverException InvalidArgument(string message) => new("invalid argument: " + message);
}

/// <summary>
/// An action sequence of the test driver (https://w3c.github.io/webdriver/#perform-actions): the actions of each tick, and the
/// duration of the ticks. The ticks are performed from tasks of the event loop of the browsing context (as the input of a
/// user: no script is running, so the microtasks run after each event listener), and a tick lasts its duration on the clock
/// of the event loop.
/// </summary>
/// <param name="context">The window of the browsing context of the actions.</param>
/// <param name="releaseActions">Whether the keys and the buttons left pressed by the previous action sequences are released first (see <see cref="WptTestDriver.ReleaseActions"/>).</param>
internal sealed class WptActionSequence(Window context, bool releaseActions = false)
{
    private readonly List<(List<Action> Actions, double Duration)> _ticks = [];

    /// <summary>The window of the browsing context of the actions.</summary>
    public Window Context { get; } = context;

    /// <summary>The time between the steps of a pointer move that has a duration (a frame).</summary>
    private const double MoveStepDuration = 16;

    /// <summary>The steps of the moves that last a duration, registered by the actions of the tick being performed.</summary>
    private List<(Action<double> Step, double Duration)>? _moves;

    public void Add(int tick, Action action)
    {
        EnsureTick(tick);
        _ticks[tick].Actions.Add(action);
    }

    /// <summary>Registers, from an action of the tick being performed, a move that lasts a duration: its steps are performed during the tick.</summary>
    public void AddMove(Action<double> step, double duration) => (_moves ??= []).Add((step, duration));

    /// <summary>The duration of a tick is the longest duration of its actions, in milliseconds.</summary>
    public void AddDuration(int tick, double duration)
    {
        EnsureTick(tick);
        if (duration > _ticks[tick].Duration)
        {
            _ticks[tick] = (_ticks[tick].Actions, duration);
        }
    }

    private void EnsureTick(int tick)
    {
        while (_ticks.Count <= tick)
        {
            _ticks.Add(([], 0));
        }
    }

    /// <summary>Performs the ticks; <paramref name="completed"/> is called at the end, with the message of the error when an action failed.</summary>
    public void Run(Action<string?> completed)
    {
        // The command reaches the browser after the tasks that are already queued (e.g. the load of a resource), as a WebDriver command
        var eventLoop = Context.EventLoop;
        string? error = null;
        eventLoop.QueueTimer(TimeSpan.FromMilliseconds(1), () =>
        {
            if (releaseActions)
            {
                WptTestDriver.ReleaseActions(Context);
            }

            WptTestDriver.BeginActions(Context);
            RunTick(0);
        });

        void RunTick(int index)
        {
            if (index >= _ticks.Count)
            {
                completed(null);
                return;
            }

            var (actions, duration) = _ticks[index];
            try
            {
                foreach (var action in actions)
                {
                    action();
                }
            }
            catch (Exception ex)
            {
                completed(ex.Message);
                return;
            }

            // The steps of the moves that last a duration, then the next tick
            if (_moves is { Count: > 0 } moves)
            {
                _moves = null;
                foreach (var (step, moveDuration) in moves)
                {
                    var count = Math.Max(1, (int)Math.Ceiling(moveDuration / MoveStepDuration));
                    for (var i = 1; i <= count; i++)
                    {
                        var ratio = (double)i / count;
                        eventLoop.QueueTimer(TimeSpan.FromMilliseconds(moveDuration * ratio), () =>
                        {
                            try
                            {
                                step(ratio);
                            }
                            catch (Exception ex)
                            {
                                error ??= ex.Message;
                            }
                        });
                    }
                }
            }

            eventLoop.QueueTimer(TimeSpan.FromMilliseconds(duration), () =>
            {
                if (error is not null)
                {
                    completed(error);
                    return;
                }

                RunTick(index + 1);
            });
        }
    }
}
