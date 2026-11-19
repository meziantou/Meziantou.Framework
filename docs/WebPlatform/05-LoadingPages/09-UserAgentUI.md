---
title: User agent UI
description: Answer the prompts that the web APIs show to the user: the credential chooser, the FedCM account chooser, the payment sheet and the wallet of the digital credentials.
---

Some web APIs show a prompt of the browser and wait for the user: a browser asks which saved password to use, or whether
to save a new one. A browsing context has no user, so these APIs call a
[UserAgentUIBackend](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend), set with
[BrowsingContextOptions.UserAgentUI](cref:Meziantou.Framework.WebPlatform.Dom.BrowsingContextOptions.UserAgentUI). The
library implements the algorithms of the APIs (the validation of the arguments, the permissions policies, the errors, the
events) and calls the backend only where a browser shows something to the user.

Each kind of prompt is a virtual method of the backend. It receives a description of the prompt (a
[UserAgentPrompt](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentPrompt): the window that shows it, the origin of its
document and what the user is asked) and returns the answer of the user asynchronously. Without a backend, and for every
method that a backend does not override, the prompt behaves as if the user dismissed it.

| Method | API | Default answer |
|---|---|---|
| [ChooseCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ChooseCredentialAsync) | `navigator.credentials.get()` with `password` or `federated` | No credential: the promise resolves with `null` |
| [StoreCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.StoreCredentialAsync) | `navigator.credentials.store()` of a `PasswordCredential` or a `FederatedCredential` | The credential is not saved (the page is not told) |
| [RequestDigitalCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.RequestDigitalCredentialAsync) | `navigator.credentials.get({ digital })` and `navigator.credentials.create({ digital })` | The user cancels: the promise is rejected with a `NotAllowedError` |
| [HasPaymentHandlerAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.HasPaymentHandlerAsync) | `PaymentRequest.canMakePayment()` and `PaymentRequest.show()` | No payment method is supported: `canMakePayment()` resolves with `false`, `show()` rejects with a `NotSupportedError` |
| [ValidatePaymentMethodData](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ValidatePaymentMethodData) | The `PaymentRequest` constructor, for each payment method that has data | Any data is accepted |
| [ShowPaymentSheetAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ShowPaymentSheetAsync) | `PaymentRequest.show()` | The user dismisses the payment sheet: the promise rejects with an `AbortError` |
| [RetryPaymentAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.RetryPaymentAsync) | `PaymentResponse.retry()` | The user dismisses the payment sheet: the promise rejects with an `AbortError` |
| [CompletePaymentAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.CompletePaymentAsync) | `PaymentResponse.complete()` | The payment sheet closes at once |
| [IngestPaymentMethodManifestAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.IngestPaymentMethodManifestAsync) | `PaymentRequest.canMakePayment()` and `PaymentRequest.show()` with a URL-based payment method identifier | The payment method manifest is not fetched |
| [ChooseIdentityAccountAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ChooseIdentityAccountAsync) | `navigator.credentials.get()` with `identity` (FedCM): the account chooser | No account: the promise is rejected with a `NetworkError` |
| [ConfirmIdentityProviderLoginAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ConfirmIdentityProviderLoginAsync) | FedCM: sign in to an identity provider that returned no account | The user does not sign in: a `NetworkError` |
| [ShowIdentityCredentialErrorAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ShowIdentityCredentialErrorAsync) | FedCM: the identity provider did not mint a token | Closed at once: the promise is rejected with the `IdentityCredentialError` |
| [ShowIdentityAutoReauthenticationAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ShowIdentityAutoReauthenticationAsync) | FedCM: the returning account is signed in without the account chooser | The reauthentication continues |

The methods are called on the thread of the event loop. The task they return can complete later, on any thread: the
library settles the promise of the page on its event loop, and the event loop is not idle while a prompt is open. The
cancellation token is canceled when the page no longer waits for the answer (for instance, the signal of the request is
aborted): close the prompt then. An exception thrown by a method is handled as a dismissal of the prompt.

## Credentials

The password and federated credentials of the [Credential Management API](https://w3c.github.io/webappsec-credential-management/)
(`PasswordCredential` and `FederatedCredential`) live in the credential store of the browser, which is the one of the
backend. `navigator.credentials.store()` calls
[StoreCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.StoreCredentialAsync), and
`navigator.credentials.get({ password: true })` calls
[ChooseCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ChooseCredentialAsync), which returns the
credential that the user chooses. A backend that saves the credentials without asking, and returns the first one that
matches the request:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_UserAgentUIBackend;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// A credential store of the user agent: <c>navigator.credentials.store()</c> saves the credentials without asking, and
/// <c>navigator.credentials.get()</c> returns the first stored credential that matches the request.
/// </summary>
internal sealed class InMemoryCredentialStore : UserAgentUIBackend
{
    private readonly List<Credential> _credentials = [];

    public int ChooserPrompts { get; private set; }

    public CredentialChooserPrompt? LastPrompt { get; private set; }

    public override ValueTask<bool> StoreCredentialAsync(CredentialStorePrompt prompt, CancellationToken cancellationToken)
    {
        _credentials.Add(prompt.Credential);
        return ValueTask.FromResult(true);
    }

    public override ValueTask<Credential?> ChooseCredentialAsync(CredentialChooserPrompt prompt, CancellationToken cancellationToken)
    {
        ChooserPrompts++;
        LastPrompt = prompt;
        return ValueTask.FromResult(_credentials.Find(prompt.Matches));
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_UserAgentUI_Credentials;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new BrowsingContextOptions
{
    // The prompts of the user agent: the credential store and the credential chooser of the backend
    UserAgentUI = new InMemoryCredentialStore(),
};
```
<!-- endref -->

[CredentialChooserPrompt](cref:Meziantou.Framework.WebPlatform.Dom.CredentialChooserPrompt) describes the request: the
types of credentials (`Password`, and the providers and protocols of `Federated`), the mediation, and whether the origin
requires user mediation (`RequiresUserMediation`, set by `navigator.credentials.preventSilentAccess()`: the backend must
not return a credential without asking the user). A request whose mediation is `silent` must be answered without showing
anything; when the origin requires user mediation, it resolves with `null` without calling the backend.
[Matches](cref:Meziantou.Framework.WebPlatform.Dom.CredentialChooserPrompt.Matches) tells whether a stored credential
answers the request, and a returned credential that does not match it is ignored. The `Origin` of a `PasswordCredential` or
a `FederatedCredential` is the site where it is used.

The library checks the arguments and the algorithms of the specification: the password and federated credentials can be
requested together but not with the other types (a `NotSupportedError`), they cannot be requested or stored in a frame that
is not same origin with its ancestors (a `NotAllowedError`), nor from an opaque origin (a `SecurityError`), and they do not
support the `conditional` mediation. `navigator.credentials.create()` creates a credential from its data or from the fields
of a form (their autofill field names: `username`, `current-password`, `new-password`, `name`, `nickname` and `photo`),
without storing it. As in the specification, the `origin` member of the data of a credential is required; the `origin` of
a `FederatedCredential` created by `navigator.credentials.create()` is the origin of the document. The public key
credentials of Web Authentication do not use the backend: see [Web Authentication](08-PermissionsAndDevices.md#web-authentication).

## Digital credentials

The [Digital Credentials API](https://w3c-fedid.github.io/digital-credentials/) asks a wallet to present a credential
(`navigator.credentials.get({ digital })`, for instance an identity document) or to store a new one
(`navigator.credentials.create({ digital })`). The library filters the requests (the protocols that the user agent does not
allow are skipped: `openid4vp-v1-unsigned`, `openid4vp-v1-signed`, `openid4vp-v1-multisigned` and `org-iso-mdoc` for the
presentations, `openid4vci-v1` for the issuance, which `DigitalCredential.userAgentAllowsProtocol()` reports), checks the
permissions policies (`digital-credentials-get`, `digital-credentials-create`), consumes the transient activation that
both methods require, rejects a request while another one of the window is pending, serializes the data of the requests to
JSON, then calls [RequestDigitalCredentialAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.RequestDigitalCredentialAsync),
the digital credential chooser. The backend returns the response of the wallet: the protocol of the request it answered,
and its response as a JSON object, which becomes the `data` of the `DigitalCredential`:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_DigitalCredentialWallet;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>A wallet that answers the first request of a digital credential request with the request itself.</summary>
internal sealed class SingleResponseWallet : UserAgentUIBackend
{
    public DigitalCredentialPrompt? LastPrompt { get; private set; }

    public override ValueTask<DigitalCredentialResponse?> RequestDigitalCredentialAsync(DigitalCredentialPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        var request = prompt.Requests[0];
        var response = $$"""{"protocol":"{{request.Protocol}}","request":{{request.Data}}}""";
        return ValueTask.FromResult<DigitalCredentialResponse?>(new DigitalCredentialResponse(request.Protocol, response));
    }
}
```
<!-- endref -->

[DigitalCredentialPrompt](cref:Meziantou.Framework.WebPlatform.Dom.DigitalCredentialPrompt) gives the kind of request
(a presentation or an issuance) and the requests, in the order of the page. Returning null rejects the promise with a
`NotAllowedError`, as when the user cancels. The request is aborted, and the cancellation token canceled, when its signal
is aborted or its document is destroyed.

## Payments

The [Payment Request API](https://w3c.github.io/payment-request/) lets a page ask the browser to pay: the page lists the
payment methods it accepts and the details of the payment (`new PaymentRequest(methods, details, options)`), and
`request.show()` opens the payment sheet of the browser. The payment handlers and the payment sheet are the ones of the
backend:

- [HasPaymentHandlerAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.HasPaymentHandlerAsync) tells
  whether the browser can pay with a payment method (its identifier, such as `https://pay.example/pay`, and its data as
  JSON). `canMakePayment()` resolves with `true` when one method is supported; `show()` offers the supported methods and
  rejects with a `NotSupportedError` when there is none.
- [ShowPaymentSheetAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ShowPaymentSheetAsync) shows the
  sheet and returns the [PaymentSheetResponse](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetResponse) of the user
  who accepts the payment (the selected method, the response of the payment handler as JSON, the payer details), or
  `null` when the user dismisses the sheet.
- [RetryPaymentAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.RetryPaymentAsync) shows the sheet again
  when the page calls `response.retry(errors)`, and
  [CompletePaymentAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.CompletePaymentAsync) closes it when
  the page calls `response.complete(result)`.

A payment sheet that accepts the payment with the first supported method:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_UserAgentUIBackend_Payments;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>
/// A payment sheet whose user accepts the payment with the first supported payment method, once the page updated the
/// details: the user agent has a payment handler for https://pay.example/pay.
/// </summary>
internal sealed class AcceptingPaymentSheet : UserAgentUIBackend
{
    public PaymentSheetPrompt? LastPrompt { get; private set; }

    public PaymentComplete? Result { get; private set; }

    public override ValueTask<bool> HasPaymentHandlerAsync(PaymentHandlerQuery query, CancellationToken cancellationToken)
        => ValueTask.FromResult(query.Method.Identifier == "https://pay.example/pay");

    public override async ValueTask<PaymentSheetResponse?> ShowPaymentSheetAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
    {
        LastPrompt = prompt;
        var details = await prompt.WaitForUpdateAsync(cancellationToken);
        return new PaymentSheetResponse
        {
            MethodName = prompt.Methods[0].Identifier,
            Details = $$"""{"token": "tok_{{details.Total.Amount.Value}}"}""",
            PayerName = "John Doe",
        };
    }

    // The user fixes the errors shown by the sheet (prompt.Errors)
    public override ValueTask<PaymentSheetResponse?> RetryPaymentAsync(PaymentSheetPrompt prompt, CancellationToken cancellationToken)
        => ValueTask.FromResult<PaymentSheetResponse?>(new PaymentSheetResponse { MethodName = prompt.Response!.MethodName, PayerName = "Jane Doe" });

    public override ValueTask CompletePaymentAsync(PaymentSheetPrompt prompt, PaymentComplete result, string serializedData, CancellationToken cancellationToken)
    {
        Result = result;
        return ValueTask.CompletedTask;
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_UserAgentUI_Payments;format=md-fenced-code-block;dedent=true -->
```csharp
var paymentSheet = new AcceptingPaymentSheet();
var options = new BrowsingContextOptions
{
    // The payment handlers and the payment sheet of the Payment Request API are the ones of the backend
    UserAgentUI = paymentSheet,
};
```
<!-- endref -->

The [PaymentSheetPrompt](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt) describes the request: the
supported methods (`Methods`), the information to collect (`Options`: payer name, email and phone, shipping address) and
the current details (`Details`: the total, the line items, the shipping options and the modifiers, whose amounts are
validated and whose currency codes are in upper case). The same prompt is given to the three methods of a request.

While the sheet is shown, the backend reports what the user changes, and the page can update the details:

- [ChangeShippingAddressAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt.ChangeShippingAddressAsync)
  fires `shippingaddresschange`: the page sees the address without the recipient, the organization, the phone number and
  the address lines, and usually updates the shipping options for it.
- [ChangeShippingOptionAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt.ChangeShippingOptionAsync)
  fires `shippingoptionchange`.
- [ChangePaymentMethodAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt.ChangePaymentMethodAsync) fires
  `paymentmethodchange` (for example with the billing address of a card, to compute the taxes).
- [ChangePayerDetailsAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt.ChangePayerDetailsAsync) fires
  `payerdetailchange` at the `PaymentResponse` while a payment is retried.

Each method completes with the details once the page updated them (`event.updateWith(promise)`), or at once when the page
does not update them. [WaitForUpdateAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentSheetPrompt.WaitForUpdateAsync)
waits for the promise given to `show(detailsPromise)`; a payment accepted during an update is accepted once the update
completes. `Errors` holds the errors to show to the user (`error`, `shippingAddressErrors` and `payerErrors` of the last
update, or the errors given to `retry()`). An update whose promise rejects, or whose details are not valid, aborts the
request, and the cancellation token of the sheet is canceled.

The library implements the rest of the specification: the validation of the payment method identifiers (a standardized
identifier such as `secure-payment-confirmation`, or an `https:` URL), of the amounts and of the shipping options, the
`payment` permissions policy (allowed in the same-origin frames, and in the frames whose `allow` attribute lists
`payment`), the transient activation that `show()` consumes, the single payment sheet of a tab (a second `show()` rejects
with an `AbortError`), `abort()`, and the closing of the sheet when its document is unloaded.
[ValidatePaymentMethodData](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ValidatePaymentMethodData) checks
the data of the payment methods that define a type for it, when the request is constructed.

The owner of a URL-based payment method describes it with a
[payment method manifest](https://w3c.github.io/payment-method-manifest/): the web app manifests of its default payment
applications, and the origins whose payment handlers can handle it. When a page asks whether it can pay with the method,
[IngestPaymentMethodManifestAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.IngestPaymentMethodManifestAsync)
receives the identifier. As fetching the manifest tells the owner of the method that the user visits the page, the default
implementation fetches nothing. A backend that installs payment handlers calls
[FetchAsync](cref:Meziantou.Framework.WebPlatform.Dom.PaymentMethodManifestRequest.FetchAsync), which sends a `HEAD`
request to the identifier, follows its `Link: <...>; rel="payment-method-manifest"` header, fetches and validates the
manifest ([PaymentMethodManifest](cref:Meziantou.Framework.WebPlatform.Dom.PaymentMethodManifest)), then fetches the web
app manifests of its default applications. The requests carry no credentials, and the manifests cannot be redirected.

The service workers of a site can declare a payment handler of the
[Payment Handler API](https://w3c.github.io/web-based-payment-handler/): `registration.paymentManager` records its
`userHint` and the data it provides (`enableDelegations()`), and the scripts of the service workers can create
`CanMakePaymentEvent` and `PaymentRequestEvent` objects and listen to the `canmakepayment` and `paymentrequest` events.
The library does not invoke the payment handlers of the service workers: the user agent never fires these events, the
payment applications of the payment method manifests are not installed, and an event created by a script cannot answer a
payment (`respondWith()` throws an `InvalidStateError`). The payment handlers of the user agent are the ones of the backend
(`HasPaymentHandlerAsync`).

## Federated Credential Management

[Federated Credential Management](https://w3c-fedid.github.io/FedCM/) (FedCM) lets a site sign the user in with an
account of an identity provider: `navigator.credentials.get({ identity: { providers: [{ configURL, clientId }] } })`
returns an [IdentityCredential](cref:Meziantou.Framework.WebPlatform.Dom.IdentityCredential) whose `token` the provider
minted for the site. The library implements the requests of the browser to the provider: its well-known file and config
file, its accounts (with the cookies of the provider), the client metadata of the site, and the token of the chosen
account. The backend shows the dialogs. A backend whose account chooser chooses the first account:

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_FedCmBackend;format=md-fenced-code-block;dedent=true -->
```csharp
/// <summary>The dialogs of FedCM: the account chooser chooses the first account, and the other dialogs are recorded.</summary>
internal sealed class FirstAccountChooser : UserAgentUIBackend
{
    public List<IdentityCredentialPrompt> Prompts { get; } = [];

    public override ValueTask<IdentityAccountChoice?> ChooseIdentityAccountAsync(IdentityAccountChooserPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.FromResult<IdentityAccountChoice?>(IdentityAccountChoice.Select(prompt.Accounts[0]));
    }

    public override ValueTask ShowIdentityCredentialErrorAsync(IdentityCredentialErrorPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.CompletedTask;
    }

    public override ValueTask<bool> ShowIdentityAutoReauthenticationAsync(IdentityAutoReauthenticationPrompt prompt, CancellationToken cancellationToken)
    {
        Prompts.Add(prompt);
        return ValueTask.FromResult(true);
    }
}
```
<!-- endref -->

<!-- ref:../../../tests/Meziantou.Framework.WebPlatform.Tests/JavaScript/HostDeviceApisTests.cs;region=WebPlatform_UserAgentUI_FedCm;format=md-fenced-code-block;dedent=true -->
```csharp
var options = new BrowsingContextOptions
{
    // The dialogs of FedCM: the account chooser of the backend chooses the first account
    UserAgentUI = new FirstAccountChooser(),
};
```
<!-- endref -->

[IdentityAccountChooserPrompt](cref:Meziantou.Framework.WebPlatform.Dom.IdentityAccountChooserPrompt) lists the
accounts of the providers (an [IdentityProviderAccount](cref:Meziantou.Framework.WebPlatform.Dom.IdentityProviderAccount)
has the name, the email, the picture and the provider of the account, whether it is connected to the site, and the
privacy policy and terms of service of the site for the user who signs up) and the context of the request (`signin`,
`signup`, `use` or `continue`), which the title of the chooser shows. The backend answers with
[IdentityAccountChoice.Select](cref:Meziantou.Framework.WebPlatform.Dom.IdentityAccountChoice.Select), with
[IdentityAccountChoice.UseOtherAccount](cref:Meziantou.Framework.WebPlatform.Dom.IdentityAccountChoice.UseOtherAccount)
when the user signs in to another account (a request to a single provider that supports it), or with `null` when the user
closes the chooser. A backend can refuse to show the chooser, for instance during a cooldown after the user closed it, by
returning `null` at once.

The library applies the rules of the specification and of the browsers:

- An account connected to the site (chosen before, and not disconnected by `IdentityCredential.disconnect()`) is
  reauthenticated without the chooser when it is the only returning account and the mediation is not `required`; the
  backend is told with [ShowIdentityAutoReauthenticationAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ShowIdentityAutoReauthenticationAsync).
  `navigator.credentials.preventSilentAccess()` turns it off until the user chooses an account again. A `silent` request
  only succeeds this way.
- The login status of a provider is set by `navigator.login.setStatus()` and by the `Set-Login` response header. The
  accounts of a provider whose user is logged out are not fetched. When a provider whose user is logged in returns no
  account, [ConfirmIdentityProviderLoginAsync](cref:Meziantou.Framework.WebPlatform.Dom.UserAgentUIBackend.ConfirmIdentityProviderLoginAsync)
  asks whether to sign in: the library opens the login page of the provider in a new browsing context, and waits until it
  calls `IdentityProvider.close()`. A `continue_on` response of the provider opens its continuation page the same way,
  which returns the token with `IdentityProvider.resolve()`.
- A request needs the `identity-credentials-get` permissions policy feature in a cross-origin frame, an `active` request
  needs a transient activation, and a page has one pending request at a time (an `active` request replaces a pending
  `passive` one). The library does not delay the rejection of a request that showed no dialog.
