using Meziantou.Framework.WebPlatform.Animations;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class Document
{
    private DocumentAnimations? _animations;

    /// <summary>The animations of the document, created on first use.</summary>
    internal DocumentAnimations Animations => _animations ??= new DocumentAnimations(this);

    /// <summary>The animations of the document, or null when nothing was animated.</summary>
    internal DocumentAnimations? AnimationsOrNull => _animations;

    private Svg.Animations.SmilAnimations? _smil;

    /// <summary>The SMIL animations of the document (https://svgwg.org/specs/animations/), created on first use.</summary>
    internal Svg.Animations.SmilAnimations Smil => _smil ??= new Svg.Animations.SmilAnimations(this);

    /// <summary>The SMIL animations of the document, or null when it never had an animation element nor a seeked timeline.</summary>
    internal Svg.Animations.SmilAnimations? SmilOrNull => _smil;

    /// <summary>Whether the SMIL animations run (see <see cref="WebPlatformFeatures.SvgAnimationsEnabled"/>).</summary>
    internal bool SmilEnabled => (BrowsingContext ?? OriginDocument?.BrowsingContext)?.Options.EffectiveFeatures.SvgAnimationsEnabled ?? true;

    /// <summary>Whether the load event of the document was fired: the timelines of the SMIL animations begin then.</summary>
    internal bool LoadEventFired { get; private set; }

    /// <summary>Starts the timelines of the SMIL animations, after the load event is fired (https://svgwg.org/specs/animations/#TimingAttributes).</summary>
    internal void OnLoadEventFired()
    {
        LoadEventFired = true;
        LoadEventTime = RelevantWindow?.GetCurrentHighResolutionTime();
        _smil?.Start();
    }

    /// <summary>The time of the clock of the document when its load event was fired, in milliseconds.</summary>
    internal double? LoadEventTime { get; private set; }

    /// <summary>Gets the default document timeline (https://drafts.csswg.org/web-animations-1/#dom-document-timeline).</summary>
    [IdlMember("timeline", "https://drafts.csswg.org/web-animations-1/#dom-document-timeline")]
    public DocumentTimeline Timeline => Animations.DefaultTimeline;

    /// <summary>
    /// Returns the relevant animations of the elements of the document (not of its shadow trees), sorted by their composite
    /// order (https://drafts.csswg.org/web-animations-1/#dom-documentorshadowroot-getanimations).
    /// </summary>
    [IdlMember("getAnimations", "https://drafts.csswg.org/web-animations-1/#dom-documentorshadowroot-getanimations")]
    public IReadOnlyList<Animation> GetAnimations()
    {
        FlushAnimationStyles();
        return _animations?.GetAnimations(this, shadowIncluding: false) ?? [];
    }

    private bool _animationStylesUpdateRequested;

    /// <summary>
    /// Schedules the update of the CSS animations and transitions at the next rendering opportunity, after a change of the tree,
    /// of the state of an element or of a style sheet.
    /// </summary>
    internal void RequestAnimationStylesUpdate()
    {
        if (_animationStylesUpdateRequested || BrowsingContext is null || EventLoop is not { } eventLoop)
            return;

        _animationStylesUpdateRequested = true;
        eventLoop.RequestAnimationStylesUpdate(this);
    }

    /// <summary>Whether a style attribute of the document declared an animation or a transition property.</summary>
    internal bool InlineStylesDeclareAnimations { get; set; }

    /// <summary>Whether a style attribute of the document declared <c>content-visibility</c> or <c>contain-intrinsic-*</c> (see <see cref="Css.Cascade.StyleEngine.MayHaveContentVisibility"/>).</summary>
    internal bool InlineStylesDeclareContentVisibility { get; set; }

    /// <summary>Whether a style attribute of the document declared <c>position-try-fallbacks</c> (see <see cref="Css.Cascade.StyleEngine.MayHavePositionTryFallbacks"/>).</summary>
    internal bool InlineStylesDeclarePositionTryFallbacks { get; set; }

    /// <summary>
    /// Updates the styles of the elements of the document, which creates, updates and cancels their CSS animations and
    /// transitions (https://drafts.csswg.org/css-animations-1/#animations). Nothing is done when the styles did not change since
    /// the last update, or when no style declares an animation or a transition.
    /// </summary>
    internal void FlushAnimationStyles()
    {
        _animationStylesUpdateRequested = false;
        if (BrowsingContext is null || !IsFullyActive)
            return;

        var animations = _animations;
        var engine = GetStyleEngine();
        if (animations?.HasDeclarativeAnimations != true && !engine.MayHaveDeclarativeAnimations())
            return;

        if (!engine.BeginAnimationFlush())
        {
            UpdateLayoutForContainerQueries(engine);
            return;
        }

        var pseudoElements = new List<string>(3);
        foreach (var pseudoElement in (ReadOnlySpan<string>)["marker", "before", "after"])
        {
            if (engine.MayHavePseudoElementRules(pseudoElement))
            {
                pseudoElements.Add(pseudoElement);
            }
        }

        foreach (var node in ShadowIncludingInclusiveDescendants())
        {
            if (node is not Element element)
                continue;

            engine.GetStyle(element);
            foreach (var pseudoElement in pseudoElements)
            {
                engine.GetStyle(element, pseudoElement);
            }
        }

        ComputeViewTransitionPseudoElementStyles();
        _animations?.CancelDisconnectedDeclarativeAnimations();
        _animations?.UpdateCssNamedTimelines(engine);
        engine.PruneBeforeChangeStyles();
        UpdateLayoutForContainerQueries(engine);
    }

    /// <summary>
    /// Updates the layout when the styles depend on the sizes of the size containers: the layout computes the styles of their
    /// descendants again with the sizes of the containers, and so their animations (https://drafts.csswg.org/css-conditional-5/#animated-containers).
    /// </summary>
    private void UpdateLayoutForContainerQueries(Css.Cascade.StyleEngine engine)
    {
        if (engine.HasQueriedContainerSizes && GetLayoutEngine() is { NeedsLayout: true } layoutEngine)
        {
            layoutEngine.EnsureLayout();
        }
    }
}

public partial class ShadowRoot
{
    /// <summary>
    /// Returns the relevant animations of the elements of the shadow tree, sorted by their composite order
    /// (https://drafts.csswg.org/web-animations-1/#dom-documentorshadowroot-getanimations).
    /// </summary>
    [IdlMember("getAnimations", "https://drafts.csswg.org/web-animations-1/#dom-documentorshadowroot-getanimations")]
    public IReadOnlyList<Animation> GetAnimations()
    {
        var document = NodeDocument;
        document.FlushAnimationStyles();
        return document.AnimationsOrNull?.GetAnimations(this, shadowIncluding: false) ?? [];
    }
}
