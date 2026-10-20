using Meziantou.Framework.WebPlatform.Html;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

// Speculative loading (https://html.spec.whatwg.org/multipage/speculative-loading.html): the speculation rule sets of the
// document, and the candidates that they select ("consider speculative loads").
public partial class Document
{
    /// <summary>How long the pointer stays over a hyperlink before its "moderate" candidates are enacted.</summary>
    private static readonly TimeSpan ModerateHoverDelay = TimeSpan.FromMilliseconds(200);

    private bool _considerSpeculativeLoadsMicrotaskQueued;
    private List<(UrlRecord Url, SpeculationEagerness Eagerness)>? _linkInterests;
    private Element? _hoveredHyperlink;
    private List<HtmlScriptElement>? _speculationRulesScripts;
    private string? _speculationRulesBaseUrl;
    private int _speculationRulesDomVersion = -1;

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#document-sr-sets</summary>
    internal List<SpeculationRuleSet>? SpeculationRuleSets { get; private set; }

    /// <summary>https://wicg.github.io/nav-speculation/prefetch.html#document-prefetch-records (the prerender records included).</summary>
    internal List<PrefetchRecord>? PrefetchRecords { get; private set; }

    /// <summary>Whether a speculation rule set of the document has a document rule: the links of the document are candidates.</summary>
    private bool HasDocumentSpeculationRules
    {
        get
        {
            if (SpeculationRuleSets is not { } ruleSets)
                return false;

            foreach (var ruleSet in ruleSets)
            {
                if (ruleSet.PrefetchRules.Exists(static rule => rule.Predicate is not null) || ruleSet.PrerenderRules.Exists(static rule => rule.Predicate is not null))
                    return true;
            }

            return false;
        }
    }

    /// <summary>The script elements whose speculation rules are registered in the document.</summary>
    internal void AddSpeculationRulesScript(HtmlScriptElement script)
    {
        _speculationRulesBaseUrl ??= DocumentBaseUrl.Serialize();
        (_speculationRulesScripts ??= []).Add(script);
    }

    internal void RemoveSpeculationRulesScript(HtmlScriptElement script) => _speculationRulesScripts?.Remove(script);

    internal void AddSpeculationRuleSet(SpeculationRuleSet ruleSet)
    {
        (SpeculationRuleSets ??= []).Add(ruleSet);
        ConsiderSpeculativeLoads();
    }

    internal void RemoveSpeculationRuleSet(SpeculationRuleSet ruleSet)
    {
        if (SpeculationRuleSets?.Remove(ruleSet) == true)
        {
            ConsiderSpeculativeLoads();
        }
    }

    /// <summary>
    /// A hyperlink of the document changed (it was inserted or removed, or its <c>href</c>, <c>referrerpolicy</c> or
    /// <c>rel</c> attribute changed), or the base URL of the document changed: the links that the document rules select are
    /// considered again.
    /// </summary>
    internal void InvalidateSpeculationLinks()
    {
        if (SpeculationRuleSets is { Count: > 0 })
        {
            ConsiderSpeculativeLoads();
        }
    }

    /// <summary>
    /// The rendering of the document was updated: "when style recalculation would cause selector matching results to change,
    /// the user agent must consider speculative loads" (the selectors of the document rules, and whether the links are
    /// rendered). The candidates are computed again by each rendering update of a document that has document rules.
    /// </summary>
    internal void ConsiderSpeculativeLoadsForRendering()
    {
        if (SpeculationRuleSets is not { Count: > 0 } || !IsFullyActive)
            return;

        // The base URL of the document is not known to have changed until it is read (e.g. a base element was inserted)
        if (_speculationRulesDomVersion != DomVersion || HasDocumentSpeculationRules)
        {
            InnerConsiderSpeculativeLoads();
        }
    }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#consider-speculative-loads</summary>
    internal void ConsiderSpeculativeLoads()
    {
        if (BrowsingContext is not { Parent: null } || _considerSpeculativeLoadsMicrotaskQueued || EnvironmentEventLoop is not { } eventLoop)
            return;

        _considerSpeculativeLoadsMicrotaskQueued = true;
        eventLoop.QueueMicrotask(() =>
        {
            _considerSpeculativeLoadsMicrotaskQueued = false;
            InnerConsiderSpeculativeLoads();

            // The styles and the layout of a document with speculation rules are kept up to date by the rendering updates,
            // which consider the speculative loads again
            if (SpeculationRuleSets is { Count: > 0 } && IsFullyActive)
            {
                GetLayoutEngine().RequestRenderingUpdates();
            }
        });
    }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#inner-consider-speculative-loads-steps</summary>
    private void InnerConsiderSpeculativeLoads()
    {
        var interests = _linkInterests;
        _linkInterests = null;
        if (!IsFullyActive || BrowsingContext is not { Parent: null } navigable || !navigable.Options.EffectiveFeatures.SpeculationRulesEnabled)
            return;

        // https://html.spec.whatwg.org/multipage/urls-and-fetching.html#dynamic-changes-to-base-urls: the rules of the script
        // elements are parsed again when the document base URL changed
        _speculationRulesDomVersion = DomVersion;
        if (_speculationRulesScripts is { Count: > 0 } scripts && DocumentBaseUrl.Serialize() is var baseUrl && baseUrl != _speculationRulesBaseUrl)
        {
            _speculationRulesBaseUrl = baseUrl;
            foreach (var script in scripts.ToArray())
            {
                script.UpdateSpeculationRules();
            }
        }

        var candidates = new List<SpeculativeLoadCandidate>();
        if (SpeculationRuleSets is { } ruleSets)
        {
            // The prerender rules are treated as prefetch rules when the navigable cannot prerender
            var canPrerender = NavigationalPrefetch.CanPrerender(this);
            List<(Element Link, UrlRecord Url)>? links = null;
            foreach (var ruleSet in ruleSets)
            {
                AddCandidates(ruleSet.PrefetchRules, isPrerender: false);
                AddCandidates(ruleSet.PrerenderRules, isPrerender: canPrerender);
            }

            void AddCandidates(List<SpeculationRule> rules, bool isPrerender)
            {
                foreach (var rule in rules)
                {
                    foreach (var url in rule.Urls)
                    {
                        candidates.Add(new SpeculativeLoadCandidate(url, rule, rule.ReferrerPolicy, isPrerender));
                    }

                    if (rule.Predicate is { } predicate)
                    {
                        links ??= FindSpeculationLinks();
                        foreach (var (link, url) in links)
                        {
                            if (predicate.Matches(link, url))
                            {
                                // https://html.spec.whatwg.org/multipage/speculative-loading.html#compute-a-speculative-load-referrer-policy
                                var referrerPolicy = rule.ReferrerPolicy.Length > 0 ? rule.ReferrerPolicy : HyperlinkElementUtils.GetReferrerPolicy(link);
                                candidates.Add(new SpeculativeLoadCandidate(url, rule, referrerPolicy, isPrerender));
                            }
                        }
                    }
                }
            }
        }

        if (PrefetchRecords is { } records)
        {
            foreach (var record in records.ToArray())
            {
                if (!candidates.Exists(record.IsStillBeingSpeculated))
                {
                    NavigationalPrefetch.CancelAndDiscard(this, record);
                }
            }
        }

        // The groups of redundant candidates: the candidates of a group that are at least as eager as its first candidate
        var groups = new List<List<SpeculativeLoadCandidate>>();
        foreach (var candidate in candidates)
        {
            var group = new List<SpeculativeLoadCandidate> { candidate };
            foreach (var other in candidates)
            {
                if (!ReferenceEquals(other, candidate) && other.IsPrerender == candidate.IsPrerender && other.Eagerness <= candidate.Eagerness && candidate.IsRedundantWith(other))
                {
                    group.Add(other);
                }
            }

            if (!groups.Exists(existing => existing.Count == group.Count && existing.TrueForAll(group.Contains)))
            {
                groups.Add(group);
            }
        }

        foreach (var group in groups)
        {
            var candidate = group[0];
            if (!ShouldEnact(candidate, interests))
                continue;

            NavigationalPrefetch.Start(this, new PrefetchRecord(candidate, CollectTags(group)));
        }
    }

    /// <summary>
    /// Whether the user agent enacts a candidate, according to its eagerness: an "immediate" candidate at once, an "eager"
    /// candidate when the pointer moves over a link to its URL, a "moderate" candidate when the pointer stays over the link
    /// for 200 ms, and a "conservative" candidate when a button of the pointer is pressed on the link.
    /// </summary>
    private static bool ShouldEnact(SpeculativeLoadCandidate candidate, List<(UrlRecord Url, SpeculationEagerness Eagerness)>? interests)
    {
        if (candidate.Eagerness == SpeculationEagerness.Immediate)
            return true;

        if (interests is null)
            return false;

        foreach (var (url, eagerness) in interests)
        {
            if (eagerness >= candidate.Eagerness && candidate.Rule.NoVarySearchHint.AreEquivalent(candidate.Url, url))
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#collect-tags-from-speculative-load-candidates</summary>
    private static List<string?> CollectTags(List<SpeculativeLoadCandidate> candidates)
    {
        var tags = new List<string?>();
        foreach (var candidate in candidates)
        {
            foreach (var tag in candidate.Rule.Tags)
            {
                if (!tags.Contains(tag))
                {
                    tags.Add(tag);
                }
            }
        }

        tags.Sort(static (a, b) => a is null ? (b is null ? 0 : -1) : b is null ? 1 : string.CompareOrdinal(a, b));
        return tags;
    }

    /// <summary>
    /// The links of https://html.spec.whatwg.org/multipage/speculative-loading.html#find-matching-links: the <c>a</c> and
    /// <c>area</c> elements with an <c>href</c> attribute that are being rendered, whose URL is an HTTP(S) URL.
    /// </summary>
    private List<(Element Link, UrlRecord Url)> FindSpeculationLinks()
    {
        var links = new List<(Element, UrlRecord)>();
        foreach (var node in ShadowIncludingInclusiveDescendants())
        {
            if (node is not Element element || element is not (HtmlAnchorElement or HtmlAreaElement) || !element.HasAttribute("href"))
                continue;

            if (HyperlinkElementUtils.GetUrl(element) is not { Scheme: "http" or "https" } url || !FocusModel.IsBeingRendered(element))
                continue;

            links.Add((element, url));
        }

        return links;
    }

    /// <summary>
    /// The pointer of the user moved over <paramref name="target"/> (or out of the document, when it is null): the user
    /// indicates interest in the hyperlink that contains it, for the eagerness heuristics.
    /// </summary>
    internal void OnPointerOverForSpeculation(Node? target)
    {
        if (SpeculationRuleSets is not { Count: > 0 })
            return;

        var link = FindHyperlink(target);
        if (ReferenceEquals(link, _hoveredHyperlink))
            return;

        _hoveredHyperlink = link;
        if (link is null || HyperlinkElementUtils.GetUrl(link) is not { } url)
            return;

        AddLinkInterest(url, SpeculationEagerness.Eager);
        EnvironmentEventLoop?.QueueTimer(ModerateHoverDelay, () =>
        {
            if (ReferenceEquals(_hoveredHyperlink, link) && link.IsConnected && HyperlinkElementUtils.GetUrl(link) is { } currentUrl)
            {
                AddLinkInterest(currentUrl, SpeculationEagerness.Moderate);
            }
        });
    }

    /// <summary>A button of the pointer of the user was pressed on <paramref name="target"/>: the "conservative" candidates of its hyperlink are enacted.</summary>
    internal void OnPointerDownForSpeculation(Node? target)
    {
        if (SpeculationRuleSets is not { Count: > 0 } || FindHyperlink(target) is not { } link || HyperlinkElementUtils.GetUrl(link) is not { } url)
            return;

        AddLinkInterest(url, SpeculationEagerness.Conservative);
    }

    private void AddLinkInterest(UrlRecord url, SpeculationEagerness eagerness)
    {
        (_linkInterests ??= []).Add((url, eagerness));
        ConsiderSpeculativeLoads();
    }

    private static Element? FindHyperlink(Node? target)
    {
        for (var node = target; node is not null; node = node.ParentNode ?? (node as ShadowRoot)?.Host)
        {
            if (node is Element element and (HtmlAnchorElement or HtmlAreaElement) && element.HasAttribute("href"))
                return element;
        }

        return null;
    }

    internal void AddPrefetchRecord(PrefetchRecord record) => (PrefetchRecords ??= []).Add(record);

    private List<Action>? _postPrerenderingActivationSteps;

    /// <summary>Whether the response of the document had <c>Supports-Loading-Mode: prerender-cross-origin-frames</c>: its cross-origin frames are loaded while it is prerendered.</summary>
    internal bool SupportsPrerenderingCrossOriginFrames { get; set; }

    /// <summary>
    /// Gets whether the document is prerendering: it is in a prerendering traversable, which is not shown, until a navigation
    /// activates it (https://wicg.github.io/nav-speculation/prerendering.html#dom-document-prerendering).
    /// </summary>
    /// <seealso href="https://wicg.github.io/nav-speculation/prerendering.html#dom-document-prerendering"/>
    [IdlMember("prerendering", "https://wicg.github.io/nav-speculation/prerendering.html#dom-document-prerendering")]
    public bool Prerendering => BrowsingContext is { } browsingContext && ReferenceEquals(browsingContext.Document, this) && browsingContext.Top.IsPrerendering;

    /// <summary>Gets or sets the event handler of the <c>prerenderingchange</c> event (the <c>onprerenderingchange</c> event handler attribute).</summary>
    /// <seealso href="https://wicg.github.io/nav-speculation/prerendering.html#dom-document-onprerenderingchange"/>
    [IdlMember("onprerenderingchange", "https://wicg.github.io/nav-speculation/prerendering.html#dom-document-onprerenderingchange")]
    public Action<Event>? OnPrerenderingChange
    {
        get => GetEventHandler("prerenderingchange");
        set => SetEventHandler("prerenderingchange", value);
    }

    /// <summary>
    /// Runs <paramref name="steps"/> now, or when the prerendering document is activated
    /// (https://wicg.github.io/nav-speculation/prerendering.html#delay-while-prerendering: the post-prerendering activation steps list).
    /// </summary>
    internal void RunAfterPrerendering(Action steps)
    {
        if (Prerendering)
        {
            (_postPrerenderingActivationSteps ??= []).Add(steps);
        }
        else
        {
            steps();
        }
    }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#delay-while-prerendering, for an operation that returns a
    /// promise: in a prerendering document, the operation starts when the document is activated.
    /// </summary>
    internal static Task<T> DelayWhilePrerendering<T>(GlobalScope? global, Func<Task<T>> operation)
    {
        if (global is not Window { Document: { Prerendering: true } document })
            return operation();

        var completion = new TaskCompletionSource<T>();
        document.RunAfterPrerendering(() => _ = operation().ContinueWith(
            task =>
            {
                if (task.IsFaulted)
                {
                    completion.TrySetException(task.Exception.InnerExceptions);
                }
                else if (task.IsCanceled)
                {
                    completion.TrySetCanceled();
                }
                else
                {
                    completion.TrySetResult(task.Result);
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default));
        return completion.Task;
    }

    /// <summary>
    /// https://wicg.github.io/nav-speculation/prerendering.html#finalize-activation: runs the post-prerendering activation
    /// steps of the document, then fires <c>prerenderingchange</c>.
    /// </summary>
    internal void FinalizePrerenderingActivation()
    {
        var steps = _postPrerenderingActivationSteps;
        _postPrerenderingActivationSteps = null;
        if (steps is not null)
        {
            foreach (var step in steps)
            {
                step();
            }
        }

        FireEvent(new Event("prerenderingchange"));
    }

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/speculative-loading.html#process-the-speculation-rules-header: the rule sets of
    /// the URLs of the header of a top-level document are fetched with CORS and added to its speculation rule sets. The responses
    /// whose status is not an ok status or whose MIME type is not <c>application/speculationrules+json</c> are ignored.
    /// </summary>
    internal void ProcessSpeculationRulesHeader(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        if (BrowsingContext is not { Parent: null, Options: { DocumentLoader: not null } options } || !options.EffectiveFeatures.SpeculationRulesEnabled
            || Infra.StructuredFields.GetList(headers, "Speculation-Rules") is not { } parsedList)
        {
            return;
        }

        foreach (var member in parsedList)
        {
            if (member.Item?.Value is not string item || UrlParser.Parse(item, DocumentBaseUrl) is not { } url)
                continue;

            QueueGlobalTask(() =>
            {
                if (!IsFullyActive)
                    return;

                var request = SubresourceFetching.CreateRequest(this, url, RequestDestination.SpeculationRules, cors: true, referrerPolicy: "", initiator: null);
                Fetching.Fetch(request, response =>
                {
                    if (response.IsNetworkError || response.Status is < 200 or > 299 || !IsFullyActive)
                        return;

                    if (HeaderList.ExtractMimeType(response.HeaderList) is not { Essence: "application/speculationrules+json" })
                        return;

                    // The body is UTF-8 decoded, whatever the charset of the Content-Type
                    var bodyBytes = (response.Body ?? []).AsSpan();
                    if (bodyBytes.StartsWith<byte>([0xEF, 0xBB, 0xBF]))
                    {
                        bodyBytes = bodyBytes[3..];
                    }

                    var bodyText = System.Text.Encoding.UTF8.GetString(bodyBytes);
                    SpeculationRuleSet ruleSet;
                    try
                    {
                        ruleSet = SpeculationRuleSet.Parse(bodyText, this, response.Url ?? url);
                    }
                    catch (Exception ex) when (ex is System.Text.Json.JsonException or TypeErrorException)
                    {
                        return;
                    }

                    AddSpeculationRuleSet(ruleSet);
                }, QueueGlobalTask);
            });
        }
    }
}

/// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#speculative-load-candidate (a prefetch candidate or a prerender candidate).</summary>
internal sealed class SpeculativeLoadCandidate(UrlRecord url, SpeculationRule rule, string referrerPolicy, bool isPrerender)
{
    public UrlRecord Url { get; } = url;

    public SpeculationRule Rule { get; } = rule;

    public SpeculationEagerness Eagerness => Rule.Eagerness;

    public string ReferrerPolicy { get; } = referrerPolicy;

    public bool IsPrerender { get; } = isPrerender;

    /// <summary>https://html.spec.whatwg.org/multipage/speculative-loading.html#sl-candidate-redundant-with</summary>
    public bool IsRedundantWith(SpeculativeLoadCandidate other)
        => Rule.NoVarySearchHint.Equals(other.Rule.NoVarySearchHint) && Rule.NoVarySearchHint.AreEquivalent(Url, other.Url);
}
