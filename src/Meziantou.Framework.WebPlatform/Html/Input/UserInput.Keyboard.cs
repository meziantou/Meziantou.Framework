using System.Globalization;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Dom;

public sealed partial class UserInput
{
    private KeyboardInput? _keyboard;

    /// <summary>
    /// Gets the keyboard of the user agent: the keys pressed and released by the user, dispatched as trusted keyboard events
    /// (https://w3c.github.io/uievents/#events-keyboardevents) to the focused element of the top-level browsing context.
    /// </summary>
    public KeyboardInput Keyboard => _keyboard ??= new KeyboardInput(this);

    /// <summary>Dispatches a trusted event, in the realm of its target.</summary>
    internal static bool DispatchTrustedEvent(Event @event, Node target) => Dispatch(@event, target);
}

/// <summary>
/// A key of a keyboard: the values of the attributes of its keyboard events (https://w3c.github.io/uievents/#keyboardevent).
/// </summary>
public sealed record KeyboardKey
{
    /// <summary>Creates a key whose code, legacy key code, location and text are derived from its key value, as the keys of a US keyboard.</summary>
    /// <param name="key">The key value (https://w3c.github.io/uievents-key/): a character (e.g. <c>a</c>, <c>A</c>, <c>1</c>, <c>?</c>) or the name of a key (e.g. <c>Enter</c>, <c>Shift</c>, <c>ArrowLeft</c>, <c>F1</c>).</param>
    public KeyboardKey(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Key = key;
        var (code, keyCode, location) = UsKeyboardLayout.Get(key);
        Code = code;
        KeyCode = keyCode;
        Location = location;
        Text = UsKeyboardLayout.GetText(key);
    }

    /// <summary>Gets the key value (the <c>key</c> attribute of the events, https://w3c.github.io/uievents-key/).</summary>
    public string Key { get; init; }

    /// <summary>Gets the physical key (the <c>code</c> attribute of the events, https://w3c.github.io/uievents-code/), e.g. <c>KeyA</c>, or the empty string.</summary>
    public string Code { get; init; }

    /// <summary>Gets the legacy virtual key code (the <c>keyCode</c> attribute of the <c>keydown</c> and <c>keyup</c> events), or 0.</summary>
    public uint KeyCode { get; init; }

    /// <summary>Gets the location of the key on the keyboard (the <c>location</c> attribute of the events): 0 standard, 1 left, 2 right, 3 numeric keypad.</summary>
    public uint Location { get; init; }

    /// <summary>
    /// Gets the text that the key inserts in the editable content (the <c>keypress</c> event is fired when it is not empty),
    /// e.g. the character of the key, or <c>\r</c> for the Enter key; empty for a key that inserts no text.
    /// </summary>
    public string Text { get; init; }
}

/// <summary>
/// The keyboard of the user agent (see <see cref="UserInput.Keyboard"/>). The keys are dispatched to the focused element
/// of the top-level browsing context (or to the body of the focused document), as trusted <c>keydown</c>, <c>keypress</c> and
/// <c>keyup</c> events, and the library runs their default actions: the insertion of the text of the key in a text control or
/// in editable content, the deletion of Backspace and Delete, the sequential focus navigation of Tab, the activation of the
/// focused element by Enter and Space, the caret movements of the arrow keys, Home and End, the close requests of Escape,
/// and the clipboard shortcuts (Control or Meta with C, X and V). The modifier keys that are pressed are the modifiers of
/// the events (and of the pointer events, see <see cref="UserInput.Modifiers"/>).
/// </summary>
public sealed class KeyboardInput
{
    private readonly UserInput _input;
    private readonly List<string> _pressedKeys = [];
    private readonly HashSet<string> _modifiers = new(StringComparer.Ordinal);

    internal KeyboardInput(UserInput input)
    {
        _input = input;
    }

    /// <summary>Gets the keys that are pressed (their key values), in the order they were pressed.</summary>
    public IReadOnlyList<string> PressedKeys => _pressedKeys;

    /// <summary>
    /// Presses a key: the <c>keydown</c> event, then the <c>keypress</c> event and the insertion of its text (a character key or
    /// Enter, not pressed with Control or Meta), or the default action of the key. A key that is already pressed repeats (the
    /// <c>repeat</c> attribute of the events is true). A letter pressed while Shift is pressed is an uppercase letter.
    /// </summary>
    /// <param name="key">The key value (https://w3c.github.io/uievents-key/): a character (e.g. <c>a</c>) or the name of a key (e.g. <c>Enter</c>, <c>Shift</c>, <c>ArrowLeft</c>).</param>
    public void Down(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Down(CreateKey(key));
    }

    /// <summary>
    /// Presses a key described by the values of its keyboard events (see <see cref="Down(string)"/>): its key value is used as is
    /// (the Shift key does not change it).
    /// </summary>
    public void Down(KeyboardKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var repeat = _pressedKeys.Contains(key.Key);
        if (!repeat)
        {
            _pressedKeys.Add(key.Key);
        }

        if (IsModifier(key.Key) && _modifiers.Add(key.Key))
        {
            UpdateInputModifiers();
        }

        if (GetTarget() is not { } target)
            return;

        if (!Dispatch(target, CreateEvent("keydown", target, key, key.KeyCode, charCode: 0, repeat)))
            return;

        // The default action of the key (e.g. Tab) may have moved the focus
        if (GetTarget() is not { } element)
            return;

        // The clipboard shortcuts (Control or Meta with C, X or V) run the copy, cut and paste actions of the focused document
        var hasShortcutModifier = (_modifiers.Contains("Control") || _modifiers.Contains("Meta")) && !_modifiers.Contains("Alt");
        var lowerKey = key.Key.ToLowerInvariant();
        if (hasShortcutModifier && lowerKey is "c" or "x" or "v")
        {
            var document = element.NodeDocument;
            _ = lowerKey switch
            {
                "c" => ClipboardActions.Copy(document, scriptTriggered: false),
                "x" => ClipboardActions.Cut(document, scriptTriggered: false),
                _ => ClipboardActions.Paste(document, scriptTriggered: false, asPlainText: _modifiers.Contains("Shift")),
            };
            return;
        }

        // A key pressed with Control or Meta is a shortcut (e.g. Select All, Undo: the default actions of keydown): it types no character
        var isShortcut = hasShortcutModifier && key.Key != "Enter";
        if (!isShortcut && key.Text.Length > 0)
        {
            var charCode = (uint)char.ConvertToUtf32(key.Text, 0);
            if (Dispatch(element, CreateEvent("keypress", element, key, charCode, charCode, repeat)))
            {
                InsertKeyText(element, key.Text);
            }
        }
        else if (key.Key == "Backspace")
        {
            // Control (Alt on macOS) deletes a word
            Edit(element, _modifiers.Contains("Control") || _modifiers.Contains("Alt") ? "deleteWordBackward" : "deleteContentBackward", data: null);
        }
        else if (key.Key == "Delete")
        {
            Edit(element, _modifiers.Contains("Control") || _modifiers.Contains("Alt") ? "deleteWordForward" : "deleteContentForward", data: null);
        }
    }

    /// <summary>Releases a key: the <c>keyup</c> event. A key that is not pressed is released anyway.</summary>
    /// <param name="key">The key value of the key (see <see cref="Down(string)"/>).</param>
    public void Up(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        Up(CreateKey(key));
    }

    /// <summary>Releases a key described by the values of its keyboard events (see <see cref="Up(string)"/>).</summary>
    public void Up(KeyboardKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        _pressedKeys.Remove(key.Key);
        if (IsModifier(key.Key) && _modifiers.Remove(key.Key))
        {
            UpdateInputModifiers();
        }

        if (GetTarget() is not { } target)
            return;

        Dispatch(target, CreateEvent("keyup", target, key, key.KeyCode, charCode: 0, repeat: false));
    }

    /// <summary>Presses and releases a key (see <see cref="Down(string)"/> and <see cref="Up(string)"/>).</summary>
    /// <param name="key">The key value of the key.</param>
    public void Press(string key)
    {
        ArgumentNullException.ThrowIfNull(key);
        var keyboardKey = CreateKey(key);
        Down(keyboardKey);
        Up(keyboardKey);
    }

    /// <summary>
    /// Types a text: presses and releases the key of each character (a line feed or a carriage return is the Enter key).
    /// The characters that a US keyboard cannot type are typed as keys that have no code.
    /// </summary>
    /// <param name="text">The text.</param>
    public void Type(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        foreach (var rune in text.EnumerateRunes())
        {
            var value = rune.ToString();
            var key = value is "\n" or "\r" ? new KeyboardKey("Enter") : new KeyboardKey(value);
            Down(key);
            Up(key);
        }
    }

    /// <summary>
    /// Inserts a text in the focused text control or editable content, as an input method that commits a text: the
    /// <c>beforeinput</c> and <c>input</c> events of an <c>insertText</c> edit, without keyboard events.
    /// </summary>
    /// <param name="text">The text.</param>
    public void InsertText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        if (text.Length > 0 && GetTarget() is { } element)
        {
            Edit(element, "insertText", text);
        }
    }

    private KeyboardKey CreateKey(string key)
    {
        // The key value of a letter key pressed while the Shift key is down is the uppercase letter, as the keyboards produce
        if (key.Length == 1 && key[0] is >= 'a' and <= 'z' && _modifiers.Contains("Shift"))
            return new KeyboardKey(key.ToUpperInvariant());

        return new KeyboardKey(key);
    }

    private static bool IsModifier(string key) => key is "Shift" or "Control" or "Alt" or "Meta";

    /// <summary>
    /// The target of the keyboard events (https://html.spec.whatwg.org/multipage/interaction.html#focus-processing-model): the
    /// focused element of the top-level browsing context, else the body element (or the document element) of the focused document.
    /// </summary>
    private Element? GetTarget()
    {
        var topDocument = _input.BrowsingContext.Document;
        if (!topDocument.IsFullyActive)
            return null;

        return FocusModel.GetCurrentlyFocusedArea(topDocument) switch
        {
            Element element => element,
            Document document => document.Body ?? document.DocumentElement,
            _ => null,
        };
    }

    /// <summary>The pressed modifier keys are the modifiers of the pointer and wheel events (<see cref="UserInput.Modifiers"/>).</summary>
    private void UpdateInputModifiers()
    {
        _input.Modifiers =
            (_modifiers.Contains("Shift") ? InputModifiers.Shift : InputModifiers.None)
            | (_modifiers.Contains("Control") ? InputModifiers.Control : InputModifiers.None)
            | (_modifiers.Contains("Alt") ? InputModifiers.Alt : InputModifiers.None)
            | (_modifiers.Contains("Meta") ? InputModifiers.Meta : InputModifiers.None);
    }

    private KeyboardEvent CreateEvent(string type, Element target, KeyboardKey key, uint keyCode, uint charCode, bool repeat) => new(type, new KeyboardEventInit
    {
        Bubbles = true,
        Cancelable = true,
        Composed = true,
        View = target.NodeDocument.DefaultView,
        Key = key.Key,
        Code = key.Code,
        Location = key.Location,
        KeyCode = keyCode,
        CharCode = charCode,
        Repeat = repeat,
        ShiftKey = _modifiers.Contains("Shift"),
        CtrlKey = _modifiers.Contains("Control"),
        AltKey = _modifiers.Contains("Alt"),
        MetaKey = _modifiers.Contains("Meta"),
    });

    private static bool Dispatch(Element target, Event @event) => UserInput.DispatchTrustedEvent(@event, target);

    private void InsertKeyText(Element element, string text)
    {
        // The text is inserted in the value of a text control, or at the selection of editable content: the Enter key inserts
        // a paragraph, or a line break with the Shift key
        var data = text is "\r" or "\n" ? "\n" : text;

        // The space key activates a focused button or summary element, even when it is editable: it types no character
        if (data == " " && element is not HtmlInputElement && UserAgentDefaultActions.IsActivatedBySpace(element))
            return;

        if (element is HtmlInputElement or HtmlTextAreaElement)
        {
            Editing.UserEditTextControl(element, data == "\n" ? "insertLineBreak" : "insertText", data);
        }
        else if (element is HtmlElement { IsContentEditable: true })
        {
            if (data == "\n")
            {
                Editing.UserEdit(element, _modifiers.Contains("Shift") ? "insertLineBreak" : "insertParagraph", data: null);
            }
            else
            {
                Editing.UserEdit(element, "insertText", data);
            }
        }
    }

    private static void Edit(Element element, string inputType, string? data)
    {
        if (element is HtmlInputElement or HtmlTextAreaElement)
        {
            Editing.UserEditTextControl(element, inputType, data);
        }
        else if (element is HtmlElement { IsContentEditable: true })
        {
            Editing.UserEdit(element, inputType, data);
        }
    }
}

/// <summary>The codes, legacy key codes and locations of the keys of a US keyboard (https://w3c.github.io/uievents-code/).</summary>
internal static class UsKeyboardLayout
{
    private static readonly Dictionary<string, (string Code, uint KeyCode, uint Location)> NamedKeys = new(StringComparer.Ordinal)
    {
        ["Backspace"] = ("Backspace", 8, 0),
        ["Tab"] = ("Tab", 9, 0),
        ["Enter"] = ("Enter", 13, 0),
        ["Shift"] = ("ShiftLeft", 16, 1),
        ["Control"] = ("ControlLeft", 17, 1),
        ["Alt"] = ("AltLeft", 18, 1),
        ["Pause"] = ("Pause", 19, 0),
        ["CapsLock"] = ("CapsLock", 20, 0),
        ["Escape"] = ("Escape", 27, 0),
        ["PageUp"] = ("PageUp", 33, 0),
        ["PageDown"] = ("PageDown", 34, 0),
        ["End"] = ("End", 35, 0),
        ["Home"] = ("Home", 36, 0),
        ["ArrowLeft"] = ("ArrowLeft", 37, 0),
        ["ArrowUp"] = ("ArrowUp", 38, 0),
        ["ArrowRight"] = ("ArrowRight", 39, 0),
        ["ArrowDown"] = ("ArrowDown", 40, 0),
        ["PrintScreen"] = ("PrintScreen", 44, 0),
        ["Insert"] = ("Insert", 45, 0),
        ["Delete"] = ("Delete", 46, 0),
        ["Meta"] = ("MetaLeft", 91, 1),
        ["ContextMenu"] = ("ContextMenu", 93, 0),
        ["NumLock"] = ("NumLock", 144, 0),
        ["ScrollLock"] = ("ScrollLock", 145, 0),
    };

    // The characters of the keys of a US keyboard, without and with the Shift key, and their codes and key codes
    private static readonly (char Unshifted, char Shifted, string Code, uint KeyCode)[] Punctuation =
    [
        (' ', ' ', "Space", 32),
        ('`', '~', "Backquote", 192),
        ('-', '_', "Minus", 189),
        ('=', '+', "Equal", 187),
        ('[', '{', "BracketLeft", 219),
        (']', '}', "BracketRight", 221),
        ('\\', '|', "Backslash", 220),
        (';', ':', "Semicolon", 186),
        ('\'', '"', "Quote", 222),
        (',', '<', "Comma", 188),
        ('.', '>', "Period", 190),
        ('/', '?', "Slash", 191),
    ];

    private const string ShiftedDigits = ")!@#$%^&*(";

    public static (string Code, uint KeyCode, uint Location) Get(string key)
    {
        if (NamedKeys.TryGetValue(key, out var named))
            return named;

        // F1 to F24
        if (key.Length is 2 or 3 && key[0] == 'F' && int.TryParse(key.AsSpan(1), NumberStyles.None, CultureInfo.InvariantCulture, out var function) && function is >= 1 and <= 24)
            return (key, (uint)(111 + function), 0);

        if (key.Length != 1)
            return ("", 0, 0);

        var c = key[0];
        if (c is >= 'a' and <= 'z' or >= 'A' and <= 'Z')
            return ("Key" + char.ToUpperInvariant(c), char.ToUpperInvariant(c), 0);

        if (c is >= '0' and <= '9')
            return ("Digit" + c, c, 0);

        var shiftedDigit = ShiftedDigits.IndexOf(c, StringComparison.Ordinal);
        if (shiftedDigit >= 0)
            return ("Digit" + (char)('0' + shiftedDigit), (uint)('0' + shiftedDigit), 0);

        foreach (var (unshifted, shifted, code, keyCode) in Punctuation)
        {
            if (c == unshifted || c == shifted)
                return (code, keyCode, 0);
        }

        return ("", 0, 0);
    }

    /// <summary>The text that a key inserts: its character, or a carriage return for Enter.</summary>
    public static string GetText(string key)
    {
        if (key == "Enter")
            return "\r";

        if (key.Length == 1 || (key.Length == 2 && char.IsSurrogatePair(key, 0)))
            return key;

        return "";
    }
}
