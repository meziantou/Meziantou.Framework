namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The data that a payment handler may provide instead of the payment sheet (https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation).</summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation"/>
[IdlInterface("PaymentDelegation", "https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation")]
public enum PaymentDelegation
{
    /// <summary>The shipping address.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-shippingaddress"/>
    [IdlMember("shippingAddress", "https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-shippingaddress")]
    ShippingAddress,

    /// <summary>The name of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payername"/>
    [IdlMember("payerName", "https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payername")]
    PayerName,

    /// <summary>The phone number of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payerphone"/>
    [IdlMember("payerPhone", "https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payerphone")]
    PayerPhone,

    /// <summary>The email address of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payeremail"/>
    [IdlMember("payerEmail", "https://w3c.github.io/web-based-payment-handler/#dom-paymentdelegation-payeremail")]
    PayerEmail,
}

/// <summary>
/// The payment handler of a service worker registration (https://w3c.github.io/web-based-payment-handler/#paymentmanager-interface):
/// <c>registration.paymentManager</c>. The library records the hint and the delegations of the payment handler; it does not
/// invoke the payment handlers of the service workers (the <c>canmakepayment</c> and <c>paymentrequest</c> events are never
/// fired by the user agent).
/// </summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#paymentmanager-interface"/>
[IdlInterface("PaymentManager", "https://w3c.github.io/web-based-payment-handler/#paymentmanager-interface")]
public sealed class PaymentManager
{
    private readonly HashSet<PaymentDelegation> _delegations = [];

    internal PaymentManager()
    {
    }

    /// <summary>Gets or sets the hint shown next to the name of the payment handler (e.g. <c>**** 1234</c>).</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentmanager-userhint"/>
    [IdlMember("userHint", "https://w3c.github.io/web-based-payment-handler/#dom-paymentmanager-userhint")]
    public string UserHint { get; set; } = "";

    /// <summary>Gets the data that the payment handler provides instead of the payment sheet.</summary>
    [NotInIdl("The delegations recorded by enableDelegations(), which the scripts cannot read")]
    public IReadOnlyCollection<PaymentDelegation> Delegations => _delegations;

    /// <summary>
    /// Records the data that the payment handler provides (https://w3c.github.io/web-based-payment-handler/#dom-paymentmanager-enabledelegations).
    /// </summary>
    /// <param name="delegations">The delegations.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentmanager-enabledelegations"/>
    [IdlMember("enableDelegations", "https://w3c.github.io/web-based-payment-handler/#dom-paymentmanager-enabledelegations")]
    public Task EnableDelegations(IReadOnlyList<PaymentDelegation> delegations)
    {
        ArgumentNullException.ThrowIfNull(delegations);
        _delegations.UnionWith(delegations);
        return Task.CompletedTask;
    }
}

/// <summary>
/// The event fired at the service worker of a payment handler to ask whether it can make a payment
/// (https://w3c.github.io/web-based-payment-handler/#the-canmakepaymentevent). The library never fires it: an event created by a
/// script cannot be answered.
/// </summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#the-canmakepaymentevent"/>
[IdlInterface("CanMakePaymentEvent", "https://w3c.github.io/web-based-payment-handler/#the-canmakepaymentevent")]
public sealed class CanMakePaymentEvent : ExtendableEvent
{
    private bool _respondWithCalled;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-canmakepaymentevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/web-based-payment-handler/#dom-canmakepaymentevent-constructor")]
    public CanMakePaymentEvent(string type)
        : base(type)
    {
    }

    /// <summary>
    /// Answers whether the payment handler can make the payment (https://w3c.github.io/web-based-payment-handler/#dom-canmakepaymentevent-respondwith).
    /// Throws an <c>InvalidStateError</c> <see cref="DomException"/> when the event is not trusted, is not being dispatched or
    /// was already answered.
    /// </summary>
    /// <param name="canMakePaymentResponse">The answer.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-canmakepaymentevent-respondwith"/>
    [IdlMember("respondWith", "https://w3c.github.io/web-based-payment-handler/#dom-canmakepaymentevent-respondwith")]
    public void RespondWith(Task<bool> canMakePaymentResponse)
    {
        ArgumentNullException.ThrowIfNull(canMakePaymentResponse);
        PaymentHandlerEvents.CheckCanRespond(this, ref _respondWithCalled);
        AddLifetimePromise(canMakePaymentResponse);
    }
}

/// <summary>The initialization dictionary of a <see cref="PaymentRequestEvent"/> (https://w3c.github.io/web-based-payment-handler/#paymentrequesteventinit-dictionary).</summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#paymentrequesteventinit-dictionary"/>
[IdlInterface("PaymentRequestEventInit", "https://w3c.github.io/web-based-payment-handler/#paymentrequesteventinit-dictionary")]
public sealed class PaymentRequestEventInit : ExtendableEventInit
{
    /// <summary>Gets the origin of the top-level document of the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-toporigin"/>
    [IdlMember("topOrigin", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-toporigin")]
    public string? TopOrigin { get; init; }

    /// <summary>Gets the origin of the document that created the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentrequestorigin"/>
    [IdlMember("paymentRequestOrigin", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentrequestorigin")]
    public string? PaymentRequestOrigin { get; init; }

    /// <summary>Gets the identifier of the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentrequestid"/>
    [IdlMember("paymentRequestId", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentrequestid")]
    public string? PaymentRequestId { get; init; }

    /// <summary>Gets the payment methods of the request that the payment handler supports.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-methoddata"/>
    [IdlMember("methodData", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-methoddata")]
    public IReadOnlyList<PaymentMethodData>? MethodData { get; init; }

    /// <summary>Gets the total of the payment.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-total"/>
    [IdlMember("total", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-total")]
    public PaymentCurrencyAmount? Total { get; init; }

    /// <summary>Gets the modifiers of the payment methods that the payment handler supports.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-modifiers"/>
    [IdlMember("modifiers", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-modifiers")]
    public IReadOnlyList<PaymentDetailsModifier>? Modifiers { get; init; }

    /// <summary>Gets the information that the payment handler collects.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentoptions"/>
    [IdlMember("paymentOptions", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-paymentoptions")]
    public PaymentOptions? PaymentOptions { get; init; }

    /// <summary>Gets the shipping options.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-shippingoptions"/>
    [IdlMember("shippingOptions", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequesteventinit-shippingoptions")]
    public IReadOnlyList<PaymentShippingOption>? ShippingOptions { get; init; }
}

/// <summary>The details updated by the merchant while a payment handler handles a payment (https://w3c.github.io/web-based-payment-handler/#paymentrequestdetailsupdate-dictionary).</summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#paymentrequestdetailsupdate-dictionary"/>
[IdlInterface("PaymentRequestDetailsUpdate", "https://w3c.github.io/web-based-payment-handler/#paymentrequestdetailsupdate-dictionary")]
public sealed class PaymentRequestDetailsUpdate
{
    /// <summary>Gets the error to show to the user.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-error"/>
    [IdlMember("error", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-error")]
    public string? Error { get; init; }

    /// <summary>Gets the updated total.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-total"/>
    [IdlMember("total", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-total")]
    public PaymentCurrencyAmount? Total { get; init; }

    /// <summary>Gets the updated modifiers.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-modifiers"/>
    [IdlMember("modifiers", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-modifiers")]
    public IReadOnlyList<PaymentDetailsModifier>? Modifiers { get; init; }

    /// <summary>Gets the updated shipping options.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-shippingoptions"/>
    [IdlMember("shippingOptions", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-shippingoptions")]
    public IReadOnlyList<PaymentShippingOption>? ShippingOptions { get; init; }

    /// <summary>Gets the errors of the payment method.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-paymentmethoderrors"/>
    [IdlMember("paymentMethodErrors", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-paymentmethoderrors")]
    public object? PaymentMethodErrors { get; init; }

    /// <summary>Gets the errors of the shipping address.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-shippingaddresserrors"/>
    [IdlMember("shippingAddressErrors", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestdetailsupdate-shippingaddresserrors")]
    public AddressErrors? ShippingAddressErrors { get; init; }
}

/// <summary>An address given by a payment handler (https://w3c.github.io/web-based-payment-handler/#addressinit-dictionary).</summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#addressinit-dictionary"/>
[IdlInterface("AddressInit", "https://w3c.github.io/web-based-payment-handler/#addressinit-dictionary")]
public sealed class AddressInit
{
    /// <summary>Gets the country code.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-country"/>
    [IdlMember("country", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-country")]
    public string Country { get; init; } = "";

    /// <summary>Gets the lines of the street address.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-addressline"/>
    [IdlMember("addressLine", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-addressline")]
    public IReadOnlyList<string> AddressLine { get; init; } = [];

    /// <summary>Gets the region.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-region"/>
    [IdlMember("region", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-region")]
    public string Region { get; init; } = "";

    /// <summary>Gets the city.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-city"/>
    [IdlMember("city", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-city")]
    public string City { get; init; } = "";

    /// <summary>Gets the dependent locality.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-dependentlocality"/>
    [IdlMember("dependentLocality", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-dependentlocality")]
    public string DependentLocality { get; init; } = "";

    /// <summary>Gets the postal code.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-postalcode"/>
    [IdlMember("postalCode", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-postalcode")]
    public string PostalCode { get; init; } = "";

    /// <summary>Gets the sorting code.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-sortingcode"/>
    [IdlMember("sortingCode", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-sortingcode")]
    public string SortingCode { get; init; } = "";

    /// <summary>Gets the organization.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-organization"/>
    [IdlMember("organization", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-organization")]
    public string Organization { get; init; } = "";

    /// <summary>Gets the recipient.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-recipient"/>
    [IdlMember("recipient", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-recipient")]
    public string Recipient { get; init; } = "";

    /// <summary>Gets the phone number.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-addressinit-phone"/>
    [IdlMember("phone", "https://w3c.github.io/web-based-payment-handler/#dom-addressinit-phone")]
    public string Phone { get; init; } = "";
}

/// <summary>The response of a payment handler (https://w3c.github.io/web-based-payment-handler/#paymenthandlerresponse-dictionary).</summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#paymenthandlerresponse-dictionary"/>
[IdlInterface("PaymentHandlerResponse", "https://w3c.github.io/web-based-payment-handler/#paymenthandlerresponse-dictionary")]
public sealed class PaymentHandlerResponse
{
    /// <summary>Gets the payment method used by the payment handler.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-methodname"/>
    [IdlMember("methodName", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-methodname")]
    public string? MethodName { get; init; }

    /// <summary>Gets the response of the payment method, an object serialized to JSON for the merchant.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-details"/>
    [IdlMember("details", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-details")]
    public object? Details { get; init; }

    /// <summary>Gets the name of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payername"/>
    [IdlMember("payerName", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payername")]
    public string? PayerName { get; init; }

    /// <summary>Gets the email address of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payeremail"/>
    [IdlMember("payerEmail", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payeremail")]
    public string? PayerEmail { get; init; }

    /// <summary>Gets the phone number of the payer.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payerphone"/>
    [IdlMember("payerPhone", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-payerphone")]
    public string? PayerPhone { get; init; }

    /// <summary>Gets the shipping address.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-shippingaddress"/>
    [IdlMember("shippingAddress", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-shippingaddress")]
    public AddressInit? ShippingAddress { get; init; }

    /// <summary>Gets the identifier of the selected shipping option.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-shippingoption"/>
    [IdlMember("shippingOption", "https://w3c.github.io/web-based-payment-handler/#dom-paymenthandlerresponse-shippingoption")]
    public string? ShippingOption { get; init; }
}

/// <summary>
/// The event fired at the service worker of a payment handler to handle a payment
/// (https://w3c.github.io/web-based-payment-handler/#the-paymentrequestevent). The library never fires it: an event created by a
/// script cannot be answered, nor open a window.
/// </summary>
/// <seealso href="https://w3c.github.io/web-based-payment-handler/#the-paymentrequestevent"/>
[IdlInterface("PaymentRequestEvent", "https://w3c.github.io/web-based-payment-handler/#the-paymentrequestevent")]
public sealed class PaymentRequestEvent : ExtendableEvent
{
    private bool _respondWithCalled;

    /// <summary>Creates an event.</summary>
    /// <param name="type">The type of the event.</param>
    /// <param name="eventInitDict">The initialization dictionary.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-constructor")]
    public PaymentRequestEvent(string type, PaymentRequestEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        TopOrigin = eventInitDict?.TopOrigin ?? "";
        PaymentRequestOrigin = eventInitDict?.PaymentRequestOrigin ?? "";
        PaymentRequestId = eventInitDict?.PaymentRequestId ?? "";
        MethodData = eventInitDict?.MethodData ?? [];
        Total = eventInitDict?.Total ?? new PaymentCurrencyAmount { Currency = "", Value = "" };
        Modifiers = eventInitDict?.Modifiers ?? [];
        PaymentOptions = eventInitDict?.PaymentOptions;
        ShippingOptions = eventInitDict?.ShippingOptions;
    }

    /// <summary>Gets the origin of the top-level document of the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-toporigin"/>
    [IdlMember("topOrigin", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-toporigin")]
    public string TopOrigin { get; }

    /// <summary>Gets the origin of the document that created the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentrequestorigin"/>
    [IdlMember("paymentRequestOrigin", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentrequestorigin")]
    public string PaymentRequestOrigin { get; }

    /// <summary>Gets the identifier of the payment request.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentrequestid"/>
    [IdlMember("paymentRequestId", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentrequestid")]
    public string PaymentRequestId { get; }

    /// <summary>Gets the payment methods of the request that the payment handler supports.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-methoddata"/>
    [IdlMember("methodData", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-methoddata")]
    public IReadOnlyList<PaymentMethodData> MethodData { get; }

    /// <summary>Gets the total of the payment (a <see cref="PaymentCurrencyAmount"/>).</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-total"/>
    [IdlMember("total", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-total")]
    public object Total { get; }

    /// <summary>Gets the modifiers of the payment methods that the payment handler supports.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-modifiers"/>
    [IdlMember("modifiers", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-modifiers")]
    public IReadOnlyList<PaymentDetailsModifier> Modifiers { get; }

    /// <summary>Gets the information that the payment handler collects (a <see cref="Dom.PaymentOptions"/>), or null.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentoptions"/>
    [IdlMember("paymentOptions", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-paymentoptions")]
    public object? PaymentOptions { get; }

    /// <summary>Gets the shipping options, or null.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-shippingoptions"/>
    [IdlMember("shippingOptions", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-shippingoptions")]
    public IReadOnlyList<PaymentShippingOption>? ShippingOptions { get; }

    /// <summary>
    /// Opens a window of the payment handler (https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-openwindow).
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>: the event does not handle a payment request
    /// of the user agent.
    /// </summary>
    /// <param name="url">The URL of the window.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-openwindow"/>
    [IdlMember("openWindow", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-openwindow")]
    public Task<WindowClient?> OpenWindow(string url) => Task.FromException<WindowClient?>(NotHandlingAPaymentRequest());

    /// <summary>
    /// Reports that the user changed the payment method (https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changepaymentmethod).
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>: the event does not handle a payment request
    /// of the user agent.
    /// </summary>
    /// <param name="methodName">The payment method.</param>
    /// <param name="methodDetails">The details of the payment method.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changepaymentmethod"/>
    [IdlMember("changePaymentMethod", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changepaymentmethod")]
    public Task<PaymentRequestDetailsUpdate?> ChangePaymentMethod(string methodName, object? methodDetails = null) => Task.FromException<PaymentRequestDetailsUpdate?>(NotHandlingAPaymentRequest());

    /// <summary>
    /// Reports that the user changed the shipping address (https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingaddress).
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>: the event does not handle a payment request
    /// of the user agent.
    /// </summary>
    /// <param name="shippingAddress">The shipping address.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingaddress"/>
    [IdlMember("changeShippingAddress", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingaddress")]
    public Task<PaymentRequestDetailsUpdate?> ChangeShippingAddress(AddressInit? shippingAddress = null) => Task.FromException<PaymentRequestDetailsUpdate?>(NotHandlingAPaymentRequest());

    /// <summary>
    /// Reports that the user changed the shipping option (https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingoption).
    /// The task fails with an <c>InvalidStateError</c> <see cref="DomException"/>: the event does not handle a payment request
    /// of the user agent.
    /// </summary>
    /// <param name="shippingOption">The identifier of the shipping option.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingoption"/>
    [IdlMember("changeShippingOption", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-changeshippingoption")]
    public Task<PaymentRequestDetailsUpdate?> ChangeShippingOption(string shippingOption) => Task.FromException<PaymentRequestDetailsUpdate?>(NotHandlingAPaymentRequest());

    /// <summary>
    /// Answers the payment request (https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-respondwith).
    /// Throws an <c>InvalidStateError</c> <see cref="DomException"/> when the event is not trusted, is not being dispatched or
    /// was already answered.
    /// </summary>
    /// <param name="handlerResponsePromise">The response of the payment handler.</param>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-respondwith"/>
    [IdlMember("respondWith", "https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-respondwith")]
    public void RespondWith(Task<PaymentHandlerResponse> handlerResponsePromise)
    {
        ArgumentNullException.ThrowIfNull(handlerResponsePromise);
        PaymentHandlerEvents.CheckCanRespond(this, ref _respondWithCalled);
        AddLifetimePromise(handlerResponsePromise);
    }

    private DomException NotHandlingAPaymentRequest()
        => DomException.InvalidState(IsTrusted ? "The event does not handle a payment request of the user agent" : "The event is not trusted");
}

/// <summary>The common steps of the events of the payment handlers.</summary>
internal static class PaymentHandlerEvents
{
    /// <summary>https://w3c.github.io/web-based-payment-handler/#dom-paymentrequestevent-respondwith, steps 1 to 3</summary>
    public static void CheckCanRespond(ExtendableEvent @event, ref bool respondWithCalled)
    {
        if (!@event.IsTrusted)
            throw DomException.InvalidState("The event is not trusted");

        if (!@event.DispatchFlag || respondWithCalled)
            throw DomException.InvalidState(respondWithCalled ? "respondWith() was already called" : "The event is not being dispatched");

        respondWithCalled = true;
    }
}

// The payment handler of a service worker registration (https://w3c.github.io/web-based-payment-handler/#extension-to-serviceworkerregistration)
public sealed partial class ServiceWorkerRegistration
{
    /// <summary>Gets the payment handler of the registration.</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerregistration-paymentmanager"/>
    [IdlMember("paymentManager", "https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerregistration-paymentmanager")]
    public PaymentManager PaymentManager => field ??= new PaymentManager();
}

// The events of the payment handlers (https://w3c.github.io/web-based-payment-handler/#extension-to-serviceworkerglobalscope)
public sealed partial class ServiceWorkerGlobalScope
{
    /// <summary>Gets or sets the event handler of the <c>canmakepayment</c> event (see <see cref="CanMakePaymentEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerglobalscope-oncanmakepayment"/>
    [IdlMember("oncanmakepayment", "https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerglobalscope-oncanmakepayment")]
    public Action<Event>? OnCanMakePayment
    {
        get => GetEventHandler("canmakepayment");
        set => SetEventHandler("canmakepayment", value);
    }

    /// <summary>Gets or sets the event handler of the <c>paymentrequest</c> event (see <see cref="PaymentRequestEvent"/>).</summary>
    /// <seealso href="https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerglobalscope-onpaymentrequest"/>
    [IdlMember("onpaymentrequest", "https://w3c.github.io/web-based-payment-handler/#dom-serviceworkerglobalscope-onpaymentrequest")]
    public Action<Event>? OnPaymentRequest
    {
        get => GetEventHandler("paymentrequest");
        set => SetEventHandler("paymentrequest", value);
    }
}
