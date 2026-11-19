namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The result of <see cref="Performance.MeasureUserAgentSpecificMemory"/> (https://wicg.github.io/performance-measure-memory/#dictdef-memorymeasurement).</summary>
/// <seealso href="https://wicg.github.io/performance-measure-memory/#dictdef-memorymeasurement"/>
[IdlInterface("MemoryMeasurement", "https://wicg.github.io/performance-measure-memory/#dictdef-memorymeasurement")]
public sealed class MemoryMeasurement
{
    /// <summary>Gets the estimated size of the measured memory, in bytes: the sum of the sizes of the entries of <see cref="Breakdown"/>.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memorymeasurement-bytes"/>
    [IdlMember("bytes", "https://wicg.github.io/performance-measure-memory/#dom-memorymeasurement-bytes")]
    public ulong Bytes { get; init; }

    /// <summary>Gets the entries of the measurement, in a random order.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memorymeasurement-breakdown"/>
    [IdlMember("breakdown", "https://wicg.github.io/performance-measure-memory/#dom-memorymeasurement-breakdown")]
    public IReadOnlyList<MemoryBreakdownEntry> Breakdown { get; init; } = [];
}

/// <summary>An entry of a <see cref="MemoryMeasurement"/> (https://wicg.github.io/performance-measure-memory/#dictdef-memorybreakdownentry).</summary>
/// <seealso href="https://wicg.github.io/performance-measure-memory/#dictdef-memorybreakdownentry"/>
[IdlInterface("MemoryBreakdownEntry", "https://wicg.github.io/performance-measure-memory/#dictdef-memorybreakdownentry")]
public sealed class MemoryBreakdownEntry
{
    /// <summary>Gets the estimated size of the memory of the entry, in bytes.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-bytes"/>
    [IdlMember("bytes", "https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-bytes")]
    public ulong Bytes { get; init; }

    /// <summary>Gets the realms to which the memory is attributed.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-attribution"/>
    [IdlMember("attribution", "https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-attribution")]
    public IReadOnlyList<MemoryAttribution> Attribution { get; init; } = [];

    /// <summary>Gets the types of the memory (<c>DOM</c>, <c>JavaScript</c>).</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-types"/>
    [IdlMember("types", "https://wicg.github.io/performance-measure-memory/#dom-memorybreakdownentry-types")]
    public IReadOnlyList<string> Types { get; init; } = [];
}

/// <summary>The realm to which the memory of a <see cref="MemoryBreakdownEntry"/> is attributed (https://wicg.github.io/performance-measure-memory/#dictdef-memoryattribution).</summary>
/// <seealso href="https://wicg.github.io/performance-measure-memory/#dictdef-memoryattribution"/>
[IdlInterface("MemoryAttribution", "https://wicg.github.io/performance-measure-memory/#dictdef-memoryattribution")]
public sealed class MemoryAttribution
{
    /// <summary>Gets the URL of the realm, or <c>cross-origin-url</c> for the realms of another origin.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-url"/>
    [IdlMember("url", "https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-url")]
    public string Url { get; init; } = "";

    /// <summary>Gets the element (an <c>iframe</c>) of the document of the measurement that contains the realm, or null for a top-level window or a worker.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-container"/>
    [IdlMember("container", "https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-container")]
    public MemoryAttributionContainer? Container { get; init; }

    /// <summary>Gets the type of the global object of the realm (<c>Window</c>, <c>DedicatedWorkerGlobalScope</c>...), or <c>cross-origin-aggregated</c>.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-scope"/>
    [IdlMember("scope", "https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-scope")]
    public string Scope { get; init; } = "";
}

/// <summary>The container element of a <see cref="MemoryAttribution"/> (https://wicg.github.io/performance-measure-memory/#dictdef-memoryattributioncontainer).</summary>
/// <seealso href="https://wicg.github.io/performance-measure-memory/#dictdef-memoryattributioncontainer"/>
[IdlInterface("MemoryAttributionContainer", "https://wicg.github.io/performance-measure-memory/#dictdef-memoryattributioncontainer")]
public sealed class MemoryAttributionContainer
{
    /// <summary>Gets the value of the <c>id</c> attribute of the element.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memoryattributioncontainer-id"/>
    [IdlMember("id", "https://wicg.github.io/performance-measure-memory/#dom-memoryattributioncontainer-id")]
    public string Id { get; init; } = "";

    /// <summary>Gets the value of the <c>src</c> attribute of the element, resolved as a URL.</summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-memoryattributioncontainer-src"/>
    [IdlMember("src", "https://wicg.github.io/performance-measure-memory/#dom-memoryattributioncontainer-src")]
    public string Src { get; init; } = "";
}

// The Measure Memory API (https://wicg.github.io/performance-measure-memory/)
public sealed partial class Performance
{
    /// <summary>The estimated size of the realm of a global object (its JavaScript built-ins and its global object).</summary>
    private const ulong RealmBaseBytes = 64 * 1024;

    /// <summary>The estimated size of a node of a document.</summary>
    private const ulong NodeBytes = 128;

    /// <summary>
    /// Estimates the memory of the agent cluster of the window or of the worker
    /// (https://wicg.github.io/performance-measure-memory/#dom-performance-measureuseragentspecificmemory): one entry per
    /// realm of the browsing context group of a window (the documents of its windows and frames, and their dedicated workers),
    /// or the realm of the worker, and an empty entry, in a random order. A realm of the origin of the measurement is
    /// attributed to its URL, the other realms to <c>cross-origin-url</c>; the container is the <c>iframe</c> element of a
    /// document of the origin of the measurement that contains the realm. The sizes are a deterministic estimate of the
    /// objects of the library (a base size per realm, the nodes and the text of the documents), not of the JavaScript
    /// heap. The method is only exposed to the scripts of the cross-origin isolated windows, service workers and shared
    /// workers. The task fails with an <c>InvalidStateError</c> <see cref="DomException"/> when the document is not fully active.
    /// </summary>
    /// <seealso href="https://wicg.github.io/performance-measure-memory/#dom-performance-measureuseragentspecificmemory"/>
    [IdlMember("measureUserAgentSpecificMemory", "https://wicg.github.io/performance-measure-memory/#dom-performance-measureuseragentspecificmemory")]
    public Task<MemoryMeasurement> MeasureUserAgentSpecificMemory()
    {
        var global = _global;
        if (global is Window { Document.IsFullyActive: false })
            return Task.FromException<MemoryMeasurement>(DomException.InvalidState("The document is not fully active"));

        var entries = new List<MemoryBreakdownEntry>();
        if (global is Window window && window.Document.BrowsingContext is { } browsingContext)
        {
            var origin = window.Document.Origin;
            foreach (var topLevel in browsingContext.Top.GetGroupTopLevelBrowsingContexts())
            {
                // The windows that are closed, and the windows that switched to another browsing context group (the
                // Cross-Origin-Opener-Policy of their document), are not in the agent cluster
                if (topLevel.IsDestroyed || topLevel.IsClosing || !ReferenceEquals(topLevel.Group, browsingContext.Group))
                    continue;

                foreach (var navigable in topLevel.GetInclusiveDescendantNavigables())
                {
                    // The initial about:blank document of a navigable that navigates is not measured: it is replaced by the
                    // document of the navigation (e.g. a popup opened on another site, which gets its own agent cluster)
                    var document = navigable.Document;
                    if (document.RelevantWindow is not { } navigableWindow || (document.IsInitialAboutBlank && navigable.IsNavigating))
                        continue;

                    var container = GetContainer(navigable, origin);
                    var sameOrigin = OriginRecord.IsSameOrigin(document.Origin, origin);
                    entries.Add(CreateEntry(EstimateDocument(document), sameOrigin ? document.Url : "cross-origin-url", sameOrigin ? "Window" : "cross-origin-aggregated", container));
                    foreach (var worker in navigableWindow.GetWorkers())
                    {
                        if (worker.Global is { } workerGlobal)
                        {
                            entries.Add(CreateEntry(RealmBaseBytes, sameOrigin ? worker.Url.Serialize() : "cross-origin-url", sameOrigin ? GetScope(workerGlobal) : "cross-origin-aggregated", container));
                        }
                    }
                }
            }
        }
        else
        {
            entries.Add(CreateEntry(RealmBaseBytes, global.EnvironmentDocument.Url, GetScope(global), container: null));
        }

        // The empty entry, and a random order (https://wicg.github.io/performance-measure-memory/#randomize-the-order)
        entries.Add(new MemoryBreakdownEntry());
#pragma warning disable CA5394 // The order of the entries does not need a secure random number generator
        Random.Shared.Shuffle(System.Runtime.InteropServices.CollectionsMarshal.AsSpan(entries));
#pragma warning restore CA5394
        ulong bytes = 0;
        foreach (var entry in entries)
        {
            bytes += entry.Bytes;
        }

        var measurement = new MemoryMeasurement { Bytes = bytes, Breakdown = entries };
        var completion = new TaskCompletionSource<MemoryMeasurement>();
        global.QueueGlobalTask(() => completion.TrySetResult(measurement));
        return completion.Task;

        static MemoryBreakdownEntry CreateEntry(ulong bytes, string url, string scope, MemoryAttributionContainer? container) => new()
        {
            Bytes = bytes,
            Attribution = [new MemoryAttribution { Url = url, Scope = scope, Container = container }],
            Types = ["DOM", "JavaScript"],
        };
    }

    /// <summary>The name of the interface of a global object (https://wicg.github.io/performance-measure-memory/#dom-memoryattribution-scope).</summary>
    private static string GetScope(GlobalScope global) => global switch
    {
        Window => "Window",
        ServiceWorkerGlobalScope => "ServiceWorkerGlobalScope",
        SharedWorkerGlobalScope => "SharedWorkerGlobalScope",
        _ => "DedicatedWorkerGlobalScope",
    };

    /// <summary>
    /// https://wicg.github.io/performance-measure-memory/#get-the-container-element: the closest container of the navigable or
    /// of its ancestors whose node document has the origin of the measurement, or null for a top-level navigable.
    /// </summary>
    private static MemoryAttributionContainer? GetContainer(BrowsingContext navigable, OriginRecord origin)
    {
        for (BrowsingContext? current = navigable; current?.Container is { } element; current = current.Parent)
        {
            if (OriginRecord.IsSameOrigin(element.NodeDocument.Origin, origin))
                return new MemoryAttributionContainer { Id = element.GetAttributeValue("id") ?? "", Src = Html.Reflection.GetUrl(element, "src") };
        }

        return null;
    }

    /// <summary>A deterministic estimate of the memory of a document: a base size, its nodes, and the characters of its text.</summary>
    private static ulong EstimateDocument(Document document)
    {
        var bytes = RealmBaseBytes;
        foreach (var node in document.DescendantsAndSelf())
        {
            bytes += NodeBytes;
            if (node is CharacterData characterData)
            {
                bytes += (ulong)characterData.Data.Length * 2;
            }
        }

        return bytes;
    }
}
