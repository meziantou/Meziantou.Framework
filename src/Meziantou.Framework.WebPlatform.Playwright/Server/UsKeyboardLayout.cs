namespace Meziantou.Framework.WebPlatform.Playwright.Server;

/// <summary>The description of a key: the values of its keyboard events (the <c>KeyDescription</c> of the Playwright server).</summary>
internal sealed record KeyDescription(string Key, int KeyCode, int KeyCodeWithoutLocation, string Code, string Text, int Location)
{
    /// <summary>Gets the description of the key while Shift is pressed, or null when Shift does not change it.</summary>
    public KeyDescription? Shifted { get; init; }
}

/// <summary>
/// The keys of a US keyboard (<c>usKeyboardLayout.ts</c> of the Playwright server): the key names, the codes and the
/// characters the clients give to <c>keyboard.press()</c> map to the description of the key.
/// </summary>
internal static class UsKeyboardLayout
{
    private static readonly Lazy<Dictionary<string, KeyDescription>> Layout = new(Build);

    public static KeyDescription? Get(string key) => Layout.Value.GetValueOrDefault(key);

    public static bool Contains(string key) => Layout.Value.ContainsKey(key);

    private static Dictionary<string, KeyDescription> Build()
    {
        var aliases = new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["ShiftLeft"] = ["Shift"],
            ["ControlLeft"] = ["Control"],
            ["AltLeft"] = ["Alt"],
            ["MetaLeft"] = ["Meta"],
            ["Enter"] = ["\n", "\r"],
        };

        var result = new Dictionary<string, KeyDescription>(StringComparer.Ordinal);
        foreach (var definition in Definitions)
        {
            var description = new KeyDescription(definition.Key, definition.KeyCode, definition.KeyCodeWithoutLocation != 0 ? definition.KeyCodeWithoutLocation : definition.KeyCode, definition.Code, definition.Text ?? "", definition.Location);
            if (definition.Key.Length == 1)
            {
                description = description with { Text = definition.Key };
            }

            KeyDescription? shifted = null;
            if (definition.ShiftKey is not null)
            {
                shifted = description with { Key = definition.ShiftKey, Text = definition.ShiftKey };
                if (definition.ShiftKeyCode != 0)
                {
                    shifted = shifted with { KeyCode = definition.ShiftKeyCode, KeyCodeWithoutLocation = definition.ShiftKeyCode };
                }
            }

            result[definition.Code] = description with { Shifted = shifted };
            if (aliases.TryGetValue(definition.Code, out var names))
            {
                foreach (var alias in names)
                {
                    result[alias] = description;
                }
            }

            if (definition.Location != 0)
                continue;

            if (description.Key.Length == 1)
            {
                result[description.Key] = description;
            }

            if (shifted is not null)
            {
                result[shifted.Key] = shifted with { Shifted = null };
            }
        }

        return result;
    }

    private sealed record Definition(string Code, int KeyCode, string Key, string? ShiftKey, int ShiftKeyCode, string? Text, int Location, int KeyCodeWithoutLocation);

    private static readonly Definition[] Definitions =
    [
        // Functions row
        new("Escape", 27, "Escape", null, 0, null, 0, 0),
        new("F1", 112, "F1", null, 0, null, 0, 0),
        new("F2", 113, "F2", null, 0, null, 0, 0),
        new("F3", 114, "F3", null, 0, null, 0, 0),
        new("F4", 115, "F4", null, 0, null, 0, 0),
        new("F5", 116, "F5", null, 0, null, 0, 0),
        new("F6", 117, "F6", null, 0, null, 0, 0),
        new("F7", 118, "F7", null, 0, null, 0, 0),
        new("F8", 119, "F8", null, 0, null, 0, 0),
        new("F9", 120, "F9", null, 0, null, 0, 0),
        new("F10", 121, "F10", null, 0, null, 0, 0),
        new("F11", 122, "F11", null, 0, null, 0, 0),
        new("F12", 123, "F12", null, 0, null, 0, 0),
        // Numbers row
        new("Backquote", 192, "`", "~", 0, null, 0, 0),
        new("Digit1", 49, "1", "!", 0, null, 0, 0),
        new("Digit2", 50, "2", "@", 0, null, 0, 0),
        new("Digit3", 51, "3", "#", 0, null, 0, 0),
        new("Digit4", 52, "4", "$", 0, null, 0, 0),
        new("Digit5", 53, "5", "%", 0, null, 0, 0),
        new("Digit6", 54, "6", "^", 0, null, 0, 0),
        new("Digit7", 55, "7", "&", 0, null, 0, 0),
        new("Digit8", 56, "8", "*", 0, null, 0, 0),
        new("Digit9", 57, "9", "(", 0, null, 0, 0),
        new("Digit0", 48, "0", ")", 0, null, 0, 0),
        new("Minus", 189, "-", "_", 0, null, 0, 0),
        new("Equal", 187, "=", "+", 0, null, 0, 0),
        new("Backslash", 220, "\\", "|", 0, null, 0, 0),
        new("Backspace", 8, "Backspace", null, 0, null, 0, 0),
        // First row
        new("Tab", 9, "Tab", null, 0, null, 0, 0),
        new("KeyQ", 81, "q", "Q", 0, null, 0, 0),
        new("KeyW", 87, "w", "W", 0, null, 0, 0),
        new("KeyE", 69, "e", "E", 0, null, 0, 0),
        new("KeyR", 82, "r", "R", 0, null, 0, 0),
        new("KeyT", 84, "t", "T", 0, null, 0, 0),
        new("KeyY", 89, "y", "Y", 0, null, 0, 0),
        new("KeyU", 85, "u", "U", 0, null, 0, 0),
        new("KeyI", 73, "i", "I", 0, null, 0, 0),
        new("KeyO", 79, "o", "O", 0, null, 0, 0),
        new("KeyP", 80, "p", "P", 0, null, 0, 0),
        new("BracketLeft", 219, "[", "{", 0, null, 0, 0),
        new("BracketRight", 221, "]", "}", 0, null, 0, 0),
        // Second row
        new("CapsLock", 20, "CapsLock", null, 0, null, 0, 0),
        new("KeyA", 65, "a", "A", 0, null, 0, 0),
        new("KeyS", 83, "s", "S", 0, null, 0, 0),
        new("KeyD", 68, "d", "D", 0, null, 0, 0),
        new("KeyF", 70, "f", "F", 0, null, 0, 0),
        new("KeyG", 71, "g", "G", 0, null, 0, 0),
        new("KeyH", 72, "h", "H", 0, null, 0, 0),
        new("KeyJ", 74, "j", "J", 0, null, 0, 0),
        new("KeyK", 75, "k", "K", 0, null, 0, 0),
        new("KeyL", 76, "l", "L", 0, null, 0, 0),
        new("Semicolon", 186, ";", ":", 0, null, 0, 0),
        new("Quote", 222, "'", "\"", 0, null, 0, 0),
        new("Enter", 13, "Enter", null, 0, "\r", 0, 0),
        // Third row
        new("ShiftLeft", 160, "Shift", null, 0, null, 1, 16),
        new("KeyZ", 90, "z", "Z", 0, null, 0, 0),
        new("KeyX", 88, "x", "X", 0, null, 0, 0),
        new("KeyC", 67, "c", "C", 0, null, 0, 0),
        new("KeyV", 86, "v", "V", 0, null, 0, 0),
        new("KeyB", 66, "b", "B", 0, null, 0, 0),
        new("KeyN", 78, "n", "N", 0, null, 0, 0),
        new("KeyM", 77, "m", "M", 0, null, 0, 0),
        new("Comma", 188, ",", "<", 0, null, 0, 0),
        new("Period", 190, ".", ">", 0, null, 0, 0),
        new("Slash", 191, "/", "?", 0, null, 0, 0),
        new("ShiftRight", 161, "Shift", null, 0, null, 2, 16),
        // Last row
        new("ControlLeft", 162, "Control", null, 0, null, 1, 17),
        new("MetaLeft", 91, "Meta", null, 0, null, 1, 0),
        new("AltLeft", 164, "Alt", null, 0, null, 1, 18),
        new("Space", 32, " ", null, 0, null, 0, 0),
        new("AltRight", 165, "Alt", null, 0, null, 2, 18),
        new("AltGraph", 225, "AltGraph", null, 0, null, 0, 0),
        new("MetaRight", 92, "Meta", null, 0, null, 2, 0),
        new("ContextMenu", 93, "ContextMenu", null, 0, null, 0, 0),
        new("ControlRight", 163, "Control", null, 0, null, 2, 17),
        // Center block
        new("PrintScreen", 44, "PrintScreen", null, 0, null, 0, 0),
        new("ScrollLock", 145, "ScrollLock", null, 0, null, 0, 0),
        new("Pause", 19, "Pause", null, 0, null, 0, 0),
        new("PageUp", 33, "PageUp", null, 0, null, 0, 0),
        new("PageDown", 34, "PageDown", null, 0, null, 0, 0),
        new("Insert", 45, "Insert", null, 0, null, 0, 0),
        new("Delete", 46, "Delete", null, 0, null, 0, 0),
        new("Home", 36, "Home", null, 0, null, 0, 0),
        new("End", 35, "End", null, 0, null, 0, 0),
        new("ArrowLeft", 37, "ArrowLeft", null, 0, null, 0, 0),
        new("ArrowUp", 38, "ArrowUp", null, 0, null, 0, 0),
        new("ArrowRight", 39, "ArrowRight", null, 0, null, 0, 0),
        new("ArrowDown", 40, "ArrowDown", null, 0, null, 0, 0),
        // Media keys
        new("AudioVolumeMute", 173, "AudioVolumeMute", null, 0, null, 0, 0),
        new("AudioVolumeDown", 174, "AudioVolumeDown", null, 0, null, 0, 0),
        new("AudioVolumeUp", 175, "AudioVolumeUp", null, 0, null, 0, 0),
        new("MediaTrackNext", 176, "MediaTrackNext", null, 0, null, 0, 0),
        new("MediaTrackPrevious", 177, "MediaTrackPrevious", null, 0, null, 0, 0),
        new("MediaPlayPause", 179, "MediaPlayPause", null, 0, null, 0, 0),
        // Numpad
        new("NumLock", 144, "NumLock", null, 0, null, 0, 0),
        new("NumpadDivide", 111, "/", null, 0, null, 3, 0),
        new("NumpadMultiply", 106, "*", null, 0, null, 3, 0),
        new("NumpadSubtract", 109, "-", null, 0, null, 3, 0),
        new("Numpad7", 36, "Home", "7", 103, null, 3, 0),
        new("Numpad8", 38, "ArrowUp", "8", 104, null, 3, 0),
        new("Numpad9", 33, "PageUp", "9", 105, null, 3, 0),
        new("Numpad4", 37, "ArrowLeft", "4", 100, null, 3, 0),
        new("Numpad5", 12, "Clear", "5", 101, null, 3, 0),
        new("Numpad6", 39, "ArrowRight", "6", 102, null, 3, 0),
        new("NumpadAdd", 107, "+", null, 0, null, 3, 0),
        new("Numpad1", 35, "End", "1", 97, null, 3, 0),
        new("Numpad2", 40, "ArrowDown", "2", 98, null, 3, 0),
        new("Numpad3", 34, "PageDown", "3", 99, null, 3, 0),
        new("Numpad0", 45, "Insert", "0", 96, null, 3, 0),
        new("NumpadDecimal", 46, "Delete", ".", 110, null, 3, 0),
        new("NumpadEnter", 13, "Enter", null, 0, "\r", 3, 0),
    ];
}
