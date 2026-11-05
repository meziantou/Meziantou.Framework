namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The orientation of a display feature (https://drafts.csswg.org/css-viewport/#display-feature-override).</summary>
public enum DisplayFeatureOrientation
{
    /// <summary>A vertical fold or hinge: it splits the viewport in segments side by side.</summary>
    Vertical,

    /// <summary>A horizontal fold or hinge: it splits the viewport in segments one above the other.</summary>
    Horizontal,
}

/// <summary>
/// A display feature of the screen (https://drafts.csswg.org/css-viewport/#display-feature-override), such as the fold of a
/// foldable device or the hinge between two screens, which splits the viewport in segments.
/// </summary>
/// <param name="Orientation">The orientation of the feature.</param>
/// <param name="Offset">The distance in CSS pixels from the left edge (vertical feature) or the top edge (horizontal feature) of the viewport.</param>
/// <param name="MaskLength">The width (vertical feature) or the height (horizontal feature) of the feature in CSS pixels: the area that the viewport does not display.</param>
public sealed record DisplayFeature(DisplayFeatureOrientation Orientation, double Offset, double MaskLength);

/// <summary>The host hooks of the viewport segments (https://drafts.csswg.org/css-viewport/#viewport-segments, <see cref="BrowsingContextOptions.ViewportSegments"/>).</summary>
public sealed class ViewportSegmentsOptions
{
    /// <summary>
    /// Gets or sets the display features of the screen, which split the viewport of the top-level documents in segments
    /// (<c>window.viewport.segments</c>, the <c>horizontal-viewport-segments</c> and <c>vertical-viewport-segments</c> media
    /// features and the <c>viewport-segment-*</c> environment variables). A provider can be shared by several browsing
    /// contexts. The default is null: the viewport has a single segment.
    /// </summary>
    public DisplayFeaturesProvider? Provider { get; init; }
}

/// <summary>
/// The display features of the screen of the device (https://drafts.csswg.org/css-viewport/#display-feature-override), of the
/// browsing contexts that share a <see cref="BrowsingContextOptions"/> (<see cref="ViewportSegmentsOptions.Provider"/>). Set
/// <see cref="DisplayFeatures"/> to simulate a foldable device: the documents evaluate their media queries again and compute
/// their styles again.
/// </summary>
/// <remarks>A provider can be shared by several browsing contexts, and used from any thread.</remarks>
public class DisplayFeaturesProvider
{
    private readonly Lock _lock = new();
    private readonly List<WeakReference<BrowsingContext>> _browsingContexts = [];
    private IReadOnlyList<DisplayFeature> _displayFeatures = [];

    /// <summary>Raised when the display features change (<see cref="OnDisplayFeaturesChanged"/>).</summary>
    public event EventHandler? DisplayFeaturesChanged;

    /// <summary>
    /// Gets or sets the display features of the screen (empty by default: the viewport has a single segment). The offsets are
    /// relative to the viewport of the top-level documents. Setting it updates the documents.
    /// </summary>
    /// <exception cref="ArgumentException">An offset or a mask length is negative or not finite.</exception>
    public IReadOnlyList<DisplayFeature> DisplayFeatures
    {
        get
        {
            lock (_lock)
            {
                return _displayFeatures;
            }
        }
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            foreach (var feature in value)
            {
                ArgumentNullException.ThrowIfNull(feature, nameof(value));
                if (!double.IsFinite(feature.Offset) || feature.Offset < 0 || !double.IsFinite(feature.MaskLength) || feature.MaskLength < 0)
                    throw new ArgumentException("The offset and the mask length of a display feature must be positive numbers", nameof(value));
            }

            lock (_lock)
            {
                _displayFeatures = [.. value];
            }

            OnDisplayFeaturesChanged();
        }
    }

    /// <summary>Returns the display features of the screen. The default implementation returns <see cref="DisplayFeatures"/>. It is called on the thread of an event loop, and must not block it.</summary>
    public virtual IReadOnlyList<DisplayFeature> GetDisplayFeatures() => DisplayFeatures;

    /// <summary>
    /// Raises <see cref="DisplayFeaturesChanged"/>, then updates the documents: their media queries are evaluated again
    /// (<c>change</c> events of the <c>MediaQueryList</c> objects) and their styles are computed again. It can be called from any thread.
    /// </summary>
    protected void OnDisplayFeaturesChanged()
    {
        DisplayFeaturesChanged?.Invoke(this, EventArgs.Empty);
        List<BrowsingContext> browsingContexts = [];
        lock (_lock)
        {
            foreach (var reference in _browsingContexts)
            {
                if (reference.TryGetTarget(out var browsingContext))
                {
                    browsingContexts.Add(browsingContext);
                }
            }
        }

        foreach (var browsingContext in browsingContexts)
        {
            if (browsingContext.IsDestroyed)
                continue;

            browsingContext.EventLoop.QueueTask(() =>
            {
                if (browsingContext.IsDestroyed)
                    return;

                foreach (var navigable in browsingContext.GetInclusiveDescendantNavigables())
                {
                    navigable.Document.InvalidateLayout();
                }

                browsingContext.EventLoop.RequestRenderingOpportunity(browsingContext.Document);
            });
        }
    }

    /// <summary>Registers a top-level browsing context, whose documents are updated when the display features change.</summary>
    internal void Register(BrowsingContext topLevel)
    {
        lock (_lock)
        {
            _browsingContexts.RemoveAll(static reference => !reference.TryGetTarget(out _));
            _browsingContexts.Add(new WeakReference<BrowsingContext>(topLevel));
        }
    }
}

/// <summary>
/// The segments of a viewport split by display features (https://drafts.csswg.org/css-env-1/#viewport-segments): a grid of
/// columns (split by the vertical features) and rows (split by the horizontal features).
/// </summary>
/// <param name="Columns">The left and right edges of the columns, from left to right.</param>
/// <param name="Rows">The top and bottom edges of the rows, from top to bottom.</param>
internal sealed record ViewportSegmentGrid((double Start, double End)[] Columns, (double Start, double End)[] Rows)
{
    /// <summary>Computes the segments of a viewport, or returns null when the viewport has a single segment.</summary>
    public static ViewportSegmentGrid? Create(IReadOnlyList<DisplayFeature> features, double width, double height)
    {
        if (features.Count == 0)
            return null;

        var columns = Split(features, DisplayFeatureOrientation.Vertical, width);
        var rows = Split(features, DisplayFeatureOrientation.Horizontal, height);
        return columns.Length == 1 && rows.Length == 1 ? null : new ViewportSegmentGrid(columns, rows);
    }

    private static (double Start, double End)[] Split(IReadOnlyList<DisplayFeature> features, DisplayFeatureOrientation orientation, double length)
    {
        var result = new List<(double Start, double End)>();
        var start = 0d;
        foreach (var feature in features.Where(feature => feature.Orientation == orientation).OrderBy(feature => feature.Offset))
        {
            var end = Math.Min(feature.Offset, length);
            if (end > start)
            {
                result.Add((start, end));
            }

            start = Math.Max(start, Math.Min(feature.Offset + feature.MaskLength, length));
        }

        if (length > start || result.Count == 0)
        {
            result.Add((start, Math.Max(start, length)));
        }

        return [.. result];
    }

    /// <summary>
    /// The value of a <c>viewport-segment-*</c> environment variable (https://drafts.csswg.org/css-env-1/#viewport-segments)
    /// for the segment of a column and a row, in CSS pixels, or null when the segment does not exist.
    /// </summary>
    public double? GetValue(string name, int column, int row)
    {
        if ((uint)column >= (uint)Columns.Length || (uint)row >= (uint)Rows.Length)
            return null;

        var (left, right) = Columns[column];
        var (top, bottom) = Rows[row];
        return name switch
        {
            "viewport-segment-width" => right - left,
            "viewport-segment-height" => bottom - top,
            "viewport-segment-top" => top,
            "viewport-segment-left" => left,
            "viewport-segment-bottom" => bottom,
            "viewport-segment-right" => right,
            _ => null,
        };
    }

    /// <summary>The rectangles of the segments, in row-major order (https://drafts.csswg.org/css-viewport/#dom-viewport-segments).</summary>
    public IEnumerable<(double X, double Y, double Width, double Height)> GetSegments()
    {
        foreach (var (top, bottom) in Rows)
        {
            foreach (var (left, right) in Columns)
            {
                yield return (left, top, right - left, bottom - top);
            }
        }
    }

    public bool Equals([System.Diagnostics.CodeAnalysis.NotNullWhen(true)] ViewportSegmentGrid? other) => other is not null && Columns.AsSpan().SequenceEqual(other.Columns) && Rows.AsSpan().SequenceEqual(other.Rows);

    public override int GetHashCode() => HashCode.Combine(Columns.Length, Rows.Length);
}
