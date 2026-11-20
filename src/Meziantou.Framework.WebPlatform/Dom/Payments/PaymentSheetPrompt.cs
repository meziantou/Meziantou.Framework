namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// A payment method of a payment request, given to <see cref="UserAgentUIBackend.HasPaymentHandlerAsync"/>: the backend
/// answers whether the user agent has a payment handler that can make a payment with it.
/// </summary>
public sealed class PaymentHandlerQuery : UserAgentPrompt
{
    internal PaymentHandlerQuery(Window window, PaymentSheetMethod method)
        : base(window)
    {
        Method = method;
    }

    /// <summary>Gets the payment method identifier and its data.</summary>
    public PaymentSheetMethod Method { get; }
}

/// <summary>A payment method of a payment request: its identifier and its data (https://w3c.github.io/payment-request/#dfn-serializedmethoddata).</summary>
/// <param name="Identifier">The payment method identifier, as given by the page (e.g. <c>secure-payment-confirmation</c> or <c>https://example.com/pay</c>).</param>
/// <param name="Data">The data of the payment method serialized to JSON, or null when the page gave none.</param>
public sealed record PaymentSheetMethod(string Identifier, string? Data);

/// <summary>
/// A modifier of the details of a payment request (https://w3c.github.io/payment-request/#dom-paymentdetailsmodifier): the
/// total and the line items that apply when the user selects a payment method.
/// </summary>
/// <param name="SupportedMethods">The payment method identifier to which the modifier applies.</param>
/// <param name="Total">The total that replaces the total of the details, or null.</param>
/// <param name="AdditionalDisplayItems">The line items appended to the line items of the details.</param>
/// <param name="Data">The data of the modifier serialized to JSON, or null.</param>
public sealed record PaymentSheetModifier(string SupportedMethods, PaymentItem? Total, IReadOnlyList<PaymentItem> AdditionalDisplayItems, string? Data);

/// <summary>
/// The details of a payment request shown by the payment sheet (https://w3c.github.io/payment-request/#dfn-details): the
/// details given to the constructor, updated by the page. The amounts are valid, and their currency codes are in upper case.
/// </summary>
public sealed class PaymentSheetDetails
{
    internal PaymentSheetDetails(string id, PaymentItem total, IReadOnlyList<PaymentItem> displayItems, IReadOnlyList<PaymentShippingOption> shippingOptions, IReadOnlyList<PaymentSheetModifier> modifiers)
    {
        Id = id;
        Total = total;
        DisplayItems = displayItems;
        ShippingOptions = shippingOptions;
        Modifiers = modifiers;
    }

    /// <summary>Gets the identifier of the payment request.</summary>
    public string Id { get; }

    /// <summary>Gets the total amount of the payment.</summary>
    public PaymentItem Total { get; }

    /// <summary>Gets the line items of the payment.</summary>
    public IReadOnlyList<PaymentItem> DisplayItems { get; }

    /// <summary>Gets the shipping options that the user can choose from (empty when the request does not request shipping).</summary>
    public IReadOnlyList<PaymentShippingOption> ShippingOptions { get; }

    /// <summary>Gets the modifiers of the details for particular payment methods (the last applicable modifier wins).</summary>
    public IReadOnlyList<PaymentSheetModifier> Modifiers { get; }

    internal PaymentSheetDetails With(PaymentItem? total = null, IReadOnlyList<PaymentItem>? displayItems = null, IReadOnlyList<PaymentShippingOption>? shippingOptions = null, IReadOnlyList<PaymentSheetModifier>? modifiers = null)
        => new(Id, total ?? Total, displayItems ?? DisplayItems, shippingOptions ?? ShippingOptions, modifiers ?? Modifiers);
}

/// <summary>
/// The errors that the payment sheet shows to the user: the errors of the last update of the details by the page
/// (https://w3c.github.io/payment-request/#dom-paymentdetailsupdate), or the errors given to <c>PaymentResponse.retry()</c>
/// (https://w3c.github.io/payment-request/#dom-paymentvalidationerrors).
/// </summary>
public sealed class PaymentSheetErrors
{
    /// <summary>Gets a general description of the error (e.g. why no shipping option is available for the address), or null.</summary>
    public string? Error { get; init; }

    /// <summary>Gets the errors of the shipping address, or null.</summary>
    public AddressErrors? ShippingAddress { get; init; }

    /// <summary>Gets the errors of the payer details, or null.</summary>
    public PayerErrors? Payer { get; init; }

    /// <summary>Gets the errors specific to the payment method, serialized to JSON, or null.</summary>
    public string? PaymentMethod { get; init; }
}

/// <summary>
/// The response of the user who accepts a payment in the payment sheet (https://w3c.github.io/payment-request/#user-accepts-the-payment-request-algorithm),
/// returned by <see cref="UserAgentUIBackend.ShowPaymentSheetAsync"/> and <see cref="UserAgentUIBackend.RetryPaymentAsync"/>:
/// it becomes the <see cref="PaymentResponse"/> of the page.
/// </summary>
public sealed class PaymentSheetResponse
{
    /// <summary>Gets the identifier of the payment method that the user selected (one of <see cref="PaymentSheetPrompt.Methods"/>).</summary>
    public required string MethodName { get; init; }

    /// <summary>
    /// Gets the response of the payment handler, serialized to JSON (https://w3c.github.io/payment-request/#dom-paymentresponse-details):
    /// a JSON object whose shape is defined by the payment method. A text that is not JSON is an internal error of the payment
    /// handler: the promise of the page is rejected with an <c>OperationError</c>.
    /// </summary>
    public string Details { get; init; } = "{}";

    /// <summary>
    /// Gets the full shipping address chosen by the user when the request requests shipping, or null to use the last address
    /// given to <see cref="PaymentSheetPrompt.ChangeShippingAddressAsync"/>. A request that requests shipping is accepted
    /// only with a shipping address and a shipping option (<see cref="PaymentSheetPrompt.ShippingOption"/>): otherwise the user
    /// aborts the payment request.
    /// </summary>
    public ContactAddress? ShippingAddress { get; init; }

    /// <summary>Gets the name of the payer (used when the request requests it), or null.</summary>
    public string? PayerName { get; init; }

    /// <summary>Gets the email address of the payer (used when the request requests it), or null.</summary>
    public string? PayerEmail { get; init; }

    /// <summary>Gets the phone number of the payer, formatted as E.164 (used when the request requests it), or null.</summary>
    public string? PayerPhone { get; init; }
}

/// <summary>
/// A payment request shown by the payment sheet, given to <see cref="UserAgentUIBackend.ShowPaymentSheetAsync"/> (and to
/// <see cref="UserAgentUIBackend.RetryPaymentAsync"/> and <see cref="UserAgentUIBackend.CompletePaymentAsync"/> for the
/// same request): the supported payment methods, the details and the options of the request. The backend reports the
/// interactions of the user with the change methods, which can be called from any thread while the sheet is shown: they
/// fire the events of the page, and complete with the details updated by the page (<c>updateWith()</c>), or at once when the
/// page does not update them. Only one change can be pending at a time.
/// </summary>
/// <remarks>
/// A payment method whose payment handler shows its own user interface instead of the sheet (e.g. the transaction dialog
/// of Secure Payment Confirmation) uses the same request, and the same response (<see cref="PaymentSheetResponse"/>).
/// </remarks>
public sealed class PaymentSheetPrompt : UserAgentPrompt
{
    private readonly PaymentRequest _request;
    private volatile PaymentSheetDetails _details;
    private volatile PaymentSheetErrors? _errors;
    private volatile ContactAddress? _shippingAddress;
    private volatile string? _shippingOption;
    private volatile PaymentSheetResponse? _response;
    private volatile bool _isUpdating;

    internal PaymentSheetPrompt(Window window, PaymentRequest request, IReadOnlyList<PaymentSheetMethod> methods, PaymentOptions options, PaymentSheetDetails details, string? shippingOption)
        : base(window)
    {
        _request = request;
        Methods = methods;
        Options = options;
        _details = details;
        _shippingOption = shippingOption;
    }

    /// <summary>Gets the payment methods of the request that the user agent supports (<see cref="UserAgentUIBackend.HasPaymentHandlerAsync"/>), in the order of the request.</summary>
    public IReadOnlyList<PaymentSheetMethod> Methods { get; }

    /// <summary>Gets the information that the request collects (payer details, shipping address).</summary>
    public PaymentOptions Options { get; }

    /// <summary>Gets the current details of the request (updated by the page).</summary>
    public PaymentSheetDetails Details
    {
        get => _details;
        internal set => _details = value;
    }

    /// <summary>Gets the errors to show to the user (the last update of the page, or the errors of <c>retry()</c>), or null.</summary>
    public PaymentSheetErrors? Errors
    {
        get => _errors;
        internal set => _errors = value;
    }

    /// <summary>Gets the last full shipping address given to <see cref="ChangeShippingAddressAsync"/>, or null.</summary>
    public ContactAddress? ShippingAddress
    {
        get => _shippingAddress;
        internal set => _shippingAddress = value;
    }

    /// <summary>
    /// Gets the identifier of the selected shipping option, or null: the option selected by the page (<see cref="PaymentShippingOption.Selected"/>),
    /// or the last option given to <see cref="ChangeShippingOptionAsync"/>.
    /// </summary>
    public string? ShippingOption
    {
        get => _shippingOption;
        internal set => _shippingOption = value;
    }

    /// <summary>Gets whether the page is updating the details: the user cannot accept the payment until the update completes (see <see cref="WaitForUpdateAsync"/>).</summary>
    public bool IsUpdating
    {
        get => _isUpdating;
        internal set => _isUpdating = value;
    }

    /// <summary>Gets the response accepted by the user, or null before the payment is accepted (see <see cref="UserAgentUIBackend.RetryPaymentAsync"/>).</summary>
    public PaymentSheetResponse? Response
    {
        get => _response;
        internal set => _response = value;
    }

    /// <summary>
    /// Waits until the details are not being updated (e.g. the promise given to <c>show()</c> is settled), and returns them.
    /// The task is canceled when the sheet is closed.
    /// </summary>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    public Task<PaymentSheetDetails> WaitForUpdateAsync(CancellationToken cancellationToken = default) => _request.WaitForUpdateAsync(cancellationToken);

    /// <summary>
    /// Reports that the user chose a shipping address (https://w3c.github.io/payment-request/#shipping-address-changed-algorithm):
    /// the page receives the address without the members that identify the recipient (organization, phone number, recipient,
    /// address line) in a <c>shippingaddresschange</c> event, and can update the details (e.g. the shipping options for the
    /// address). Returns the details, once updated. The task fails with an <see cref="InvalidOperationException"/> when the
    /// request does not request shipping, is not interactive, or another change is pending, and is canceled when the sheet
    /// is closed.
    /// </summary>
    /// <param name="address">The full address chosen by the user.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    public Task<PaymentSheetDetails> ChangeShippingAddressAsync(ContactAddress address, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        return _request.ChangeShippingAddressAsync(address.Copy(), cancellationToken);
    }

    /// <summary>
    /// Reports that the user chose a shipping option (https://w3c.github.io/payment-request/#shipping-option-changed-algorithm):
    /// the page receives a <c>shippingoptionchange</c> event, and can update the details (e.g. the total). Returns the details,
    /// once updated. The task fails with an <see cref="InvalidOperationException"/> when the request does not request
    /// shipping, the option is not one of the shipping options of the details, the request is not interactive, or another
    /// change is pending, and is canceled when the sheet is closed.
    /// </summary>
    /// <param name="shippingOptionId">The identifier of the option.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    public Task<PaymentSheetDetails> ChangeShippingOptionAsync(string shippingOptionId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(shippingOptionId);
        return _request.ChangeShippingOptionAsync(shippingOptionId, cancellationToken);
    }

    /// <summary>
    /// Reports that the user changed the payment method in the payment handler (https://w3c.github.io/payment-request/#payment-method-changed-algorithm):
    /// the page receives a <c>paymentmethodchange</c> event (e.g. with the redacted billing address of the card, to compute the
    /// taxes), and can update the details. Returns the details, once updated. The task fails with an
    /// <see cref="InvalidOperationException"/> when the request is not interactive or another change is pending, and is
    /// canceled when the sheet is closed.
    /// </summary>
    /// <param name="methodName">The payment method identifier of the payment handler.</param>
    /// <param name="methodDetails">The data of the payment method serialized to JSON (an object), or null.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    public Task<PaymentSheetDetails> ChangePaymentMethodAsync(string methodName, string? methodDetails, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(methodName);
        return _request.ChangePaymentMethodAsync(methodName, methodDetails, cancellationToken);
    }

    /// <summary>
    /// Reports that the user changed the payer details while retrying a payment (https://w3c.github.io/payment-request/#payer-detail-changed-algorithm):
    /// the requested details of the <c>PaymentResponse</c> are updated and the page receives a <c>payerdetailchange</c> event.
    /// A null argument is a detail that did not change. Returns the details, once updated. The task fails with an
    /// <see cref="InvalidOperationException"/> when the payment was not accepted, is not being retried, or another change is
    /// pending, and is canceled when the sheet is closed.
    /// </summary>
    /// <param name="payerName">The new name of the payer, or null.</param>
    /// <param name="payerEmail">The new email address of the payer, or null.</param>
    /// <param name="payerPhone">The new phone number of the payer, or null.</param>
    /// <param name="cancellationToken">A token to stop waiting.</param>
    public Task<PaymentSheetDetails> ChangePayerDetailsAsync(string? payerName, string? payerEmail, string? payerPhone, CancellationToken cancellationToken = default)
        => _request.ChangePayerDetailsAsync(payerName, payerEmail, payerPhone, cancellationToken);
}
