namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The initialization dictionary of a <see cref="PaymentRequestUpdateEvent"/> (https://w3c.github.io/payment-request/#paymentrequestupdateeventinit-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequestupdateeventinit"/>
[IdlInterface("PaymentRequestUpdateEventInit", "https://w3c.github.io/payment-request/#dom-paymentrequestupdateeventinit")]
public class PaymentRequestUpdateEventInit : EventInit
{
}

/// <summary>
/// The event fired at a <see cref="PaymentRequest"/> (<c>shippingaddresschange</c>, <c>shippingoptionchange</c>) or a
/// <see cref="PaymentResponse"/> (<c>payerdetailchange</c>) when the user changes something in the payment sheet
/// (https://w3c.github.io/payment-request/#paymentrequestupdateevent-interface): the page updates the details of the request
/// with <see cref="UpdateWith"/>.
/// </summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent"/>
[IdlInterface("PaymentRequestUpdateEvent", "https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent")]
public class PaymentRequestUpdateEvent : Event
{
    /// <summary>Creates an event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent-constructor")]
    public PaymentRequestUpdateEvent(string type, PaymentRequestUpdateEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
    }

    /// <summary>https://w3c.github.io/payment-request/#dfn-waitforupdate: an update of the details is in progress (or the event cannot be used anymore).</summary>
    internal bool WaitForUpdate { get; set; }

    /// <summary>
    /// Updates the details of the payment request with the value of a promise (https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent-updatewith):
    /// the payment sheet waits until the promise is settled. A rejected promise, or a value that is not valid, aborts the
    /// payment request. Throws an <c>InvalidStateError</c> <see cref="DomException"/> when the event is not trusted, is already
    /// used, or its request is not shown or is already being updated.
    /// </summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent-updatewith"/>
    [IdlMember("updateWith", "https://w3c.github.io/payment-request/#dom-paymentrequestupdateevent-updatewith")]
    public void UpdateWith(Task<PaymentDetailsUpdate> detailsPromise)
    {
        ArgumentNullException.ThrowIfNull(detailsPromise);
        if (!IsTrusted)
            throw DomException.InvalidState("The event is not trusted");

        if (WaitForUpdate)
            throw DomException.InvalidState("updateWith() was already called");

        var request = Target switch
        {
            PaymentResponse response => response.Request,
            PaymentRequest paymentRequest => paymentRequest,
            _ => null,
        };
        if (request is null || request.State != PaymentRequestState.Interactive)
            throw DomException.InvalidState("The payment request is not interactive");

        if (request.IsUpdating)
            throw DomException.InvalidState("The payment request is already being updated");

        StopPropagationFlag = true;
        StopImmediatePropagationFlag = true;
        WaitForUpdate = true;
        var pmi = this is PaymentMethodChangeEvent methodChange ? methodChange.MethodName : null;
        request.UpdateDetails(detailsPromise, pmi);
    }
}

/// <summary>The initialization dictionary of a <see cref="PaymentMethodChangeEvent"/> (https://w3c.github.io/payment-request/#paymentmethodchangeeventinit-dictionary).</summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit"/>
[IdlInterface("PaymentMethodChangeEventInit", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit")]
public sealed class PaymentMethodChangeEventInit : PaymentRequestUpdateEventInit
{
    /// <summary>Gets the payment method identifier of the payment handler.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit-methodname"/>
    [IdlMember("methodName", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit-methodname")]
    public string MethodName { get; init; } = "";

    /// <summary>Gets the data of the payment method (a JavaScript object), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit-methoddetails"/>
    [IdlMember("methodDetails", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeeventinit-methoddetails")]
    public object? MethodDetails { get; init; }
}

/// <summary>
/// The event fired at a <see cref="PaymentRequest"/> when the user changes the payment method in the payment handler
/// (https://w3c.github.io/payment-request/#paymentmethodchangeevent-interface), e.g. a card whose billing address changes the taxes.
/// </summary>
/// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent"/>
[IdlInterface("PaymentMethodChangeEvent", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent")]
public sealed class PaymentMethodChangeEvent : PaymentRequestUpdateEvent
{
    /// <summary>Creates an event with the given type and initialization dictionary.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-constructor"/>
    [IdlMember("constructor", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-constructor")]
    public PaymentMethodChangeEvent(string type, PaymentMethodChangeEventInit? eventInitDict = null)
        : base(type, eventInitDict)
    {
        MethodName = eventInitDict?.MethodName ?? "";
        MethodDetails = eventInitDict?.MethodDetails;
    }

    /// <summary>Gets the payment method identifier of the payment handler.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-methodname"/>
    [IdlMember("methodName", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-methodname")]
    public string MethodName { get; }

    /// <summary>Gets the data of the payment method (a JavaScript object), or null.</summary>
    /// <seealso href="https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-methoddetails"/>
    [IdlMember("methodDetails", "https://w3c.github.io/payment-request/#dom-paymentmethodchangeevent-methoddetails")]
    public object? MethodDetails { get; }
}
