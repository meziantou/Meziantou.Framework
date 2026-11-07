using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A position option of <c>position-try-fallbacks</c> applied to an anchored query container by its last layout
/// (https://drafts.csswg.org/css-anchor-position-2/#anchored): the entry of <c>position-try-fallbacks</c> (a <c>@position-try</c>
/// name and try tactics, or a &lt;position-area&gt;) and the writing modes its &lt;position-area&gt; is resolved with.
/// </summary>
internal sealed record AnchoredFallback(string Text, string? RuleName, IReadOnlyList<TryTactic> Tactics, string? PositionArea, WritingDirection ContainingBlock, WritingDirection Self);

// The anchored container queries (https://drafts.csswg.org/css-anchor-position-2/#anchored-container-queries): the position
// option chosen by the layout of an anchored query container is kept by element, and the styles of its descendants that queried
// another option are computed again during the layout (the style and layout interleaving, as for the size containers, see
// AnchorPositioning.Layout).
internal sealed partial class LayoutEngine
{
    /// <summary>The position options applied to the anchored query containers by the last layout pass that laid them out (null for the base styles).</summary>
    private Dictionary<Element, (AnchoredFallback? Fallback, int Pass)>? _anchoredFallbacks;

    /// <summary>
    /// Returns the position option of <c>position-try-fallbacks</c> applied to an anchored query container by its last layout, or
    /// null when none is applied (the base styles, or the container was not laid out). The layout is not computed.
    /// </summary>
    public AnchoredFallback? GetAnchoredFallback(Element element)
        => _anchoredFallbacks is not null && _anchoredFallbacks.TryGetValue(element, out var entry) ? entry.Fallback : null;

    /// <summary>Records the position option applied to an anchored query container by a layout pass (null for the base styles).</summary>
    internal void RecordAnchoredFallback(Element element, AnchoredFallback? fallback, int pass)
        => (_anchoredFallbacks ??= new(ReferenceEqualityComparer.Instance))[element] = (fallback, pass);

    /// <summary>Removes the position options of the anchored query containers that the pass did not lay out (removed, no longer containers or not rendered).</summary>
    private void RemoveStaleAnchoredFallbacks(int pass)
    {
        if (_anchoredFallbacks is not { Count: > 0 } fallbacks)
            return;

        List<Element>? stale = null;
        foreach (var (element, entry) in fallbacks)
        {
            if (entry.Pass != pass)
            {
                (stale ??= []).Add(element);
            }
        }

        if (stale is not null)
        {
            foreach (var element in stale)
            {
                fallbacks.Remove(element);
            }
        }
    }
}
