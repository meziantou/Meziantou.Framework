using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Dom;

// The container timing of a document (https://w3c.github.io/container-timing/): the paints of the images and of the texts reported
// by "mark paint timing" are accumulated in the painted region of their container roots, which report a container timing entry
// at the end of each rendering update that painted new content in their subtree
public partial class Document
{
    /// <summary>
    /// Called when the <c>containertiming</c> attribute of an element of the document is set: the paints of the document are computed
    /// from then on (see <see cref="StartPaintTiming"/>), so the paints of a container root are buffered before they are observed.
    /// </summary>
    internal void OnContainerTimingRootRegistered()
    {
        if (BrowsingContext?.Options.EffectiveFeatures.ContainerTimingEnabled is true)
        {
            StartPaintTiming();
        }
    }

    /// <summary>
    /// https://w3c.github.io/container-timing/: the images and the texts painted by the rendering update are added to the painted
    /// region of their nearest container root, and of the container roots of its ancestors until an element with a
    /// <c>containertimingignore</c> attribute. Each container root whose painted region grew reports an entry.
    /// </summary>
    private void ReportContainerTiming(PaintTimingState state, LayoutEngine engine, Window window, PaintTimingInfo paintTimingInfo, List<(PendingImageRecord Record, PhysicalRect PaintedRect)> paintedImages, List<Element> paintedTextElements)
    {
        var records = state.ContainerTimingRecords;

        // The record of a container root is deleted when it is disconnected or its attribute is removed: it starts again from an empty region
        if (records.Count > 0)
        {
            List<HtmlElement>? removed = null;
            foreach (var (root, _) in records)
            {
                if (!root.IsConnected || root.GetAttributeValue("containertiming") is null)
                {
                    (removed ??= []).Add(root);
                }
            }

            if (removed is not null)
            {
                foreach (var root in removed)
                {
                    records.Remove(root);
                }
            }
        }

        // As the largest contentful paint, the new paints are ignored once an input event was dispatched
        if (window.HasDispatchedInputEvent)
            return;

        List<ContainerTimingRecord>? pendingRecords = null;
        foreach (var (record, paintedRect) in paintedImages)
        {
            HandlePaint(record.Element, GetImageIntersectionRect(engine, record, paintedRect));
        }

        foreach (var element in paintedTextElements)
        {
            HandlePaint(element, GetTextIntersectionRect(state, engine, element));
        }

        if (pendingRecords is null)
            return;

        foreach (var record in pendingRecords)
        {
            var bounds = record.Region.Bounds;
            var entry = new PerformanceContainerTiming(window, paintTimingInfo, record.Root.GetAttributeValue("containertiming") ?? "", new DomRectReadOnly(bounds.X, bounds.Y, bounds.Width, bounds.Height),
                (ulong)Math.Max(0, Math.Round(record.Region.Area, MidpointRounding.AwayFromZero)), record.FirstRenderTime, record.LastPaintedElement as HtmlElement, record.Root);
            window.Performance.QueueContainerTimingEntry(entry);
            record.IsPending = false;
            record.LastPaintedElement = null;
            record.LastPaintedArea = 0;
        }

        void HandlePaint(Element element, PhysicalRect intersectionRect)
        {
            if (intersectionRect.IsEmpty || !PaintTimingHelpers.IsExposedForPaintTiming(element, this))
                return;

            // The container roots of the element: the element itself and its ancestors, until an element with containertimingignore
            // (a container root with containertimingignore receives the paints of its subtree, but does not propagate them)
            for (var current = element; current is not null; current = current.ParentElement)
            {
                if (current is HtmlElement root && root.GetAttributeValue("containertiming") is not null)
                {
                    if (!records.TryGetValue(root, out var record))
                    {
                        record = new ContainerTimingRecord(root, paintTimingInfo.DefaultPaintTimestamp);
                        records.Add(root, record);
                    }

                    if (record.Region.Add(intersectionRect))
                    {
                        // As in the browsers, the reported element is the one with the largest area of its own (the first one for a tie),
                        // not the last one painted nor the one that painted the largest new area
                        var area = intersectionRect.Width * intersectionRect.Height;
                        if (!record.IsPending || area > record.LastPaintedArea)
                        {
                            record.LastPaintedElement = element;
                            record.LastPaintedArea = area;
                        }

                        if (!record.IsPending)
                        {
                            record.IsPending = true;
                            (pendingRecords ??= []).Add(record);
                        }
                    }
                }

                if (current.GetAttributeValue("containertimingignore") is not null)
                    break;
            }
        }
    }

    /// <summary>The container timing record of a container root: its painted region and the paints of the current rendering update.</summary>
    private sealed class ContainerTimingRecord(HtmlElement root, double firstRenderTime)
    {
        public HtmlElement Root { get; } = root;

        /// <summary>The time of the first paint in the container root.</summary>
        public double FirstRenderTime { get; } = firstRenderTime;

        public ContainerTimingRegion Region { get; } = new();

        /// <summary>Whether the painted region grew during the rendering update (an entry is reported at its end).</summary>
        public bool IsPending { get; set; }

        /// <summary>The largest element that painted new content during the rendering update.</summary>
        public Element? LastPaintedElement { get; set; }

        /// <summary>The area of the intersection rect of <see cref="LastPaintedElement"/>.</summary>
        public double LastPaintedArea { get; set; }
    }

    /// <summary>The region painted in a container root: a union of rectangles, its area (the overlapping parts are counted once) and its bounding rectangle.</summary>
    private sealed class ContainerTimingRegion
    {
        private readonly List<PhysicalRect> _rects = [];

        public double Area { get; private set; }

        public PhysicalRect Bounds { get; private set; }

        /// <summary>Adds a rectangle to the region, and returns whether the region grew (the rectangle is not covered by the region).</summary>
        public bool Add(PhysicalRect rect)
        {
            List<PhysicalRect>? overlaps = null;
            foreach (var existing in _rects)
            {
                if (existing.Left <= rect.Left && existing.Top <= rect.Top && existing.Right >= rect.Right && existing.Bottom >= rect.Bottom)
                    return false;

                if (IntersectionGeometry.TryIntersect(existing, rect, out var overlap) && !overlap.IsEmpty)
                {
                    (overlaps ??= []).Add(overlap);
                }
            }

            // The new area: the area of the rectangle that is not covered by the region
            var newArea = (rect.Width * rect.Height) - (overlaps is null ? 0 : GetUnionArea(overlaps));
            if (newArea <= 1e-6)
                return false;

            _rects.Add(rect);
            Area += newArea;
            Bounds = Bounds.Union(rect);
            return true;
        }

        /// <summary>Returns the area of the union of rectangles: the vertical strips between their horizontal edges, and the merged intervals of the rectangles in each strip.</summary>
        private static double GetUnionArea(List<PhysicalRect> rects)
        {
            var xs = new List<double>(rects.Count * 2);
            foreach (var rect in rects)
            {
                xs.Add(rect.Left);
                xs.Add(rect.Right);
            }

            xs.Sort();
            var area = 0d;
            var intervals = new List<(double Top, double Bottom)>();
            for (var i = 0; i < xs.Count - 1; i++)
            {
                var left = xs[i];
                var right = xs[i + 1];
                if (right <= left)
                    continue;

                intervals.Clear();
                foreach (var rect in rects)
                {
                    if (rect.Left <= left && rect.Right >= right)
                    {
                        intervals.Add((rect.Top, rect.Bottom));
                    }
                }

                if (intervals.Count == 0)
                    continue;

                intervals.Sort();
                var height = 0d;
                var (top, bottom) = intervals[0];
                for (var j = 1; j < intervals.Count; j++)
                {
                    var interval = intervals[j];
                    if (interval.Top > bottom)
                    {
                        height += bottom - top;
                        (top, bottom) = interval;
                    }
                    else
                    {
                        bottom = Math.Max(bottom, interval.Bottom);
                    }
                }

                height += bottom - top;
                area += height * (right - left);
            }

            return area;
        }
    }
}
