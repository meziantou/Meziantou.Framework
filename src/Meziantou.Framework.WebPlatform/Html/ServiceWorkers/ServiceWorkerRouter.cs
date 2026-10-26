using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A rule of the static routing API of the service workers (https://w3c.github.io/ServiceWorker/#dictdef-routerrule): the
/// requests that match <see cref="Condition"/> are handled by <see cref="Source"/> (the network, the cache, or the fetch
/// event) before the fetch event is dispatched. The rules are registered by <see cref="InstallEvent.AddRoutes(RouterRule)"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-routerrule"/>
[IdlInterface("RouterRule", "https://w3c.github.io/ServiceWorker/#dictdef-routerrule")]
public sealed class RouterRule
{
    /// <summary>Gets the condition of the requests handled by the rule.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routerrule-condition"/>
    [IdlMember("condition", "https://w3c.github.io/ServiceWorker/#dom-routerrule-condition")]
    public required RouterCondition Condition { get; init; }

    /// <summary>Gets the source of the response of the requests that match the condition.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routerrule-source"/>
    [IdlMember("source", "https://w3c.github.io/ServiceWorker/#dom-routerrule-source")]
    public required RouterSource Source { get; init; }
}

/// <summary>
/// The condition of a <see cref="RouterRule"/> (https://w3c.github.io/ServiceWorker/#dictdef-routercondition): a request
/// matches when it matches all the members that are present. <see cref="Or"/> and <see cref="Not"/> cannot be combined
/// with the other members. A null member is not present.
/// </summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-routercondition"/>
[IdlInterface("RouterCondition", "https://w3c.github.io/ServiceWorker/#dictdef-routercondition")]
public sealed class RouterCondition
{
    /// <summary>Gets the pattern of the URL of the request, relative to the URL of the script of the service worker. A pattern with regular expression groups is not allowed.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-urlpattern"/>
    [IdlMember("urlPattern", "https://w3c.github.io/ServiceWorker/#dom-routercondition-urlpattern")]
    public UrlPatternCompatible? UrlPattern { get; init; }

    /// <summary>Gets the method of the request (normalized before the comparison, e.g. <c>post</c> matches <c>POST</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-requestmethod"/>
    [IdlMember("requestMethod", "https://w3c.github.io/ServiceWorker/#dom-routercondition-requestmethod")]
    public string? RequestMethod { get; init; }

    /// <summary>Gets the mode of the request (<c>navigate</c>, <c>same-origin</c>, <c>no-cors</c> or <c>cors</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-requestmode"/>
    [IdlMember("requestMode", "https://w3c.github.io/ServiceWorker/#dom-routercondition-requestmode")]
    public string? RequestMode { get; init; }

    /// <summary>Gets the destination of the request (e.g. <c>script</c>, <c>style</c>, <c>document</c>, or the empty string).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-requestdestination"/>
    [IdlMember("requestDestination", "https://w3c.github.io/ServiceWorker/#dom-routercondition-requestdestination")]
    public string? RequestDestination { get; init; }

    /// <summary>Gets whether the service worker must be running, or not running.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-runningstatus"/>
    [IdlMember("runningStatus", "https://w3c.github.io/ServiceWorker/#dom-routercondition-runningstatus")]
    public RunningStatus? RunningStatus { get; init; }

    /// <summary>Gets the conditions of which one must match.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-or"/>
    [IdlMember("or", "https://w3c.github.io/ServiceWorker/#dom-routercondition-or")]
    public IReadOnlyList<RouterCondition>? Or { get; init; }

    /// <summary>Gets the condition that must not match.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routercondition-not"/>
    [IdlMember("not", "https://w3c.github.io/ServiceWorker/#dom-routercondition-not")]
    public RouterCondition? Not { get; init; }
}

/// <summary>The cache source of a <see cref="RouterRule"/> (https://w3c.github.io/ServiceWorker/#dictdef-routersourcedict).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-routersourcedict"/>
[IdlInterface("RouterSourceDict", "https://w3c.github.io/ServiceWorker/#dictdef-routersourcedict")]
public sealed class RouterSourceDict
{
    /// <summary>Gets the name of the cache whose response is used, or null to look up every cache (as the <c>cache</c> source).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routersourcedict-cachename"/>
    [IdlMember("cacheName", "https://w3c.github.io/ServiceWorker/#dom-routersourcedict-cachename")]
    public string? CacheName { get; init; }
}

/// <summary>Whether the service worker is running (https://w3c.github.io/ServiceWorker/#enumdef-runningstatus).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-runningstatus"/>
[IdlInterface("RunningStatus", "https://w3c.github.io/ServiceWorker/#enumdef-runningstatus")]
public enum RunningStatus
{
    /// <summary>The service worker is running.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-runningstatus-running"/>
    [IdlMember("running", "https://w3c.github.io/ServiceWorker/#dom-runningstatus-running")]
    Running,

    /// <summary>The service worker is not running (it is started to handle the fetch event).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-runningstatus-not-running"/>
    [IdlMember("not-running", "https://w3c.github.io/ServiceWorker/#dom-runningstatus-not-running")]
    NotRunning,
}

/// <summary>The source of the response of a <see cref="RouterRule"/> (https://w3c.github.io/ServiceWorker/#enumdef-routersourceenum).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-routersourceenum"/>
[IdlInterface("RouterSourceEnum", "https://w3c.github.io/ServiceWorker/#enumdef-routersourceenum")]
public enum RouterSourceKind
{
    /// <summary>The response stored for the request in the caches of the origin, or the network when there is none.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routersourceenum-cache"/>
    [IdlMember("cache", "https://w3c.github.io/ServiceWorker/#dom-routersourceenum-cache")]
    Cache,

    /// <summary>The fetch event of the service worker.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routersourceenum-fetch-event"/>
    [IdlMember("fetch-event", "https://w3c.github.io/ServiceWorker/#dom-routersourceenum-fetch-event")]
    FetchEvent,

    /// <summary>The network: the fetch event is not dispatched.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routersourceenum-network"/>
    [IdlMember("network", "https://w3c.github.io/ServiceWorker/#dom-routersourceenum-network")]
    Network,

    /// <summary>The first of the network (a response whose status is an ok status) and the fetch event that gives a response (for the GET requests).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-routersourceenum-race-network-and-fetch-handler"/>
    [IdlMember("race-network-and-fetch-handler", "https://w3c.github.io/ServiceWorker/#dom-routersourceenum-race-network-and-fetch-handler")]
    RaceNetworkAndFetchHandler,
}

/// <summary>The <c>(RouterSourceDict or RouterSourceEnum)</c> union (https://w3c.github.io/ServiceWorker/#typedefdef-routersource).</summary>
public readonly struct RouterSource : IUnionValue, IEquatable<RouterSource>
{
    private RouterSource(object value) => Value = value;

    /// <summary>Gets the value of the union: a <see cref="RouterSourceDict"/> or a <see cref="RouterSourceKind"/>.</summary>
    [NotInIdl]
    public object Value { get; }

    /// <summary>Gets the value as a <see cref="RouterSourceDict"/>, or null when the union holds a <see cref="RouterSourceKind"/>.</summary>
    [NotInIdl]
    public RouterSourceDict? Dictionary => Value as RouterSourceDict;

    /// <summary>Gets the value as a <see cref="RouterSourceKind"/>, or null when the union holds a <see cref="RouterSourceDict"/>.</summary>
    [NotInIdl]
    public RouterSourceKind? Kind => Value is RouterSourceKind kind ? kind : null;

    /// <summary>Converts a cache source to the union.</summary>
    [NotInIdl]
    public static implicit operator RouterSource(RouterSourceDict value) => new(value ?? throw new ArgumentNullException(nameof(value)));

    /// <summary>Converts a kind of source to the union.</summary>
    [NotInIdl]
    public static implicit operator RouterSource(RouterSourceKind value) => new(value);

    /// <summary>Creates the union from a cache source.</summary>
    [NotInIdl]
    public static RouterSource FromRouterSourceDict(RouterSourceDict value) => value;

    /// <summary>Creates the union from a kind of source.</summary>
    [NotInIdl]
    public static RouterSource FromRouterSourceKind(RouterSourceKind value) => value;

    /// <inheritdoc/>
    [NotInIdl]
    public bool Equals(RouterSource other) => Equals(Value, other.Value);

    /// <inheritdoc/>
    [NotInIdl]
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RouterSource other && Equals(other);

    /// <inheritdoc/>
    [NotInIdl]
    public override int GetHashCode() => Value?.GetHashCode() ?? 0;

    /// <summary>Determines whether two unions are equal.</summary>
    [NotInIdl]
    public static bool operator ==(RouterSource left, RouterSource right) => left.Equals(right);

    /// <summary>Determines whether two unions are different.</summary>
    [NotInIdl]
    public static bool operator !=(RouterSource left, RouterSource right) => !left.Equals(right);
}

/// <summary>
/// A verified router condition (https://w3c.github.io/ServiceWorker/#verify-router-condition): the URL pattern is parsed
/// once, when the rule is registered (the specification parses it again for each request, with the same result).
/// </summary>
internal sealed class ServiceWorkerRouterCondition
{
    public UrlPattern? UrlPattern { get; init; }

    public string? Method { get; init; }

    public string? Mode { get; init; }

    public string? Destination { get; init; }

    public RunningStatus? RunningStatus { get; init; }

    public IReadOnlyList<ServiceWorkerRouterCondition>? Or { get; init; }

    public ServiceWorkerRouterCondition? Not { get; init; }
}

/// <summary>A rule of the list of router rules of a service worker (https://w3c.github.io/ServiceWorker/#dfn-list-of-router-rules).</summary>
/// <param name="Condition">The verified condition.</param>
/// <param name="Source">The kind of source (<see cref="RouterSourceKind.Cache"/> for a <see cref="RouterSourceDict"/> with a cache name).</param>
/// <param name="CacheName">The name of the cache of a <see cref="RouterSourceDict"/>, or null.</param>
internal sealed record ServiceWorkerRouterRule(ServiceWorkerRouterCondition Condition, RouterSourceKind Source, string? CacheName)
{
    /// <summary>The name of the source in the resource timing (https://w3c.github.io/resource-timing/#dom-performanceresourcetiming-workermatchedroutersource).</summary>
    public string SourceName => ServiceWorkerRouter.GetSourceName(Source);
}

/// <summary>The algorithms of the static routing API (https://w3c.github.io/ServiceWorker/#dom-installevent-addroutes).</summary>
internal static class ServiceWorkerRouter
{
    private const int MaxConditionCount = 1024;
    private const int MaxConditionDepth = 10;

    /// <summary>
    /// Verifies the rules given to <c>addRoutes()</c> (https://w3c.github.io/ServiceWorker/#dom-installevent-addroutes, step 3),
    /// and returns them, or null when one of them is not valid.
    /// </summary>
    public static List<ServiceWorkerRouterRule>? Verify(IReadOnlyList<RouterRule> rules, ServiceWorkerRecord serviceWorker)
    {
        var result = new List<ServiceWorkerRouterRule>(rules.Count);
        foreach (var rule in rules)
        {
            if (Verify(rule.Condition, serviceWorker.ScriptUrl) is not { } condition)
                return null;

            // A cache source without cache name looks up every cache, as the "cache" source; a dictionary without member is
            // handled as the fetch event (https://w3c.github.io/ServiceWorker/#handle-fetch, step 16.3.5)
            var (source, cacheName) = rule.Source.Value switch
            {
                RouterSourceDict { CacheName: { } name } => (RouterSourceKind.Cache, name),
                RouterSourceDict => (RouterSourceKind.FetchEvent, null),
                RouterSourceKind kind => (kind, (string?)null),
                _ => (RouterSourceKind.FetchEvent, null),
            };

            if (rule.Source.Value is RouterSourceKind.FetchEvent or RouterSourceKind.RaceNetworkAndFetchHandler && serviceWorker.EventTypesToHandle?.Contains("fetch") is not true)
                return null;

            result.Add(new ServiceWorkerRouterRule(condition, source, cacheName));
        }

        return result;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#verify-router-condition: returns the verified condition, or null when it is not valid.</summary>
    private static ServiceWorkerRouterCondition? Verify(RouterCondition condition, UrlRecord baseUrl)
    {
        var hasCondition = false;
        UrlPattern? pattern = null;
        if (condition.UrlPattern is { } rawPattern)
        {
            try
            {
                pattern = UrlPattern.FromWebIdlValue(rawPattern, baseUrl);
            }
            catch (TypeErrorException)
            {
                return null;
            }

            // A user-defined regular expression is not allowed
            if (pattern.HasRegExpGroups)
                return null;

            hasCondition = true;
        }

        if (condition.RequestMethod is { } method)
        {
            if (!FetchHeaders.IsMethod(method) || FetchHeaders.IsForbiddenMethod(method))
                return null;

            hasCondition = true;
        }

        hasCondition |= condition.RequestMode is not null || condition.RequestDestination is not null || condition.RunningStatus is not null;
        List<ServiceWorkerRouterCondition>? or = null;
        if (condition.Or is { } orConditions)
        {
            if (hasCondition)
                return null;

            or = new List<ServiceWorkerRouterCondition>(orConditions.Count);
            foreach (var orCondition in orConditions)
            {
                if (Verify(orCondition, baseUrl) is not { } verified)
                    return null;

                or.Add(verified);
            }

            hasCondition = true;
        }

        ServiceWorkerRouterCondition? not = null;
        if (condition.Not is { } notCondition)
        {
            if (hasCondition)
                return null;

            not = Verify(notCondition, baseUrl);
            if (not is null)
                return null;

            hasCondition = true;
        }

        if (!hasCondition)
            return null;

        return new ServiceWorkerRouterCondition
        {
            UrlPattern = pattern,
            Method = condition.RequestMethod is { } requestMethod ? FetchHeaders.NormalizeMethod(requestMethod) : null,
            Mode = condition.RequestMode,
            Destination = condition.RequestDestination,
            RunningStatus = condition.RunningStatus,
            Or = or,
            Not = not,
        };
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#check-router-registration-limit-algorithm</summary>
    public static bool CheckRegistrationLimit(IReadOnlyList<ServiceWorkerRouterRule> rules)
    {
        var remaining = MaxConditionCount;
        foreach (var rule in rules)
        {
            if (!CountInnerConditions(rule.Condition, ref remaining, MaxConditionDepth))
                return false;
        }

        return true;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#count-router-inner-conditions-algorithm: returns false when the quota is exceeded.</summary>
    private static bool CountInnerConditions(ServiceWorkerRouterCondition condition, ref int remaining, int depth)
    {
        remaining--;
        if (remaining == 0 || depth == 0)
            return false;

        if (condition.Or is { } orConditions)
        {
            foreach (var orCondition in orConditions)
            {
                if (!CountInnerConditions(orCondition, ref remaining, depth - 1))
                    return false;
            }
        }
        else if (condition.Not is { } notCondition)
        {
            return CountInnerConditions(notCondition, ref remaining, depth - 1);
        }

        return true;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#get-router-source-algorithm: the first rule whose condition matches the request, or null.</summary>
    public static ServiceWorkerRouterRule? GetRouterSource(ServiceWorkerRecord serviceWorker, FetchRequest request)
    {
        foreach (var rule in serviceWorker.RouterRules)
        {
            if (Matches(rule.Condition, serviceWorker, request))
                return rule;
        }

        return null;
    }

    /// <summary>https://w3c.github.io/ServiceWorker/#match-router-condition-algorithm</summary>
    private static bool Matches(ServiceWorkerRouterCondition condition, ServiceWorkerRecord serviceWorker, FetchRequest request)
    {
        if (condition.Or is { } orConditions)
            return orConditions.Any(orCondition => Matches(orCondition, serviceWorker, request));

        if (condition.Not is { } notCondition)
            return !Matches(notCondition, serviceWorker, request);

        // The URL of the request of a navigation is its current URL (see FetchRequest.NavigationRedirectUrls)
        if (condition.UrlPattern is { } pattern && !pattern.Test(request.CurrentUrl))
            return false;

        if (condition.Method is { } method && !string.Equals(request.Method, method, StringComparison.Ordinal))
            return false;

        if (condition.Mode is { } mode && !string.Equals(GetModeName(request.Mode), mode, StringComparison.Ordinal))
            return false;

        if (condition.Destination is { } destination && !string.Equals(GetDestinationName(request.Destination), destination, StringComparison.Ordinal))
            return false;

        if (condition.RunningStatus is { } runningStatus && runningStatus == RunningStatus.Running != (serviceWorker.Agent is not null))
            return false;

        return true;
    }

    /// <summary>Converts a rule to the rule stored with its service worker (see <see cref="ServiceWorkerBackend"/>).</summary>
    public static StoredServiceWorkerRouterRule ToStored(ServiceWorkerRouterRule rule) => new()
    {
        Condition = ToStored(rule.Condition),
        Source = rule.SourceName,
        CacheName = rule.CacheName,
    };

    private static StoredServiceWorkerRouterCondition ToStored(ServiceWorkerRouterCondition condition) => new()
    {
        UrlPattern = condition.UrlPattern is { } pattern ? new StoredServiceWorkerUrlPattern
        {
            Protocol = pattern.Protocol,
            Username = pattern.Username,
            Password = pattern.Password,
            Hostname = pattern.Hostname,
            Port = pattern.Port,
            Pathname = pattern.Pathname,
            Search = pattern.Search,
            Hash = pattern.Hash,
            IgnoreCase = pattern.IgnoreCase,
        } : null,
        RequestMethod = condition.Method,
        RequestMode = condition.Mode,
        RequestDestination = condition.Destination,
        RunningStatus = condition.RunningStatus switch
        {
            Dom.RunningStatus.Running => "running",
            Dom.RunningStatus.NotRunning => "not-running",
            _ => null,
        },
        Or = condition.Or is { } or ? [.. or.Select(ToStored)] : null,
        Not = condition.Not is { } not ? ToStored(not) : null,
    };

    /// <summary>Restores the rules stored with a service worker, or returns null when one of them is not valid.</summary>
    public static List<ServiceWorkerRouterRule>? FromStored(IReadOnlyList<StoredServiceWorkerRouterRule> rules)
    {
        var result = new List<ServiceWorkerRouterRule>(rules.Count);
        foreach (var rule in rules)
        {
            RouterSourceKind? source = rule.Source switch
            {
                "network" => RouterSourceKind.Network,
                "cache" => RouterSourceKind.Cache,
                "fetch-event" => RouterSourceKind.FetchEvent,
                "race-network-and-fetch-handler" => RouterSourceKind.RaceNetworkAndFetchHandler,
                _ => null,
            };
            if (source is null || rule.Condition is null || FromStored(rule.Condition) is not { } condition)
                return null;

            result.Add(new ServiceWorkerRouterRule(condition, source.GetValueOrDefault(), rule.CacheName));
        }

        return CheckRegistrationLimit(result) ? result : null;
    }

    private static ServiceWorkerRouterCondition? FromStored(StoredServiceWorkerRouterCondition condition)
    {
        UrlPattern? pattern = null;
        if (condition.UrlPattern is { } storedPattern)
        {
            try
            {
                pattern = new UrlPattern(
                    new UrlPatternInit
                    {
                        Protocol = storedPattern.Protocol,
                        Username = storedPattern.Username,
                        Password = storedPattern.Password,
                        Hostname = storedPattern.Hostname,
                        Port = storedPattern.Port,
                        Pathname = storedPattern.Pathname,
                        Search = storedPattern.Search,
                        Hash = storedPattern.Hash,
                    },
                    new UrlPatternOptions { IgnoreCase = storedPattern.IgnoreCase });
            }
            catch (TypeErrorException)
            {
                return null;
            }

            if (pattern.HasRegExpGroups)
                return null;
        }

        var (runningStatus, isValidRunningStatus) = condition.RunningStatus switch
        {
            null => ((RunningStatus?)null, true),
            "running" => (Dom.RunningStatus.Running, true),
            "not-running" => (Dom.RunningStatus.NotRunning, true),
            _ => (null, false),
        };
        if (!isValidRunningStatus || (condition.RequestMethod is { } method && (!FetchHeaders.IsMethod(method) || FetchHeaders.IsForbiddenMethod(method))))
            return null;

        List<ServiceWorkerRouterCondition>? or = null;
        if (condition.Or is { } orConditions)
        {
            or = new List<ServiceWorkerRouterCondition>(orConditions.Count);
            foreach (var orCondition in orConditions)
            {
                if (orCondition is null || FromStored(orCondition) is not { } restored)
                    return null;

                or.Add(restored);
            }
        }

        ServiceWorkerRouterCondition? not = null;
        if (condition.Not is { } notCondition && (not = FromStored(notCondition)) is null)
            return null;

        return new ServiceWorkerRouterCondition
        {
            UrlPattern = pattern,
            Method = condition.RequestMethod is { } requestMethod ? FetchHeaders.NormalizeMethod(requestMethod) : null,
            Mode = condition.RequestMode,
            Destination = condition.RequestDestination,
            RunningStatus = runningStatus,
            Or = or,
            Not = not,
        };
    }

    /// <summary>The name of the mode of a request (https://fetch.spec.whatwg.org/#concept-request-mode).</summary>
    private static string GetModeName(RequestMode mode) => mode switch
    {
        RequestMode.SameOrigin => "same-origin",
        RequestMode.NoCors => "no-cors",
        RequestMode.Navigate => "navigate",
        RequestMode.WebSocket => "websocket",
        _ => "cors",
    };

    /// <summary>The name of the destination of a request (https://fetch.spec.whatwg.org/#concept-request-destination): the empty string for the empty destination.</summary>
    private static string GetDestinationName(RequestDestination destination) => FetchMetadata.GetDestinationName(destination) is var name && name == "empty" ? "" : name;

    public static string GetSourceName(RouterSourceKind source) => source switch
    {
        RouterSourceKind.Cache => "cache",
        RouterSourceKind.FetchEvent => "fetch-event",
        RouterSourceKind.Network => "network",
        _ => "race-network-and-fetch-handler",
    };
}
