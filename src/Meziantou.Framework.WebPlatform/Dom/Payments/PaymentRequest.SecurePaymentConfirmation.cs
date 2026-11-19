using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Scripting;
using Meziantou.Framework.WebPlatform.Urls;
using Availability = Meziantou.Framework.WebPlatform.Dom.SecurePaymentConfirmationAvailability;

namespace Meziantou.Framework.WebPlatform.Dom;

// Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/): the secure-payment-confirmation payment
// method, whose payment handler is implemented by the library. show() shows the transaction dialog of the backend
// (UserAgentUIBackend.ShowSecurePaymentConfirmationAsync) instead of the payment sheet, then authenticates the payment with
// Web Authentication (the payment extension): the details of the response are the PublicKeyCredential.
public sealed partial class PaymentRequest
{
    /// <summary>The payment method identifier of Secure Payment Confirmation.</summary>
    internal const string SecurePaymentConfirmationMethod = "secure-payment-confirmation";

    /// <summary>The validated data of the secure-payment-confirmation payment method, or null when the request does not use it.</summary>
    private readonly SecurePaymentConfirmationRequest? _securePaymentConfirmation;

    /// <summary>The data after the steps to check if a payment can be made (the icons that cannot be fetched and the unavailable credentials are removed).</summary>
    private SecurePaymentConfirmationRequest? _checkedSecurePaymentConfirmation;

    /// <summary>
    /// Returns whether Secure Payment Confirmation is available in the current document
    /// (https://w3c.github.io/secure-payment-confirmation/#sctn-secure-payment-confirmation-available-api): it is not enabled when
    /// the backend has no payment handler for <c>secure-payment-confirmation</c> (<see cref="UserAgentUIBackend.HasPaymentHandlerAsync"/>),
    /// and it needs the <c>payment</c> feature and a user-verifying platform authenticator. The availability is unknown in a
    /// document that is not fully active.
    /// </summary>
    /// <seealso href="https://w3c.github.io/secure-payment-confirmation/#dom-paymentrequest-securepaymentconfirmationavailability"/>
    [IdlMember("securePaymentConfirmationAvailability", "https://w3c.github.io/secure-payment-confirmation/#dom-paymentrequest-securepaymentconfirmationavailability")]
    public static Task<Availability> SecurePaymentConfirmationAvailability()
    {
        // The function of a document that is no longer fully active (e.g. saved from a removed iframe)
        if (GlobalScope.Current is not Window window || !window.Document.IsFullyActive)
            return Task.FromResult(Availability.UnavailableUnknownReason);

        var result = new TaskCompletionSource<Availability>();
        var query = new PaymentHandlerQuery(window, new PaymentSheetMethod(SecurePaymentConfirmationMethod, Data: null));
        UserAgentUI.Show(
            window,
            (backend, cancellationToken) => backend.HasPaymentHandlerAsync(query, cancellationToken),
            dismissed: false,
            supported => result.TrySetResult(supported switch
            {
                false => Availability.UnavailableFeatureNotEnabled,
                _ when !PermissionsPolicyAlgorithms.IsAllowedToUse(window.Document, PolicyControlledFeature.Payment) => Availability.UnavailableNoPermissionPolicy,
                _ when !WebAuthenticationClient.HasUserVerifyingPlatformAuthenticator(window) => Availability.UnavailableNoUserVerifyingPlatformAuthenticator,
                _ => Availability.Available,
            }));
        return result.Task;
    }

    /// <summary>
    /// Returns the capabilities of Secure Payment Confirmation (https://w3c.github.io/secure-payment-confirmation/#sctn-secure-payment-confirmation-capabilities),
    /// sorted by key: <c>browserBoundKeyHardware</c> is false (the browser bound keys are software keys).
    /// </summary>
    /// <seealso href="https://w3c.github.io/secure-payment-confirmation/#dom-paymentrequest-getsecurepaymentconfirmationcapabilities"/>
    [IdlMember("getSecurePaymentConfirmationCapabilities", "https://w3c.github.io/secure-payment-confirmation/#dom-paymentrequest-getsecurepaymentconfirmationcapabilities")]
    public static Task<IReadOnlyList<KeyValuePair<string, bool>>> GetSecurePaymentConfirmationCapabilities()
        => Task.FromResult<IReadOnlyList<KeyValuePair<string, bool>>>([new("browserBoundKeyHardware", false)]);

    /// <summary>https://w3c.github.io/secure-payment-confirmation/#sctn-modify-payment-request-constructor: Secure Payment Confirmation is the only payment method of its request.</summary>
    private static void CheckSecurePaymentConfirmationIsAlone(HashSet<string> seenPmis)
    {
        if (seenPmis.Count > 1 && seenPmis.Contains(SecurePaymentConfirmationMethod))
            throw new RangeErrorException("The secure-payment-confirmation payment method cannot be used with another payment method");
    }

    /// <summary>
    /// Converts the data of the payment method to a <see cref="SecurePaymentConfirmationRequest"/>, then runs the steps to
    /// validate payment method data (https://w3c.github.io/secure-payment-confirmation/#sctn-steps-to-validate-payment-method-data).
    /// </summary>
    private static SecurePaymentConfirmationRequest ValidateSecurePaymentConfirmationData(Window window, object? data)
    {
        var operations = window.ScriptEngine?.GetScriptValueOperations(window) ?? DotNetScriptValueOperations.Instance;
        var request = operations.ToSecurePaymentConfirmationRequest(data);
        if (request.CredentialIds.Count == 0)
            throw new RangeErrorException("SecurePaymentConfirmationRequest.credentialIds is empty");

        foreach (var id in request.CredentialIds)
        {
            if (id.IsEmpty)
                throw new RangeErrorException("A credential ID of SecurePaymentConfirmationRequest.credentialIds is empty");
        }

        if (request.Challenge.IsEmpty)
            throw new TypeErrorException("SecurePaymentConfirmationRequest.challenge is empty");

        var instrument = request.Instrument;
        if (instrument.DisplayName.Length == 0)
            throw new TypeErrorException("SecurePaymentConfirmationRequest.instrument.displayName is empty");

        if (instrument.Icon.Length == 0)
            throw new TypeErrorException("SecurePaymentConfirmationRequest.instrument.icon is empty");

        if (UrlParser.Parse(instrument.Icon, baseUrl: null) is null)
            throw new TypeErrorException("SecurePaymentConfirmationRequest.instrument.icon is not a valid URL");

        if (instrument.Details is { Length: 0 })
            throw new TypeErrorException("SecurePaymentConfirmationRequest.instrument.details is empty");

        if (HostParser.Parse(request.RpId) is not { Kind: UrlHostKind.Domain })
            throw new TypeErrorException($"SecurePaymentConfirmationRequest.rpId '{request.RpId}' is not a valid domain");

        if (request.PayeeName is null && request.PayeeOrigin is null)
            throw new TypeErrorException("SecurePaymentConfirmationRequest requires payeeName or payeeOrigin");

        if (request.PayeeName is { Length: 0 } || request.PayeeOrigin is { Length: 0 })
            throw new TypeErrorException("SecurePaymentConfirmationRequest.payeeName and payeeOrigin cannot be empty");

        if (request.PayeeOrigin is { } payeeOrigin && UrlParser.Parse(payeeOrigin, baseUrl: null) is not { Scheme: "https" })
            throw new TypeErrorException("SecurePaymentConfirmationRequest.payeeOrigin is not an https: URL");

        foreach (var logo in request.PaymentEntitiesLogos ?? [])
        {
            if (logo.Url.Length == 0 || UrlParser.Parse(logo.Url, baseUrl: null) is null)
                throw new TypeErrorException("The url of a PaymentEntityLogo is not a valid URL");

            if (logo.Label.Length == 0)
                throw new TypeErrorException("The label of a PaymentEntityLogo is empty");
        }

        // The extensions are only allowed for the relying party (https://w3c.github.io/secure-payment-confirmation/#sctn-security-webauthn-extensions)
        if (request.Extensions is { } extensions && !IsEmpty(extensions) && !IsRelyingPartyOf(window, request.RpId))
            throw new TypeErrorException("SecurePaymentConfirmationRequest.extensions can only be used by the relying party");

        if (request.Locale is { Count: > 0 } locale)
        {
            var dialogLanguageTag = window.Options.EffectiveLanguages[0];
            var matched = false;
            foreach (var tag in locale)
            {
                if (tag.Length == 0 || !Notification.IsValidLanguageTag(tag))
                    throw new TypeErrorException($"'{tag}' is not a well-formed language tag");

                if (IsLanguageMatch(tag, dialogLanguageTag))
                {
                    matched = true;
                    break;
                }
            }

            if (!matched)
                throw new DomException("The language of the transaction dialog matches none of the locales", "NotSupportedError") { RealmWindow = window };
        }

        return request;

        static bool IsEmpty(AuthenticationExtensionsClientInputs extensions)
            => extensions is { Appid: null, AppidExclude: null, CredProps: null, Prf: null, LargeBlob: null, RemoteClientDataJson: null, Payment: null };

        // A tag and the language of the dialog match when both have several subtags and are equal, or when one of them only
        // has a primary language subtag that is the primary language subtag of the other one
        static bool IsLanguageMatch(string tag, string dialogLanguageTag)
        {
            var tagIsPrimary = !tag.Contains('-', StringComparison.Ordinal);
            var dialogIsPrimary = !dialogLanguageTag.Contains('-', StringComparison.Ordinal);
            if (!tagIsPrimary && !dialogIsPrimary)
                return Infra.InfraAscii.EqualsIgnoreAsciiCase(tag, dialogLanguageTag);

            return Infra.InfraAscii.EqualsIgnoreAsciiCase(GetPrimaryLanguage(tag), GetPrimaryLanguage(dialogLanguageTag));
        }

        static string GetPrimaryLanguage(string tag) => tag.IndexOf('-', StringComparison.Ordinal) is var index and >= 0 ? tag[..index] : tag;
    }

    /// <summary>Whether the RP ID is the domain of the origin of the window (otherwise the request is made by a third party, e.g. the merchant).</summary>
    private static bool IsRelyingPartyOf(Window window, string rpId)
        => window.Document.Origin.EffectiveDomain is { Kind: UrlHostKind.Domain } domain && domain.Serialized == rpId;

    /// <summary>
    /// The steps to check if a payment can be made with the secure-payment-confirmation payment method of the request, once
    /// the backend supports it (null when the request does not use it). The values of the window are read on its event loop.
    /// </summary>
    private Func<CancellationToken, Task<bool>>? CreateSecurePaymentConfirmationCheck()
    {
        if (!_serializedMethodData.Exists(method => method.Identifier == SecurePaymentConfirmationMethod))
            return null;

        if (_securePaymentConfirmation is not { } data)
            return _ => Task.FromResult(false);

        var authenticators = _window.Options.WebAuthentication?.Authenticators;
        var thirdParty = !IsRelyingPartyOf(_window, data.RpId);
        return cancellationToken => CheckSecurePaymentConfirmationAsync(data, authenticators, thirdParty, cancellationToken);
    }

    /// <summary>https://w3c.github.io/secure-payment-confirmation/#sctn-steps-to-check-if-a-payment-can-be-made</summary>
    private async Task<bool> CheckSecurePaymentConfirmationAsync(SecurePaymentConfirmationRequest data, VirtualAuthenticatorEnvironment? authenticators, bool thirdParty, CancellationToken cancellationToken)
    {
        var payeeOrigin = data.PayeeOrigin is { } payee && UrlParser.Parse(payee, baseUrl: null) is { } payeeUrl ? OriginRecord.FromUrl(payeeUrl).Serialize() : null;

        // The icon and the logos are fetched whether or not a credential matches, so that the page cannot probe the credentials
        var instrument = data.Instrument;
        if (!await FetchImageAsync(instrument.Icon, cancellationToken).ConfigureAwait(false))
        {
            if (instrument.IconMustBeShown)
                return false;

            instrument = new PaymentCredentialInstrument { DisplayName = instrument.DisplayName, Icon = "", IconMustBeShown = false, Details = instrument.Details };
        }

        List<PaymentEntityLogo>? logos = null;
        if (data.PaymentEntitiesLogos is { } paymentEntitiesLogos)
        {
            logos = new List<PaymentEntityLogo>(paymentEntitiesLogos.Count);
            foreach (var logo in paymentEntitiesLogos)
            {
                var fetched = await FetchImageAsync(logo.Url, cancellationToken).ConfigureAwait(false);
                logos.Add(fetched ? logo : new PaymentEntityLogo { Url = "", Label = logo.Label });
            }
        }

        // The credentials available on the authenticators (silently); a third party can only use the credentials created
        // with the payment extension
        var credentialIds = new List<ReadOnlyMemory<byte>>(data.CredentialIds.Count);
        foreach (var id in data.CredentialIds)
        {
            var available = authenticators?.Authenticators.Any(authenticator => authenticator.Lookup(id.Span, data.RpId) is { } credential && (!thirdParty || credential.IsPaymentCredential)) is true;
            if (available)
            {
                credentialIds.Add(id);
            }
        }

        _checkedSecurePaymentConfirmation = new SecurePaymentConfirmationRequest
        {
            Challenge = data.Challenge,
            RpId = data.RpId,
            CredentialIds = credentialIds,
            Instrument = instrument,
            Timeout = data.Timeout,
            PayeeName = data.PayeeName,
            PayeeOrigin = payeeOrigin,
            PaymentEntitiesLogos = logos,
            Extensions = data.Extensions,
            BrowserBoundPubKeyCredParams = data.BrowserBoundPubKeyCredParams,
            Locale = data.Locale,
            ShowOptOut = data.ShowOptOut,
        };
        return true;
    }

    /// <summary>
    /// Fetches the image resource of an icon or a logo, and decodes it with the rendering backend of the document (an image
    /// is not decoded without rendering backend). Returns false when the URL is not valid, the fetch fails or the image cannot
    /// be decoded. The task completes in the task of the event loop that processes the response: the steps that follow run in
    /// it, so the event loop is not idle in between.
    /// </summary>
    private Task<bool> FetchImageAsync(string url, CancellationToken cancellationToken)
    {
        if (UrlParser.Parse(url, baseUrl: null) is not { } imageUrl)
            return Task.FromResult(false);

        var completion = new TaskCompletionSource<bool>();
        if (cancellationToken.CanBeCanceled)
        {
            cancellationToken.Register(() => completion.TrySetResult(false));
        }

        _window.QueueGlobalTask(() =>
        {
            var document = _window.Document;
            var request = new FetchRequest(imageUrl)
            {
                Client = document,
                Origin = document.Origin.Serialize(),
                Destination = RequestDestination.Image,
                Mode = RequestMode.NoCors,
                IsUserAgentRequest = true,
                CredentialsMode = FetchCredentialsMode.Omit,
                ServiceWorkersMode = ServiceWorkersMode.None,
            };
            Fetching.Fetch(
                request,
                response =>
                {
                    var internalResponse = response.Internal;
                    if (internalResponse.IsNetworkError || !HeaderList.IsOkStatus(internalResponse.Status) || internalResponse.Body is not { } body)
                    {
                        completion.TrySetResult(false);
                        return;
                    }

                    if (document.RenderingBackend is not { } backend)
                    {
                        completion.TrySetResult(true);
                        return;
                    }

                    try
                    {
                        // The image must be complete: a truncated or corrupted image cannot be shown
                        using var decoder = backend.CreateImageDecoder(body, mimeType: null);
                        using var image = decoder?.DecodeFrame(0, ExifOrientation.TopLeft, convertToSrgb: true);
                        completion.TrySetResult(image is not null && !decoder!.IsLastFrameIncomplete);
                    }
                    catch (Exception ex) when (ex is not OutOfMemoryException)
                    {
                        // A decoder of the backend may throw for corrupted data
                        completion.TrySetResult(false);
                    }
                },
                _window.QueueGlobalTask,
                cancellationToken);
        });

        return completion.Task;
    }

    /// <summary>
    /// Shows the transaction dialog (https://w3c.github.io/secure-payment-confirmation/#sctn-transaction-confirmation-ux)
    /// instead of the payment sheet, and runs the outcome of the choice of the user.
    /// </summary>
    private void ShowTransactionDialog(CancellationToken token)
    {
        if (_checkedSecurePaymentConfirmation is not { } data)
        {
            UserAbortsPaymentRequest();
            return;
        }

        var showOptOut = data.ShowOptOut is true;
        var prompt = new SecurePaymentConfirmationPrompt(_window, _details.Total.Amount, data.Instrument, data.PayeeName, data.PayeeOrigin, data.PaymentEntitiesLogos ?? [], data.RpId, data.CredentialIds.Count, showOptOut);
        UserAgentUI.Show(
            _window,
            (backend, cancellationToken) => backend.ShowSecurePaymentConfirmationAsync(prompt, cancellationToken),
            dismissed: SecurePaymentConfirmationChoice.Decline,
            choice =>
            {
                if (State != PaymentRequestState.Interactive || token.IsCancellationRequested)
                    return;

                switch (choice)
                {
                    case SecurePaymentConfirmationChoice.Accept:
                        RespondToSecurePaymentConfirmation(data, data.CredentialIds, token);
                        break;

                    // The user accepts the transaction without a credential: the response is a NotAllowedError
                    case SecurePaymentConfirmationChoice.AuthenticateAnotherWay:
                        RespondToSecurePaymentConfirmation(data, [], token);
                        break;

                    case SecurePaymentConfirmationChoice.OptOut when showOptOut:
                        Fail(CreateException("The user opted out of the process for the relying party", "OptOutError"));
                        break;

                    default:
                        UserAbortsPaymentRequest();
                        break;
                }
            },
            token);
    }

    /// <summary>
    /// The steps to respond to a payment request (https://w3c.github.io/secure-payment-confirmation/#sctn-steps-to-respond-to-a-payment-request):
    /// the payment is authenticated with one of the credentials, and the details of the response are the <see cref="PublicKeyCredential"/>.
    /// </summary>
    private void RespondToSecurePaymentConfirmation(SecurePaymentConfirmationRequest data, IReadOnlyList<ReadOnlyMemory<byte>> credentialIds, CancellationToken token)
    {
        if (credentialIds.Count == 0)
        {
            Fail(CreateException("The payment is not authenticated with a credential", "NotAllowedError"));
            return;
        }

        var document = _window.Document;
        var payment = new AuthenticationExtensionsPaymentInputs
        {
            IsPayment = true,
            RpId = data.RpId,
            TopOrigin = (document.BrowsingContext?.Top.Document ?? document).Origin.Serialize(),
            PayeeName = data.PayeeName,
            PayeeOrigin = data.PayeeOrigin,
            PaymentEntitiesLogos = data.PaymentEntitiesLogos is { Count: > 0 } logos ? logos : null,
            Total = _details.Total.Amount,
            Instrument = data.Instrument,
            BrowserBoundPubKeyCredParams = data.BrowserBoundPubKeyCredParams,
        };
        var extensions = data.Extensions is { } inputs
            ? new AuthenticationExtensionsClientInputs { Appid = inputs.Appid, AppidExclude = inputs.AppidExclude, CredProps = inputs.CredProps, Prf = inputs.Prf, LargeBlob = inputs.LargeBlob, RemoteClientDataJson = inputs.RemoteClientDataJson, Payment = payment }
            : new AuthenticationExtensionsClientInputs { Payment = payment };
        var options = new PublicKeyCredentialRequestOptions
        {
            Challenge = data.Challenge,
            Timeout = data.Timeout,
            RpId = data.RpId,
            UserVerification = "required",
            Extensions = extensions,
            AllowCredentials = [.. credentialIds.Select(id => new PublicKeyCredentialDescriptor { Type = "public-key", Id = id, Transports = ["internal"] })],
        };

        // The ceremony settles its task from a task of the event loop of the window
        _ = WebAuthenticationClient.DiscoverForSecurePaymentConfirmation(_window, options, data.BrowserBoundPubKeyCredParams ?? []).ContinueWith(
            task =>
            {
                if (State != PaymentRequestState.Interactive || token.IsCancellationRequested)
                    return;

                if (task.IsCompletedSuccessfully && task.Result is PublicKeyCredential credential)
                {
                    AcceptSecurePaymentConfirmation(credential);
                }
                else
                {
                    Fail(task.Exception?.InnerException ?? CreateException("The payment is not authenticated", "NotAllowedError"));
                }
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>https://w3c.github.io/payment-request/#user-accepts-the-payment-request-algorithm, with the credential of the authentication as details.</summary>
    private void AcceptSecurePaymentConfirmation(PublicKeyCredential credential)
    {
        var response = new PaymentResponse(this, _details.Id);
        Response = response;
        response.Accept(SecurePaymentConfirmationMethod, credential, shippingAddress: null, shippingOption: null, payerName: null, payerEmail: null, payerPhone: null);
        State = PaymentRequestState.Closed;
        DisposeUserInterface();
        _acceptPromise?.TrySetResult(response);
    }
}
