namespace Meziantou.Framework.WebPlatform.Dom;

// The transaction dialog of Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/#sctn-transaction-confirmation-ux)
public abstract partial class UserAgentUIBackend
{
    /// <summary>
    /// Shows the transaction dialog of Secure Payment Confirmation (<c>PaymentRequest.show()</c> with the
    /// <c>secure-payment-confirmation</c> payment method, https://w3c.github.io/secure-payment-confirmation/#sctn-transaction-confirmation-ux)
    /// and returns the choice of the user. The dialog shows the payee, the total and the payment instrument of the prompt; when
    /// the user accepts the transaction, the user agent authenticates the payment with one of the credentials of the prompt
    /// (Web Authentication, on the authenticators of <see cref="WebAuthenticationOptions.Authenticators"/>). The token is
    /// canceled when the page aborts the request (<c>abort()</c>) or the document is unloaded. The default implementation
    /// returns <see cref="SecurePaymentConfirmationChoice.Decline"/>.
    /// </summary>
    /// <param name="prompt">The transaction to confirm.</param>
    /// <param name="cancellationToken">Canceled when the dialog must be closed.</param>
    /// <returns>The choice of the user.</returns>
    public virtual ValueTask<SecurePaymentConfirmationChoice> ShowSecurePaymentConfirmationAsync(SecurePaymentConfirmationPrompt prompt, CancellationToken cancellationToken) => new(SecurePaymentConfirmationChoice.Decline);
}

/// <summary>The choice of the user in the transaction dialog of Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/#sctn-transaction-confirmation-outcome).</summary>
public enum SecurePaymentConfirmationChoice
{
    /// <summary>The user does not want to proceed with the payment: the promise of <c>show()</c> is rejected with an <c>AbortError</c>.</summary>
    Decline,

    /// <summary>The user proceeds with the payment and authenticates with a credential: the promise of <c>show()</c> is resolved with a response whose details are the <c>PublicKeyCredential</c> of the authentication.</summary>
    Accept,

    /// <summary>The user proceeds with the payment but does not want to (or cannot) use a credential: the promise of <c>show()</c> is rejected with a <c>NotAllowedError</c>.</summary>
    AuthenticateAnotherWay,

    /// <summary>
    /// The user opts out of the process for the relying party: the promise of <c>show()</c> is rejected with an
    /// <c>OptOutError</c>. The choice is only possible when <see cref="SecurePaymentConfirmationPrompt.ShowOptOut"/> is set,
    /// otherwise it is a <see cref="Decline"/>.
    /// </summary>
    OptOut,
}

/// <summary>
/// The transaction shown by the transaction dialog of Secure Payment Confirmation
/// (<see cref="UserAgentUIBackend.ShowSecurePaymentConfirmationAsync"/>, https://w3c.github.io/secure-payment-confirmation/#sctn-information-presented).
/// </summary>
public sealed class SecurePaymentConfirmationPrompt : UserAgentPrompt
{
    internal SecurePaymentConfirmationPrompt(Window window, PaymentCurrencyAmount total, PaymentCredentialInstrument instrument, string? payeeName, string? payeeOrigin, IReadOnlyList<PaymentEntityLogo> paymentEntitiesLogos, string rpId, int credentialCount, bool showOptOut)
        : base(window)
    {
        Total = total;
        Instrument = instrument;
        PayeeName = payeeName;
        PayeeOrigin = payeeOrigin;
        PaymentEntitiesLogos = paymentEntitiesLogos;
        RpId = rpId;
        CredentialCount = credentialCount;
        ShowOptOut = showOptOut;
    }

    /// <summary>Gets the total of the transaction (its currency code is in upper case).</summary>
    public PaymentCurrencyAmount Total { get; }

    /// <summary>Gets the payment instrument; its icon is an empty string when it cannot be fetched (and does not have to be shown).</summary>
    public PaymentCredentialInstrument Instrument { get; }

    /// <summary>Gets the name of the payee, or null.</summary>
    public string? PayeeName { get; }

    /// <summary>Gets the origin of the payee (e.g. <c>https://merchant.example</c>), or null.</summary>
    public string? PayeeOrigin { get; }

    /// <summary>Gets the logos of the entities that facilitate the payment; the URL of a logo that cannot be fetched is an empty string.</summary>
    public IReadOnlyList<PaymentEntityLogo> PaymentEntitiesLogos { get; }

    /// <summary>Gets the relying party identifier of the credentials.</summary>
    public string RpId { get; }

    /// <summary>
    /// Gets the number of credentials of the request that are available on the authenticators of the user agent: when it is 0,
    /// the user can only authenticate another way (<see cref="SecurePaymentConfirmationChoice.AuthenticateAnotherWay"/>) or decline.
    /// </summary>
    public int CredentialCount { get; }

    /// <summary>Gets whether the dialog lets the user opt out of the process for the relying party (<see cref="SecurePaymentConfirmationChoice.OptOut"/>).</summary>
    public bool ShowOptOut { get; }
}
