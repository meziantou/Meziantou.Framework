---
title: Validating credentials
description: Validate the username and password with your own delegate, configure the realm and the size limit, and register several schemes.
---

[AddHttpBasic](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationExtensions.AddHttpBasic) calls `options.ValidateCredentials` with the `HttpContext`, the username and the password. Return a
`ClaimsPrincipal` to authenticate the request, or `null` to reject it:

<!-- ref:../../../samples/Meziantou.AspNetCore.Samples/HttpBasicExamples.cs;region=HttpBasic_Usage;format=md-fenced-code-block;dedent=true -->
```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasic(options =>
    {
        options.Realm = "My application";
        options.MaxCredentialLength = 4096;
        options.ValidateCredentials = (context, username, password) =>
        {
            if (!string.Equals(username, "admin", StringComparison.Ordinal) ||
                !string.Equals(password, "secret", StringComparison.Ordinal))
            {
                return ValueTask.FromResult<ClaimsPrincipal?>(null);
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.Name, username),
                new Claim(ClaimTypes.NameIdentifier, username),
            };
            var identity = new ClaimsIdentity(claims, authenticationType: HttpBasicAuthenticationDefaults.AuthenticationScheme);
            return ValueTask.FromResult<ClaimsPrincipal?>(new ClaimsPrincipal(identity));
        };
    });

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/", (ClaimsPrincipal user) => $"Hello {user.Identity?.Name}!")
    .RequireAuthorization();

app.Run();
```
<!-- endref -->

The delegate is an [HttpBasicCredentialValidator](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicCredentialValidator) and returns a `ValueTask<ClaimsPrincipal?>`, so it can query a
database or a remote service. Use `context.RequestServices` to resolve the services it needs.

When [ValidateCredentials](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.ValidateCredentials) is not set, every request is rejected.

## Build the principal

Give the identity an authentication type, as in the example above: a `ClaimsIdentity` without one reports
`IsAuthenticated == false`, and authorization fails even though the password was right. The `ClaimTypes.Name` claim
becomes `User.Identity.Name`.

## What reaches the delegate

The handler parses the header before calling your code:

- Only the `Basic` scheme is read (in any case). A request without an `Authorization` header, or with another scheme
  such as `Bearer`, is left to the other authentication handlers.
- The credentials are decoded as UTF-8; a payload that is not valid Base64 or UTF-8 is rejected.
- The username ends at the first `:`, so a password can contain `:`. An empty username is passed to your delegate: it
  is up to you to accept it or not.
- Credentials that contain a control character (U+0000 to U+001F, or U+007F), in the username or the password, are
  rejected before your delegate is called, as RFC 7617 requires. Other characters are passed unchanged, including C1
  controls (U+0080 to U+009F) and line or paragraph separators (U+2028, U+2029).

The username is untrusted input: escape it before you write it to a log.

## Options

| Option | Default | Description |
| --- | --- | --- |
| [Realm](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.Realm) | `Restricted` | The `realm` of the `WWW-Authenticate` header. `null` omits it. Only printable ASCII (U+0020 to U+007E) is allowed: other characters throw an `ArgumentException` when the option is set |
| [MaxCredentialLength](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.MaxCredentialLength) | `4096` ([HttpBasicAuthenticationOptions.DefaultMaxCredentialLength](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.DefaultMaxCredentialLength)) | The maximum length, in characters, of the Base64 payload. Longer payloads are rejected before being decoded |
| [ValidateCredentials](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.ValidateCredentials) | Rejects everything | Your validation delegate |
| [AllowTwoFactorEnabledAccounts](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationOptions.AllowTwoFactorEnabledAccounts) | `false` | Only used by the [Identity integration](03-AspNetCoreIdentity.md#two-factor-authentication) |

The options derive from `AuthenticationSchemeOptions`, so the usual options of ASP.NET Core authentication handlers
(`Events`, `ForwardDefault`, ...) are available too.

## Several schemes

The default scheme name is [HttpBasicAuthenticationDefaults.AuthenticationScheme](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationDefaults.AuthenticationScheme) (`"Basic"`). The overloads
`AddHttpBasic(authenticationScheme, configureOptions)` and `AddHttpBasic(authenticationScheme, displayName,
configureOptions)` register more schemes, for instance one per kind of client, each with its own delegate and realm.
Select a scheme for an endpoint with an authorization policy (`AddAuthenticationSchemes`).
