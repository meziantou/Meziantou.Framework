---
title: Overview
description: Add HTTP Basic authentication to an ASP.NET Core application and choose how the credentials are validated.
---

`Meziantou.AspNetCore.Authentication.HttpBasic` is an authentication handler for HTTP Basic: the client sends a
username and a password in the `Authorization` header of every request, and the handler turns them into a
`ClaimsPrincipal`.

```bash
dotnet add package Meziantou.AspNetCore.Authentication.HttpBasic
```

The types are in the [Meziantou.AspNetCore.Authentication.HttpBasic](cref:Meziantou.AspNetCore.Authentication.HttpBasic) namespace.

> [!WARNING]
> HTTP Basic sends the password on every request, encoded with Base64, which is not encryption. Serve these endpoints
> over HTTPS only. A leaked Basic credential is the password itself, and it can be replayed until the password changes.

## Protect an endpoint

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

A request without credentials, or with wrong ones, gets `401 Unauthorized` with a
`WWW-Authenticate: Basic realm="My application", charset="UTF-8"` header, so a browser shows its sign-in prompt.

## Two ways to validate credentials

- [Validating credentials](02-ValidatingCredentials.md): [AddHttpBasic](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationExtensions.AddHttpBasic) calls your delegate with the username and
  the password. Use it for a fixed account, a configuration file, or credentials stored in your own database.
- [ASP.NET Core Identity](03-AspNetCoreIdentity.md): [AddHttpBasicIdentity\<TUser\>](cref:Meziantou.AspNetCore.Authentication.HttpBasic.HttpBasicAuthenticationIdentityExtensions.AddHttpBasicIdentity) checks the password with Identity,
  with its lockout and two-factor rules.

## When to use HTTP Basic

HTTP Basic fits clients that cannot run an interactive sign-in: scripts, build agents, monitoring probes, devices, or a
private feed consumed by a package manager. For users in a browser, a cookie-based sign-in is safer: the password is
sent once instead of on every request.

Before exposing an endpoint, read [Securing a Basic endpoint](04-Security.md): rate limiting, account lockout and the
limits of the handler.
