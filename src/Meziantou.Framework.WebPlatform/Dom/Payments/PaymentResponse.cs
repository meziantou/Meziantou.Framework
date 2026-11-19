namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>
/// The response of the user who accepted a payment request (https://w3c.github.io/payment-request/#paymentresponse-interface):
/// the page processes the payment, then calls <see cref="Complete"/>, or <see cref="Retry"/> to let the user fix errors.
/// </summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse"/>
[IdlInterface("PaymentResponse", "https://w3c.github.io/payment-request/#dom-paymentresponse")]
public sealed class PaymentResponse : EventTarget
{
    private TaskCompletionSource? _retryPromise;
    private TaskCompletionSource? _completePromise;

    internal PaymentResponse(PaymentRequest request, string requestId)
    {
        Request = request;
        RequestId = requestId;
    }

    internal override GlobalScope? RelevantGlobal => Request.Window;

    /// <summary>Gets the identifier of the payment request.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-requestid"/>
    [IdlMember("requestId", "https://w3c.github.io/payment-request/#dom-paymentresponse-requestid")]
    public string RequestId { get; }

    /// <summary>Gets the identifier of the payment method that the user selected.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-methodname"/>
    [IdlMember("methodName", "https://w3c.github.io/payment-request/#dom-paymentresponse-methodname")]
    public string MethodName { get; private set; } = "";

    /// <summary>Gets the response of the payment handler (a JavaScript object whose shape is defined by the payment method).</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-details"/>
    [IdlMember("details", "https://w3c.github.io/payment-request/#dom-paymentresponse-details")]
    public object Details { get; private set; } = new();

    /// <summary>Gets the full shipping address chosen by the user, or null when the request does not request shipping.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-shippingaddress"/>
    [IdlMember("shippingAddress", "https://w3c.github.io/payment-request/#dom-paymentresponse-shippingaddress")]
    public ContactAddress? ShippingAddress { get; private set; }

    /// <summary>Gets the identifier of the selected shipping option, or null when the request does not request shipping.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-shippingoption"/>
    [IdlMember("shippingOption", "https://w3c.github.io/payment-request/#dom-paymentresponse-shippingoption")]
    public string? ShippingOption { get; private set; }

    /// <summary>Gets the name of the payer, or null when the request does not request it.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-payername"/>
    [IdlMember("payerName", "https://w3c.github.io/payment-request/#dom-paymentresponse-payername")]
    public string? PayerName { get; private set; }

    /// <summary>Gets the email address of the payer, or null when the request does not request it.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-payeremail"/>
    [IdlMember("payerEmail", "https://w3c.github.io/payment-request/#dom-paymentresponse-payeremail")]
    public string? PayerEmail { get; private set; }

    /// <summary>Gets the phone number of the payer, or null when the request does not request it.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-payerphone"/>
    [IdlMember("payerPhone", "https://w3c.github.io/payment-request/#dom-paymentresponse-payerphone")]
    public string? PayerPhone { get; private set; }

    /// <summary>Gets or sets the event handler of the <c>payerdetailchange</c> event.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-onpayerdetailchange"/>
    [IdlMember("onpayerdetailchange", "https://w3c.github.io/payment-request/#dom-paymentresponse-onpayerdetailchange")]
    public Action<Event>? OnPayerDetailChange
    {
        get => GetEventHandler("payerdetailchange");
        set => SetEventHandler("payerdetailchange", value);
    }

    /// <summary>https://w3c.github.io/payment-request/#dfn-request</summary>
    internal PaymentRequest Request { get; }

    /// <summary>https://w3c.github.io/payment-request/#dfn-complete</summary>
    internal bool IsComplete { get; set; }

    /// <summary>Whether <see cref="Retry"/> is pending (https://w3c.github.io/payment-request/#dfn-retrypromise is not null).</summary>
    internal bool IsRetrying => _retryPromise is not null;

    /// <summary>
    /// Tells the user agent that the payment is processed (https://w3c.github.io/payment-request/#complete-method): the payment
    /// sheet is closed (<see cref="UserAgentUIBackend.CompletePaymentAsync"/>). The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when the response is already complete or is being retried, and with an <c>AbortError</c>
    /// when the document is unloaded first.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-complete"/>
    [IdlMember("complete", "https://w3c.github.io/payment-request/#dom-paymentresponse-complete")]
    public Task Complete(PaymentComplete result = PaymentComplete.Unknown, PaymentCompleteDetails? details = null)
    {
        if (IsComplete)
            return Task.FromException(Request.CreateException("The payment response is already complete", "InvalidStateError"));

        if (_retryPromise is not null)
            return Task.FromException(Request.CreateException("The payment is being retried", "InvalidStateError"));

        string serializedData;
        try
        {
            serializedData = details?.Data is { } data ? PaymentRequest.SerializeJson(Request.Window, data) : "null";
        }
#pragma warning disable CA1031 // The exceptions of the serialization reject the promise
        catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
        {
            return Task.FromException(ex);
        }

        IsComplete = true;
        var promise = new TaskCompletionSource();
        _completePromise = promise;
        if (!Request.Window.Document.IsFullyActive)
        {
            promise.TrySetException(Request.CreateException("The document is not fully active", "AbortError"));
            return promise.Task;
        }

        Request.Complete(result, serializedData, promise);
        return promise.Task;
    }

    /// <summary>
    /// Asks the user to fix the errors of the response (https://w3c.github.io/payment-request/#retry-method): the payment sheet
    /// is shown again (<see cref="UserAgentUIBackend.RetryPaymentAsync"/>), and the task completes once the user accepts the
    /// payment again: the members of the response are updated. The task fails with an <c>InvalidStateError</c>
    /// <see cref="DomException"/> when the document is not fully active, the response is complete or is already being retried,
    /// with a <c>NotSupportedError</c> when the payment was not accepted in the payment sheet (Secure Payment Confirmation), and
    /// with an <c>AbortError</c> when the user dismisses the sheet.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentresponse-retry"/>
    [IdlMember("retry", "https://w3c.github.io/payment-request/#dom-paymentresponse-retry")]
    public Task Retry(PaymentValidationErrors? errorFields = null)
    {
        if (!Request.Window.Document.IsFullyActive)
            return Task.FromException(Request.CreateException("The document is not fully active", "InvalidStateError"));

        if (IsComplete)
            return Task.FromException(Request.CreateException("The payment response is complete", "InvalidStateError"));

        if (_retryPromise is not null)
            return Task.FromException(Request.CreateException("The payment is already being retried", "InvalidStateError"));

        // The payment handler of Secure Payment Confirmation has no payment sheet to show again: the request keeps its state, so
        // complete() can still be called
        if (Request.Prompt is null)
            return Task.FromException(Request.CreateException("The payment handler of the response cannot retry the payment", "NotSupportedError"));

        PaymentSheetErrors? errors = null;
        if (errorFields is not null)
        {
            string? paymentMethodErrors;
            try
            {
                paymentMethodErrors = errorFields.PaymentMethod is { } paymentMethod ? PaymentRequest.SerializeJson(Request.Window, paymentMethod) : null;
            }
#pragma warning disable CA1031 // The exceptions of the conversion reject the promise
            catch (Exception ex) when (ex is not OutOfMemoryException)
#pragma warning restore CA1031
            {
                return Task.FromException(ex);
            }

            errors = new PaymentSheetErrors { Error = errorFields.Error, Payer = errorFields.Payer, ShippingAddress = errorFields.ShippingAddress, PaymentMethod = paymentMethodErrors };
        }

        var retryPromise = new TaskCompletionSource();
        _retryPromise = retryPromise;
        Request.Retry(errors);
        return retryPromise.Task;
    }

    /// <summary>The user accepted the payment (https://w3c.github.io/payment-request/#user-accepts-the-payment-request-algorithm): the members are set.</summary>
    internal void Accept(string methodName, object? details, ContactAddress? shippingAddress, string? shippingOption, string? payerName, string? payerEmail, string? payerPhone)
    {
        MethodName = methodName;
        Details = details ?? new object();
        ShippingAddress = shippingAddress;
        ShippingOption = shippingOption;
        PayerName = payerName;
        PayerEmail = payerEmail;
        PayerPhone = payerPhone;
    }

    /// <summary>https://w3c.github.io/payment-request/#payer-detail-changed-algorithm: the changed details that the request requests.</summary>
    internal void ChangePayerDetails(string? payerName, string? payerEmail, string? payerPhone)
    {
        if (payerName is not null)
        {
            PayerName = payerName;
        }

        if (payerEmail is not null)
        {
            PayerEmail = payerEmail;
        }

        if (payerPhone is not null)
        {
            PayerPhone = payerPhone;
        }
    }

    internal void ResolveRetry()
    {
        var promise = _retryPromise;
        _retryPromise = null;
        promise?.TrySetResult();
    }

    internal void RejectRetry(Exception error)
    {
        var promise = _retryPromise;
        _retryPromise = null;
        promise?.TrySetException(error);
    }

    /// <summary>The document is unloaded while the payment sheet is shown: the pending promises are rejected.</summary>
    internal void OnDocumentUnloaded(Exception error)
    {
        IsComplete = true;
        RejectRetry(error);
        _completePromise?.TrySetException(error);
    }
}
