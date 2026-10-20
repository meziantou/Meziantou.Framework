namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a service worker (https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerstate).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerstate"/>
[IdlInterface("ServiceWorkerState", "https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerstate")]
public enum ServiceWorkerState
{
    /// <summary>The script of the service worker was fetched and is about to be installed.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-parsed"/>
    [IdlMember("parsed", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-parsed")]
    Parsed,

    /// <summary>The <c>install</c> event of the service worker is dispatched.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installing"/>
    [IdlMember("installing", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installing")]
    Installing,

    /// <summary>The service worker is installed, and waits to be activated.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installed"/>
    [IdlMember("installed", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-installed")]
    Installed,

    /// <summary>The <c>activate</c> event of the service worker is dispatched.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activating"/>
    [IdlMember("activating", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activating")]
    Activating,

    /// <summary>The service worker is activated: it handles the functional events (e.g. <c>fetch</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activated"/>
    [IdlMember("activated", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-activated")]
    Activated,

    /// <summary>The service worker was replaced by a new one, its installation failed, or its registration was cleared.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-redundant"/>
    [IdlMember("redundant", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerstate-redundant")]
    Redundant,
}

/// <summary>Whether the HTTP cache is used by the update checks of a registration (https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerupdateviacache).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerupdateviacache"/>
[IdlInterface("ServiceWorkerUpdateViaCache", "https://w3c.github.io/ServiceWorker/#enumdef-serviceworkerupdateviacache")]
public enum ServiceWorkerUpdateViaCache
{
    /// <summary>The cache is used for the imported scripts, not for the main script.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-imports"/>
    [IdlMember("imports", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-imports")]
    Imports,

    /// <summary>The cache is used for all the scripts.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-all"/>
    [IdlMember("all", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-all")]
    All,

    /// <summary>The cache is not used.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-none"/>
    [IdlMember("none", "https://w3c.github.io/ServiceWorker/#dom-serviceworkerupdateviacache-none")]
    None,
}

/// <summary>The kind of the navigable of a window client (https://w3c.github.io/ServiceWorker/#enumdef-frametype).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-frametype"/>
[IdlInterface("FrameType", "https://w3c.github.io/ServiceWorker/#enumdef-frametype")]
public enum FrameType
{
    /// <summary>An auxiliary browsing context (e.g. opened by <c>window.open()</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-frametype-auxiliary"/>
    [IdlMember("auxiliary", "https://w3c.github.io/ServiceWorker/#dom-frametype-auxiliary")]
    Auxiliary,

    /// <summary>A top-level browsing context.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-frametype-top-level"/>
    [IdlMember("top-level", "https://w3c.github.io/ServiceWorker/#dom-frametype-top-level")]
    TopLevel,

    /// <summary>A nested browsing context (e.g. an <c>iframe</c>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-frametype-nested"/>
    [IdlMember("nested", "https://w3c.github.io/ServiceWorker/#dom-frametype-nested")]
    Nested,

    /// <summary>A client that is not a window (a worker).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-frametype-none"/>
    [IdlMember("none", "https://w3c.github.io/ServiceWorker/#dom-frametype-none")]
    None,
}

/// <summary>The type of a service worker client (https://w3c.github.io/ServiceWorker/#enumdef-clienttype).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#enumdef-clienttype"/>
[IdlInterface("ClientType", "https://w3c.github.io/ServiceWorker/#enumdef-clienttype")]
public enum ClientType
{
    /// <summary>A window.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clienttype-window"/>
    [IdlMember("window", "https://w3c.github.io/ServiceWorker/#dom-clienttype-window")]
    Window,

    /// <summary>A dedicated worker.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clienttype-worker"/>
    [IdlMember("worker", "https://w3c.github.io/ServiceWorker/#dom-clienttype-worker")]
    Worker,

    /// <summary>A shared worker.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clienttype-sharedworker"/>
    [IdlMember("sharedworker", "https://w3c.github.io/ServiceWorker/#dom-clienttype-sharedworker")]
    SharedWorker,

    /// <summary>All the types of clients (a query of <see cref="Clients.MatchAll"/>).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clienttype-all"/>
    [IdlMember("all", "https://w3c.github.io/ServiceWorker/#dom-clienttype-all")]
    All,
}

/// <summary>The options of <see cref="ServiceWorkerContainer.Register(string, RegistrationOptions?)"/> (https://w3c.github.io/ServiceWorker/#dictdef-registrationoptions).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-registrationoptions"/>
[IdlInterface("RegistrationOptions", "https://w3c.github.io/ServiceWorker/#dictdef-registrationoptions")]
public sealed class RegistrationOptions
{
    /// <summary>Gets the scope URL, relative to the API base URL, or null for the directory of the script.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-registrationoptions-scope"/>
    [IdlMember("scope", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-scope")]
    public string? Scope { get; init; }

    /// <summary>Gets the type of the script.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-registrationoptions-type"/>
    [IdlMember("type", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-type")]
    public WorkerType Type { get; init; } = WorkerType.Classic;

    /// <summary>Gets whether the HTTP cache is used by the update checks.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-registrationoptions-updateviacache"/>
    [IdlMember("updateViaCache", "https://w3c.github.io/ServiceWorker/#dom-registrationoptions-updateviacache")]
    public ServiceWorkerUpdateViaCache UpdateViaCache { get; init; } = ServiceWorkerUpdateViaCache.Imports;
}

/// <summary>The navigation preload state of a registration (https://w3c.github.io/ServiceWorker/#dictdef-navigationpreloadstate).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-navigationpreloadstate"/>
[IdlInterface("NavigationPreloadState", "https://w3c.github.io/ServiceWorker/#dictdef-navigationpreloadstate")]
public sealed class NavigationPreloadState
{
    /// <summary>Gets whether the navigation preload is enabled.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-enabled"/>
    [IdlMember("enabled", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-enabled")]
    public bool Enabled { get; init; }

    /// <summary>Gets the value of the <c>Service-Worker-Navigation-Preload</c> header.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-headervalue"/>
    [IdlMember("headerValue", "https://w3c.github.io/ServiceWorker/#dom-navigationpreloadstate-headervalue")]
    public string? HeaderValue { get; init; }
}

/// <summary>The options of <see cref="Clients.MatchAll"/> (https://w3c.github.io/ServiceWorker/#dictdef-clientqueryoptions).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-clientqueryoptions"/>
[IdlInterface("ClientQueryOptions", "https://w3c.github.io/ServiceWorker/#dictdef-clientqueryoptions")]
public sealed class ClientQueryOptions
{
    /// <summary>Gets whether the clients that are not controlled by the service worker are included.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-includeuncontrolled"/>
    [IdlMember("includeUncontrolled", "https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-includeuncontrolled")]
    public bool IncludeUncontrolled { get; init; }

    /// <summary>Gets the type of the clients.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-type"/>
    [IdlMember("type", "https://w3c.github.io/ServiceWorker/#dom-clientqueryoptions-type")]
    public ClientType Type { get; init; } = ClientType.Window;
}

/// <summary>The initialization dictionary of an <see cref="ExtendableEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-extendableeventinit).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-extendableeventinit"/>
[IdlInterface("ExtendableEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-extendableeventinit")]
public class ExtendableEventInit : EventInit
{
}

/// <summary>The initialization dictionary of a <see cref="FetchEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-fetcheventinit).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-fetcheventinit"/>
[IdlInterface("FetchEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-fetcheventinit")]
public sealed class FetchEventInit : ExtendableEventInit
{
    /// <summary>Gets the request: a <c>Request</c> object of the script engine (the Fetch API is implemented in JavaScript).</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-request"/>
    [IdlMember("request", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-request")]
    public required object Request { get; init; }

    /// <summary>Gets the promise of the response of the navigation preload, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-preloadresponse"/>
    [IdlMember("preloadResponse", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-preloadresponse")]
    public Task<object?>? PreloadResponse { get; init; }

    /// <summary>Gets the identifier of the client of the request.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-clientid"/>
    [IdlMember("clientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-clientid")]
    public string ClientId { get; init; } = "";

    /// <summary>Gets the identifier of the client created by the navigation.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-resultingclientid"/>
    [IdlMember("resultingClientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-resultingclientid")]
    public string ResultingClientId { get; init; } = "";

    /// <summary>Gets the identifier of the client replaced by the navigation.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-replacesclientid"/>
    [IdlMember("replacesClientId", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-replacesclientid")]
    public string ReplacesClientId { get; init; } = "";

    /// <summary>Gets the promise fulfilled when the event was handled, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-handled"/>
    [IdlMember("handled", "https://w3c.github.io/ServiceWorker/#dom-fetcheventinit-handled")]
    public Task? Handled { get; init; }
}

/// <summary>The initialization dictionary of an <see cref="ExtendableMessageEvent"/> (https://w3c.github.io/ServiceWorker/#dictdef-extendablemessageeventinit).</summary>
/// <seealso href="https://w3c.github.io/ServiceWorker/#dictdef-extendablemessageeventinit"/>
[IdlInterface("ExtendableMessageEventInit", "https://w3c.github.io/ServiceWorker/#dictdef-extendablemessageeventinit")]
public sealed class ExtendableMessageEventInit : ExtendableEventInit
{
    /// <summary>Gets the data of the message.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-data"/>
    [IdlMember("data", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-data")]
    public object? Data { get; init; } = Scripting.NullValue.Instance;

    /// <summary>Gets the origin of the sender.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-origin"/>
    [IdlMember("origin", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-origin")]
    public string Origin { get; init; } = "";

    /// <summary>Gets the last event ID.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-lasteventid"/>
    [IdlMember("lastEventId", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-lasteventid")]
    public string LastEventId { get; init; } = "";

    /// <summary>Gets the sender: a <see cref="Dom.Client"/>, a <see cref="Dom.ServiceWorker"/> or a <see cref="MessagePort"/>, or null.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-source"/>
    [IdlMember("source", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-source")]
    public object? Source { get; init; }

    /// <summary>Gets the ports sent with the message.</summary>
    /// <seealso href="https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-ports"/>
    [IdlMember("ports", "https://w3c.github.io/ServiceWorker/#dom-extendablemessageeventinit-ports")]
    public IReadOnlyList<MessagePort> Ports { get; init; } = [];
}
