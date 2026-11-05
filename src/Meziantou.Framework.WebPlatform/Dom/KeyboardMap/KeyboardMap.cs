using System.Collections;
using System.Collections.Frozen;
using System.Globalization;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The host hooks of the Keyboard Map API (https://wicg.github.io/keyboard-map/, <see cref="BrowsingContextOptions.KeyboardMap"/>).</summary>
public sealed class KeyboardMapOptions
{
    /// <summary>
    /// Gets or sets the source of the keyboard layout of the device (<c>navigator.keyboard.getLayoutMap()</c>), e.g. a
    /// <see cref="KeyboardLayoutProvider"/> whose <see cref="KeyboardLayoutProvider.Layout"/> is set by the application. A
    /// provider can be shared by several browsing contexts. The default is null: the layout is US QWERTY
    /// (<see cref="KeyboardLayoutProvider.UsQwerty"/>).
    /// </summary>
    public KeyboardLayoutProvider? Provider { get; init; }
}

/// <summary>
/// The source of the keyboard layout of the device (https://wicg.github.io/keyboard-map/), read by
/// <c>navigator.keyboard.getLayoutMap()</c> in the browsing contexts that share a <see cref="BrowsingContextOptions"/>
/// (<see cref="KeyboardMapOptions.Provider"/>). The default implementation returns <see cref="Layout"/>, set by the
/// application. Override <see cref="GetLayout"/> to read another source, and call <see cref="OnLayoutChanged"/> when the
/// layout changes, to fire the <c>layoutchange</c> event of the <c>Keyboard</c> objects of the documents.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class KeyboardLayoutProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<Keyboard>> _keyboards = [];
    private IReadOnlyDictionary<string, string> _layout = UsQwerty;

    /// <summary>
    /// Gets the US QWERTY layout: the key produced without modifier by each writing system key
    /// (https://w3c.github.io/uievents-code/#key-alphanumeric-writing-system) of a US keyboard, by <c>code</c> value.
    /// </summary>
    public static IReadOnlyDictionary<string, string> UsQwerty { get; } = CreateUsQwerty();

    /// <summary>Raised when the layout changes (<see cref="OnLayoutChanged"/>).</summary>
    public event EventHandler? LayoutChanged;

    /// <summary>
    /// Gets or sets the layout returned by the default implementation of <see cref="GetLayout"/>: the key value (e.g.
    /// <c>q</c>) produced without modifier by each key, by <c>code</c> value (e.g. <c>KeyQ</c>). Only the writing system keys
    /// are reported to the documents. The default is <see cref="UsQwerty"/>. Setting it fires the <c>layoutchange</c> event
    /// of the <c>Keyboard</c> objects of the documents.
    /// </summary>
    public IReadOnlyDictionary<string, string> Layout
    {
        get
        {
            lock (_lock)
            {
                return _layout;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            lock (_lock)
            {
                _layout = value;
            }

            OnLayoutChanged();
        }
    }

    /// <summary>Returns the current layout of the keyboard: the key value produced without modifier by each key, by <c>code</c> value.</summary>
    public virtual IReadOnlyDictionary<string, string> GetLayout() => Layout;

    /// <summary>
    /// Notifies the documents that the layout changed: their <c>Keyboard</c> objects fire the <c>layoutchange</c> event (a
    /// hidden document gets it when it becomes visible). It can be called from any thread.
    /// </summary>
    protected void OnLayoutChanged()
    {
        LayoutChanged?.Invoke(this, EventArgs.Empty);
        List<Keyboard> keyboards = [];
        lock (_lock)
        {
            foreach (var reference in _keyboards)
            {
                if (reference.TryGetTarget(out var keyboard))
                {
                    keyboards.Add(keyboard);
                }
            }
        }

        foreach (var keyboard in keyboards)
        {
            keyboard.OnLayoutChanged();
        }
    }

    /// <summary>Registers a <c>Keyboard</c>, to fire its <c>layoutchange</c> event.</summary>
    internal void Register(Keyboard keyboard)
    {
        lock (_lock)
        {
            _keyboards.RemoveAll(reference => !reference.TryGetTarget(out _));
            _keyboards.Add(new WeakReference<Keyboard>(keyboard));
        }
    }

    private static FrozenDictionary<string, string> CreateUsQwerty()
    {
        var layout = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Backquote"] = "`",
            ["Backslash"] = "\\",
            ["BracketLeft"] = "[",
            ["BracketRight"] = "]",
            ["Comma"] = ",",
            ["Equal"] = "=",
            ["Minus"] = "-",
            ["Period"] = ".",
            ["Quote"] = "'",
            ["Semicolon"] = ";",
            ["Slash"] = "/",
        };

        for (var digit = 0; digit <= 9; digit++)
        {
            var value = digit.ToString(CultureInfo.InvariantCulture);
            layout["Digit" + value] = value;
        }

        for (var c = 'a'; c <= 'z'; c++)
        {
            layout["Key" + char.ToUpperInvariant(c)] = c.ToString();
        }

        return layout.ToFrozenDictionary(StringComparer.Ordinal);
    }
}

/// <summary>
/// The keyboard layout of the device (https://wicg.github.io/keyboard-map/#keyboardlayoutmap-interface), returned by
/// <c>navigator.keyboard.getLayoutMap()</c>: the key value produced without modifier by each writing system key, by
/// <c>code</c> value (e.g. <c>KeyQ</c> is <c>a</c> on an AZERTY keyboard).
/// </summary>
/// <seealso href="https://wicg.github.io/keyboard-map/#keyboardlayoutmap"/>
[IdlInterface("KeyboardLayoutMap", "https://wicg.github.io/keyboard-map/#keyboardlayoutmap")]
public sealed class KeyboardLayoutMap : IReadOnlyCollection<KeyValuePair<string, string>>
{
    /// <summary>The writing system keys (https://wicg.github.io/keyboard-map/#writing-system-keys), in the order of the specification.</summary>
    private static readonly string[] WritingSystemKeys = CreateWritingSystemKeys();

    private readonly List<KeyValuePair<string, string>> _entries;
    private readonly Dictionary<string, string> _values;

    internal KeyboardLayoutMap(IReadOnlyDictionary<string, string> layout)
    {
        _entries = [];
        _values = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var code in WritingSystemKeys)
        {
            // The keys that are not valid in the layout are skipped, and so are the keys that produce no character
            if (layout.TryGetValue(code, out var key) && !string.IsNullOrEmpty(key))
            {
                _entries.Add(KeyValuePair.Create(code, key));
                _values[code] = key;
            }
        }
    }

    /// <summary>Gets the number of keys of the layout.</summary>
    [NotInIdl("The size member of maplike<DOMString, DOMString>")]
    public int Count => _entries.Count;

    /// <summary>Returns the key value produced by a key, or null when the key is not in the layout.</summary>
    /// <param name="code">The <c>code</c> value of the key, such as <c>KeyQ</c>.</param>
    [NotInIdl("The get() member of maplike<DOMString, DOMString>")]
    public string? Get(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _values.GetValueOrDefault(code);
    }

    /// <summary>Returns whether a key is in the layout.</summary>
    /// <param name="code">The <c>code</c> value of the key, such as <c>KeyQ</c>.</param>
    [NotInIdl("The has() member of maplike<DOMString, DOMString>")]
    public bool Has(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        return _values.ContainsKey(code);
    }

    /// <summary>Enumerates the keys and their values, in the order of the writing system keys of the specification.</summary>
    [NotInIdl("The entries of maplike<DOMString, DOMString>")]
    public IEnumerator<KeyValuePair<string, string>> GetEnumerator() => _entries.GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    private static string[] CreateWritingSystemKeys()
    {
        List<string> codes = ["Backquote", "Backslash", "BracketLeft", "BracketRight", "Comma"];
        for (var digit = 0; digit <= 9; digit++)
        {
            codes.Add("Digit" + digit.ToString(CultureInfo.InvariantCulture));
        }

        codes.AddRange(["Equal", "IntlBackslash", "IntlRo", "IntlYen"]);
        for (var c = 'A'; c <= 'Z'; c++)
        {
            codes.Add("Key" + c);
        }

        codes.AddRange(["Minus", "Period", "Quote", "Semicolon", "Slash"]);
        return [.. codes];
    }
}

// The Keyboard Map API (https://wicg.github.io/keyboard-map/)
public sealed partial class Keyboard
{
    private TaskCompletionSource<KeyboardLayoutMap>? _pendingLayoutMap;
    private bool _layoutChangedWhileHidden;
    private bool _isRegistered;

    /// <summary>
    /// Returns the keyboard layout of the device (https://wicg.github.io/keyboard-map/#h-keyboard-getlayoutmap), read from
    /// <see cref="KeyboardMapOptions.Provider"/> (US QWERTY by default). The calls made before the task completes return the
    /// same task (as in Chromium). The task fails with a <c>SecurityError</c> <see cref="DomException"/> when the document is
    /// not allowed to use the <c>keyboard-map</c> feature, and an <c>InvalidStateError</c> when it is not fully active.
    /// </summary>
    /// <seealso href="https://wicg.github.io/keyboard-map/#dom-keyboard-getlayoutmap"/>
    [IdlMember("getLayoutMap", "https://wicg.github.io/keyboard-map/#dom-keyboard-getlayoutmap")]
    public Task<KeyboardLayoutMap> GetLayoutMap()
    {
        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException<KeyboardLayoutMap>(DomException.InvalidState("The document is not fully active"));

        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(document, PolicyControlledFeature.KeyboardMap))
            return Task.FromException<KeyboardLayoutMap>(new DomException("The document is not allowed to use the keyboard-map feature", "SecurityError"));

        if (_pendingLayoutMap is { } pending)
            return pending.Task;

        var promise = new TaskCompletionSource<KeyboardLayoutMap>();
        _pendingLayoutMap = promise;

        // The same promise object is returned while the layout is read
        Scripting.PromiseSlots.Register(promise.Task);
        RegisterWithProvider();
        _window.QueueGlobalTask(() =>
        {
            _pendingLayoutMap = null;
            IReadOnlyDictionary<string, string> layout;
            try
            {
                layout = _window.Options.KeyboardMap?.Provider?.GetLayout() ?? KeyboardLayoutProvider.UsQwerty;
            }
#pragma warning disable CA1031 // An error of the provider rejects the promise
            catch (Exception ex)
#pragma warning restore CA1031
            {
                promise.TrySetException(new DomException("The keyboard layout is not available: " + ex.Message, "InvalidStateError"));
                return;
            }

            promise.TrySetResult(new KeyboardLayoutMap(layout));
        });
        return promise.Task;
    }

    /// <summary>Gets or sets the event handler of the <c>layoutchange</c> event, fired when the keyboard layout changes.</summary>
    /// <seealso href="https://wicg.github.io/keyboard-map/#dom-keyboard-onlayoutchange"/>
    [IdlMember("onlayoutchange", "https://wicg.github.io/keyboard-map/#dom-keyboard-onlayoutchange")]
    public Action<Event>? OnLayoutChange
    {
        get => GetEventHandler("layoutchange");
        set
        {
            SetEventHandler("layoutchange", value);
            RegisterWithProvider();
        }
    }

    /// <summary>The keyboard is notified of the layout changes once a page uses the Keyboard Map API.</summary>
    internal void RegisterWithProvider()
    {
        if (_isRegistered || _window.Options.KeyboardMap?.Provider is not { } provider)
            return;

        _isRegistered = true;
        provider.Register(this);
    }

    /// <summary>
    /// The layout of the provider changed (https://wicg.github.io/keyboard-map/#layoutchange-event): the <c>layoutchange</c>
    /// event is fired by a task, unless the document is hidden (it is then fired when the document becomes visible). Called
    /// from any thread.
    /// </summary>
    internal void OnLayoutChanged()
    {
        _window.QueueGlobalTask(() =>
        {
            if (_window.Document.Hidden)
            {
                _layoutChangedWhileHidden = true;
                return;
            }

            FireEvent(new Event("layoutchange"));
        });
    }

    /// <summary>The document became visible: the <c>layoutchange</c> event of a change made while it was hidden is fired.</summary>
    internal void OnDocumentVisible()
    {
        if (!_layoutChangedWhileHidden)
            return;

        _layoutChangedWhileHidden = false;
        FireEvent(new Event("layoutchange"));
    }
}
