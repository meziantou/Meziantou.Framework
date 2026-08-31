# Meziantou.AspNetCore.Authentication.HttpBasic

ASP.NET Core authentication handler for HTTP Basic authentication.

> [!WARNING]
> HTTP Basic sends the password on **every** request, encoded with Base64, which is reversible and not encryption. Serve these endpoints over HTTPS only. Unlike a session cookie, a leaked Basic credential is the password itself and stays replayable until it is changed.

Credential validation is delegate-based through `options.ValidateCredentials`, which returns a `ClaimsPrincipal` for valid credentials and `null` for invalid credentials.

You can also integrate with ASP.NET Core Identity using `AddHttpBasicIdentity<TUser>()`.

## Usage

```csharp
using Meziantou.AspNetCore.Authentication.HttpBasic;
using System.Security.Claims;

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

## ASP.NET Core Identity integration

```csharp
using Meziantou.AspNetCore.Authentication.HttpBasic;
using Microsoft.AspNetCore.Identity;

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

The principal is built by `SignInManager<TUser>.CreateUserPrincipalAsync`, so `User.Identity.AuthenticationType` is Identity's own `"Identity.Application"`, not the Basic scheme name:

```csharp
app.MapGet("/", (ClaimsPrincipal user) => user.Identity?.AuthenticationType);
// => "Identity.Application"
```

Authorization policies are keyed on the authentication *scheme*, so `RequireAuthorization` and `AddAuthenticationSchemes(...)` behave as expected. Only code that branches on `AuthenticationType` is affected — audit logging, "how did this user sign in" checks, or an application that mixes cookie and Basic authentication would attribute these requests to the cookie scheme. Use the scheme name from the authentication ticket if you need to tell them apart.

## Security options

- `MaxCredentialLength` limits the size (in characters) of the Base64 credential payload in the `Authorization` header. The limit is applied before the payload is decoded.

### Use HTTPS

Credentials travel in cleartext on every request. Do not expose a Basic endpoint over plain HTTP outside of loopback.

### Rate limit the endpoint

HTTP Basic is an easy brute-force target: there is no CSRF token, no session, and no interactive step, so an attacker can replay guesses as fast as the server answers. Put the endpoint behind [ASP.NET Core rate limiting](https://learn.microsoft.com/aspnet/core/performance/rate-limit).

This matters for throughput too. Credentials are revalidated from scratch on every request, and with ASP.NET Core Identity that means a full password hash each time — on the order of tens of milliseconds of CPU per request. A single client can consume a disproportionate amount of CPU.

### Account lockout with ASP.NET Core Identity

`AddHttpBasicIdentity<TUser>` does **not** record failed sign-in attempts by default, so Identity's lockout never triggers no matter how `IdentityOptions.Lockout` is configured. Pass `lockoutOnFailure: true` to opt in:

```csharp
builder.Services
    .AddAuthentication(HttpBasicAuthenticationDefaults.AuthenticationScheme)
    .AddHttpBasicIdentity<IdentityUser>(options => options.Realm = "My application", lockoutOnFailure: true);
```

The default is `false` because lockout on an endpoint with no interactive step lets a third party lock accounts out on purpose simply by sending bad passwords. Neither default is safe on its own: choose `lockoutOnFailure: true` to bound guessing per account, or keep `false` and rely on rate limiting to bound guessing per caller. Doing neither leaves an unthrottled password oracle.
