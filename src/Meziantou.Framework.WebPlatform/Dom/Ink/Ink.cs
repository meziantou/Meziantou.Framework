using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The parameter of <see cref="Ink.RequestPresenter"/> (https://wicg.github.io/ink-enhancement/#dictdef-inkpresenterparam).
/// </summary>
/// <seealso href="https://wicg.github.io/ink-enhancement/#dictdef-inkpresenterparam"/>
[IdlInterface("InkPresenterParam", "https://wicg.github.io/ink-enhancement/#dictdef-inkpresenterparam")]
public sealed class InkPresenterParam
{
    /// <summary>
    /// Gets or sets the element the ink trails are clipped to, or null for the viewport of the document. It must be an element
    /// of the document of the window.
    /// </summary>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-inkpresenterparam-presentationarea"/>
    [IdlMember("presentationArea", "https://wicg.github.io/ink-enhancement/#dom-inkpresenterparam-presentationarea")]
    public Element? PresentationArea { get; init; }
}

/// <summary>
/// The style of a delegated ink trail (https://wicg.github.io/ink-enhancement/#dictdef-inktrailstyle): its color and the
/// diameter of its tip.
/// </summary>
/// <seealso href="https://wicg.github.io/ink-enhancement/#dictdef-inktrailstyle"/>
[IdlInterface("InkTrailStyle", "https://wicg.github.io/ink-enhancement/#dictdef-inktrailstyle")]
public sealed class InkTrailStyle
{
    /// <summary>Gets the color of the trail: a CSS &lt;color&gt;.</summary>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-inktrailstyle-color"/>
    [IdlMember("color", "https://wicg.github.io/ink-enhancement/#dom-inktrailstyle-color")]
    public required string Color { get; init; }

    /// <summary>Gets the diameter of the tip of the trail, in CSS pixels: it must be greater than 0.</summary>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-inktrailstyle-diameter"/>
    [IdlMember("diameter", "https://wicg.github.io/ink-enhancement/#dom-inktrailstyle-diameter")]
    public required double Diameter { get; init; }
}

/// <summary>
/// The Ink API of a window (https://wicg.github.io/ink-enhancement/#ink, <c>navigator.ink</c>): it creates the presenters
/// that delegate the drawing of the ink trails of the pointer events to the compositor.
/// </summary>
/// <seealso href="https://wicg.github.io/ink-enhancement/#ink"/>
[IdlInterface("Ink", "https://wicg.github.io/ink-enhancement/#ink")]
public sealed class Ink
{
    private readonly Window _window;

    internal Ink(Window window) => _window = window;

    /// <summary>
    /// Creates a new presenter of delegated ink trails (https://wicg.github.io/ink-enhancement/#dom-ink-requestpresenter).
    /// The task fails with a <c>NotAllowedError</c> <see cref="DomException"/> when the presentation area is not an element
    /// connected to the document of the window, as in Chromium.
    /// </summary>
    /// <param name="param">The presentation area of the presenter, or null for the viewport.</param>
    /// <returns>The new presenter.</returns>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-ink-requestpresenter"/>
    [IdlMember("requestPresenter", "https://wicg.github.io/ink-enhancement/#dom-ink-requestpresenter")]
    public Task<DelegatedInkTrailPresenter> RequestPresenter(InkPresenterParam? param = null)
    {
        // "presentationArea must either be null or be in the same document as the Ink interface, otherwise throw an error and abort"
        if (param?.PresentationArea is { } area && (!area.IsConnected || !ReferenceEquals(area.NodeDocument, _window.Document)))
            return Task.FromException<DelegatedInkTrailPresenter>(DomException.NotAllowed("The presentation area is not in the document of the window"));

        return Task.FromResult(new DelegatedInkTrailPresenter(_window, param?.PresentationArea));
    }
}

/// <summary>
/// A presenter of delegated ink trails (https://wicg.github.io/ink-enhancement/#delegatedinktrailpresenter), created by
/// <see cref="Ink.RequestPresenter"/>: the page gives it the last rendered point of its ink trail, from which the
/// compositor draws the trail up to the latest position of the pointer until the next animation frame. The engine
/// validates the requests and records the start point; it does not draw the predicted trail (the next frame of the page
/// draws the trail itself).
/// </summary>
/// <seealso href="https://wicg.github.io/ink-enhancement/#delegatedinktrailpresenter"/>
[IdlInterface("DelegatedInkTrailPresenter", "https://wicg.github.io/ink-enhancement/#delegatedinktrailpresenter")]
public sealed class DelegatedInkTrailPresenter
{
    private readonly Window _window;

    internal DelegatedInkTrailPresenter(Window window, Element? presentationArea)
    {
        _window = window;
        PresentationArea = presentationArea;
    }

    /// <summary>Gets the element the ink trails are clipped to, or null for the viewport of the document.</summary>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-delegatedinktrailpresenter-presentationarea"/>
    [IdlMember("presentationArea", "https://wicg.github.io/ink-enhancement/#dom-delegatedinktrailpresenter-presentationarea")]
    public Element? PresentationArea { get; }

    /// <summary>The position (client coordinates, in CSS pixels) and the style of the last start point of the ink trail, or null.</summary>
    internal (double X, double Y, InkTrailStyle Style)? LastStartPoint { get; private set; }

    /// <summary>
    /// Sets the last point of the ink trail that the page rendered
    /// (https://wicg.github.io/ink-enhancement/#dom-delegatedinktrailpresenter-updateinktrailstartpoint). The request is
    /// ignored when the presentation area is no longer connected to the document, as in Chromium.
    /// </summary>
    /// <param name="event">The pointer event of the point: a trusted event.</param>
    /// <param name="style">The style of the trail.</param>
    /// <exception cref="DomException">
    /// A <c>NotAllowedError</c> when the event is not trusted, and a <c>NotSupportedError</c> when the diameter is not
    /// greater than 0.
    /// </exception>
    /// <exception cref="TypeErrorException">The color is not a valid CSS color.</exception>
    /// <seealso href="https://wicg.github.io/ink-enhancement/#dom-delegatedinktrailpresenter-updateinktrailstartpoint"/>
    [IdlMember("updateInkTrailStartPoint", "https://wicg.github.io/ink-enhancement/#dom-delegatedinktrailpresenter-updateinktrailstartpoint")]
    public void UpdateInkTrailStartPoint(PointerEvent @event, InkTrailStyle style)
    {
        ArgumentNullException.ThrowIfNull(@event);
        ArgumentNullException.ThrowIfNull(style);

        // "This must be a trusted event", the color "must be a valid CSS color" and the diameter "must be greater than 0"
        if (!@event.IsTrusted)
            throw DomException.NotAllowed("Only the trusted pointer events are accepted");

        if (CssValueComputer.ParseCssColorValue(style.Color) is null)
            throw new TypeErrorException($"'{style.Color}' is not a valid color");

        if (!(style.Diameter > 0))
            throw DomException.NotSupported("The diameter of the ink trail must be greater than 0");

        if (PresentationArea is { IsConnected: false } || !_window.Document.IsFullyActive)
            return;

        LastStartPoint = (@event.ClientX, @event.ClientY, style);
    }
}
