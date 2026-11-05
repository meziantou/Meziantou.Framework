using System.Collections.Frozen;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Keyboard Lock API (https://wicg.github.io/keyboard-lock/, <see cref="BrowsingContextOptions.KeyboardLock"/>).</summary>
public sealed class KeyboardLockOptions
{
    /// <summary>
    /// Gets or sets the callback that registers the system key press handler (https://wicg.github.io/keyboard-lock/#system-key-press-handler)
    /// of a top-level document (<c>navigator.keyboard.lock()</c>), with the reserved key codes (the <c>code</c> values of the
    /// keys, e.g. <c>KeyW</c>; empty to capture all the keys): while the document is fullscreen, the host sends these keys
    /// to the document instead of handling them (e.g. <c>Alt+Tab</c>). A new call replaces the reserved key codes. The
    /// library checks the document (the active document of a top-level browsing context) and the key codes before
    /// calling it. The callback is invoked on the thread of the event loop, and must not block it. The default is null:
    /// no key is captured, but the promise is resolved (as in a browser whose page is not fullscreen).
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext, IReadOnlyList<string>>? Lock { get; init; }

    /// <summary>
    /// Gets or sets the callback that unregisters the system key press handler registered by <see cref="Lock"/>
    /// (<c>navigator.keyboard.unlock()</c>, or the document is unloaded). The callback is invoked on the thread of the
    /// event loop. The default is null.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext>? Unlock { get; init; }
}

/// <summary>
/// The keyboard of a window (https://wicg.github.io/keyboard-lock/#keyboard-interface, <c>navigator.keyboard</c>): the
/// Keyboard Lock API, which captures the keys that the system or the browser handle, with <see cref="KeyboardLockOptions"/>.
/// A document whose keyboard is locked is not kept in the back/forward cache (as in Chromium).
/// </summary>
/// <seealso href="https://wicg.github.io/keyboard-lock/#dom-keyboard"/>
[IdlInterface("Keyboard", "https://wicg.github.io/keyboard-lock/#dom-keyboard")]
public sealed partial class Keyboard : EventTarget
{
    /// <summary>The valid key code attribute values (https://w3c.github.io/uievents-code/#key-code-attribute-value).</summary>
    private static readonly FrozenSet<string> KeyCodes = CreateKeyCodes();

    private readonly Window _window;
    private TaskCompletionSource? _pendingLock;

    internal Keyboard(Window window) => _window = window;

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>Gets whether the keyboard is locked (https://wicg.github.io/keyboard-lock/#keyboard-enable-keyboard-lock): a lock is requested or active.</summary>
    internal bool IsLocked => IsEnabled || _pendingLock is not null;

    /// <summary>https://wicg.github.io/keyboard-lock/#keyboard-enable-keyboard-lock</summary>
    private bool IsEnabled { get; set; }

    /// <summary>
    /// Captures the keys that the system or the browser handle (https://wicg.github.io/keyboard-lock/#h-keyboard-lock), with
    /// <see cref="KeyboardLockOptions.Lock"/>. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>
    /// when the document is not the active document of a top-level browsing context, an <c>InvalidAccessError</c> when a
    /// key code is not valid, and an <c>AbortError</c> when another call replaces the request before it completes.
    /// </summary>
    /// <param name="keyCodes">The <c>code</c> values of the keys to capture, or an empty list to capture all the keys.</param>
    /// <seealso href="https://wicg.github.io/keyboard-lock/#dom-keyboard-lock"/>
    [IdlMember("lock", "https://wicg.github.io/keyboard-lock/#dom-keyboard-lock")]
    public Task Lock(IReadOnlyList<string>? keyCodes = null)
    {
        var codes = keyCodes?.ToArray() ?? [];
        if (!IsInActiveTopLevelBrowsingContext())
            return Task.FromException(DomException.InvalidState("The keyboard can only be locked by the active document of a top-level browsing context"));

        // A pending request is replaced by the new one
        _pendingLock?.TrySetException(new DomException("The keyboard lock request was replaced by another request", "AbortError"));
        var promise = new TaskCompletionSource();
        _pendingLock = promise;

        // The steps run in the keyboard lock task queue
        _window.QueueGlobalTask(() =>
        {
            if (!ReferenceEquals(_pendingLock, promise))
                return;

            _pendingLock = null;
            if (Array.Find(codes, code => !KeyCodes.Contains(code)) is { } invalid)
            {
                SetDisabled();
                promise.TrySetException(new DomException($"'{invalid}' is not a valid key code", "InvalidAccessError"));
                return;
            }

            if (!IsInActiveTopLevelBrowsingContext())
            {
                promise.TrySetException(DomException.InvalidState("The document is no longer active"));
                return;
            }

            IsEnabled = true;
            _window.Options.KeyboardLock?.Lock?.Invoke(CallbackContext.For(_window), codes);
            promise.TrySetResult();
        });
        return promise.Task;
    }

    /// <summary>Releases the keys captured by <see cref="Lock"/> (https://wicg.github.io/keyboard-lock/#h-keyboard-unlock), with <see cref="KeyboardLockOptions.Unlock"/>.</summary>
    /// <seealso href="https://wicg.github.io/keyboard-lock/#dom-keyboard-unlock"/>
    [IdlMember("unlock", "https://wicg.github.io/keyboard-lock/#dom-keyboard-unlock")]
    public void Unlock()
    {
        // A pending request is aborted (as Chromium)
        if (_pendingLock is { } pending)
        {
            _pendingLock = null;
            pending.TrySetException(new DomException("The keyboard lock request was aborted by unlock()", "AbortError"));
        }

        SetDisabled();
    }

    /// <summary>The document is unloaded: the user agent implicitly calls <see cref="Unlock"/>.</summary>
    internal void OnDocumentUnloaded() => Unlock();

    private void SetDisabled()
    {
        if (!IsEnabled)
            return;

        IsEnabled = false;
        _window.Options.KeyboardLock?.Unlock?.Invoke(CallbackContext.For(_window));
    }

    /// <summary>Whether the script runs in the active document of a top-level browsing context.</summary>
    private bool IsInActiveTopLevelBrowsingContext()
    {
        var document = _window.Document;
        return document.IsFullyActive && document.BrowsingContext is { Parent: null } browsingContext && ReferenceEquals(browsingContext.Document, document);
    }

    private static FrozenSet<string> CreateKeyCodes()
    {
        List<string> codes =
        [
            // The alphanumeric section
            "Backquote", "Backslash", "BracketLeft", "BracketRight", "Comma", "Equal", "IntlBackslash", "IntlRo", "IntlYen",
            "Minus", "Period", "Quote", "Semicolon", "Slash",
            "AltLeft", "AltRight", "Backspace", "CapsLock", "ContextMenu", "ControlLeft", "ControlRight", "Enter", "MetaLeft",
            "MetaRight", "ShiftLeft", "ShiftRight", "Space", "Tab", "Convert", "KanaMode", "Lang1", "Lang2", "Lang3", "Lang4",
            "Lang5", "NonConvert",

            // The control pad and the arrow pad
            "Delete", "End", "Help", "Home", "Insert", "PageDown", "PageUp", "ArrowDown", "ArrowLeft", "ArrowRight", "ArrowUp",

            // The numpad
            "NumLock", "NumpadAdd", "NumpadBackspace", "NumpadClear", "NumpadClearEntry", "NumpadComma", "NumpadDecimal",
            "NumpadDivide", "NumpadEnter", "NumpadEqual", "NumpadHash", "NumpadMemoryAdd", "NumpadMemoryClear",
            "NumpadMemoryRecall", "NumpadMemoryStore", "NumpadMemorySubtract", "NumpadMultiply", "NumpadParenLeft",
            "NumpadParenRight", "NumpadStar", "NumpadSubtract",

            // The function section
            "Escape", "Fn", "FnLock", "PrintScreen", "ScrollLock", "Pause",

            // The media keys
            "BrowserBack", "BrowserFavorites", "BrowserForward", "BrowserHome", "BrowserRefresh", "BrowserSearch", "BrowserStop",
            "Eject", "LaunchApp1", "LaunchApp2", "LaunchMail", "MediaPlayPause", "MediaSelect", "MediaStop", "MediaTrackNext",
            "MediaTrackPrevious", "Power", "Sleep", "AudioVolumeDown", "AudioVolumeMute", "AudioVolumeUp", "WakeUp",

            // The legacy, non-standard and special keys
            "Hyper", "Super", "Turbo", "Abort", "Resume", "Suspend", "Again", "Copy", "Cut", "Find", "Open", "Paste", "Props",
            "Select", "Undo", "Hiragana", "Katakana", "Unidentified",
        ];

        for (var c = 'A'; c <= 'Z'; c++)
        {
            codes.Add("Key" + c);
        }

        for (var digit = 0; digit <= 9; digit++)
        {
            codes.Add("Digit" + digit.ToString(System.Globalization.CultureInfo.InvariantCulture));
            codes.Add("Numpad" + digit.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        for (var function = 1; function <= 24; function++)
        {
            codes.Add("F" + function.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        return codes.ToFrozenSet(StringComparer.Ordinal);
    }
}

// The Keyboard Lock API (https://wicg.github.io/keyboard-lock/)
public sealed partial class Navigator
{
    private Keyboard? _keyboard;

    /// <summary>Gets the keyboard of the window (https://wicg.github.io/keyboard-lock/#navigator-interface).</summary>
    /// <seealso href="https://wicg.github.io/keyboard-lock/#dom-navigator-keyboard"/>
    [IdlMember("keyboard", "https://wicg.github.io/keyboard-lock/#dom-navigator-keyboard")]
    public Keyboard Keyboard => _keyboard ??= new Keyboard(_window);

    internal Keyboard? KeyboardOrNull => _keyboard;
}
