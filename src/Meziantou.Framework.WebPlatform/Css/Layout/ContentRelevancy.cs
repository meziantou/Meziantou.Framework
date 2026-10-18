using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Html;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>The proximity to the viewport of an element with <c>content-visibility: auto</c> (https://drafts.csswg.org/css-contain-2/#proximity-to-the-viewport).</summary>
internal enum ViewportProximity : byte
{
    /// <summary>https://drafts.csswg.org/css-contain-2/#not-determined: not computed since the element was connected (or gained <c>content-visibility: auto</c>).</summary>
    NotDetermined,

    /// <summary>https://drafts.csswg.org/css-contain-2/#close-to-the-viewport: its overflow clip edge intersects the viewport or a margin of 50% around it.</summary>
    Close,

    /// <summary>https://drafts.csswg.org/css-contain-2/#far-away-from-the-viewport</summary>
    Far,
}

/// <summary>The state of an element with <c>content-visibility: auto</c> (stored by the element, see <see cref="ContentRelevancy"/>).</summary>
internal sealed class ContentVisibilityAutoState
{
    /// <summary>The proximity to the viewport determined by the last rendering update.</summary>
    public ViewportProximity Proximity { get; set; }

    /// <summary>
    /// The "currently relevant to the user" flag (https://drafts.csswg.org/css-contain-2/#currently-relevant-to-the-user), or null
    /// before the first determination: as in the browsers, the first determination fires a <c>contentvisibilityautostatechange</c>
    /// event whether the element is relevant or not.
    /// </summary>
    public bool? CurrentlyRelevant { get; set; }
}

/// <summary>
/// The relevance to the user of the elements with <c>content-visibility: auto</c> (https://drafts.csswg.org/css-contain-2/#relevant-to-the-user):
/// an element that is not relevant skips its contents (<see cref="ContainerBox.SkipsContents"/>).
/// </summary>
/// <remarks>
/// <para>
/// The proximity to the viewport is determined by the rendering steps (<see cref="LayoutEngine.UpdateRendering"/>, "update
/// content relevancy for a document"), with the layout of the frame: the initial determination takes effect immediately (the
/// layout is computed again in the same rendering update when the element is close to the viewport), the next ones at the
/// next layout. The other conditions (the focus, the selection, the top layer) are checked by each layout pass, so they take
/// effect immediately, as in the browsers (e.g. selecting the contents of a skipped element un-skips it before the next frame).
/// </para>
/// <para>
/// The <c>contentvisibilityautostatechange</c> event is fired by a task when the "currently relevant to the user" flag changes,
/// which is updated by the rendering steps after the proximities are determined.
/// </para>
/// </remarks>
internal static class ContentRelevancy
{
    /// <summary>The margin around the viewport (and the scrollports of the scroll containers) within which an element is close to the viewport: 50% of their size, as suggested by the specification and as in the browsers.</summary>
    private static readonly IntersectionMargin ViewportMargin = IntersectionMargin.FromPercentage(50);

    /// <summary>Returns the state of an element with <c>content-visibility: auto</c>, or null when it was never determined.</summary>
    public static ContentVisibilityAutoState? GetState(Element element) => (element.RareDataOrNull as ElementRareData)?.ContentVisibilityAuto;

    /// <summary>
    /// Returns whether an element is relevant to the user (https://drafts.csswg.org/css-contain-2/#relevant-to-the-user): it is
    /// close to the viewport (as determined by the last rendering update), or it or its contents are focused, selected, or in the
    /// top layer.
    /// </summary>
    public static bool IsRelevantToTheUser(Element element)
    {
        if (GetState(element) is { Proximity: ViewportProximity.Close })
            return true;

        var document = element.NodeDocument;

        // "Either the element or its contents are focused"
        if (FocusModel.GetFocusedElement(document) is { } focused && IsFlatTreeInclusiveAncestor(element, focused))
            return true;

        // "Either the element or its contents are selected": the range of the selection intersects them
        if (document.SelectionOrNull?.RangeOrNull is { } range && range.IntersectsNode(element))
            return true;

        // https://drafts.csswg.org/css-anchor-position-1/#anchor-relevance: a descendant is a target anchor element
        if (document.LayoutEngineOrNull is { } engine && engine.IsAnchorRelevant(element))
            return true;

        // "Either the element or its contents are placed in the top layer"
        if (document.TopLayerElements is { Count: > 0 } topLayer)
        {
            foreach (var topLayerElement in topLayer)
            {
                if (IsFlatTreeInclusiveAncestor(element, topLayerElement))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Returns whether an element skips its contents (https://drafts.csswg.org/css-contain-2/#skips-its-contents) from its computed
    /// style and its relevance to the user, for the features that do not need the box tree (innerText, checkVisibility()):
    /// <c>content-visibility: hidden</c>, or <c>content-visibility: auto</c> when it is not relevant to the user. The closed
    /// <c>details</c> elements are not included.
    /// </summary>
    public static bool SkipsContents(Element element, ComputedStyle style)
    {
        var contentVisibility = style.Layout.ContentVisibility;
        if (contentVisibility == ContentVisibility.Visible || !UsedContainment.CanApplySizeContainment(element, style))
            return false;

        return contentVisibility == ContentVisibility.Hidden || !IsRelevantToTheUser(element);
    }

    /// <summary>
    /// Returns whether an element is in the skipped contents of a flat tree ancestor (https://drafts.csswg.org/css-contain-2/#skips-its-contents):
    /// <c>content-visibility: hidden</c>, a closed <c>details</c> element, or <c>content-visibility: auto</c> when the ancestor
    /// is not relevant to the user (not when its proximity to the viewport is not determined yet and
    /// <paramref name="ignoreUndetermined"/> is true). The element may still generate boxes.
    /// </summary>
    public static bool IsInSkippedContents(Element element, bool ignoreUndetermined = false)
    {
        var generation = BoxGeneration.Get(element);
        if (generation.InSkippedContents)
            return true;

        if (generation.Kind == BoxGenerationKind.NoStyle)
            return false;

        var engine = element.NodeDocument.GetStyleEngine();
        for (var ancestor = HtmlElement.GetFlatTreeParent(element) as Element; ancestor is not null; ancestor = HtmlElement.GetFlatTreeParent(ancestor) as Element)
        {
            var style = engine.GetStyle(ancestor);
            if (style.Layout.ContentVisibility == ContentVisibility.Auto && SkipsContents(ancestor, style) && !(ignoreUndetermined && GetState(ancestor) is not { Proximity: not ViewportProximity.NotDetermined }))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Updates the content relevancy of the document (https://drafts.csswg.org/css-contain-2/#update-content-relevancy-for-a-document):
    /// for each element with <c>content-visibility: auto</c> of the last layout, updates its "currently relevant to the user"
    /// flag when <paramref name="updateNonInitial"/> is true (with the proximity determined by the previous rendering update),
    /// determines its proximity to the viewport and, for an initial determination, updates the flag again. A change of the
    /// flag posts a task that fires a <c>contentvisibilityautostatechange</c> event (https://drafts.csswg.org/css-contain-2/#content-visibility-auto-state-change).
    /// The layout must be up to date.
    /// </summary>
    /// <returns>
    /// Whether an initial determination made an element relevant to the user ("hadInitialVisibleContentVisibilityDetermination",
    /// https://html.spec.whatwg.org/multipage/webappapis.html#update-the-rendering): the layout must be computed again before
    /// the rendering continues. <paramref name="proximityChanged"/> tells whether another proximity changed: it takes effect at
    /// the next layout (and the next rendering update fires its event).
    /// </returns>
    public static bool UpdateContentRelevancy(LayoutEngine engine, List<ContainerBox> boxes, bool updateNonInitial, out bool proximityChanged)
    {
        var hadInitialVisibleDetermination = false;
        proximityChanged = false;
        foreach (var box in boxes)
        {
            if (box.Element is not { } element || !ReferenceEquals(engine.BoxTree?.GetPrincipalBox(element), box))
                continue;

            var state = GetOrCreateState(element);
            var isInitialDetermination = state.Proximity == ViewportProximity.NotDetermined;
            if (updateNonInitial && !isInitialDetermination)
            {
                UpdateCurrentlyRelevant(element, state);
            }

            var proximity = IsCloseToTheViewport(engine, box) ? ViewportProximity.Close : ViewportProximity.Far;
            if (proximity != state.Proximity)
            {
                state.Proximity = proximity;
                proximityChanged |= !isInitialDetermination;
            }

            if (isInitialDetermination && UpdateCurrentlyRelevant(element, state) && state.CurrentlyRelevant == true)
            {
                hadInitialVisibleDetermination = true;
            }
        }

        // The skipped state is updated by the next layout pass (the initial determinations by the layout of this rendering update)
        if (hadInitialVisibleDetermination || proximityChanged)
        {
            engine.SetNeedsLayout();
        }

        return hadInitialVisibleDetermination;
    }

    /// <summary>
    /// Makes the elements with <c>content-visibility: auto</c> that skip the contents containing an element relevant to the user,
    /// before it is scrolled into view (as the browsers do: the element is "activated", it is laid out and stops skipping its
    /// contents until the next determination of its proximity to the viewport). Returns whether an element was activated.
    /// </summary>
    public static bool ActivateAncestors(Element element)
    {
        var activated = false;
        var engine = element.NodeDocument.GetStyleEngine();
        for (var ancestor = HtmlElement.GetFlatTreeParent(element) as Element; ancestor is not null; ancestor = HtmlElement.GetFlatTreeParent(ancestor) as Element)
        {
            var style = engine.GetStyle(ancestor);
            if (style.Layout.ContentVisibility == ContentVisibility.Auto && SkipsContents(ancestor, style))
            {
                GetOrCreateState(ancestor).Proximity = ViewportProximity.Close;
                activated = true;
            }
        }

        return activated;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-contain-2/#update-currently-relevant-to-the-user: updates the "currently relevant to the user"
    /// flag of an element, and posts a task that fires a <c>contentvisibilityautostatechange</c> event when it changed.
    /// </summary>
    private static bool UpdateCurrentlyRelevant(Element element, ContentVisibilityAutoState state)
    {
        var relevant = IsRelevantToTheUser(element);
        if (state.CurrentlyRelevant == relevant)
            return false;

        state.CurrentlyRelevant = relevant;
        element.NodeDocument.QueueElementTask(() =>
        {
            // The element may have been disconnected or lost content-visibility: auto since the state changed
            if (element.IsConnected && ReferenceEquals(GetState(element), state))
            {
                element.FireEvent(new ContentVisibilityAutoStateChangeEvent("contentvisibilityautostatechange", new ContentVisibilityAutoStateChangeEventInit { Skipped = !relevant }));
            }
        });
        return true;
    }

    /// <summary>
    /// Forgets the state of the elements that no longer have <c>content-visibility: auto</c> or a box: "when the element becomes
    /// disconnected, the element's proximity to the viewport becomes not determined".
    /// </summary>
    public static void ForgetStates(List<Element> previous, List<ContainerBox> boxes)
    {
        if (previous.Count == 0)
            return;

        var current = new HashSet<Element>(ReferenceEqualityComparer.Instance);
        foreach (var box in boxes)
        {
            if (box.Element is { } element)
            {
                current.Add(element);
            }
        }

        foreach (var element in previous)
        {
            if (!current.Contains(element) && element.RareDataOrNull is ElementRareData rareData)
            {
                rareData.ContentVisibilityAuto = null;
            }
        }
    }

    private static ContentVisibilityAutoState GetOrCreateState(Element element) => element.ElementRareData.ContentVisibilityAuto ??= new ContentVisibilityAutoState();

    /// <summary>
    /// Returns whether the box of an element is close to the viewport (https://drafts.csswg.org/css-contain-2/#close-to-the-viewport):
    /// "its paint containment box's overflow clip edge intersects with the viewport": its border box (as in the browsers) extended
    /// to its overflow clip edge (the contents painted in its <c>overflow-clip-margin</c> can be in the viewport while the box is
    /// not) intersects the viewport expanded by 50%, after the clipping by its ancestors (the scrollports of the scroll
    /// containers are expanded by 50% too, as the scroll margin of the browsers): the intersection of an intersection observer
    /// whose root is the document (see <see cref="IntersectionGeometry.IntersectsViewport"/>). An element without fragment (e.g.
    /// in the skipped contents of another element) is far away.
    /// </summary>
    private static bool IsCloseToTheViewport(LayoutEngine engine, ContainerBox box) => IntersectionGeometry.IntersectsViewport(engine, box, ViewportMargin, includeOverflowClipEdge: true);

    private static bool IsFlatTreeInclusiveAncestor(Element ancestor, Element element)
    {
        for (Element? current = element; current is not null; current = CssomViewHelpers.GetFlatTreeParentElement(current))
        {
            if (ReferenceEquals(current, ancestor))
                return true;
        }

        return false;
    }
}
