using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// Soft navigations and interaction contentful paints (https://wicg.github.io/soft-navigations/): the interaction contexts of the
// document, the nodes modified by the interactions, the attribution of the contentful paints to the interactions, and the
// detection of the soft navigations.
public partial class Document
{
    /// <summary>The soft navigation state of the document, created by its first interaction.</summary>
    private SoftNavigationState? _softNavigation;

    /// <summary>Whether the soft navigations are detected in the document (see <see cref="WebPlatformFeatures.SoftNavigationsEnabled"/>).</summary>
    private bool IsSoftNavigationsSupported => BrowsingContext?.Options.EffectiveFeatures.SoftNavigationsEnabled == true && DefaultView is not null;

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#set-the-current-interaction-context-for-event-dispatch, at the start of the dispatch
    /// of a trusted event whose relevant global object is the window of the document: the interaction contexts record the first
    /// scroll, and the event of an interaction (with an interaction id) sets the current interaction context, until the scope is
    /// disposed (https://wicg.github.io/soft-navigations/#unset-the-current-interaction-context-after-event-dispatch).
    /// </summary>
    internal InteractionContext.Scope EnterInteractionContextForEventDispatch(Event @event, PerformanceEventTiming? timingEntry)
    {
        var interactionId = timingEntry?.InteractionId ?? 0;
        if (_softNavigation is { } state)
        {
            UpdateInteractionContextsForEvent(state, @event);
        }

        if (interactionId == 0 || !IsSoftNavigationsSupported)
            return default;

        return InteractionContext.Enter(GetOrCreateInteractionContext(timingEntry!));
    }

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#update-the-interaction-contexts-for-an-event. As in the browsers, an input does not
    /// stop the attribution of the paints to the interactions that started before it (the specification records the first input
    /// timestamp): an interaction whose content is painted after the next interaction started is still a soft navigation (WPT
    /// soft-navigation-heuristics/second-interaction-not-softnav.tentative.html, detection/tentative/racing-soft-navigations.html).
    /// </summary>
    private void UpdateInteractionContextsForEvent(SoftNavigationState state, Event @event)
    {
        if (@event.Type != "scroll" || state.Contexts.Count == 0 || DefaultView is not { } window)
            return;

        var timestamp = window.GetCurrentHighResolutionTime();
        foreach (var context in state.Contexts.Values)
        {
            if (context.FirstScrollTimestamp == 0)
            {
                context.FirstScrollTimestamp = timestamp;
            }
        }
    }

    /// <summary>https://wicg.github.io/soft-navigations/#get-or-create-the-context-for-an-interaction</summary>
    private InteractionContext GetOrCreateInteractionContext(PerformanceEventTiming timingEntry)
    {
        if (_softNavigation is null)
        {
            _softNavigation = new SoftNavigationState();

            // The paints of the document are computed from then on, and the promise reactions propagate the interaction contexts
            StartPaintTiming();
            if (DefaultView?.ScriptEngine is { } scriptEngine)
            {
                scriptEngine.PropagatesContinuationState = true;
            }
        }

        var interactionId = timingEntry.InteractionId;
        if (!_softNavigation.Contexts.TryGetValue(interactionId, out var context))
        {
            context = new InteractionContext(interactionId, this, timingEntry.StartTime);
            _softNavigation.Contexts[interactionId] = context;
        }

        return context;
    }

    /// <summary>https://wicg.github.io/soft-navigations/#get-the-current-interaction-context</summary>
    private InteractionContext? GetCurrentInteractionContext() => InteractionContext.Current is { } context && ReferenceEquals(context.Document, this) ? context : null;

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#record-a-node-as-modified-for-interaction-paint-timing: a node of the document was
    /// inserted, its <c>class</c>, <c>style</c> or resource attribute changed, or its data changed (a text node). The caller checked
    /// that there is a current interaction context (<see cref="InteractionContext.Current"/>).
    /// </summary>
    internal void RecordNodeModifiedForInteractionPaintTiming(Node targetNode)
    {
        if (_softNavigation is not { } state || GetCurrentInteractionContext() is not { } context || !PaintTimingHelpers.IsExposedForPaintTiming(targetNode, this))
            return;

        if (!ReferenceEquals(state.LastModificationContext, context))
        {
            state.LastModificationContext = context;
            state.CurrentModificationGenerationId++;
        }

        if (state.MarkedNodeStates.TryGetValue(targetNode, out var previousNodeState) && previousNodeState.ModificationId == state.CurrentModificationGenerationId)
            return;

        state.MarkedNodeStates[targetNode] = new InteractionPaintTimingNodeState(context, state.CurrentModificationGenerationId);
        state.IsDirty = true;
    }

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#process-a-same-document-commit: the URL of the document changed by a same-document
    /// navigation (<c>history.pushState()</c>, <c>history.replaceState()</c>, a fragment navigation, a traversal, a navigation
    /// intercepted by the Navigation API).
    /// </summary>
    internal void ProcessSameDocumentCommit(UrlRecord oldUrl, UrlRecord newUrl, NavigationType navigationType)
    {
        if (_softNavigation is not { } state || GetCurrentInteractionContext() is not { } context)
            return;

        var url = newUrl.Serialize();
        if (url == oldUrl.Serialize())
            return;

        context.LastUrl = url;
        if (context.FirstUrl is null)
        {
            context.FirstUrl = url;
            context.NavigationType = navigationType;
        }

        state.ActiveSoftNavigationCandidate = context;
        EvaluateSoftNavigationEmission(state, context);
    }

    /// <summary>https://wicg.github.io/soft-navigations/#evaluate-soft-navigation-emission</summary>
    private void EvaluateSoftNavigationEmission(SoftNavigationState state, InteractionContext context)
    {
        if (context.Emitted || !ReferenceEquals(state.ActiveSoftNavigationCandidate, context) || context.FirstUrl is null || context.FirstContentfulPaint is not { } firstPaint || DefaultView is not { } window)
            return;

        // https://wicg.github.io/soft-navigations/#create-a-soft-navigation-entry and https://wicg.github.io/soft-navigations/#emit-a-soft-navigation-entry
        var entry = new PerformanceSoftNavigation(window, context, firstPaint.PaintTimingInfo);
        window.Performance.IncrementNavigationId();
        window.Performance.QueueSoftNavigationEntry(entry);
        context.Emitted = true;
    }

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#report-interaction-contentful-paints-and-soft-navigations, run by "mark paint
    /// timing" after the largest contentful paint: the painted images and texts (the ones painted for the first time, and the
    /// content of the elements whose paint tracking was reset by an interaction) are grouped by the interaction they are attributed
    /// to, and the largest one of each interaction that is larger than its current candidate is reported.
    /// </summary>
    private void ReportInteractionContentfulPaints(PaintTimingState state, LayoutEngine engine, Window window, PaintTimingInfo paintTimingInfo, List<(PendingImageRecord Record, PhysicalRect PaintedRect)> paintedImages, List<Element> paintedTextElements, ContentfulPaintScan scan)
    {
        var softNavigation = _softNavigation!;
        UpdateInteractionContextForDocument(softNavigation, engine);
        if (softNavigation.PropagatedNodeStates.Count == 0)
            return;

        Dictionary<InteractionContext, (List<(PendingImageRecord Record, PhysicalRect PaintedRect)> Images, List<Element> Texts)>? candidates = null;
        foreach (var image in paintedImages)
        {
            if (GetPaintAttributionInteractionContext(softNavigation, image.Record.Element) is { } context)
            {
                GetCandidateSets(context).Images.Add(image);
            }
        }

        foreach (var element in paintedTextElements)
        {
            if (GetPaintAttributionInteractionContext(softNavigation, element) is { } context)
            {
                GetCandidateSets(context).Texts.Add(element);
            }
        }

        // The elements whose paint tracking was reset are candidates again when they paint their content (an image or a text)
        if (softNavigation.ResetElements.Count > 0)
        {
            foreach (var element in softNavigation.ResetElements.ToArray())
            {
                if (!element.IsConnected || GetPaintAttributionInteractionContext(softNavigation, element) is not { } context)
                {
                    softNavigation.ResetElements.Remove(element);
                    continue;
                }

                var painted = false;
                if (scan.Images.TryGetValue(element, out var images))
                {
                    foreach (var record in state.PaintedImageRecords)
                    {
                        if (ReferenceEquals(record.Element, element) && GetPaintedRect(images, record.Url) is { } paintedRect)
                        {
                            GetCandidateSets(context).Images.Add((record, paintedRect));
                            painted = true;
                        }
                    }

                    foreach (var (url, paintedRect) in images)
                    {
                        if (url.Length > 0 && state.CssImages.TryGetValue(url, out var load))
                        {
                            GetCandidateSets(context).Images.Add((new PendingImageRecord(element, url, load.NaturalWidth, load.NaturalHeight, load.ContentLength, load.LoadTime, IsCssImage: true), paintedRect));
                            painted = true;
                        }
                    }
                }

                if (HasVisibleText(state, scan, element))
                {
                    GetCandidateSets(context).Texts.Add(element);
                    painted = true;
                }

                if (painted)
                {
                    softNavigation.ResetElements.Remove(element);
                }
            }
        }

        if (candidates is null)
            return;

        foreach (var (context, candidateSets) in candidates)
        {
            var newCandidate = ComputeNewLargestContentfulPaintCandidate(state, engine, candidateSets.Images, candidateSets.Texts, scan, context.CurrentCandidateSize);
            if (newCandidate is not null)
            {
                ProcessNewAttributedContentfulPaintCandidate(softNavigation, window, newCandidate, context, paintTimingInfo);
            }
        }

        (List<(PendingImageRecord Record, PhysicalRect PaintedRect)> Images, List<Element> Texts) GetCandidateSets(InteractionContext context)
        {
            candidates ??= [];
            if (!candidates.TryGetValue(context, out var sets))
            {
                sets = ([], []);
                candidates[context] = sets;
            }

            return sets;
        }
    }

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#process-a-new-attributed-contentful-paint-candidate. The soft navigation is
    /// evaluated before the entry is queued: the interaction contentful paint that confirms a soft navigation belongs to it
    /// (its <see cref="PerformanceEntry.NavigationId"/> is the one of the soft navigation).
    /// </summary>
    private void ProcessNewAttributedContentfulPaintCandidate(SoftNavigationState softNavigation, Window window, LargestContentfulPaintCandidate candidate, InteractionContext context, PaintTimingInfo paintTimingInfo)
    {
        // The paints after a scroll are not attributed to the interaction
        if (context.FirstScrollTimestamp > 0 && paintTimingInfo.DefaultPaintTimestamp > context.FirstScrollTimestamp)
            return;

        // https://w3c.github.io/largest-contentful-paint/#create-a-largestcontentfulpaint-entry
        var lcpEntry = new LargestContentfulPaint(window, paintTimingInfo, (uint)Math.Min(uint.MaxValue, Math.Round(candidate.Size, MidpointRounding.AwayFromZero)), candidate.LoadTime, candidate.Element.GetAttributeValue("id") ?? "", candidate.Url ?? "", candidate.Element);
        var entry = new InteractionContentfulPaint(window, context, lcpEntry, paintTimingInfo);
        context.FirstContentfulPaint ??= entry;
        context.LargestContentfulPaint = entry;
        context.CurrentCandidateSize = candidate.Size;
        EvaluateSoftNavigationEmission(softNavigation, context);
        window.Performance.QueueInteractionContentfulPaintEntry(entry);
    }

    /// <summary>https://wicg.github.io/soft-navigations/#get-the-paint-attribution-interaction-context</summary>
    private static InteractionContext? GetPaintAttributionInteractionContext(SoftNavigationState state, Element element)
        => state.PropagatedNodeStates.TryGetValue(element, out var nodeState) ? nodeState.Context : null;

    /// <summary>
    /// https://wicg.github.io/soft-navigations/#update-the-interaction-context-for-a-document-and-its-descendants: the state of the
    /// modified nodes is propagated to their descendants, and recorded on the elements their paints are attributed to (the element
    /// of a text node is the one that owns it for the paint timing). The states of the nodes that are no longer in the document
    /// are removed.
    /// </summary>
    private void UpdateInteractionContextForDocument(SoftNavigationState state, LayoutEngine engine)
    {
        if (!state.IsDirty)
            return;

        state.IsDirty = false;
        var stack = new Stack<(Node Node, InteractionPaintTimingNodeState? Inherited)>();
        stack.Push((this, null));
        while (stack.TryPop(out var item))
        {
            var (node, inherited) = item;

            // https://wicg.github.io/soft-navigations/#update-the-interaction-context-for-a-node-and-its-descendants
            if (state.MarkedNodeStates.TryGetValue(node, out var nodeState))
            {
                if (nodeState.IsMoreRecentThan(inherited))
                {
                    inherited = nodeState;
                    if (node is Text)
                    {
                        state.MarkedNodeStates.Remove(node);
                    }
                }
                else
                {
                    state.MarkedNodeStates.Remove(node);
                }
            }

            if (inherited is not null && (node is Text ? GetTextOwner(engine, node) : node as Element) is { } attributionElement)
            {
                state.PropagatedNodeStates.TryGetValue(attributionElement, out var previousPropagatedState);
                if (inherited.IsMoreRecentThan(previousPropagatedState))
                {
                    state.PropagatedNodeStates[attributionElement] = inherited;

                    // https://wicg.github.io/soft-navigations/#reset-paint-tracking
                    if (previousPropagatedState is null || !ReferenceEquals(previousPropagatedState.Context, inherited.Context))
                    {
                        state.ResetElements.Add(attributionElement);
                    }
                }
            }

            for (var child = node.LastChild; child is not null; child = child.PreviousSibling)
            {
                stack.Push((child, inherited));
            }
        }

        RemoveDisconnected(state.MarkedNodeStates);
        RemoveDisconnected(state.PropagatedNodeStates);

        static void RemoveDisconnected<TNode>(Dictionary<TNode, InteractionPaintTimingNodeState> states)
            where TNode : Node
        {
            List<TNode>? removed = null;
            foreach (var (node, _) in states)
            {
                if (!node.IsConnected)
                {
                    (removed ??= []).Add(node);
                }
            }

            if (removed is not null)
            {
                foreach (var node in removed)
                {
                    states.Remove(node);
                }
            }
        }
    }

    /// <summary>An InteractionPaintTimingNodeState (https://wicg.github.io/soft-navigations/#interactionpainttimingnodestate).</summary>
    private sealed record InteractionPaintTimingNodeState(InteractionContext Context, ulong ModificationId)
    {
        /// <summary>https://wicg.github.io/soft-navigations/#more-recent-than</summary>
        public bool IsMoreRecentThan(InteractionPaintTimingNodeState? other) => other is null || ModificationId > other.ModificationId;
    }

    /// <summary>The soft navigation state of a document (https://wicg.github.io/soft-navigations/#sec-html-document).</summary>
    private sealed class SoftNavigationState
    {
        /// <summary>https://wicg.github.io/soft-navigations/#document-interaction-id-to-interaction-context</summary>
        public Dictionary<ulong, InteractionContext> Contexts { get; } = [];

        /// <summary>https://wicg.github.io/soft-navigations/#document-active-soft-navigation-candidate</summary>
        public InteractionContext? ActiveSoftNavigationCandidate { get; set; }

        /// <summary>https://wicg.github.io/soft-navigations/#document-current-modification-generation-id</summary>
        public ulong CurrentModificationGenerationId { get; set; }

        /// <summary>https://wicg.github.io/soft-navigations/#document-last-modification-context</summary>
        public InteractionContext? LastModificationContext { get; set; }

        /// <summary>https://wicg.github.io/soft-navigations/#document-marked-node-state</summary>
        public Dictionary<Node, InteractionPaintTimingNodeState> MarkedNodeStates { get; } = [];

        /// <summary>https://wicg.github.io/soft-navigations/#document-propagated-node-state</summary>
        public Dictionary<Element, InteractionPaintTimingNodeState> PropagatedNodeStates { get; } = [];

        /// <summary>https://wicg.github.io/soft-navigations/#document-is-interaction-paint-timing-dirty</summary>
        public bool IsDirty { get; set; }

        /// <summary>The elements whose paint tracking was reset (https://wicg.github.io/soft-navigations/#reset-paint-tracking): their next paint is a candidate of their interaction.</summary>
        public HashSet<Element> ResetElements { get; } = [];
    }
}
