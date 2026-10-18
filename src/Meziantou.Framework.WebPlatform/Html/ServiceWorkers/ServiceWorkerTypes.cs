namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a service worker (https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerstate).</summary>
[IdlInterface("ServiceWorkerState", "https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerstate")]
public enum ServiceWorkerState
{
    /// <summary>The script of the service worker was fetched and is about to be installed.</summary>
    [IdlMember("parsed", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-parsed")]
    Parsed,

    /// <summary>The <c>install</c> event of the service worker is dispatched.</summary>
    [IdlMember("installing", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installing")]
    Installing,

    /// <summary>The service worker is installed, and waits to be activated.</summary>
    [IdlMember("installed", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installed")]
    Installed,

    /// <summary>The <c>activate</c> event of the service worker is dispatched.</summary>
    [IdlMember("activating", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activating")]
    Activating,

    /// <summary>The service worker is activated: it handles the functional events (e.g. <c>fetch</c>).</summary>
    [IdlMember("activated", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activated")]
    Activated,

    /// <summary>The service worker was replaced by a new one, its installation failed, or its registration was cleared.</summary>
    [IdlMember("redundant", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-redundant")]
    Redundant,
}

/// <summary>Whether the HTTP cache is used by the update checks of a registration (https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerupdateviacache).</summary>
[IdlInterface("ServiceWorkerUpdateViaCache", "https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerupdateviacache")]
public enum ServiceWorkerUpdateViaCache
{
    /// <summary>The cache is used for the imported scripts, not for the main script.</summary>
    [IdlMember("imports", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-imports")]
    Imports,

    /// <summary>The cache is used for all the scripts.</summary>
    [IdlMember("all", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-all")]
    All,

    /// <summary>The cache is not used.</summary>
    [IdlMember("none", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-none")]
    None,
}

/// <summary>The kind of the navigable of a window client (https://w3c.github.io/ServiceWorker/#enumdef-frametype).</summary>
[IdlInterface("FrameType", "https://w3c.github.io/ServiceWorker/#enumdef-frametype")]
public enum FrameType
{
    /// <summary>An auxiliary browsing context (e.g. opened by <c>window.open()</c>).</summary>
    [IdlMember("auxiliary", "https://w3c.github.io/ServiceWorker/#dom-frametype-auxiliary")]
    Auxiliary,

    /// <summary>A top-level browsing context.</summary>
    [IdlMember("top-level", "https://w3c.github.io/ServiceWorker/#dom-frametype-top-level")]
    TopLevel,

    /// <summary>A nested browsing context (e.g. an <c>iframe</c>).</summary>
    [IdlMember("nested", "https://w3c.github.io/ServiceWorker/#dom-frametype-nested")]
    Nested,

    /// <summary>A client that is not a window (a worker).</summary>
    [IdlMember("none", "https://w3c.github.io/ServiceWorker/#dom-frametype-none")]
    None,
}

/// <summary>The type of a service worker client (https://w3c.github.io/ServiceWorker/#enumdef-clienttype).</summary>
[IdlInterface("ClientType", "https://w3c.github.io/ServiceWorker/#enumdef-clienttype")]
public enum ClientType
{
    /// <summary>A window.</summary>
    [IdlMember("window", "https://w3c.github.io/ServiceWorker/#dom-clienttype-window")]
    Window,

    /// <summary>A dedicated worker.</summary>
    [IdlMember("worker", "https://w3c.github.io/ServiceWorker/#dom-clienttype-worker")]
    Worker,

    /// <summary>A shared worker.</summary>
    [IdlMember("sharedworker", "https://w3c.github.io/ServiceWorker/#dom-clienttype-sharedworker")]
    SharedWorker,

    /// <summary>All the types of clients (a query of <see cref="Clients.MatchAll"/>).</summary>
    [IdlMember("all", "https://w3c.github.io/ServiceWorker/#dom-clienttype-all")]
    All,
}

/// <summary>The options of <see cref="ServiceWorkerContainer.Register(string, RegistrationOptions?)"/> (https://w3c.github.io/ServiceWorker/#dictdef-registrationoptions).</summary>
[IdlInterface("RegistrationOptions", "https://w3c.github.io/ServiceWorker/#dictdef-registrationoptions")]
public sealed class RegistrationOptions
{
    /// <summary>Gets the scope URL, relative to the API base URL, or null for the directory of the script.</summary>
    [IdlMember("scope", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-scope")]
    public string? Scope { get; init; }

    /// <summary>Gets the type of the script.</summary>
    [IdlMember("type", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-type")]
    public WorkerType Type { get; init; } = WorkerType.Classic;

    /// <summary>Gets whether the HTTP cache is used by the update checks.</summary>
    [IdlMember("updateViaCache", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-updateviacache")]
    public ServiceWorkerUpdateViaCache UpdateViaCache { get; init; } = ServiceWorkerUpdateViaCache.Imports;
}

/// <summary>The navigation preload state of a registration (https://w3c.github.io/ServiceWorker/#dictdef-navigationpreloadstate).</summary>
[IdlInterface("NavigationPreloadState", "https://w3c.github.io/ServiceWorker/#dictdef-navigationpreloadstate")]
public sealed class NavigationPreloadState
{
    /// <summary>Gets whether the navigation preload is enabled.</summary>
    [IdlMember("enabled", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-enabled")]
    public bool Enabled { get; init; }

    /// <summary>Gets the value of the <c>Service-Worker-Navigation-Preload</c> header.</summary>
    [IdlMember("headerValue", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-headervalue")]
    public string? HeaderValue { get; init; }
}

/// <summary>The options of <see cref="Clients.MatchAll"/> (https://w3c.github.io/ServiceWorker/#dictdef-clientqueryoptions).</summary>
[IdlInterface("ClientQueryOptions", "https://w3c.github.io/ServiceWorker/#dictdef-clientqueryoptions")]
public sealed class ClientQueryOptions
{
    /// <summary>Gets whether the clients that are not controlled by the service worker are included.</summary>
    [IdlMember("includeUncontrolled", "https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-includeuncontrolled")]
    public bool IncludeUncontrolled { get; init; }

    /// <summary>Gets the type of the clients.</summary>
    [IdlMember("type", "https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-type")]
    public ClientType Type { get; init; } = ClientType.Window;
}

/// <summary>The initialization dictionary of an <see cref="ExtendableEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-extendableeventinit).</summary>
[IdlInterface("ExtendableEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-extendableeventinit")]
public class ExtendableEventInit : EventInit
{
}

/// <summary>The initialization dictionary of a <see cref="FetchEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-fetcheventinit).</summary>
[IdlInterface("FetchEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-fetcheventinit")]
public sealed class FetchEventInit : ExtendableEventInit
{
    /// <summary>Gets the request: a <c>Request</c> object of the script engine (the Fetch API is implemented in JavaScript).</summary>
    [IdlMember("request", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-request")]
    public required object Request { get; init; }

    /// <summary>Gets the promise of the response of the navigation preload, or null.</summary>
    [IdlMember("preloadResponse", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-preloadresponse")]
    public Task<object?>? PreloadResponse { get; init; }

    /// <summary>Gets the identifier of the client of the request.</summary>
    [IdlMember("clientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-clientid")]
    public string ClientId { get; init; } = "";

    /// <summary>Gets the identifier of the client created by the navigation.</summary>
    [IdlMember("resultingClientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-resultingclientid")]
    public string ResultingClientId { get; init; } = "";

    /// <summary>Gets the identifier of the client replaced by the navigation.</summary>
    [IdlMember("replacesClientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-replacesclientid")]
    public string ReplacesClientId { get; init; } = "";

    /// <summary>Gets the promise fulfilled when the event was handled, or null.</summary>
    [IdlMember("handled", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-handled")]
    public Task? Handled { get; init; }
}

/// <summary>The initialization dictionary of an <see cref="ExtendableMessageEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-extendablemessageeventinit).</summary>
[IdlInterface("ExtendableMessageEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-extendablemessageeventinit")]
public sealed class ExtendableMessageEventInit : ExtendableEventInit
{
    /// <summary>Gets the data of the message.</summary>
    [IdlMember("data", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-data")]
    public object? Data { get; init; } = Scripting.NullValue.Instance;

    /// <summary>Gets the origin of the sender.</summary>
    [IdlMember("origin", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-origin")]
    public string Origin { get; init; } = "";

    /// <summary>Gets the last event ID.</summary>
    [IdlMember("lastEventId", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-lasteventid")]
    public string LastEventId { get; init; } = "";

    /// <summary>Gets the sender: a <see cref="Dom.Client"/>, a <see cref="Dom.ServiceWorker"/> or a <see cref="MessagePort"/>, or null.</summary>
    [IdlMember("source", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-source")]
    public object? Source { get; init; }

    /// <summary>Gets the ports sent with the message.</summary>
    [IdlMember("ports", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-ports")]
    public IReadOnlyList<MessagePort> Ports { get; init; } = [];
}
