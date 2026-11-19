using System.Text.Json;
using System.Text.Json.Nodes;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Wpt.Runner;

// The payment sheet of the Payment Request API and the transaction dialog of Secure Payment Confirmation. The user agent has
// a payment handler for basic-card (the method that the payment-request tests expect to be supported), whose additional data
// type is BasicCardRequest (https://web.archive.org/web/2021/https://w3c.github.io/payment-method-basic-card/#basiccardrequest-dictionary).
// The payment sheet stays open until the page aborts the request (the tests do not interact with it), and the payment
// method manifests are ingested (the payment-method-manifest tests check the requests of the user agent).
internal sealed partial class WptUserAgentUI
{
    private const string BasicCard = "basic-card";

    public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken) => new(query.Method.Identifier == BasicCard);

    public override void ValidatePaymentMethodData(string methodIdentifier, string serializedData)
    {
        if (methodIdentifier != BasicCard)
            return;

        // The conversion to a BasicCardRequest dictionary: supportedNetworks is a sequence<DOMString>
        var data = JsonNode.Parse(serializedData);
        if (data is JsonObject dictionary && dictionary.TryGetPropertyValue("supportedNetworks", out var networks) && networks?.GetValueKind() != JsonValueKind.Array)
            throw new TypeErrorException("BasicCardRequest.supportedNetworks is not a sequence");
    }

    public override ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => WaitUntilClosedAsync(cancellationToken);

    public override ValueTask<PaymentSheetResponse?> RetryPaymentAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken) => WaitUntilClosedAsync(cancellationToken);

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

    /// <summary>A payment sheet that the user does not answer: it is closed when the page aborts the request.</summary>
    private static async ValueTask<PaymentSheetResponse?> WaitUntilClosedAsync(CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
        }

        return null;
    }

    // The payment tests use no command (set_spc_transaction_mode belongs to Secure Payment Confirmation)
    private static bool TryExecutePaymentCommand(string command, JsonArray arguments, out string? result)
    {
        result = null;
        return false;
    }
}
