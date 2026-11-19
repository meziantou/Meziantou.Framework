---
title: User agent UI
description: Answer the prompts that the web APIs show to the user, such as the credential chooser of the Credential Management API.
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
