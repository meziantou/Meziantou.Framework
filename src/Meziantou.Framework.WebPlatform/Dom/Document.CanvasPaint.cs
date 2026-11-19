namespace Meziantou.Framework.WebPlatform.Dom;

// HTML-in-canvas (https://github.com/WICG/html-in-canvas): the snapshots of the drawable elements and the paint events of the
// canvases, in the rendering updates
public partial class Document
{
    /// <summary>The canvases whose content is drawable or that requested a paint event (see <see cref="HtmlCanvasElement.RequestPaint"/>), or null.</summary>
    private HashSet<HtmlCanvasElement>? _renderedCanvases;

    /// <summary>Registers a canvas whose drawable elements are recorded by the rendering updates, and schedules a rendering update.</summary>
    internal void AddRenderedCanvas(HtmlCanvasElement canvas)
    {
        (_renderedCanvases ??= new(ReferenceEqualityComparer.Instance)).Add(canvas);
        if (EventLoop is { } eventLoop)
        {
            eventLoop.AddDocumentWithRenderedCanvases(this);
            eventLoop.RequestRenderingUpdate();
        }
    }

    /// <summary>Whether the document has canvases registered by <see cref="AddRenderedCanvas"/>.</summary>
    internal bool HasRenderedCanvases => _renderedCanvases is { Count: > 0 };

    /// <summary>
    /// The paint step of the rendering update for the canvases of the document (https://github.com/WICG/html-in-canvas: "this
    /// event fires just after intersection observer steps have run"): the snapshots of the drawable elements of each canvas are
    /// recorded, then the <c>paint</c> events are fired, "in reverse tree order", at the canvases whose drawable elements changed
    /// (also by the drawings of the paint events fired before, see <see cref="HtmlCanvasElement.UpdateCanvasImages"/>) or that
    /// requested one. The canvases that are no longer connected nor drawable are forgotten.
    /// </summary>
    internal void RunCanvasPaintSteps()
    {
        if (_renderedCanvases is null)
            return;

        _renderedCanvases.RemoveWhere(canvas => !canvas.IsConnected || !ReferenceEquals(canvas.NodeDocument, this));
        if (_renderedCanvases.Count == 0)
            return;

        var canvases = GetInShadowIncludingTreeOrder(_renderedCanvases);
        var engine = GetLayoutEngine();
        engine.EnsureLayout();
        var results = new List<(HtmlCanvasElement Canvas, List<Element> Changed, bool Fires)>();
        foreach (var canvas in canvases)
        {
            if (canvas.RecordSnapshots(engine) is { } result)
            {
                results.Add((canvas, result.Changed, result.Fires));
            }
            else
            {
                _renderedCanvases.Remove(canvas);
            }
        }

        // The snapshots draw the current bitmaps of their canvases: the drawings of the paint event of a nested canvas (fired
        // first) change the snapshot of its drawable ancestor recorded by this rendering update
        for (var i = results.Count - 1; i >= 0; i--)
        {
            var (canvas, changed, fires) = results[i];
            if (canvas.UpdateCanvasImages(changed) | fires)
            {
                canvas.FireEvent(new CanvasPaintEvent("paint", new CanvasPaintEventInit { ChangedElements = changed }));
            }
        }
    }

    /// <summary>Returns the elements of the document in shadow-including tree order.</summary>
    private List<HtmlCanvasElement> GetInShadowIncludingTreeOrder(HashSet<HtmlCanvasElement> elements)
    {
        var result = new List<HtmlCanvasElement>(elements.Count);
        var stack = new Stack<Node>();
        stack.Push(this);
        while (stack.TryPop(out var node) && result.Count < elements.Count)
        {
            if (node is HtmlCanvasElement canvas && elements.Contains(canvas))
            {
                result.Add(canvas);
            }

            for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
            {
                stack.Push(child);
            }

            if (node is Element { AttachedShadowRoot: { } shadowRoot })
            {
                stack.Push(shadowRoot);
            }
        }

        return result;
    }
}
