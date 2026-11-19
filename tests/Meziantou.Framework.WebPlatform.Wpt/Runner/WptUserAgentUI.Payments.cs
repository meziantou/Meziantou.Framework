using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The payment sheet of the Payment Request API and the transaction dialog of Secure Payment Confirmation. The user agent has
// a payment handler for basic-card (the method that the payment-request tests expect to be supported), whose additional data
// type is BasicCardRequest (https://web.archive.org/web/2021/https://w3c.github.io/payment-method-basic-card/#basiccardrequest-dictionary),
// and supports Secure Payment Confirmation. The payment sheet stays open until the page aborts the request (the tests do not
// interact with it), and the payment method manifests are ingested (the payment-method-manifest tests check the requests of
// the user agent). The transaction dialog is answered by the transaction automation mode
// (https://w3c.github.io/secure-payment-confirmation/#sctn-automation-set-spc-transaction-mode): with "none", it stays open
// until the page aborts the request.
internal sealed partial class WptUserAgentUI
{
    private const string BasicCard = "basic-card";
    private const string SecurePaymentConfirmation = "secure-payment-confirmation";

    /// <summary>The current transaction automation mode of Secure Payment Confirmation (null for <c>none</c>).</summary>
    private volatile object? _spcTransactionMode;

    public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken) => new(query.Method.Identifier is BasicCard or SecurePaymentConfirmation);

    public override void ValidatePaymentMethodData(string methodIdentifier, string serializedData)
    {
        if (methodIdentifier != BasicCard)
            return;

        // The conversion to a BasicCardRequest dictionary: supportedNetworks is a sequence<DOMString>
        var data = JsonNode.Parse(serializedData);
        if (data is JsonObject dictionary && dictionary.TryGetPropertyValue("supportedNetworks", out var networks) && networks?.GetValueKind() != JsonValueKind.Array)
            throw new TypeErrorException("BasicCardRequest.supportedNetworks is not a sequence");
    }

    public override ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => WaitUntilClosedAsync<PaymentSheetResponse?>(null, cancellationToken);

    public override ValueTask<PaymentSheetResponse?> RetryPaymentAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => WaitUntilClosedAsync<PaymentSheetResponse?>(null, cancellationToken);

    public override async ValueTask IngestPaymentMethodManifestAsync(PaymentMethodManifestRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _pendingManifestFetches);
        try
        {
            await request.FetchAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            Interlocked.Decrement(ref _pendingManifestFetches);
        }
    }

    /// <summary>The number of payment method manifests being fetched (their requests run on the thread pool).</summary>
    private int _pendingManifestFetches;

    /// <summary>
    /// Gets whether the user agent is fetching a payment method manifest: the runner does not advance the virtual clock meanwhile,
    /// so the timers of the test (e.g. the polling of the server logs) do not run ahead of the fetches.
    /// </summary>
    public bool IsBusy => Volatile.Read(ref _pendingManifestFetches) > 0;

    public override ValueTask<SecurePaymentConfirmationChoice> ShowSecurePaymentConfirmationAsync(SecurePaymentConfirmationPrompt prompt, CancellationToken cancellationToken) => _spcTransactionMode switch
    {
        SecurePaymentConfirmationChoice choice => new(choice),
        _ => WaitUntilClosedAsync(SecurePaymentConfirmationChoice.Decline, cancellationToken),
    };

    /// <summary>A prompt that the user does not answer: it is closed when the page aborts the request.</summary>
    private static async ValueTask<T> WaitUntilClosedAsync<T>(T dismissed, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return dismissed;
    }

    // https://w3c.github.io/secure-payment-confirmation/#sctn-automation-set-spc-transaction-mode
    private bool TryExecutePaymentCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        if (command != "set_spc_transaction_mode")
            return false;

        _spcTransactionMode = GetString(arguments.Count > 0 ? arguments[0] : null, "mode") switch
        {
            "none" => null,
            "autoAccept" => SecurePaymentConfirmationChoice.Accept,
            "autoReject" => SecurePaymentConfirmationChoice.Decline,
            "autoOptOut" => SecurePaymentConfirmationChoice.OptOut,
            "autoChooseToAuthAnotherWay" => SecurePaymentConfirmationChoice.AuthenticateAnotherWay,
            var mode => throw WptWebDriverException.InvalidArgument($"'{mode}' is not a transaction automation mode"),
        };
        return true;
    }
}
