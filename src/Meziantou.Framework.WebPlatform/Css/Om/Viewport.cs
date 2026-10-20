using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The viewport of a window (https://drafts.csswg.org/css-viewport/#viewport-interface): its segments, the regions of a
/// viewport split by the display features of a foldable or dual-screen device. The output device has a single screen
/// without display features, so the viewport has a single segment.
/// </summary>
/// <seealso href="https://drafts.csswg.org/css-viewport/#viewport"/>
[IdlInterface("Viewport", "https://drafts.csswg.org/css-viewport/#viewport")]
public sealed class Viewport : IRealmBoundPlatformObject
{
    private readonly Window _window;

    internal Viewport(Window window)
    {
        _window = window;
    }

    Window? IRealmBoundPlatformObject.RealmWindow => _window;

    /// <summary>
    /// Gets the segments of the viewport (https://drafts.csswg.org/css-viewport/#dom-viewport-segments): null when the document
    /// is not fully active, else "if there is only a single viewport segment (typically the size of the Viewport) return an
    /// array with that single segment": the rectangle of the viewport (its origin, the size of <see cref="Window.InnerWidth"/>
    /// and <see cref="Window.InnerHeight"/>). The display features cannot be overridden (no WebDriver emulation).
    /// </summary>
    /// <seealso href="https://drafts.csswg.org/css-viewport/#dom-viewport-segments"/>
    [IdlMember("segments", "https://drafts.csswg.org/css-viewport/#dom-viewport-segments")]
    public IReadOnlyList<DomRect>? Segments
    {
        get
        {
            var document = _window.Document;
            if (!document.IsFullyActive)
                return null;

            var environment = StyleEngine.CreateEnvironment(document);
            return [new DomRect(0, 0, environment.Width, environment.Height)];
        }
    }
}

public partial class Window
{
    private Viewport? _viewport;

    /// <summary>Gets the viewport of the window (https://drafts.csswg.org/css-viewport/#dom-window-viewport).</summary>
    /// <seealso href="https://drafts.csswg.org/css-viewport/#dom-window-viewport"/>
    [IdlMember("viewport", "https://drafts.csswg.org/css-viewport/#dom-window-viewport")]
    public Viewport Viewport => _viewport ??= new Viewport(this);
}
