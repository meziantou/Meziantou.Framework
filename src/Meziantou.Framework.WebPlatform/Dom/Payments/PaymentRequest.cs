using System.Runtime.CompilerServices;
using Meziantou.Framework.WebPlatform.Scripting;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The state of a payment request (https://w3c.github.io/payment-request/#dfn-state).</summary>
internal enum PaymentRequestState
{
    /// <summary>The request is constructed and not shown.</summary>
    Created,

    /// <summary>The request is shown to the user.</summary>
    Interactive,

    /// <summary>The request completed (accepted, aborted or failed).</summary>
    Closed,
}

/// <summary>
/// A payment request (https://w3c.github.io/payment-request/#paymentrequest-interface): the page describes the accepted payment
/// methods and the details of the payment, and <see cref="Show"/> shows the payment sheet of the user agent. The payment
/// handlers and the payment sheet are the ones of the user agent UI backend (<see cref="BrowsingContextOptions.UserAgentUI"/>,
/// see <see cref="UserAgentUIBackend.HasPaymentHandlerAsync"/> and <see cref="UserAgentUIBackend.ShowPaymentSheetAsync"/>):
/// without a backend, no payment method is supported. The constructor throws a <c>SecurityError</c> <see cref="DomException"/>
/// when the document is not allowed to use the <c>payment</c> feature (its default allowlist is <c>'self'</c>).
/// </summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest"/>
[IdlInterface("PaymentRequest", "https://w3c.github.io/payment-request/#dom-paymentrequest")]
#pragma warning disable CA1001 // The cancellation source of the user interface is disposed when the user interface is closed
public sealed partial class PaymentRequest : EventTarget
#pragma warning restore CA1001
{
    /// <summary>The request whose payment sheet is shown, per payment-relevant browsing context (the top-level browsing context).</summary>
    private static readonly ConditionalWeakTable<BrowsingContext, PaymentRequest> ShowingRequests = [];

    /// <summary>The request whose payment sheet is shown, per document, to close the sheet when the document is unloaded.</summary>
    private static readonly ConditionalWeakTable<Document, PaymentRequest> ShowingRequestsByDocument = [];

    private static readonly Lock ShowingLock = new();

    private readonly Window _window;
    private readonly PaymentOptions _options;
    private readonly List<PaymentSheetMethod> _serializedMethodData;
    private PaymentSheetDetails _details;
    private TaskCompletionSource<PaymentResponse>? _acceptPromise;
    private PaymentSheetPrompt? _prompt;
    private CancellationTokenSource? _ui;
    private BrowsingContext? _showingContext;
    private Document? _showingDocument;
    private bool _manifestsIngested;

    /// <summary>The change of the user whose events are dispatched, completed once the page updated the details.</summary>
    private TaskCompletionSource<PaymentSheetDetails>? _pendingChange;

    /// <summary>The waiters of <see cref="PaymentSheetPrompt.WaitForUpdateAsync"/>.</summary>
    private List<TaskCompletionSource<PaymentSheetDetails>>? _updateWaiters;

    /// <summary>A response accepted while the details were being updated: it is accepted once the update completes.</summary>
    private PaymentSheetResponse? _pendingAccept;

    /// <summary>Creates a payment request for the current window (https://w3c.github.io/payment-request/#constructor).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/payment-request/#dom-paymentrequest-constructor")]
    public PaymentRequest(IReadOnlyList<PaymentMethodData> methodData, PaymentDetailsInit details, PaymentOptions? options = null)
        : this(GlobalScope.Current as Window ?? throw new InvalidOperationException("A payment request can only be created by a window"), methodData, details, options)
    {
    }

    /// <summary>
    /// Creates a payment request for a window (https://w3c.github.io/payment-request/#constructor): the payment methods are
    /// validated (a <see cref="TypeErrorException"/> when there is none, a <see cref="RangeErrorException"/> for an invalid or
    /// duplicate identifier), their data is serialized to JSON, and the amounts are checked (a <see cref="RangeErrorException"/>
    /// for a currency code that is not well-formed, a <see cref="TypeErrorException"/> for an invalid or negative total value).
    /// </summary>
    /// <param name="window">The window of the request.</param>
    /// <param name="methodData">The accepted payment methods.</param>
    /// <param name="details">The details of the payment.</param>
    /// <param name="options">The information to collect.</param>
    [NotInIdl("The .NET constructor whose window is explicit")]
    public PaymentRequest(Window window, IReadOnlyList<PaymentMethodData> methodData, PaymentDetailsInit details, PaymentOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(methodData);
        ArgumentNullException.ThrowIfNull(details);
        _window = window;
        options ??= new PaymentOptions();
        if (!PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.Payment))
            throw CreateException("The document is not allowed to use the payment feature", "SecurityError");

        var id = details.Id ?? Guid.NewGuid().ToString();

        // Process the payment methods
        if (methodData.Count == 0)
            throw new TypeErrorException("At least one payment method is required");

        var backend = window.Options.UserAgentUI;
        var seenPmis = new HashSet<string>(StringComparer.Ordinal);
        _serializedMethodData = new List<PaymentSheetMethod>(methodData.Count);
        foreach (var paymentMethod in methodData)
        {
            if (!PaymentMethodIdentifier.IsValid(paymentMethod.SupportedMethods))
                throw new RangeErrorException($"'{paymentMethod.SupportedMethods}' is not a valid payment method identifier");

            if (!seenPmis.Add(PaymentMethodIdentifier.GetComparisonKey(paymentMethod.SupportedMethods)))
                throw new RangeErrorException($"The payment method identifier '{paymentMethod.SupportedMethods}' is a duplicate");

            CheckSecurePaymentConfirmationIsAlone(seenPmis);

            // The .NET SecurePaymentConfirmationRequest dictionary has no JSON serialization: its data is an empty object
            var serializedData = paymentMethod.Data switch
            {
                null => null,
                SecurePaymentConfirmationRequest => "{}",
                _ => SerializeJson(window, paymentMethod.Data),
            };

            // The additional data type of the payment method: SecurePaymentConfirmationRequest, or the one defined by the
            // payment handlers of the backend
            if (serializedData is not null)
            {
                if (paymentMethod.SupportedMethods == SecurePaymentConfirmationMethod)
                {
                    _securePaymentConfirmation = ValidateSecurePaymentConfirmationData(window, paymentMethod.Data);
                }
                else
                {
                    backend?.ValidatePaymentMethodData(paymentMethod.SupportedMethods, serializedData);
                }
            }

            _serializedMethodData.Add(new PaymentSheetMethod(paymentMethod.SupportedMethods, serializedData));
        }

        // Process the total and the display items
        var total = PaymentAmounts.CheckItem(details.Total, total: true);
        var displayItems = CheckItems(details.DisplayItems) ?? [];

        // Process the shipping options
        string? selectedShippingOption = null;
        IReadOnlyList<PaymentShippingOption> shippingOptions = [];
        if (options.RequestShipping && details.ShippingOptions is { } detailsShippingOptions)
        {
            shippingOptions = CheckShippingOptions(detailsShippingOptions, out selectedShippingOption);
        }

        // Process the payment details modifiers
        var modifiers = details.Modifiers is { } detailsModifiers ? CheckModifiers(window, detailsModifiers, validateIdentifiers: false) : [];

        _options = options;
        _details = new PaymentSheetDetails(id, total, displayItems, shippingOptions, modifiers);
        State = PaymentRequestState.Created;
        ShippingOption = selectedShippingOption;
        ShippingType = options.RequestShipping ? options.ShippingType : null;
    }

    internal override GlobalScope? RelevantGlobal => _window;

    /// <summary>Gets the identifier of the request: the one given by the page, or a unique identifier generated by the user agent.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-id"/>
    [IdlMember("id", "https://w3c.github.io/payment-request/#dom-paymentrequest-id")]
    public string Id => _details.Id;

    /// <summary>Gets the shipping address chosen by the user (without the members that identify the recipient while the sheet is shown), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-shippingaddress"/>
    [IdlMember("shippingAddress", "https://w3c.github.io/payment-request/#dom-paymentrequest-shippingaddress")]
    public ContactAddress? ShippingAddress { get; private set; }

    /// <summary>Gets the identifier of the selected shipping option, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-shippingoption"/>
    [IdlMember("shippingOption", "https://w3c.github.io/payment-request/#dom-paymentrequest-shippingoption")]
    public string? ShippingOption { get; private set; }

    /// <summary>Gets the kind of address collected when the request requests shipping, or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-shippingtype"/>
    [IdlMember("shippingType", "https://w3c.github.io/payment-request/#dom-paymentrequest-shippingtype")]
    public PaymentShippingType? ShippingType { get; }

    /// <summary>Gets or sets the event handler of the <c>shippingaddresschange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-onshippingaddresschange"/>
    [IdlMember("onshippingaddresschange", "https://w3c.github.io/payment-request/#dom-paymentrequest-onshippingaddresschange")]
    public Action<Event>? OnShippingAddressChange
    {
        get => GetEventHandler("shippingaddresschange");
        set => SetEventHandler("shippingaddresschange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>shippingoptionchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-onshippingoptionchange"/>
    [IdlMember("onshippingoptionchange", "https://w3c.github.io/payment-request/#dom-paymentrequest-onshippingoptionchange")]
    public Action<Event>? OnShippingOptionChange
    {
        get => GetEventHandler("shippingoptionchange");
        set => SetEventHandler("shippingoptionchange", value);
    }

    /// <summary>Gets or sets the event handler of the <c>paymentmethodchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-onpaymentmethodchange"/>
    [IdlMember("onpaymentmethodchange", "https://w3c.github.io/payment-request/#dom-paymentrequest-onpaymentmethodchange")]
    public Action<Event>? OnPaymentMethodChange
    {
        get => GetEventHandler("paymentmethodchange");
        set => SetEventHandler("paymentmethodchange", value);
    }

    /// <summary>https://w3c.github.io/payment-request/#dfn-state</summary>
    internal PaymentRequestState State { get; private set; }

    /// <summary>https://w3c.github.io/payment-request/#dfn-updating</summary>
    internal bool IsUpdating { get; private set; }

    /// <summary>https://w3c.github.io/payment-request/#dfn-response</summary>
    internal PaymentResponse? Response { get; private set; }

    internal Window Window => _window;

    internal PaymentOptions Options => _options;

    internal PaymentSheetPrompt? Prompt => _prompt;

    /// <summary>
    /// Shows the payment sheet (https://w3c.github.io/payment-request/#show-method): the task completes with the response of
    /// the user once they accept the payment. It fails with a <c>SecurityError</c> <see cref="DomException"/> when the window has
    /// no transient activation (which is consumed), an <c>InvalidStateError</c> when the document is not fully active or the
    /// request was already shown, an <c>AbortError</c> when the document is hidden, another payment sheet is shown in the tab,
    /// the user dismisses the sheet or the page aborts the request, and a <c>NotSupportedError</c> when the user agent supports
    /// none of the payment methods. <paramref name="detailsPromise"/> updates the details once settled.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-show"/>
    [IdlMember("show", "https://w3c.github.io/payment-request/#dom-paymentrequest-show")]
    public Task<PaymentResponse> Show(Task<PaymentDetailsUpdate>? detailsPromise = null)
    {
        if (!_window.HasTransientActivation)
            return Task.FromException<PaymentResponse>(CreateException("The window has no transient activation", "SecurityError"));

        _window.ConsumeUserActivation();
        var document = _window.Document;
        if (!document.IsFullyActive)
            return Task.FromException<PaymentResponse>(CreateException("The document is not fully active", "InvalidStateError"));

        if (document.VisibilityState != "visible")
            return Task.FromException<PaymentResponse>(CreateException("The document is not visible", "AbortError"));

        if (State != PaymentRequestState.Created)
            return Task.FromException<PaymentResponse>(CreateException("The payment request was already shown", "InvalidStateError"));

        if (!TrySetShowing())
        {
            State = PaymentRequestState.Closed;
            return Task.FromException<PaymentResponse>(CreateException("A payment request is already showing", "AbortError"));
        }

        State = PaymentRequestState.Interactive;
        var acceptPromise = new TaskCompletionSource<PaymentResponse>();
        _acceptPromise = acceptPromise;
#pragma warning disable CA2000 // Disposed when the user interface is closed
        _ui = new CancellationTokenSource();
#pragma warning restore CA2000
        var token = _ui.Token;
        IngestPaymentMethodManifests();

        // The payment handlers of the payment methods (the queries are created on the event loop of the window)
        var queries = _serializedMethodData.ConvertAll(method => new PaymentHandlerQuery(_window, method));
        var securePaymentConfirmationCheck = CreateSecurePaymentConfirmationCheck();
        UserAgentUI.Show<IReadOnlyList<PaymentSheetMethod>>(
            _window,
            (backend, cancellationToken) => new ValueTask<IReadOnlyList<PaymentSheetMethod>>(GetSupportedMethodsAsync(backend, queries, securePaymentConfirmationCheck, cancellationToken)),
            dismissed: [],
            methods =>
            {
                if (State != PaymentRequestState.Interactive || token.IsCancellationRequested)
                    return;

                if (methods.Count == 0)
                {
                    State = PaymentRequestState.Closed;
                    CloseUserInterface();
                    acceptPromise.TrySetException(CreateException("The user agent supports none of the payment methods", "NotSupportedError"));
                    return;
                }

                // The payment handler of Secure Payment Confirmation shows the transaction dialog
                if (_securePaymentConfirmation is not null)
                {
                    ShowTransactionDialog(token);
                    return;
                }

                PresentUserInterface(methods, detailsPromise, token);
            },
            token);
        return acceptPromise.Task;
    }

    /// <summary>
    /// Aborts the request while the payment sheet is shown (https://w3c.github.io/payment-request/#abort-method): the sheet is
    /// closed and the task of <see cref="Show"/> fails with an <c>AbortError</c> <see cref="DomException"/>. The task fails with
    /// an <c>InvalidStateError</c> when the request is not shown or a payment is being retried.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-abort"/>
    [IdlMember("abort", "https://w3c.github.io/payment-request/#dom-paymentrequest-abort")]
    public Task Abort()
    {
        if (Response is { IsRetrying: true })
            return Task.FromException(CreateException("The payment is being retried", "InvalidStateError"));

        if (State != PaymentRequestState.Interactive)
            return Task.FromException(CreateException("The payment request is not shown", "InvalidStateError"));

        var promise = new TaskCompletionSource();
        CloseUserInterface();
        _window.QueueGlobalTask(() =>
        {
            State = PaymentRequestState.Closed;
            _acceptPromise?.TrySetException(CreateException("The payment request was aborted", "AbortError"));
            promise.TrySetResult();
        });
        return promise.Task;
    }

    /// <summary>
    /// Returns whether the user agent supports one of the payment methods (https://w3c.github.io/payment-request/#can-make-payment-algorithm),
    /// as answered by <see cref="UserAgentUIBackend.HasPaymentHandlerAsync"/>. The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when the request was shown or the document is not fully active.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequest-canmakepayment"/>
    [IdlMember("canMakePayment", "https://w3c.github.io/payment-request/#dom-paymentrequest-canmakepayment")]
    public Task<bool> CanMakePayment()
    {
        if (State != PaymentRequestState.Created)
            return Task.FromException<bool>(CreateException("The payment request was already shown", "InvalidStateError"));

        if (!_window.Document.IsFullyActive)
            return Task.FromException<bool>(CreateException("The document is not fully active", "InvalidStateError"));

        IngestPaymentMethodManifests();
        var result = new TaskCompletionSource<bool>();
        var queries = _serializedMethodData.ConvertAll(method => new PaymentHandlerQuery(_window, method));
        var securePaymentConfirmationCheck = CreateSecurePaymentConfirmationCheck();
        UserAgentUI.Show(
            _window,
            async (backend, cancellationToken) => (await GetSupportedMethodsAsync(backend, queries, securePaymentConfirmationCheck, cancellationToken).ConfigureAwait(false)).Count > 0,
            dismissed: false,
            result.SetResult);
        return result.Task;
    }

    /// <summary>
    /// The unloading document cleanup steps (https://w3c.github.io/payment-request/#show-method, "if document stops being fully
    /// active"): the payment sheet shown by the document is closed, and the request is closed.
    /// </summary>
    internal static void RunUnloadingDocumentCleanupSteps(Document document)
    {
        PaymentRequest? request;
        lock (ShowingLock)
        {
            if (!ShowingRequestsByDocument.TryGetValue(document, out request))
                return;
        }

        request.OnDocumentUnloaded();
    }

    /// <summary>Creates a <see cref="DomException"/> of the realm of the window of the request.</summary>
    internal DomException CreateException(string message, string name) => new(message, name) { RealmWindow = _window };

    /// <summary>https://infra.spec.whatwg.org/#serialize-a-javascript-value-to-a-json-string: a value that JSON.stringify() serializes to undefined is a TypeError.</summary>
    internal static string SerializeJson(GlobalScope global, object? value)
        => ModelContext.SerializeJson(global, value) ?? throw new TypeErrorException("The value cannot be serialized to JSON");

    /// <summary>https://infra.spec.whatwg.org/#parse-a-json-string-to-a-javascript-value, in the realm of the window.</summary>
    internal static object? ParseJson(GlobalScope global, string json)
        => global.ScriptEngine is { } engine ? engine.ParseJson(global, json, out _) : DotNetJson.Parse(json, out _);

    private static List<PaymentItem>? CheckItems(IReadOnlyList<PaymentItem>? items) => items?.Select(item => PaymentAmounts.CheckItem(item, total: false)).ToList();

    private static List<PaymentShippingOption> CheckShippingOptions(IReadOnlyList<PaymentShippingOption> options, out string? selectedShippingOption)
    {
        selectedShippingOption = null;
        var seenIds = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<PaymentShippingOption>(options.Count);
        foreach (var option in options)
        {
            var amount = PaymentAmounts.CheckAndCanonicalize(option.Amount);
            if (!seenIds.Add(option.Id))
                throw new TypeErrorException($"The shipping option identifier '{option.Id}' is a duplicate");

            if (option.Selected)
            {
                selectedShippingOption = option.Id;
            }

            result.Add(new PaymentShippingOption { Id = option.Id, Label = option.Label, Amount = amount, Selected = option.Selected });
        }

        return result;
    }

    private static List<PaymentSheetModifier> CheckModifiers(GlobalScope global, IReadOnlyList<PaymentDetailsModifier> modifiers, bool validateIdentifiers)
    {
        var result = new List<PaymentSheetModifier>(modifiers.Count);
        foreach (var modifier in modifiers)
        {
            if (validateIdentifiers && !PaymentMethodIdentifier.IsValid(modifier.SupportedMethods))
                throw new RangeErrorException($"'{modifier.SupportedMethods}' is not a valid payment method identifier");

            var total = modifier.Total is { } modifierTotal ? PaymentAmounts.CheckItem(modifierTotal, total: true) : null;
            var additionalDisplayItems = CheckItems(modifier.AdditionalDisplayItems) ?? [];
            var serializedData = modifier.Data is null ? null : SerializeJson(global, modifier.Data);
            result.Add(new PaymentSheetModifier(modifier.SupportedMethods, total, additionalDisplayItems, serializedData));
        }

        return result;
    }

    /// <summary>
    /// The payment method consultation (https://w3c.github.io/payment-request/#show-method, "for each paymentMethod"): the
    /// methods for which the backend has a payment handler. The queries are sent at once, from the event loop of the window.
    /// <paramref name="securePaymentConfirmationCheck"/> runs the steps to check if a payment can be made of Secure Payment
    /// Confirmation once the backend supports it.
    /// </summary>
    private static async Task<IReadOnlyList<PaymentSheetMethod>> GetSupportedMethodsAsync(UserAgentUIBackend backend, List<PaymentHandlerQuery> queries, Func<CancellationToken, Task<bool>>? securePaymentConfirmationCheck, CancellationToken cancellationToken)
    {
        var answers = new Task<bool>[queries.Count];
        for (var i = 0; i < queries.Count; i++)
        {
            try
            {
                answers[i] = backend.HasPaymentHandlerAsync(queries[i], cancellationToken).AsTask();
            }
#pragma warning disable CA1031 // A failure of the backend is an unsupported method
            catch (Exception)
#pragma warning restore CA1031
            {
                answers[i] = Task.FromResult(false);
            }
        }

        var result = new List<PaymentSheetMethod>(queries.Count);
        for (var i = 0; i < queries.Count; i++)
        {
            try
            {
                if (await answers[i].ConfigureAwait(false))
                {
                    result.Add(queries[i].Method);
                }
            }
#pragma warning disable CA1031 // A failure of the backend is an unsupported method
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
#pragma warning restore CA1031
            {
            }
        }

        if (securePaymentConfirmationCheck is not null && result.FindIndex(method => method.Identifier == SecurePaymentConfirmationMethod) is var index and >= 0 && !await securePaymentConfirmationCheck(cancellationToken).ConfigureAwait(false))
        {
            result.RemoveAt(index);
        }

        return result;
    }

    /// <summary>
    /// Ingests the payment method manifests of the URL-based payment method identifiers (https://w3c.github.io/payment-method-manifest/#ingest-payment-method-manifests)
    /// with the backend. The specification ingests them when the request is constructed; as in Chromium, they are ingested
    /// when the page asks whether it can make a payment (<c>canMakePayment()</c>, <c>show()</c>), once per request.
    /// </summary>
    private void IngestPaymentMethodManifests()
    {
        if (_manifestsIngested || _window.Options.UserAgentUI is not { } backend)
            return;

        _manifestsIngested = true;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var method in _serializedMethodData)
        {
            if (Urls.UrlParser.Parse(method.Identifier, baseUrl: null) is not { } url || !PaymentMethodIdentifier.IsValidUrlBased(url) || !seen.Add(url.Serialize()))
                continue;

            var request = new PaymentMethodManifestRequest(_window, url);

            // The event loop is not idle while the manifests are fetched (as for the other fetches): its timers do not run ahead of them
            var eventLoop = _window.EventLoop;
            eventLoop.BeginOperation();
            try
            {
                _ = backend.IngestPaymentMethodManifestAsync(request, CancellationToken.None).AsTask().ContinueWith(
                    task =>
                    {
                        _ = task.Exception;
                        eventLoop.EndOperation();
                    },
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);
            }
#pragma warning disable CA1031 // A failure of the backend is ignored
            catch (Exception)
#pragma warning restore CA1031
            {
                eventLoop.EndOperation();
            }
        }
    }

    /// <summary>
    /// Presents the payment sheet (https://w3c.github.io/payment-request/#show-method, "present a user interface"), and runs
    /// the update of the details with the promise given to <c>show()</c>.
    /// </summary>
    private void PresentUserInterface(IReadOnlyList<PaymentSheetMethod> methods, Task<PaymentDetailsUpdate>? detailsPromise, CancellationToken token)
    {
        var prompt = new PaymentSheetPrompt(_window, this, methods, _options, _details, ShippingOption);
        _prompt = prompt;
        if (detailsPromise is not null)
        {
            UpdateDetails(detailsPromise, pmi: null);
        }

        if (State != PaymentRequestState.Interactive || token.IsCancellationRequested)
            return;

        UserAgentUI.Show(
            _window,
            (backend, cancellationToken) => backend.ShowPaymentSheetAsync(prompt, cancellationToken),
            dismissed: null,
            response =>
            {
                if (response is null)
                {
                    UserAbortsPaymentRequest();
                }
                else
                {
                    UserAcceptsPaymentRequest(response);
                }
            },
            token);
    }

    /// <summary>Shows the payment sheet again for <c>PaymentResponse.retry()</c> (https://w3c.github.io/payment-request/#retry-method).</summary>
    internal void Retry(PaymentSheetErrors? errors)
    {
        State = PaymentRequestState.Interactive;
#pragma warning disable CA2000 // Disposed when the user interface is closed
        _ui = new CancellationTokenSource();
#pragma warning restore CA2000
        var token = _ui.Token;
        if (_prompt is not { } prompt)
            return;

        prompt.Errors = errors;
        UserAgentUI.Show(
            _window,
            (backend, cancellationToken) => backend.RetryPaymentAsync(prompt, cancellationToken),
            dismissed: null,
            response =>
            {
                if (response is null)
                {
                    UserAbortsPaymentRequest();
                }
                else
                {
                    UserAcceptsPaymentRequest(response);
                }
            },
            token);
    }

    /// <summary>Closes the payment sheet for <c>PaymentResponse.complete()</c> (https://w3c.github.io/payment-request/#complete-method).</summary>
    internal void Complete(PaymentComplete result, string serializedData, TaskCompletionSource promise)
    {
        if (_prompt is not { } prompt)
        {
            ClearShowing();
            _window.QueueGlobalTask(() => promise.TrySetResult());
            return;
        }

#pragma warning disable CA2000 // Disposed when the user interface is closed
        var cancellation = new CancellationTokenSource();
#pragma warning restore CA2000
        _ui = cancellation;
        UserAgentUI.Show(
            _window,
            async (backend, cancellationToken) =>
            {
                await backend.CompletePaymentAsync(prompt, result, serializedData, cancellationToken).ConfigureAwait(false);
                return true;
            },
            dismissed: true,
            _ =>
            {
                ClearShowing();
                if (ReferenceEquals(_ui, cancellation))
                {
                    DisposeUserInterface();
                }

                promise.TrySetResult();
            },
            cancellation.Token);
    }

    /// <summary>https://w3c.github.io/payment-request/#user-accepts-the-payment-request-algorithm</summary>
    private void UserAcceptsPaymentRequest(PaymentSheetResponse answer)
    {
        // The user interface does not let the user accept the payment while the details are being updated: the payment is
        // accepted once the update completes
        if (IsUpdating)
        {
            _pendingAccept = answer;
            return;
        }

        if (State != PaymentRequestState.Interactive)
            return;

        var shippingAddress = answer.ShippingAddress?.Copy() ?? _prompt?.ShippingAddress;
        if (_options.RequestShipping && (shippingAddress is null || ShippingOption is null))
        {
            UserAbortsPaymentRequest();
            return;
        }

        object? details;
        try
        {
            details = ParseJson(_window, answer.Details);
        }
#pragma warning disable CA1031 // Details that are not JSON are an internal error of the payment handler
        catch (Exception)
#pragma warning restore CA1031
        {
            PaymentHandlerIndicatesInternalError();
            return;
        }

        var isRetry = Response is not null;
        var response = Response ?? new PaymentResponse(this, _details.Id);
        Response = response;
        if (_options.RequestShipping)
        {
            ShippingAddress = shippingAddress;
        }

        response.Accept(
            answer.MethodName,
            details,
            _options.RequestShipping ? shippingAddress : null,
            _options.RequestShipping ? ShippingOption : null,
            _options.RequestPayerName ? answer.PayerName : null,
            _options.RequestPayerEmail ? answer.PayerEmail : null,
            _options.RequestPayerPhone ? answer.PayerPhone : null);
        if (_prompt is { } prompt)
        {
            prompt.Response = answer;
        }

        State = PaymentRequestState.Closed;
        DisposeUserInterface();
        if (isRetry)
        {
            response.ResolveRetry();
        }
        else
        {
            _acceptPromise?.TrySetResult(response);
        }
    }

    /// <summary>https://w3c.github.io/payment-request/#user-aborts-the-payment-request-algorithm</summary>
    private void UserAbortsPaymentRequest() => Fail(CreateException("The user aborted the payment request", "AbortError"));

    /// <summary>https://w3c.github.io/payment-request/#payment-handler-indicates-an-internal-error-algorithm</summary>
    private void PaymentHandlerIndicatesInternalError() => Fail(CreateException("The payment handler failed", "OperationError"));

    /// <summary>Closes the request and rejects its pending promise (the promise of <c>show()</c> or of <c>retry()</c>).</summary>
    private void Fail(Exception error)
    {
        if (State != PaymentRequestState.Interactive)
            return;

        State = PaymentRequestState.Closed;
        CloseUserInterface();
        if (Response is { } response)
        {
            response.IsComplete = true;
            response.RejectRetry(error);
        }
        else
        {
            _acceptPromise?.TrySetException(error);
        }
    }

    /// <summary>
    /// The document of the request is no longer fully active while the payment sheet is shown: the sheet is closed, and the
    /// pending promise is rejected with an <c>AbortError</c>.
    /// </summary>
    private void OnDocumentUnloaded()
    {
        var error = CreateException("The document is not fully active", "AbortError");
        if (State == PaymentRequestState.Interactive)
        {
            Fail(error);
        }
        else
        {
            CloseUserInterface();
        }

        Response?.OnDocumentUnloaded(error);
    }

    /// <summary>
    /// https://w3c.github.io/payment-request/#update-a-paymentrequest-s-details-algorithm: the user interface waits until the
    /// promise is settled, then the details are validated and updated. A rejected promise aborts the update with an
    /// <c>AbortError</c>; a value that cannot be converted, or that is not valid, aborts it with the exception.
    /// </summary>
    internal void UpdateDetails(Task<PaymentDetailsUpdate> detailsPromise, string? pmi)
    {
        IsUpdating = true;
        if (_prompt is { } prompt)
        {
            prompt.IsUpdating = true;
        }

        _ = detailsPromise.ContinueWith(
            settled => OnDetailsSettled(settled, pmi),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void OnDetailsSettled(Task<PaymentDetailsUpdate> settled, string? pmi)
    {
        if (State != PaymentRequestState.Interactive)
        {
            EndUpdate();
            return;
        }

        if (!settled.IsCompletedSuccessfully)
        {
            // A rejected promise aborts the update with an AbortError; the exception of the conversion of its value to a
            // PaymentDetailsUpdate is rethrown
            var exception = settled.Exception?.InnerException;
            AbortUpdate(exception is null || settled.IsCanceled || PromiseRejections.IsRejection(exception) ? CreateException("The promise of the details was rejected", "AbortError") : exception);
            return;
        }

        var details = settled.Result;
        PaymentSheetDetails updated;
        PaymentSheetErrors errors;
        string? selectedShippingOption = null;
        var shippingOptionsUpdated = false;
        try
        {
            var total = details.Total is { } detailsTotal ? PaymentAmounts.CheckItem(detailsTotal, total: true) : null;
            var displayItems = CheckItems(details.DisplayItems);
            List<PaymentShippingOption>? shippingOptions = null;
            if (details.ShippingOptions is { } detailsShippingOptions && _options.RequestShipping)
            {
                shippingOptions = CheckShippingOptions(detailsShippingOptions, out selectedShippingOption);
                shippingOptionsUpdated = true;
            }

            var modifiers = details.Modifiers is { } detailsModifiers ? CheckModifiers(_window, detailsModifiers, validateIdentifiers: true) : null;

            // The errors of the payment method are converted to the type defined by the payment method (pmi): the backend
            // receives them as JSON
            var paymentMethodErrors = details.PaymentMethodErrors is { } methodErrors && pmi is not null ? SerializeJson(_window, methodErrors) : null;
            updated = _details.With(total, displayItems, shippingOptions, modifiers);
            errors = new PaymentSheetErrors { Error = details.Error, ShippingAddress = details.ShippingAddressErrors, Payer = details.PayerErrors, PaymentMethod = paymentMethodErrors };
        }
#pragma warning disable CA1031 // The exceptions abort the update (including the exceptions of JSON.stringify())
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            AbortUpdate(ex);
            return;
        }

        _details = updated;
        if (shippingOptionsUpdated)
        {
            ShippingOption = selectedShippingOption;
        }

        if (_prompt is { } prompt)
        {
            prompt.Details = updated;
            prompt.Errors = errors;
            prompt.ShippingOption = ShippingOption;
        }

        EndUpdate();
        if (_pendingAccept is { } pendingAccept)
        {
            _pendingAccept = null;
            UserAcceptsPaymentRequest(pendingAccept);
        }
    }

    /// <summary>The update is complete: the user interface is enabled again, and the waiters receive the details.</summary>
    private void EndUpdate()
    {
        IsUpdating = false;
        if (_prompt is { } prompt)
        {
            prompt.IsUpdating = false;
        }

        _pendingChange?.TrySetResult(_details);
        _pendingChange = null;
        if (_updateWaiters is { } waiters)
        {
            _updateWaiters = null;
            foreach (var waiter in waiters)
            {
                waiter.TrySetResult(_details);
            }
        }
    }

    /// <summary>https://w3c.github.io/payment-request/#abort-the-update</summary>
    private void AbortUpdate(Exception exception)
    {
        CloseUserInterface();
        _window.QueueGlobalTask(() =>
        {
            ClearShowing();
            if (State == PaymentRequestState.Interactive)
            {
                State = PaymentRequestState.Closed;
                if (Response is { } response)
                {
                    response.IsComplete = true;
                    response.RejectRetry(exception);
                }
                else
                {
                    _acceptPromise?.TrySetException(exception);
                }
            }

            IsUpdating = false;
            _pendingAccept = null;
        });
    }

    /// <summary>Waits until the details are not being updated (<see cref="PaymentSheetPrompt.WaitForUpdateAsync"/>).</summary>
    internal Task<PaymentSheetDetails> WaitForUpdateAsync(CancellationToken cancellationToken)
    {
        var waiter = new TaskCompletionSource<PaymentSheetDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiToken = _ui?.Token ?? CancellationToken.None;
        _window.QueueGlobalTask(() =>
        {
            if (!IsUpdating)
            {
                waiter.TrySetResult(_details);
                return;
            }

            (_updateWaiters ??= []).Add(waiter);
        });
        return WaitAsync(waiter.Task, uiToken, cancellationToken);
    }

    /// <summary>https://w3c.github.io/payment-request/#shipping-address-changed-algorithm</summary>
    internal Task<PaymentSheetDetails> ChangeShippingAddressAsync(ContactAddress address, CancellationToken cancellationToken)
        => RunUserChange(() =>
        {
            if (!_options.RequestShipping)
                return "The payment request does not request shipping";

            if (_prompt is { } prompt)
            {
                prompt.ShippingAddress = address;
            }

            ShippingAddress = address.Redact();
            return null;
        }, () => new PaymentRequestUpdateEvent("shippingaddresschange"), target: null, cancellationToken);

    /// <summary>https://w3c.github.io/payment-request/#shipping-option-changed-algorithm</summary>
    internal Task<PaymentSheetDetails> ChangeShippingOptionAsync(string shippingOptionId, CancellationToken cancellationToken)
        => RunUserChange(() =>
        {
            if (!_options.RequestShipping)
                return "The payment request does not request shipping";

            if (!_details.ShippingOptions.Any(option => option.Id == shippingOptionId))
                return $"'{shippingOptionId}' is not a shipping option of the payment request";

            ShippingOption = shippingOptionId;
            if (_prompt is { } prompt)
            {
                prompt.ShippingOption = shippingOptionId;
            }

            return null;
        }, () => new PaymentRequestUpdateEvent("shippingoptionchange"), target: null, cancellationToken);

    /// <summary>https://w3c.github.io/payment-request/#payment-method-changed-algorithm</summary>
    internal Task<PaymentSheetDetails> ChangePaymentMethodAsync(string methodName, string? methodDetails, CancellationToken cancellationToken)
    {
        object? details = null;
        return RunUserChange(() =>
        {
            try
            {
                details = methodDetails is null ? null : ParseJson(_window, methodDetails);
            }
#pragma warning disable CA1031 // Details that are not JSON are a bug of the backend
            catch (Exception)
#pragma warning restore CA1031
            {
                return "The details of the payment method are not JSON";
            }

            return null;
        }, () => new PaymentMethodChangeEvent("paymentmethodchange", new PaymentMethodChangeEventInit { MethodName = methodName, MethodDetails = details }), target: null, cancellationToken);
    }

    /// <summary>https://w3c.github.io/payment-request/#payer-detail-changed-algorithm</summary>
    internal Task<PaymentSheetDetails> ChangePayerDetailsAsync(string? payerName, string? payerEmail, string? payerPhone, CancellationToken cancellationToken)
        => RunUserChange(() =>
        {
            if (Response is not { } response)
                return "The payment was not accepted";

            response.ChangePayerDetails(_options.RequestPayerName ? payerName : null, _options.RequestPayerEmail ? payerEmail : null, _options.RequestPayerPhone ? payerPhone : null);
            return null;
        }, () => new PaymentRequestUpdateEvent("payerdetailchange"), target: () => Response, cancellationToken);

    /// <summary>
    /// Runs a change of the user from a task of the event loop (https://w3c.github.io/payment-request/#paymentrequest-updated-algorithm):
    /// <paramref name="apply"/> updates the request (or returns why the change is not possible), then the event is dispatched,
    /// and the task completes once the page updated the details with <c>updateWith()</c>, or at once.
    /// </summary>
    private Task<PaymentSheetDetails> RunUserChange(Func<string?> apply, Func<PaymentRequestUpdateEvent> createEvent, Func<EventTarget?>? target, CancellationToken cancellationToken)
    {
        var change = new TaskCompletionSource<PaymentSheetDetails>(TaskCreationOptions.RunContinuationsAsynchronously);
        var uiToken = _ui?.Token ?? CancellationToken.None;
        _window.QueueGlobalTask(() =>
        {
            if (State != PaymentRequestState.Interactive || uiToken.IsCancellationRequested)
            {
                change.TrySetException(new InvalidOperationException("The payment request is not interactive"));
                return;
            }

            if (IsUpdating || _pendingChange is not null)
            {
                change.TrySetException(new InvalidOperationException("Another change of the payment request is pending"));
                return;
            }

            if (apply() is { } error)
            {
                change.TrySetException(new InvalidOperationException(error));
                return;
            }

            var @event = createEvent();
            _pendingChange = change;
            (target?.Invoke() ?? this).FireEvent(@event);
            if (!@event.WaitForUpdate)
            {
                @event.WaitForUpdate = true;
                if (ReferenceEquals(_pendingChange, change))
                {
                    _pendingChange = null;
                }

                change.TrySetResult(_details);
            }
        });
        return WaitAsync(change.Task, uiToken, cancellationToken);
    }

    /// <summary>A task canceled when the user interface is closed or the caller stops waiting.</summary>
    private static async Task<PaymentSheetDetails> WaitAsync(Task<PaymentSheetDetails> task, CancellationToken uiToken, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(uiToken, cancellationToken);
        return await task.WaitAsync(linked.Token).ConfigureAwait(false);
    }

    /// <summary>
    /// Sets the "payment request is showing" boolean of the payment-relevant browsing context (the top-level browsing context
    /// of the window): false when another request is showing.
    /// </summary>
    private bool TrySetShowing()
    {
        var document = _window.Document;
        if (document.BrowsingContext?.Top is not { } top)
            return false;

        lock (ShowingLock)
        {
            if (ShowingRequests.TryGetValue(top, out var showing) && !ReferenceEquals(showing, this))
                return false;

            ShowingRequests.AddOrUpdate(top, this);
            ShowingRequestsByDocument.AddOrUpdate(document, this);
            _showingContext = top;
            _showingDocument = document;
        }

        return true;
    }

    /// <summary>Sets the "payment request is showing" boolean to false.</summary>
    private void ClearShowing()
    {
        lock (ShowingLock)
        {
            if (_showingContext is { } context && ShowingRequests.TryGetValue(context, out var showing) && ReferenceEquals(showing, this))
            {
                ShowingRequests.Remove(context);
            }

            if (_showingDocument is { } document && ShowingRequestsByDocument.TryGetValue(document, out showing) && ReferenceEquals(showing, this))
            {
                ShowingRequestsByDocument.Remove(document);
            }

            _showingContext = null;
            _showingDocument = null;
        }
    }

    /// <summary>Closes down the user interface: the prompt of the backend is canceled, and the "payment request is showing" boolean is false.</summary>
    private void CloseUserInterface()
    {
        ClearShowing();
        _pendingAccept = null;
        _pendingChange?.TrySetCanceled();
        _pendingChange = null;
        if (_updateWaiters is { } waiters)
        {
            _updateWaiters = null;
            foreach (var waiter in waiters)
            {
                waiter.TrySetCanceled();
            }
        }

        DisposeUserInterface(cancel: true);
    }

    /// <summary>Releases the cancellation of the current prompt of the backend (the prompt answered, or is canceled).</summary>
    private void DisposeUserInterface(bool cancel = false)
    {
        if (_ui is not { } ui)
            return;

        _ui = null;
        if (cancel)
        {
            ui.Cancel();
        }

        ui.Dispose();
    }
}
