using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The painting of the document (see Painter): the display list of the viewport, painted lazily and cached until the layout
// (which changes with the styles, including the paint-only properties), the DOM or the scroll positions change.
internal sealed partial class LayoutEngine
{
    private DisplayList? _displayList;

    // The timestamp (of the clock of the event loop) of the next frame of an animated image of the display list, or null
    private long? _displayListNextFrame;
    private int _displayListLayoutVersion = -1;
    private int _displayListPaintVersion = -1;
    private int _displayListDomVersion = -1;
    private DisplayColor _displayListBaseColor;
    private int _paintVersion;

    /// <summary>The number of times the document was painted (for the tests and the diagnostics).</summary>
    public int PaintCount { get; private set; }

    /// <summary>
    /// Returns the display list of the viewport of the document (https://drafts.csswg.org/css2/#painting): the layout is computed
    /// when it is out of date, and the document is painted again only when the layout or the scroll positions changed since the
    /// last painting. Returns null when the document is not rendered.
    /// </summary>
    public DisplayList? GetDisplayList()
    {
        // https://drafts.csswg.org/css-view-transitions-1/#document-rendering-suppression-for-view-transitions: the document is not
        // rendered while the update callback of a view transition runs: the frame painted when the old state was captured is displayed
        if (_displayList is not null && _document.ViewTransitionRenderingSuppressed)
            return _displayList;

        if (EnsureLayout() is null)
            return null;

        // The painting reads the styles and the fragments of the layout it paints: a change of a version (e.g. the version of
        // the style sheets of the document) in the middle of the painting would start a new generation of styles, and the parts of the
        // painting that read the fragments of an element (e.g. the content of a foreignObject element) would lay the document
        // out again and paint the fragments of two layouts. The updates of the styles are suspended, and the layout is brought
        // up to date with the generation of styles that is painted (the steps that follow a layout may have started a new one)
        using var suspension = _document.GetStyleEngine().SuspendUpdates();
        if (EnsureLayout() is not { } root)
            return null;

        // A change of the DOM that changes neither the styles nor the layout can change the painting (e.g. an attribute of a filter
        // primitive or of the content of a mask element, which have no box)
        // The display list shows the frames of the animated images of a time: it is painted again when one of them changed
        var clock = _document.EventLoop?.TimeProvider;
        var animationChanged = _displayListNextFrame is { } nextFrame && clock is not null && clock.GetTimestamp() >= nextFrame;
        var baseColor = GetCanvasBaseColor();
        if (_displayList is null || animationChanged || _displayListLayoutVersion != _fragmentIndexVersion || _displayListPaintVersion != _paintVersion || _displayListDomVersion != _document.DomVersion
            || _displayListBaseColor != baseColor)
        {
            _displayListBaseColor = baseColor;
            _displayList = Painter.Paint(this, _document, root, ViewportScrollState.Offset, baseColor, _isCaretBlinkedOff, out var nextFrameDelay, out var hasAnimatedCaret);
            _displayListNextFrame = nextFrameDelay is { } delay && clock is not null ? clock.GetTimestamp() + (long)Math.Ceiling(delay / 1000 * clock.TimestampFrequency) : null;
            _displayListHasAnimatedCaret = hasAnimatedCaret;
            _displayListLayoutVersion = _fragmentIndexVersion;
            _displayListPaintVersion = _paintVersion;
            _displayListDomVersion = _document.DomVersion;
            PaintCount++;
        }

        // The caret blinks while it is painted
        if (_displayListHasAnimatedCaret)
        {
            _caretBlinkPainted = true;
            ScheduleCaretBlink();
        }

        return _displayList;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // The blinking of the caret (https://drafts.csswg.org/css-ui-4/#caret-animation, ViewportOptions.CaretBlinkInterval): a
    // state of the document toggled by a timer of the event loop, not a function of the time of the painting, so that what is
    // painted only depends on the tasks that the event loop ran. The timer runs while the display list is requested: it stops
    // (the caret being displayed) when the document was not painted during a phase.
    // ----------------------------------------------------------------------------------------------------------------

    private bool _displayListHasAnimatedCaret;
    private bool _isCaretBlinkedOff;
    private bool _caretBlinkPainted;
    private bool _caretBlinkScheduled;
    private int _caretBlinkGeneration;

    /// <summary>The time during which a blinking caret is displayed, then hidden, or null when the caret does not blink.</summary>
    private TimeSpan? CaretBlinkInterval
    {
        get
        {
            var interval = (_document.BrowsingContext?.Top.Options.Viewport ?? Dom.ViewportOptions.Default).CaretBlinkInterval;
            return interval > TimeSpan.Zero ? interval : null;
        }
    }

    private void ScheduleCaretBlink()
    {
        if (_caretBlinkScheduled || CaretBlinkInterval is not { } interval || _document.EventLoop is not { } eventLoop)
            return;

        _caretBlinkScheduled = true;
        var generation = _caretBlinkGeneration;
        eventLoop.QueueTimer(interval, () =>
        {
            if (generation != _caretBlinkGeneration)
                return;

            _caretBlinkScheduled = false;
            if (!_caretBlinkPainted || !_document.IsFullyActive)
            {
                // Nothing displayed the caret during the phase: the blinking stops
                if (_isCaretBlinkedOff)
                {
                    _isCaretBlinkedOff = false;
                    InvalidateCaretPaint();
                }

                return;
            }

            _caretBlinkPainted = false;
            _isCaretBlinkedOff = !_isCaretBlinkedOff;
            InvalidateCaretPaint();
            ScheduleCaretBlink();
        });
    }

    /// <summary>Marks the painting of the document as out of date, and the one of the documents that paint it in an embedding element.</summary>
    private void InvalidateCaretPaint()
    {
        InvalidatePaint();
        for (var container = _document.BrowsingContext?.Parent; container is not null; container = container.Parent)
        {
            container.Document.InvalidatePaint();
        }
    }

    /// <summary>
    /// Called when the caret of the document may have changed (the selection, the selection of a text control, the focused
    /// area, the system focus): the document is painted again, and the blinking starts again with the caret displayed, as in
    /// the browsers (the caret does not disappear while the user moves it).
    /// </summary>
    public void OnCaretChanged()
    {
        InvalidateCaretPaint();
        _isCaretBlinkedOff = false;
        if (_caretBlinkScheduled)
        {
            _caretBlinkGeneration++;
            _caretBlinkScheduled = false;
            ScheduleCaretBlink();
        }
    }

    /// <summary>
    /// Returns the color the canvas of the document is drawn over: the canvas color of its color scheme for a top-level document
    /// (white, the default background of the browsers, or a dark color for a dark color scheme); a nested document is transparent,
    /// unless its color scheme is not the used color scheme of the element that embeds it
    /// (https://drafts.csswg.org/css-color-adjust-1/#color-scheme-effect: "the embedded document's canvas [...] must be opaque", so
    /// a dark document is readable in a light page; as in the browsers, the initial about:blank document stays transparent).
    /// </summary>
    internal DisplayColor GetCanvasBaseColor()
    {
        if (_document.BrowsingContext is { Parent: null })
            return Painter.GetCanvasBaseColor(_document);

        if (_document is { IsInitialAboutBlank: false, BrowsingContext.Container: { } container, DocumentElement: { } rootElement } && ComputedStyleDeclarations.IsBeingRendered(container)
            && CssValueComputer.GetUsedColorScheme(_document.GetStyleEngine().GetStyle(rootElement)) != CssValueComputer.GetUsedColorScheme(container.NodeDocument.GetStyleEngine().GetStyle(container)))
        {
            return Painter.GetCanvasBaseColor(_document);
        }

        return DisplayColor.Transparent;
    }

    /// <summary>Marks the painting as out of date after a change that does not change the layout (e.g. a scroll position).</summary>
    public void InvalidatePaint() => _paintVersion++;
}
