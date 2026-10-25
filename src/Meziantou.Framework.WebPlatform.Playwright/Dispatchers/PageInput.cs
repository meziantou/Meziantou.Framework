using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Playwright.Protocol;
using Meziantou.Framework.WebPlatform.Playwright.Server;

namespace Meziantou.Framework.WebPlatform.Playwright.Dispatchers;

/// <summary>
/// The keyboard of a page (the <c>Keyboard</c> of the Playwright server): the keys of the clients (key names, codes and
/// characters of a US keyboard, e.g. <c>Shift+A</c>) are dispatched with <see cref="UserInput.Keyboard"/>. It is used on the
/// thread of the browser context.
/// </summary>
internal sealed class PageKeyboard(PageDispatcher page)
{
    private static readonly string[] Modifiers = ["Alt", "Control", "Meta", "Shift"];

    private readonly HashSet<string> _pressedModifiers = new(StringComparer.Ordinal);
    private readonly HashSet<string> _pressedKeys = new(StringComparer.Ordinal);

    private KeyboardInput Input => page.BrowsingContext.Input.Keyboard;

    public IReadOnlyCollection<string> PressedModifiers => _pressedModifiers;

    public void Down(string key)
    {
        var description = GetDescription(key);
        _pressedKeys.Add(description.Code);
        if (Modifiers.Contains(description.Key))
        {
            _pressedModifiers.Add(description.Key);
        }

        Input.Down(ToKeyboardKey(description));
    }

    public void Up(string key)
    {
        var description = GetDescription(key);
        if (Modifiers.Contains(description.Key))
        {
            _pressedModifiers.Remove(description.Key);
        }

        _pressedKeys.Remove(description.Code);
        Input.Up(ToKeyboardKey(description));
    }

    public void InsertText(string text) => Input.InsertText(text);

    public async Task TypeAsync(string text, double delay, Progress progress)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            var character = rune.ToString();
            if (UsKeyboardLayout.Contains(character))
            {
                await PressAsync(character, delay, progress).ConfigureAwait(true);
            }
            else
            {
                if (delay > 0)
                {
                    await progress.WaitAsync(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(true);
                }

                InsertText(character);
            }
        }
    }

    public async Task PressAsync(string key, double delay, Progress progress)
    {
        var tokens = Split(key);
        key = tokens[^1];
        for (var i = 0; i < tokens.Count - 1; i++)
        {
            Down(tokens[i]);
        }

        Down(key);
        if (delay > 0)
        {
            await progress.WaitAsync(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(true);
        }

        Up(key);
        for (var i = tokens.Count - 2; i >= 0; i--)
        {
            Up(tokens[i]);
        }

        // "Shift++": the last token is "+"
        static List<string> Split(string keyString)
        {
            var keys = new List<string>();
            var building = "";
            foreach (var rune in keyString.EnumerateRunes())
            {
                var character = rune.ToString();
                if (character == "+" && building.Length > 0)
                {
                    keys.Add(building);
                    building = "";
                }
                else
                {
                    building += character;
                }
            }

            keys.Add(building);
            return keys;
        }
    }

    /// <summary>Presses the modifiers of an action (e.g. <c>click({ modifiers: ['Shift'] })</c>), and returns the modifiers to restore.</summary>
    public IReadOnlyList<string> EnsureModifiers(IReadOnlyList<string> modifiers)
    {
        var resolved = modifiers.Select(ResolveSmartModifier).ToList();
        foreach (var modifier in resolved)
        {
            if (!Modifiers.Contains(modifier))
                throw new PlaywrightError("Unknown modifier " + modifier);
        }

        var restore = _pressedModifiers.ToList();
        foreach (var key in Modifiers)
        {
            var needDown = resolved.Contains(key);
            var isDown = _pressedModifiers.Contains(key);
            if (needDown && !isDown)
            {
                Down(key);
            }
            else if (!needDown && isDown)
            {
                Up(key);
            }
        }

        return restore;
    }

    private KeyDescription GetDescription(string key)
    {
        key = ResolveSmartModifier(key);
        var description = UsKeyboardLayout.Get(key) ?? throw new PlaywrightError($"Unknown key: \"{key}\"") { Details = null };
        if (_pressedModifiers.Contains("Shift") && description.Shifted is { } shifted)
        {
            description = shifted;
        }

        // A key pressed with a modifier other than Shift types no text
        if (_pressedModifiers.Count > 1 || (!_pressedModifiers.Contains("Shift") && _pressedModifiers.Count == 1))
            return description with { Text = "" };

        return description;
    }

    private static string ResolveSmartModifier(string key) => key == "ControlOrMeta" ? (OperatingSystem.IsMacOS() ? "Meta" : "Control") : key;

    private static KeyboardKey ToKeyboardKey(KeyDescription description) => new(description.Key)
    {
        Code = description.Code,
        KeyCode = (uint)description.KeyCodeWithoutLocation,
        Location = (uint)description.Location,
        Text = description.Text,
    };
}

/// <summary>The mouse and the touch screen of a page (the <c>Mouse</c> and <c>Touchscreen</c> of the Playwright server).</summary>
internal sealed class PageMouse(PageDispatcher page)
{
    private double _x;
    private double _y;

    private UserInput Input => page.BrowsingContext.Input;

    public double X => _x;

    public double Y => _y;

    public void Move(double x, double y, int steps)
    {
        var fromX = _x;
        var fromY = _y;
        _x = x;
        _y = y;
        steps = Math.Max(1, steps);
        for (var i = 1; i <= steps; i++)
        {
            Input.Mouse.Move(fromX + ((x - fromX) * i / steps), fromY + ((y - fromY) * i / steps));
        }
    }

    public void Down(string button) => Input.Mouse.Down(GetButton(button));

    public void Up(string button) => Input.Mouse.Up(GetButton(button));

    public async Task ClickAsync(double x, double y, string button, int clickCount, double delay, Progress progress)
    {
        Move(x, y, steps: 1);
        for (var count = 1; count <= clickCount; count++)
        {
            Down(button);
            if (delay > 0)
            {
                await progress.WaitAsync(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(true);
            }

            Up(button);
            if (delay > 0 && count < clickCount)
            {
                await progress.WaitAsync(TimeSpan.FromMilliseconds(delay)).ConfigureAwait(true);
            }
        }
    }

    public void Wheel(double deltaX, double deltaY) => Input.Wheel(_x, _y, deltaX, deltaY);

    public void Tap(double x, double y)
    {
        var finger = Input.CreatePointer("touch");
        finger.Move(x, y);
        finger.Down();
        finger.Up();
    }

    private static int GetButton(string button) => button switch
    {
        "right" => 2,
        "middle" => 1,
        _ => 0,
    };
}
