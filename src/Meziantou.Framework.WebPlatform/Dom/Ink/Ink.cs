using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

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

    /// <summary>The presenters whose ink trail may still be displayed (see <see cref="DelegatedInkTrailPresenter.UpdateInkTrailStartPoint"/>).</summary>
    private List<DelegatedInkTrailPresenter>? _activePresenters;

    internal Ink(Window window) => _window = window;

    /// <summary>Registers a presenter whose ink trail starts (it is drawn until it expires).</summary>
    internal void AddActivePresenter(DelegatedInkTrailPresenter presenter)
    {
        _activePresenters ??= [];
        _activePresenters.RemoveAll(active => !ReferenceEquals(active, presenter) && !active.HasTrail);
        if (!_activePresenters.Contains(presenter))
        {
            _activePresenters.Add(presenter);
        }
    }

    /// <summary>
    /// Called after a trusted <c>pointermove</c> event is dispatched in the document of the window: the position of the
    /// pointer extends the ink trails that started from an earlier event of the same pointer.
    /// </summary>
    internal void OnPointerMove(PointerEvent @event)
    {
        if (_activePresenters is null)
            return;

        foreach (var presenter in _activePresenters)
        {
            presenter.OnPointerMove(@event);
        }
    }

    /// <summary>
    /// Adds the ink trails that the compositor displays to the display list of a screenshot of the document of the window
    /// (in the coordinates of its viewport, or of the document for a full page screenshot): the trails expire after the
    /// rendering update that follows their start point.
    /// </summary>
    internal DisplayList AddInkTrails(DisplayList displayList, bool fullPage)
    {
        if (_activePresenters is null)
            return displayList;

        _activePresenters.RemoveAll(presenter => !presenter.HasTrail);
        if (_activePresenters.Count == 0)
            return displayList;

        var document = _window.Document;
        var viewport = new PhysicalRect(0, 0, document.GetStyleEngine().Environment.Width, document.GetStyleEngine().Environment.Height);
        var scrollOffset = fullPage && document.LayoutEngineOrNull is { } engine ? engine.ViewportScrollState.Offset : default;
        DisplayListBuilder? builder = null;
        foreach (var presenter in _activePresenters)
        {
            if (presenter.GetTrailPaint(viewport) is not { } trail)
                continue;

            if (builder is null)
            {
                builder = new DisplayListBuilder();
                builder.AddList(displayList);
            }

            builder.Save();
            if (fullPage)
            {
                builder.Transform(DisplayMatrix.Translation(scrollOffset.X, scrollOffset.Y));
            }

            builder.Add(new ClipRectItem(trail.Clip));
            builder.Add(new DrawPathItem(trail.Path, trail.Paint));
            builder.Restore();
        }

        return builder?.ToDisplayList(displayList.Size) ?? displayList;
    }

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

    /// <summary>The ink trail displayed from the last start point, or null.</summary>
    private InkTrail? _trail;

    /// <summary>Whether the presenter has an ink trail that did not expire.</summary>
    internal bool HasTrail
    {
        get
        {
            if (_trail is { } trail && (_window.EventLoop.RenderingUpdateCount > trail.RenderingUpdate + 1 || !_window.Document.IsFullyActive))
            {
                _trail = null;
            }

            return _trail is not null;
        }
    }

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

        if (CssValueComputer.ParseCssColorValue(style.Color) is not { } color)
            throw new TypeErrorException($"'{style.Color}' is not a valid color");

        if (!(style.Diameter > 0))
            throw DomException.NotSupported("The diameter of the ink trail must be greater than 0");

        if (PresentationArea is { IsConnected: false } || !_window.Document.IsFullyActive)
            return;

        // The trail starts from the point of an event of the document of the window (the coordinates of the events of other
        // documents are not in its viewport)
        if (@event.Target is Node target && !ReferenceEquals(target as Document ?? target.NodeDocument, _window.Document))
            return;

        _trail = new InkTrail(@event, @event.PointerId, style, DisplayColor.FromCss(color) ?? DisplayColor.Black, _window.EventLoop.RenderingUpdateCount);
        _trail.Points.Add((@event.ClientX, @event.ClientY));
        _window.Navigator.Ink.AddActivePresenter(this);
    }

    /// <summary>A trusted <c>pointermove</c> event was dispatched in the document of the window: its position extends the trail of its pointer.</summary>
    internal void OnPointerMove(PointerEvent @event)
    {
        if (!HasTrail || _trail is not { } trail || ReferenceEquals(trail.StartEvent, @event) || trail.PointerId != @event.PointerId)
            return;

        // (the trail of a pointer that moves while no frame is rendered keeps its last points)
        var last = trail.Points[^1];
        if ((last.X != @event.ClientX || last.Y != @event.ClientY) && trail.Points.Count < InkTrail.MaxPoints)
        {
            trail.Points.Add((@event.ClientX, @event.ClientY));
        }
    }

    /// <summary>
    /// Returns the path, the paint and the clip of the ink trail (in client coordinates), or null when there is none: a
    /// polyline from the start point through the later positions of the pointer, with the diameter and the color of the
    /// style, round caps and joins, clipped to the border box of the presentation area (or to the viewport).
    /// </summary>
    internal (DisplayPath Path, DisplayPaint Paint, PhysicalRect Clip)? GetTrailPaint(PhysicalRect viewport)
    {
        if (!HasTrail || _trail is not { Points.Count: > 1 } trail)
            return null;

        var clip = viewport;
        if (PresentationArea is { } area)
        {
            if (!area.IsConnected)
                return null;

            var rect = area.GetBoundingClientRect();
            var left = Math.Max(clip.Left, Math.Min(rect.Left, rect.Right));
            var top = Math.Max(clip.Top, Math.Min(rect.Top, rect.Bottom));
            var right = Math.Min(clip.Right, Math.Max(rect.Left, rect.Right));
            var bottom = Math.Min(clip.Bottom, Math.Max(rect.Top, rect.Bottom));
            clip = new PhysicalRect(left, top, right - left, bottom - top);
        }

        if (clip.IsEmpty)
            return null;

        var path = new DisplayPath();
        path.MoveTo(trail.Points[0].X, trail.Points[0].Y);
        for (var i = 1; i < trail.Points.Count; i++)
        {
            path.LineTo(trail.Points[i].X, trail.Points[i].Y);
        }

        // The diameter is an unrestricted double: a trail wider than the clip (e.g. Infinity) covers it
        var width = Math.Min(trail.Style.Diameter, (Math.Max(clip.Width, clip.Height) * 2) + 1);
        var paint = DisplayPaint.ForStroke(trail.Color, new StrokeStyle(width, LineCap.Round, LineJoin.Round));
        return (path, paint, clip);
    }

    /// <summary>An ink trail: its start event and pointer, its style, its points (client coordinates), and the rendering update of its start.</summary>
    private sealed class InkTrail(PointerEvent startEvent, int pointerId, InkTrailStyle style, DisplayColor color, long renderingUpdate)
    {
        /// <summary>The maximum number of points of a trail: the later positions of the pointer are not added.</summary>
        public const int MaxPoints = 1024;

        public PointerEvent StartEvent { get; } = startEvent;

        public int PointerId { get; } = pointerId;

        public InkTrailStyle Style { get; } = style;

        public DisplayColor Color { get; } = color;

        public long RenderingUpdate { get; } = renderingUpdate;

        public List<(double X, double Y)> Points { get; } = [];
    }
}
