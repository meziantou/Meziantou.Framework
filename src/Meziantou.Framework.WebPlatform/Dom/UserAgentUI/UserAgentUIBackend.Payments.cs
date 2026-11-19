namespace Meziantou.Framework.WebPlatform.Dom;

// The payment handlers and the payment sheet of the Payment Request API (https://w3c.github.io/payment-request/), and the
// ingestion of the payment method manifests (https://w3c.github.io/payment-method-manifest/)
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Returns whether the user agent has a payment handler that can make a payment with a payment method
    /// (https://w3c.github.io/payment-request/#dfn-steps-to-check-if-a-payment-can-be-made), for <c>PaymentRequest.canMakePayment()</c>
    /// and <c>PaymentRequest.show()</c>: the payment sheet only offers the supported methods, and <c>show()</c> fails with a
    /// <c>NotSupportedError</c> when no method is supported. The default implementation returns false: no payment method is
    /// supported.
    /// </summary>
    /// <param name="query">The payment method and its data.</param>
    /// <param name="cancellationToken">Canceled when the page no longer needs the answer.</param>
    /// <returns>Whether the payment method is supported.</returns>
    public virtual ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken) => new(false);

    /// <summary>
    /// Validates the data of a payment method when a <c>PaymentRequest</c> is constructed (https://w3c.github.io/payment-request/#dfn-steps-to-validate-payment-method-data),
    /// for the payment methods that define an additional data type: the method throws a <see cref="TypeErrorException"/> or a
    /// <see cref="RangeErrorException"/>, rethrown by the constructor, when the data is not valid. It is called synchronously,
    /// on the thread of the event loop of the window, for each payment method that has data. The default implementation
    /// accepts any data.
    /// </summary>
    /// <param name="methodIdentifier">The payment method identifier, as given by the page.</param>
    /// <param name="serializedData">The data of the payment method, serialized to JSON.</param>
    public virtual void ValidatePaymentMethodData(string methodIdentifier, string serializedData)
    {
    }

    /// <summary>
    /// Shows the payment sheet of a payment request (<c>PaymentRequest.show()</c>, https://w3c.github.io/payment-request/#show-method)
    /// and returns the response of the user once they accept the payment, or null when they dismiss the sheet (the promise
    /// of <c>show()</c> is rejected with an <c>AbortError</c>). While the sheet is shown, the backend reports the changes of
    /// the user with the methods of the prompt (<see cref="PaymentSheetPrompt.ChangeShippingAddressAsync"/>,
    /// <see cref="PaymentSheetPrompt.ChangeShippingOptionAsync"/>, <see cref="PaymentSheetPrompt.ChangePaymentMethodAsync"/>),
    /// which fire the events of the page and return the details updated by the page. The token is canceled when the page
    /// aborts the request (<c>abort()</c>), an update of the details fails, or the document is unloaded. The default
    /// implementation returns null.
    /// </summary>
    /// <param name="prompt">The payment request.</param>
    /// <param name="cancellationToken">Canceled when the sheet must be closed.</param>
    /// <returns>The response of the user, or null.</returns>
    public virtual ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => new((PaymentSheetResponse?)null);

    /// <summary>
    /// Shows the payment sheet again so that the user fixes the errors of an accepted payment (<c>PaymentResponse.retry()</c>,
    /// https://w3c.github.io/payment-request/#retry-method): <see cref="PaymentSheetPrompt.Errors"/> describes the errors.
    /// Returns the new response of the user (with the same payment method), or null when they dismiss the sheet (the promise
    /// of <c>retry()</c> is rejected with an <c>AbortError</c>). While the sheet is shown, the backend can report the changes
    /// of the payer details with <see cref="PaymentSheetPrompt.ChangePayerDetailsAsync"/>. The default implementation returns null.
    /// </summary>
    /// <param name="prompt">The payment request, whose <see cref="PaymentSheetPrompt.Response"/> is the response to fix.</param>
    /// <param name="cancellationToken">Canceled when the sheet must be closed.</param>
    /// <returns>The new response of the user, or null.</returns>
    public virtual ValueTask<PaymentSheetResponse?> RetryPaymentAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => new((PaymentSheetResponse?)null);

    /// <summary>
    /// Closes the payment sheet once the page processed an accepted payment (<c>PaymentResponse.complete()</c>,
    /// https://w3c.github.io/payment-request/#complete-method): the backend can show the outcome to the user. The promise of
    /// <c>complete()</c> is resolved when the task completes. The default implementation completes at once.
    /// </summary>
    /// <param name="prompt">The payment request, whose <see cref="PaymentSheetPrompt.Response"/> is the processed response.</param>
    /// <param name="result">The outcome of the payment given by the page.</param>
    /// <param name="serializedData">The data given by the page for the payment method, serialized to JSON (<c>null</c> when the page gave none).</param>
    /// <param name="cancellationToken">Canceled when the document is unloaded.</param>
    public virtual ValueTask CompletePaymentAsync(PaymentSheetPrompt prompt, PaymentComplete result, string serializedData, CancellationToken cancellationToken) => default;

    /// <summary>
    /// Ingests the payment method manifest of a URL-based payment method identifier (https://w3c.github.io/payment-method-manifest/#ingest-payment-method-manifests),
    /// when a page asks whether it can make a payment with it (<c>PaymentRequest.canMakePayment()</c>, <c>PaymentRequest.show()</c>).
    /// The backend calls <see cref="PaymentMethodManifestRequest.FetchAsync"/> to fetch the manifest and the web app
    /// manifests of its default payment applications, then installs the payment handlers in a way that is specific to the user
    /// agent, and remembers the origins that can provide payment handlers for the method. As ingesting a manifest tells the
    /// owner of the payment method that the user visits the page, the default implementation fetches nothing.
    /// </summary>
    /// <param name="request">The payment method identifier.</param>
    /// <param name="cancellationToken">Canceled when the document is unloaded.</param>
    public virtual ValueTask IngestPaymentMethodManifestAsync(PaymentMethodManifestRequest request, CancellationToken cancellationToken) => default;
}
