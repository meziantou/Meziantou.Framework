---
title: ASP.NET Core Identity
description: Check HTTP Basic credentials against ASP.NET Core Identity users, with its lockout, confirmation and two-factor rules.
---

`AddHttpBasicIdentity<TUser>` validates the username and the password with the `SignInManager<TUser>` of ASP.NET Core
Identity:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/HttpBasicExamples.cs;region=HttpBasic_IdentityIntegration;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddIdentityCore<IdentityUser>()
                .AddSignInManager();

builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options =>
    {
        options.Realm = "My application";
    });
```
<!-- endref -->

Identity validates the credentials, so `options.ValidateCredentials` must not be set for this scheme. Setting it, in
the callback or in a later `Configure` or `PostConfigure` call, throws an `OptionsValidationException` at startup
instead of replacing the password check. To validate credentials yourself, use
[`AddHttpBasic`](02-ValidatingCredentials.md).

To restrict which accounts can access a resource, use authorization policies (a fallback policy applies to every
endpoint).

## Rules that still apply

Identity's sign-in checks apply to Basic requests: locked-out accounts are rejected, and so are accounts that do not
meet the confirmation requirements of `IdentityOptions.SignIn` (`RequireConfirmedAccount`, `RequireConfirmedEmail`,
`RequireConfirmedPhoneNumber`).

## Account lockout

`AddHttpBasicIdentity<TUser>` does not record failed attempts by default, so Identity's lockout never triggers,
whatever `IdentityOptions.Lockout` says. Pass `lockoutOnFailure: true` to record them:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/HttpBasicExamples.cs;region=HttpBasic_LockoutOnFailure;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options => options.Realm = "My application", lockoutOnFailure: true);
```
<!-- endref -->

The default is `false` because an endpoint without an interactive step lets anyone lock an account out on purpose by
sending wrong passwords. Neither choice is safe alone: `lockoutOnFailure: true` limits the guesses per account, and
rate limiting limits the guesses per caller. Use at least one of them; see [Securing a Basic endpoint](04-Security.md).

## Two-factor authentication

Accounts that require two-factor authentication are rejected, even with the right password: a Basic header carries
only a username and a password, so it cannot provide the second factor, and accepting the password alone would bypass
it.

An account requires two-factor authentication when `SignInManager<TUser>.IsTwoFactorEnabledAsync` returns `true`, the
same check as `PasswordSignInAsync`: the user store implements `IUserTwoFactorStore<TUser>`, two-factor authentication
is enabled for the user, and at least one two-factor provider can produce a token for them. A remembered two-factor
client (the `Identity.TwoFactorRememberMe` cookie) does not exempt the account.

The client gets a plain `401 Unauthorized`, as for a wrong password, so the response does not reveal that the password
was right. The rejection does not count as a failed attempt for lockout. Give the scripts and services that act for
these accounts their own credentials, such as scoped and revocable API keys validated by `AddHttpBasic` or by another
scheme.

If you accept the risk, `AllowTwoFactorEnabledAccounts` authenticates these accounts with their password alone:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/HttpBasicExamples.cs;region=HttpBasic_AllowTwoFactorEnabledAccounts;format=md-fenced-code-block;dedent=true -->
```csharp
builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options => options.AllowTwoFactorEnabledAccounts = true);
```
<!-- endref -->

> [!WARNING]
> With `AllowTwoFactorEnabledAccounts`, anyone who knows the password of a two-factor account can use the endpoint
> without the second factor. Only enable it when the resources behind the Basic scheme do not need that protection, or
> when an authorization policy requires evidence of multi-factor authentication by other means.

## Authentication type

The principal is created by `SignInManager<TUser>.CreateUserPrincipalAsync`, so `User.Identity.AuthenticationType` is
Identity's `"Identity.Application"`, not the name of the Basic scheme:

<!-- ref:../../../tests/Meziantou.AspNetCore.Authentication.HttpBasic.Tests/HttpBasicAuthenticationTests.cs;region=HttpBasicIdentity_AuthenticationType;format=md-fenced-code-block;dedent=true -->
```csharp
app.MapGet("/", (ClaimsPrincipal user) => user.Identity?.AuthenticationType);
// => "Identity.Application"
```
<!-- endref -->

Authorization policies use the authentication scheme, so `RequireAuthorization` and `AddAuthenticationSchemes` work as
expected. Only code that branches on `AuthenticationType` is affected, such as audit logs or an application that mixes
cookie and Basic authentication: it would attribute these requests to the cookie scheme. Use the scheme of the
authentication ticket to tell them apart.
