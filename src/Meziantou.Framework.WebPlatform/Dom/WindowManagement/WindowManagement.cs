namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Window Management API (https://w3c.github.io/window-management/, <see cref="BrowsingContextOptions.Screens"/>).</summary>
public sealed class ScreenOptions
{
    /// <summary>
    /// Gets or sets the screens of the device: a <see cref="ScreenProvider"/> to which the application adds the
    /// <see cref="DeviceScreen"/> objects of its screens. A provider can be shared by several browsing contexts. The default
    /// is null: the device has one screen, the size of the viewport (<see cref="BrowsingContextOptions.Viewport"/>).
    /// </summary>
    public ScreenProvider? Provider { get; init; }
}

/// <summary>
/// The screens of the device (https://w3c.github.io/window-management/#concept-screen), used by the windows of the browsing
/// contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="ScreenOptions.Provider"/>): <c>window.screen</c>
/// is the current screen of a window (the screen with the largest intersection with its client window), and
/// <c>window.getScreenDetails()</c> lists them. Adding, removing and changing the screens fire the <c>screenschange</c>,
/// <c>currentscreenchange</c> and <c>change</c> events.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public sealed class ScreenProvider
{
    private readonly Lock _lock = new();
    private readonly List<DeviceScreen> _screens = [];
    private readonly List<WeakReference<WindowScreens>> _windows = [];

    /// <summary>Gets the screens, in the order they were added.</summary>
    public IReadOnlyList<DeviceScreen> Screens
    {
        get
        {
            lock (_lock)
            {
                return [.. _screens];
            }
        }
    }

    /// <summary>Adds a screen (a screen is connected, or enabled). Can be called from any thread.</summary>
    /// <param name="screen">The screen. A screen can be added to one provider at a time.</param>
    /// <exception cref="InvalidOperationException">The screen is added to a provider.</exception>
    public void AddScreen(DeviceScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        lock (_lock)
        {
            if (screen.Provider is not null)
                throw new InvalidOperationException("The screen is already added to a provider");

            screen.Provider = this;
            _screens.Add(screen);
        }

        OnScreensChanged();
    }

    /// <summary>Removes a screen (a screen is disconnected, or disabled). Can be called from any thread.</summary>
    /// <param name="screen">The screen.</param>
    /// <returns>Whether the screen was added to this provider.</returns>
    public bool RemoveScreen(DeviceScreen screen)
    {
        ArgumentNullException.ThrowIfNull(screen);
        lock (_lock)
        {
            if (!_screens.Remove(screen))
                return false;

            screen.Provider = null;
        }

        OnScreensChanged();
        return true;
    }

    /// <summary>The screens, or the properties of a screen, changed: the windows compare them with the ones they observed.</summary>
    internal void OnScreensChanged()
    {
        WindowScreens[] windows;
        lock (_lock)
        {
            _windows.RemoveAll(static reference => !reference.TryGetTarget(out _));
            windows = [.. _windows.Select(static reference => reference.TryGetTarget(out var window) ? window : null).OfType<WindowScreens>()];
        }

        foreach (var window in windows)
        {
            window.QueueUpdate();
        }
    }

    internal void Register(WindowScreens window)
    {
        lock (_lock)
        {
            _windows.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _windows.Add(new WeakReference<WindowScreens>(window));
        }
    }
}

/// <summary>
/// A screen of the device (https://w3c.github.io/window-management/#concept-screen): its screen area and available screen
/// area in the virtual screen arrangement, in CSS pixels (the multi-screen origin is the top left corner of the arrangement,
/// the coordinates may be negative), and its properties. A change of a property, once the screen is added to a
/// <see cref="ScreenProvider"/>, fires the <c>change</c> events of the windows.
/// </summary>
/// <remarks>The members can be used from any thread.</remarks>
public sealed class DeviceScreen
{
    private readonly Lock _lock = new();
    private readonly Func<ScreenValues>? _dynamicValues;
    private int _left;
    private int _top;
    private int _width;
    private int _height;
    private int? _availLeft;
    private int? _availTop;
    private int? _availWidth;
    private int? _availHeight;
    private float _devicePixelRatio = 1;
    private uint _colorDepth = 24;
    private bool _isPrimary;
    private bool _isInternal;
    private string _label = "";

    /// <summary>Creates a screen.</summary>
    /// <param name="left">The x-coordinate of the screen area.</param>
    /// <param name="top">The y-coordinate of the screen area.</param>
    /// <param name="width">The width of the screen area.</param>
    /// <param name="height">The height of the screen area.</param>
    public DeviceScreen(int left, int top, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(height);
        _left = left;
        _top = top;
        _width = width;
        _height = height;
    }

    /// <summary>The screen of a window without provider: its values are computed from the viewport.</summary>
    internal DeviceScreen(Func<ScreenValues> values)
    {
        _dynamicValues = values;
    }

    /// <summary>Gets or sets the x-coordinate of the screen area (screen position).</summary>
    public int Left { get => Get(() => _left); set => Set(ref _left, value); }

    /// <summary>Gets or sets the y-coordinate of the screen area (screen position).</summary>
    public int Top { get => Get(() => _top); set => Set(ref _top, value); }

    /// <summary>Gets or sets the width of the screen area.</summary>
    public int Width { get => Get(() => _width); set => Set(ref _width, ValidateSize(value)); }

    /// <summary>Gets or sets the height of the screen area.</summary>
    public int Height { get => Get(() => _height); set => Set(ref _height, ValidateSize(value)); }

    /// <summary>Gets or sets the x-coordinate of the available screen area (the default is <see cref="Left"/>).</summary>
    public int AvailLeft { get => Get(() => _availLeft ?? _left); set => Set(ref _availLeft, value); }

    /// <summary>Gets or sets the y-coordinate of the available screen area (the default is <see cref="Top"/>).</summary>
    public int AvailTop { get => Get(() => _availTop ?? _top); set => Set(ref _availTop, value); }

    /// <summary>Gets or sets the width of the available screen area, without the taskbars and the menu bars of the system (the default is <see cref="Width"/>).</summary>
    public int AvailWidth { get => Get(() => _availWidth ?? _width); set => Set(ref _availWidth, ValidateSize(value)); }

    /// <summary>Gets or sets the height of the available screen area (the default is <see cref="Height"/>).</summary>
    public int AvailHeight { get => Get(() => _availHeight ?? _height); set => Set(ref _availHeight, ValidateSize(value)); }

    /// <summary>Gets or sets the device pixel ratio of the screen (the default is 1).</summary>
    public float DevicePixelRatio
    {
        get => Get(() => _devicePixelRatio);
        set => Set(ref _devicePixelRatio, value > 0 && float.IsFinite(value) ? value : throw new ArgumentOutOfRangeException(nameof(value)));
    }

    /// <summary>Gets or sets the color depth of the screen (the default is 24).</summary>
    public uint ColorDepth { get => Get(() => _colorDepth); set => Set(ref _colorDepth, value); }

    /// <summary>Gets or sets whether the screen is the primary screen. When no screen is primary, the first screen is.</summary>
    public bool IsPrimary { get => Get(() => _isPrimary); set => Set(ref _isPrimary, value); }

    /// <summary>Gets or sets whether the screen is built into the device (e.g. the screen of a laptop).</summary>
    public bool IsInternal { get => Get(() => _isInternal); set => Set(ref _isInternal, value); }

    /// <summary>Gets or sets the label of the screen, which describes it to the user (e.g. <c>Built-in display</c>).</summary>
    public string Label { get => Get(() => _label); set => Set(ref _label, value ?? throw new ArgumentNullException(nameof(value))); }

    internal ScreenProvider? Provider { get; set; }

    internal ScreenValues GetValues()
    {
        if (_dynamicValues is not null)
            return _dynamicValues();

        lock (_lock)
        {
            return new ScreenValues(_left, _top, _width, _height, _availLeft ?? _left, _availTop ?? _top, _availWidth ?? _width, _availHeight ?? _height, _devicePixelRatio, _colorDepth, _isPrimary, _isInternal, _label);
        }
    }

    private static int ValidateSize(int value) => value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(value));

    private T Get<T>(Func<T> getter)
    {
        lock (_lock)
        {
            return getter();
        }
    }

    private void Set<T>(ref T field, T value)
    {
        lock (_lock)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
                return;

            field = value;
        }

        Provider?.OnScreensChanged();
    }
}

/// <summary>The observable properties of a screen (https://w3c.github.io/window-management/#observable-screen-properties).</summary>
internal readonly record struct ScreenValues(int Left, int Top, int Width, int Height, int AvailLeft, int AvailTop, int AvailWidth, int AvailHeight, float DevicePixelRatio, uint ColorDepth, bool IsPrimary, bool IsInternal, string Label)
{
    /// <summary>The basic observable properties (https://w3c.github.io/window-management/#basic-observable-properties), which fire the change event of window.screen.</summary>
    public (int Width, int Height, int AvailWidth, int AvailHeight, uint ColorDepth) Basic => (Width, Height, AvailWidth, AvailHeight, ColorDepth);
}

/// <summary>
/// The screens of the device for a window (https://w3c.github.io/window-management/#screendetails), returned by
/// <see cref="Window.GetScreenDetails"/>: the same object for the window, whose <see cref="Screens"/> are sorted by position.
/// </summary>
/// <seealso href="https://w3c.github.io/window-management/#screendetails"/>
[IdlInterface("ScreenDetails", "https://w3c.github.io/window-management/#screendetails")]
public sealed class ScreenDetails : EventTarget
{
    private readonly WindowScreens _state;

    internal ScreenDetails(WindowScreens state)
    {
        _state = state;
    }

    internal override GlobalScope? RelevantGlobal => _state.Window;

    /// <summary>Gets the screens, sorted by the position of their screen area (left, then top). As in Chromium, it is empty once the window has no browsing context (e.g. the window of a removed iframe).</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetails-screens"/>
    [IdlMember("screens", "https://w3c.github.io/window-management/#dom-screendetails-screens")]
    public IReadOnlyList<ScreenDetailed> Screens => _state.GetScreens();

    /// <summary>Gets the current screen of the window: one of <see cref="Screens"/>. As in Chromium, it is null once the window has no browsing context.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetails-currentscreen"/>
    [IdlMember("currentScreen", "https://w3c.github.io/window-management/#dom-screendetails-currentscreen")]
    public ScreenDetailed? CurrentScreen => _state.Window.BrowsingContext is null ? null : _state.GetCurrentScreen();

    /// <summary>Gets or sets the event handler of the <c>screenschange</c> event: a screen was added or removed.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetails-onscreenschange"/>
    [IdlMember("onscreenschange", "https://w3c.github.io/window-management/#dom-screendetails-onscreenschange")]
    public Action<Event>? OnScreensChange
    {
        get => GetEventHandler("screenschange");
        set => SetEventHandler("screenschange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>currentscreenchange</c> event: the window moved to another screen, or a property of the current screen changed.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetails-oncurrentscreenchange"/>
    [IdlMember("oncurrentscreenchange", "https://w3c.github.io/window-management/#dom-screendetails-oncurrentscreenchange")]
    public Action<Event>? OnCurrentScreenChange
    {
        get => GetEventHandler("currentscreenchange");
        set => SetEventHandler("currentscreenchange", value);
    }
}

/// <summary>A screen of <see cref="ScreenDetails.Screens"/> (https://w3c.github.io/window-management/#screendetailed): the properties of a <see cref="DeviceScreen"/>.</summary>
/// <seealso href="https://w3c.github.io/window-management/#screendetailed"/>
[IdlInterface("ScreenDetailed", "https://w3c.github.io/window-management/#screendetailed")]
public sealed class ScreenDetailed : Screen
{
    internal ScreenDetailed(Window window, DeviceScreen screen)
        : base(window)
    {
        DeviceScreen = screen;
        LastValues = screen.GetValues();
    }

    /// <summary>Gets the screen of the device.</summary>
    [NotInIdl("The screen of the host, which is not exposed to scripts")]
    public DeviceScreen DeviceScreen { get; }

    /// <summary>The values of the last change event.</summary>
    internal ScreenValues LastValues { get; set; }

    /// <summary>Gets the x-coordinate of the available screen area.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-availleft"/>
    [IdlMember("availLeft", "https://w3c.github.io/window-management/#dom-screendetailed-availleft")]
    public int AvailLeft => Values.AvailLeft;

    /// <summary>Gets the y-coordinate of the available screen area.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-availtop"/>
    [IdlMember("availTop", "https://w3c.github.io/window-management/#dom-screendetailed-availtop")]
    public int AvailTop => Values.AvailTop;

    /// <summary>Gets the x-coordinate of the screen area.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-left"/>
    [IdlMember("left", "https://w3c.github.io/window-management/#dom-screendetailed-left")]
    public int Left => Values.Left;

    /// <summary>Gets the y-coordinate of the screen area.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-top"/>
    [IdlMember("top", "https://w3c.github.io/window-management/#dom-screendetailed-top")]
    public int Top => Values.Top;

    /// <summary>Gets whether the screen is the primary screen.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-isprimary"/>
    [IdlMember("isPrimary", "https://w3c.github.io/window-management/#dom-screendetailed-isprimary")]
    public bool IsPrimary => Values.IsPrimary;

    /// <summary>Gets whether the screen is built into the device.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-isinternal"/>
    [IdlMember("isInternal", "https://w3c.github.io/window-management/#dom-screendetailed-isinternal")]
    public bool IsInternal => Values.IsInternal;

    /// <summary>Gets the device pixel ratio of the screen.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-devicepixelratio"/>
    [IdlMember("devicePixelRatio", "https://w3c.github.io/window-management/#dom-screendetailed-devicepixelratio")]
    public float DevicePixelRatio => Values.DevicePixelRatio;

    /// <summary>Gets the label of the screen.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screendetailed-label"/>
    [IdlMember("label", "https://w3c.github.io/window-management/#dom-screendetailed-label")]
    public string Label => Values.Label;

    private protected override ScreenValues Values => DeviceScreen.GetValues();
}

/// <summary>
/// The screens of a window: its current screen, its <see cref="ScreenDetails"/> ([[screenDetails]]) and their
/// <see cref="ScreenDetailed"/> objects, and the values observed by the last events. The changes (of the screens, or of the
/// client window) queue a task that fires the events.
/// </summary>
internal sealed class WindowScreens
{
    private readonly Dictionary<DeviceScreen, ScreenDetailed> _detailedScreens = [];
    private int _updateQueued;
    private (int Width, int Height, int AvailWidth, int AvailHeight, uint ColorDepth) _lastBasicValues;
    private DeviceScreen? _lastCurrentScreen;
    private ScreenValues _lastCurrentValues;

    public WindowScreens(Window window)
    {
        Window = window;

        // The screen of a window without provider: the viewport, at the origin
        DefaultScreen = new DeviceScreen(GetDefaultValues);
        Provider?.Register(this);
        var current = GetCurrentDeviceScreen();
        _lastCurrentScreen = current;
        _lastCurrentValues = current.GetValues();
        _lastBasicValues = _lastCurrentValues.Basic;
    }

    /// <summary>The screen of a window without browsing context: its size is 0, its color depth 24.</summary>
    public static ScreenValues EmptyScreenValues { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 1, 24, IsPrimary: true, IsInternal: false, "");

    public Window Window { get; }

    public ScreenDetails? Details { get; private set; }

    private DeviceScreen DefaultScreen { get; }

    private ScreenProvider? Provider => Window.Options.Screens?.Provider;

    /// <summary>https://w3c.github.io/window-management/#dom-screen-isextended: the device has more than one screen.</summary>
    public bool IsExtended => PermissionsPolicyAlgorithms.IsAllowedToUse(Window.Document, PolicyControlledFeature.WindowManagement, report: false) && Provider is { Screens.Count: > 1 };

    public ScreenDetails GetOrCreateDetails()
    {
        if (Details is null)
        {
            Details = new ScreenDetails(this);
            foreach (var screen in GetDeviceScreens())
            {
                _detailedScreens[screen] = new ScreenDetailed(Window, screen);
            }
        }

        return Details;
    }

    /// <summary>The values of the current screen (the values of window.screen).</summary>
    public ScreenValues GetCurrentValues() => GetCurrentDeviceScreen().GetValues();

    /// <summary>https://w3c.github.io/window-management/#dom-screendetails-screens: sorted by the position of the screen area.</summary>
    public IReadOnlyList<ScreenDetailed> GetScreens() => Window.BrowsingContext is null ? [] : [.. GetDeviceScreens().Select(GetOrCreateDetailed)];

    public ScreenDetailed GetCurrentScreen() => GetOrCreateDetailed(GetCurrentDeviceScreen());

    /// <summary>The changes of the screens or of the client window are processed by a task. Called from any thread.</summary>
    public void QueueUpdate()
    {
        if (Interlocked.Exchange(ref _updateQueued, 1) == 0)
        {
            Window.QueueGlobalTask(Update);
        }
    }

    /// <summary>The screen of the device where a fullscreen request places the window (the target screen of the request).</summary>
    public static ScreenValues? GetTargetScreen(Document document, ScreenDetailed? screen)
    {
        // https://w3c.github.io/window-management/#api-permission-policy-integration: without the policy, the requests to place
        // content on specific screens are clamped to the current screen
        if (screen is null || !PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.WindowManagement, report: false))
            return null;

        var provider = document.BrowsingContext?.Options.Screens?.Provider;
        if (provider is not null && !provider.Screens.Contains(screen.DeviceScreen))
            return null;

        return screen.DeviceScreen.GetValues();
    }

    // The screens of the provider, or the default screen
    private List<DeviceScreen> GetDeviceScreens()
    {
        var screens = Provider?.Screens is { Count: > 0 } providerScreens ? providerScreens.ToList() : [DefaultScreen];
        var values = screens.ToDictionary(screen => screen, screen => screen.GetValues());

        // https://w3c.github.io/window-management/#screen-ordering: the left, then the top of the screen area
        return [.. screens.OrderBy(screen => values[screen].Left).ThenBy(screen => values[screen].Top)];
    }

    /// <summary>https://w3c.github.io/window-management/#current-screen: the screen with the largest intersection with the client window, or the primary screen.</summary>
    private DeviceScreen GetCurrentDeviceScreen()
    {
        var screens = GetDeviceScreens();
        if (screens.Count == 1)
            return screens[0];

        var left = Window.ScreenX;
        var top = Window.ScreenY;
        var right = (long)left + Window.OuterWidth;
        var bottom = (long)top + Window.OuterHeight;
        DeviceScreen? best = null;
        long bestArea = 0;
        foreach (var screen in screens)
        {
            var values = screen.GetValues();
            var width = Math.Min(right, (long)values.Left + values.Width) - Math.Max(left, values.Left);
            var height = Math.Min(bottom, (long)values.Top + values.Height) - Math.Max(top, values.Top);
            if (width > 0 && height > 0 && width * height > bestArea)
            {
                best = screen;
                bestArea = width * height;
            }
        }

        return best ?? screens.Find(screen => screen.IsPrimary) ?? screens[0];
    }

    private ScreenDetailed GetOrCreateDetailed(DeviceScreen screen)
    {
        if (!_detailedScreens.TryGetValue(screen, out var detailed))
        {
            detailed = new ScreenDetailed(Window, screen);
            _detailedScreens.Add(screen, detailed);
        }

        return detailed;
    }

    // The screen of a window without provider: the size of the viewport, which is also the available area
    private ScreenValues GetDefaultValues()
    {
        if (Window.BrowsingContext is null)
            return EmptyScreenValues;

        var environment = Css.Cascade.StyleEngine.CreateEnvironment(Window.Document);
        var width = (int)environment.DeviceWidth;
        var height = (int)environment.DeviceHeight;
        return new ScreenValues(0, 0, width, height, 0, 0, width, height, (float)environment.DevicePixelRatio, 24, IsPrimary: true, IsInternal: false, "");
    }

    // The events of https://w3c.github.io/window-management/#api-screen-onchange-attribute,
    // https://w3c.github.io/window-management/#api-screendetails-onscreenschange-attribute and
    // https://w3c.github.io/window-management/#api-screendetails-oncurrentscreenchange-attribute
    private void Update()
    {
        Interlocked.Exchange(ref _updateQueued, 0);
        if (Window.BrowsingContext is null)
            return;

        var screens = GetDeviceScreens();
        var current = GetCurrentDeviceScreen();
        var currentValues = current.GetValues();
        var changedScreens = new List<ScreenDetailed>();
        var screensChanged = false;
        if (Details is not null)
        {
            foreach (var removed in _detailedScreens.Keys.Where(screen => !screens.Contains(screen)).ToList())
            {
                _detailedScreens.Remove(removed);
                screensChanged = true;
            }

            foreach (var screen in screens)
            {
                if (!_detailedScreens.TryGetValue(screen, out var detailed))
                {
                    _detailedScreens.Add(screen, new ScreenDetailed(Window, screen));
                    screensChanged = true;
                    continue;
                }

                var values = screen.GetValues();
                if (values != detailed.LastValues)
                {
                    detailed.LastValues = values;
                    changedScreens.Add(detailed);
                }
            }
        }

        var currentChanged = current != _lastCurrentScreen || currentValues != _lastCurrentValues;
        _lastCurrentScreen = current;
        _lastCurrentValues = currentValues;
        var basicChanged = currentValues.Basic != _lastBasicValues;
        _lastBasicValues = currentValues.Basic;

        foreach (var detailed in changedScreens)
        {
            detailed.FireEvent(new Event("change"));
        }

        if (screensChanged)
        {
            Details!.FireEvent(new Event("screenschange"));
        }

        if (currentChanged)
        {
            Details?.FireEvent(new Event("currentscreenchange"));
        }

        if (basicChanged)
        {
            Window.ScreenOrNull?.FireEvent(new Event("change"));
        }
    }
}

// https://w3c.github.io/window-management/#api-extensions-to-screen
public partial class Screen
{
    /// <summary>Gets whether the device has more than one screen (false when the <c>window-management</c> permissions policy feature is not allowed).</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screen-isextended"/>
    [IdlMember("isExtended", "https://w3c.github.io/window-management/#dom-screen-isextended")]
    public bool IsExtended => _window.BrowsingContext is not null && _window.GetScreensState().IsExtended;

    /// <summary>Gets or sets the event handler of the <c>change</c> event: a basic observable property of the screen (its size, its available size or its color depth) changed.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-screen-onchange"/>
    [IdlMember("onchange", "https://w3c.github.io/window-management/#dom-screen-onchange")]
    public Action<Event>? OnChange
    {
        get => GetEventHandler("change");
        set => SetEventHandler("change", value);
    }
}

// https://w3c.github.io/window-management/#api-extensions-to-window
public partial class Window
{
    private WindowScreens? _screensState;

    internal WindowScreens? ScreensStateOrNull => _screensState;

    /// <summary>
    /// Returns the screens of the device (https://w3c.github.io/window-management/#dom-window-getscreendetails), always the
    /// same object for the window. The task fails with a <c>NotAllowedError</c> <see cref="DomException"/> when the
    /// <c>window-management</c> permissions policy feature is not allowed, or the permission is denied, and with an
    /// <c>InvalidStateError</c> when the window has no browsing context (as in Chromium).
    /// </summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-window-getscreendetails"/>
    [IdlMember("getScreenDetails", "https://w3c.github.io/window-management/#dom-window-getscreendetails")]
    public Task<ScreenDetails> GetScreenDetails()
    {
        if (BrowsingContext is null || !Document.IsFullyActive)
            return Task.FromException<ScreenDetails>(DomException.InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(Document, PolicyControlledFeature.WindowManagement))
            return Task.FromException<ScreenDetails>(DomException.NotAllowed("The document is not allowed to use the window-management feature"));

        var result = new TaskCompletionSource<ScreenDetails>();
        var permission = Permissions.RequestPermissionToUse(this, "window-management");
        QueueGlobalTask(() =>
        {
            if (permission == PermissionState.Denied)
            {
                result.TrySetException(DomException.NotAllowed("The window-management permission is denied"));
                return;
            }

            result.TrySetResult(GetScreensState().GetOrCreateDetails());
        });
        return result.Task;
    }

    /// <summary>
    /// Minimizes the window (https://w3c.github.io/window-management/#dom-window-minimize). Only an installed web application
    /// can change its display state: the task fails with an <c>InvalidStateError</c> <see cref="DomException"/> (as in
    /// Chromium), or a <c>NotAllowedError</c> when the <c>window-management</c> permissions policy feature is not allowed.
    /// </summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-window-minimize"/>
    [IdlMember("minimize", "https://w3c.github.io/window-management/#dom-window-minimize")]
    public Task Minimize() => RequestDisplayStateChange();

    /// <summary>Maximizes the window (https://w3c.github.io/window-management/#dom-window-maximize): see <see cref="Minimize"/>.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-window-maximize"/>
    [IdlMember("maximize", "https://w3c.github.io/window-management/#dom-window-maximize")]
    public Task Maximize() => RequestDisplayStateChange();

    /// <summary>Restores the window (https://w3c.github.io/window-management/#dom-window-restore): see <see cref="Minimize"/>.</summary>
    /// <seealso href="https://w3c.github.io/window-management/#dom-window-restore"/>
    [IdlMember("restore", "https://w3c.github.io/window-management/#dom-window-restore")]
    public Task Restore() => RequestDisplayStateChange();

    /// <summary>Sets whether the user can resize the window (https://w3c.github.io/window-management/#dom-window-setresizable): see <see cref="Minimize"/>.</summary>
    /// <param name="resizable">Whether the window is resizable.</param>
    /// <seealso href="https://w3c.github.io/window-management/#dom-window-setresizable"/>
    [IdlMember("setResizable", "https://w3c.github.io/window-management/#dom-window-setresizable")]
    public Task SetResizable(bool resizable)
    {
        // The resizable state is never applied: the documents are not installed web applications
        _ = resizable;
        return RequestDisplayStateChange();
    }

    /// <summary>The screens of the window, created by the first access to <c>window.screen</c> or <c>getScreenDetails()</c>.</summary>
    internal WindowScreens GetScreensState() => _screensState ??= new WindowScreens(this);

    // https://w3c.github.io/window-management/#request-a-window-display-state-change: the documents are not installed web
    // applications (Chromium rejects with an InvalidStateError, the specification with a NotAllowedError)
    private Task RequestDisplayStateChange()
    {
        if (BrowsingContext is null || !PermissionsPolicyAlgorithms.IsAllowedToUse(Document, PolicyControlledFeature.WindowManagement))
            return Task.FromException(DomException.NotAllowed("The document is not allowed to use the window-management feature"));

        return Task.FromException(DomException.InvalidState("The display state of a window can only be changed by an installed web application"));
    }
}
