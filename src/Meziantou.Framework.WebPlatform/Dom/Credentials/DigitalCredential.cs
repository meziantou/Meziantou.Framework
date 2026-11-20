using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The presentation requests of <c>navigator.credentials.get()</c> (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialrequestoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialrequestoptions"/>
[IdlInterface("DigitalCredentialRequestOptions", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialrequestoptions")]
public sealed class DigitalCredentialRequestOptions
{
    /// <summary>Gets the requests, one per protocol, among which the wallet answers one.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialrequestoptions-requests"/>
    [IdlMember("requests", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialrequestoptions-requests")]
    public required IReadOnlyList<DigitalCredentialGetRequest> Requests { get; init; }
}

/// <summary>A presentation request (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest).</summary>
/// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest"/>
[IdlInterface("DigitalCredentialGetRequest", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest")]
public sealed class DigitalCredentialGetRequest
{
    /// <summary>Gets the presentation protocol of the request (e.g. <c>openid4vp-v1-unsigned</c> or <c>org-iso-mdoc</c>).</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest-protocol"/>
    [IdlMember("protocol", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest-protocol")]
    public required string Protocol { get; init; }

    /// <summary>Gets the data of the request, specific to the protocol: an object that is serialized to JSON for the wallet.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest-data"/>
    [IdlMember("data", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialgetrequest-data")]
    public required object Data { get; init; }
}

/// <summary>The issuance requests of <c>navigator.credentials.create()</c> (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreationoptions).</summary>
/// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreationoptions"/>
[IdlInterface("DigitalCredentialCreationOptions", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreationoptions")]
public sealed class DigitalCredentialCreationOptions
{
    /// <summary>Gets the requests, one per protocol, among which the wallet answers one.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreationoptions-requests"/>
    [IdlMember("requests", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreationoptions-requests")]
    public required IReadOnlyList<DigitalCredentialCreateRequest> Requests { get; init; }
}

/// <summary>An issuance request (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest).</summary>
/// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest"/>
[IdlInterface("DigitalCredentialCreateRequest", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest")]
public sealed class DigitalCredentialCreateRequest
{
    /// <summary>Gets the issuance protocol of the request (e.g. <c>openid4vci-v1</c>).</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest-protocol"/>
    [IdlMember("protocol", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest-protocol")]
    public required string Protocol { get; init; }

    /// <summary>Gets the data of the request, specific to the protocol: an object that is serialized to JSON for the wallet.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest-data"/>
    [IdlMember("data", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialcreaterequest-data")]
    public required object Data { get; init; }
}

/// <summary>
/// A credential presented or issued by a wallet (https://w3c-fedid.github.io/digital-credentials/#the-digitalcredential-interface),
/// the result of <c>navigator.credentials.get({ digital })</c> and <c>navigator.credentials.create({ digital })</c>. The
/// wallet is the one of the <see cref="UserAgentUIBackend"/> (see <see cref="UserAgentUIBackend.RequestDigitalCredentialAsync"/>).
/// </summary>
/// <seealso href="https://w3c-fedid.github.io/digital-credentials/#the-digitalcredential-interface"/>
[IdlInterface("DigitalCredential", "https://w3c-fedid.github.io/digital-credentials/#the-digitalcredential-interface")]
public sealed class DigitalCredential : Credential
{
    /// <summary>The presentation protocols (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialpresentationprotocol), which the user agent allows.</summary>
    internal static readonly string[] PresentationProtocols = ["openid4vp-v1-unsigned", "openid4vp-v1-signed", "openid4vp-v1-multisigned", "org-iso-mdoc"];

    /// <summary>The issuance protocols (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredentialissuanceprotocol), which the user agent allows.</summary>
    internal static readonly string[] IssuanceProtocols = ["openid4vci-v1"];

    internal DigitalCredential(string protocol, object data)
    {
        Protocol = protocol;
        Data = data;
    }

    /// <summary>Gets the empty string: a digital credential has no identifier.</summary>
    public override string Id => "";

    /// <inheritdoc/>
    public override string Type => "digital";

    /// <summary>Gets the protocol of the request that the wallet answered.</summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-protocol"/>
    [IdlMember("protocol", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-protocol")]
    public string Protocol { get; }

    /// <summary>
    /// Gets the response of the wallet, specific to the protocol: the object parsed from its JSON (a JavaScript object for the
    /// scripts, a <see cref="System.Text.Json.Nodes.JsonObject"/> when the window has no script engine).
    /// </summary>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-data"/>
    [IdlMember("data", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-data")]
    public object Data { get; }

    /// <summary>
    /// Returns whether the user agent allows a protocol (https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-useragentallowsprotocol):
    /// the presentation protocols (<c>openid4vp-v1-unsigned</c>, <c>openid4vp-v1-signed</c>, <c>openid4vp-v1-multisigned</c>
    /// and <c>org-iso-mdoc</c>) and the issuance protocol <c>openid4vci-v1</c>.
    /// </summary>
    /// <param name="protocol">The identifier of the protocol.</param>
    /// <seealso href="https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-useragentallowsprotocol"/>
    [IdlMember("userAgentAllowsProtocol", "https://w3c-fedid.github.io/digital-credentials/#dom-digitalcredential-useragentallowsprotocol")]
    public static bool UserAgentAllowsProtocol(string protocol) => PresentationProtocols.Contains(protocol, StringComparer.Ordinal) || IssuanceProtocols.Contains(protocol, StringComparer.Ordinal);

    /// <summary>The digital credential requests of a window (https://w3c-fedid.github.io/digital-credentials/#dfn-request-coordinator).</summary>
    internal sealed class Coordinator
    {
        /// <summary>The active promise of the window, while a request is pending.</summary>
        public TaskCompletionSource<Credential?>? ActivePromise { get; set; }
    }

    /// <summary>
    /// [[DiscoverFromExternalSource]] and [[Create]] of DigitalCredential (https://w3c-fedid.github.io/digital-credentials/#sctn-discoverfromexternalsource,
    /// https://w3c-fedid.github.io/digital-credentials/#sctn-create): the requests of an allowed protocol are prepared and
    /// shown in the wallet (<see cref="UserAgentUIBackend.RequestDigitalCredentialAsync"/>). The returned task is settled by tasks
    /// of the event loop of the window.
    /// </summary>
    internal static Task<Credential?> Request(Window window, Coordinator coordinator, DigitalCredentialRequestKind kind, IEnumerable<(string Protocol, object Data)> requests, AbortSignal? signal)
    {
        // Filter the requests (https://w3c-fedid.github.io/digital-credentials/#dfn-filter-the-requests): the protocols that are
        // not a value of the enumeration of the kind of request, or that the user agent does not allow, are skipped
        var allowed = kind == DigitalCredentialRequestKind.Presentation ? PresentationProtocols : IssuanceProtocols;
        var filtered = requests.Where(request => allowed.Contains(request.Protocol, StringComparer.Ordinal)).ToList();
        if (filtered.Count == 0)
            return Task.FromException<Credential?>(new TypeErrorException("The options do not contain a request of a protocol that the user agent allows"));

        // https://w3c-fedid.github.io/digital-credentials/#dfn-prepare-credential-requests
        var document = window.Document;
        if (!document.IsFullyActive)
            return Task.FromException<Credential?>(DomException.InvalidState("The document is not fully active"));

        if (!window.HasTransientActivation)
            return Task.FromException<Credential?>(DomException.NotAllowed("A digital credential request requires a user activation"));

        window.ConsumeUserActivation();
        var result = new TaskCompletionSource<Credential?>();
        if (coordinator.ActivePromise is not null)
        {
            window.QueueGlobalTask(() => result.TrySetException(DomException.NotAllowed("A digital credential request is already pending")));
            return result.Task;
        }

        // Validate the requests (https://w3c-fedid.github.io/digital-credentials/#dfn-validate-the-requests): the data is serialized to JSON
        var validated = new List<DigitalCredentialRequest>(filtered.Count);
        try
        {
            foreach (var (protocol, data) in filtered)
            {
                var json = ModelContext.SerializeJson(window, data) ?? throw new TypeErrorException("The data of the request cannot be serialized to JSON");
                validated.Add(new DigitalCredentialRequest(protocol, json));
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return Task.FromException<Credential?>(ex is TypeErrorException or DomException ? ex : new TypeErrorException(ex.Message));
        }

        coordinator.ActivePromise = result;
        _ = result.Task.ContinueWith(_ =>
        {
            if (ReferenceEquals(coordinator.ActivePromise, result))
            {
                coordinator.ActivePromise = null;
            }
        }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        CredentialsContainer.Abort(signal, result, out var cancellationToken);
        if (result.Task.IsCompleted)
            return result.Task;

        // https://w3c-fedid.github.io/digital-credentials/#dfn-prepare-credential-requests, step 10: the request is aborted when
        // the document is no longer fully active
        void OnDocumentDestroyed() => result.TrySetException(new DomException("The document is no longer fully active", "AbortError"));
        document.AddPromptCloser(OnDocumentDestroyed);
        _ = result.Task.ContinueWith(_ => document.RemovePromptCloser(OnDocumentDestroyed), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

        var prompt = new DigitalCredentialPrompt(window, kind, validated);
        UserAgentUI.Show(
            window,
            (backend, token) => backend.RequestDigitalCredentialAsync(prompt, token),
            dismissed: null,
            response => Complete(window, result, response),
            cancellationToken);
        return result.Task;
    }

    /// <summary>https://w3c-fedid.github.io/digital-credentials/#dfn-initiate-the-request: the response of the wallet is parsed.</summary>
    private static void Complete(Window window, TaskCompletionSource<Credential?> result, DigitalCredentialResponse? response)
    {
        if (response is null)
        {
            result.TrySetException(DomException.NotAllowed("The user did not select a digital credential"));
            return;
        }

        object? data;
        bool isObject;
        try
        {
            data = window.ScriptEngine is { } engine ? engine.ParseJson(window, response.Data, out isObject) : DotNetJson.Parse(response.Data, out isObject);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            result.TrySetException(ex);
            return;
        }

        if (!isObject || data is null)
        {
            result.TrySetException(new TypeErrorException("The response of the wallet is not a JSON object"));
            return;
        }

        result.TrySetResult(new DigitalCredential(response.Protocol, data));
    }
}
