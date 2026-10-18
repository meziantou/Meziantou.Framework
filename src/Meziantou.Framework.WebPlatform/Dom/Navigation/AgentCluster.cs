namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// An agent cluster of a browsing context group (https://tc39.es/ecma262/#sec-agent-clusters,
/// https://html.spec.whatwg.org/multipage/webappapis.html#integration-with-the-javascript-agent-cluster-formalism): the
/// windows whose key (a site, or an origin for an origin-keyed agent cluster) is the same. The windows of an agent cluster
/// share their similar-origin window agent: they can access each other synchronously when they are same origin-domain, and
/// share WebAssembly modules and SharedArrayBuffers. All the windows of the library run on the event loop of the group.
/// </summary>
internal sealed class AgentCluster(object key, bool isOriginKeyed)
{
    /// <summary>The agent cluster key: a site, or an origin (https://html.spec.whatwg.org/multipage/webappapis.html#agent-cluster-key).</summary>
    public object Key { get; } = key;

    /// <summary>https://html.spec.whatwg.org/multipage/webappapis.html#is-origin-keyed</summary>
    public bool IsOriginKeyed { get; } = isOriginKeyed;
}

/// <summary>The agent cluster map and the historical agent cluster key map of a browsing context group (https://html.spec.whatwg.org/multipage/document-sequences.html#browsing-context-group).</summary>
internal sealed class AgentClusterMap
{
    private readonly Dictionary<object, AgentCluster> _agentClusters = [];
    private readonly Dictionary<object, object> _historicalKeys = [];

    /// <summary>
    /// https://html.spec.whatwg.org/multipage/webappapis.html#obtain-similar-origin-window-agent: the agent cluster of the
    /// key of the origin: its site, unless the origin requested an origin-keyed agent cluster (the first request of an
    /// origin in the group is kept by the historical agent cluster key map, so that same-origin windows are in the same
    /// agent cluster). The agent clusters of a group whose cross-origin isolation mode is not "none" are origin-keyed.
    /// </summary>
    public AgentCluster Obtain(OriginRecord origin, bool requestsOriginKeyed, bool isCrossOriginIsolated = false)
    {
        var site = origin.GetSiteKey();
        var originKey = origin.GetOriginKey();
        object key;
        if (isCrossOriginIsolated)
        {
            key = originKey;
        }
        else if (_historicalKeys.TryGetValue(originKey, out var historicalKey))
        {
            key = historicalKey;
        }
        else
        {
            key = requestsOriginKeyed ? originKey : site;
            _historicalKeys[originKey] = key;
        }

        if (!_agentClusters.TryGetValue(key, out var agentCluster))
        {
            // The key of an opaque origin is the origin itself (its site): the agent cluster is origin-keyed
            agentCluster = new AgentCluster(key, isOriginKeyed: key.Equals(originKey));
            _agentClusters[key] = agentCluster;
        }

        return agentCluster;
    }

    /// <summary>
    /// Whether the <c>Origin-Agent-Cluster</c> header of a response requests an origin-keyed agent cluster: its value is the
    /// structured field boolean true (https://html.spec.whatwg.org/multipage/browsers.html#origin-keyed-agent-clusters).
    /// </summary>
    public static bool RequestsOriginKeyed(IReadOnlyList<KeyValuePair<string, string>> headers)
    {
        // https://www.rfc-editor.org/rfc/rfc8941#name-parsing-an-item: a bare item, which may be followed by parameters
        if (HeaderList.Get(headers, "Origin-Agent-Cluster") is not { } value)
            return false;

        var item = value.Trim(' ', '\t');
        var parameters = item.IndexOf(';', StringComparison.Ordinal);
        if (parameters >= 0)
        {
            item = item[..parameters];
        }

        return item == "?1";
    }
}
