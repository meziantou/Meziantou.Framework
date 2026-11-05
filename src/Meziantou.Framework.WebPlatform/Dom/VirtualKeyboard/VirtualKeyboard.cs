using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The rectangle of the on-screen keyboard (https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-boundingrect), in CSS
/// pixels relative to the viewport of the top-level document. An empty rectangle means that the keyboard is hidden.
/// </summary>
/// <param name="X">The distance from the left edge of the viewport.</param>
/// <param name="Y">The distance from the top edge of the viewport.</param>
/// <param name="Width">The width of the keyboard.</param>
/// <param name="Height">The height of the keyboard.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct VirtualKeyboardRect(double X, double Y, double Width, double Height)
{
    /// <summary>Gets whether the rectangle is empty (the keyboard is hidden).</summary>
    public bool IsEmpty => Width <= 0 || Height <= 0;
}

/// <summary>The host hooks of the VirtualKeyboard API (https://w3c.github.io/virtual-keyboard/, <see cref="BrowsingContextOptions.VirtualKeyboard"/>).</summary>
public sealed class VirtualKeyboardOptions
{
    /// <summary>
    /// Gets or sets the callback that asks the system to show the on-screen keyboard (<c>navigator.virtualKeyboard.show()</c>).
    /// The library checks the document (sticky activation, a focused form control or editing host whose
    /// <c>virtualkeyboardpolicy</c> is <c>manual</c> and whose <c>inputmode</c> is not <c>none</c>) before calling it. Once the
    /// keyboard is shown, the host reports its rectangle with <see cref="VirtualKeyboardProvider.BoundingRect"/>. The callback is
    /// invoked on the thread of the event loop, and must not block it. The default is null: nothing is shown.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext>? Show { get; init; }

    /// <summary>
    /// Gets or sets the callback that asks the system to hide the on-screen keyboard (<c>navigator.virtualKeyboard.hide()</c>).
    /// The library checks the document (sticky activation, a focused element whose <c>virtualkeyboardpolicy</c> is
    /// <c>manual</c>) before calling it. The callback is invoked on the thread of the event loop, and must not block it. The
    /// default is null: nothing is hidden.
    /// The first argument is the context of the window (see <see cref="CallbackContext"/>): its <see cref="CallbackContext.Window"/> is never null.
    /// </summary>
    public Action<CallbackContext>? Hide { get; init; }

    /// <summary>
    /// Gets or sets the on-screen keyboard of the device: its <see cref="VirtualKeyboardProvider.BoundingRect"/> is the
    /// <c>boundingRect</c> of the documents, and the <c>keyboard-inset-*</c> environment variables while
    /// <c>overlaysContent</c> is true. A provider can be shared by several browsing contexts. The default is null: the keyboard
    /// is never shown.
    /// </summary>
    public VirtualKeyboardProvider? Provider { get; init; }
}

/// <summary>
/// The on-screen keyboard of the device (https://w3c.github.io/virtual-keyboard/), read by <c>navigator.virtualKeyboard</c>,
/// of the browsing contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="VirtualKeyboardOptions.Provider"/>).
/// Set <see cref="BoundingRect"/> when the keyboard is shown, moved or hidden: the documents update their
/// <c>boundingRect</c> and fire their <c>geometrychange</c> events.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class VirtualKeyboardProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<VirtualKeyboard>> _keyboards = [];
    private VirtualKeyboardRect _boundingRect;

    /// <summary>Raised when the geometry of the keyboard changes (<see cref="OnGeometryChanged"/>).</summary>
    public event EventHandler? GeometryChanged;

    /// <summary>
    /// Gets or sets the rectangle of the keyboard, relative to the viewport of the top-level documents (empty when the
    /// keyboard is hidden, the default). Setting it updates the <c>VirtualKeyboard</c> objects of the documents, which fire
    /// their <c>geometrychange</c> events.
    /// </summary>
    public VirtualKeyboardRect BoundingRect
    {
        get
        {
            lock (_lock)
            {
                return _boundingRect;
            }
        }
        set
        {
            lock (_lock)
            {
                if (_boundingRect == value)
                    return;

                _boundingRect = value;
            }

            OnGeometryChanged();
        }
    }

    /// <summary>Returns the rectangle of the keyboard. The default implementation returns <see cref="BoundingRect"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual VirtualKeyboardRect GetBoundingRect() => BoundingRect;

    /// <summary>
    /// Raises <see cref="GeometryChanged"/>, then reads the rectangle again for the <c>VirtualKeyboard</c> objects of the
    /// documents, which fire their <c>geometrychange</c> events. It can be called from any thread.
    /// </summary>
    protected void OnGeometryChanged()
    {
        GeometryChanged?.Invoke(this, EventArgs.Empty);
        List<VirtualKeyboard> keyboards = [];
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
            keyboard.OnGeometryChanged();
        }
    }

    /// <summary>Registers a <c>VirtualKeyboard</c>, to update it.</summary>
    internal void Register(VirtualKeyboard keyboard)
    {
        lock (_lock)
        {
            _keyboards.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _keyboards.Add(new WeakReference<VirtualKeyboard>(keyboard));
        }
    }
}

/// <summary>
/// The on-screen keyboard of a window (https://w3c.github.io/virtual-keyboard/#the-virtualkeyboard-interface,
/// <c>navigator.virtualKeyboard</c>): it is shown and hidden with <see cref="VirtualKeyboardOptions"/>, and its geometry is
/// read from the <see cref="VirtualKeyboardProvider"/> of the browsing context. Only the top-level documents get the geometry
/// of the keyboard; the rectangle of the documents of the nested browsing contexts stays empty.
/// </summary>
/// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard"/>
[IdlInterface("VirtualKeyboard", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard")]
public sealed class VirtualKeyboard : EventTarget
{
    private readonly Window _window;

    internal VirtualKeyboard(Window window)
    {
        _window = window;
        if (window.Options.VirtualKeyboard?.Provider is { } provider)
        {
            BoundingRectValue = GetIntersection(provider.GetBoundingRect());
            provider.Register(this);
        }
    }

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>The intersection of the keyboard with the viewport of the document, in the coordinates of the viewport.</summary>
    internal VirtualKeyboardRect BoundingRectValue { get; private set; }

    /// <summary>Gets the intersection of the keyboard with the viewport of the document, in client coordinates.</summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-boundingrect"/>
    [IdlMember("boundingRect", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-boundingrect")]
    public DomRect BoundingRect => new(BoundingRectValue.X, BoundingRectValue.Y, BoundingRectValue.Width, BoundingRectValue.Height);

    /// <summary>
    /// Gets or sets whether the keyboard overlays the content: the viewport is not resized when the keyboard is shown, and the
    /// <c>keyboard-inset-*</c> environment variables give its geometry. It can only be set in a top-level browsing context.
    /// </summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-overlayscontent"/>
    [IdlMember("overlaysContent", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-overlayscontent")]
    public bool OverlaysContent
    {
        get;
        set
        {
            // https://w3c.github.io/virtual-keyboard/#boundingrect-overlayscontent-and-ongeometrychange-attributes: only in a
            // top-level browsing context
            if (field == value || _window.BrowsingContext is not { Parent: null })
                return;

            field = value;
            InvalidateEnvironment();
        }
    }

    /// <summary>Gets or sets the event handler of the <c>geometrychange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-ongeometrychange"/>
    [IdlMember("ongeometrychange", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-ongeometrychange")]
    public Action<Event>? OnGeometryChange
    {
        get => GetEventHandler("geometrychange");
        set => SetEventHandler("geometrychange", value);
    }

    /// <summary>Shows the on-screen keyboard (https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-show), with <see cref="VirtualKeyboardOptions.Show"/>.</summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-show"/>
    [IdlMember("show", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-show")]
    public void Show()
    {
        if (!_window.HasStickyActivation || _window.Document.FocusedArea is not { } focused)
            return;

        if (focused is not (HtmlInputElement or HtmlTextAreaElement) && !Editing.IsEditingHost(focused))
            return;

        if (!IsManualPolicy(focused))
            return;

        _window.Options.VirtualKeyboard?.Show?.Invoke(CallbackContext.For(_window));
    }

    /// <summary>Hides the on-screen keyboard (https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-hide), with <see cref="VirtualKeyboardOptions.Hide"/>.</summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-hide"/>
    [IdlMember("hide", "https://w3c.github.io/virtual-keyboard/#dom-virtualkeyboard-hide")]
    public void Hide()
    {
        if (!_window.HasStickyActivation || _window.Document.FocusedArea is not { } focused || !IsManualPolicy(focused))
            return;

        _window.Options.VirtualKeyboard?.Hide?.Invoke(CallbackContext.For(_window));
    }

    /// <summary>
    /// The geometry of the keyboard changed: the bounding rect is set by a task
    /// (https://w3c.github.io/virtual-keyboard/#dfn-set-the-virtual-keyboard-bounding-rect), then the <c>geometrychange</c>
    /// event is fired. Called from any thread.
    /// </summary>
    internal void OnGeometryChanged()
    {
        _window.QueueGlobalTask(() =>
        {
            if (_window.Options.VirtualKeyboard?.Provider is not { } provider)
                return;

            VirtualKeyboardRect rect;
            try
            {
                rect = GetIntersection(provider.GetBoundingRect());
            }
#pragma warning disable CA1031 // An error of the provider keeps the last geometry
            catch (Exception)
#pragma warning restore CA1031
            {
                return;
            }

            if (rect == BoundingRectValue)
                return;

            BoundingRectValue = rect;
            if (OverlaysContent)
            {
                InvalidateEnvironment();
            }

            FireEvent(new Event("geometrychange"));
        });
    }

    /// <summary>The intersection of the keyboard with the viewport of a top-level document (empty for the other documents).</summary>
    private VirtualKeyboardRect GetIntersection(VirtualKeyboardRect keyboard)
    {
        var document = _window.Document;
        if (keyboard.IsEmpty || document.BrowsingContext is not { Parent: null })
            return default;

        var viewport = Css.Cascade.StyleEngine.CreateEnvironment(document);
        var left = Math.Max(0, keyboard.X);
        var top = Math.Max(0, keyboard.Y);
        var right = Math.Min(viewport.Width, keyboard.X + keyboard.Width);
        var bottom = Math.Min(viewport.Height, keyboard.Y + keyboard.Height);
        return right <= left || bottom <= top ? default : new VirtualKeyboardRect(left, top, right - left, bottom - top);
    }

    /// <summary>The <c>keyboard-inset-*</c> environment variables of the document changed: its styles are computed again.</summary>
    private void InvalidateEnvironment()
    {
        if (_window.BrowsingContext is not { } browsingContext || browsingContext.IsDestroyed)
            return;

        _window.Document.InvalidateLayout();
        browsingContext.EventLoop.RequestRenderingOpportunity(_window.Document);
    }

    /// <summary>Whether the <c>virtualkeyboardpolicy</c> of the element is <c>manual</c>, and its <c>inputmode</c> is not <c>none</c>.</summary>
    private static bool IsManualPolicy(Element element)
        => element is HtmlElement htmlElement && htmlElement.VirtualKeyboardPolicy == "manual" && !InfraAscii.EqualsIgnoreAsciiCase(htmlElement.GetAttributeValue("inputmode"), "none");

    /// <summary>
    /// The values of the <c>keyboard-inset-*</c> environment variables of a document (https://w3c.github.io/virtual-keyboard/#keyboard-inset-variables):
    /// the geometry of the keyboard of a top-level document whose keyboard overlays the content, or null (the insets are 0).
    /// </summary>
    internal static VirtualKeyboardRect? GetKeyboardInsets(Document document)
    {
        if (document.BrowsingContext is not { Parent: null } || document.RelevantWindow is not { NavigatorOrNull.VirtualKeyboardOrNull: { OverlaysContent: true } keyboard } window || !ReferenceEquals(window.Document, document))
            return null;

        return keyboard.BoundingRectValue.IsEmpty ? null : keyboard.BoundingRectValue;
    }
}

// The VirtualKeyboard API (https://w3c.github.io/virtual-keyboard/#navigator-extension)
public sealed partial class Navigator
{
    private VirtualKeyboard? _virtualKeyboard;

    /// <summary>Gets the on-screen keyboard of the window (https://w3c.github.io/virtual-keyboard/#navigator-extension).</summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-navigator-virtualkeyboard"/>
    [IdlMember("virtualKeyboard", "https://w3c.github.io/virtual-keyboard/#dom-navigator-virtualkeyboard")]
    public VirtualKeyboard VirtualKeyboard => _virtualKeyboard ??= new VirtualKeyboard(_window);

    internal VirtualKeyboard? VirtualKeyboardOrNull => _virtualKeyboard;
}

// The virtualKeyboardPolicy attribute (https://w3c.github.io/virtual-keyboard/#the-virtualkeyboardpolicy-attribute)
public partial class HtmlElement
{
    /// <summary>
    /// Gets or sets the <c>virtualkeyboardpolicy</c> content attribute (https://w3c.github.io/virtual-keyboard/#the-virtualkeyboardpolicy-attribute):
    /// <c>auto</c>, <c>manual</c>, or the empty string when it is missing or invalid.
    /// </summary>
    /// <seealso href="https://w3c.github.io/virtual-keyboard/#dom-elementcontenteditable-virtualkeyboardpolicy"/>
    [IdlMember("virtualKeyboardPolicy", "https://w3c.github.io/virtual-keyboard/#dom-elementcontenteditable-virtualkeyboardpolicy")]
    public string VirtualKeyboardPolicy
    {
        get => GetAttributeValue("virtualkeyboardpolicy") switch
        {
            { } value when InfraAscii.EqualsIgnoreAsciiCase(value, "auto") => "auto",
            { } value when InfraAscii.EqualsIgnoreAsciiCase(value, "manual") => "manual",
            _ => "",
        };
        set => SetAttribute("virtualkeyboardpolicy", value);
    }
}
