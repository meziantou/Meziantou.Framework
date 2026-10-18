using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Infra;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The lazy loading of the <c>img</c> and <c>iframe</c> elements (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-loading-attributes):
/// an element whose <c>loading</c> attribute is <c>lazy</c> stores the rest of its fetch or navigation as its lazy load
/// resumption steps, and observes its intersection with the viewport with the lazy load intersection observer of its document,
/// whose callback runs them when it intersects (or when the attribute changes to <c>eager</c>).
/// </summary>
/// <remarks>
/// The lazy load intersection observer is an <see cref="IntersectionObserver"/> of the user agent (not exposed to the scripts)
/// whose scroll margin is the lazy load scroll margin: the viewport and the scrollports are expanded by
/// <see cref="LazyLoadScrollMargin"/>, so the resource is fetched shortly before it is scrolled into view. The lazy loading is
/// disabled with <see cref="WebPlatformFeatures.IntersectionObserverEnabled"/>.
/// </remarks>
internal static class LazyLoading
{
    /// <summary>
    /// The lazy load scroll margin (https://html.spec.whatwg.org/multipage/urls-and-fetching.html#lazy-load-scroll-margin), an
    /// implementation-defined value: 1250 CSS pixels in every direction, the distance of the browsers for a fast connection.
    /// </summary>
    public const double LazyLoadScrollMargin = 1250;

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/urls-and-fetching.html#will-lazy-load-element-steps: false when scripting is
    /// disabled for the element (or the intersection observers are disabled), else whether its <c>loading</c> attribute is in
    /// the Lazy state.
    /// </summary>
    public static bool WillLazyLoad(Element element)
    {
        var document = element.NodeDocument;
        if (document.BrowsingContext is not { Options: { ScriptingEnabled: true } options } || !document.ScriptingEnabled || !options.EffectiveFeatures.IntersectionObserverEnabled)
            return false;

        return IsLazy(element.GetAttributeValue("loading"));
    }

    /// <summary>Whether a value of a lazy loading attribute is in the Lazy state (the missing and invalid values are in the Eager state).</summary>
    public static bool IsLazy(string? value) => value is not null && InfraAscii.EqualsIgnoreAsciiCase(value, "lazy");

    /// <summary>Sets the lazy load resumption steps of an element, and starts intersection-observing it.</summary>
    public static void Defer(Element element, Action resumptionSteps)
    {
        element.ElementRareData.LazyLoadResumptionSteps = resumptionSteps;
        StartIntersectionObserving(element);
    }

    /// <summary>Clears the lazy load resumption steps of an element and stops intersection-observing it (e.g. a new fetch or navigation starts).</summary>
    public static void ClearResumptionSteps(Element element)
    {
        if (element.RareDataOrNull is ElementRareData { LazyLoadResumptionSteps: not null } rareData)
        {
            rareData.LazyLoadResumptionSteps = null;
            StopIntersectionObserving(element);
        }
    }

    /// <summary>
    /// The steps run when the lazy loading attribute of an img or iframe element changes to the Eager state
    /// (https://html.spec.whatwg.org/multipage/embedded-content.html#attr-img-loading): its lazy load resumption steps, if any,
    /// are run.
    /// </summary>
    public static void OnLoadingAttributeChanged(Element element, string? oldValue, string? value)
    {
        if (!IsLazy(oldValue) || IsLazy(value))
            return;

        // The poster lazy load resumption steps of a video element are run by the element (HtmlVideoElement.OnLoadingAttributeChanged)
        if (element.NodeDocument.LazyLoadIntersectionObserver is { } observer)
        {
            observer.Unobserve(element);
        }

        if (element.RareDataOrNull is not ElementRareData { LazyLoadResumptionSteps: { } resumptionSteps } rareData)
            return;

        rareData.LazyLoadResumptionSteps = null;
        resumptionSteps();
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#start-intersection-observing-a-lazy-loading-element</summary>
    public static void StartIntersectionObserving(Element element)
    {
        var document = element.NodeDocument;
        document.LazyLoadIntersectionObserver ??= new IntersectionObserver(document, OnIntersection, IntersectionMargin.FromPixels(LazyLoadScrollMargin));
        document.LazyLoadIntersectionObserver.Observe(element);
    }

    /// <summary>https://html.spec.whatwg.org/multipage/urls-and-fetching.html#stop-intersection-observing-a-lazy-loading-element</summary>
    public static void StopIntersectionObserving(Element element)
    {
        // A video element stays observed while the fetch of its poster frame is deferred
        if (element is HtmlVideoElement { HasPosterLazyLoadResumptionSteps: true })
            return;

        element.NodeDocument.LazyLoadIntersectionObserver?.Unobserve(element);
    }

    /// <summary>The callback of the lazy load intersection observer: runs the lazy load resumption steps of the elements that intersect.</summary>
    private static void OnIntersection(IReadOnlyList<IntersectionObserverEntry> entries, IntersectionObserver observer)
    {
        foreach (var entry in entries)
        {
            if (!entry.IsIntersecting)
                continue;

            // The poster lazy load resumption steps of a video element (https://html.spec.whatwg.org/multipage/media.html#poster-lazy-load-resumption-steps)
            var video = entry.Target as HtmlVideoElement;
            var rareData = entry.Target.RareDataOrNull as ElementRareData;
            var resumptionSteps = rareData?.LazyLoadResumptionSteps;
            if (resumptionSteps is null && video is not { HasPosterLazyLoadResumptionSteps: true })
                continue;

            observer.Unobserve(entry.Target);
            if (resumptionSteps is not null)
            {
                rareData!.LazyLoadResumptionSteps = null;
                resumptionSteps();
            }

            video?.RunPosterLazyLoadResumptionSteps();
        }
    }
}
